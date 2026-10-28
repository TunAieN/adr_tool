using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace AndroidMultiDeviceV1.Core;

// Shared configuration across all devices. Disabled by default so upgrading never
// silently edits a real account. No credentials are stored here.
public sealed class ProfileUpdateOptions
{
    public bool Enabled { get; set; }
    public bool ChangeName { get; set; }
    public bool ChangeBio { get; set; }
    public bool ChangePhoto { get; set; }
    // Explicit user opt-in, because TikTok calls the action Save & post.
    public bool AllowPhotoSaveAndPost { get; set; }
    public bool RandomName { get; set; } = true;
    public bool RandomBio { get; set; } = true;
    public bool StrictUniquePhotos { get; set; } = true;
    public string Names { get; set; } = "";
    public string Bios { get; set; } = "";
    public string PhotoFolder { get; set; } = "";
}

public sealed class ProfileUpdateRecord
{
    public Guid AccountId { get; set; }
    public string TikTokHandle { get; set; } = "";
    public string Name { get; set; } = "";
    public string Bio { get; set; } = "";
    public string PhotoSha256 { get; set; } = "";
    public string PhotoVisualHash { get; set; } = "";
    public string PhotoFile { get; set; } = "";
    public bool NameVerified { get; set; }
    // Persistent per-account state: never repeat a potentially submitted nickname change.
    // Values: "" (legacy), VERIFIED, SAVE_PENDING, COOLDOWN.
    public string NameChangeState { get; set; } = "";
    public string NameObserved { get; set; } = "";
    public DateTimeOffset? NameAttemptedAt { get; set; }
    public DateTimeOffset? NameEligibleAt { get; set; }
    public bool BioVerified { get; set; }
    // VERIFIED or SAVE_PENDING; avoid resubmitting a Bio change on an uncertain save.
    public string BioChangeState { get; set; } = "";
    public bool PhotoVerified { get; set; }
    // IDLE / GALLERY_REVIEW / SAVE_PENDING / VERIFIED; never post twice when outcome is uncertain.
    public string PhotoChangeState { get; set; } = "";
    public DateTimeOffset? PhotoAttemptedAt { get; set; }
    public DateTimeOffset LastUpdated { get; set; }
}

public static class ProfileUpdateStore
{
    public static string SettingsPath => Path.Combine(Storage.Folder, "profile-update.json");
    public static string RecordsPath => Path.Combine(Storage.Folder, "profile-update-records.json");
    public static ProfileUpdateOptions Read()
    {
        try { return File.Exists(SettingsPath) ? JsonSerializer.Deserialize<ProfileUpdateOptions>(File.ReadAllText(SettingsPath),Storage.JsonOptions) ?? new() : new(); }
        catch(JsonException) { return new(); }
    }
    public static void Save(ProfileUpdateOptions settings)
        => Storage.WriteAtomic(SettingsPath,JsonSerializer.Serialize(settings,Storage.JsonOptions));
}

// One global lock protects the photo ledger for all concurrently running workers.
// Strict uniqueness is checked against actual SHA256 content, not filenames.
public sealed class ProfileUpdateLedger
{
    readonly object _gate=new();
    readonly Dictionary<Guid,ProfileUpdateRecord> _records;
    public ProfileUpdateLedger()
    {
        try { _records=File.Exists(ProfileUpdateStore.RecordsPath)
            ? JsonSerializer.Deserialize<List<ProfileUpdateRecord>>(File.ReadAllText(ProfileUpdateStore.RecordsPath),Storage.JsonOptions)
                ?.ToDictionary(x=>x.AccountId) ?? new()
            : new(); }
        catch(Exception e) when(e is IOException or JsonException or ArgumentException)
        { throw new InvalidDataException("Không thể đọc lịch sử hồ sơ; không được tự xóa lịch sử trùng ảnh.",e); }
    }
    void SaveLocked() => Storage.WriteAtomic(ProfileUpdateStore.RecordsPath,
        JsonSerializer.Serialize(_records.Values.ToList(),Storage.JsonOptions));
    public ProfileUpdateRecord Snapshot(Guid id)
    {
        lock(_gate)
        {
            if(!_records.TryGetValue(id,out var r))return new() {AccountId=id};
            return new() {AccountId=id,TikTokHandle=r.TikTokHandle,Name=r.Name,Bio=r.Bio,
                PhotoSha256=r.PhotoSha256,PhotoVisualHash=r.PhotoVisualHash,PhotoFile=r.PhotoFile,NameVerified=r.NameVerified,
                NameChangeState=r.NameChangeState,NameObserved=r.NameObserved,NameAttemptedAt=r.NameAttemptedAt,
                NameEligibleAt=r.NameEligibleAt,
                BioVerified=r.BioVerified,BioChangeState=r.BioChangeState,PhotoVerified=r.PhotoVerified,
                PhotoChangeState=r.PhotoChangeState,PhotoAttemptedAt=r.PhotoAttemptedAt,LastUpdated=r.LastUpdated};
        }
    }
    public void Mark(Guid id,string handle,Action<ProfileUpdateRecord> update)
    {
        lock(_gate)
        {
            if(!_records.TryGetValue(id,out var r))_records[id]=r=new(){AccountId=id};
            r.TikTokHandle=TikTokStateDetector.NormalizeHandle(handle);
            update(r);r.LastUpdated=DateTimeOffset.Now;SaveLocked();
        }
    }
    static ulong VisualHash(Image photo)
    {
        // 64-bit difference hash of the visual content; unlike a file hash,
        // it notices near-identical JPEG/PNG encodings of the same photo.
        using var thumb=new Bitmap(9,8);
        using(var g=Graphics.FromImage(thumb))g.DrawImage(photo,0,0,9,8);
        ulong hash=0;
        for(int y=0;y<8;y++)for(int x=0;x<8;x++)
        {
            var a=thumb.GetPixel(x,y);var b=thumb.GetPixel(x+1,y);
            int aa=a.R*299+a.G*587+a.B*114;
            int bb=b.R*299+b.G*587+b.B*114;
            hash=(hash<<1)|(aa>bb?1UL:0UL);
        }
        return hash;
    }
    static int VisualDistance(ulong a,ulong b)=>System.Numerics.BitOperations.PopCount(a^b);
    public string ReserveUniquePhoto(Guid id,string folder)
    {
        var supported=new HashSet<string>(StringComparer.OrdinalIgnoreCase){".jpg",".jpeg",".png",".bmp"};
        if(!Directory.Exists(folder))throw new DirectoryNotFoundException("Thư mục ảnh không tồn tại: "+folder);
        var photos=Directory.EnumerateFiles(folder,"*",SearchOption.TopDirectoryOnly)
            .Where(f=>supported.Contains(Path.GetExtension(f))).ToList();
        if(photos.Count==0)throw new InvalidOperationException("Thư mục ảnh không có ảnh hỗ trợ.");
        lock(_gate)
        {
            if(_records.TryGetValue(id,out var existing) && File.Exists(existing.PhotoFile))
                return existing.PhotoFile; // stable association, even after interrupted run
            var used=_records.Values.Where(x=>x.AccountId!=id && x.PhotoSha256.Length>0)
                .Select(x=>x.PhotoSha256).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var usedVisual=_records.Values.Where(x=>x.AccountId!=id && x.PhotoVisualHash.Length>0)
                .Select(x=>ulong.TryParse(x.PhotoVisualHash,System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture,out var value)?value:(ulong?)null)
                .Where(x=>x.HasValue).Select(x=>x!.Value).ToList();
            var options=new List<(string File,string Hash,string Visual)>();
            foreach(var photo in photos)
            {
                try
                {
                    using var image=Image.FromFile(photo);
                    // Never silently upscale a too-small source just to bypass TikTok's limit.
                    if(image.Width<250 || image.Height<250)continue;
                    string hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(photo)));
                    ulong visual=VisualHash(image);
                    if(!used.Contains(hash) && !usedVisual.Any(v=>VisualDistance(v,visual)<=4))
                        options.Add((photo,hash,visual.ToString("X16")));
                }
                catch(Exception e) when(e is IOException or ArgumentException or OutOfMemoryException) { }
            }
            if(options.Count==0)throw new InvalidOperationException(
                "Không còn ảnh hợp lệ chưa sử dụng (mỗi chiều >= 250 px). Bổ sung ảnh mới vào thư mục.");
            var selected=options[RandomNumberGenerator.GetInt32(options.Count)];
            if(!_records.TryGetValue(id,out var row))_records[id]=row=new(){AccountId=id};
            // Reserve persistently BEFORE uploading. If Android disconnects halfway,
            // another account won't silently receive the same picture.
            row.PhotoFile=selected.File;row.PhotoSha256=selected.Hash;
            row.PhotoVisualHash=selected.Visual;
            row.PhotoVerified=false;row.PhotoChangeState="IDLE";row.LastUpdated=DateTimeOffset.Now;SaveLocked();
            return selected.File;
        }
    }
}

public static class ProfilePhotoPreparer
{
    public static string PrepareJpeg(string source,Guid accountId)
    {
        using var original=Image.FromFile(source);
        if(original.Width<250 || original.Height<250)
            throw new InvalidDataException("Ảnh nguồn phải có chiều rộng và chiều cao tối thiểu 250 px.");
        // Correct JPEG EXIF rotation before cropping.
        const int orientation=0x0112;
        if(original.PropertyIdList.Contains(orientation))
        {
            var value=original.GetPropertyItem(orientation)?.Value;
            if(value is {Length:>=2})
            {
                int n=BitConverter.ToUInt16(value,0);
                var rotate=n switch
                {
                    2=>RotateFlipType.RotateNoneFlipX,3=>RotateFlipType.Rotate180FlipNone,
                    4=>RotateFlipType.Rotate180FlipX,5=>RotateFlipType.Rotate90FlipX,
                    6=>RotateFlipType.Rotate90FlipNone,7=>RotateFlipType.Rotate270FlipX,
                    8=>RotateFlipType.Rotate270FlipNone,_=>RotateFlipType.RotateNoneFlipNone
                };
                original.RotateFlip(rotate);
            }
        }
        string dir=Path.Combine(Storage.Folder,"prepared-photos");Directory.CreateDirectory(dir);
        string output=Path.Combine(dir,accountId.ToString("N")+".jpg");
        string temp=output+".tmp";
        using(var canvas=new Bitmap(720,720))
        using(var g=Graphics.FromImage(canvas))
        {
            g.Clear(Color.White);g.InterpolationMode=InterpolationMode.HighQualityBicubic;
            g.SmoothingMode=SmoothingMode.HighQuality;
            g.PixelOffsetMode=PixelOffsetMode.HighQuality;
            int side=Math.Min(original.Width,original.Height);
            var rect=new Rectangle((original.Width-side)/2,(original.Height-side)/2,side,side);
            g.DrawImage(original,new Rectangle(0,0,720,720),rect,GraphicsUnit.Pixel);
            var encoder=ImageCodecInfo.GetImageEncoders().First(x=>x.MimeType=="image/jpeg");
            using var parameters=new EncoderParameters(1);
            parameters.Param[0]=new EncoderParameter(System.Drawing.Imaging.Encoder.Quality,90L);
            canvas.Save(temp,encoder,parameters);
        }
        File.Move(temp,output,true);
        return output;
    }
}

public sealed record ProfileUpdateResult(bool Success,string Detail,bool Retryable=false,bool Deferred=false);

public sealed class ProfileUpdateWorkflow(UiSelectors selectors,Action<string> log)
{
    static string[] Lines(string text) => text.Split(new char[] { '\n', '\r' },StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries)
        .Where(s=>!string.IsNullOrWhiteSpace(s)).Distinct().ToArray();
    static string Choose(string[] data,bool random,Guid accountId)
    {
        if(data.Length==0)throw new InvalidOperationException("Chưa nhập danh sách giá trị cho hồ sơ.");
        int index=random?RandomNumberGenerator.GetInt32(data.Length):
            (int)((uint)accountId.GetHashCode() % (uint)data.Length);
        return data[index];
    }
    async Task<bool> WaitTap(AppiumSession session,string key,int seconds,CancellationToken ct)
    {
        var t=Stopwatch.StartNew();
        do
        {
            ct.ThrowIfCancellationRequested();
            if(await session.ClickAnyAsync(selectors.Get(key),ct))return true;
            await Task.Delay(700,ct);
        }while(t.Elapsed<TimeSpan.FromSeconds(seconds));
        return false;
    }
    async Task<bool> WaitVisible(AppiumSession session,string key,int seconds,CancellationToken ct)
    {
        var t=Stopwatch.StartNew();
        do
        {
            ct.ThrowIfCancellationRequested();
            if(await session.ExistsAnyAsync(selectors.Get(key),ct))return true;
            await Task.Delay(700,ct);
        }while(t.Elapsed<TimeSpan.FromSeconds(seconds));
        return false;
    }
    // Dedicated text-field selectors avoid mistaking the new Name editor for Bio.
    async Task<bool> CheckInput(AppiumSession session,string key,string expected,CancellationToken ct)
    {
        foreach(var loc in selectors.Get(key))
        {
            var id=await session.FindAsync(loc,ct);
            if(id is not null && (await session.ElementTextAsync(id,ct)).Trim().Equals(expected.Trim(),StringComparison.Ordinal))return true;
        }
        return false;
    }
    static async Task<string> ReadDisplayedNameAsync(AppiumSession session,UiSelectors selectors,
        CancellationToken ct)
    {
        foreach(var locator in selectors.Get("profileNameEntry"))
        {
            var element=await session.FindAsync(locator,ct);
            if(element is null)continue;
            var value=(await session.ElementTextAsync(element,ct)).Trim();
            if(!string.IsNullOrWhiteSpace(value))return value;
        }
        return "";
    }

    static bool IsNameCooldown(string source)
    {
        return source.Contains("nickname can only be changed",StringComparison.OrdinalIgnoreCase) ||
            source.Contains("change it again after",StringComparison.OrdinalIgnoreCase) ||
            source.Contains("name can only be changed every 7 days",StringComparison.OrdinalIgnoreCase) ||
            source.Contains("tên chỉ có thể thay đổi",StringComparison.OrdinalIgnoreCase);
    }

    static DateTimeOffset EstimateNameEligibleAt(string source)
    {
        var match=Regex.Match(source,@"(?:after|sau)\s+([A-Za-z]+\s+\d{1,2},?\s+\d{4})",
            RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        if(match.Success && DateTime.TryParse(match.Groups[1].Value,CultureInfo.GetCultureInfo("en-US"),
            DateTimeStyles.None,out var date))
        {
            // UI says 'after [date]'; choose the following day in the user's operating timezone.
            return new DateTimeOffset(DateTime.SpecifyKind(date.Date.AddDays(1),DateTimeKind.Unspecified),TimeSpan.FromHours(7));
        }
        // This is only a conservative estimate if TikTok does not expose a date.
        return DateTimeOffset.Now.AddDays(7);
    }

    static bool SourceIsEditProfile(string source)
    {
        // The page must have Edit profile header AND its Name/Bio fields.
        // A Profile page exposing the Edit button is not an editor page.
        return HasExactUiText(source,"Edit profile") &&
               HasExactUiText(source,"Name") && HasExactUiText(source,"Bio");
    }

    async Task<bool> IsEditProfileAsync(AppiumSession session,CancellationToken ct)
        => SourceIsEditProfile(await session.SourceAsync(ct));

    // Username verification on the NEW UI navigates into Edit profile in order
    // to read mud[2]. This workflow receives the SAME Appium session and must
    // reuse that page rather than blindly tapping Profile -> Edit again.
    // Older builds verify the handle directly from Profile, so keep the
    // original navigation path whenever the editor is not already open.
    async Task<(bool Ready,string Detail)> PrepareEditProfileAsync(AppiumSession session,
        ManagedAccount account,CancellationToken ct)
    {
        bool HasWrongHandle(string source,out string detail)
        {
            detail="";
            string expected=TikTokStateDetector.NormalizeHandle(account.ExpectedTikTokHandle);
            if(TikTokStateDetector.TryReadNewEditProfileUsername(source,out string actual))
            {
                if(!string.Equals(actual,expected,StringComparison.OrdinalIgnoreCase))
                {
                    detail="PROFILE_UPDATE_HANDLE_MISMATCH: username của Edit profile không khớp tài khoản được phân công; không sửa hồ sơ.";
                    return true;
                }
                log("PROFILE_UPDATE_HANDLE_RECHECK: mud[2] khớp đúng username tài khoản đã xác minh.");
            }
            else if(source.Contains("com.ss.android.ugc.trill:id/mud",StringComparison.Ordinal))
            {
                // A new-style editor with unreadable mud[2] must not be treated
                // as the older layout: the wrong account might be open.
                detail="PROFILE_UPDATE_USERNAME_UNVERIFIED: có mud nhưng không đọc được mud[2]; không sửa hồ sơ.";
                return true;
            }
            return false;
        }

        var current=await session.SourceAsync(ct);
        if(SourceIsEditProfile(current))
        {
            if(HasWrongHandle(current,out var problem))return (false,problem);
            log("PROFILE_UPDATE_EDIT_REUSED: sau xác minh đã ở Edit profile; bỏ qua nhấn Profile/Sửa hồ sơ lần hai.");
            return (true,"");
        }

        // An older recovery path may leave the UI in the Username sub-editor.
        // In that case return ONE level to Edit profile before attempting any
        // Name/Bio/Avatar mutation. Never mistake the Username form for Name.
        if(!SourceIsEditProfile(current) &&
           (HasExactUiText(current,"Username") || HasExactUiText(current,"Edit username")) &&
           current.Contains("<android.widget.EditText",StringComparison.Ordinal))
        {
            log("PROFILE_UPDATE_USERNAME_SUBEDITOR_DETECTED: đang ở màn hình sửa Username; quay lại Edit profile một lần.");
            await session.BackAsync(ct);
            await Task.Delay(750,ct);
            var returned=await session.SourceAsync(ct);
            if(!SourceIsEditProfile(returned))
                return (false,"PROFILE_UPDATE_USERNAME_SUBEDITOR_RETURN_FAILED: chưa quay về Edit profile; không sửa thông tin.");
            if(HasWrongHandle(returned,out var recoveryProblem))return (false,recoveryProblem);
            log("PROFILE_UPDATE_USERNAME_SUBEDITOR_RETURNED: đã về đúng Edit profile và xác minh lại mud[2].");
            return (true,"");
        }

        // Legacy route: preflight validated username directly from Profile.
        // Preserve the previous Profile-tab flow; add support for the new tlh
        // control if we happen to land back on the new-style Profile page.
        log("PROFILE_UPDATE_LEGACY_NAVIGATION: chưa ở Edit profile; mở Profile như luồng cũ.");
        if(!await WaitTap(session,"profile",12,ct))
            return (false,"PROFILE_UPDATE_PROFILE_NOT_FOUND: không mở được tab Profile sau xác minh đăng nhập.");

        var profile=await session.SourceAsync(ct);
        if(SourceIsEditProfile(profile))
        {
            if(HasWrongHandle(profile,out var problem))return (false,problem);
            log("PROFILE_UPDATE_EDIT_READY: đã ở Edit profile sau khi mở Profile.");
            return (true,"");
        }
        bool newStyle=TikTokStateDetector.SourceHasNewOwnerEditButton(profile);
        var selectorName=newStyle ? "profileEditEntry" : "profileEditButton";
        log(newStyle ? "PROFILE_UPDATE_OPEN_NEW: tìm nút tlh." :
            "PROFILE_UPDATE_OPEN_LEGACY: tìm nút Edit giao diện cũ.");
        if(!await WaitTap(session,selectorName,12,ct))
            return (false,"PROFILE_UPDATE_EDIT_BUTTON_NOT_FOUND: không mở được Sửa hồ sơ ("+selectorName+").");

        // Clicking is not proof of navigation. Confirm the editor before
        // touching any Name, Bio or photo controls.
        for(int attempt=0;attempt<4;attempt++)
        {
            await Task.Delay(650,ct);
            var source=await session.SourceAsync(ct);
            if(!SourceIsEditProfile(source))continue;
            if(HasWrongHandle(source,out var problem))return (false,problem);
            log("PROFILE_UPDATE_EDIT_READY: đã xác nhận Edit profile trước khi sửa thông tin.");
            return (true,"");
        }
        return (false,"PROFILE_UPDATE_EDIT_NOT_CONFIRMED: đã nhấn Edit nhưng chưa thấy trang Sửa hồ sơ; dừng an toàn.");
    }

    async Task<bool> ReturnToEditAsync(AppiumSession session,CancellationToken ct)
    {
        if(await IsEditProfileAsync(session,ct))return true;
        for(int i=0;i<3;i++)
        {
            ct.ThrowIfCancellationRequested();
            if(await session.ExistsAnyAsync(selectors.Get("profileEditButton"),ct))
            {
                await session.ClickVisibleAnyAsync(selectors.Get("profileEditButton"),ct);
            }
            else if(await session.ExistsAnyAsync(selectors.Get("profileEditEntry"),ct))
            {
                // New layout returns to Profile with tlh instead of the old Edit button.
                await session.ClickVisibleAnyAsync(selectors.Get("profileEditEntry"),ct);
            }
            else if(!await session.ClickVisibleAnyAsync(selectors.Get("profileEditorBack"),ct))
            {
                await session.BackAsync(ct);
            }
            await Task.Delay(800,ct);
            if(await IsEditProfileAsync(session,ct))return true;
        }
        return false;
    }

    async Task<bool> InspectBioEditorAsync(AppiumSession session,CancellationToken ct)
    {
        var source=await session.SourceAsync(ct);
        if(HasExactUiText(source,"Edit profile") || HasExactUiText(source,"Username") ||
           HasExactUiText(source,"Name"))return false;
        if(!HasExactUiText(source,"Bio") && !HasExactUiText(source,"Edit bio"))return false;
        return await session.ExistsAnyAsync(selectors.Get("profileBioEditorInput"),ct);
    }

    async Task<bool> OpenBioEditorAsync(AppiumSession session,CancellationToken ct)
    {
        if(!await ReturnToEditAsync(session,ct))
        {
            log("Tiểu sử: chưa trở về màn hình Edit profile sau khi sửa Name.");
            return false;
        }
        int attempt=0;
        foreach(var candidate in selectors.Get("profileBioOpenCandidates"))
        {
            ct.ThrowIfCancellationRequested();
            attempt++;
            if(!await session.ClickVisibleAnyAsync(new[]{candidate},ct))continue;
            log($"Tiểu sử: đã click ứng viên hàng Bio {attempt}; kiểm tra màn hình tiếp theo.");
            for(int poll=0;poll<5;poll++)
            {
                await Task.Delay(700,ct);
                if(await InspectBioEditorAsync(session,ct))
                {
                    log("PROFILE_BIO_EDITOR_READY: đúng màn hình Bio; ô nhập hw7/hvc.");
                    return true;
                }
            }
            if(!await IsEditProfileAsync(session,ct))
            {
                // An unrelated editor was opened. Never type into an unknown field.
                log("Tiểu sử: click dẫn tới màn hình khác Bio; quay về Edit profile.");
                if(!await ReturnToEditAsync(session,ct))return false;
            }
        }
        log("Tiểu sử: không thể mở editor từ nhãn Bio hoặc tvContent; không nhập dữ liệu.");
        return false;
    }

    async Task<string> ReadDisplayedBioAsync(AppiumSession session,CancellationToken ct)
    {
        if(!await IsEditProfileAsync(session,ct))return "";
        foreach(var locator in selectors.Get("profileBioEntry"))
        {
            var element=await session.FindAsync(locator,ct);
            if(element is null)continue;
            string result=(await session.ElementTextAsync(element,ct)).Trim();
            if(!string.IsNullOrWhiteSpace(result))return result;
        }
        return "";
    }

    async Task<string> WaitDisplayedBioAsync(AppiumSession session,string expected,CancellationToken ct)
    {
        string observed="";
        for(int i=0;i<6;i++)
        {
            observed=await ReadDisplayedBioAsync(session,ct);
            if(observed.Equals(expected,StringComparison.Ordinal))return observed;
            await Task.Delay(650,ct);
        }
        return observed;
    }

    // Clicking a row in Edit Profile is not proof that the Name editor opened.
    // Some TikTok builds expose the name as a TextView which accepts a click
    // even when the real click target is its surrounding row.
    enum NameEditorState { NotOpened, Ready, Cooldown, WrongEditor }

    static bool HasExactUiText(string source,string text)
    {
        // Appium page source contains the Android node attributes as XML.
        // Match a WHOLE text or content-desc attribute, not a substring:
        // 'Name' must never be confused with 'Username'.
        string escaped=Regex.Escape(System.Security.SecurityElement.Escape(text) ?? text);
        return Regex.IsMatch(source,"(?:text|content-desc)=\""+escaped+"\"",RegexOptions.IgnoreCase);
    }

    async Task<NameEditorState> InspectNameEditorAsync(AppiumSession session,CancellationToken ct)
    {
        var source=await session.SourceAsync(ct);
        if(IsNameCooldown(source))return NameEditorState.Cooldown;
        bool hasNewNameInput=await session.ExistsAnyAsync(selectors.Get("profileNameEditorInputNew"),ct);
        bool hasLegacyInput=!hasNewNameInput && await session.ExistsAnyAsync(selectors.Get("profileEditorInput"),ct);
        if(!hasNewNameInput && !hasLegacyInput)return NameEditorState.NotOpened;
        // On the user's Edit Profile page mud[1] is Name; mud[2] is Username.
        // hw7 is reused by Name AND Bio on some builds, so a matching input
        // resource-id alone never authorizes typing; check the page header.
        if(HasExactUiText(source,"Username") || HasExactUiText(source,"Edit username") ||
           HasExactUiText(source,"Edit profile"))return NameEditorState.WrongEditor;
        // A correct input locator alone does not prove we opened Name.
        // Require a specific Name/Display Name/Nickname heading on BOTH UIs.
        // mud[2] is used ONLY to READ username and is not a Name-entry target.
        bool namePage=HasExactUiText(source,"Name") ||
                      HasExactUiText(source,"Edit name") ||
                      HasExactUiText(source,"Display name") ||
                      HasExactUiText(source,"Nickname");
        if(namePage && (hasNewNameInput || hasLegacyInput))
            return NameEditorState.Ready;
        return NameEditorState.WrongEditor;
    }

    async Task<NameEditorState> OpenNameEditorAsync(AppiumSession session,CancellationToken ct)
    {
        // Prefer mud[1] exactly for the new Name row. Do not use mud[2]
        // (Username); verify destination by page header and editable field.
        // Each candidate is clicked separately; a tap is NOT proof of navigation.
        int attempt=0;
        foreach(var target in selectors.Get("profileNameOpenCandidates"))
        {
            ct.ThrowIfCancellationRequested();
            // A preceding candidate might have opened Username or another
            // unexpected page without the expected input control. Never click
            // more row candidates outside Edit profile.
            if(!await ReturnToEditAsync(session,ct))
            {
                log("PROFILE_NAME_NAVIGATION_STOP: không xác minh được Edit profile trước khi click.");
                return NameEditorState.NotOpened;
            }
            attempt++;
            if(!await session.ClickVisibleAnyAsync(new[]{target},ct))continue;
            log($"Tên hiển thị: đã click ứng viên dòng Name {attempt}; kiểm tra màn hình tiếp theo.");
            for(int poll=0;poll<4;poll++)
            {
                await Task.Delay(750,ct);
                var state=await InspectNameEditorAsync(session,ct);
                if(state is NameEditorState.Ready or NameEditorState.Cooldown)
                {
                    log(state==NameEditorState.Ready
                        ? "PROFILE_NAME_EDITOR_READY: đúng màn hình Name; ô nhập hw7/lfn/hvc."
                        : "Tên hiển thị: đã mở màn hình Name nhưng TikTok đang giới hạn sửa tên.");
                    return state;
                }
                if(state==NameEditorState.WrongEditor)
                {
                    log("Tên hiển thị: click dẫn tới trình sửa khác Name; quay lại, không nhập dữ liệu.");
                    await session.BackAsync(ct);
                    if(!await ReturnToEditAsync(session,ct))
                    {
                        log("Tên hiển thị: không về được Edit profile sau trang khác; dừng để tránh sửa Username.");
                        return NameEditorState.NotOpened;
                    }
                    break;
                }
            }
            // If the tap opened an unrecognized screen (no lfn/hvc), return
            // before considering another candidate. No speculative typing.
            if(!await ReturnToEditAsync(session,ct))
            {
                log("PROFILE_NAME_NAVIGATION_STOP: sau click không trở về được Edit profile.");
                return NameEditorState.NotOpened;
            }
        }
        return NameEditorState.NotOpened;
    }

    // Clearing TikTok app data revokes its access to the media library.
    // Recognize ONLY a photo/media permission prompt: the same generic Android
    // permission IDs can also appear for contacts, microphone and notifications.
    static bool IsPhotoMediaPermissionPrompt(string xml)
    {
        string ui=System.Net.WebUtility.HtmlDecode(xml);
        bool media=Regex.IsMatch(ui,
            @"(?i)\b(photos?|pictures?|images?|videos?|media|gallery)\b|ảnh|hình ảnh|thư viện|bộ sưu tập");
        bool fromPermissionController=ui.Contains("permissioncontroller",StringComparison.OrdinalIgnoreCase);
        bool asksForPermission=Regex.IsMatch(ui,
            @"(?i)\b(allow|access|permission|select photos|choose photos)\b|cho phép|truy cập|cấp quyền");
        bool mentionsTikTok=ui.Contains("TikTok",StringComparison.OrdinalIgnoreCase) ||
            ui.Contains("com.ss.android.ugc.trill",StringComparison.OrdinalIgnoreCase);
        // The screen may contain text from the app underneath a system dialog;
        // explicitly exclude unrelated requests even if 'Gallery' is underneath.
        bool unrelatedPermission=Regex.IsMatch(ui,
            @"(?i)allow\s+tiktok\s+to\s+(send\s+you\s+notifications|access\s+(your\s+)?contacts)|cho\s+phép\s+tiktok\s+(gửi\s+thông\s+báo|truy\s+cập\s+danh\s+bạ)");
        if(unrelatedPermission)return false;
        // Neither a generic 'Allow' button nor the word 'Gallery' alone proves
        // that it is safe to grant this permission.
        return media && asksForPermission && (fromPermissionController || mentionsTikTok);
    }

    static readonly UiLocator[] PhotoMediaPermissionAllowButtons =
    {
        // Android 14/15/16: choose full photo access only if the user enabled
        // automatic avatar upload. Never choose limited-image selection blindly.
        new("id","com.android.permissioncontroller:id/permission_allow_all_button"),
        new("id","com.android.permissioncontroller:id/permission_allow_button"),
        new("xpath","//android.widget.Button[@text='Allow all photos']"),
        new("xpath","//android.widget.Button[@text='Allow all photos and videos']"),
        new("xpath","//android.widget.Button[@text='Allow access to all photos']"),
        new("xpath","//android.widget.Button[contains(@text,'Allow all') or contains(@content-desc,'Allow all')]"),
        new("xpath","//android.widget.Button[@text='Cho phép truy cập tất cả ảnh']"),
        new("xpath","//android.widget.Button[@text='Allow']"),
        new("xpath","//android.widget.Button[@text='Cho phép']"),
    };

    // Gallery access is handled BEFORE attempting to match l3z/l37 thumbnails.
    // A permission sheet overlays those elements and previously produced
    // misleading logs: "0 thumbnail" in every scan after pm clear.
    async Task<bool> EnsureGalleryPhotoAccessAsync(AppiumSession session,CancellationToken ct)
    {
        int consentClicks=0;
        bool uploadRetried=false;
        for(int check=1;check<=12;check++)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(750,ct);
            string page;
            try { page=await session.SourceAsync(ct); }
            catch(InvalidOperationException e)
            {
                if(check>=12)throw new InvalidOperationException(
                    "Không đọc được UI Gallery sau khi xin quyền ảnh: "+e.Message,e);
                continue; // PermissionController sometimes interrupts Appium source.
            }
            if(IsPhotoMediaPermissionPrompt(page))
            {
                if(consentClicks>=2)
                {
                    log("Ảnh: vẫn thấy yêu cầu quyền ảnh sau hai lần cho phép; dừng để kiểm tra.");
                    return false;
                }
                log("Ảnh: phát hiện hộp thoại yêu cầu quyền truy cập ảnh/video sau khi reset TikTok.");
                if(!await session.ClickVisibleAnyAsync(PhotoMediaPermissionAllowButtons,ct))
                {
                    log("Ảnh: chưa thấy nút Allow/Allow all photos hợp lệ; không nhấn nút khác hoặc chọn ngẫu nhiên.");
                    return false;
                }
                consentClicks++;
                log("Ảnh: đã chọn Cho phép truy cập thư viện ảnh; chờ Gallery hiển thị thumbnail.");
                await Task.Delay(1100,ct);
                continue;
            }
            if(await session.ExistsAnyAsync(selectors.Get("profileGalleryItems"),ct))
            {
                if(consentClicks>0)log("Ảnh: Gallery đã tải sau khi cấp quyền; bắt đầu so khớp thumbnail.");
                return true;
            }
            // Some TikTok builds close Upload photo rather than opening Gallery
            // immediately after the user grants media access. Re-open ONCE only,
            // when the Upload photo menu is positively identified.
            if(consentClicks>0 && check>=5 && !uploadRetried &&
               await session.ExistsAnyAsync(selectors.Get("profilePhotoUpload"),ct))
            {
                uploadRetried=true;
                if(await session.ClickVisibleAnyAsync(selectors.Get("profilePhotoUpload"),ct))
                    log("Ảnh: sau cấp quyền TikTok quay về Upload photo; mở lại Gallery một lần.");
                continue;
            }
        }
        log("Ảnh: không còn thấy hộp thoại quyền ảnh, nhưng vẫn không tìm thấy nút chọn ảnh l3z/l37. " +
            "Có thể Gallery chưa tải hoặc selector đã đổi; cần ảnh/Source của màn hình hiện tại.");
        return false;
    }

    // Exit a cancelled photo picker without committing any public operation.
    async Task LeavePhotoPickerAsync(AppiumSession session,CancellationToken ct)
    {
        for(int i=0;i<4;i++)
        {
            if(await session.ExistsAnyAsync(selectors.Get("profileBioEntry"),ct))return;
            await session.BackAsync(ct);
            await Task.Delay(500,ct);
        }
    }

    // Reconcile a potentially completed avatar save by observation only.
    // TikTok draws circular avatars and may overlay UI on top of the image,
    // resulting in a lower similarity than the full square Gallery thumbnail.
    // A strong score >= 0.88 needs two consecutive readings. A moderate
    // score >= 0.80 needs THREE stable readings (spread <= 0.05).
    // Neither path clicks Save & post; an uncertain result remains SAVE_PENDING.
    async Task<(bool Verified,double Best)> VerifyPhotoCommitReadOnlyAsync(AppiumSession session,string prepared,
        CancellationToken ct)
    {
        const double strong=0.88;
        const double circular=0.80;
        double best=0;
        var recent=new List<double>(3);
        for(int attempt=1;attempt<=7;attempt++)
        {
            ct.ThrowIfCancellationRequested();
            // Inspect the actual avatar on Profile, not the Edit Profile camera overlay.
            if(await session.ExistsAnyAsync(selectors.Get("profileBioEntry"),ct))
            {
                if(!await session.ClickAnyAsync(selectors.Get("profileEditorBack"),ct))
                    await session.BackAsync(ct);
            }
            await Task.Delay(1300,ct);
            bool nodeFound=await session.ExistsAnyAsync(selectors.Get("profilePhotoNode"),ct);
            double score=nodeFound
                ? await GalleryPhotoMatcher.VerifyAvatarAsync(session,prepared,
                    selectors.Get("profilePhotoNode"),ct)
                : 0;
            best=Math.Max(best,score);
            if(score>=circular)
            {
                recent.Add(score);
                if(recent.Count>3)recent.RemoveAt(0);
            }
            else recent.Clear();
            bool strongPair=recent.Count>=2 &&
                recent[recent.Count-1]>=strong && recent[recent.Count-2]>=strong;
            bool stableTriple=recent.Count>=3 &&
                recent.Min()>=circular && recent.Max()-recent.Min()<=0.05;
            log($"Ảnh: đọc avatar {attempt}/7; node={(nodeFound ? "yes" : "no")}; " +
                $"điểm={score:F3}; số lần khớp liên tiếp={recent.Count}; " +
                $"mức xác minh={(strongPair || stableTriple ? "đủ" : "chưa đủ")}." );
            if(strongPair || stableTriple)return (true,best);
        }
        return (false,best);
    }

    // After Name is VERIFIED, compare the username on the SAME Edit profile
    // page, twice. Never infer account identity from display name alone.
    // Preflight and PrepareEditProfileAsync must have already verified the
    // expected username before any Name change. This is not a preflight repair.
    async Task<(bool Ok,string Detail)> SyncHandleAfterNameAsync(
        AppiumSession session,DeviceRecord device,ManagedAccount account,
        AccountVault vault,ProfileUpdateLedger ledger,CancellationToken ct)
    {
        var record=ledger.Snapshot(account.Id);
        if(!record.NameVerified || record.NameChangeState!="VERIFIED" ||
           string.IsNullOrWhiteSpace(record.Name))
        {
            log("PROFILE_HANDLE_SYNC_SKIPPED: tên chưa được xác minh lưu; giữ nguyên username trong kho.");
            return (true,"");
        }
        if(!await ReturnToEditAsync(session,ct))
            return (false,"PROFILE_HANDLE_SYNC_NO_EDIT: chưa về trang Edit profile để đọc Username.");
        string oldHandle=TikTokStateDetector.NormalizeHandle(account.ExpectedTikTokHandle);
        string? stableHandle=null;
        for(int attempt=1;attempt<=2;attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var xml=await session.SourceAsync(ct);
            if(!SourceIsEditProfile(xml) ||
               TikTokStateDetector.ContainsProtectionPrompt(xml))
                return (false,"PROFILE_HANDLE_SYNC_UNVERIFIED: không ở Edit profile hoặc có yêu cầu bảo mật.");
            // Legacy Edit profile has no mud nodes; retain the old navigation
            // and expected handle instead of turning compatibility into failure.
            if(!xml.Contains("com.ss.android.ugc.trill:id/mud",StringComparison.Ordinal))
            {
                log("PROFILE_HANDLE_SYNC_LEGACY: giao diện cũ không có mud[2]; giữ username đã xác minh ở Profile.");
                return (true,"");
            }
            if(!TikTokStateDetector.TryReadNewEditProfileUsername(xml,out string current))
                return (false,"PROFILE_HANDLE_SYNC_UNVERIFIED: giao diện mới có mud nhưng chưa đọc được mud[2].");
            if(stableHandle is not null &&
               !string.Equals(stableHandle,current,StringComparison.OrdinalIgnoreCase))
                return (false,"PROFILE_HANDLE_SYNC_UNSTABLE: hai lần đọc username không giống nhau; không cập nhật kho.");
            stableHandle=current;
            if(attempt==1)await Task.Delay(500,ct);
        }
        // Same visible Edit profile with the verified Name is an additional
        // consistency guard against a navigation/account switch between reads.
        var shownName=await ReadDisplayedNameAsync(session,selectors,ct);
        if(!string.Equals(shownName,record.Name,StringComparison.Ordinal))
            return (false,"PROFILE_HANDLE_SYNC_NAME_MISMATCH: Name đang hiển thị khác giá trị đã lưu.");
        if(stableHandle==oldHandle)
        {
            log($"PROFILE_HANDLE_UNCHANGED: @{oldHandle}; kho giữ nguyên.");
            return (true,"");
        }
        var result=vault.SyncHandleAfterVerifiedName(account.Id,device.Serial,oldHandle,stableHandle!);
        if(!result.Changed)
            return (false,result.Reason+" Không sửa dữ liệu tài khoản.");
        // Match the immutable account GUID. Preserve Name/Bio/Photo history
        // while updating the username label used by that per-account ledger.
        account.ExpectedTikTokHandle=stableHandle!;
        ledger.Mark(account.Id,stableHandle!,_=>{});
        log($"PROFILE_HANDLE_UPDATED: @{oldHandle} -> @{stableHandle}; " +
            "đã lưu vào kho mã hóa của đúng account, giữ nguyên email/mật khẩu/trạng thái.");
        return (true,"");
    }

    public async Task<ProfileUpdateResult> ExecuteAsync(AppiumSession session,AdbClient adb,
        DeviceRecord device,ManagedAccount account,ProfileUpdateOptions options,ProfileUpdateLedger ledger,AccountVault vault,
        CancellationToken ct)
    {
        if(!options.Enabled)return new(true,"Cập nhật hồ sơ đang tắt.");
        bool nameDeferred=false;
        var record=ledger.Snapshot(account.Id);
        if(record.TikTokHandle.Length>0 && record.TikTokHandle!=TikTokStateDetector.NormalizeHandle(account.ExpectedTikTokHandle))
        {
            // Profile-update records are keyed by the immutable account GUID, but
            // their handle label can lag behind a manual correction in the vault.
            // NEVER discard Name/Bio/Photo journals merely because the label is old.
            string oldHandle=TikTokStateDetector.NormalizeHandle(record.TikTokHandle);
            string newHandle=TikTokStateDetector.NormalizeHandle(account.ExpectedTikTokHandle);
            var accounts=vault.Snapshot();
            var stored=accounts.FirstOrDefault(a=>a.Id==account.Id);
            bool sameAccount=stored is not null &&
                string.Equals(stored.DeviceSerial,device.Serial,StringComparison.OrdinalIgnoreCase) &&
                TikTokStateDetector.NormalizeHandle(stored.ExpectedTikTokHandle)==newHandle &&
                (stored.State is "RESERVED" or "ACTIVE");
            bool uniqueHandle=!accounts.Any(a=>a.Id!=account.Id &&
                TikTokStateDetector.NormalizeHandle(a.ExpectedTikTokHandle)==newHandle);
            // A verified automatic rename has durable evidence inside the encrypted vault.
            bool automaticRenameEvidence=sameAccount && uniqueHandle &&
                TikTokStateDetector.NormalizeHandle(stored!.PreviousVerifiedTikTokHandle)==oldHandle &&
                stored.VerifiedHandleChangedAt is not null;
            if(automaticRenameEvidence)
            {
                ledger.Mark(account.Id,newHandle,_=>{});
                log($"PROFILE_HANDLE_LEDGER_RECOVERED: @{oldHandle} -> @{newHandle}; " +
                    "lịch sử của cùng accountId được giữ nguyên sau lần đồng bộ handle đã xác minh.");
            }
            else
            {
                // When an operator corrected the handle in the vault, the old
                // PreviousVerifiedTikTokHandle field was intentionally cleared.
                // Re-bind the label ONLY after preflight identified the expected
                // account AND the current Edit profile reproduces BOTH values
                // previously verified for this same account GUID. A matching
                // display name alone is never enough to authorize a repair.
                log($"PROFILE_HISTORY_HANDLE_MISMATCH: lịch sử=@{oldHandle}; " +
                    $"kho=@{newHandle}; kiểm tra lại Name/Bio trước khi tiếp tục ảnh.");
                if(!sameAccount || !uniqueHandle ||
                    !record.NameVerified || !record.BioVerified ||
                    string.IsNullOrWhiteSpace(record.Name) ||
                    string.IsNullOrWhiteSpace(record.Bio))
                    return new(false,"PROFILE_HISTORY_IDENTITY_UNVERIFIED: chưa đủ bằng chứng " +
                        "cùng accountId, thiết bị và Name/Bio đã xác minh; giữ nguyên lịch sử, không sửa hồ sơ.");

                var checkEditor=await PrepareEditProfileAsync(session,account,ct);
                if(!checkEditor.Ready)
                    return new(false,"PROFILE_HISTORY_SCREEN_UNVERIFIED: "+checkEditor.Detail,Retryable:true);
                var first=await session.SourceAsync(ct);
                if(!SourceIsEditProfile(first) ||
                   TikTokStateDetector.ContainsProtectionPrompt(first) ||
                   !TikTokStateDetector.TryReadNewEditProfileUsername(first,out string liveHandle) ||
                   !string.Equals(liveHandle,newHandle,StringComparison.OrdinalIgnoreCase))
                    return new(false,"PROFILE_HISTORY_SCREEN_UNVERIFIED: " +
                        "không đọc đúng username mới trên Edit profile; không nối lịch sử.",Retryable:true);

                string displayedName=await ReadDisplayedNameAsync(session,selectors,ct);
                string displayedBio=await WaitDisplayedBioAsync(session,record.Bio,ct);
                var second=await session.SourceAsync(ct);
                bool handleStable=SourceIsEditProfile(second) &&
                    !TikTokStateDetector.ContainsProtectionPrompt(second) &&
                    TikTokStateDetector.TryReadNewEditProfileUsername(second,out string again) &&
                    string.Equals(again,liveHandle,StringComparison.OrdinalIgnoreCase);
                bool nameMatches=string.Equals(displayedName,record.Name,StringComparison.Ordinal);
                bool bioMatches=string.Equals(displayedBio,record.Bio,StringComparison.Ordinal);
                log($"PROFILE_HISTORY_SCREEN_CHECK: usernameStable={handleStable}; " +
                    $"nameMatch={nameMatches}; bioMatch={bioMatches}; " +
                    $"previousNameVerified={record.NameVerified}; previousBioVerified={record.BioVerified}.");
                if(!handleStable || !nameMatches || !bioMatches)
                    return new(false,"PROFILE_HISTORY_VALUES_MISMATCH: Name/Bio hiện tại không trùng " +
                        "lịch sử đã xác minh; không cập nhật nhãn lịch sử và không sửa nhầm tài khoản.");
                // Update the old handle LABEL on the same account GUID, not the
                // actual username in the vault. Preserve every progress flag,
                // reserved photo hash, save-pending and cooldown journal.
                ledger.Mark(account.Id,newHandle,_=>{});
                log($"PROFILE_HISTORY_REBOUND: @{oldHandle} -> @{newHandle}; " +
                    "đã đối chiếu Name/Bio trên cùng Edit profile; giữ nguyên tiến độ và ảnh đã dự trữ.");
            }
            record=ledger.Snapshot(account.Id);
        }
        var editor=await PrepareEditProfileAsync(session,account,ct);
        if(!editor.Ready)
            return new(false,editor.Detail,Retryable:true);
        if(options.ChangeName)
        {
            if(record.NameVerified || record.NameChangeState=="VERIFIED")
            {
                log("Tên hiển thị: đã xác minh ở phiên trước; bỏ qua đổi tên.");
            }
            else if(record.NameChangeState is "SAVE_PENDING" or "COOLDOWN")
            {
                // Important: after Save, TikTok can immediately disable nickname editing.
                // Verify using the read-only Edit Profile row, NOT by opening nickname editor.
                string actual=await ReadDisplayedNameAsync(session,selectors,ct);
                if(record.Name.Length>0 && actual.Equals(record.Name,StringComparison.Ordinal))
                {
                    ledger.Mark(account.Id,account.ExpectedTikTokHandle,r=>
                    {
                        r.NameVerified=true;r.NameChangeState="VERIFIED";r.NameObserved=actual;
                    });
                    log("Tên hiển thị: đã đối chiếu thành công giá trị lưu từ phiên trước; không đổi lại.");
                }
                else
                {
                    var until=record.NameEligibleAt.HasValue
                        ? " (TikTok dự kiến mở lại sau "+record.NameEligibleAt.Value.ToString("yyyy-MM-dd")+")" : "";
                    nameDeferred=true;
                    log("Tên hiển thị: " + (record.NameChangeState=="COOLDOWN"
                        ? "TikTok đang giới hạn thay đổi 7 ngày"
                        : "Save lần trước đang chờ xác minh") + until +
                        "; bỏ qua gửi Save, tiếp tục các mục hồ sơ khác.");
                }
            }
            else
            {
                string name=string.IsNullOrWhiteSpace(record.Name)
                    ? Choose(Lines(options.Names),options.RandomName,account.Id):record.Name;
                var shown=await ReadDisplayedNameAsync(session,selectors,ct);
                if(shown.Equals(name,StringComparison.Ordinal))
                {
                    ledger.Mark(account.Id,account.ExpectedTikTokHandle,r=>
                    {
                        r.Name=name;r.NameObserved=shown;r.NameVerified=true;
                        r.NameChangeState="VERIFIED";
                    });
                    log("Tên hiển thị đã trùng cấu hình trên Edit Profile; ghi nhận, không gửi Save.");
                }
                else
                {
                    var nameScreen=await OpenNameEditorAsync(session,ct);
                    if(nameScreen==NameEditorState.NotOpened)
                        return new(false,"Đang ở Edit Profile nhưng không mở được màn hình sửa Name; " +
                            "đã thử các locator và xác minh sau click, không gửi Save. " +
                            "Hãy lấy UI XML của hàng Name để chỉnh selector.",Retryable:true);
                    // A locked nickname screen may have no editable input at all.
                    // Recognize it as COOLDOWN, not a failed input action.
                    var editorSource=await session.SourceAsync(ct);
                    if(nameScreen==NameEditorState.Cooldown || IsNameCooldown(editorSource))
                    {
                        var eligible=EstimateNameEligibleAt(editorSource);
                        ledger.Mark(account.Id,account.ExpectedTikTokHandle,r=>
                        {
                            r.NameObserved=shown;
                            r.NameChangeState="COOLDOWN";
                            r.NameEligibleAt=eligible;
                        });
                        log("Tên hiển thị: TikTok khóa thay đổi 7 ngày, ghi lại mốc và không thử Save. " +
                            "Tên hiện tại: " + (string.IsNullOrWhiteSpace(shown)?"chưa đọc được":shown));
                        nameDeferred=true;
                        if((options.ChangeBio || options.ChangePhoto) && !await ReturnToEditAsync(session,ct))
                            return new(false,"Đã phát hiện giới hạn đổi tên nhưng chưa quay được về màn hình Edit.",Retryable:true);
                    }
                    else
                    {
                        if(nameScreen!=NameEditorState.Ready ||
                           !await WaitVisible(session,"profileNameEditorInput",8,ct))
                            return new(false,"Không xác minh được ô nhập của màn hình Name; không gửi Save.",Retryable:true);
                        if(await session.ExistsAnyAsync(selectors.Get("profileNameClear"),ct))
                            await session.ClickAnyAsync(selectors.Get("profileNameClear"),ct);
                        await session.ClearAnyAsync(selectors.Get("profileNameEditorInput"),ct);
                        if(!await session.SetTextAnyAsync(selectors.Get("profileNameEditorInput"),name,ct) ||
                            !await CheckInput(session,"profileNameEditorInput",name,ct))
                            return new(false,"Chưa nhập đúng tên mới; không gửi Save.");
                        log("PROFILE_NAME_INPUT_VERIFIED: văn bản trong hw7/lfn/hvc khớp giá trị cấu hình.");
                        // Write-ahead journal BEFORE submitting the action. If the process dies
                        // after Save, the next run must reconcile, not repeat the name change.
                        ledger.Mark(account.Id,account.ExpectedTikTokHandle,r=>
                        {
                            r.Name=name;r.NameAttemptedAt=DateTimeOffset.Now;
                            r.NameChangeState="SAVE_PENDING";r.NameVerified=false;
                        });
                        if(!await WaitTap(session,"profileEditorSave",12,ct))
                            return new(false,"Không nhấn được Save; giữ trạng thái SAVE_PENDING để tránh gửi lại không chắc chắn.",Retryable:true);
                        if(await WaitVisible(session,"profileNameConfirm",6,ct) &&
                            !await WaitTap(session,"profileNameConfirm",5,ct))
                            return new(false,"Không xác nhận được Save; giữ trạng thái SAVE_PENDING.",Retryable:true);
                        await Task.Delay(1100,ct);
                        if(!await ReturnToEditAsync(session,ct))
                            return new(false,"Đã gửi Save tên nhưng chưa quay được về Edit; không được đổi lại khi Start.",Retryable:true);
                        string actual=await ReadDisplayedNameAsync(session,selectors,ct);
                        if(actual.Equals(name,StringComparison.Ordinal))
                        {
                            ledger.Mark(account.Id,account.ExpectedTikTokHandle,r=>
                            {
                                r.NameVerified=true;r.NameChangeState="VERIFIED";r.NameObserved=actual;
                            });
                            log("Tên hiển thị: đã lưu và xác minh từ hàng Name trên Edit Profile.");
                        }
                        else
                        {
                            var page=await session.SourceAsync(ct);
                            if(IsNameCooldown(page))
                            {
                                ledger.Mark(account.Id,account.ExpectedTikTokHandle,r=>
                                {
                                    r.NameChangeState="COOLDOWN";r.NameEligibleAt=EstimateNameEligibleAt(page);
                                    r.NameObserved=actual;
                                });
                            }
                            nameDeferred=true;
                            log("Tên hiển thị: đã gửi Save nhưng chưa đối chiếu được giá trị; " +
                                "ghi SAVE_PENDING/COOLDOWN và không thử đổi lại ở phiên sau.");
                        }
                    }
                }
            }
        }
        // Re-check the TikTok username after the Name step, not after Bio/Photo.
        // Never use this path to reassign an account that failed preflight.
        if(options.ChangeName)
        {
            var handleSync=await SyncHandleAfterNameAsync(session,device,account,vault,ledger,ct);
            if(!handleSync.Ok)return new(false,handleSync.Detail,Retryable:true);
        }
        record=ledger.Snapshot(account.Id);
        if(options.ChangeBio && record.BioVerified)
            log("PROFILE_BIO_ALREADY_VERIFIED: Bio đã hoàn thành trong phiên trước; bỏ qua Save lại.");
        if(options.ChangeBio && !record.BioVerified)
        {
            string bio=string.IsNullOrWhiteSpace(record.Bio)
                ? Choose(Lines(options.Bios),options.RandomBio,account.Id) : record.Bio;
            if(bio.Length>80)return new(false,"Bio quá 80 ký tự; hãy rút ngắn trước khi gửi.");
            if(!await ReturnToEditAsync(session,ct))
                return new(false,"Sau bước Name chưa trở lại Edit profile; không click Bio trên màn hình khác.",Retryable:true);
            string currentBio=await WaitDisplayedBioAsync(session,bio,ct);
            if(currentBio.Equals(bio,StringComparison.Ordinal))
            {
                ledger.Mark(account.Id,account.ExpectedTikTokHandle,r=>
                {r.Bio=bio;r.BioVerified=true;r.BioChangeState="VERIFIED";});
                log("Tiểu sử: đã có đúng nội dung trên Edit profile; không Save lại.");
            }
            else if(record.BioChangeState=="SAVE_PENDING")
            {
                log("Tiểu sử: Save cũ chưa xác minh, không tự gửi thêm lần nữa.");
                return new(false,"Bio SAVE_PENDING; chưa thể xác minh nội dung. Không Save lặp.",Retryable:true,Deferred:true);
            }
            else
            {
                if(!await OpenBioEditorAsync(session,ct))
                    return new(false,"Không mở được ô sửa tiểu sử; đã thử nhãn Bio và hàng tvContent, không gửi Save.",Retryable:true);
                if(!await session.ClearAnyAsync(selectors.Get("profileBioEditorInput"),ct))
                    return new(false,"Không xóa được Bio cũ; dừng trước khi nhập dữ liệu.",Retryable:true);
                if(!await session.SetTextAnyAsync(selectors.Get("profileBioEditorInput"),bio,ct) ||
                   !await CheckInput(session,"profileBioEditorInput",bio,ct))
                    return new(false,"Không nhập/xác minh được tiểu sử.",Retryable:true);
                log("PROFILE_BIO_INPUT_VERIFIED: văn bản trong hw7/hvc khớp giá trị cấu hình.");
                // Record the intent BEFORE submitting Save, so a timeout or restart
                // cannot accidentally submit the same edit twice.
                ledger.Mark(account.Id,account.ExpectedTikTokHandle,r=>
                {r.Bio=bio;r.BioVerified=false;r.BioChangeState="SAVE_PENDING";});
                if(!await WaitTap(session,"profileEditorSave",10,ct))
                    return new(false,"Không xác nhận được Save Bio; giữ SAVE_PENDING.",Retryable:true,Deferred:true);
                if(!await ReturnToEditAsync(session,ct))
                    return new(false,"Đã gửi Save Bio nhưng chưa về Edit profile; không gửi lại.",Retryable:true,Deferred:true);
                string saved=await WaitDisplayedBioAsync(session,bio,ct);
                if(!saved.Equals(bio,StringComparison.Ordinal))
                    return new(false,"Đã gửi Save Bio nhưng chưa đọc lại đúng nội dung; giữ SAVE_PENDING.",Retryable:true,Deferred:true);
                ledger.Mark(account.Id,account.ExpectedTikTokHandle,r=>
                {r.Bio=bio;r.BioVerified=true;r.BioChangeState="VERIFIED";});
                log("Tiểu sử: đã lưu và xác minh trên Edit profile.");
            }
        }
        record=ledger.Snapshot(account.Id);
        if(options.ChangePhoto && !record.PhotoVerified)
            log($"PROFILE_PHOTO_RESUME: NameVerified={record.NameVerified}; " +
                $"BioVerified={record.BioVerified}; PhotoState={record.PhotoChangeState}; " +
                "tiếp tục ảnh cũ, không đổi lại Name/Bio.");
        if(options.ChangePhoto && !record.PhotoVerified)
        {
            string path=ledger.ReserveUniquePhoto(account.Id,options.PhotoFolder);
            string prepared=ProfilePhotoPreparer.PrepareJpeg(path,account.Id);
            if(record.PhotoChangeState=="SAVE_PENDING")
            {
                // The outcome of a previous save attempt is uncertain. Never submit again.
                // Inspect only the current avatar, without any Save/Post action.
                var verified=await VerifyPhotoCommitReadOnlyAsync(session,prepared,ct);
                log($"Ảnh: phiên trước có SAVE_PENDING; chỉ đọc avatar, điểm cao nhất={verified.Best:F3}." +
                    (verified.Verified ? " Đã xác minh từ nhiều lần đọc ổn định." :
                        " Chưa có đủ bằng chứng ảnh đã lưu; không gửi lại Save & post."));
                if(verified.Verified)
                {
                    ledger.Mark(account.Id,account.ExpectedTikTokHandle,r=>
                    {r.PhotoVerified=true;r.PhotoChangeState="VERIFIED";});
                    return new(true,"Đã xác minh ảnh đại diện từ lần Save trước.");
                }
                return new(false,"Ảnh từng có thao tác Save & post nhưng chưa xác minh được kết quả; không đăng lại.",Retryable:true,Deferred:true);
            }
            string remote="/sdcard/Pictures/TikTokProfileAssets/"+account.Id.ToString("N")+".jpg";
            await adb.RunAsync(ct,"-s",device.Serial,"shell","mkdir","-p","/sdcard/Pictures/TikTokProfileAssets");
            await adb.RunAsync(ct,"-s",device.Serial,"push",prepared,remote);
            try { await adb.RunAsync(ct,"-s",device.Serial,"shell","am","broadcast","-a",
                "android.intent.action.MEDIA_SCANNER_SCAN_FILE","-d","file://"+remote); }
            catch(InvalidOperationException) { log("Ảnh đã đẩy qua ADB nhưng Media Scanner chưa xác nhận; cần kiểm tra thư viện."); }

            // When the Gallery UI does not expose filenames, identify the target
            // photo by comparing the actual visible thumbnails to the prepared
            // image. XPath index [1]/[2] is never sufficient proof.
            if(!await WaitVisible(session,"profilePhotoCamera",10,ct))
            {
                // Bio flow might have returned to Profile instead of Edit Profile.
                if(!(await session.ClickAnyAsync(selectors.Get("profileEditButton"),ct) ||
                     await session.ClickAnyAsync(selectors.Get("profileEditEntry"),ct)) ||
                   !await WaitVisible(session,"profilePhotoCamera",12,ct))
                    return new(false,"Không tìm thấy ảnh trên Edit Profile; cần xác minh giao diện.",Retryable:true);
            }
            if(!await WaitTap(session,"profilePhotoCamera",12,ct) ||
               !await WaitTap(session,"profilePhotoUpload",12,ct))
                return new(false,"Không mở được TikTok Upload photo.",Retryable:true);
            log("Ảnh: đã yêu cầu mở Gallery; kiểm tra quyền truy cập ảnh trước khi tìm thumbnail.");
            if(!await EnsureGalleryPhotoAccessAsync(session,ct))
            {
                ledger.Mark(account.Id,account.ExpectedTikTokHandle,
                    r=>r.PhotoChangeState="GALLERY_REVIEW");
                log("Ảnh: Gallery chưa sẵn sàng sau kiểm tra quyền; giữ nguyên tên/Bio và ảnh, không chọn ngẫu nhiên.");
                await LeavePhotoPickerAsync(session,ct);
                return new(false,"Chưa mở được thư viện ảnh sau khi kiểm tra quyền; cần xem UI thực tế.",
                    Retryable:true,Deferred:true);
            }
            log("Ảnh: Gallery sẵn sàng; đối chiếu thumbnail với JPEG vừa chuyển lên máy.");
            GalleryPhotoMatcher.MatchResult? result=null;
            for(int page=0;page<4;page++)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(1400,ct);
                result=await GalleryPhotoMatcher.FindAsync(session,prepared,
                    selectors.Get("profileGalleryItems"),ct);
                log($"Ảnh: Gallery lượt {page+1}/4; tìm thấy {result.VisibleCount} phần tử; " +
                    $"so khớp {result.ComparedCount}; điểm tốt nhất={result.Best:F3}, thứ hai={result.RunnerUp:F3}. " +
                    result.Reason);
                if(result.Match is not null)break;
                // Zero matched rectangles is a geometry/selector problem, not
                // evidence that we should swipe away from the newest upload.
                if(result.ComparedCount==0)
                {
                    log("Ảnh: lỗi nhận diện thumbnail: " +result.Reason);
                    if(page>=2)break; // allow gallery a few seconds to finish loading
                    continue; // retry same page; don't move away from newest items
                }
                if(page<3)await session.SwipeGalleryUpAsync(ct);
            }
            if(result?.Match is null)
            {
                ledger.Mark(account.Id,account.ExpectedTikTokHandle,r=>r.PhotoChangeState="GALLERY_REVIEW");
                log("Ảnh: chưa nhận diện duy nhất thumbnail; " +(result?.Reason??"Không có dữ liệu Gallery.") );
                log("Ảnh: giữ nguyên ảnh đã gán và bỏ qua thay ảnh, không click vị trí đoán.");
                await LeavePhotoPickerAsync(session,ct);
                return new(false,"Tên/Bio đã lưu; ảnh chưa thể đối chiếu trong Gallery. Chưa thể kết luận hồ sơ hoàn tất.",Retryable:true,Deferred:true);
            }
            // The returned id was matched to the same screenshot in which its
            // geometry was read. If Appium says stale, never fall back to [1].
            try { await session.ClickElementAsync(result.Match.ElementId,ct); }
            catch(InvalidOperationException)
            {
                return new(false,"Thumbnail đã thay đổi trước khi click; thử lại sau khi Gallery ổn định.",Retryable:true);
            }
            log("Ảnh: đã chọn thumbnail khớp hình ảnh; tiếp tục màn hình Next/Confirm.");
            if(!await WaitTap(session,"profilePhotoNext",18,ct))
                return new(false,"Đã chọn ảnh nhưng không thấy nút Next; chưa gửi Save.",Retryable:true);
            if(!await WaitTap(session,"profilePhotoConfirm",18,ct))
                return new(false,"Đã chọn ảnh nhưng không thấy Confirm; chưa gửi Save.",Retryable:true);
            if(!options.AllowPhotoSaveAndPost)
            {
                ledger.Mark(account.Id,account.ExpectedTikTokHandle,r=>r.PhotoChangeState="GALLERY_REVIEW");
                await LeavePhotoPickerAsync(session,ct);
                return new(false,"Ảnh đã được chọn nhưng Save & post đang tắt trong cấu hình; chưa hoàn tất hồ sơ.",Retryable:true,Deferred:true);
            }
            // Durable journal BEFORE a potentially public irreversible action.
            ledger.Mark(account.Id,account.ExpectedTikTokHandle,r=>
            {
                r.PhotoAttemptedAt=DateTimeOffset.Now;
                r.PhotoChangeState="SAVE_PENDING";
            });
            // A WebDriver click may be reported as failed even after TikTok has
            // accepted it. Never press the irreversible button a second time.
            bool tapAcknowledged=await WaitTap(session,"profilePhotoSavePost",15,ct);
            log(tapAcknowledged
                ? "Ảnh: Appium xác nhận thao tác Save & post; đang kiểm tra avatar thực tế."
                : "Ảnh: Appium không xác nhận nút Save & post; có thể TikTok đã lưu. " +
                  "Chỉ kiểm tra avatar; không gửi Save & post lần nữa.");
            var photoScore=await VerifyPhotoCommitReadOnlyAsync(session,prepared,ct);
            log($"Ảnh: điểm avatar sau thao tác lưu (đọc lại) = {photoScore.Best:F3}; " +
                $"xác minh ổn định={(photoScore.Verified ? "yes" : "no")}.");
            if(photoScore.Verified)
            {
                ledger.Mark(account.Id,account.ExpectedTikTokHandle,r=>
                {r.PhotoVerified=true;r.PhotoChangeState="VERIFIED";});
                return new(true,"Đã xác minh ảnh đại diện mới trên Profile.");
            }
            return new(false,
                "Ảnh có thể đã lưu nhưng chưa xác minh được avatar ổn định trên Profile; " +
                "giữ SAVE_PENDING và không gửi Save & post lại. Cần kiểm tra thủ công nếu UI không cho đọc avatar.",
                Retryable:true,Deferred:true);
        }
        return nameDeferred
            ? new(true,"Đã xử lý các mục khả dụng; riêng tên đang chờ xác minh/giới hạn 7 ngày, không gửi lại Save.",Deferred:true)
            : new(true,"Các trường đã bật được kiểm tra lại trong màn hình Edit Profile.");
    }
}
