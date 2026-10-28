using System.Text.Json;

namespace AndroidMultiDeviceV1.Core;

/// <summary>
/// Configuration only. Phase 1 does not start LIVE sessions, dispatch comments,
/// or interact with TikTok. Stored separately from the encrypted account vault.
/// </summary>
public sealed class LiveManagerSettings
{
    public int SchemaVersion { get; set; } = 1;
    public bool KeywordSearchEnabled { get; set; } = true;
    // Dedicated Chrome localhost CDP is opt-in. Defaults preserve Android Search.
    public string LiveSourceMode { get; set; } = "DeviceTikTokSearch";
    [Obsolete("Legacy UI setting; the CDP source uses a dedicated Chrome profile.")]
    public string ChromeWindowTitleContains { get; set; } = "TikTok";
    public int ChromePageLoadTimeoutSeconds { get; set; } = 20;
    public List<string> Keywords { get; set; } = ["live", "liên quân", "ghép đôi"];
    public string KeywordSelectionMode { get; set; } = "LRU";
    public int PerKeywordTimeoutSeconds { get; set; } = 240;
    public int TotalSearchTimeoutSeconds { get; set; } = 600;
    public int CardsPerBatch { get; set; } = 8;
    public int MinimumViewers { get; set; } = 100;
    public int MaximumViewers { get; set; } = 0; // 0 = no upper limit
    public int MaxSearchScrolls { get; set; } = 12;
    // LIVE search ONLY: move about half a card row per drag; never fling between rooms.
    // 30% of 624px ~= 187px versus a typical row height ~= 339px.
    public int SearchSwipePercent { get; set; } = 40; // 1..75% screen height, user-adjustable drag
    public int SearchAfterSwipeWaitMilliseconds { get; set; } = 4500;
    public int SearchPerCardWaitMilliseconds { get; set; } = 650;
    public int MaxLiveSwipeChecks { get; set; } = 8;
    public int SessionDurationSeconds { get; set; } = 120;
    public int RevisitCooldownMinutes { get; set; } = 20;
    [System.Text.Json.Serialization.JsonIgnore]
    public int RoomCooldownMinutes
    {
        get => RevisitCooldownMinutes;
        set => RevisitCooldownMinutes=value;
    }
    public int MaxDevicesPerRoom { get; set; } = 5;
    public int UiReadyTimeoutSeconds { get; set; } = 45;
    public int MinimumReadyWaitSeconds { get; set; } = 5;
    public int ExtraWaitMinSeconds { get; set; } = 0;
    public int ExtraWaitMaxSeconds { get; set; } = 5;
    public int StableUiReadings { get; set; } = 3;
    public int UiPollMilliseconds { get; set; } = 1000;
    public int ActionWaitMinMilliseconds { get; set; } = 999;
    public int ActionWaitMaxMilliseconds { get; set; } = 2111;
    public int RoundRestMinMilliseconds { get; set; } = 1500;
    public int RoundRestMaxMilliseconds { get; set; } = 4000;
    // Phase 5 draft library. Phase 1 only stores/validates it; publishing starts
    // only after Phase 3 has positively accepted the current LIVE room.
    public List<string> CommentDrafts { get; set; } = [];
    public string DraftSuggestionMode { get; set; } = "Shuffle";

    public List<string> Validate()
    {
        var errors = new List<string>();
        if (SchemaVersion != 1) errors.Add("Phiên bản cấu hình chưa được hỗ trợ.");
        if (Keywords is null) { errors.Add("Danh sách từ khóa không hợp lệ."); return errors; }
        if (CommentDrafts is null) { errors.Add("Thư viện nội dung không hợp lệ."); return errors; }
        if (LiveSourceMode is not ("DeviceTikTokSearch" or "ChromeTikTokWeb"))
            errors.Add("Nguồn tìm LIVE không hợp lệ.");
        if (ChromePageLoadTimeoutSeconds is < 5 or > 120)
            errors.Add("Timeout TikTok Web phải từ 5–120 giây.");
        if (KeywordSelectionMode is not ("LRU" or "Random" or "Sequential")) errors.Add("Cách chọn từ khóa không hợp lệ.");
        if (Keywords.Count > 100) errors.Add("Tối đa 100 từ khóa.");
        if (KeywordSearchEnabled && Keywords.Count == 0) errors.Add("Hãy nhập ít nhất một từ khóa hoặc tắt tìm kiếm.");
        if (Keywords.Any(k => string.IsNullOrWhiteSpace(k) || k.Length > 200)) errors.Add("Mỗi từ khóa phải có 1–200 ký tự.");
        if (Keywords.Distinct(StringComparer.OrdinalIgnoreCase).Count() != Keywords.Count) errors.Add("Danh sách từ khóa bị trùng.");
        if (PerKeywordTimeoutSeconds is < 5 or > 3600) errors.Add("Timeout/từ khóa phải từ 5 đến 3600 giây.");
        if (TotalSearchTimeoutSeconds is < 5 or > 7200 || TotalSearchTimeoutSeconds < PerKeywordTimeoutSeconds)
            errors.Add("Timeout toàn đợt phải từ 5–7200 giây và không nhỏ hơn timeout một từ khóa.");
        if (CardsPerBatch is < 1 or > 50) errors.Add("Card mỗi lượt phải từ 1–50.");
        if (MinimumViewers is < 0 or > 100000000 || MaximumViewers is < 0 or > 100000000 ||
            (MaximumViewers != 0 && MaximumViewers < MinimumViewers))
            errors.Add("Giới hạn Viewer không hợp lệ (tối đa bằng 0 nghĩa là không giới hạn).");
        if (SearchSwipePercent is < 1 or > 75)
            errors.Add("Độ dài kéo Search phải từ 1–75% chiều cao màn hình.");
        if (SearchAfterSwipeWaitMilliseconds is < 1200 or > 20000 ||
            SearchPerCardWaitMilliseconds is < 0 or > 5000)
            errors.Add("Thời gian đọc Search không hợp lệ (sau vuốt 1200–20000ms, mỗi card 0–5000ms).");
        if (MaxSearchScrolls is < 1 or > 200 || MaxLiveSwipeChecks is < 1 or > 200)
            errors.Add("Số lượt cuộn/vuốt phải từ 1–200.");
        if (SessionDurationSeconds is < 10 or > 36000) errors.Add("Thời lượng LIVE phải từ 10–36000 giây.");
        if (RoomCooldownMinutes is < 0 or > 10080) errors.Add("Cooldown phòng phải từ 0–10080 phút.");
        if (MaxDevicesPerRoom is < 1 or > 100) errors.Add("Số thiết bị/phòng phải từ 1–100.");
        if (UiReadyTimeoutSeconds is < 5 or > 600) errors.Add("Timeout chờ UI phải từ 5–600 giây.");
        if (MinimumReadyWaitSeconds is < 0 or > 120 || ExtraWaitMinSeconds is < 0 or > 120 ||
            ExtraWaitMaxSeconds < ExtraWaitMinSeconds || ExtraWaitMaxSeconds > 120)
            errors.Add("Khoảng chờ UI không hợp lệ.");
        if (MinimumReadyWaitSeconds > UiReadyTimeoutSeconds)
            errors.Add("Chờ tối thiểu khi vào LIVE không thể lớn hơn timeout UI.");
        if (StableUiReadings is < 1 or > 20 || UiPollMilliseconds is < 200 or > 10000)
            errors.Add("Số lần kiểm tra UI và chu kỳ kiểm tra không hợp lệ.");
        if (ActionWaitMinMilliseconds is < 0 or > 60000 || ActionWaitMaxMilliseconds < ActionWaitMinMilliseconds || ActionWaitMaxMilliseconds > 60000)
            errors.Add("Khoảng chờ thao tác không hợp lệ.");
        if (RoundRestMinMilliseconds is < 0 or > 600000 || RoundRestMaxMilliseconds < RoundRestMinMilliseconds || RoundRestMaxMilliseconds > 600000)
            errors.Add("Khoảng nghỉ giữa vòng không hợp lệ.");
        if (CommentDrafts.Count > 10000 || CommentDrafts.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 500))
            errors.Add("Thư viện tối đa 10.000 dòng, mỗi dòng 1–500 ký tự.");
        if (DraftSuggestionMode is not ("Sequential" or "Random" or "Shuffle"))
            errors.Add("Chế độ gợi ý nội dung không hợp lệ.");
        return errors;
    }
}

public static class LiveManagerSettingsStore
{
    public static string FilePath => Path.Combine(Storage.Folder, "live-manager-settings.json");
    public static LiveManagerSettings Load()
    {
        if (!File.Exists(FilePath)) return new LiveManagerSettings();
        try
        {
            var value = JsonSerializer.Deserialize<LiveManagerSettings>(File.ReadAllText(FilePath), Storage.JsonOptions)
                ?? throw new InvalidDataException("Cấu hình LIVE trống hoặc không hợp lệ.");
            // Keep the saved drag percentage, including short drags.
            // Migrate only the exact V2.1.16 default pair; do not replace user-custom timeouts.
            if(value.PerKeywordTimeoutSeconds==65 && value.TotalSearchTimeoutSeconds==130)
            {
                value.PerKeywordTimeoutSeconds=240;
                value.TotalSearchTimeoutSeconds=600;
            }
            var errors = value.Validate();
            if (errors.Count != 0) throw new InvalidDataException("Cấu hình LIVE không hợp lệ: " + string.Join("; ", errors));
            return value;
        }
        catch (JsonException ex) { throw new InvalidDataException("Không đọc được JSON cấu hình LIVE. Không tự ghi đè file cũ.", ex); }
    }
    public static void Save(LiveManagerSettings value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var errors = value.Validate();
        if (errors.Count > 0) throw new ArgumentException(string.Join(Environment.NewLine, errors));
        Storage.WriteAtomic(FilePath, JsonSerializer.Serialize(value, Storage.JsonOptions));
    }
}
