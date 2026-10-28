using System.Text.Json.Serialization;

namespace AndroidMultiDeviceV1.Core;

public sealed class DeviceRecord
{
    public Guid DeviceId { get; set; } = Guid.NewGuid();
    public string Serial { get; set; } = "";
    public string Name { get; set; } = "";
    // Operator-owned inventory metadata; the immutable DeviceId and Serial
    // remain authoritative for workers and encrypted per-device account pools.
    public string DisplayAlias { get; set; } = "";
    public string CloudInstanceName { get; set; } = "";
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public string OperatorNotes { get; set; } = "";
    [JsonIgnore] public DateTimeOffset? LastSeenAt { get; set; }
    public int SystemPort { get; set; }
    public int SessionMinutes { get; set; } = 30;
    // V4: independent per-phone lifecycle. DeviceId is the durable policy owner;
    // changing alias/ADB endpoint never changes a phone's account policy.
    public AccountLifecyclePolicy? AccountLifecycle { get; set; } = new();
    [JsonIgnore] public string Connection { get; set; } = "UNKNOWN";
    [JsonIgnore] public string Model { get; set; } = "";
    [JsonIgnore] public string AppiumStatus { get; set; } = "DISCONNECTED";
    [JsonIgnore] public string WorkerState { get; set; } = "Idle";
    [JsonIgnore] public string LastError { get; set; } = "";
    [JsonIgnore] public string Stage { get; set; } = "IDLE";
    [JsonIgnore] public string AccountEmail { get; set; } = "";
    [JsonIgnore] public string Detail { get; set; } = "";
    [JsonIgnore] public DateTimeOffset? StartedAt { get; set; }
    [JsonIgnore] public DateTimeOffset? WorkerStartedAt { get; set; }
    // V5: observable live runtime (never persisted as a configuration value).
    [JsonIgnore] public string LifecycleRuntimeMode { get; set; } = "CHUA_KICH_HOAT";
    [JsonIgnore] public DateTimeOffset? LifecycleSessionStartedUtc { get; set; }
    [JsonIgnore] public DateTimeOffset? LifecycleDeadlineUtc { get; set; }
    [JsonIgnore] public int LifecycleActiveLimitMinutes { get; set; }
    [JsonIgnore] public string LifecycleAccountHandle { get; set; } = "";
}

public sealed class ManagedAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public bool IsSelectedForRun { get; set; } = false;
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public string DeviceSerial { get; set; } = "";
    public string State { get; set; } = "AVAILABLE";
    public string ExpectedTikTokHandle { get; set; } = "";
    // The previous VERIFIED handle is stored inside the encrypted vault so an
    // interrupted ledger update can be safely reconciled by immutable AccountId.
    public string PreviousVerifiedTikTokHandle { get; set; } = "";
    public DateTimeOffset? VerifiedHandleChangedAt { get; set; }
    public DateTimeOffset? LastStart { get; set; }
    public DateTimeOffset? LastEnd { get; set; }
    public string LastResult { get; set; } = "";
    // V3: persisted inside encrypted vault. EverUsed never reverts to false.
    public bool EverUsed { get; set; }
    public DateTimeOffset? FirstUsedAtUtc { get; set; }
    public DateTimeOffset? SessionStartedAtUtc { get; set; }
    public DateTimeOffset? SessionDeadlineUtc { get; set; }
    public DateTimeOffset? CredentialPurgeDueAtUtc { get; set; }
    public DateTimeOffset? CredentialsPurgedAtUtc { get; set; }
    // V5.2: cumulative VERIFIED run time. Legacy records may have an unknown
    // earlier history; track the new verified interval without claiming that
    // its elapsed value represents lifetime usage.
    public bool RunDurationKnown { get; set; }
    // V5.2: older history had no trustworthy stopwatch; the recorded interval
    // starts at the first VERIFIED Start LIVE after upgrade. Never represent it
    // as the full lifetime usage of this account.
    public bool LegacyHistoryUnverified { get; set; }
    public double AccumulatedRunSeconds { get; set; }
    public int SessionBudgetMinutes { get; set; }
    public DateTimeOffset? LastDurationConfirmedAtUtc { get; set; }
    // HMAC digests retain a no-credentials duplicate guard after auto purge.
    public string EmailFingerprint { get; set; } = "";
    public string HandleFingerprint { get; set; } = "";
    [JsonIgnore] public string UsedDisplay => EverUsed || LastStart.HasValue ? "ĐÃ DÙNG" : "CHƯA DÙNG";
    [JsonIgnore] public double EffectiveRunSeconds => AccumulatedRunSeconds +
        (State=="ACTIVE" && SessionStartedAtUtc.HasValue
            ? Math.Max(0,(DateTimeOffset.UtcNow-SessionStartedAtUtc.Value).TotalSeconds) : 0);
    [JsonIgnore] public string UsedDurationDisplay => !EverUsed && !LastStart.HasValue
        ? "00:00:00" : RunDurationKnown
            ? AccountDurationDisplay.FormatElapsed(EffectiveRunSeconds) : "Chưa xác định";
    [JsonIgnore] public string RemainingDisplay => !EverUsed && !LastStart.HasValue
        ? "Chưa chạy" : RunDurationKnown && SessionBudgetMinutes>0
            ? Math.Max(0,(int)Math.Ceiling(SessionBudgetMinutes*60-EffectiveRunSeconds)).ToString()+" giây" +
              (LegacyHistoryUnverified ? " (mốc mới)" : "")
            : "Chưa xác định";
    [JsonIgnore] public string StateVi => (State is "INTERRUPTED" or "AVAILABLE" or "RETRY_PENDING")
        && (EverUsed || LastStart.HasValue)
        ? !RunDurationKnown ? "Đã dùng · chưa rõ giờ" :
            (SessionBudgetMinutes>0 && EffectiveRunSeconds>=SessionBudgetMinutes*60
                ? "Đã dùng · hết giờ" : LegacyHistoryUnverified
                    ? "Đã dùng · theo dõi từ mốc mới" : "Đã dùng · có thể tiếp tục")
        : AccountLifecycleLabels.Translate(State,EverUsed || LastStart.HasValue);
    // Technical failures are retryable; these fields persist across restarts.
    public int TechnicalRetryCount { get; set; }
    public DateTimeOffset? NextTechnicalRetryAt { get; set; }
    // V5.9: one explicitly selected retry of a known UI/transport error.
    // Never used by AUTO; cleared after verified session creation.
    public bool ExplicitTechnicalRetryPending { get; set; }
}

public sealed class AccountLifecyclePolicy
{
    public int SessionLimitMinutes { get; set; } = 360;
    public bool AutoRotateAccounts { get; set; } = true;
    public bool NeverReuseUsedAccounts { get; set; } = true;
    public bool AutoPurgeUsedAccounts { get; set; } = true;
    public int PurgeUsedAfterHours { get; set; } = 24;

    public AccountLifecyclePolicy Copy() => new()
    {
        SessionLimitMinutes = SessionLimitMinutes,
        AutoRotateAccounts = AutoRotateAccounts,
        NeverReuseUsedAccounts = NeverReuseUsedAccounts,
        AutoPurgeUsedAccounts = AutoPurgeUsedAccounts,
        PurgeUsedAfterHours = PurgeUsedAfterHours
    };
}

public sealed class Settings
{
    public string AdbPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk", "platform-tools", "adb.exe");
    public string AppiumUrl { get; set; } = "http://127.0.0.1:4723";
    public int MaxParallel { get; set; } = 3;
    public int MaxDevicesPerLiveRoom { get; set; } = 5;
    public int MaxSessionRecoveryAttempts { get; set; } = 3;
    public bool AutoUpdateProfile { get; set; } = false;
    public bool ForceLiveIdleOneSecond { get; set; } = true;
    // V3 legacy defaults are migration input only; worker policy comes from each DeviceRecord.
    // V3: new account per verified session, per-device worker deadline.
    public int AccountSessionLimitMinutes { get; set; } = 360;
    public bool AutoRotateAccounts { get; set; } = true;
    public bool NeverReuseUsedAccounts { get; set; } = true;
    public bool AutoPurgeUsedAccounts { get; set; } = true;
    public int PurgeUsedAfterHours { get; set; } = 24;
    // Operator-only dashboard layout preferences; legacy settings.json without these keys remains valid.
    public Dictionary<string,int> DeviceDashboardColumnWidths { get; set; } = new();
    public Dictionary<string,bool> DeviceDashboardColumnVisibility { get; set; } = new();
    public int DeviceDashboardSplitterDistance { get; set; } = 0;
    public List<DeviceRecord> Devices { get; set; } = [];

    public AccountLifecyclePolicy AccountPolicyFor(DeviceRecord device) =>
        device.AccountLifecycle ??= new AccountLifecyclePolicy();

    public AccountLifecyclePolicy AccountPolicyFor(string serial) =>
        Devices.FirstOrDefault(d => d.Serial.Equals(serial,StringComparison.OrdinalIgnoreCase))
            is { } device ? AccountPolicyFor(device) : new AccountLifecyclePolicy();
    // A connected ADB endpoint that the operator deliberately removed must not
    // be silently re-added by the next Refresh. Explicit Connect removes it.
    public List<string> RemovedDeviceSerials { get; set; } = [];
}

public enum LoginOutcome { Confirmed, NeedsHuman, TechnicalFailure, Restricted, AccountInvalid }
public sealed record LoginResult(LoginOutcome Outcome, string Detail);
public sealed record UiLocator(string By, string Value);
