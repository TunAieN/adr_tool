using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AndroidMultiDeviceV1.Core;

public enum TikTokUiState
{
    LoggedOut, ExpectedAccount, OtherApprovedAccount, DifferentAccount, Unknown,
    GoogleAuthentication, Onboarding, SecurityChallenge, AccountRestricted
}
public sealed record TikTokObservation(TikTokUiState State, string Detail, string? VerifiedHandle = null);
public enum ReconcileOutcome
{
    Ready, NeedsHuman, NeedsReview, Restricted, AccountCheckpoint, AccountInvalid, TechnicalFailure
}
public sealed record ReconcileResult(ReconcileOutcome Outcome, string Detail, bool ReusedExistingSession = false);

public enum SecurityRecoveryOutcome
{
    UiClean, ClearDataReadyForLogin, ChallengeStillPresent, UiNotReady
}
public enum StaleGoogleRecoveryOutcome
{
    TikTokRecovered, ClearDataReadyForLogin, Failed
}

/// <summary>
/// Appium can briefly report launcher3 while Android is starting TikTok.
/// Do not classify the phone based on one early current_package reading.
/// Only an exact TikTok package (Appium) or visible TikTok-owned hierarchy
/// proves that TikTok is on screen. Google/permission dialogs remain separate.
/// </summary>
public static class TikTokForegroundProbe
{
    public const string TikTokPackage = "com.ss.android.ugc.trill";

    public static bool SourceHasTikTokUi(string source)
    {
        // Android UIAutomator/Appium XML reports package= on visible UI nodes.
        // An unrelated app merely mentioning 'TikTok' in its text is not evidence.
        return source.Contains("package=\"" + TikTokPackage + "\"",StringComparison.Ordinal) ||
               source.Contains("resource-id=\"" + TikTokPackage + ":id/",StringComparison.Ordinal);
    }

    /// <summary>
    /// A TikTok-local "Welcome back" account picker is the logged-out entry
    /// screen: remembered accounts are *choices*, not proof that any user is
    /// authenticated. Only accept it when BOTH the sheet title and the
    /// Add another account action are visible in a TikTok-owned hierarchy.
    /// Never choose one of the remembered identities automatically.
    /// </summary>
    /// <summary>
    /// After TikTok logout some Android/cloud builds temporarily display an
    /// editable-looking Profile with @null and stale name/bio. It is neither a
    /// verified account identity NOR proof that logout has completed.
    /// This must never be used to authorize logout of an unapproved account.
    /// </summary>
    public static bool IsGhostProfileAfterLogout(string source)
    {
        if(!SourceHasTikTokUi(source))return false;
        bool nullHandle=source.Contains("text=\"@null\"",StringComparison.OrdinalIgnoreCase) ||
            source.Contains("text=\"null\"",StringComparison.OrdinalIgnoreCase) ||
            source.Contains("content-desc=\"@null\"",StringComparison.OrdinalIgnoreCase);
        bool profileUi=source.Contains("resource-id=\"com.ss.android.ugc.trill:id/t_k\"",StringComparison.Ordinal) ||
            source.Contains("text=\"Edit\"",StringComparison.OrdinalIgnoreCase) ||
            source.Contains("text=\"Edit profile\"",StringComparison.OrdinalIgnoreCase);
        return nullHandle && profileUi;
    }

    public static bool IsLoggedOutWelcomeBack(string source)
    {
        if(!SourceHasTikTokUi(source)) return false;
        try
        {
            var document=XDocument.Parse(source);
            bool welcome=false, another=false;
            foreach(var node in document.Descendants())
            {
                string label=((string?)node.Attribute("text")??"").Trim();
                string desc=((string?)node.Attribute("content-desc")??"").Trim();
                if(label.StartsWith("Welcome back",StringComparison.OrdinalIgnoreCase) ||
                   desc.StartsWith("Welcome back",StringComparison.OrdinalIgnoreCase) ||
                   label.StartsWith("Chào mừng trở lại",StringComparison.OrdinalIgnoreCase) ||
                   desc.StartsWith("Chào mừng trở lại",StringComparison.OrdinalIgnoreCase))
                    welcome=true;
                if(label.Equals("Add another account",StringComparison.OrdinalIgnoreCase) ||
                   desc.Equals("Add another account",StringComparison.OrdinalIgnoreCase) ||
                   label.Equals("Thêm tài khoản khác",StringComparison.OrdinalIgnoreCase) ||
                   desc.Equals("Thêm tài khoản khác",StringComparison.OrdinalIgnoreCase))
                    another=true;
                if(welcome && another)return true;
            }
        }
        catch(System.Xml.XmlException) { /* UI hierarchy still rebuilding; poll again. */ }
        return false;
    }

    /// <summary>
    /// A clean TikTok launch may open the login-method sheet directly after
    /// Profile is tapped, without an intermediate Welcome back or Log in button.
    /// This is positive evidence of a logged-out entry point, NOT a missing
    /// username. Require TikTok-owned UI plus both a Google login method and
    /// the login title / phone-email method so a random feed/video cannot
    /// be mistaken for the sign-in dialog.
    /// </summary>
    public static bool IsTikTokLoginMethodsScreen(string source)
    {
        if(!SourceHasTikTokUi(source))return false;
        try
        {
            var document=XDocument.Parse(source);
            bool title=false, google=false, phoneEmail=false;
            foreach(var node in document.Descendants())
            {
                string label=((string?)node.Attribute("text")??"").Trim();
                string desc=((string?)node.Attribute("content-desc")??"").Trim();
                foreach(string text in new[] {label,desc})
                {
                    if(text.Equals("Log in to TikTok",StringComparison.OrdinalIgnoreCase) ||
                       text.Equals("Đăng nhập vào TikTok",StringComparison.OrdinalIgnoreCase))title=true;
                    if(text.Contains("Continue with Google",StringComparison.OrdinalIgnoreCase) ||
                       text.Contains("Tiếp tục với Google",StringComparison.OrdinalIgnoreCase))google=true;
                    if(text.Contains("Use phone / email / username",StringComparison.OrdinalIgnoreCase) ||
                       text.Contains("Sử dụng số điện thoại",StringComparison.OrdinalIgnoreCase))phoneEmail=true;
                }
                if(google && (title || phoneEmail))return true;
            }
        }
        catch(System.Xml.XmlException) { /* Page source is still rebuilding: retry. */ }
        return false;
    }

    /// <summary>
    /// Android's notification runtime permission may overlay TikTok immediately
    /// after a deliberate pm clear. It is NOT a TikTok login/security screen.
    /// Only deny if both the app name and notification-specific prompt exist,
    /// and the exact Android PermissionController deny button is present.
    /// Do not mistake the contacts/accounts prompt for this one.
    /// </summary>
    public static bool IsTikTokNotificationPrompt(string source)
    {
        if(string.IsNullOrWhiteSpace(source))return false;
        bool appName=source.Contains("TikTok",StringComparison.OrdinalIgnoreCase);
        bool notification=source.Contains("notifications",StringComparison.OrdinalIgnoreCase) ||
            source.Contains("thông báo",StringComparison.OrdinalIgnoreCase);
        bool denyButton=source.Contains("com.android.permissioncontroller:id/permission_deny_button",StringComparison.Ordinal) ||
            source.Contains("com.android.permissioncontroller:id/permission_deny_and_dont_ask_again_button",StringComparison.Ordinal);
        return appName && notification && denyButton;
    }

    /// <summary>
    /// Fast, XML-grounded handler for the TikTok CONTACTS runtime permission.
    /// This can appear well after Google I agree, outside the old 12-second
    /// check. Never touch an unrelated permission, Google chooser or CAPTCHA.
    /// </summary>
    public static bool IsTikTokContactsPrompt(string source)
    {
        if(string.IsNullOrWhiteSpace(source) ||
           !source.Contains("TikTok",StringComparison.OrdinalIgnoreCase) ||
           !(source.Contains("contacts",StringComparison.OrdinalIgnoreCase) ||
             source.Contains("danh bạ",StringComparison.OrdinalIgnoreCase)) ||
           source.Contains("notifications",StringComparison.OrdinalIgnoreCase) ||
           source.Contains("thông báo",StringComparison.OrdinalIgnoreCase))return false;
        return source.Contains("com.android.permissioncontroller:id/permission_deny_button",StringComparison.Ordinal) ||
               source.Contains("com.android.permissioncontroller:id/permission_deny_and_dont_ask_again_button",StringComparison.Ordinal);
    }

    public static async Task<bool> DismissTikTokContactsPromptAsync(
        AppiumSession session,string source,CancellationToken ct,Action<string>? log=null)
    {
        if(!IsTikTokContactsPrompt(source))return false;
        try
        {
            // The prompt and the exact Android PermissionController deny
            // button must BOTH be proven by this single XML snapshot.
            var doc=XDocument.Parse(source);
            var deny=doc.Descendants().FirstOrDefault(node=>
            {
                string id=(string?)node.Attribute("resource-id")??"";
                return id is "com.android.permissioncontroller:id/permission_deny_button" or
                    "com.android.permissioncontroller:id/permission_deny_and_dont_ask_again_button";
            });
            if(deny is null ||
               !TryGetUiBounds(deny,out int left,out int top,out int right,out int bottom) ||
               right<=left || bottom<=top)return false;
            ct.ThrowIfCancellationRequested();
            await session.TapUiPointAsync((left+right)/2,(top+bottom)/2,ct);
            log?.Invoke("FAST_POST_GOOGLE_CONTACTS_DENIED: exactPermissionControllerButton=true; " +
                "awaitNextSnapshot=true; loginIdentityNotYetVerified=true.");
            return true;
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
        catch(Exception ex) when(AppiumFailureClassifier.IsFatalSessionFailure(ex)){throw;}
        catch(Exception ex)
        {
            log?.Invoke($"FAST_POST_GOOGLE_CONTACTS_RETRY: UI transient ({ex.GetType().Name}).");
            return false;
        }
    }

    public static async Task<bool> DismissTikTokNotificationPromptAsync(
        AppiumSession session,string source,CancellationToken ct,Action<string>? log=null)
    {
        if(!IsTikTokNotificationPrompt(source))return false;
        log?.Invoke("Android: thấy hộp thoại TikTok xin quyền thông báo; chọn Don't allow.");
        string resourceId=source.Contains(
            "com.android.permissioncontroller:id/permission_deny_and_dont_ask_again_button",
            StringComparison.Ordinal)
            ? "com.android.permissioncontroller:id/permission_deny_and_dont_ask_again_button"
            : "com.android.permissioncontroller:id/permission_deny_button";
        bool clicked=false;
        try
        {
            string? button=await session.FindAsync(new UiLocator("id",resourceId),ct,2);
            if(button is not null)
            {
                await session.ClickElementAsync(button,ct);
                clicked=true;
            }
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
        catch(Exception ex) when(AppiumFailureClassifier.IsFatalSessionFailure(ex)){throw;}
        catch(Exception ex)
        {
            log?.Invoke($"Android: bỏ qua popup permission chưa ổn định ({ex.GetType().Name}).");
        }
        if(!clicked)
        {
            log?.Invoke("Android: nút Don't allow chưa thể click; đang chờ hệ thống hiển thị đầy đủ.");
            return false;
        }
        await Task.Delay(650,ct);
        string after=await session.SourceAsync(ct);
        if(IsTikTokNotificationPrompt(after))
            log?.Invoke("Android: đã click Don't allow nhưng popup chưa đóng; tiếp tục quan sát, không click liên tục.");
        else
            log?.Invoke("Android: đã từ chối quyền thông báo TikTok; tiếp tục đợi Home/Profile/Login.");
        return true;
    }

    /// <summary>
    /// Only the first TikTok launch after an operator-requested pm clear is
    /// allowed to traverse these setup pages. Never interpret onboarding as
    /// a restriction or security challenge, and never click an unlabelled button
    /// unless the expected screen has been identified independently.
    /// </summary>
    sealed class FirstLaunchProgress
    {
        public bool TermsTapped;
        public bool GenderTapped;
        public bool GenderContinued;
        public bool InterestsSkipped;
        public int TermsAttempts;
        public int GenderContinueAttempts;
        public int InterestsSkipAttempts;
        public DateTimeOffset TermsLastAttempt;
        public DateTimeOffset GenderContinueLastAttempt;
        public DateTimeOffset InterestsSkipLastAttempt;
        public string LastPage = "";
    }

    static bool CanRetry(int attempts,DateTimeOffset last)
        => attempts<2 && (attempts==0 || DateTimeOffset.UtcNow-last>=TimeSpan.FromSeconds(12));

    static IReadOnlyList<UiLocator> Resolve(UiSelectors? configured,string key,UiLocator[] fallback)
    {
        var values=configured?.Get(key);
        return values is { Count: >0 } ? values : fallback;
    }

    static bool HasText(string source,string text)
        => source.Contains(text,StringComparison.OrdinalIgnoreCase);

    static bool IsTermsPage(string source)
        => SourceHasTikTokUi(source) &&
           ((HasText(source,"Terms and Policies") || HasText(source,"Terms & Policies")) ||
            (HasText(source,"Terms of Service") && HasText(source,"Privacy Policy"))) &&
           HasText(source,"Agree and continue");

    static bool IsGenderPage(string source)
        => SourceHasTikTokUi(source) &&
           HasText(source,"Tell us more about yourself") &&
           HasText(source,"Prefer not to say");

    // This screen can reappear AFTER Google OAuth, not just after pm clear.
    // Keep it out of Profile/Back recovery until the setup is completed.
    public static bool IsOnboardingScreen(string source)
        => IsTermsPage(source) || IsGenderPage(source) || IsInterestsPage(source,false);

    static bool IsInterestsPage(string source,bool justContinuedGender)
    {
        if(!SourceHasTikTokUi(source) || !HasText(source,"Skip")) return false;
        // Different TikTok builds use different wording for the interests step.
        // The explicit transition from Gender -> Continue is additional proof.
        return HasText(source,"interest") || HasText(source,"interests") ||
               HasText(source,"topics") || HasText(source,"What do you like") ||
               HasText(source,"Choose what you") || justContinuedGender;
    }

    static readonly UiLocator[] TermsContinue =
    [
        new("id","com.ss.android.ugc.trill:id/f0x"),
        new("xpath","//android.widget.Button[@resource-id='com.ss.android.ugc.trill:id/f0x']"),
        new("id","com.ss.android.ugc.trill:id/f19"),
        new("xpath","//android.widget.Button[@resource-id='com.ss.android.ugc.trill:id/f19']"),
        new("xpath","//*[@text='Agree and continue' or @content-desc='Agree and continue']/ancestor-or-self::*[@clickable='true'][1]"),
        new("xpath","//android.widget.Button[@text='Agree and continue']")
    ];
    static readonly UiLocator[] GenderFirstOption =
    [
        // First option is "Man" on the screen supplied by the operator.
        new("xpath","//android.widget.ScrollView/android.view.View[1]/android.widget.Button"),
        new("xpath","//android.widget.Button[@text='Man' or @content-desc='Man']"),
        new("xpath","//*[@text='Man' or @content-desc='Man']/ancestor-or-self::*[@clickable='true'][1]")
    ];
    static readonly UiLocator[] GenderContinue =
    [
        // On the gender onboarding screen the Compose button may have no text attribute.
        // Only use this positional selector AFTER IsGenderPage has matched the screen.
        new("xpath","//androidx.compose.ui.platform.ComposeView/android.view.View/android.view.View[2]/android.widget.Button"),
        new("xpath","//android.widget.Button[@text='Continue' or @content-desc='Continue']"),
        new("xpath","//*[@text='Continue' or @content-desc='Continue']/ancestor-or-self::*[@clickable='true'][1]")
    ];
    static readonly UiLocator[] InterestsSkip =
    [
        // Prefer the *label*; the ComposeView position is only a fallback.
        new("xpath","//android.widget.Button[@text='Skip' or @content-desc='Skip']"),
        new("xpath","//*[@text='Skip' or @content-desc='Skip']/ancestor-or-self::*[@clickable='true'][1]"),
        new("xpath","//androidx.compose.ui.platform.ComposeView/android.view.View/android.view.View[1]/android.widget.Button")
    ];

    /// <returns>True only if this is a recognized first-launch screen; even
    /// if click cannot yet complete, the caller keeps waiting on this page.</returns>
    static async Task<bool> HandleFirstLaunchPageAsync(AppiumSession session,
        string source,FirstLaunchProgress state,CancellationToken ct,Action<string>? log,
        UiSelectors? selectors)
    {
        if(IsTermsPage(source))
        {
            state.LastPage="Terms and Policies";
            if(CanRetry(state.TermsAttempts,state.TermsLastAttempt))
            {
                state.TermsAttempts++;
                state.TermsLastAttempt=DateTimeOffset.UtcNow;
                state.TermsTapped=await session.ClickVisibleAnyAsync(Resolve(selectors,"firstLaunchTermsContinue",TermsContinue),ct);
                if(state.TermsTapped)
                    log?.Invoke($"Thiết lập TikTok: đã nhấn Agree and continue (lượt {state.TermsAttempts}/2); chờ chuyển màn hình.");
                else log?.Invoke("Thiết lập TikTok: đã thấy Terms nhưng không nhấn được nút f0x/f19 hoặc Agree and continue; lưu trạng thái để kiểm tra.");
            }
            return true;
        }
        if(IsGenderPage(source))
        {
            state.LastPage="Tell us more about yourself";
            if(!state.GenderTapped)
            {
                state.GenderTapped=await session.ClickVisibleAnyAsync(Resolve(selectors,"firstLaunchGenderFirst",GenderFirstOption),ct);
                if(state.GenderTapped)
                    log?.Invoke("Thiết lập TikTok: đã chọn tùy chọn giới tính đầu tiên (Man theo giao diện cung cấp).");
                return true;
            }
            if(CanRetry(state.GenderContinueAttempts,state.GenderContinueLastAttempt))
            {
                state.GenderContinueAttempts++;
                state.GenderContinueLastAttempt=DateTimeOffset.UtcNow;
                state.GenderContinued=await session.ClickVisibleAnyAsync(Resolve(selectors,"firstLaunchGenderContinue",GenderContinue),ct);
                if(state.GenderContinued)
                    log?.Invoke($"Thiết lập TikTok: đã nhấn Continue sau khi chọn giới tính (lượt {state.GenderContinueAttempts}/2); chờ sở thích.");
                else
                    log?.Invoke($"Thiết lập TikTok: đã chọn giới tính nhưng chưa click được Continue (lượt {state.GenderContinueAttempts}/2); chờ nút sẵn sàng.");
            }
            return true;
        }
        if(IsInterestsPage(source,state.GenderContinued))
        {
            state.LastPage="Interests / Skip";
            if(CanRetry(state.InterestsSkipAttempts,state.InterestsSkipLastAttempt))
            {
                state.InterestsSkipAttempts++;
                state.InterestsSkipLastAttempt=DateTimeOffset.UtcNow;
                state.InterestsSkipped=await session.ClickVisibleAnyAsync(Resolve(selectors,"firstLaunchInterestSkip",InterestsSkip),ct);
                if(state.InterestsSkipped)
                    log?.Invoke($"Thiết lập TikTok: đã nhấn Skip ở sở thích (lượt {state.InterestsSkipAttempts}/2); chờ Home/Profile.");
            }
            return true;
        }
        return false;
    }

    /// <summary>
    /// A foreground package is NOT proof that TikTok has finished loading.
    /// Look for real interactive TikTok controls in the current hierarchy.
    /// This works for both authenticated and logged-out screens, including
    /// the remembered-account Welcome back dialog.
    /// </summary>
    public static bool SourceHasReadyTikTokUi(string source)
    {
        if(!SourceHasTikTokUi(source))return false;
        try
        {
            var document=XDocument.Parse(source);
            foreach(var node in document.Descendants())
            {
                string id=(string?)node.Attribute("resource-id")??"";
                string desc=(string?)node.Attribute("content-desc")??"";
                string label=(string?)node.Attribute("text")??"";
                string tag=node.Name.LocalName;
                if(id is "com.ss.android.ugc.trill:id/t_k" or "com.ss.android.ugc.trill:id/tlh" or "com.ss.android.ugc.trill:id/z78" ||
                   desc is "Profile menu" or "Settings and privacy" ||
                   (desc=="Profile" && tag=="android.widget.FrameLayout") ||
                   label.StartsWith("Welcome back",StringComparison.OrdinalIgnoreCase) ||
                   label is "Edit profile" or "Log in" or "Đăng nhập" ||
                   label.Contains("Continue with Google",StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch(System.Xml.XmlException){ /* Hierarchy is being rebuilt; poll again. */ }
        return false;
    }

    /// <summary>
    /// Only the TikTok Home feed can show its first-run full-screen coachmark.
    /// Merely seeing a Profile navigation icon is not proof that it can receive
    /// a tap: a full-screen coachmark can intercept the tap and advance itself.
    /// </summary>
    public static bool SourceHasHomeFeedNavigation(string source)
    {
        if(!SourceHasTikTokUi(source))return false;
        try
        {
            var document=XDocument.Parse(source);
            bool home=false,profile=false,forYou=false;
            foreach(var node in document.Descendants())
            {
                string label=((string?)node.Attribute("text")??"").Trim();
                string desc=((string?)node.Attribute("content-desc")??"").Trim();
                if(label.Equals("Home",StringComparison.OrdinalIgnoreCase) ||
                   desc.Equals("Home",StringComparison.OrdinalIgnoreCase) ||
                   label.Equals("Trang chủ",StringComparison.OrdinalIgnoreCase) ||
                   desc.Equals("Trang chủ",StringComparison.OrdinalIgnoreCase))home=true;
                if(label.Equals("Profile",StringComparison.OrdinalIgnoreCase) ||
                   desc.Equals("Profile",StringComparison.OrdinalIgnoreCase) ||
                   label.Equals("Hồ sơ",StringComparison.OrdinalIgnoreCase) ||
                   desc.Equals("Hồ sơ",StringComparison.OrdinalIgnoreCase))profile=true;
                if(label.Equals("For You",StringComparison.OrdinalIgnoreCase) ||
                   desc.Equals("For You",StringComparison.OrdinalIgnoreCase) ||
                   label.Equals("Dành cho bạn",StringComparison.OrdinalIgnoreCase))forYou=true;
            }
            // Profile navigation alone may also appear on a real owner's page.
            // Home + Profile + For You are all visible on the feed in the
            // actual new-device screenshot. Keep this guard narrow.
            return home && profile && forYou;
        }
        catch(System.Xml.XmlException) { return false; }
    }

    /// <summary>
    /// Detects TikTok's feed share/context bottom sheet. A normal feed also has
    /// a single Share control, therefore never use that word alone as evidence.
    /// Require several menu-only actions to avoid closing an ordinary video.
    /// </summary>
    public static bool SourceHasFeedShareSheet(string source)
    {
        if(!SourceHasTikTokUi(source))return false;
        try
        {
            var document=XDocument.Parse(source);
            bool report=false,notInterested=false,download=false,shareWith=false;
            foreach(var node in document.Descendants())
            {
                string text=((string?)node.Attribute("text")??"").Trim();
                string desc=((string?)node.Attribute("content-desc")??"").Trim();
                foreach(string value in new[]{text,desc})
                {
                    if(value.Equals("Report",StringComparison.OrdinalIgnoreCase) ||
                       value.Equals("Báo cáo",StringComparison.OrdinalIgnoreCase))report=true;
                    if(value.Contains("Not interested",StringComparison.OrdinalIgnoreCase) ||
                       value.Contains("Không quan tâm",StringComparison.OrdinalIgnoreCase))notInterested=true;
                    if(value.Equals("Download",StringComparison.OrdinalIgnoreCase) ||
                       value.Contains("Download video",StringComparison.OrdinalIgnoreCase) ||
                       value.Contains("Save video",StringComparison.OrdinalIgnoreCase) ||
                       value.Equals("Tải xuống",StringComparison.OrdinalIgnoreCase) ||
                       value.Contains("Lưu video",StringComparison.OrdinalIgnoreCase))download=true;
                    if(value.Contains("Share with",StringComparison.OrdinalIgnoreCase) ||
                       value.Contains("Chia sẻ với",StringComparison.OrdinalIgnoreCase))shareWith=true;
                }
            }
            int evidence=(report?1:0)+(notInterested?1:0)+(download?1:0)+(shareWith?1:0);
            return evidence>=2 && (shareWith || notInterested);
        }
        catch(System.Xml.XmlException) { return false; }
    }

    /// <summary>
    /// Clears an accidentally opened feed share sheet before tutorial/Profile
    /// navigation. AndroidBack is the safest first action; a top-edge tap is a
    /// bounded fallback and is used only while the modal remains proven in XML.
    /// Returns true when a share sheet was detected and a cleanup was attempted.
    /// </summary>
    public static async Task<bool> DismissFeedShareSheetIfPresentAsync(
        AppiumSession session,string source,CancellationToken ct,Action<string>? log=null)
    {
        if(!SourceHasFeedShareSheet(source))return false;
        ct.ThrowIfCancellationRequested();
        string package=await session.CurrentPackageAsync(ct);
        if(package!=TikTokPackage && !SourceHasTikTokUi(source))return false;

        log?.Invoke("FEED_SHARE_SHEET_DETECTED: phát hiện menu Report/Not interested/Download/Share with; đóng trước khi điều hướng Profile.");
        await session.BackAsync(ct);
        log?.Invoke("FEED_SHARE_SHEET_BACK_REQUESTED: method=AndroidBack.");
        await Task.Delay(700,ct);

        string afterBack;
        try { afterBack=await session.SourceAsync(ct); }
        catch(OperationCanceledException) when(ct.IsCancellationRequested) { throw; }
        catch(Exception ex)
        {
            log?.Invoke($"FEED_SHARE_SHEET_RECHECK_PENDING: chưa đọc được XML sau Back ({ex.GetType().Name}); vòng kế tiếp sẽ xác minh lại.");
            return true;
        }
        if(!SourceHasFeedShareSheet(afterBack))
        {
            log?.Invoke("FEED_SHARE_SHEET_DISMISSED: method=AndroidBack; tiếp tục tìm tab Profile.");
            return true;
        }

        var (x,y,width,height)=await session.WindowRectAsync(ct);
        int safeX=x+Math.Max(24,width*8/100);
        int safeY=y+Math.Max(80,height*15/100);
        log?.Invoke($"FEED_SHARE_SHEET_SAFE_TAP: modal còn hiện sau Back; tap=({safeX},{safeY}) ở vùng phía trên menu.");
        await session.TapUiPointAsync(safeX,safeY,ct);
        await Task.Delay(700,ct);
        string afterTap=await session.SourceAsync(ct);
        log?.Invoke(SourceHasFeedShareSheet(afterTap)
            ? "FEED_SHARE_SHEET_DISMISS_UNCONFIRMED: menu vẫn hiện; không click Profile, vòng kế tiếp sẽ thử dọn lại."
            : "FEED_SHARE_SHEET_DISMISSED: method=SAFE_TOP_TAP; tiếp tục tìm tab Profile.");
        return true;
    }

    /// <summary>
    /// A Profile word, creator avatar or LIVE popup is not the bottom navigation tab.
    /// Accept a specific Profile control only when its visible bounds are within
    /// the bottom navigation strip. Avoid clicking the first arbitrary "Profile"
    /// text in the TikTok hierarchy.
    /// </summary>
    public static bool SourceHasBottomProfileTab(string source)
    {
        if(!SourceHasTikTokUi(source))return false;
        try
        {
            var root=XDocument.Parse(source);
            var nodes=root.Descendants().ToList();
            int bottom=0,width=0;
            foreach(var node in nodes)
            {
                if(TryGetUiBounds(node,out _,out _,out int x2,out int y2))
                {
                    bottom=Math.Max(bottom,y2);
                    width=Math.Max(width,x2);
                }
            }
            if(bottom<=0 || width<=0)return false;
            foreach(var node in nodes)
            {
                string label=((string?)node.Attribute("content-desc")??
                              (string?)node.Attribute("text")??"").Trim();
                if(!label.Equals("Profile",StringComparison.OrdinalIgnoreCase) &&
                   !label.Equals("Hồ sơ",StringComparison.OrdinalIgnoreCase))continue;
                if((string?)node.Attribute("displayed")=="false")continue;
                if(!TryGetUiBounds(node,out int left,out int top,out int right,out int end))continue;
                if((top+end)/2>=bottom*76/100 && (left+right)/2>=width*60/100)return true;
            }
        }
        catch(System.Xml.XmlException) { /* transient accessibility dump */ }
        return false;
    }

    public static (int X,int Y)? BottomProfileTabPoint(string source)
    {
        if(!SourceHasTikTokUi(source))return null;
        try
        {
            var nodes=XDocument.Parse(source).Descendants().ToList();
            int bottom=0,width=0;
            foreach(var node in nodes)
            {
                if(TryGetUiBounds(node,out _,out _,out int x2,out int y2))
                { bottom=Math.Max(bottom,y2);width=Math.Max(width,x2); }
            }
            if(bottom<=0 || width<=0)return null;
            return nodes.Select(node=>
                {
                    string label=((string?)node.Attribute("content-desc")??
                                  (string?)node.Attribute("text")??"").Trim();
                    bool profile=label.Equals("Profile",StringComparison.OrdinalIgnoreCase) ||
                        label.Equals("Hồ sơ",StringComparison.OrdinalIgnoreCase);
                    if(!profile || (string?)node.Attribute("displayed")=="false" ||
                       !TryGetUiBounds(node,out int left,out int top,out int right,out int end))
                        return (Valid:false,X:0,Y:0);
                    int x=(left+right)/2,y=(top+end)/2;
                    return (Valid:y>=bottom*76/100 && x>=width*60/100,X:x,Y:y);
                })
                .Where(point=>point.Valid)
                .Select(point=>((int X,int Y)?)(point.X,point.Y))
                .FirstOrDefault();
        }
        catch(System.Xml.XmlException){return null;}
    }

    /// <summary>
    /// Rotation-only handoff from a fresh TikTok Feed to the logged-out Profile.
    /// A coordinate tap is allowed only after XML proves the Profile-labelled
    /// node is in the bottom-right navigation strip.
    /// </summary>
    public static async Task<bool> OpenProfileAfterAccountRotationAsync(
        AppiumSession session,UiSelectors selectors,CancellationToken ct,Action<string>? log=null)
    {
        int taps=0;
        bool tutorialSwiped=false;
        var wait=Stopwatch.StartNew();
        while(wait.Elapsed<TimeSpan.FromSeconds(55))
        {
            ct.ThrowIfCancellationRequested();
            string package=await session.CurrentPackageAsync(ct);
            string source=await session.SourceAsync(ct);
            if(package!=TikTokPackage || !SourceHasTikTokUi(source))
            { await Task.Delay(900,ct);continue; }
            if(await DismissFeedShareSheetIfPresentAsync(session,source,ct,log))
            {
                await Task.Delay(300,ct);
                continue;
            }
            bool loginReady=IsTikTokLoginMethodsScreen(source) ||
                IsLoggedOutWelcomeBack(source) ||
                source.Contains("resource-id=\"com.ss.android.ugc.trill:id/z78\"",StringComparison.Ordinal) ||
                source.Contains("text=\"Log in\"",StringComparison.Ordinal) ||
                source.Contains("text=\"Đăng nhập\"",StringComparison.Ordinal);
            if(loginReady)
            {
                log?.Invoke($"ROTATE_PROFILE_LOGIN_CONFIRMED: taps={taps}; next=GOOGLE_LOGIN.");
                return true;
            }
            bool feed=SourceHasHomeFeedNavigation(source);
            bool bottomProfile=SourceHasBottomProfileTab(source);
            if(!tutorialSwiped && SourceHasSwipeUpTutorial(source))
            {
                tutorialSwiped=true;
                log?.Invoke("ROTATE_FEED_TUTORIAL_SWIPE: oneSwipeOnly=true.");
                await session.SwipeTikTokFirstRunFeedTutorialAsync(ct);
                await Task.Delay(1700,ct);
                continue;
            }
            if((feed || bottomProfile) && taps<3)
            {
                UiLocator[] exactBottomSelectors=
                [
                    new("xpath","//android.widget.FrameLayout[@content-desc='Profile']/android.widget.ImageView"),
                    new("xpath","//android.widget.FrameLayout[@content-desc='Profile']"),
                    new("xpath","//android.widget.Button[@content-desc='Profile']"),
                    new("xpath","//android.widget.FrameLayout[@content-desc='Hồ sơ']"),
                    new("xpath","//android.widget.Button[@content-desc='Hồ sơ']")
                ];
                bool clicked=await session.ClickVisibleAnyAsync(exactBottomSelectors,ct);
                string evidence=clicked?"EXACT_BOTTOM_SELECTOR":"NONE";
                if(!clicked)
                {
                    var point=BottomProfileTabPoint(source);
                    if(point.HasValue)
                    {
                        await session.TapUiPointAsync(point.Value.X,point.Value.Y,ct);
                        clicked=true;
                        evidence=$"VERIFIED_BOTTOM_BOUNDS; tap=({point.Value.X},{point.Value.Y})";
                    }
                }
                if(clicked)
                {
                    taps++;
                    log?.Invoke($"ROTATE_PROFILE_TAP_REQUESTED: attempt={taps}/3; evidence={evidence}.");
                    await Task.Delay(1800,ct);
                    continue;
                }
                log?.Invoke("ROTATE_PROFILE_TAB_NOT_CLICKABLE: bottomProfile verified but no safe target.");
            }
            await Task.Delay(900,ct);
        }
        log?.Invoke($"ROTATE_PROFILE_LOGIN_UNCONFIRMED: taps={taps}/3; timeoutSec=55.");
        return false;
    }

    private static bool TryGetUiBounds(XElement node,out int left,out int top,out int right,out int bottom)
    {
        left=top=right=bottom=0;
        string bounds=(string?)node.Attribute("bounds")??"";
        var m=Regex.Match(bounds,@"^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$");
        if(!m.Success)return false;
        return int.TryParse(m.Groups[1].Value,out left) &&
               int.TryParse(m.Groups[2].Value,out top) &&
               int.TryParse(m.Groups[3].Value,out right) &&
               int.TryParse(m.Groups[4].Value,out bottom);
    }

    public static bool SourceHasLiveRoomControls(string source)
        => SourceHasTikTokUi(source) &&
           (source.Contains("com.ss.android.ugc.trill:id/tv_online_audience_num",StringComparison.Ordinal) ||
            source.Contains("com.ss.android.ugc.trill:id/k13",StringComparison.Ordinal));

    public static bool SourceHasSwipeUpTutorial(string source)
    {
        // A full-screen coachmark can hide the Home/Profile/For You nodes from
        // UiAutomator even though the text is visible. Text is sufficient only
        // when the hierarchy itself still belongs to TikTok.
        if(!SourceHasTikTokUi(source))return false;
        try
        {
            var document=XDocument.Parse(source);
            foreach(var node in document.Descendants())
            {
                string label=(string?)node.Attribute("text")??"";
                string desc=(string?)node.Attribute("content-desc")??"";
                foreach(string value in new[]{label,desc})
                    if(value.Contains("Swipe up for more",StringComparison.OrdinalIgnoreCase) ||
                       value.Contains("Swipe up to",StringComparison.OrdinalIgnoreCase) ||
                       value.Contains("Vuốt lên để",StringComparison.OrdinalIgnoreCase))return true;
            }
        }
        catch(System.Xml.XmlException) { /* transient XML may omit the coachmark */ }
        return false;
    }

    /// <summary>
    /// On first-run TikTok may paint the Swipe-up coachmark in a graphics layer
    /// absent from /source. Right after *verified* Gender/Interests onboarding,
    /// a single bounded swipe is justified on the Home/For You feed even when
    /// the overlay text cannot be read. It must never run on login, Profile,
    /// Google, a LIVE room, or arbitrary unknown TikTok pages.
    /// </summary>
    public static async Task<bool> TryClearFirstRunFeedCoachmarkAsync(
        AppiumSession session,string source,bool justCompletedOnboarding,
        CancellationToken ct,Action<string>? log=null)
    {
        bool textDetected=SourceHasSwipeUpTutorial(source);
        // An explicit coachmark label is enough even if it conceals the feed
        // navigation nodes from the accessibility snapshot.
        if(!SourceHasHomeFeedNavigation(source) && !textDetected)return false;
        if(!textDetected && !justCompletedOnboarding)return false;
        if(await session.CurrentPackageAsync(ct)!=TikTokPackage)return false;
        log?.Invoke("NEW_DEVICE_FEED_TUTORIAL_SWIPE_REQUESTED: method=SwipeUp; " +
            (textDetected?"coachmark=XML":"coachmark=possible_graphic_overlay_after_onboarding") +
            "; oneSwipeOnly=true.");
        await session.SwipeTikTokFirstRunFeedTutorialAsync(ct);
        const int tutorialSettleMs=1200;
        await Task.Delay(tutorialSettleMs,ct);
        string after=await session.SourceAsync(ct);
        bool stillVisible=SourceHasSwipeUpTutorial(after);
        log?.Invoke("NEW_DEVICE_FEED_TUTORIAL_SWIPE_CHECK: " +
            (stillVisible?"overlay_still_visible_in_XML":"overlay_not_detected_in_XML") +
            "; currentFeed="+SourceHasHomeFeedNavigation(after)+
            $"; gestureCompleted=true; settleMs={tutorialSettleMs}.");
        // A hidden overlay cannot be proven absent by XML alone; the event log
        // deliberately reports a gesture and observation, not a guaranteed dismissal.
        return true;
    }

    /// <summary>
    /// Fresh/cloud devices can show TikTok's first-run setup WITHOUT the manager
    /// having performed pm clear. Only opt into onboarding recovery when the
    /// current TikTok-owned XML actually shows a known setup screen.
    /// Avoid unexpected clicks on normal LIVE/Feed/Profile screens.
    /// </summary>
    public static async Task<bool> HandleNewDeviceOnboardingIfPresentAsync(
        AppiumSession session,UiSelectors selectors,CancellationToken ct,
        Action<string>? log=null,int seconds=120)
    {
        string source=await session.SourceAsync(ct);
        string page=IsTermsPage(source)?"Terms and Policies":
                    IsGenderPage(source)?"Gender":
                    IsInterestsPage(source,false)?"Interests":"";
        if(page.Length==0)return true;
        log?.Invoke("NEW_DEVICE_ONBOARDING_DETECTED: "+page+
            "; đang xử lý theo giao diện thực tế, chưa tìm Profile.");
        bool ready=await WaitForReadyUiAsync(session,ct,seconds,log,
            firstLaunchOnboarding:true,firstLaunchSelectors:selectors);
        if(ready)
            log?.Invoke("NEW_DEVICE_ONBOARDING_COMPLETE: đã thấy thanh điều hướng TikTok; nếu còn hướng dẫn Feed, Preflight sẽ xử lý trước khi xác nhận Profile.");
        else
            log?.Invoke("NEW_DEVICE_ONBOARDING_PENDING: chưa thấy Home/Profile/Login; " +
                "giữ nguyên dữ liệu và tài khoản, không nhấn mù hoặc xóa lại.");
        return ready;
    }

    /// <summary>
    /// After Google's consent returns, TikTok may show gender/interests onboarding
    /// well AFTER the initial pm-clear first-launch setup. Keep the *same* Appium
    /// session, reuse existing first-launch selectors, and never start Profile
    /// recovery on one of these intermediate pages. Does not mark the account
    /// verified; SessionReconciler still compares the actual handle.
    /// </summary>
    public static async Task<bool> CompletePostGoogleOnboardingAsync(
        AppiumSession session,UiSelectors selectors,CancellationToken ct,
        Action<string>? log=null,int seconds=105)
    {
        var elapsed=Stopwatch.StartNew();
        var setup=new FirstLaunchProgress();
        int stableMain=0;
        bool sawSetup=false;
        bool feedSwipeAttempted=false;
        while(elapsed.Elapsed<TimeSpan.FromSeconds(seconds))
        {
            ct.ThrowIfCancellationRequested();
            string source;
            try { source=await session.SourceAsync(ct); }
            catch(OperationCanceledException) when(ct.IsCancellationRequested) { throw; }
            catch(Exception ex) when(ex is not OperationCanceledException)
            {
                log?.Invoke("POST_GOOGLE_UI_RETRY: chưa đọc được UI ("+ex.GetType().Name+"); chưa coi đăng nhập thất bại.");
                await Task.Delay(1300,ct);
                continue;
            }
            if(await DismissTikTokNotificationPromptAsync(session,source,ct,log))
            {
                stableMain=0;
                await Task.Delay(450,ct);
                continue;
            }
            // The contacts prompt may appear late, well after the old
            // 12-second watch in LoginWorkflow. Do not let it occupy the
            // entire 105-second post-Google UI wait.
            if(await DismissTikTokContactsPromptAsync(session,source,ct,log))
            {
                stableMain=0;
                await Task.Delay(450,ct);
                continue;
            }
            if(await DismissFeedShareSheetIfPresentAsync(session,source,ct,log))
            {
                stableMain=0;
                await Task.Delay(300,ct);
                continue;
            }
            if(IsOnboardingScreen(source) ||
               (sawSetup && IsInterestsPage(source,setup.GenderContinued)))
            {
                if(!sawSetup)
                    log?.Invoke("POST_GOOGLE_ONBOARDING_DETECTED: tiếp tục Terms/Gender/Interests; chưa tìm Profile.");
                sawSetup=true;
                stableMain=0;
                await HandleFirstLaunchPageAsync(session,source,setup,ct,log,selectors);
                await Task.Delay(1000,ct);
                continue;
            }
            // A fresh Gender/Interests run ends on the Home feed. The visual
            // Swipe-up coachmark may be absent from Appium XML; clear it once
            // before declaring the post-Google flow complete.
            if(!feedSwipeAttempted &&
               (SourceHasHomeFeedNavigation(source) || SourceHasSwipeUpTutorial(source)) &&
               (sawSetup || SourceHasSwipeUpTutorial(source)))
            {
                feedSwipeAttempted=await TryClearFirstRunFeedCoachmarkAsync(
                    session,source,sawSetup,ct,log);
                if(feedSwipeAttempted)
                {
                    stableMain=0;
                    continue;
                }
            }
            // Explicit tutorial text still present after the bounded swipe:
            // don't claim that the normal app is ready merely because its
            // underlying Home navigation is exposed in the same XML.
            if(SourceHasSwipeUpTutorial(source))
            {
                if(feedSwipeAttempted && SourceHasHomeFeedNavigation(source))
                    log?.Invoke("POST_GOOGLE_TUTORIAL_STALE_TEXT_IGNORED: swipeCompleted=true; " +
                        "Home/Profile verified=true; noLongTimeout=true.");
                else
                {
                    stableMain=0;
                    await Task.Delay(900,ct);
                    continue;
                }
            }
            // Neither Google OAuth nor a TikTok login sheet is proof that
            // account onboarding has finished. Do not press Back or Profile.
            bool mainPage=SourceHasHomeFeedNavigation(source) ||
                (SourceHasTikTokUi(source) && (
                    source.Contains("resource-id=\"com.ss.android.ugc.trill:id/tlh\"",StringComparison.Ordinal) ||
                    source.Contains("resource-id=\"com.ss.android.ugc.trill:id/t_k\"",StringComparison.Ordinal)));
            if(mainPage)
            {
                if(++stableMain>=2)
                {
                    // A fresh account can show the Virtual Items policy over
                    // an otherwise valid Home feed. Clear it before reporting
                    // onboarding complete so the later Search icon is usable.
                    if(await LivePopupHandler.DismissVirtualItemsPoliciesAsync(
                        session,message=>log?.Invoke(message),ct,source))
                    {
                        log?.Invoke("POST_GOOGLE_VIRTUAL_ITEMS_POLICY_DISMISSED: Home feed đã sạch trước bước xác minh/Auto LIVE.");
                        stableMain=0;
                        continue;
                    }
                    log?.Invoke(sawSetup ?
                        "POST_GOOGLE_ONBOARDING_COMPLETE: đã trở về Home/Profile; tiếp tục xác minh username." :
                        "POST_GOOGLE_MAIN_UI_READY: đã thấy Home/Profile; tiếp tục xác minh username.");
                    return true;
                }
            }
            else stableMain=0;
            await Task.Delay(1150,ct);
        }
        log?.Invoke(sawSetup ?
            "POST_GOOGLE_ONBOARDING_PENDING: chưa hoàn tất các bước thiết lập; giữ nguyên phiên để kiểm tra lại." :
            "POST_GOOGLE_UI_PENDING: chưa thấy Home/Profile sau Google; không dùng Back để bỏ qua bước trung gian.");
        return false;
    }

    /// <summary>
    /// Wait until real TikTok navigation/login controls exist for two polls.
    /// Using the package alone used to cause an immediate failed Profile click
    /// while cloud Android devices were still rendering their home screen.
    /// </summary>
    public static async Task<bool> WaitForReadyUiAsync(AppiumSession session,CancellationToken ct,
        int seconds=90,Action<string>? log=null,bool firstLaunchOnboarding=false,
        UiSelectors? firstLaunchSelectors=null)
    {
        var timer=Stopwatch.StartNew();
        var setup=new FirstLaunchProgress();
        int consecutive=0;
        int transientErrors=0;
        bool feedSwipeAttempted=false;
        do
        {
            ct.ThrowIfCancellationRequested();
            string source;
            try
            {
                source=await session.SourceAsync(ct);
                transientErrors=0;
            }
            catch(InvalidOperationException ex) when(firstLaunchOnboarding &&
                !ct.IsCancellationRequested && ++transientErrors<=3)
            {
                log?.Invoke($"Thiết lập TikTok: UI vừa khởi động, Appium chưa đọc được (lần {transientErrors}/3): {ex.GetType().Name}.");
                if(timer.Elapsed>=TimeSpan.FromSeconds(seconds))return false;
                await Task.Delay(1500,ct);
                continue;
            }
            if(await DismissTikTokNotificationPromptAsync(session,source,ct,log))
            {
                consecutive=0;
                await Task.Delay(450,ct);
                continue;
            }
            // Only deny the exact TikTok contacts runtime permission; other
            // Android permission prompts are not silently dismissed.
            if(await DismissTikTokContactsPromptAsync(session,source,ct,log))
            {
                consecutive=0;
                await Task.Delay(450,ct);
                continue;
            }
            if(await DismissFeedShareSheetIfPresentAsync(session,source,ct,log))
            {
                consecutive=0;
                await Task.Delay(300,ct);
                continue;
            }
            // A stale puzzle/security overlay is a valid preflight state, not
            // a reason to wait the full startup timeout. Let SessionReconciler
            // run its bounded X -> Back -> force-stop cleanup immediately.
            if(TikTokStateDetector.ContainsProtectionPrompt(source) ||
               LivePopupHandler.IsVerifyCaptcha(source))
            {
                string protectedPackage=await session.CurrentPackageAsync(ct);
                if(protectedPackage==TikTokPackage || SourceHasTikTokUi(source))
                {
                    log?.Invoke("PREFLIGHT_SECURITY_DETECTED_DURING_STARTUP: chuyển sang automated cleanup; không trả NeedsHuman.");
                    return true;
                }
            }
            // Terms/Gender/Interests happen before any Profile/Home controls.
            // Detect only on first launch after explicit app-data reset.
            if(firstLaunchOnboarding && await HandleFirstLaunchPageAsync(session,source,setup,ct,log,firstLaunchSelectors))
            {
                consecutive=0;
                if(timer.Elapsed>=TimeSpan.FromSeconds(seconds))
                {
                    log?.Invoke("Thiết lập TikTok: hết thời gian tại bước " + setup.LastPage + "; không nhấn lặp.");
                    return false;
                }
                await Task.Delay(1000,ct);
                continue;
            }
            // A normal Home navigation bar can coexist with the graphical
            // first-run tutorial. Complete one feed swipe before telling the
            // caller that onboarding is done. Other pages remain untouched.
            if(firstLaunchOnboarding && !feedSwipeAttempted &&
               (SourceHasHomeFeedNavigation(source) || SourceHasSwipeUpTutorial(source)) &&
               (setup.GenderContinued || setup.InterestsSkipped || SourceHasSwipeUpTutorial(source)))
            {
                feedSwipeAttempted=await TryClearFirstRunFeedCoachmarkAsync(
                    session,source,setup.GenderContinued || setup.InterestsSkipped,ct,log);
                if(feedSwipeAttempted)
                {
                    consecutive=0;
                    continue;
                }
            }
            // If the text is still exposed after our single attempt, do not
            // claim a verified, overlay-free Home from its underlying tabs.
            if(firstLaunchOnboarding && SourceHasSwipeUpTutorial(source))
            {
                if(feedSwipeAttempted && SourceHasHomeFeedNavigation(source))
                    log?.Invoke("NEW_DEVICE_TUTORIAL_STALE_TEXT_IGNORED: swipeCompleted=true; " +
                        "Home/Profile verified=true; noLongTimeout=true.");
                else
                {
                    consecutive=0;
                    if(timer.Elapsed>=TimeSpan.FromSeconds(seconds))
                    {
                        log?.Invoke("NEW_DEVICE_FEED_TUTORIAL_PENDING: vẫn thấy hướng dẫn trong XML sau thử vuốt; không coi Home đã sẵn sàng.");
                        return false;
                    }
                    await Task.Delay(900,ct);
                    continue;
                }
            }
            bool tikTokOwned=SourceHasTikTokUi(source);
            // A TikTok-owned accessibility snapshot already proves foreground
            // context. Avoid an extra current-package Appium RPC on every poll.
            bool correctApp=tikTokOwned || await session.CurrentPackageAsync(ct)==TikTokPackage;
            if(correctApp && SourceHasReadyTikTokUi(source))
            {
                if(++consecutive>=2)return true;
            }
            else consecutive=0;
            if(timer.Elapsed>=TimeSpan.FromSeconds(seconds))
            {
                if(firstLaunchOnboarding)
                    log?.Invoke("Thiết lập TikTok: chưa thấy Home/Profile/Login sau bước " +
                        (setup.LastPage.Length>0?setup.LastPage:"khởi động") + ". Không xóa dữ liệu lần nữa.");
                return false;
            }
            await Task.Delay(1150,ct);
        }while(true);
    }

    public static async Task<bool> IsVisibleAsync(AppiumSession session,CancellationToken ct)
    {
        string package=await session.CurrentPackageAsync(ct);
        if(package==TikTokPackage)return true;
        if(package is "com.google.android.gms" or "com.google.android.gsf.login" or
            "com.android.chrome" or "com.google.android.googlequicksearchbox")return false;
        return SourceHasTikTokUi(await session.SourceAsync(ct));
    }

    public static async Task<bool> WaitForTikTokAsync(AppiumSession session,CancellationToken ct,
        int seconds=25)
    {
        var timer=Stopwatch.StartNew();
        do
        {
            ct.ThrowIfCancellationRequested();
            if(await IsVisibleAsync(session,ct))return true;
            if(timer.Elapsed>=TimeSpan.FromSeconds(seconds))break;
            await Task.Delay(850,ct);
        } while(true);
        return false;
    }
}

/// <summary>
/// Detects app state from *current* UI. Never trusts prior database status as proof
/// of current login. An identity is accepted only when Profile and its editable
/// owner controls are visible, and the exact handle matches a configured account
/// assigned to this device. Missing/ambiguous handles stay UNKNOWN.
/// </summary>
public sealed class TikTokStateDetector(UiSelectors selectors)
{
    const string TikTokPackage = "com.ss.android.ugc.trill";
    static readonly Regex HandleSyntax = new(@"^@?([A-Za-z0-9._]{2,24})$", RegexOptions.Compiled);
    static readonly string[] Challenges = [
        "captcha", "verify it's you", "verify your identity", "unusual activity",
        "suspicious activity", "too many attempts", "xác minh danh tính", "hoạt động bất thường"
    ];
    static readonly string[] Restrictions = [
        "account suspended", "account banned", "account restricted",
        "tài khoản đã bị đình chỉ", "tài khoản bị khóa"
    ];

    public static string NormalizeHandle(string? value) => (value ?? "").Trim().TrimStart('@').ToLowerInvariant();
    public static bool IsValidHandle(string? value) =>
        HandleSyntax.IsMatch(value?.Trim() ?? "") &&
        NormalizeHandle(value) is not ("null" or "undefined");

    public async Task<TikTokObservation> DetectAsync(AppiumSession session, string expectedHandle,
        IEnumerable<string> approvedHandles, CancellationToken ct, Action<string>? progress = null)
    {
        ct.ThrowIfCancellationRequested();
        // Cloud phones can report the TikTok package before their navigation
        // controls have rendered. Poll for actual UI readiness instead of
        // clicking Profile immediately after a force-stop / monkey launch.
        string package="";
        string initial="";
        var readyTimer=Stopwatch.StartNew();
        var progressTimer=Stopwatch.StartNew();
        void Heartbeat(string stage)
        {
            if(progressTimer.Elapsed<TimeSpan.FromSeconds(12))return;
            progressTimer.Restart();
            progress?.Invoke(stage + $" (đã {readyTimer.Elapsed.TotalSeconds:0}s từ khi bắt đầu nhận diện)");
        }
        progress?.Invoke("Đang đọc ứng dụng foreground và chờ giao diện TikTok ổn định.");
        int stableUiSamples=0;
        while(true)
        {
            ct.ThrowIfCancellationRequested();
            Heartbeat("Đang chờ giao diện TikTok sẵn sàng");
            initial=await session.SourceAsync(ct);
            if(await TikTokForegroundProbe.DismissTikTokNotificationPromptAsync(session,initial,ct,progress))
            {
                stableUiSamples=0;
                await Task.Delay(800,ct);
                continue;
            }
            if(await TikTokForegroundProbe.DismissFeedShareSheetIfPresentAsync(session,initial,ct,progress))
            {
                stableUiSamples=0;
                await Task.Delay(300,ct);
                continue;
            }
            package=await session.CurrentPackageAsync(ct);
            bool tiktokVisible=package==TikTokPackage || TikTokForegroundProbe.SourceHasTikTokUi(initial);
            if (!tiktokVisible && package is ("com.google.android.gms" or "com.google.android.gsf.login" or
                "com.android.chrome" or "com.google.android.googlequicksearchbox"))
                return new(TikTokUiState.GoogleAuthentication,
                    "Phát hiện giao diện Google Authentication; Preflight sẽ xác định và dọn phiên cũ có giới hạn.");
            if(TikTokForegroundProbe.IsOnboardingScreen(initial))
                return new(TikTokUiState.Onboarding,
                    "POST_GOOGLE_ONBOARDING_DETECTED: đang ở Terms/Gender/Interests; chưa thể tìm Profile.");
            if(HasRestriction(initial))
                return new(TikTokUiState.AccountRestricted,"Phát hiện cảnh báo hạn chế/khóa tài khoản.");
            if(HasChallenge(initial))
                return new(TikTokUiState.SecurityChallenge,"Có màn hình yêu cầu xác minh bảo mật.");
            // On first launch the operator may already be on the login-method
            // modal. There is no username until Google authentication completes.
            if(TikTokForegroundProbe.IsTikTokLoginMethodsScreen(initial))
            {
                progress?.Invoke("Đã thấy Log in to TikTok / Continue with Google; bỏ qua đọc username, chuyển luồng đăng nhập.");
                return new(TikTokUiState.LoggedOut,"Màn hình phương thức đăng nhập TikTok đã sẵn sàng.");
            }
            if(TikTokForegroundProbe.IsGhostProfileAfterLogout(initial))
                return new(TikTokUiState.Unknown,
                    "Profile @null đang tải/không đồng bộ sau logout. Không coi null là username thật.");
            // A newly assigned phone may resume in Edit profile, Gallery,
            // an editor, or a LIVE sub-screen instead of Home/Profile.
            // A stable, interactive TikTok-owned hierarchy is ready for
            // *navigation diagnosis*, not proof of successful login.
            bool ownerUiReady=TikTokForegroundProbe.SourceHasReadyTikTokUi(initial);
            bool subScreenReady=tiktokVisible && TikTokForegroundProbe.SourceHasTikTokUi(initial) &&
                initial.Contains("clickable=\"true\"",StringComparison.OrdinalIgnoreCase) &&
                readyTimer.Elapsed>=TimeSpan.FromSeconds(2);
            if(tiktokVisible && (ownerUiReady || subScreenReady))
            {
                if(++stableUiSamples>=2)
                {
                    if(!ownerUiReady)
                        progress?.Invoke("PROFILE_RECOVERY_PRECHECK: TikTok đang ở màn hình phụ; bắt đầu nhận diện điều hướng, không giả định Home/Profile.");
                    break;
                }
            }
            else stableUiSamples=0;
            // LIVE / full-screen viewers may have a usable TikTok hierarchy but
            // no bottom Profile tab. Don't wait 75 seconds treating that as a splash.
            // A clickable TikTok hierarchy is only a hint to try safe navigation;
            // it is NOT proof of the logged-in identity.
            if(readyTimer.Elapsed>=TimeSpan.FromSeconds(20) && tiktokVisible &&
               TikTokForegroundProbe.SourceHasTikTokUi(initial) &&
               initial.Contains("clickable=\"true\"",StringComparison.OrdinalIgnoreCase))
                return new(TikTokUiState.Unknown,
                    $"UI TikTok đã hiện nhưng không có thanh điều hướng Profile (có thể đang xem LIVE hoặc mở màn hình phụ); foreground={package}.");
            if(readyTimer.Elapsed>=TimeSpan.FromSeconds(75))
                return new(TikTokUiState.Unknown,
                    $"TikTok chưa sẵn sàng sau 75 giây (foreground={package}); không click Profile khi UI chưa tải xong.");
            await Task.Delay(1150,ct);
        }

        progress?.Invoke("Đã thấy UI TikTok; đang phân loại màn hình Login/Google/Profile.");
        // After logout TikTok may show a multi-row Welcome back chooser
        // rather than a standalone Log in control. No Profile navigation is
        // needed: this screen itself proves the previous session ended.
        if(TikTokForegroundProbe.IsLoggedOutWelcomeBack(initial))
            return new(TikTokUiState.LoggedOut,
                "TikTok Welcome back / Add another account: phiên trước đã đăng xuất; đang ở màn hình chọn cách đăng nhập.");
        if(TikTokForegroundProbe.IsTikTokLoginMethodsScreen(initial))
        {
            progress?.Invoke("Đã thấy Log in to TikTok / Continue with Google; không cần mở lại Profile.");
            return new(TikTokUiState.LoggedOut,"Màn hình Continue with Google đã sẵn sàng.");
        }
        if(TikTokForegroundProbe.IsGhostProfileAfterLogout(initial))
            return new(TikTokUiState.Unknown,
                "Profile đang hiển thị @null (dữ liệu phiên chưa đồng bộ sau logout); không phải username thật hay bằng chứng đăng nhập.");
        // We already have a fresh /source snapshot. Repeating up to fourteen
        // separate Appium findElement requests here is very slow on XCloudPhone,
        // and may stall after the 'Back successful' message. Classify the actual
        // visible hierarchy instead; the login workflow owns its click locators.
        if(initial.Contains("Choose an account",StringComparison.OrdinalIgnoreCase) &&
            (initial.Contains("com.google.android.gms",StringComparison.Ordinal) ||
             package=="com.google.android.gms"))
            return new(TikTokUiState.GoogleAuthentication,"Màn hình chọn tài khoản Google đang mở.");

        bool ownerHandleVisible=ExtractProfileButtonHandles(initial).Count==1;
        if(!ownerHandleVisible && (
            initial.Contains("resource-id=\"com.ss.android.ugc.trill:id/z78\"",StringComparison.Ordinal) ||
            initial.Contains("Continue with Google",StringComparison.OrdinalIgnoreCase) ||
            initial.Contains("Tiếp tục với Google",StringComparison.OrdinalIgnoreCase) ||
            initial.Contains("text=\"Log in\"",StringComparison.Ordinal) ||
            initial.Contains("text=\"Đăng nhập\"",StringComparison.Ordinal)))
            return new(TikTokUiState.LoggedOut,"Đang ở màn hình đăng nhập TikTok.");

        // The specified Profile-tab ImageView is the primary selector. Always
        // navigate via the owner's bottom tab; never infer identity from a video.
        // The first visible frame can still be loading or covered by a spinner.
        // Only click a *displayed* Profile control; if the user is already on
        // their own Profile page, use the owner-username element directly.
        bool profileOpened=false;
        bool tutorialSwiped=false;
        int profileTapRequests=0;
        int recoveryBackRequests=0;
        const int MaxRecoveryBackRequests=5;
        DateTimeOffset lastProfileTap=DateTimeOffset.MinValue;
        var navigationTimer=Stopwatch.StartNew();
        progress?.Invoke("Đang tìm tab Profile và điều hướng tới trang tài khoản.");
        // Five bounded BACK actions are only for confirmed TikTok-owned
        // sub-screens. Stop as soon as the Profile tab/owner page appears;
        // never unwind Google/verification/login or guess screen coordinates.
        while(navigationTimer.Elapsed<TimeSpan.FromSeconds(75))
        {
            ct.ThrowIfCancellationRequested();
            var ui=await session.SourceAsync(ct);
            if(await TikTokForegroundProbe.DismissFeedShareSheetIfPresentAsync(session,ui,ct,progress))
            {
                await Task.Delay(300,ct);
                continue;
            }
            if(HasRestriction(ui))
                return new(TikTokUiState.AccountRestricted,"Có cảnh báo hạn chế tài khoản trước khi mở Profile.");
            if(HasChallenge(ui))
                return new(TikTokUiState.SecurityChallenge,"Có yêu cầu xác minh trước khi mở Profile.");
            if(TikTokForegroundProbe.IsTikTokLoginMethodsScreen(ui) ||
               TikTokForegroundProbe.IsLoggedOutWelcomeBack(ui))
                return new(TikTokUiState.LoggedOut,"Đã mở giao diện đăng nhập từ Profile.");
            Heartbeat("Đang tìm tab Profile / chờ giao diện điều hướng");
            // A coachmark can be drawn over an already populated accessibility
            // tree. It takes priority over apparent owner-page controls.
            if(!tutorialSwiped && TikTokForegroundProbe.SourceHasSwipeUpTutorial(ui))
            {
                string currentPackage=await session.CurrentPackageAsync(ct);
                if(currentPackage==TikTokPackage)
                {
                    tutorialSwiped=true;
                    progress?.Invoke("NEW_DEVICE_FEED_TUTORIAL_DETECTED: Swipe up for more in XML; thử vuốt lên một lần trước khi tìm Profile.");
                    await session.SwipeTikTokFirstRunFeedTutorialAsync(ct);
                    await Task.Delay(1700,ct);
                    continue;
                }
            }
            // Positive evidence only: never claim Profile navigation just
            // because WebDriver returned success for a tap on its icon.
            if(ExtractProfileButtonHandles(ui).Count==1)
            {
                profileOpened=true;
                progress?.Invoke("PROFILE_PAGE_CONFIRMED: đã đọc được nút username của Profile (giao diện cũ).");
                break;
            }
            // The owner's Edit profile button is a strong owner-page marker,
            // including when BACK has just restored that page from an editor.
            // This is not an arbitrary creator's profile; only the owner's
            // editable control qualifies. Handle still needs exact verification.
            if(SourceHasNewOwnerEditButton(ui) &&
               !TikTokForegroundProbe.SourceHasLiveRoomControls(ui) &&
               !TikTokForegroundProbe.SourceHasHomeFeedNavigation(ui))
            {
                profileOpened=true;
                progress?.Invoke($"PROFILE_PAGE_CONFIRMED: thấy Edit profile tlh trên trang chủ tài khoản; recoveryBack={recoveryBackRequests}.");
                break;
            }
            if(profileTapRequests>0 &&
               ui.Contains("resource-id=\"com.ss.android.ugc.trill:id/z78\"",StringComparison.Ordinal))
                return new(TikTokUiState.LoggedOut,"PROFILE_LOGIN_ENTRY_VISIBLE: trang Profile chưa đăng nhập.");

            bool feed=TikTokForegroundProbe.SourceHasHomeFeedNavigation(ui);
            bool tutorial=TikTokForegroundProbe.SourceHasSwipeUpTutorial(ui);
            // A real bottom Profile tab is a navigation destination.
            // Never press Back when it is already present: use the existing
            // owner-tab selector below, preserving the preflight login flow.
            bool profileInXml=TikTokForegroundProbe.SourceHasBottomProfileTab(ui);
            if(!profileInXml && (ui.Contains("text=\"Profile\"",StringComparison.Ordinal) ||
                                 ui.Contains("content-desc=\"Profile\"",StringComparison.Ordinal)))
                progress?.Invoke("PROFILE_TAB_TEXT_IGNORED: thấy chữ Profile nhưng không xác nhận được tab ở thanh dưới; không nhấn nhầm sang LIVE/overlay.");
            if(recoveryBackRequests>0 && profileInXml)
                progress?.Invoke($"PROFILE_RECOVERY_STOP: đã thấy tab Profile sau {recoveryBackRequests} Back; chuyển sang nhấn Profile.");
            if((tutorial || (feed && profileTapRequests>0 &&
                DateTimeOffset.UtcNow-lastProfileTap>=TimeSpan.FromSeconds(3))) &&
               !tutorialSwiped)
            {
                // The tutorial may be rendered as an accessibility-hidden
                // graphical overlay. In that case a first tap on Profile is
                // consumed while the feed remains visible; one bounded swipe
                // is a recovery, only on a confirmed Home/For You feed.
                tutorialSwiped=true;
                progress?.Invoke((tutorial?"NEW_DEVICE_FEED_TUTORIAL_DETECTED: ":
                    "NEW_DEVICE_FEED_NAVIGATION_BLOCKED: ")+
                    (tutorial?"Swipe up for more visible in XML":"Profile tap did not navigate; feed still visible")+
                    "; thử vuốt lên MỘT lần, không chạm vào tài khoản khác.");
                await session.SwipeTikTokFirstRunFeedTutorialAsync(ct);
                await Task.Delay(1800,ct);
                continue;
            }
            // On cloud phones the screen can require several seconds to
            // navigate. Observe actual destination before any further tap.
            if(profileTapRequests>0 &&
               DateTimeOffset.UtcNow-lastProfileTap<TimeSpan.FromSeconds(3))
            {
                await Task.Delay(850,ct);
                continue;
            }
            // Avoid guessing a random screen position. Only use the explicit
            // bottom Profile selector when that navigation item is in XML.
            if(profileInXml && profileTapRequests<2)
            {
                // Do not use broad //*[@text='Profile'] alternatives here: those
                // can match text or a creator's overlay rather than the bottom tab.
                UiLocator[] ownerBottomTab =
                [
                    new UiLocator("xpath","//android.widget.FrameLayout[@content-desc=\"Profile\"]/android.widget.ImageView"),
                    new UiLocator("xpath","//android.widget.FrameLayout[@content-desc=\"Profile\"]"),
                    new UiLocator("xpath","//android.widget.Button[@content-desc=\"Profile\"]")
                ];
                // The same XML already proves the Profile control is in the
                // bottom navigation strip. Use its verified bounds directly,
                // instead of up to three sequential find/displayed/click RPCs.
                bool profileTapped=false;
                var tabPoint=TikTokForegroundProbe.BottomProfileTabPoint(ui);
                if(tabPoint.HasValue &&
                   await session.CurrentPackageAsync(ct)==TikTokPackage)
                {
                    await session.TapUiPointAsync(tabPoint.Value.X,tabPoint.Value.Y,ct);
                    profileTapped=true;
                    progress?.Invoke($"FAST_PROFILE_TAP: verifiedBottomBounds=true; x={tabPoint.Value.X}; y={tabPoint.Value.Y}.");
                }
                else
                {
                    // Fail-safe fallback for devices exposing a Profile tab
                    // in XML without usable bounds; never click arbitrary text.
                    profileTapped=await session.ClickVisibleAnyAsync(ownerBottomTab,ct);
                }
                if(profileTapped)
                {
                    ++profileTapRequests;
                    lastProfileTap=DateTimeOffset.UtcNow;
                    progress?.Invoke($"PROFILE_BOTTOM_TAB_TAP_REQUESTED: lần {profileTapRequests}/2; selector=verified_bottom_only; chờ xác minh trang Profile.");
                    await Task.Delay(450,ct); // next loop verifies destination
                    continue;
                }
                progress?.Invoke("PROFILE_BOTTOM_TAB_NOT_CLICKABLE: thấy tab ở thanh dưới nhưng không thao tác được qua selector chính xác; không dùng click chữ chung.");
            }
            if(profileTapRequests>=2 && DateTimeOffset.UtcNow-lastProfileTap>=TimeSpan.FromSeconds(3) && feed)
                break;

            // No usable Profile tab or owner page: the phone may have resumed
            // in Edit profile, Gallery, or another TikTok sub-page. Back out
            // at most FIVE times, checking a fresh UI snapshot after each.
            // Do NOT Back from the feed (could exit TikTok), login/Google,
            // a restriction/challenge, or any non-TikTok foreground app.
            if(!profileInXml && !feed && recoveryBackRequests<MaxRecoveryBackRequests &&
               TikTokForegroundProbe.SourceHasTikTokUi(ui))
            {
                string foreground=await session.CurrentPackageAsync(ct);
                if(foreground!=TikTokPackage)
                    return new(TikTokUiState.Unknown,
                        $"PROFILE_RECOVERY_STOP: foreground={foreground}; không gửi Back sang ứng dụng khác.");
                bool backInXml=ui.Contains("content-desc=\"Back to previous screen\"",StringComparison.Ordinal);
                bool xpathClicked=false;
                if(backInXml)
                {
                    IReadOnlyList<UiLocator> backControls=selectors.Get("profileNavigationBack");
                    if(backControls.Count==0)
                        backControls=[new UiLocator("xpath","//android.widget.Button[@content-desc=\"Back to previous screen\"]")];
                    xpathClicked=await session.ClickVisibleAnyAsync(backControls,ct);
                }
                if(!xpathClicked)
                    await session.BackAsync(ct);
                ++recoveryBackRequests;
                progress?.Invoke($"PROFILE_RECOVERY_BACK_REQUESTED: {recoveryBackRequests}/{MaxRecoveryBackRequests}; method={(xpathClicked?"XPath":"AndroidBack")}; đọc lại UI trước khi thử tiếp.");
                await Task.Delay(950,ct);
                continue;
            }
            if(recoveryBackRequests>=MaxRecoveryBackRequests && !profileInXml)
            {
                progress?.Invoke("PROFILE_RECOVERY_GIVE_UP: đã thử tối đa 5 lần Back; chưa thấy Profile; dừng an toàn.");
                break;
            }
            await Task.Delay(1050,ct);
        }
        if(!profileOpened)
            return new(TikTokUiState.Unknown,
                "PROFILE_NAVIGATION_NOT_CONFIRMED: chưa xác minh được trang Profile/Login sau điều hướng; " +
                $"tapRequests={profileTapRequests}, backRequests={recoveryBackRequests}/{MaxRecoveryBackRequests}, tutorialSwipe={tutorialSwiped}. Không coi là lỗi tài khoản.");
        progress?.Invoke("Đã vào Profile; đang chờ và đọc username để đối chiếu với tài khoản được gán.");
        await Task.Delay(650,ct);
        var watch=Stopwatch.StartNew();
        bool profileEditTapRequested=false;
        DateTimeOffset profileEditTappedAt=DateTimeOffset.MinValue;
        while(watch.Elapsed < TimeSpan.FromSeconds(40))
        {
            ct.ThrowIfCancellationRequested();
            Heartbeat("Đang đợi username trong trang Profile");
            var xml=await session.SourceAsync(ct);
            if (HasRestriction(xml))
                return new(TikTokUiState.AccountRestricted,"Có cảnh báo hạn chế/khóa tài khoản trên Profile.");
            if (HasChallenge(xml))
                return new(TikTokUiState.SecurityChallenge,"Có màn hình xác minh bảo mật trên Profile.");
            if(TikTokForegroundProbe.IsTikTokLoginMethodsScreen(xml))
                return new(TikTokUiState.LoggedOut,
                    "Profile đã hiển thị Log in to TikTok / Continue with Google; chưa có username là bình thường.");
            if(TikTokForegroundProbe.IsGhostProfileAfterLogout(xml))
                return new(TikTokUiState.Unknown,
                    "Profile @null chưa đồng bộ, không thể xác minh tài khoản thật.");
            if (await session.ExistsAnyAsync(selectors.Get("loginEntry"),ct) ||
                await session.ExistsAnyAsync(selectors.Get("signupEntry"),ct))
                return new(TikTokUiState.LoggedOut,"Profile chưa đăng nhập.");

            // Read the exact username Button supplied by the operator. It may
            // expose text on the Button or a nested Android node, without '@'.
            // The button is trusted only AFTER successfully selecting the user's
            // bottom Profile tab. Never use arbitrary For You text as identity.
            var exactHandles=ExtractProfileButtonHandles(xml);
            foreach(var id in await session.FindAllAnyAsync(selectors.Get("profileUsernameButton"),ct))
            {
                var value=await session.ElementTextAsync(id,ct);
                if(TryParseProfileHandle(value,out var handle))exactHandles.Add(handle);
            }
            if(exactHandles.Count>1)
                return new(TikTokUiState.Unknown,
                    "Có nhiều giá trị username trong nút Profile; không thể xác minh duy nhất.");
            if(exactHandles.Count==1)
            {
                string found=exactHandles.Single();
                return ClassifyVerifiedHandle(found,expectedHandle,approvedHandles);
            }

            // New UI: tapping the owner's "Edit profile" control (tlh) opens a
            // page where the second mud TextView is the username. Never assume
            // mud[2] is a username on the feed or another user's profile.
            if(profileEditTapRequested)
            {
                if(TikTokForegroundProbe.SourceHasLiveRoomControls(xml) ||
                   TikTokForegroundProbe.SourceHasHomeFeedNavigation(xml))
                {
                    progress?.Invoke("PROFILE_EDIT_WRONG_DESTINATION: giao diện sau tap là Feed/LIVE, không phải Edit profile; dừng xác minh, không đọc username ở trang khác.");
                    await SaveProfilePreflightDiagnosticAsync(session,xml,ct,progress);
                    return new(TikTokUiState.Unknown,"PROFILE_EDIT_WRONG_DESTINATION: không xác nhận được trang Edit profile sau thao tác tlh.");
                }
                if(TryReadNewEditProfileUsername(xml,out var editHandle))
                {
                    progress?.Invoke("PROFILE_EDIT_USERNAME_FOUND: đọc username từ mud[2], đối chiếu chính xác với kho.");
                    return ClassifyVerifiedHandle(editHandle,expectedHandle,approvedHandles);
                }
                if(DateTimeOffset.UtcNow-profileEditTappedAt>=TimeSpan.FromSeconds(15))
                {
                    progress?.Invoke("PROFILE_EDIT_USERNAME_NOT_FOUND: đã yêu cầu mở Sửa hồ sơ nhưng chưa xác nhận được trang Edit profile / mud[2] sau 15 giây.");
                    await SaveProfilePreflightDiagnosticAsync(session,xml,ct,progress);
                    return new(TikTokUiState.Unknown,
                        "PROFILE_EDIT_USERNAME_NOT_FOUND: không xác minh được username trong Sửa hồ sơ (mud[2]); không coi đăng nhập thành công.");
                }
                await Task.Delay(700,ct);
                continue;
            }
            if(SourceHasNewOwnerEditButton(xml) &&
               !TikTokForegroundProbe.SourceHasLiveRoomControls(xml) &&
               !TikTokForegroundProbe.SourceHasHomeFeedNavigation(xml))
            {
                progress?.Invoke("PROFILE_EDIT_ENTRY_FOUND: tlh trên trang chủ tài khoản (không phải Feed/LIVE); thử mở Sửa hồ sơ một lần.");
                if(await session.ClickVisibleAnyAsync(selectors.Get("profileEditEntry"),ct))
                {
                    profileEditTapRequested=true;
                    profileEditTappedAt=DateTimeOffset.UtcNow;
                    progress?.Invoke("PROFILE_EDIT_TAP_REQUESTED: đã nhấn tlh; chờ trang Sửa hồ sơ và mud[2].");
                    await Task.Delay(1050,ct);
                    continue;
                }
                progress?.Invoke("PROFILE_EDIT_TAP_NOT_CONFIRMED: tlh có trên XML nhưng chưa thao tác được; sẽ thử lại khi UI ổn định.");
            }

            // Only use the older generic @TextView fallback with a verified
            // owner-only Edit profile indicator. It MUST NOT match unrelated text.
            if(!SourceHasNewOwnerEditButton(xml) &&
               await session.ExistsAnyAsync(selectors.Get("profileLoggedInMarker"),ct))
            {
                var matches=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(var element in await session.FindAllAnyAsync(selectors.Get("profileHandle"),ct))
                {
                    if(TryParseProfileHandle(await session.ElementTextAsync(element,ct),out var handle))
                        matches.Add(handle);
                }
                if(matches.Count==1)
                    return ClassifyVerifiedHandle(matches.Single(),expectedHandle,approvedHandles);
                if(matches.Count>1)
                    return new(TikTokUiState.Unknown,"Có nhiều handle trên Profile; không thể xác minh duy nhất.");
            }
            await Task.Delay(650,ct);
        }
        return new(TikTokUiState.Unknown,
            "Chưa xác minh được handle trên Profile (t_k hoặc tlh → mud[2]). Không coi đăng nhập thành công.");
    }

    private static async Task SaveProfilePreflightDiagnosticAsync(
        AppiumSession session,string xml,CancellationToken ct,Action<string>? progress)
    {
        try
        {
            string folder=Path.Combine(Storage.Folder,"diagnostics",
                "profile-preflight-"+DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder,"page-source.xml"),xml,ct);
            await File.WriteAllBytesAsync(Path.Combine(folder,"screen.png"),
                await session.ScreenshotPngAsync(ct),ct);
            progress?.Invoke("PROFILE_PREFLIGHT_DIAGNOSTIC_SAVED: "+folder+
                "; XML và ảnh lưu cục bộ trên PC, không có mật khẩu trong log.");
        }
        catch(Exception ex) when(ex is not OperationCanceledException)
        {
            progress?.Invoke("PROFILE_PREFLIGHT_DIAGNOSTIC_FAILED: "+ex.GetType().Name);
        }
    }

    // Exact new-owner edit control. The element must be visible in a TikTok-owned
    // UI hierarchy; a textual mention of 'tlh' alone is not sufficient.
    internal static bool SourceHasNewOwnerEditButton(string xml)
    {
        if(!TikTokForegroundProbe.SourceHasTikTokUi(xml) ||
           TikTokForegroundProbe.SourceHasLiveRoomControls(xml) ||
           TikTokForegroundProbe.SourceHasHomeFeedNavigation(xml))return false;
        try
        {
            var root=XDocument.Parse(xml);
            return root.Descendants().Any(e=>
                e.Name.LocalName=="android.widget.FrameLayout" &&
                (string?)e.Attribute("resource-id")=="com.ss.android.ugc.trill:id/tlh" &&
                (string?)e.Attribute("displayed")!="false" &&
                (string?)e.Attribute("enabled")!="false");
        }
        catch(System.Xml.XmlException) {return false;}
    }

    // Honor XPath (//android.widget.TextView[@resource-id='...:id/mud'])[2]
    // exactly. Never take the first mud node (often the display name), and
    // never scan all TextViews for an arbitrary @handle as fallback here.
    internal static bool TryReadNewEditProfileUsername(string xml,out string handle)
    {
        handle="";
        if(!TikTokForegroundProbe.SourceHasTikTokUi(xml))return false;
        try
        {
            var root=XDocument.Parse(xml);
            var fields=root.Descendants().SelectMany(e=>new []
            {
                (string?)e.Attribute("text")??"",(string?)e.Attribute("content-desc")??""
            }).ToList();
            bool editHeading=fields.Any(v=>v.Trim().Equals("Edit profile",StringComparison.OrdinalIgnoreCase) ||
                                            v.Trim().Equals("Sửa hồ sơ",StringComparison.OrdinalIgnoreCase));
            bool nameRow=fields.Any(v=>v.Trim().Equals("Name",StringComparison.OrdinalIgnoreCase) ||
                                          v.Trim().Equals("Tên",StringComparison.OrdinalIgnoreCase));
            bool usernameRow=fields.Any(v=>v.Trim().Equals("Username",StringComparison.OrdinalIgnoreCase) ||
                                              v.Trim().Equals("Tên người dùng",StringComparison.OrdinalIgnoreCase));
            // The second mud may exist on a different TikTok screen. Require
            // an Edit profile heading OR both labelled owner fields first.
            if(!editHeading && !(nameRow && usernameRow))return false;
            var mud=root.Descendants().Where(e=>
                e.Name.LocalName=="android.widget.TextView" &&
                (string?)e.Attribute("resource-id")=="com.ss.android.ugc.trill:id/mud").ToList();
            if(mud.Count<2)return false;
            var second=mud[1];
            if((string?)second.Attribute("displayed")=="false")return false;
            var text=(string?)second.Attribute("text");
            var desc=(string?)second.Attribute("content-desc");
            // A valid handle must be the actual second mud node's value.
            return TryParseProfileHandle(!string.IsNullOrWhiteSpace(text)?text:desc,out handle);
        }
        catch(System.Xml.XmlException) {return false;}
    }

    // Exact comparison, never substring matching. Accept bare handles, @handles,
    // or a single explicitly labelled 'TikTok ID: handle' string.
    internal static bool TryParseProfileHandle(string? source,out string handle)
    {
        handle="";
        string value=(source??"").Trim();
        if(value.Length==0)return false;
        // @null / undefined are transient TikTok placeholders, NOT real handles.
        string withoutAt=value.TrimStart('@');
        if(withoutAt.Equals("null",StringComparison.OrdinalIgnoreCase) ||
           withoutAt.Equals("undefined",StringComparison.OrdinalIgnoreCase))return false;
        if(IsValidHandle(value))
        {
            handle=NormalizeHandle(value);
            return true;
        }
        var labelled=Regex.Match(value,
            @"^(?:TikTok\s*ID|Username|User\s*name|Tên\s*người\s*dùng)\s*[:：]\s*(@?[A-Za-z0-9._]{2,24})$",
            RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        if(labelled.Success && IsValidHandle(labelled.Groups[1].Value))
        {
            handle=NormalizeHandle(labelled.Groups[1].Value);
            return true;
        }
        return false;
    }

    static HashSet<string> ExtractProfileButtonHandles(string xml)
    {
        var handles=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var doc=XDocument.Parse(xml);
            foreach(var button in doc.Descendants().Where(e=>
                (string?)e.Attribute("resource-id")=="com.ss.android.ugc.trill:id/t_k" &&
                e.Name.LocalName=="android.widget.Button"))
            {
                foreach(var node in button.DescendantsAndSelf())
                {
                    foreach(var key in new[]{"text","content-desc"})
                        if(TryParseProfileHandle((string?)node.Attribute(key),out var handle))
                            handles.Add(handle);
                }
            }
        }
        catch(System.Xml.XmlException) { /* driver sometimes returns transient source; poll again */ }
        return handles;
    }

    static TikTokObservation ClassifyVerifiedHandle(string found,string expectedHandle,
        IEnumerable<string> approvedHandles)
    {
        if(found==NormalizeHandle(expectedHandle))
            return new(TikTokUiState.ExpectedAccount,
                "Đã đối chiếu chính xác username trên Profile với tài khoản được gán.",found);
        var approved=approvedHandles.Select(NormalizeHandle)
            .Where(IsValidHandle).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(approved.Contains(found))
            return new(TikTokUiState.OtherApprovedAccount,
                "Profile đang đăng nhập tài khoản khác thuộc kho của chính điện thoại.",found);
        // This is not an ambiguous UI state: the owner Profile supplied one
        // exact username and it differs from the explicitly expected account.
        return new(TikTokUiState.DifferentAccount,
            $"Username Profile @{found} không trùng username dự kiến @{NormalizeHandle(expectedHandle)} " +
            "trên tài khoản đã chọn.",found);
    }

    internal static bool ContainsProtectionPrompt(string source) =>
        HasChallenge(source) || HasRestriction(source);

    static bool HasChallenge(string source) => Challenges.Any(marker =>
        source.Contains(marker,StringComparison.OrdinalIgnoreCase));
    static bool HasRestriction(string source) => Restrictions.Any(marker =>
        source.Contains(marker,StringComparison.OrdinalIgnoreCase));
}

/// <summary>One bounded non-destructive attempt at bringing TikTok back to its main UI.</summary>
public sealed class TikTokStateRecovery(AdbClient adb, Action<string> log)
{
    static bool IsGooglePackage(string package) => package is
        "com.google.android.gms" or "com.google.android.gsf.login" or
        "com.android.chrome" or "com.google.android.googlequicksearchbox";

    /// <summary>
    /// Preflight-only recovery for a Google authentication activity left by a
    /// previous worker/session. Never call this from the active post-login wait.
    /// Two bounded Back actions are followed by one TikTok restart; if Google
    /// still owns the foreground, clear TikTok + Play Services once and relaunch.
    /// </summary>
    public async Task<StaleGoogleRecoveryOutcome> RecoverStaleGoogleAuthenticationAsync(
        AppiumSession session,DeviceRecord device,CancellationToken ct)
    {
        log("PREFLIGHT_GOOGLE_STALE_DETECTED: activeLoginFlow=false; recovery=BACK_2_THEN_RESTART.");
        for(int attempt=1;attempt<=2;attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await session.BackAsync(ct);
                log($"PREFLIGHT_GOOGLE_BACK: attempt={attempt}/2; sent=true.");
            }
            catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
            catch(Exception ex)
            {
                log($"PREFLIGHT_GOOGLE_BACK: attempt={attempt}/2; sent=false; error={ex.GetType().Name}.");
            }
            await Task.Delay(1200,ct);
            string package=await session.CurrentPackageAsync(ct);
            string source=await session.SourceAsync(ct);
            if(package==TikTokForegroundProbe.TikTokPackage ||
               TikTokForegroundProbe.SourceHasTikTokUi(source))
            {
                log($"PREFLIGHT_GOOGLE_BACK_RECOVERED: attempt={attempt}/2; foreground={package}.");
                return StaleGoogleRecoveryOutcome.TikTokRecovered;
            }
        }

        log("PREFLIGHT_GOOGLE_FORCE_STOP: Back 2/2 chưa thoát; restart TikTok giữ nguyên dữ liệu.");
        await adb.ForceStopTikTokAsync(device.Serial,ct);
        await Task.Delay(2000,ct);
        await adb.LaunchTikTokAsync(device.Serial,ct);
        if(await WaitForTikTokForegroundAsync(session,ct,25))
        {
            log("PREFLIGHT_GOOGLE_FORCE_STOP_RECOVERED: TikTok foreground=true; clearData=false.");
            return StaleGoogleRecoveryOutcome.TikTokRecovered;
        }

        log("PREFLIGHT_GOOGLE_CLEAR_DATA: Google vẫn kẹt sau Back/restart; " +
            "packages=com.ss.android.ugc.trill,com.google.android.gms; once=true.");
        await adb.ClearTikTokAppDataAsync(device.Serial,ct);
        await adb.ClearGooglePlayServicesDataAsync(device.Serial,ct);
        await Task.Delay(2000,ct);
        await adb.LaunchTikTokAsync(device.Serial,ct);
        if(await WaitForTikTokForegroundAsync(session,ct,60))
        {
            log("PREFLIGHT_GOOGLE_CLEAR_DATA_OK: giao diện sạch; next=GOOGLE_LOGIN.");
            return StaleGoogleRecoveryOutcome.ClearDataReadyForLogin;
        }
        log("PREFLIGHT_GOOGLE_RECOVERY_FAILED: TikTok chưa foreground sau clear/relaunch.");
        return StaleGoogleRecoveryOutcome.Failed;
    }

    static async Task<bool> WaitForTikTokForegroundAsync(
        AppiumSession session,CancellationToken ct,int seconds)
    {
        var wait=Stopwatch.StartNew();
        while(wait.Elapsed<TimeSpan.FromSeconds(seconds))
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(900,ct);
            string package=await session.CurrentPackageAsync(ct);
            string source=await session.SourceAsync(ct);
            if(package==TikTokForegroundProbe.TikTokPackage ||
               TikTokForegroundProbe.SourceHasTikTokUi(source))return true;
        }
        return false;
    }

    /// <summary>
    /// Preflight-only cleanup for a stale Verify-to-continue/security overlay.
    /// Attempt 1 retains app data and force-restarts TikTok. Attempt 2 escalates
    /// to pm clear so a persistent local security flag cannot poison the worker.
    /// The caller re-runs identity detection after every attempt.
    /// </summary>
    public async Task<SecurityRecoveryOutcome> RecoverSecurityChallengeAsync(
        AppiumSession session,DeviceRecord device,int restartAttempt,CancellationToken ct)
    {
        string source="";
        for(int closeAttempt=1;closeAttempt<=5;closeAttempt++)
        {
            ct.ThrowIfCancellationRequested();
            source=await session.SourceAsync(ct);
            if(!TikTokStateDetector.ContainsProtectionPrompt(source) &&
               !LivePopupHandler.IsVerifyCaptcha(source))
            {
                log($"PREFLIGHT_SECURITY_CLEAN: UI sạch tại lượt {closeAttempt}/5; tiếp tục luồng.");
                return SecurityRecoveryOutcome.UiClean;
            }

            log($"PREFLIGHT_SECURITY_CLOSE_ATTEMPT: {closeAttempt}/5; đọc XML mới và thử nút X.");
            var captcha=await LivePopupHandler.DismissVerifyCaptchaAsync(
                session,log,ct,source,$"PREFLIGHT_SECURITY_{closeAttempt}_OF_5");
            source=captcha.Source;
            if(!TikTokStateDetector.ContainsProtectionPrompt(source) &&
               !LivePopupHandler.IsVerifyCaptcha(source))
            {
                log($"PREFLIGHT_SECURITY_CLEAN: nút X đã đóng challenge tại lượt {closeAttempt}/5.");
                return SecurityRecoveryOutcome.UiClean;
            }

            log($"PREFLIGHT_SECURITY_BACK_REQUESTED: attempt={closeAttempt}/5; AndroidBack=1.");
            try { await session.BackAsync(ct); }
            catch(OperationCanceledException) when(ct.IsCancellationRequested) { throw; }
            catch(Exception ex) when(AppiumFailureClassifier.IsFatalSessionFailure(ex)) { throw; }
            catch(Exception ex)
            { log($"PREFLIGHT_SECURITY_BACK_FAILED: attempt={closeAttempt}/5; {ex.GetType().Name}."); }
            await Task.Delay(closeAttempt==5?1800:1200,ct);
            source=await session.SourceAsync(ct);
            if(!TikTokStateDetector.ContainsProtectionPrompt(source) &&
               !LivePopupHandler.IsVerifyCaptcha(source))
            {
                log($"PREFLIGHT_SECURITY_CLEAN: AndroidBack đã dọn challenge tại lượt {closeAttempt}/5.");
                return SecurityRecoveryOutcome.UiClean;
            }
        }
        log("PREFLIGHT_SECURITY_CLOSE_EXHAUSTED: Captcha còn tồn tại sau 5/5 lượt X/Back.");

        if(restartAttempt>=2)
            return await ClearDataForCleanLoginAsync(session,device,ct);
        else
        {
            log($"PREFLIGHT_SECURITY_FORCE_STOP: attempt={restartAttempt}/2; " +
                "package=com.ss.android.ugc.trill; clearData=false.");
            await adb.RunAsync(ct,"-s",device.Serial,"shell","am","force-stop",
                TikTokForegroundProbe.TikTokPackage);
        }
        await Task.Delay(2000,ct);
        await adb.LaunchTikTokAsync(device.Serial,ct);
        for(int check=1;check<=20;check++)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(check==1?1500:900,ct);
            string package=await session.CurrentPackageAsync(ct);
            source=await session.SourceAsync(ct);
            if(package==TikTokForegroundProbe.TikTokPackage &&
               TikTokForegroundProbe.SourceHasTikTokUi(source))
            {
                bool blocked=TikTokStateDetector.ContainsProtectionPrompt(source) ||
                    LivePopupHandler.IsVerifyCaptcha(source);
                log($"PREFLIGHT_SECURITY_RELAUNCH_RESULT: attempt={restartAttempt}/2; " +
                    $"check={check}/20; challengePresent={blocked}.");
                if(blocked)return SecurityRecoveryOutcome.ChallengeStillPresent;
                return SecurityRecoveryOutcome.UiClean;
            }
        }
        log($"PREFLIGHT_SECURITY_RELAUNCH_FAILED: attempt={restartAttempt}/2; TikTok UI chưa sẵn sàng.");
        return SecurityRecoveryOutcome.UiNotReady;
    }

    public async Task<SecurityRecoveryOutcome> ClearDataForCleanLoginAsync(
        AppiumSession session,DeviceRecord device,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        log("PREFLIGHT_SECURITY_CLEAR_DATA: attempt=2/2; command=pm clear; " +
            "packages=com.ss.android.ugc.trill,com.google.android.gms; escalation=true.");
        await adb.ClearTikTokAppDataAsync(device.Serial,ct);
        log("PREFLIGHT_SECURITY_CLEAR_DATA_TIKTOK_OK: dữ liệu TikTok đã sạch; " +
            "đang xóa cache/token Google Play Services trên đúng thiết bị.");
        await adb.ClearGooglePlayServicesDataAsync(device.Serial,ct);
        log("PREFLIGHT_SECURITY_CLEAR_DATA_OK: dữ liệu TikTok và Google Play Services đã sạch; " +
            "mở app để đăng nhập Google lại từ đầu.");
        await Task.Delay(2000,ct);
        await adb.LaunchTikTokAsync(device.Serial,ct);
        for(int check=1;check<=20;check++)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(check==1?1500:900,ct);
            string package=await session.CurrentPackageAsync(ct);
            string source=await session.SourceAsync(ct);
            if(package!=TikTokForegroundProbe.TikTokPackage ||
               !TikTokForegroundProbe.SourceHasTikTokUi(source))continue;
            bool blocked=TikTokStateDetector.ContainsProtectionPrompt(source) ||
                LivePopupHandler.IsVerifyCaptcha(source);
            log($"PREFLIGHT_SECURITY_CLEAR_DATA_RELAUNCH: check={check}/20; " +
                $"challengePresent={blocked}.");
            return blocked ? SecurityRecoveryOutcome.ChallengeStillPresent :
                SecurityRecoveryOutcome.ClearDataReadyForLogin;
        }
        log("PREFLIGHT_SECURITY_CLEAR_DATA_RELAUNCH_FAILED: TikTok UI chưa sẵn sàng.");
        return SecurityRecoveryOutcome.UiNotReady;
    }

    /// <summary>
    /// A navigation-only fix for fullscreen screens, especially TikTok LIVE.
    /// One Android Back to return to the feed; if normal Profile controls do
    /// not appear, one force-stop/relaunch with all app data retained.
    /// Never dismiss Google authentication or a visible protection challenge.
    /// </summary>
    public async Task<bool> RecoverNavigationAsync(
        AppiumSession session, DeviceRecord device, CancellationToken ct,
        bool forceRestartFirst=false)
    {
        string package=await session.CurrentPackageAsync(ct);
        string source=await session.SourceAsync(ct);
        if(IsGooglePackage(package) || TikTokStateDetector.ContainsProtectionPrompt(source))
        {
            log("Khôi phục điều hướng tạm dừng: đang có xác minh/bảo mật hoặc Google.");
            return false;
        }
        if(package!=TikTokForegroundProbe.TikTokPackage &&
           !TikTokForegroundProbe.SourceHasTikTokUi(source))return false;

        if(!forceRestartFirst)
        {
            log("TikTok đang ở màn hình không có tab Profile (có thể LIVE); thử Android Back một lần.");
            try { await session.BackAsync(ct); }
            catch(InvalidOperationException ex)
            { log("Android Back chưa thực hiện được: "+ex.GetType().Name); }
            if(await TikTokForegroundProbe.WaitForReadyUiAsync(session,ct,12))
            {
                log("Đã thấy giao diện TikTok có nút Profile/Login sau khi thoát màn hình phụ.");
                return true;
            }
        }
        // DetectAsync has already exhausted its five bounded Back actions when
        // forceRestartFirst is true. Do not send a sixth Back: restart the app.
        return await RestartTikTokOnceAsync(session,device,ct);
    }

    /// <summary>
    /// One cache-only recovery after a verified, authorized logout attempt.
    /// Unsupported/ambiguous Android pm versions are a HARD STOP: never fall
    /// back to pm clear without --cache-only. Never touch Google app cache.
    /// </summary>
    public async Task<bool> TryClearTikTokCacheAndRestartAsync(
        AppiumSession session, DeviceRecord device, CancellationToken ct)
    {
        string source=await session.SourceAsync(ct);
        if(TikTokStateDetector.ContainsProtectionPrompt(source))
        {
            log("Logout: đang có màn hình bảo mật; không xóa cache.");
            return false;
        }
        string package=await session.CurrentPackageAsync(ct);
        if(IsGooglePackage(package) ||
            (package!=TikTokForegroundProbe.TikTokPackage &&
             !TikTokForegroundProbe.SourceHasTikTokUi(source)))
        {
            log("Logout: giao diện không phải TikTok; không xóa cache ứng dụng.");
            return false;
        }
        try
        {
            await adb.ClearTikTokCacheOnlyAsync(device.Serial,ct);
            log("Logout: đã xóa CHỈ cache TikTok (pm clear --cache-only), không xóa dữ liệu ứng dụng.");
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested) { throw; }
        catch(Exception ex)
        {
            log("Logout: không xóa cache được an toàn: "+ex.Message);
            return false;
        }
        return await RestartTikTokOnceAsync(session,device,ct);
    }

    /// <summary>
    /// SWITCH ONLY: the caller has verified the old account's exact TikTok
    /// username, unique vault ownership for THIS device and no restriction.
    /// Do not use on UNKNOWN, Google screens or active security challenges.
    /// A successful switch clears TikTok and Google Play Services local data on
    /// this device. It does not delete server-side TikTok/Google accounts.
    /// </summary>
    public async Task<bool> ResetTikTokForApprovedSwitchAsync(
        AppiumSession session, DeviceRecord device, CancellationToken ct)
    {
        string package=await session.CurrentPackageAsync(ct);
        string source=await session.SourceAsync(ct);
        if(TikTokStateDetector.ContainsProtectionPrompt(source) || IsGooglePackage(package))
        {
            log("Chuyển tài khoản: đang có màn hình Google/xác minh; KHÔNG xóa dữ liệu TikTok.");
            return false;
        }
        if(package!=TikTokForegroundProbe.TikTokPackage &&
           !TikTokForegroundProbe.SourceHasTikTokUi(source))
        {
            log("Chuyển tài khoản: chưa thấy TikTok trên thiết bị; KHÔNG xóa dữ liệu.");
            return false;
        }
        try
        {
            log("Chuyển tài khoản: xóa TOÀN BỘ dữ liệu cục bộ TikTok trên " + device.Name +
                " (phiên đăng nhập, cache, cài đặt và bản nháp cục bộ); không xóa tài khoản trong kho.");
            await adb.ClearTikTokAppDataAsync(device.Serial,ct);
            log("Chuyển tài khoản: ADB xác nhận pm clear TikTok = Success; " +
                "đang xóa cache/token Google Play Services trên đúng thiết bị.");
            await adb.ClearGooglePlayServicesDataAsync(device.Serial,ct);
            log("Chuyển tài khoản: ADB xác nhận pm clear TikTok và Google Play Services = Success; " +
                "ứng dụng trở về trạng thái đăng nhập sạch.");
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested) { throw; }
        catch(Exception ex)
        {
            log("Chuyển tài khoản: không xác nhận được chuỗi xóa dữ liệu TikTok/Google Play Services: " + ex.Message +
                ". KHÔNG tự thực hiện lần hai.");
            return false;
        }
        // The TikTok process is killed by pm clear; no stale element IDs may
        // be reused from the Appium session after this point.
        try
        {
            await Task.Delay(1800,ct);
            await adb.LaunchTikTokAsync(device.Serial,ct);
            log("Chuyển tài khoản: đã mở TikTok sạch; chờ thiết lập ban đầu rồi Home/Profile/Login (tối đa 120 giây).");
            bool ready=await TikTokForegroundProbe.WaitForReadyUiAsync(session,ct,120,log,
                firstLaunchOnboarding:true,firstLaunchSelectors:UiSelectors.Load());
            if(!ready)
            {
                log("Chuyển tài khoản: đã xóa dữ liệu và xử lý popup nếu xuất hiện, nhưng chưa hoàn tất thiết lập hoặc chưa có Home/Profile/Login; KHÔNG xóa lặp.");
                try
                {
                    string latest=await session.SourceAsync(ct);
                    if(latest.Contains("Allow permissions",StringComparison.OrdinalIgnoreCase) ||
                       latest.Contains("Cho phép quyền",StringComparison.OrdinalIgnoreCase))
                        log("Chuyển tài khoản: màn hình hướng dẫn cấp quyền bên trong TikTok vẫn còn; cần xác định nút Skip/Continue thực tế trước khi thao tác.");
                }
                catch(Exception e) when(e is not OperationCanceledException)
                {
                    log("Chuyển tài khoản: chưa đọc được giao diện sau popup ("+e.GetType().Name+").");
                }
            }
            return ready;
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested) { throw; }
        catch(Exception ex)
        {
            // This exception occurs after pm clear has already succeeded: NEVER
            // clear again to recover from a WebDriver observation failure.
            string detail=ex.Message.Replace('\r',' ').Replace('\n',' ');
            if(detail.Length>250)detail=detail[..250];
            log("Chuyển tài khoản: TikTok đã được xóa nhưng đọc UI thất bại (" +
                ex.GetType().Name + "): " + detail + "; KHÔNG xóa lại.");
            return false;
        }
    }

    public async Task<bool> RestartTikTokOnceAsync(AppiumSession session, DeviceRecord device, CancellationToken ct)
    {
        string package=await session.CurrentPackageAsync(ct);
        string source=await session.SourceAsync(ct);
        if(IsGooglePackage(package) || TikTokStateDetector.ContainsProtectionPrompt(source))
        {
            log("Không đóng/mở TikTok khi xác minh bảo mật hoặc Google vẫn đang hiển thị.");
            return false;
        }
        bool tiktokVisible=package==TikTokForegroundProbe.TikTokPackage ||
            TikTokForegroundProbe.SourceHasTikTokUi(source);
        bool launcher=package is "com.android.launcher3" or "com.google.android.apps.nexuslauncher";
        if(!tiktokVisible && !launcher)return false;

        if(tiktokVisible)
        {
            log("Giao diện TikTok chưa về Home/Profile; đóng và mở ứng dụng lại một lần (không xóa dữ liệu).");
            await adb.RunAsync(ct,"-s",device.Serial,"shell","am","force-stop",TikTokForegroundProbe.TikTokPackage);
            await Task.Delay(2000,ct);
        }
        var launchOutput=await adb.LaunchTikTokAsync(device.Serial,ct);
        log("Đã yêu cầu mở lại TikTok; chờ Home/Profile/Login hiển thị (tối đa 90 giây).");
        bool ready=await TikTokForegroundProbe.WaitForReadyUiAsync(session,ct,90);
        if(!ready)log("Sau 90 giây vẫn chưa thấy UI điều hướng. Không kết luận tài khoản bị khóa. " +
                     "ADB launch: " + (launchOutput.Length>160?launchOutput[..160]:launchOutput));
        return ready;
    }
}

/// <summary>
/// Reconciles expected account, verified UI, and persisted account assignment.
/// Policy B: local TikTok data reset replaces fragile UI logout ONLY when the
/// *other* exact handle has an approved account on this same device and no restriction.
/// </summary>
public sealed class SessionReconciler(
    TikTokStateDetector detector, TikTokStateRecovery recovery,
    LoginWorkflow login, AccountVault vault, Action<string> log)
{
    sealed record SecurityCleanupResult(
        TikTokObservation Observation, bool AccountCheckpoint, bool ClearDataUsed);

    public async Task<TikTokObservation> InspectAsync(AppiumSession session, DeviceRecord device,
        ManagedAccount expected, CancellationToken ct, bool allowRecovery = true)
    {
        var accounts=vault.Snapshot().Where(a=>a.DeviceSerial.Equals(device.Serial,StringComparison.OrdinalIgnoreCase))
            .Where(a=>a.State is not ("RESTRICTED" or "DISABLED")).ToList();
        var known=accounts.Select(a=>a.ExpectedTikTokHandle)
            .Where(TikTokStateDetector.IsValidHandle).ToArray();
        // A delayed contacts permission prompt can overlay TikTok after Google
        // authentication and hide the Profile tab. Dismiss it before reading UI.
        // Other permission dialogs and security challenges remain untouched.
        await login.DismissTikTokContactsPermissionIfPresentAsync(session,ct);
        // Google One-tap may be rendered either by Play Services or as an
        // overlay while TikTok remains the reported foreground package. Remove
        // it before every Profile/Google classification; never select its saved
        // identity or Continue button.
        string oneTapSource=await session.SourceAsync(ct);
        if(LoginWorkflow.IsGoogleOneTapSource(oneTapSource))
        {
            log("PREFLIGHT_GOOGLE_ONE_TAP_DETECTED: dismissBeforeProfile=true; savedIdentitySelected=false.");
            if(!await login.DismissGoogleOneTapAsync(session,oneTapSource,ct))
                return new(TikTokUiState.GoogleAuthentication,
                    "GOOGLE_ONE_TAP_DISMISS_FAILED: modal che UI; chuyển recovery ngay, không chờ 75 giây.");
            await Task.Delay(900,ct);
            log("PREFLIGHT_GOOGLE_ONE_TAP_DISMISSED: đọc lại UI từ đầu.");
        }
        var trace=allowRecovery ? new Action<string>(detail => log("Preflight UI: "+detail)) : null;
        TikTokObservation current;
        try { current=await detector.DetectAsync(session,expected.ExpectedTikTokHandle,known,ct,trace); }
        catch(TimeoutException ex)
        {
            log("Lỗi timeout UI: "+ex.Message);
            return new(TikTokUiState.Unknown,"Appium UI timeout: "+ex.Message);
        }
        // Never restart on Google, a security challenge, or an exact account
        // mismatch. Only navigation/load failures are recoverable here.
        if(allowRecovery && IsNavigationFailure(current))
        {
            bool profileBackLimit=current.Detail.Contains(
                "PROFILE_NAVIGATION_NOT_CONFIRMED",StringComparison.Ordinal);
            for(int restartAttempt=1;restartAttempt<=2 && IsNavigationFailure(current);restartAttempt++)
            {
                log($"Preflight UI: FORCE_STOP_RECOVERY_ATTEMPT {restartAttempt}/2; " +
                    "khởi động lại TikTok và quét Profile từ đầu.");
                bool recovered;
                try
                {
                    recovered=await recovery.RecoverNavigationAsync(session,device,ct,
                        forceRestartFirst:profileBackLimit || restartAttempt>1);
                }
                catch(TimeoutException ex)
                {
                    log("Phục hồi UI timeout: "+ex.Message);
                    return new(TikTokUiState.Unknown,"Appium UI timeout: "+ex.Message);
                }
                if(!recovered)
                {
                    log($"Preflight UI: FORCE_STOP_RECOVERY_NOT_READY {restartAttempt}/2.");
                    profileBackLimit=true;
                    continue;
                }
                log("Preflight UI: TikTok đã mở lại; BẮT ĐẦU đọc Profile và username từ đầu.");
                try { current=await detector.DetectAsync(session,expected.ExpectedTikTokHandle,known,ct,trace); }
                catch(TimeoutException ex)
                {
                    log("Đọc Profile sau force-stop timeout: "+ex.Message);
                    current=new(TikTokUiState.Unknown,"Appium UI timeout sau force-stop: "+ex.Message);
                }
                log($"Preflight UI: FORCE_STOP_RECOVERY_RESULT {restartAttempt}/2: {current.State} - {current.Detail}");
                profileBackLimit=true;
            }
            if(IsNavigationFailure(current))
                log("Preflight UI: FORCE_STOP_RECOVERY_EXHAUSTED 2/2; báo lỗi kỹ thuật.");
        }
        return current;
    }

    static bool IsNavigationFailure(TikTokObservation observation) =>
        observation.State==TikTokUiState.Unknown &&
        (observation.Detail.Contains("UI TikTok đã hiện",StringComparison.Ordinal) ||
         observation.Detail.Contains("TikTok chưa sẵn sàng",StringComparison.Ordinal) ||
         observation.Detail.Contains("45 giây UI",StringComparison.Ordinal) ||
         observation.Detail.Contains("PROFILE_NAVIGATION_NOT_CONFIRMED",StringComparison.Ordinal) ||
         observation.Detail.Contains("Appium UI timeout sau force-stop",StringComparison.Ordinal));

    async Task<SecurityCleanupResult> CleanupSecurityChallengeAsync(
        AppiumSession session,DeviceRecord device,ManagedAccount expected,
        TikTokObservation observation,CancellationToken ct,string scope)
    {
        if(observation.State!=TikTokUiState.SecurityChallenge)
            return new(observation,false,false);
        for(int attempt=1;attempt<=2;attempt++)
        {
            string action=attempt==1?"FORCE_STOP":"CLEAR_DATA";
            log($"{scope} SECURITY_CLEANUP_ATTEMPT: {attempt}/2; action={action}; NeedsHuman=DEFERRED.");
            var recoveryOutcome=await recovery.RecoverSecurityChallengeAsync(
                session,device,attempt,ct);
            log($"{scope} SECURITY_RECOVERY_OUTCOME: attempt={attempt}/2; " +
                $"result={recoveryOutcome}.");

            if(attempt>=2 && recoveryOutcome==SecurityRecoveryOutcome.ClearDataReadyForLogin)
            {
                // pm clear guarantees there is no reusable TikTok identity. Do
                // not invoke DetectAsync here: its Profile-navigation strategy
                // belongs to an existing installation and can produce the stale
                // PROFILE_NAVIGATION_NOT_CONFIRMED loop. ReconcileAsync will now
                // enter LoginWorkflow directly for the account reserved by UI.
                log($"{scope} CLEAR_DATA_LOGIN_HANDOFF: skipProfileDetection=true; " +
                    "next=GOOGLE_LOGIN; account="+expected.Email+".");
                return new(new TikTokObservation(TikTokUiState.LoggedOut,
                    "CLEAR_DATA_READY: ứng dụng sạch; chuyển thẳng sang Google Login."),false,true);
            }
            if(attempt>=2 && recoveryOutcome==SecurityRecoveryOutcome.ChallengeStillPresent)
            {
                observation=new(TikTokUiState.SecurityChallenge,
                    "Captcha/Security xuất hiện lại ngay sau Clear Data.");
                log($"{scope} ACCOUNT_CHECKPOINT: challengePresent=true; afterClearData=true.");
                return new(observation,true,true);
            }
            if(attempt>=2 && recoveryOutcome==SecurityRecoveryOutcome.UiNotReady)
            {
                observation=new(TikTokUiState.Unknown,
                    "TikTok UI chưa sẵn sàng sau Clear Data; đã bỏ qua bộ dò Profile cũ.");
                log($"{scope} CLEAR_DATA_UI_NOT_READY: skipProfileDetection=true; technicalRetry=true.");
                return new(observation,false,true);
            }

            observation=await InspectAsync(session,device,expected,ct,allowRecovery:false);
            log($"{scope} SECURITY_CLEANUP_RESULT: {attempt}/2; state={observation.State}; " +
                $"detail={observation.Detail}");
            if(observation.State!=TikTokUiState.SecurityChallenge)
                return new(observation,false,false);
            if(attempt>=2 && recoveryOutcome==SecurityRecoveryOutcome.UiClean)
            {
                // The overlay disappeared during the fifth-check window but
                // immediately returned during the fresh identity read. This is
                // still a persistent challenge; perform the promised escalation
                // rather than misclassifying it as a Profile navigation failure.
                log($"{scope} SECURITY_CHALLENGE_REAPPEARED: afterCleanCheck=true; " +
                    "action=CLEAR_DATA; skipProfileDetectionAfter=true.");
                var clearOutcome=await recovery.ClearDataForCleanLoginAsync(session,device,ct);
                if(clearOutcome==SecurityRecoveryOutcome.ClearDataReadyForLogin)
                    return new(new TikTokObservation(TikTokUiState.LoggedOut,
                        "CLEAR_DATA_READY: ứng dụng sạch; chuyển thẳng sang Google Login."),false,true);
                if(clearOutcome==SecurityRecoveryOutcome.ChallengeStillPresent)
                    return new(new TikTokObservation(TikTokUiState.SecurityChallenge,
                        "Captcha/Security xuất hiện lại ngay sau Clear Data."),true,true);
                return new(new TikTokObservation(TikTokUiState.Unknown,
                    "TikTok UI chưa sẵn sàng sau Clear Data; đã bỏ qua bộ dò Profile cũ."),false,true);
            }
        }
        log($"{scope} SECURITY_CLEANUP_UNRESOLVED: chưa có bằng chứng Clear Data; " +
            "không gán nhầm ACCOUNT_CHECKPOINT.");
        return new(new TikTokObservation(TikTokUiState.Unknown,
            "Không ổn định sau 5 lượt đóng Captcha; cần thử lại kỹ thuật."),false,false);
    }

    // A technical UI/profile failure does not imply the real account was banned.
    // However, an unresolved identity/security issue must never authorize logout.
    static bool IsRecoverableOwnerReview(ManagedAccount account)
    {
        if(account.State!="NEEDS_REVIEW") return false;
        string reason=account.LastResult ?? "";
        // Conservative allowlist for previously observed UI/profile failures only.
        bool technical=AccountVault.IsRecoverableInterruption(account) ||
            reason.StartsWith("Cập nhật hồ sơ chưa hoàn tất:",StringComparison.OrdinalIgnoreCase) ||
            reason.StartsWith("Cập nhật hồ sơ dừng:",StringComparison.OrdinalIgnoreCase) ||
            reason.StartsWith("Lỗi kỹ thuật lặp lại 3 lần;",StringComparison.OrdinalIgnoreCase);
        bool protectedReason=new[] {"captcha", "2fa", "security", "verify your identity",
            "bảo mật", "xác minh danh tính", "tài khoản bị khóa", "account banned",
            "account suspended", "account restricted", "đình chỉ"}.Any(
                marker=>reason.Contains(marker,StringComparison.OrdinalIgnoreCase));
        return technical && !protectedReason;
    }

    static bool IsApprovedOwnerState(ManagedAccount account) => account.State switch
    {
        "AVAILABLE" or "COMPLETED" or "INTERRUPTED" or "RETRY_PENDING" => true,
        "NEEDS_REVIEW" => IsRecoverableOwnerReview(account),
        _ => false
    };

    public async Task<ReconcileResult> ReconcileAsync(AppiumSession session,DeviceRecord device,
        ManagedAccount expected,CancellationToken ct)
    {
        if(!TikTokStateDetector.IsValidHandle(expected.ExpectedTikTokHandle))
            return new(ReconcileOutcome.NeedsReview,
                "Thiếu TikTok handle dự kiến. Hãy cấu hình chính xác handle trước khi Start.");
        TikTokObservation seen=await InspectAsync(session,device,expected,ct);
        var initialSecurity=await CleanupSecurityChallengeAsync(
            session,device,expected,seen,ct,"PREFLIGHT");
        seen=initialSecurity.Observation;
        bool captchaClearDataUsed=initialSecurity.ClearDataUsed;
        int staleGoogleRecoveryAttempts=0;
        if(initialSecurity.AccountCheckpoint)
            return new(ReconcileOutcome.AccountCheckpoint,
                "ACCOUNT_CHECKPOINT: Captcha/Security vẫn xuất hiện ngay sau Clear Data; " +
                "tài khoản sẽ bị bỏ qua để worker tiếp tục queue.");
    EvaluatePreflightState:
        log($"Preflight: {seen.State} - {seen.Detail}");
        switch(seen.State)
        {
            case TikTokUiState.ExpectedAccount:
                return new(ReconcileOutcome.Ready,"Đã ở đúng tài khoản; giữ phiên hiện có.",true);
            case TikTokUiState.AccountRestricted:
                return new(ReconcileOutcome.Restricted,seen.Detail);
            case TikTokUiState.SecurityChallenge:
                return new(ReconcileOutcome.AccountCheckpoint,
                    "ACCOUNT_CHECKPOINT: Security/Captcha vẫn tồn tại sau Clear Data. "+seen.Detail);
            case TikTokUiState.Unknown:
                // A missing Profile / transient launcher / outdated XPath is an
                // observation failure, not evidence that the account is banned.
                // A retry still re-runs the identity preflight; it never blindly
                // logs out, changes users, or skips a security challenge.
                return new(ReconcileOutcome.TechnicalFailure,seen.Detail);
            case TikTokUiState.GoogleAuthentication:
            {
                if(staleGoogleRecoveryAttempts++>=1)
                    return new(ReconcileOutcome.TechnicalFailure,
                        "PREFLIGHT_GOOGLE_RECOVERY_GUARD: Google Authentication vẫn còn sau một chu kỳ recovery; dừng có giới hạn.");
                var googleRecovery=await recovery.RecoverStaleGoogleAuthenticationAsync(
                    session,device,ct);
                if(googleRecovery==StaleGoogleRecoveryOutcome.Failed)
                    return new(ReconcileOutcome.TechnicalFailure,
                        "PREFLIGHT_GOOGLE_RECOVERY_FAILED: đã Back, restart và clear một lần nhưng TikTok chưa sẵn sàng.");
                if(googleRecovery==StaleGoogleRecoveryOutcome.ClearDataReadyForLogin)
                {
                    captchaClearDataUsed=true;
                    log("PREFLIGHT_GOOGLE_CLEAR_DATA_LOGIN_HANDOFF: skipProfileDetection=true; next=GOOGLE_LOGIN.");
                    break;
                }
                seen=await InspectAsync(session,device,expected,ct,allowRecovery:false);
                goto EvaluatePreflightState;
            }
            case TikTokUiState.Onboarding:
                log("PREFLIGHT_ONBOARDING: màn hình thiết lập đang mở; chưa Back hoặc xác minh username.");
                if(!await TikTokForegroundProbe.HandleNewDeviceOnboardingIfPresentAsync(
                    session,UiSelectors.Load(),ct,log))
                    return new(ReconcileOutcome.NeedsHuman,
                        "TikTok chưa hoàn tất onboarding; không đánh dấu lỗi tài khoản.");
                seen=await InspectAsync(session,device,expected,ct,allowRecovery:false);
                if(captchaClearDataUsed && seen.State==TikTokUiState.SecurityChallenge)
                    return new(ReconcileOutcome.AccountCheckpoint,
                        "ACCOUNT_CHECKPOINT: Captcha xuất hiện lại sau Clear Data trong onboarding.");
                var onboardingSecurity=await CleanupSecurityChallengeAsync(
                    session,device,expected,seen,ct,"PREFLIGHT_ONBOARDING");
                seen=onboardingSecurity.Observation;
                captchaClearDataUsed|=onboardingSecurity.ClearDataUsed;
                if(onboardingSecurity.AccountCheckpoint)
                    return new(ReconcileOutcome.AccountCheckpoint,
                        "ACCOUNT_CHECKPOINT: Captcha trở lại sau Clear Data trong onboarding.");
                if(seen.State==TikTokUiState.ExpectedAccount)
                    return new(ReconcileOutcome.Ready,"Đã xác minh đúng username sau onboarding.");
                if(seen.State==TikTokUiState.LoggedOut)
                {
                    log("PREFLIGHT_ONBOARDING_COMPLETE: giao diện đăng nhập đã sẵn sàng; tiếp tục Google Login.");
                    break;
                }
                if(seen.State==TikTokUiState.SecurityChallenge)
                    return new(ReconcileOutcome.AccountCheckpoint,seen.Detail);
                return new(ReconcileOutcome.NeedsHuman,
                    "Onboarding đã xử lý; đang chờ xác minh Profile. "+seen.Detail);
            case TikTokUiState.OtherApprovedAccount:
            case TikTokUiState.DifferentAccount:
            {
                string observed=TikTokStateDetector.NormalizeHandle(seen.VerifiedHandle);
                string selected=TikTokStateDetector.NormalizeHandle(expected.ExpectedTikTokHandle);
                log($"PREFLIGHT_ACCOUNT_MISMATCH: observed=@{observed}; selected=@{selected}; " +
                    "action=CLEAR_TIKTOK_DATA_THEN_GOOGLE_LOGIN.");
                // The owner Profile yielded one exact username. It does not
                // match the account explicitly reserved by this worker, so the
                // old TikTok session must never be reused.
                if(!await recovery.ResetTikTokForApprovedSwitchAsync(session,device,ct))
                    return new(ReconcileOutcome.TechnicalFailure,
                        "Chưa hoàn tất làm sạch TikTok và mở UI; dừng chuyển tài khoản, KHÔNG tự xóa hoặc đăng nhập lặp.");
                captchaClearDataUsed=true;
                // pm clear Success alone is not proof the fresh installation UI
                // is ready for Google login. Re-read the UI from scratch.
                var afterReset=await InspectAsync(session,device,expected,ct,allowRecovery:false);
                if(afterReset.State==TikTokUiState.SecurityChallenge)
                    return new(ReconcileOutcome.AccountCheckpoint,
                        "ACCOUNT_CHECKPOINT: Captcha xuất hiện ngay sau Clear Data chuyển tài khoản.");
                var resetSecurity=await CleanupSecurityChallengeAsync(
                    session,device,expected,afterReset,ct,"PREFLIGHT_AFTER_RESET");
                afterReset=resetSecurity.Observation;
                if(resetSecurity.AccountCheckpoint)
                    return new(ReconcileOutcome.AccountCheckpoint,
                        "ACCOUNT_CHECKPOINT: Captcha trở lại ngay sau khi xóa dữ liệu để chuyển tài khoản.");
                log("Chuyển tài khoản: sau xóa dữ liệu TikTok, giao diện=" + afterReset.State +
                    " - " + afterReset.Detail);
                if(afterReset.State==TikTokUiState.AccountRestricted)
                    return new(ReconcileOutcome.Restricted,afterReset.Detail);
                if(afterReset.State==TikTokUiState.SecurityChallenge)
                    return new(ReconcileOutcome.AccountCheckpoint,afterReset.Detail);
                if(afterReset.State!=TikTokUiState.LoggedOut)
                    return new(ReconcileOutcome.TechnicalFailure,
                        "Dữ liệu TikTok đã được xóa, nhưng giao diện chưa xác nhận LoggedOut. " +
                        "Không tự xóa lần nữa hoặc chọn tài khoản khác.");
                log("Chuyển tài khoản: xác nhận TikTok sạch và chưa đăng nhập; bắt đầu Google Login cho tài khoản được tick.");
                break;
            }
            case TikTokUiState.LoggedOut: break;
            default: return new(ReconcileOutcome.NeedsReview,"Trạng thái TikTok chưa hỗ trợ.");
        }

        var result=await login.LoginAsync(session,expected,ct);
        if(result.Outcome==LoginOutcome.AccountInvalid)
            return new(ReconcileOutcome.AccountInvalid,result.Detail);
        if(result.Outcome==LoginOutcome.Restricted)
            return new(ReconcileOutcome.Restricted,result.Detail);
        if(result.Outcome==LoginOutcome.TechnicalFailure)
            return new(ReconcileOutcome.TechnicalFailure,result.Detail);
        if(result.Outcome==LoginOutcome.NeedsHuman &&
           !result.Detail.StartsWith("Đã đi qua Google Login.",StringComparison.Ordinal))
            return new(ReconcileOutcome.NeedsHuman,result.Detail);
        var confirmed=await InspectAsync(session,device,expected,ct,allowRecovery:false);
        if(captchaClearDataUsed && confirmed.State==TikTokUiState.SecurityChallenge)
            return new(ReconcileOutcome.AccountCheckpoint,
                "ACCOUNT_CHECKPOINT: Captcha xuất hiện ngay sau Clear Data và Google Login sạch.");
        var postGoogleSecurity=await CleanupSecurityChallengeAsync(
            session,device,expected,confirmed,ct,"POST_GOOGLE_PREFLIGHT");
        confirmed=postGoogleSecurity.Observation;
        if(postGoogleSecurity.AccountCheckpoint)
            return new(ReconcileOutcome.AccountCheckpoint,
                "ACCOUNT_CHECKPOINT: Captcha/Security trở lại ngay sau Clear Data và Google Login.");
        if(postGoogleSecurity.ClearDataUsed)
        {
            log("POST_GOOGLE_SECURITY_CLEAR_DATA: phiên TikTok đã bị xóa; bắt đầu lại Google Login sạch đúng một lần.");
            if(confirmed.State==TikTokUiState.Onboarding)
            {
                bool ready=await TikTokForegroundProbe.HandleNewDeviceOnboardingIfPresentAsync(
                    session,UiSelectors.Load(),ct,log,seconds:110);
                if(!ready)
                    return new(ReconcileOutcome.NeedsHuman,
                        "TikTok chưa hoàn tất onboarding sau Clear Data; đang chờ UI sẵn sàng.");
                confirmed=await InspectAsync(session,device,expected,ct,allowRecovery:false);
            }
            if(confirmed.State==TikTokUiState.LoggedOut)
            {
                var cleanLogin=await login.LoginAsync(session,expected,ct);
                if(cleanLogin.Outcome==LoginOutcome.AccountInvalid)
                    return new(ReconcileOutcome.AccountInvalid,cleanLogin.Detail);
                if(cleanLogin.Outcome==LoginOutcome.Restricted)
                    return new(ReconcileOutcome.Restricted,cleanLogin.Detail);
                if(cleanLogin.Outcome==LoginOutcome.TechnicalFailure)
                    return new(ReconcileOutcome.TechnicalFailure,cleanLogin.Detail);
                confirmed=await InspectAsync(session,device,expected,ct,allowRecovery:false);
                if(confirmed.State==TikTokUiState.SecurityChallenge)
                    return new(ReconcileOutcome.AccountCheckpoint,
                        "ACCOUNT_CHECKPOINT: Captcha xuất hiện ngay sau Clear Data và lần Google Login sạch.");
            }
        }
        if(confirmed.State==TikTokUiState.ExpectedAccount)
            return new(ReconcileOutcome.Ready,"Đã xác minh hồ sơ TikTok sau đăng nhập.");
        if(confirmed.State==TikTokUiState.AccountRestricted)
            return new(ReconcileOutcome.Restricted,confirmed.Detail);
        if(confirmed.State==TikTokUiState.SecurityChallenge)
            return new(ReconcileOutcome.AccountCheckpoint,
                "ACCOUNT_CHECKPOINT: Captcha/Security vẫn còn hậu đăng nhập. "+confirmed.Detail);
        if(confirmed.State is TikTokUiState.OtherApprovedAccount or TikTokUiState.DifferentAccount)
            return new(ReconcileOutcome.NeedsReview,"Sau Google login xuất hiện tài khoản TikTok khác; đã dừng an toàn.");
        return new(ReconcileOutcome.NeedsHuman,
            "Chưa xác minh đúng TikTok handle. Tool sẽ tự kiểm tra lại; nếu có 2FA/CAPTCHA, hãy xác minh trên điện thoại. "+confirmed.Detail);
    }

    // Passive, bounded observation: does not click through security prompts, enter
    // credentials, or claim success based on elapsed time or an operator's click.
    // This is only used after an ordinary authentication flow returned NeedsHuman.
    public async Task<ReconcileResult> WaitForVerifiedProfileAsync(
        AppiumSession session, DeviceRecord device, ManagedAccount expected,
        Action<string> updateStatus, CancellationToken ct)
    {
        var deadline=DateTimeOffset.UtcNow.AddMinutes(3);
        TikTokUiState? previous=null;
        bool navigationRecoveryAttempted=false;
        bool captchaClearDataUsed=false;
        TikTokObservation? lastObservation=null;
        while(DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            TikTokObservation observation;
            try
            {
                observation=await InspectAsync(session,device,expected,ct,allowRecovery:false);
            }
            catch(OperationCanceledException) when(ct.IsCancellationRequested) { throw; }
            catch(Exception ex)
            {
                return new(ReconcileOutcome.TechnicalFailure,
                    $"Không đọc được giao diện khi tự kiểm tra lại ({ex.GetType().Name}).");
            }
            if(observation.State==TikTokUiState.SecurityChallenge)
            {
                if(captchaClearDataUsed)
                    return new(ReconcileOutcome.AccountCheckpoint,
                        "ACCOUNT_CHECKPOINT: Captcha xuất hiện lại sau Clear Data trong lúc xác minh.");
                updateStatus("Phát hiện Captcha/Security; lần 1 restart, lần 2 Clear Data và đăng nhập sạch.");
                var waitSecurity=await CleanupSecurityChallengeAsync(
                    session,device,expected,observation,ct,"WAIT_VERIFY");
                observation=waitSecurity.Observation;
                captchaClearDataUsed|=waitSecurity.ClearDataUsed;
                if(waitSecurity.AccountCheckpoint)
                    return new(ReconcileOutcome.AccountCheckpoint,
                        "ACCOUNT_CHECKPOINT: Captcha/Security vẫn tồn tại ngay sau Clear Data.");
                if(observation.State==TikTokUiState.SecurityChallenge)
                    return new(ReconcileOutcome.AccountCheckpoint,
                        "ACCOUNT_CHECKPOINT: Captcha/Security chưa được dọn sau Clear Data. "+observation.Detail);
                if(waitSecurity.ClearDataUsed)
                {
                    updateStatus("Đã Clear Data để dọn Captcha; đang đăng nhập Google sạch lại đúng tài khoản.");
                    if(observation.State==TikTokUiState.Onboarding)
                    {
                        bool ready=await TikTokForegroundProbe.HandleNewDeviceOnboardingIfPresentAsync(
                            session,UiSelectors.Load(),ct,log,seconds:110);
                        if(!ready)
                            return new(ReconcileOutcome.NeedsHuman,
                                "TikTok chưa hoàn tất onboarding sau Clear Data.");
                        observation=await InspectAsync(session,device,expected,ct,allowRecovery:false);
                    }
                    if(observation.State==TikTokUiState.LoggedOut)
                    {
                        var cleanLogin=await login.LoginAsync(session,expected,ct);
                        if(cleanLogin.Outcome==LoginOutcome.AccountInvalid)
                            return new(ReconcileOutcome.AccountInvalid,cleanLogin.Detail);
                        if(cleanLogin.Outcome==LoginOutcome.Restricted)
                            return new(ReconcileOutcome.Restricted,cleanLogin.Detail);
                        if(cleanLogin.Outcome==LoginOutcome.TechnicalFailure)
                            return new(ReconcileOutcome.TechnicalFailure,cleanLogin.Detail);
                        observation=await InspectAsync(session,device,expected,ct,allowRecovery:false);
                        if(observation.State==TikTokUiState.SecurityChallenge)
                            return new(ReconcileOutcome.AccountCheckpoint,
                                "ACCOUNT_CHECKPOINT: Captcha xuất hiện ngay sau lần Google Login sạch.");
                    }
                }
                navigationRecoveryAttempted=false;
            }
            if(observation.State==TikTokUiState.Onboarding)
            {
                updateStatus("POST_GOOGLE_ONBOARDING_DETECTED: tiếp tục thiết lập TikTok; chưa tìm Profile.");
                bool onboardingReady=await TikTokForegroundProbe.HandleNewDeviceOnboardingIfPresentAsync(
                    session,UiSelectors.Load(),ct,log,seconds:110);
                if(!onboardingReady)
                    return new(ReconcileOutcome.NeedsHuman,
                        "TikTok chưa hoàn tất onboarding sau Google; không coi là lỗi tài khoản.");
                // New hierarchy after onboarding, re-inspect without Back.
                navigationRecoveryAttempted=false;
                continue;
            }
            // The human has completed the challenge if it is no longer visible.
            // A LIVE room may now be foreground, with no Profile tab at all.
            // One bounded navigation recovery is safe ONLY on a navigation failure;
            // never on an active challenge or an unknown/unapproved username.
            if(!navigationRecoveryAttempted && IsNavigationFailure(observation))
            {
                navigationRecoveryAttempted=true;
                updateStatus("Xác minh không còn hiển thị; đưa TikTok về màn hình có Profile/Login.");
                log("Sau xác minh, UI chưa có Profile (có thể đang xem LIVE); thử Back, sau đó mở lại TikTok nếu cần.");
                bool recovered=await recovery.RecoverNavigationAsync(session,device,ct);
                if(recovered)
                    observation=await InspectAsync(session,device,expected,ct,allowRecovery:false);
                else log("Chưa đưa TikTok về Home/Profile; tiếp tục quan sát đến hết thời gian cho phép.");
            }
            lastObservation=observation;
            if(observation.State!=previous)
            {
                previous=observation.State;
                log($"Tự kiểm tra phiên Google/TikTok: {observation.State} - {observation.Detail}");
            }
            switch(observation.State)
            {
                case TikTokUiState.ExpectedAccount:
                    return new(ReconcileOutcome.Ready,
                        "Đã tự xác minh đúng handle TikTok; không cần nhấn nút xác nhận.");
                case TikTokUiState.AccountRestricted:
                    return new(ReconcileOutcome.Restricted,observation.Detail);
                case TikTokUiState.OtherApprovedAccount:
                case TikTokUiState.DifferentAccount:
                    return new(ReconcileOutcome.NeedsReview,
                        "Phát hiện tài khoản khác sau xác thực; không tự chuyển phiên.");
                case TikTokUiState.SecurityChallenge:
                    return new(ReconcileOutcome.AccountCheckpoint,
                        "ACCOUNT_CHECKPOINT: Captcha/Security chưa được dọn sau Clear Data.");
                case TikTokUiState.GoogleAuthentication:
                    updateStatus("Đang chờ Google trở về TikTok; tool tự kiểm tra lại.");
                    break;
                default:
                    updateStatus("Đang tự kiểm tra trạng thái TikTok; chưa có đủ bằng chứng đúng tài khoản.");
                    break;
            }
            var remaining=deadline-DateTimeOffset.UtcNow;
            if(remaining > TimeSpan.Zero)
                await Task.Delay(remaining < TimeSpan.FromSeconds(5) ? remaining : TimeSpan.FromSeconds(5),ct);
        }
        if(lastObservation is not null && IsNavigationFailure(lastObservation))
            return new(ReconcileOutcome.TechnicalFailure,
                "Hết thời gian vì UI TikTok chưa về trạng thái điều hướng; có thể thử lại sau lỗi kỹ thuật.");
        return new(ReconcileOutcome.NeedsReview,
            "Quá 3 phút vẫn chưa xác minh được đúng TikTok handle; máy đã dừng để kiểm tra.");
    }

    public async Task<bool> LogoutAtEndAsync(AppiumSession session,DeviceRecord device,
        ManagedAccount expected,CancellationToken ct)
    {
        var current=await InspectAsync(session,device,expected,ct,allowRecovery:false);
        if(current.State==TikTokUiState.LoggedOut)return true;
        if(current.State!=TikTokUiState.ExpectedAccount) return false;
        log("Kết thúc phiên: đã xác minh đúng tài khoản đang chạy; dọn dữ liệu TikTok thay vì thao tác Log out.");
        if(!await recovery.ResetTikTokForApprovedSwitchAsync(session,device,ct))return false;
        var after=await InspectAsync(session,device,expected,ct,allowRecovery:false);
        if(after.State!=TikTokUiState.LoggedOut)
            log("Kết thúc phiên: đã xóa dữ liệu nhưng UI chưa xác nhận LoggedOut ("+after.State+").");
        return after.State==TikTokUiState.LoggedOut;
    }
}
