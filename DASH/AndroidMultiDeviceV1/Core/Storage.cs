using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AndroidMultiDeviceV1.Core;

public static class Storage
{
    public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AndroidMultiDeviceV1");
    public static string SettingsFile => Path.Combine(Folder, "settings.json");
    public static string VaultFile => Path.Combine(Folder, "account-vault.json");
    public static string LogFile => Path.Combine(Folder, "activity.log");
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private static readonly object WriteGate = new();

    public static Settings LoadSettings()
    {
        Directory.CreateDirectory(Folder);
        if (!File.Exists(SettingsFile)) return new Settings();
        string json=File.ReadAllText(SettingsFile);
        var settings=JsonSerializer.Deserialize<Settings>(json, JsonOptions) ?? new Settings();
        bool changed=DeviceIdentity.Normalize(settings);
        // One-time V3 migration: copy the old global lifecycle values to EACH
        // existing device. Never keep a shared mutable policy reference.
        using(var original=JsonDocument.Parse(json))
        {
            if(original.RootElement.TryGetProperty("Devices",out var oldDevices) &&
                oldDevices.ValueKind==JsonValueKind.Array)
            {
                var oldById=new Dictionary<Guid,bool>();
                foreach(var oldDevice in oldDevices.EnumerateArray())
                {
                    if(!oldDevice.TryGetProperty("DeviceId",out var idElement) ||
                       !Guid.TryParse(idElement.GetString(),out var id))continue;
                    oldById[id]=oldDevice.TryGetProperty("AccountLifecycle",out var policyElement) &&
                        policyElement.ValueKind==JsonValueKind.Object;
                }
                foreach(var device in settings.Devices)
                {
                    if(oldById.TryGetValue(device.DeviceId,out bool hadPolicy) && !hadPolicy)
                    {
                        device.AccountLifecycle=new AccountLifecyclePolicy
                        {
                            SessionLimitMinutes=Math.Clamp(settings.AccountSessionLimitMinutes,1,10080),
                            AutoRotateAccounts=settings.AutoRotateAccounts,
                            NeverReuseUsedAccounts=settings.NeverReuseUsedAccounts,
                            AutoPurgeUsedAccounts=settings.AutoPurgeUsedAccounts,
                            PurgeUsedAfterHours=Math.Clamp(settings.PurgeUsedAfterHours,0,8760)
                        };
                        changed=true;
                    }
                }
            }
        }
        if(changed)SaveSettings(settings);
        return settings;
    }
    public static void SaveSettings(Settings value)
    {
        Directory.CreateDirectory(Folder);
        DeviceIdentity.Normalize(value);
        WriteAtomic(SettingsFile, JsonSerializer.Serialize(value, JsonOptions));
    }
    public static void WriteAtomic(string target, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        lock(WriteGate)
        {
            string tmp = target + ".tmp";
            File.WriteAllText(tmp, contents, new UTF8Encoding(false));
            File.Move(tmp, target, true);
        }
    }
}

/// <summary>Encrypted on disk. Key derives from a user-chosen vault password; never logged or exported.</summary>
public sealed class AccountVault : IDisposable
{
    sealed class EncryptedBlob
    {
        public string Salt { get; set; } = "";
        public string Nonce { get; set; } = "";
        public string Cipher { get; set; } = "";
        public string Tag { get; set; } = "";
    }
    readonly byte[] _salt;
    readonly byte[] _key;
    readonly object _gate = new();
    List<ManagedAccount> _accounts;
    bool _disposed;
    public bool EnforceSingleUse { get; set; } = true; // legacy/test fallback
    // V4: policy is resolved by owner device. No global toggle races across workers.
    public Func<string,bool>? SingleUseForSerial { get; set; }
    bool SingleUse(string serial) => SingleUseForSerial?.Invoke(serial) ?? EnforceSingleUse;
    static bool IsRetired(ManagedAccount a) => a.State=="PURGED";
    static bool WasUsed(ManagedAccount a) => a.EverUsed || a.LastStart.HasValue;
    string Fingerprint(string value)
    {
        using var hmac=new HMACSHA256(_key);
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(value.Trim().ToLowerInvariant())));
    }
    static string CanonicalHandle(string handle) => TikTokStateDetector.NormalizeHandle(handle).ToLowerInvariant();
    public AccountVault(string vaultPassword)
    {
        if (string.IsNullOrWhiteSpace(vaultPassword) || vaultPassword.Length < 8)
            throw new ArgumentException("Mật khẩu kho phải có ít nhất 8 ký tự.");
        Directory.CreateDirectory(Storage.Folder);
        bool exists = File.Exists(Storage.VaultFile);
        EncryptedBlob? blob = exists ? JsonSerializer.Deserialize<EncryptedBlob>(File.ReadAllText(Storage.VaultFile)) : null;
        if (exists && blob is null) throw new InvalidDataException("Kho dữ liệu không hợp lệ.");
        _salt = blob is null ? RandomNumberGenerator.GetBytes(16) : Convert.FromBase64String(blob.Salt);
        _key = Rfc2898DeriveBytes.Pbkdf2(vaultPassword, _salt, 210_000, HashAlgorithmName.SHA256, 32);
        if (blob is null) { _accounts = []; Save(); }
        else
        {
            var nonce = Convert.FromBase64String(blob.Nonce);
            var cipher = Convert.FromBase64String(blob.Cipher);
            var tag = Convert.FromBase64String(blob.Tag);
            byte[] plain = new byte[cipher.Length];
            try { using var aes = new AesGcm(_key, 16); aes.Decrypt(nonce, cipher, tag, plain); }
            catch (CryptographicException) { CryptographicOperations.ZeroMemory(_key); throw new InvalidOperationException("Mật khẩu kho sai hoặc file kho bị hỏng."); }
            try
            {
                using var document=JsonDocument.Parse(plain);
                if(document.RootElement.ValueKind==JsonValueKind.Object)
                {
                    var pools=JsonSerializer.Deserialize<Dictionary<string,List<ManagedAccount>>>(plain,
                        Storage.JsonOptions)??[];
                    _accounts=[];
                    foreach(var pool in pools)
                    foreach(var account in pool.Value??[])
                    {
                        // The dictionary key is authoritative. DeviceSerial remains
                        // populated for compatibility with existing workflows.
                        account.DeviceSerial=pool.Key=="__unassigned__"?"":pool.Key;
                        _accounts.Add(account);
                    }
                }
                else
                {
                    // One-time migration from the legacy flat encrypted list.
                    _accounts=JsonSerializer.Deserialize<List<ManagedAccount>>(plain,Storage.JsonOptions)??[];
                }
            }
            finally { CryptographicOperations.ZeroMemory(plain); }
            // A process interruption is not the same as a security review. Its account
            // may be inspected on the next Start, but NEVER assumed to be logged out.
            foreach(var a in _accounts.Where(a=>!IsRetired(a) && a.LastStart.HasValue))
            {
                // Conservative upgrade: any prior attempt is never auto-reused in V3.
                a.EverUsed=true;
                if(a.LastStart is { } started)
                    a.FirstUsedAtUtc ??= started.ToUniversalTime();
            }
            foreach (var a in _accounts.Where(x => x.State is "RESERVED" or "ACTIVE"))
            {
                a.State="INTERRUPTED";
                a.ExplicitTechnicalRetryPending=false;
                a.LastEnd=DateTimeOffset.UtcNow;
                // The process died without a verified stop time. Existing
                // cumulative seconds remain but the unknown tail MUST be reviewed.
                a.RunDurationKnown=false;
                a.LegacyHistoryUnverified=true;
                a.CredentialPurgeDueAtUtc=null;
                a.LastResult="Ứng dụng ngắt đột ngột; xác nhận tổng phút đã chạy trước khi chạy lại";
            }
            // Migrate only EXPLICIT legacy UI/ADB failures. An old NEEDS_REVIEW
            // raised by a security prompt, identity mismatch, or logout ambiguity
            // stays under review; never blindly relabel all reviews as AVAILABLE.
            foreach (var a in _accounts.Where(x => x.State == "NEEDS_REVIEW" &&
                         IsLegacyTechnicalReview(x.LastResult)))
            {
                a.State = "RETRY_PENDING";
                a.TechnicalRetryCount = 0;
                a.NextTechnicalRetryAt = null;
            }
            Save();
        }
    }
    public List<ManagedAccount> Snapshot()
    {
        lock (_gate) return _accounts.Where(a=>!IsRetired(a)).Select(a => new ManagedAccount
        { Id=a.Id, IsSelectedForRun=a.IsSelectedForRun, Email=a.Email, DeviceSerial=a.DeviceSerial, State=a.State, ExpectedTikTokHandle=a.ExpectedTikTokHandle,
          LastStart=a.LastStart, LastEnd=a.LastEnd, LastResult=a.LastResult,
          PreviousVerifiedTikTokHandle=a.PreviousVerifiedTikTokHandle,
          VerifiedHandleChangedAt=a.VerifiedHandleChangedAt,
          TechnicalRetryCount=a.TechnicalRetryCount,
          NextTechnicalRetryAt=a.NextTechnicalRetryAt,
          EverUsed=a.EverUsed,FirstUsedAtUtc=a.FirstUsedAtUtc,
          SessionStartedAtUtc=a.SessionStartedAtUtc,SessionDeadlineUtc=a.SessionDeadlineUtc,
          CredentialPurgeDueAtUtc=a.CredentialPurgeDueAtUtc,RunDurationKnown=a.RunDurationKnown,LegacyHistoryUnverified=a.LegacyHistoryUnverified,
          AccumulatedRunSeconds=a.AccumulatedRunSeconds,SessionBudgetMinutes=a.SessionBudgetMinutes,
          LastDurationConfirmedAtUtc=a.LastDurationConfirmedAtUtc }).ToList();
    }
    public List<ManagedAccount> Snapshot(string deviceSerial)
    {
        if(string.IsNullOrWhiteSpace(deviceSerial))return [];
        lock(_gate)return SnapshotUnsafe(_accounts.Where(a=>!IsRetired(a) &&
            a.DeviceSerial.Equals(deviceSerial,StringComparison.OrdinalIgnoreCase)));
    }
    static List<ManagedAccount> SnapshotUnsafe(IEnumerable<ManagedAccount> source)=>source.Select(a=>new ManagedAccount
    { Id=a.Id,IsSelectedForRun=a.IsSelectedForRun,Email=a.Email,DeviceSerial=a.DeviceSerial,State=a.State,
      ExpectedTikTokHandle=a.ExpectedTikTokHandle,LastStart=a.LastStart,LastEnd=a.LastEnd,
      LastResult=a.LastResult,PreviousVerifiedTikTokHandle=a.PreviousVerifiedTikTokHandle,
      VerifiedHandleChangedAt=a.VerifiedHandleChangedAt,TechnicalRetryCount=a.TechnicalRetryCount,
      NextTechnicalRetryAt=a.NextTechnicalRetryAt,
      EverUsed=a.EverUsed,FirstUsedAtUtc=a.FirstUsedAtUtc,
      SessionStartedAtUtc=a.SessionStartedAtUtc,SessionDeadlineUtc=a.SessionDeadlineUtc,
      CredentialPurgeDueAtUtc=a.CredentialPurgeDueAtUtc,RunDurationKnown=a.RunDurationKnown,LegacyHistoryUnverified=a.LegacyHistoryUnverified,
          AccumulatedRunSeconds=a.AccumulatedRunSeconds,SessionBudgetMinutes=a.SessionBudgetMinutes,
          LastDurationConfirmedAtUtc=a.LastDurationConfirmedAtUtc }).ToList();

    /// <summary>
    /// Persist UI run selection in the encrypted account model. This state is
    /// scoped by account/device and therefore survives master-detail rebinding.
    /// </summary>
    public void SetRunSelection(Guid accountId,bool selected)
    {
        lock(_gate)
        {
            var account=_accounts.FirstOrDefault(a=>a.Id==accountId);
            if(account is null || account.IsSelectedForRun==selected)return;
            account.IsSelectedForRun=selected;
            Save();
        }
    }

    public void SetRunSelection(IEnumerable<Guid> accountIds,bool selected)
    {
        ArgumentNullException.ThrowIfNull(accountIds);
        lock(_gate)
        {
            var ids=accountIds.ToHashSet();
            bool changed=false;
            foreach(var account in _accounts.Where(a=>ids.Contains(a.Id)))
            {
                if(account.IsSelectedForRun==selected)continue;
                account.IsSelectedForRun=selected;
                changed=true;
            }
            if(changed)Save();
        }
    }
    /// <summary>
    /// Import username|email|password. Existing email/password and device assignment
    /// are preserved. For old vault records without a handle, import fills the handle
    /// only when no worker has RESERVED/ACTIVE that account. All updates are atomic.
    /// </summary>
    public (int added, int enriched, int duplicates, int invalid) ImportLines(
        string deviceSerial,IEnumerable<string> lines)
    {
        if(string.IsNullOrWhiteSpace(deviceSerial))
            throw new ArgumentException("Phải chọn thiết bị trước khi nhập Local Pool.",nameof(deviceSerial));
        lock (_gate)
        {
            int added=0, enriched=0, dup=0, bad=0;
            var byEmail=_accounts.Where(a=>!IsRetired(a)).ToDictionary(a=>a.Email,StringComparer.OrdinalIgnoreCase);
            var byHandle=new Dictionary<string,ManagedAccount>(StringComparer.OrdinalIgnoreCase);
            foreach(var existing in _accounts.Where(a=>!IsRetired(a)))
            {
                var handle=TikTokStateDetector.NormalizeHandle(existing.ExpectedTikTokHandle);
                if(TikTokStateDetector.IsValidHandle(handle))
                    byHandle.TryAdd(handle,existing);
            }
            // Validate first, then update the in-memory vault and save atomically.
            // No input line or password is ever written to the activity log.
            foreach(string raw in lines)
            {
                string line=raw.TrimEnd('\r','\n');
                if(string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))continue;
                // Password may itself contain |; only the first two pipes delimit fields.
                string[] fields=line.Split('|',3);
                if(fields.Length!=3){bad++;continue;}
                string username=TikTokStateDetector.NormalizeHandle(fields[0]);
                string email=fields[1].Trim();
                string password=fields[2]; // Preserve spaces exactly: passwords are case/space-sensitive.
                if(!TikTokStateDetector.IsValidHandle(username) ||
                   !IsValidEmail(email) || string.IsNullOrWhiteSpace(password) || password.Length>1000)
                {bad++;continue;}
                if(_accounts.Any(a=>IsRetired(a) &&
                      (a.EmailFingerprint==Fingerprint(email) ||
                       a.HandleFingerprint==Fingerprint(username))))
                {bad++;continue;} // An auto-purged account cannot enter the fresh pool again.
                if(byHandle.TryGetValue(username,out var owner) &&
                   !owner.Email.Equals(email,StringComparison.OrdinalIgnoreCase))
                {bad++;continue;} // Conflicting mapping between TikTok and Google accounts.
                if(byEmail.TryGetValue(email,out var existingAccount))
                {
                    if(!existingAccount.DeviceSerial.Equals(deviceSerial,StringComparison.OrdinalIgnoreCase))
                    {bad++;continue;} // Never move an account across local pools during import.
                    string old=TikTokStateDetector.NormalizeHandle(existingAccount.ExpectedTikTokHandle);
                    if(old.Length==0 && existingAccount.State is not ("ACTIVE" or "RESERVED"))
                    {
                        existingAccount.ExpectedTikTokHandle=username;
                        byHandle[username]=existingAccount;
                        enriched++;
                    }
                    else if(old.Length!=0 && old!=username) bad++;
                    else dup++;
                    continue;
                }
                var account=new ManagedAccount {
                    Email=email,Password=password,ExpectedTikTokHandle=username,
                    DeviceSerial=deviceSerial
                };
                _accounts.Add(account);
                byEmail[email]=account;
                byHandle[username]=account;
                added++;
            }
            if(added>0 || enriched>0)Save();
            return (added,enriched,dup,bad);
        }
    }
    static bool IsValidEmail(string email)
    {
        if(email.Length is < 3 or > 254 || email.Any(char.IsWhiteSpace))return false;
        int at=email.IndexOf('@');
        return at>0 && at==email.LastIndexOf('@') && at<email.Length-3 &&
               email[(at+1)..].Contains('.') && !email.Contains('|');
    }
    /// <summary>
    /// Permanently remove account records (including passwords and device assignment)
    /// from the encrypted vault. Deletion is all-or-nothing for the selected records.
    /// This does not delete a TikTok/Google account or log out an Android app.
    /// </summary>
    public int DeleteAccounts(IEnumerable<Guid> ids, Func<string,bool> isDeviceBusy)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(isDeviceBusy);
        lock(_gate)
        {
            var selectedIds=ids.ToHashSet();
            if(selectedIds.Count==0)return 0;
            var selected=_accounts.Where(a=>selectedIds.Contains(a.Id)).ToList();
            if(selected.Count==0)return 0;

            // A stale RESERVED/ACTIVE account is still unsafe to delete, even if
            // the UI has no worker. The operator must resolve the session first.
            if(selected.Any(a=>a.State is "RESERVED" or "ACTIVE"))
                throw new InvalidOperationException(
                    "Không thể xóa tài khoản ở trạng thái RESERVED/ACTIVE. Hãy hoàn tất phiên hoặc khôi phục trạng thái trước.");

            // Refuse deletion while any assigned device has a running worker or
            // an Appium diagnostic session. Other devices may continue running.
            if(selected.Any(a=>!string.IsNullOrWhiteSpace(a.DeviceSerial) && isDeviceBusy(a.DeviceSerial)))
                throw new InvalidOperationException(
                    "Không thể xóa: điện thoại được gán vẫn có worker hoặc phiên Appium đang hoạt động. Hãy Stop và chờ kết thúc hoàn toàn.");

            // Commit the encrypted file first; only replace the live list after
            // a successful write. Failed disk writes leave the in-memory vault
            // untouched. No plaintext account information goes into logs.
            var remaining=_accounts.Where(a=>!selectedIds.Contains(a.Id)).ToList();
            // Manual deletion also preserves minimal encrypted duplicate guards
            // for accounts that already ran, without retaining credentials.
            foreach(var used in selected.Where(WasUsed))
                remaining.Add(new ManagedAccount {
                    Id=used.Id,DeviceSerial=used.DeviceSerial,State="PURGED",
                    EmailFingerprint=Fingerprint(used.Email),
                    HandleFingerprint=Fingerprint(CanonicalHandle(used.ExpectedTikTokHandle)),
                    EverUsed=true,CredentialsPurgedAtUtc=DateTimeOffset.UtcNow
                });
            Save(remaining);
            _accounts=remaining;
            return selected.Count;
        }
    }

    /// <summary>
    /// Recognize only documented technical failures from older V1 versions.
    /// Credential errors, unidentified logged-in accounts, verification requests,
    /// and platform restrictions must NOT be migrated automatically.
    /// </summary>
    static bool IsLegacyTechnicalReview(string? result)
    {
        string value=(result ?? "").Trim();
        if(new[]{"account banned","account suspended","account restricted",
                 "tài khoản bị khóa","tài khoản đã bị đình chỉ","captcha",
                 "verify your identity","xác minh danh tính","bảo mật"}
           .Any(s=>value.Contains(s,StringComparison.OrdinalIgnoreCase)))return false;
        string[] knownTechnicalPrefixes = [
            "Ứng dụng hiện tại không phải TikTok",
            "Chưa quan sát thấy giao diện TikTok",
            "Không click được tab Profile",
            "Chưa xác minh được handle trên Profile",
            "Chưa xác minh được Profile",
            "TikTok chưa xuất hiện",
            "Không đọc được giao diện",
            "Phiên gián đoạn; cần xác minh trạng thái thực tế",
            "Ứng dụng đã ngắt trong phiên trước"
        ];
        return knownTechnicalPrefixes.Any(prefix =>
            value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    // V5.9: retry only a known technical UI/transport failure after a direct
    // human Start on this exact account. No automatic retry of used records.
    // Security/2SV, explicit identity mismatch, invalid and checkpoint are excluded.
    public static bool IsDocumentedTechnicalUiFailure(string? reason)
    {
        var text=(reason??"").Trim();
        if(text.Length==0)return false;
        string[] blocked=["GOOGLE_2SV", "2SV_POLICY", "captcha", "checkpoint",
            "security challenge", "verify your identity", "xác minh danh tính",
            "bảo mật", "bị khóa", "đình chỉ", "đăng nhập bị từ chối",
            "tài khoản không hợp lệ", "sai mật khẩu", "identity mismatch",
            "username không khớp", "đang đăng nhập tài khoản khác"];
        if(blocked.Any(s=>text.Contains(s,StringComparison.OrdinalIgnoreCase)))return false;
        if(IsLegacyTechnicalReview(text))return true;
        string[] known=[
            "Quá 3 phút vẫn chưa xác minh được đúng TikTok handle",
            "Google Choose an account", "không thấy/click được Add another account",
            "không tìm thấy Add another account", "không tìm thấy Use another account",
            "không đọc được cây Accessibility", "AppiumUiTreeUnavailableException",
            "UiAutomator2", "ADB (1)", "device offline",
            "Chưa hoàn tất TikTok Home/Profile/Login", "Cloud Phone mới chưa hoàn tất TikTok Terms",
            "Cập nhật hồ sơ gián đoạn kỹ thuật", "Cập nhật hồ sơ chưa hoàn tất",
            "Cập nhật hồ sơ dừng", "Profile chưa mở sau", "Không thể đọc UI",
            "chưa tìm thấy nút", "không tìm thấy nút", "không thấy màn hình nhập email",
            "chưa đến màn hình mật khẩu Google", "không nhập được Email",
            "Lỗi kỹ thuật chưa hoàn tất trong worker", "Lỗi kỹ thuật lặp lại 3 lần"
        ];
        return known.Any(s=>text.Contains(s,StringComparison.OrdinalIgnoreCase));
    }

    static bool IsExplicitTechnicalRetryEligible(ManagedAccount a) =>
        !IsRetired(a) && a.State is ("NEEDS_REVIEW" or "RETRY_PENDING") &&
        IsDocumentedTechnicalUiFailure(a.LastResult);

    public static bool IsRecoverableInterruption(ManagedAccount account)
    {
        if(account.State == "INTERRUPTED")return true;
        if(account.State == "RETRY_PENDING")
            return account.NextTechnicalRetryAt is null ||
                   account.NextTechnicalRetryAt <= DateTimeOffset.UtcNow;
        return account.State == "NEEDS_REVIEW" && IsLegacyTechnicalReview(account.LastResult);
    }

    ManagedAccount? FindNextCandidateUnsafe(string serial)
    {
        var pool=_accounts.Where(a=>a.DeviceSerial.Equals(serial,StringComparison.OrdinalIgnoreCase)).ToList();
        // A potentially still logged-in INTERRUPTED session is inspected first.
        // Use other AVAILABLE accounts before retrying failed technical steps,
        // avoiding tight loops on one broken UI / network connection.
        // V5.1: AUTO is always fresh-only, regardless of UI legacy toggle.
        return pool.FirstOrDefault(a=>a.State=="AVAILABLE" && !WasUsed(a));
    }

    /// <summary>
    /// Mark a transport/UI failure as RETRY_PENDING rather than permanently
    /// invalidating the Google/TikTok identity. Retry is bounded and always
    /// starts with identity reconciliation, not a blind login or logout.
    /// </summary>
    public void MarkTechnicalFailure(Guid id,string result)
    {
        lock(_gate)
        {
            var a=_accounts.FirstOrDefault(x=>x.Id==id);if(a is null)return;
            if(a.State is "RESTRICTED" or "DISABLED")return;
            SettleRunTimeUnsafe(a,DateTimeOffset.UtcNow);
            a.ExplicitTechnicalRetryPending=false;
            a.TechnicalRetryCount++;
            a.LastResult=result;
            a.LastEnd=DateTimeOffset.Now;
            if(a.TechnicalRetryCount>=3)
            {
                a.State="NEEDS_REVIEW";
                a.NextTechnicalRetryAt=null;
                a.LastResult="Lỗi kỹ thuật lặp lại 3 lần; kiểm tra cấu hình/UI rồi có thể Đặt lại. " + result;
            }
            else
            {
                a.State="RETRY_PENDING";
                int waitSeconds=a.TechnicalRetryCount==1?10:30;
                a.NextTechnicalRetryAt=DateTimeOffset.UtcNow.AddSeconds(waitSeconds);
            }
            Save();
        }
    }

    public ManagedAccount? PreviewNext(string serial)
    {
        lock(_gate)
        {
            var a=FindNextCandidateUnsafe(serial);
            if(a is null)return null;
            return new ManagedAccount { Id=a.Id, Email=a.Email, DeviceSerial=a.DeviceSerial,
                State=a.State, ExpectedTikTokHandle=a.ExpectedTikTokHandle, LastResult=a.LastResult,
                TechnicalRetryCount=a.TechnicalRetryCount,
                NextTechnicalRetryAt=a.NextTechnicalRetryAt };
        }
    }

    // Reserve ONE explicitly selected account. Never fall back to a different
    // account if selection is not eligible: a test must be deterministic.
    public ManagedAccount? PreviewSpecific(string serial, Guid accountId)
    {
        lock(_gate)
        {
            var a=_accounts.FirstOrDefault(x=>x.Id==accountId &&
                x.DeviceSerial.Equals(serial,StringComparison.OrdinalIgnoreCase));
            if(a is null || !(EligibleForReservation(a) || IsExplicitTechnicalRetryEligible(a)))return null;
            return new ManagedAccount { Id=a.Id, Email=a.Email, DeviceSerial=a.DeviceSerial,
                ExpectedTikTokHandle=a.ExpectedTikTokHandle, State=a.State, LastResult=a.LastResult };
        }
    }

    bool EligibleForReservation(ManagedAccount a) =>
        !IsRetired(a) && !WasUsed(a) && a.State=="AVAILABLE";

    public ManagedAccount? ReserveSpecific(string serial, Guid accountId)
    {
        lock(_gate)
        {
            var a=_accounts.FirstOrDefault(x=>x.Id==accountId &&
                x.DeviceSerial.Equals(serial,StringComparison.OrdinalIgnoreCase));
            if(a is null)return null;
            bool technicalRetry=IsExplicitTechnicalRetryEligible(a);
            if(!EligibleForReservation(a) && !technicalRetry)return null;
            bool recovering=IsRecoverableInterruption(a);
            a.State="RESERVED";
            a.LastStart=DateTimeOffset.Now;
            // Historical EverUsed/LastStart remain unchanged for AUTO exclusion.
            a.ExplicitTechnicalRetryPending=technicalRetry;
            a.LastResult=technicalRetry ? "MANUAL_TECHNICAL_UI_RETRY: tái xác minh đúng tài khoản; không AUTO" :
                recovering ? "Đang kiểm tra lại tài khoản được chọn sau gián đoạn" :
                "Đang mở phiên CHỈ tài khoản đã tick";
            Save();
            return new ManagedAccount { Id=a.Id, Email=a.Email, Password=a.Password,
                DeviceSerial=a.DeviceSerial, State=a.State, ExpectedTikTokHandle=a.ExpectedTikTokHandle };
        }
    }

    // Start LIVE is an explicit request to KEEP the currently signed-in account,
    // not a request to select an old account from the AUTO queue. No guessed
    // handle is accepted: only the strictly verified owner on this device.
    public enum LiveOwnerDecision { ContinueCurrent, RotateToFresh, Blocked }

    public LiveOwnerDecision AssessVerifiedLiveOwner(string serial,Guid id,int limitMinutes)
    {
        lock(_gate)
        {
            var a=_accounts.FirstOrDefault(x=>x.Id==id &&
                x.DeviceSerial.Equals(serial,StringComparison.OrdinalIgnoreCase));
            if(a is null || IsRetired(a) || a.State is "INVALID" or "RESTRICTED" or
               "DISABLED" or "ACCOUNT_CHECKPOINT" or "ACTIVE" or "RESERVED")
                return LiveOwnerDecision.Blocked;
            // A strict verified Profile is stronger evidence than an earlier
            // incomplete UI observation. Only TECHNICAL reviews can resume.
            if(a.State=="NEEDS_REVIEW" && !IsExplicitTechnicalRetryEligible(a))
                return LiveOwnerDecision.Blocked;
            if(a.State=="COMPLETED")return LiveOwnerDecision.RotateToFresh;
            if(a.State is not ("AVAILABLE" or "INTERRUPTED" or "RETRY_PENDING" or "NEEDS_REVIEW"))
                return LiveOwnerDecision.Blocked;
            if(WasUsed(a) && a.RunDurationKnown &&
               a.AccumulatedRunSeconds >= Math.Clamp(limitMinutes,1,10080)*60)
                return LiveOwnerDecision.RotateToFresh;
            return LiveOwnerDecision.ContinueCurrent;
        }
    }

    public ManagedAccount? ReserveVerifiedLiveOwner(string serial,Guid id,int limitMinutes)
    {
        lock(_gate)
        {
            var a=_accounts.FirstOrDefault(x=>x.Id==id &&
                x.DeviceSerial.Equals(serial,StringComparison.OrdinalIgnoreCase));
            if(AssessVerifiedLiveOwner(serial,id,limitMinutes)!=LiveOwnerDecision.ContinueCurrent)
                return null;
            // This is NOT a claim that the old account has never run. Previous
            // unmeasured time remains explicitly UNKNOWN. Only a new verified
            // tracking window has an accurate stopwatch from this point onward.
            if(WasUsed(a!) && !a!.RunDurationKnown)
            {
                a.LegacyHistoryUnverified=true;
                a.RunDurationKnown=true;
                a.LastResult="Start LIVE xác minh chủ tài khoản: lịch sử cũ chưa rõ; " +
                    "tính giới hạn mới từ lần xác minh này, không coi như chưa dùng";
            }
            bool wasUsed=WasUsed(a!);
            if(a!.State=="NEEDS_REVIEW")
                a.LastResult="Start LIVE: khôi phục sau lỗi UI kỹ thuật; Profile đã xác minh chính xác";
            a!.State="RESERVED";
            a.LastStart=DateTimeOffset.UtcNow;
            if(!wasUsed) a.LastResult="Start LIVE: tài khoản chưa dùng, đã xác minh chủ máy";
            Save();
            return new ManagedAccount {Id=a.Id,Email=a.Email,Password=a.Password,
                DeviceSerial=a.DeviceSerial,ExpectedTikTokHandle=a.ExpectedTikTokHandle,
                State=a.State,EverUsed=a.EverUsed,RunDurationKnown=a.RunDurationKnown,
                AccumulatedRunSeconds=a.AccumulatedRunSeconds,
                LegacyHistoryUnverified=a.LegacyHistoryUnverified};
        }
    }

    // Explicit manual resume only. The account must be in the selected phone's
    // encrypted pool; this method is NEVER used by ReserveNext/auto rotation.
    public ManagedAccount? PreviewManualResume(string serial,Guid id,int limitMinutes)
    {
        lock(_gate)
        {
            var a=_accounts.FirstOrDefault(x=>x.Id==id &&
                x.DeviceSerial.Equals(serial,StringComparison.OrdinalIgnoreCase));
            return IsManualResumeEligible(a,limitMinutes) ? SnapshotUnsafe([a!]).Single() : null;
        }
    }
    static bool IsManualResumeEligible(ManagedAccount? a,int limitMinutes) =>
        a is not null && !IsRetired(a) && WasUsed(a) && a.RunDurationKnown &&
        a.State is not ("ACTIVE" or "RESERVED" or "INVALID" or "RESTRICTED" or
                        "DISABLED" or "ACCOUNT_CHECKPOINT" or "NEEDS_REVIEW" or
                        "PURGED" or "COMPLETED") &&
        a.AccumulatedRunSeconds < Math.Clamp(limitMinutes,1,10080)*60;

    public ManagedAccount? ReserveManualResume(string serial,Guid id,int limitMinutes)
    {
        lock(_gate)
        {
            var a=_accounts.FirstOrDefault(x=>x.Id==id &&
                x.DeviceSerial.Equals(serial,StringComparison.OrdinalIgnoreCase));
            if(!IsManualResumeEligible(a,limitMinutes))return null;
            a!.State="RESERVED";
            a.LastStart=DateTimeOffset.UtcNow;
            a.LastResult="Đã giữ chỗ CHẠY TIẾP THỦ CÔNG; không đưa vào hàng đợi tự động";
            Save();
            return new ManagedAccount {Id=a.Id,Email=a.Email,Password=a.Password,
                DeviceSerial=a.DeviceSerial,ExpectedTikTokHandle=a.ExpectedTikTokHandle,State=a.State};
        }
    }

    // Used V5 records have no reliable cumulative timer. Require operator
    // review and explicit elapsed-minutes input; do not silently credit 0min.
    public void ConfirmLegacyRunMinutes(string serial,Guid id,double minutes,int limitMinutes)
    {
        lock(_gate)
        {
            var a=_accounts.FirstOrDefault(x=>x.Id==id &&
                x.DeviceSerial.Equals(serial,StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("Tài khoản không thuộc máy đã chọn.");
            if(!WasUsed(a) || a.RunDurationKnown || IsRetired(a) || a.State is "ACTIVE" or "RESERVED" or "COMPLETED" or "INVALID" or "RESTRICTED" or "DISABLED" or "ACCOUNT_CHECKPOINT")
                throw new InvalidOperationException("Chỉ xác nhận thời lượng TK cũ đã dừng, chưa xóa.");
            if(double.IsNaN(minutes) || double.IsInfinity(minutes) || minutes<0 ||
                minutes>Math.Clamp(limitMinutes,1,10080))
                throw new ArgumentOutOfRangeException(nameof(minutes));
            a.AccumulatedRunSeconds=minutes*60;
            a.SessionBudgetMinutes=Math.Clamp(limitMinutes,1,10080);
            a.RunDurationKnown=true;
            a.LegacyHistoryUnverified=false;
            a.LastDurationConfirmedAtUtc=DateTimeOffset.UtcNow;
            a.LastResult=$"Người vận hành xác nhận đã dùng {minutes:F2} phút; chỉ chạy lại thủ công";
            Save();
        }
    }

    public ManagedAccount? ReserveNext(string serial)
    {
        lock(_gate)
        {
            var a=FindNextCandidateUnsafe(serial);
            if(a is null) return null;
            bool recovering=IsRecoverableInterruption(a);
            a.State="RESERVED";
            a.LastStart=DateTimeOffset.Now;
            a.LastResult=recovering ? "Đang tự kiểm tra lại phiên gián đoạn" : "Đang mở phiên";
            Save();
            return new ManagedAccount { Id=a.Id, Email=a.Email, Password=a.Password,
                DeviceSerial=a.DeviceSerial, State=a.State, ExpectedTikTokHandle=a.ExpectedTikTokHandle };
        }
    }

    /// <summary>
    /// Rotation after a confirmed credential/policy failure must use a fresh,
    /// never-attempted account. Do not revive interrupted or retry-pending rows.
    /// </summary>
    public ManagedAccount? ReserveNextAvailable(string serial)
    {
        lock(_gate)
        {
            var a=_accounts.FirstOrDefault(item=>
                item.DeviceSerial.Equals(serial,StringComparison.OrdinalIgnoreCase) &&
                item.State=="AVAILABLE" && !WasUsed(item));
            if(a is null)return null;
            a.State="RESERVED";
            a.LastStart=DateTimeOffset.Now;
            a.LastResult="Đang mở phiên sau khi xoay tài khoản lỗi";
            Save();
            return new ManagedAccount { Id=a.Id,Email=a.Email,Password=a.Password,
                DeviceSerial=a.DeviceSerial,State=a.State,
                ExpectedTikTokHandle=a.ExpectedTikTokHandle };
        }
    }
    // Accumulate only verified LIVE time. Count wall-clock time from verified
    // start to stop, including on operator Stop and technical interruption.
    static void SettleRunTimeUnsafe(ManagedAccount a,DateTimeOffset now)
    {
        if(a.State!="ACTIVE" || !a.SessionStartedAtUtc.HasValue || !a.RunDurationKnown)return;
        a.AccumulatedRunSeconds+=Math.Max(0,(now-a.SessionStartedAtUtc.Value).TotalSeconds);
        a.SessionStartedAtUtc=null;
        a.SessionDeadlineUtc=null;
    }
    public void PauseVerifiedSession(Guid accountId,string reason)
    {
        lock(_gate)
        {
            var a=_accounts.FirstOrDefault(x=>x.Id==accountId);
            if(a is null || IsRetired(a))return;
            SettleRunTimeUnsafe(a,DateTimeOffset.UtcNow);
            if(a.State is "ACTIVE" or "RESERVED")a.State="INTERRUPTED";
            a.ExplicitTechnicalRetryPending=false;
            a.LastEnd=DateTimeOffset.UtcNow;
            a.CredentialPurgeDueAtUtc=null; // pause != expired; preserve credentials
            a.LastResult=reason;
            Save();
        }
    }

    public void SetStatus(Guid id, string state, string result)
    {
        lock(_gate)
        {
            var a=_accounts.FirstOrDefault(x=>x.Id==id); if(a is null) return;
            if(state!="ACTIVE")SettleRunTimeUnsafe(a,DateTimeOffset.UtcNow);
            a.State=state; a.LastResult=result;
            if(state!="RESERVED")a.ExplicitTechnicalRetryPending=false;
            if(state is "COMPLETED" or "NEEDS_REVIEW" or "RESTRICTED" or
                "ACCOUNT_CHECKPOINT" or "INVALID" or "INTERRUPTED" or "RETRY_PENDING")
                a.LastEnd=DateTimeOffset.Now;
            if(state=="ACTIVE")
            {
                a.EverUsed=true;
                a.FirstUsedAtUtc ??= DateTimeOffset.UtcNow;
            }
            if(state is "ACTIVE" or "COMPLETED")
            {
                a.TechnicalRetryCount=0;
                a.NextTechnicalRetryAt=null;
            }
            Save();
        }
    }
    // Called only after the expected TikTok handle was verified on this device.
    public DateTimeOffset StartVerifiedTimedSession(Guid accountId,int minutes)
    {
        lock(_gate)
        {
            var a=_accounts.Single(x=>x.Id==accountId);
            if(a.State!="RESERVED")
                throw new InvalidOperationException("Không thể bắt đầu phiên: tài khoản chưa được giữ chỗ.");
            var now=DateTimeOffset.UtcNow;
            int budget=Math.Clamp(minutes,1,10080);
            // Previously used accounts may enter here ONLY from the manual
            // reservation path, which requires a confirmed cumulative duration.
            if(a.EverUsed && !a.RunDurationKnown && !a.ExplicitTechnicalRetryPending)
                throw new InvalidOperationException("Không biết thời lượng lịch sử; cần xác minh Profile qua Start LIVE.");
            if(a.EverUsed && !a.RunDurationKnown && a.ExplicitTechnicalRetryPending)
            {
                // The old attempt did not have a trustworthy stopwatch.
                // Begin a NEW verified accounting window; never invent 0 historical minutes.
                a.LegacyHistoryUnverified=true;
                a.LastResult="Đăng nhập lại sau lỗi UI: bắt đầu mốc tính thời gian mới, lịch sử cũ chưa xác định";
            }
            a.ExplicitTechnicalRetryPending=false;
            a.RunDurationKnown=true;
            a.EverUsed=true;
            a.FirstUsedAtUtc ??= now;
            a.SessionBudgetMinutes=budget;
            double remainingSeconds=Math.Max(0,budget*60-a.AccumulatedRunSeconds);
            if(remainingSeconds<=0)
                throw new InvalidOperationException("Tài khoản đã hết tổng thời gian; không chạy tiếp.");
            a.SessionStartedAtUtc=now;
            a.SessionDeadlineUtc=now.AddSeconds(remainingSeconds);
            a.State="ACTIVE";
            a.LastResult="Đã xác minh username; phiên có giới hạn thời gian";
            a.TechnicalRetryCount=0;
            a.NextTechnicalRetryAt=null;
            Save();
            return a.SessionDeadlineUtc.Value;
        }
    }

    // V5: operator changed the duration for an already verified session.
    // Never rewrite SessionStartedAtUtc and never activate an unverified row.
    public void UpdateActiveSessionDeadline(Guid accountId,DateTimeOffset deadlineUtc,int limitMinutes)
    {
        lock(_gate)
        {
            var account=_accounts.Single(a=>a.Id==accountId);
            if(account.State!="ACTIVE" || !account.SessionStartedAtUtc.HasValue)
                throw new InvalidOperationException("Phiên tài khoản không còn ACTIVE; không cập nhật deadline.");
            account.SessionBudgetMinutes=Math.Clamp(limitMinutes,1,10080);
            account.SessionDeadlineUtc=deadlineUtc;
            Save();
        }
    }

    public void EndUsedSession(Guid accountId,string reason,int purgeAfterHours)
    {
        lock(_gate)
        {
            var a=_accounts.FirstOrDefault(x=>x.Id==accountId);
            if(a is null || IsRetired(a))return;
            a.EverUsed=true;
            a.FirstUsedAtUtc ??= a.LastStart?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
            SettleRunTimeUnsafe(a,DateTimeOffset.UtcNow);
            a.State="COMPLETED";
            a.LastEnd=DateTimeOffset.UtcNow;
            // This is an EXPIRED terminal state: ONLY now begin the purge timer.
            if(a.RunDurationKnown && a.SessionBudgetMinutes>0)
                a.AccumulatedRunSeconds=Math.Max(a.AccumulatedRunSeconds,a.SessionBudgetMinutes*60);
            a.SessionDeadlineUtc=null;
            a.CredentialPurgeDueAtUtc=null; // only after pm clear succeeds
            a.LastResult=reason;
            a.IsSelectedForRun=false;
            Save();
        }
    }

    // Data clearance is a separate external action. Never start the 24h
    // credential purge timer until BOTH TikTok and GMS pm clear succeeded.
    public void ScheduleCompletedPurge(Guid accountId,int purgeAfterHours)
    {
        lock(_gate)
        {
            var a=_accounts.Single(x=>x.Id==accountId);
            if(a.State!="COMPLETED" || !a.LastEnd.HasValue)
                throw new InvalidOperationException("Không được lên lịch xóa credential trước khi hoàn tất phiên.");
            a.CredentialPurgeDueAtUtc=DateTimeOffset.UtcNow.AddHours(
                Math.Clamp(purgeAfterHours,0,8760));
            Save();
        }
    }

    // Safe, synchronous vault cleanup. Passwords and identifying data disappear
    // from visible pools, while encrypted HMAC tombstones block re-import.
    public int PurgeDueUsedCredentials(DateTimeOffset now,int purgeAfterHours) =>
        PurgeDueUsedCredentials(now,_=>true,_=>purgeAfterHours);

    // Per-device cleanup: the expiry from a completed session is persisted;
    // each device can independently disable auto purge or choose its own delay.
    public int PurgeDueUsedCredentials(DateTimeOffset now,
        Func<string,bool> purgeEnabledForSerial,Func<string,int> purgeHoursForSerial)
    {
        ArgumentNullException.ThrowIfNull(purgeEnabledForSerial);
        ArgumentNullException.ThrowIfNull(purgeHoursForSerial);
        lock(_gate)
        {
            var due=_accounts.Where(a=>!IsRetired(a) && a.State=="COMPLETED" &&
                a.CredentialPurgeDueAtUtc.HasValue &&
                purgeEnabledForSerial(a.DeviceSerial) &&
                a.CredentialPurgeDueAtUtc.Value<=now).ToList();
            if(due.Count==0)return 0;
            foreach(var a in due)
            {
                a.EmailFingerprint=Fingerprint(a.Email);
                a.HandleFingerprint=Fingerprint(CanonicalHandle(a.ExpectedTikTokHandle));
                a.Email="";a.Password="";a.ExpectedTikTokHandle="";
                a.PreviousVerifiedTikTokHandle="";
                a.IsSelectedForRun=false;
                a.State="PURGED";
                a.LastResult="Đã xóa thông tin đăng nhập khỏi kho theo lịch";
                a.CredentialsPurgedAtUtc=now;
            }
            Save();
            return due.Count;
        }
    }

    // Called only after a successful endpoint rebind, with no worker on either
    // serial. Release stale reservations from a former ADB endpoint, preserving
    // credentials, NeverReuse history and the previously accumulated clock.
    // Only used AFTER strict Profile username verification inside the sole
    // registered LIVE worker. A previous process/ADB route can leave ACTIVE or
    // RESERVED persisted even though its Appium session is gone.
    public bool ReconcileVerifiedOrphanedOwner(string serial,Guid id)
    {
        lock(_gate)
        {
            var a=_accounts.FirstOrDefault(x=>x.Id==id &&
                x.DeviceSerial.Equals(serial,StringComparison.OrdinalIgnoreCase));
            if(a is null || a.State is not ("ACTIVE" or "RESERVED"))return false;
            if(a.State=="ACTIVE")SettleRunTimeUnsafe(a,DateTimeOffset.UtcNow);
            a.SessionStartedAtUtc=null;
            a.SessionDeadlineUtc=null;
            a.LastEnd=DateTimeOffset.UtcNow;
            a.State="INTERRUPTED";
            a.ExplicitTechnicalRetryPending=false;
            a.LastResult="Phiên cũ đã mất nhưng username Profile khớp chính xác; " +
                "khôi phục phiên LIVE theo đồng hồ đã lưu";
            Save();
            return true;
        }
    }

    public int ReconcileOrphanedAfterEndpointRebind(string serial)
    {
        lock(_gate)
        {
            var orphaned=_accounts.Where(a=>
                a.DeviceSerial.Equals(serial,StringComparison.OrdinalIgnoreCase) &&
                a.State is ("ACTIVE" or "RESERVED")).ToList();
            foreach(var a in orphaned)
            {
                if(a.State=="ACTIVE")SettleRunTimeUnsafe(a,DateTimeOffset.UtcNow);
                a.SessionStartedAtUtc=null;
                a.SessionDeadlineUtc=null;
                a.LastEnd=DateTimeOffset.UtcNow;
                a.ExplicitTechnicalRetryPending=false;
                a.State="INTERRUPTED";
                a.LastResult="Phiên Appium thuộc ADB cũ đã mất; kiểm tra lại Profile trên endpoint mới";
            }
            if(orphaned.Count>0)Save();
            return orphaned.Count;
        }
    }

    public void ChangeDeviceSerial(string oldSerial,string newSerial)
    {
        lock(_gate)
        {
            if(_accounts.Any(a=>a.DeviceSerial.Equals(newSerial,StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("ADB mới đã có kho riêng; không tự gộp hai kho tài khoản.");
            var moving=_accounts.Where(a=>a.DeviceSerial.Equals(oldSerial,StringComparison.OrdinalIgnoreCase)).ToList();
            foreach(var a in moving)a.DeviceSerial=newSerial;
            try {Save();}
            catch
            {
                foreach(var a in moving)a.DeviceSerial=oldSerial;
                throw;
            }
        }
    }
    /// <summary>
    /// Update only the handle of the one account already verified in this active
    /// Appium worker, after a post-Name read on the same device. Do not use this
    /// for an unknown/preflight-mismatched account or to change credentials.
    /// Returns false without writing anything when preconditions fail.
    /// </summary>
    public (bool Changed,string Reason) SyncHandleAfterVerifiedName(
        Guid accountId, string deviceSerial, string oldHandle, string observedHandle)
    {
        oldHandle=TikTokStateDetector.NormalizeHandle(oldHandle);
        observedHandle=TikTokStateDetector.NormalizeHandle(observedHandle);
        if(!TikTokStateDetector.IsValidHandle(oldHandle) ||
           !TikTokStateDetector.IsValidHandle(observedHandle))
            return (false,"HANDLE_SYNC_INVALID: username không hợp lệ.");
        lock(_gate)
        {
            var account=_accounts.SingleOrDefault(a=>a.Id==accountId);
            if(account is null)return (false,"HANDLE_SYNC_ACCOUNT_MISSING: ID không còn trong kho.");
            if(!string.Equals(account.DeviceSerial,deviceSerial,StringComparison.OrdinalIgnoreCase))
                return (false,"HANDLE_SYNC_DEVICE_MISMATCH: account không thuộc thiết bị này.");
            // Only the worker of an active, previously identity-verified account
            // may write this field. Manual SetHandle still requires stopped worker.
            if(account.State is not ("RESERVED" or "ACTIVE"))
                return (false,"HANDLE_SYNC_NOT_RUNNING: account không ở phiên đang xác minh.");
            if(TikTokStateDetector.NormalizeHandle(account.ExpectedTikTokHandle)!=oldHandle)
                return (false,"HANDLE_SYNC_STALE: handle trong kho đã thay đổi từ một phiên khác.");
            if(oldHandle==observedHandle)return (false,"HANDLE_SYNC_UNCHANGED");
            if(_accounts.Any(a=>a.Id!=accountId &&
                TikTokStateDetector.NormalizeHandle(a.ExpectedTikTokHandle)==observedHandle))
                return (false,"HANDLE_SYNC_CONFLICT: username mới đã thuộc account khác trong kho.");
            string previous=account.PreviousVerifiedTikTokHandle;
            var previousDate=account.VerifiedHandleChangedAt;
            account.PreviousVerifiedTikTokHandle=oldHandle;
            account.VerifiedHandleChangedAt=DateTimeOffset.UtcNow;
            account.ExpectedTikTokHandle=observedHandle;
            try { Save(); }
            catch
            {
                account.ExpectedTikTokHandle=oldHandle;
                account.PreviousVerifiedTikTokHandle=previous;
                account.VerifiedHandleChangedAt=previousDate;
                throw;
            }
            return (true,"HANDLE_SYNC_UPDATED");
        }
    }

    public void SetHandle(Guid id, string expectedHandle)
    {
        lock(_gate)
        {
            var a=_accounts.FirstOrDefault(x=>x.Id==id);
            if(a is null)return;
            if(a.State is "ACTIVE" or "RESERVED")throw new InvalidOperationException("Hãy kết thúc phiên đang hoạt động trước khi đổi TikTok handle.");
            a.ExpectedTikTokHandle=expectedHandle.Trim();
            // Manual corrections are not proof of an automatic, verified rename.
            a.PreviousVerifiedTikTokHandle="";
            a.VerifiedHandleChangedAt=null;
            Save();
        }
    }
    public void ResetToAvailable(IEnumerable<Guid> ids)
    {
        lock(_gate)
        {
            foreach(var a in _accounts.Where(x=>ids.Contains(x.Id) &&
                (!SingleUse(x.DeviceSerial) || !WasUsed(x)) &&
                (x.State is "NEEDS_REVIEW" or "COMPLETED" or "INTERRUPTED" or "RETRY_PENDING")))
            {
                a.State="AVAILABLE";
                a.TechnicalRetryCount=0;
                a.NextTechnicalRetryAt=null;
                a.LastResult="Người vận hành đã kiểm tra và đặt lại";
            }
            Save();
        }
    }
    void Save(IReadOnlyCollection<ManagedAccount>? accounts=null)
    {
        if(_disposed) throw new ObjectDisposedException(nameof(AccountVault));
        var pools=(accounts??_accounts)
            .GroupBy(a=>string.IsNullOrWhiteSpace(a.DeviceSerial)?"__unassigned__":a.DeviceSerial,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group=>group.Key,group=>group.ToList(),StringComparer.OrdinalIgnoreCase);
        byte[] plain=JsonSerializer.SerializeToUtf8Bytes(pools,Storage.JsonOptions);
        try
        {
            byte[] nonce=RandomNumberGenerator.GetBytes(12), cipher=new byte[plain.Length],tag=new byte[16];
            using(var aes=new AesGcm(_key,16)) aes.Encrypt(nonce,plain,cipher,tag);
            Storage.WriteAtomic(Storage.VaultFile,JsonSerializer.Serialize(new EncryptedBlob {
                Salt=Convert.ToBase64String(_salt),Nonce=Convert.ToBase64String(nonce),Cipher=Convert.ToBase64String(cipher),Tag=Convert.ToBase64String(tag)
            },Storage.JsonOptions));
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public void Dispose(){if(_disposed)return;_disposed=true;CryptographicOperations.ZeroMemory(_key);}
}
