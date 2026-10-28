using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AndroidMultiDeviceV1.Core;

public sealed class UiSelectors
{
    readonly Dictionary<string,List<UiLocator>> _lookup;
    UiSelectors(Dictionary<string,List<UiLocator>> lookup)=>_lookup=lookup;
    public static UiSelectors Load()
    {
        string local=Path.Combine(AppContext.BaseDirectory,"Config","selectors.json");
        if(!File.Exists(local)) throw new FileNotFoundException("Thiếu file Config/selectors.json",local);
        var config=JsonSerializer.Deserialize<Dictionary<string,List<UiLocator>>>(File.ReadAllText(local),Storage.JsonOptions);
        return new UiSelectors(config??[]);
    }
    public IReadOnlyList<UiLocator> Get(string step)
        => _lookup.TryGetValue(step,out var l)?l:[];
}

/// <summary>
/// One authorized login attempt for the already-assigned account on one Android device.
/// No selecting arbitrary saved Google identities, CAPTCHA/2FA bypass, or account fallback.
/// </summary>
public sealed class LoginWorkflow(UiSelectors selectors,Action<string> log,
    AdbClient? adb=null,string deviceSerial="")
{
    const int PollMs=650;

    async Task<bool> Exists(AppiumSession session,string step,CancellationToken ct)
        => await session.ExistsAnyAsync(selectors.Get(step),ct);
    async Task<bool> Tap(AppiumSession session,string step,CancellationToken ct)
        => await session.ClickAnyAsync(selectors.Get(step),ct);

    async Task<bool> WaitFor(AppiumSession session,string step,int timeoutSeconds,CancellationToken ct)
    {
        var timer=Stopwatch.StartNew();
        do
        {
            ct.ThrowIfCancellationRequested();
            if(await Exists(session,step,ct))return true;
            await Task.Delay(PollMs,ct);
        } while(timer.Elapsed<TimeSpan.FromSeconds(timeoutSeconds));
        return false;
    }
    async Task<bool> WaitTap(AppiumSession session,string step,int seconds,CancellationToken ct)
    {
        var timer=Stopwatch.StartNew();
        do
        {
            ct.ThrowIfCancellationRequested();
            if(await Tap(session,step,ct))return true;
            await Task.Delay(PollMs,ct);
        } while(timer.Elapsed<TimeSpan.FromSeconds(seconds));
        return false;
    }

    static string NodeLabel(XElement node)
        => $"{(string?)node.Attribute("text")} {(string?)node.Attribute("content-desc")}".Trim();

    static bool IsGoogleLoginOptionVisible(string xml)
    {
        try
        {
            var root=XDocument.Parse(xml).Root;
            return root is not null && root.DescendantsAndSelf().Any(node=>
            {
                string label=NodeLabel(node);
                return label.Contains("Continue with Google",StringComparison.OrdinalIgnoreCase) ||
                    label.Contains("Tiếp tục với Google",StringComparison.OrdinalIgnoreCase);
            });
        }
        catch(System.Xml.XmlException){return false;}
    }

    // A collapsed Compose login sheet does not expose the Google row yet. Verify
    // the surrounding TikTok login UI before considering any unlabeled ImageView,
    // so an avatar or a content icon can never be used as the expand control.
    static bool IsTikTokLoginMethodsShell(string xml)
    {
        try
        {
            var root=XDocument.Parse(xml).Root;
            if(root is null)return false;
            var nodes=root.DescendantsAndSelf().ToArray();
            bool compose=nodes.Any(node=>
                (((string?)node.Attribute("class"))??node.Name.LocalName)
                    .Contains("ComposeView",StringComparison.OrdinalIgnoreCase));
            if(!compose)return false;
            string labels=string.Join('\n',nodes.Select(NodeLabel));
            bool title=labels.Contains("Log in to TikTok",StringComparison.OrdinalIgnoreCase) ||
                labels.Contains("Đăng nhập vào TikTok",StringComparison.OrdinalIgnoreCase);
            string[] methodLabels=["Use phone / email / username","Continue with Facebook",
                "Continue with Apple","Continue with QR code","Sử dụng điện thoại",
                "Tiếp tục với Facebook","Tiếp tục với Apple"];
            int methodCount=methodLabels.Count(label=>
                labels.Contains(label,StringComparison.OrdinalIgnoreCase));
            return title && methodCount>=1 || methodCount>=2;
        }
        catch(System.Xml.XmlException){return false;}
    }

    static (int X,int Y)? FindSafeLoginExpandPoint(string xml,int screenLeft,int screenTop,
        int screenWidth,int screenHeight)
    {
        try
        {
            var root=XDocument.Parse(xml).Root;
            if(root is null)return null;
            var nodes=root.DescendantsAndSelf();
            int screenCenterX=screenLeft+screenWidth/2;
            int screenCenterY=screenTop+screenHeight/2;
            return nodes.Select(node=>
                {
                    string widget=((string?)node.Attribute("class")??node.Name.LocalName);
                    if(!widget.Contains("ImageView",StringComparison.OrdinalIgnoreCase))
                        return (Valid:false,X:0,Y:0,Score:int.MaxValue);
                    string label=NodeLabel(node);
                    if(label.Contains("logo",StringComparison.OrdinalIgnoreCase) ||
                       label.Contains("avatar",StringComparison.OrdinalIgnoreCase) ||
                       label.Contains("profile",StringComparison.OrdinalIgnoreCase))
                        return (Valid:false,X:0,Y:0,Score:int.MaxValue);
                    var match=Regex.Match((string?)node.Attribute("bounds")??string.Empty,
                        @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]");
                    if(!match.Success)return (Valid:false,X:0,Y:0,Score:int.MaxValue);
                    int left=int.Parse(match.Groups[1].Value),top=int.Parse(match.Groups[2].Value);
                    int right=int.Parse(match.Groups[3].Value),bottom=int.Parse(match.Groups[4].Value);
                    int width=right-left,height=bottom-top,x=(left+right)/2,y=(top+bottom)/2;
                    bool clickable=string.Equals((string?)node.Attribute("clickable"),"true",
                        StringComparison.OrdinalIgnoreCase) || node.Ancestors().Take(3).Any(parent=>
                        string.Equals((string?)parent.Attribute("clickable"),"true",
                            StringComparison.OrdinalIgnoreCase));
                    bool safeSize=width is >=12 and <=140 && height is >=12 and <=140;
                    bool centered=Math.Abs(x-screenCenterX)<=screenWidth/5;
                    bool inside=y>screenTop+screenHeight/5 && y<screenTop+screenHeight*9/10;
                    int score=Math.Abs(x-screenCenterX)*4+Math.Abs(y-screenCenterY);
                    return (Valid:clickable && safeSize && centered && inside,X:x,Y:y,Score:score);
                })
                .Where(candidate=>candidate.Valid)
                .OrderBy(candidate=>candidate.Score)
                .Select(candidate=>((int X,int Y)?)(candidate.X,candidate.Y))
                .FirstOrDefault();
        }
        catch(System.Xml.XmlException){return null;}
    }

    async Task<bool> ExpandTikTokLoginOptionsAsync(AppiumSession session,string source,
        CancellationToken ct)
    {
        if(IsGoogleLoginOptionVisible(source) || !IsTikTokLoginMethodsShell(source))return false;
        bool tapped=await Tap(session,"expandLoginOptions",ct);
        string evidence=tapped?"SEMANTIC_LOCATOR":"NONE";
        if(!tapped)
        {
            var window=await session.WindowRectAsync(ct);
            var point=FindSafeLoginExpandPoint(source,window.X,window.Y,window.Width,window.Height);
            if(point.HasValue)
            {
                await session.TapUiPointAsync(point.Value.X,point.Value.Y,ct);
                tapped=true;
                evidence=$"VERIFIED_COMPOSE_IMAGE; tap=({point.Value.X},{point.Value.Y})";
            }
        }
        if(!tapped)
        {
            log("GOOGLE_LOGIN_OPTIONS_EXPAND_NOT_FOUND: popup đã xác minh nhưng không có mũi tên an toàn.");
            return false;
        }
        log($"GOOGLE_LOGIN_OPTIONS_EXPAND_REQUESTED: evidence={evidence}.");
        var wait=Stopwatch.StartNew();
        while(wait.Elapsed<TimeSpan.FromSeconds(6))
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(PollMs,ct);
            if(IsGoogleLoginOptionVisible(await session.SourceAsync(ct)))
            {
                log("GOOGLE_LOGIN_OPTIONS_EXPANDED: Continue with Google đã hiển thị.");
                return true;
            }
        }
        log("GOOGLE_LOGIN_OPTIONS_EXPAND_UNCONFIRMED: chưa thấy Continue with Google sau khi mở rộng.");
        return false;
    }

    static bool IsTrustedGoogleContext(string package,string url)
    {
        if(package is "com.google.android.gms" or "com.google.android.gsf.login")return true;
        if(package is "com.android.chrome" or "com.google.android.googlequicksearchbox")
            return Uri.TryCreate(url,UriKind.Absolute,out var uri) &&
                   uri.Scheme==Uri.UriSchemeHttps &&
                   uri.Host.Equals("accounts.google.com",StringComparison.OrdinalIgnoreCase);
        return false;
    }
    async Task<bool> VerifyGoogleContext(AppiumSession session,CancellationToken ct)
    {
        string package=await session.CurrentPackageAsync(ct);
        string url=await session.CurrentUrlAsync(ct);
        return IsTrustedGoogleContext(package,url);
    }

    static string? GooglePermanentFailureCode(string source)
    {
        string visible;
        try
        {
            var root=XDocument.Parse(source).Root;
            if(root is null)return null;
            visible=string.Join('\n',root.DescendantsAndSelf().Select(NodeLabel));
        }
        catch(System.Xml.XmlException){return null;}
        if(visible.Contains("2-Step Verification policy",StringComparison.OrdinalIgnoreCase) ||
           visible.Contains("2 step verification policy",StringComparison.OrdinalIgnoreCase) ||
           visible.Contains("sign-in settings don't meet",StringComparison.OrdinalIgnoreCase) ||
           visible.Contains("sign in settings don't meet",StringComparison.OrdinalIgnoreCase) ||
           visible.Contains("organization's 2-Step Verification",StringComparison.OrdinalIgnoreCase) ||
           visible.Contains("chính sách Xác minh 2 bước",StringComparison.OrdinalIgnoreCase))
            return "GOOGLE_2SV_POLICY";
        if(visible.Contains("Couldn't sign you in",StringComparison.OrdinalIgnoreCase) ||
           visible.Contains("Could not sign you in",StringComparison.OrdinalIgnoreCase) ||
           visible.Contains("Không thể đăng nhập cho bạn",StringComparison.OrdinalIgnoreCase))
            return "GOOGLE_SIGN_IN_REJECTED";
        if(visible.Contains("account has been disabled",StringComparison.OrdinalIgnoreCase) ||
           visible.Contains("account is disabled",StringComparison.OrdinalIgnoreCase) ||
           visible.Contains("tài khoản đã bị vô hiệu hóa",StringComparison.OrdinalIgnoreCase) ||
           visible.Contains("account has been locked",StringComparison.OrdinalIgnoreCase))
            return "GOOGLE_ACCOUNT_DISABLED";
        return null;
    }

    async Task<LoginResult?> DetectGoogleAccountInvalidAsync(
        AppiumSession session,CancellationToken ct)
    {
        if(!await VerifyGoogleContext(session,ct))return null;
        string? code=GooglePermanentFailureCode(await session.SourceAsync(ct));
        if(code is null)return null;
        log($"GOOGLE_ACCOUNT_INVALID_CONFIRMED: code={code}; action=ROTATE_LOCAL_POOL.");
        return new(LoginOutcome.AccountInvalid,
            $"{code}: Google từ chối đăng nhập hoặc tài khoản không đáp ứng chính sách bảo mật.");
    }

    static bool IsGoogleVerifyIdentitySource(string source)
    {
        try
        {
            var root=XDocument.Parse(source).Root;
            if(root is null)return false;
            string visible=Regex.Replace(
                string.Join(' ',root.DescendantsAndSelf().Select(NodeLabel)),@"\s+"," ");
            return visible.Contains("Verify it's really you",StringComparison.OrdinalIgnoreCase) ||
                visible.Contains("Verify it’s really you",StringComparison.OrdinalIgnoreCase) ||
                visible.Contains("Choose a method to verify your identity",StringComparison.OrdinalIgnoreCase) ||
                visible.Contains("Choose a method to verify your identify",StringComparison.OrdinalIgnoreCase) ||
                visible.Contains("Xác minh rằng đó thực sự là bạn",StringComparison.OrdinalIgnoreCase) ||
                visible.Contains("Chọn một phương thức để xác minh danh tính",StringComparison.OrdinalIgnoreCase);
        }
        catch(System.Xml.XmlException){return false;}
    }

    static bool IsGoogleAuthenticationPackage(string package) => package is
        "com.google.android.gms" or "com.google.android.gsf.login" or
        "com.android.chrome" or "com.google.android.googlequicksearchbox";

    static string CompactUiText(string value)
        => Regex.Replace(value,@"[^\p{L}\p{N}]",string.Empty).ToLowerInvariant();

    public static bool IsGoogleOneTapSource(string source)
    {
        try
        {
            var root=XDocument.Parse(source).Root;
            if(root is null)return false;
            string visible=Regex.Replace(
                string.Join(' ',root.DescendantsAndSelf().Select(NodeLabel)),@"\s+"," ");
            string compact=CompactUiText(visible);
            if(compact.Contains("verifyitsreallyyou",StringComparison.Ordinal) ||
               compact.Contains("chooseamethodtoverifyyouridentity",StringComparison.Ordinal) ||
               compact.Contains("chooseamethodtoverifyyouridentify",StringComparison.Ordinal))
                return false;
            return compact.Contains("useyouraccountfortiktok",StringComparison.Ordinal) ||
                compact.Contains("signinwithgoogle",StringComparison.Ordinal) ||
                compact.Contains("signinginwithgoogle",StringComparison.Ordinal);
        }
        catch(System.Xml.XmlException){return false;}
    }

    static int? GoogleOneTapTop(string source)
    {
        try
        {
            var root=XDocument.Parse(source).Root;
            if(root is null)return null;
            XElement? title=root.DescendantsAndSelf().FirstOrDefault(node=>
            {
                string compact=CompactUiText(NodeLabel(node));
                return compact.Contains("useyouraccountfortiktok",StringComparison.Ordinal) ||
                    compact.Contains("signinwithgoogle",StringComparison.Ordinal) ||
                    compact.Contains("signinginwithgoogle",StringComparison.Ordinal);
            });
            if(title is null)return null;
            return title.AncestorsAndSelf().Select(node=>
                {
                    var match=Regex.Match((string?)node.Attribute("bounds")??string.Empty,
                        @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]");
                    if(!match.Success)return (Valid:false,Top:0,Width:0,Height:0);
                    int left=int.Parse(match.Groups[1].Value),top=int.Parse(match.Groups[2].Value);
                    int right=int.Parse(match.Groups[3].Value),bottom=int.Parse(match.Groups[4].Value);
                    return (Valid:right>left && bottom>top,Top:top,Width:right-left,Height:bottom-top);
                })
                .Where(item=>item.Valid && item.Height>=180)
                .OrderByDescending(item=>item.Width)
                .ThenByDescending(item=>item.Height)
                .Select(item=>(int?)item.Top)
                .FirstOrDefault();
        }
        catch(System.Xml.XmlException){return null;}
    }

    public async Task<bool> DismissGoogleOneTapAsync(
        AppiumSession session,string source,CancellationToken ct)
    {
        if(!IsGoogleOneTapSource(source))return false;
        string package=await session.CurrentPackageAsync(ct);
        bool trustedForeground=IsGoogleAuthenticationPackage(package) ||
            package==TikTokForegroundProbe.TikTokPackage;
        if(!trustedForeground)return false;
        log("GOOGLE_ONE_TAP_DETECTED: savedIdentitySelection=false; action=DISMISS.");
        bool clicked=await Tap(session,"googleOneTapClose",ct);
        string evidence=clicked?"SEMANTIC_CLOSE":"NONE";
        if(!clicked)
        {
            var window=await session.WindowRectAsync(ct);
            int modalTop=GoogleOneTapTop(source)??(window.Y+window.Height*45/100);
            int x=window.X+Math.Max(24,window.Width*8/100);
            int y=Math.Clamp(modalTop-36,window.Y+36,window.Y+window.Height*32/100);
            await session.TapUiPointAsync(x,y,ct);
            clicked=true;
            evidence=$"OUTSIDE_VERIFIED_MODAL; tap=({x},{y})";
        }
        log($"GOOGLE_ONE_TAP_DISMISS_REQUESTED: evidence={evidence}.");
        var wait=Stopwatch.StartNew();
        while(wait.Elapsed<TimeSpan.FromSeconds(8))
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(PollMs,ct);
            if(!IsGoogleOneTapSource(await session.SourceAsync(ct)))
            {
                log("GOOGLE_ONE_TAP_DISMISSED: retryContinueGoogle=true; savedIdentitySelected=false.");
                return true;
            }
        }
        log("GOOGLE_ONE_TAP_DISMISS_UNCONFIRMED: modal vẫn còn; không bấm Continue/tài khoản cũ.");
        return false;
    }

    static (int X,int Y)? FindGoogleVerifyClosePoint(string source)
    {
        try
        {
            var root=XDocument.Parse(source).Root;
            if(root is null)return null;
            XElement? dialogRoot=root.DescendantsAndSelf().FirstOrDefault(node=>
            {
                string resource=(string?)node.Attribute("resource-id")??string.Empty;
                return resource.Equals("root",StringComparison.OrdinalIgnoreCase) ||
                    resource.EndsWith(":id/root",StringComparison.OrdinalIgnoreCase);
            });
            dialogRoot??=root;
            var rootBounds=Regex.Match((string?)dialogRoot.Attribute("bounds")??string.Empty,
                @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]");
            if(!rootBounds.Success)return null;
            int rootLeft=int.Parse(rootBounds.Groups[1].Value),rootTop=int.Parse(rootBounds.Groups[2].Value);
            int rootRight=int.Parse(rootBounds.Groups[3].Value),rootBottom=int.Parse(rootBounds.Groups[4].Value);
            int rootWidth=rootRight-rootLeft,rootHeight=rootBottom-rootTop;
            return dialogRoot.DescendantsAndSelf().Select(node=>
                {
                    string widget=(string?)node.Attribute("class")??node.Name.LocalName;
                    if(!widget.Contains("Button",StringComparison.OrdinalIgnoreCase))
                        return (Valid:false,X:0,Y:0,Score:int.MaxValue);
                    var bounds=Regex.Match((string?)node.Attribute("bounds")??string.Empty,
                        @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]");
                    if(!bounds.Success)return (Valid:false,X:0,Y:0,Score:int.MaxValue);
                    int left=int.Parse(bounds.Groups[1].Value),top=int.Parse(bounds.Groups[2].Value);
                    int right=int.Parse(bounds.Groups[3].Value),bottom=int.Parse(bounds.Groups[4].Value);
                    int width=right-left,height=bottom-top,x=(left+right)/2,y=(top+bottom)/2;
                    string label=NodeLabel(node);
                    bool semantic=label.Equals("Close",StringComparison.OrdinalIgnoreCase) ||
                        label.Equals("Dismiss",StringComparison.OrdinalIgnoreCase) || label=="X" || label=="×";
                    bool topRight=x>=rootLeft+rootWidth*65/100 &&
                        y<=rootTop+rootHeight*30/100;
                    bool safeSize=width is >=12 and <=140 && height is >=12 and <=140;
                    int score=(rootRight-x)+(y-rootTop);
                    return (Valid:safeSize && (semantic || topRight),X:x,Y:y,Score:score);
                })
                .Where(candidate=>candidate.Valid)
                .OrderBy(candidate=>candidate.Score)
                .Select(candidate=>((int X,int Y)?)(candidate.X,candidate.Y))
                .FirstOrDefault();
        }
        catch(System.Xml.XmlException){return null;}
    }

    async Task<bool> DismissGoogleVerifyIdentityAsync(
        AppiumSession session,string source,CancellationToken ct)
    {
        if(!IsGoogleVerifyIdentitySource(source))return false;
        string package=await session.CurrentPackageAsync(ct);
        if(!IsGoogleAuthenticationPackage(package) &&
           package!=TikTokForegroundProbe.TikTokPackage)return false;
        log("GOOGLE_VERIFY_IDENTITY_DETECTED: challenge có thể hủy; accountInvalid=false.");
        bool clicked=await Tap(session,"googleVerifyClose",ct);
        string evidence=clicked?"SEMANTIC_CLOSE":"NONE";
        if(!clicked)
        {
            var point=FindGoogleVerifyClosePoint(source);
            if(point.HasValue)
            {
                await session.TapUiPointAsync(point.Value.X,point.Value.Y,ct);
                clicked=true;
                evidence=$"VERIFIED_TOP_RIGHT_BUTTON; tap=({point.Value.X},{point.Value.Y})";
            }
        }
        if(!clicked)
        {
            log("GOOGLE_VERIFY_IDENTITY_CLOSE_NOT_FOUND: không click nút phương thức Phone/Passkey.");
            return false;
        }
        log($"GOOGLE_VERIFY_IDENTITY_CLOSE_REQUESTED: evidence={evidence}.");
        var wait=Stopwatch.StartNew();
        while(wait.Elapsed<TimeSpan.FromSeconds(12))
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(PollMs,ct);
            if(!IsGoogleVerifyIdentitySource(await session.SourceAsync(ct)))
            {
                log("GOOGLE_VERIFY_IDENTITY_DISMISSED: restartSameAccount=true; retry=1/1.");
                return true;
            }
        }
        log("GOOGLE_VERIFY_IDENTITY_DISMISS_UNCONFIRMED: challenge vẫn còn sau nút X.");
        return false;
    }

    async Task<bool> RetryContinueWithGoogleAsync(AppiumSession session,CancellationToken ct)
    {
        string package=await session.CurrentPackageAsync(ct);
        bool gmsProcessRunning=false;
        if(adb is not null && !string.IsNullOrWhiteSpace(deviceSerial))
        {
            try
            {
                string pid=await adb.RunAsync(ct,"-s",deviceSerial,"shell","pidof","com.google.android.gms");
                gmsProcessRunning=!string.IsNullOrWhiteSpace(pid);
            }
            catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
            catch(Exception)
            {
                // pidof exits non-zero when no process exists. This diagnostic
                // must never turn a slow Google UI into a worker failure.
                gmsProcessRunning=false;
            }
        }
        bool googleForeground=package is "com.google.android.gms" or "com.google.android.gsf.login" or
            "com.android.chrome" or "com.google.android.googlequicksearchbox";
        log($"GOOGLE_PICKER_SLOW_CHECK: elapsedSec=45; foreground={package}; " +
            $"gmsProcessRunning={gmsProcessRunning}; retryBudget=1/1.");

        // A half-rendered Google activity can own the foreground without exposing
        // chooser nodes. Cancel that one pending launch, then retry only after the
        // verified TikTok method sheet is visible again.
        if(googleForeground)
        {
            await session.BackAsync(ct);
            var returnWait=Stopwatch.StartNew();
            do
            {
                await Task.Delay(PollMs,ct);
                package=await session.CurrentPackageAsync(ct);
                if(package==TikTokForegroundProbe.TikTokPackage)break;
            } while(returnWait.Elapsed<TimeSpan.FromSeconds(10));
        }
        if(package!=TikTokForegroundProbe.TikTokPackage)return false;
        string source=await session.SourceAsync(ct);
        if(!IsTikTokLoginMethodsShell(source) &&
           !TikTokForegroundProbe.IsTikTokLoginMethodsScreen(source))return false;
        if(!IsGoogleLoginOptionVisible(source))
        {
            await ExpandTikTokLoginOptionsAsync(session,source,ct);
            source=await session.SourceAsync(ct);
        }
        if(!IsGoogleLoginOptionVisible(source))return false;
        if(!await Tap(session,"continueGoogle",ct))return false;
        log("GOOGLE_PICKER_RETRY_CLICKED: Continue with Google lần 2/2; " +
            "tiếp tục chờ trong tổng ngân sách 90 giây.");
        return true;
    }

    static bool IsUseAnotherAccountNode(XElement node)
    {
        string text=((string?)node.Attribute("text")??string.Empty).Trim();
        string description=((string?)node.Attribute("content-desc")??string.Empty).Trim();
        string resource=(string?)node.Attribute("resource-id")??string.Empty;
        string[] labels=["Add another account","Use another account",
            "Thêm tài khoản khác","Sử dụng tài khoản khác"];
        return labels.Contains(text,StringComparer.OrdinalIgnoreCase) ||
            labels.Contains(description,StringComparer.OrdinalIgnoreCase) ||
            resource.Contains("add_account",StringComparison.OrdinalIgnoreCase);
    }

    static (int Left,int Top,int Width,int Height)? GoogleChooserListBounds(string xml)
    {
        try
        {
            var nodes=XDocument.Parse(xml).Descendants();
            XElement? list=nodes.FirstOrDefault(node=>
                ((string?)node.Attribute("resource-id")??string.Empty)
                    .Equals("com.google.android.gms:id/list",StringComparison.OrdinalIgnoreCase));
            list??=nodes.FirstOrDefault(node=>
                string.Equals((string?)node.Attribute("scrollable"),"true",StringComparison.OrdinalIgnoreCase) &&
                (((string?)node.Attribute("class")??string.Empty).Contains("RecyclerView",StringComparison.OrdinalIgnoreCase) ||
                 ((string?)node.Attribute("class")??string.Empty).Contains("ScrollView",StringComparison.OrdinalIgnoreCase)));
            var match=Regex.Match((string?)list?.Attribute("bounds")??string.Empty,
                @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]");
            if(!match.Success)return null;
            int left=int.Parse(match.Groups[1].Value),top=int.Parse(match.Groups[2].Value);
            int right=int.Parse(match.Groups[3].Value),bottom=int.Parse(match.Groups[4].Value);
            return right>left && bottom>top?(left,top,right-left,bottom-top):null;
        }
        catch(System.Xml.XmlException){return null;}
    }

    // Google may hide the chooser title after scrolling. Keep recognizing the
    // authenticated Google list and the exact Add another account row, rather
    // than aborting simply because the header scrolled out of the viewport.
    static bool IsVerifiedGoogleChooserBody(string xml)
    {
        try
        {
            var root=XDocument.Parse(xml).Root;
            if(root is null)return false;
            if(root.DescendantsAndSelf().Any(IsUseAnotherAccountNode))return true;
            var bounds=GoogleChooserListBounds(xml);
            if(!bounds.HasValue)return false;
            string text=string.Join(" ",root.DescendantsAndSelf().Select(NodeLabel));
            return text.Contains("Choose an account",StringComparison.OrdinalIgnoreCase) ||
                text.Contains("To continue, Google will share",StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Use an account",StringComparison.OrdinalIgnoreCase);
        }
        catch(System.Xml.XmlException){return false;}
    }

    // Fallback ONLY for an XML node labelled explicitly Add/Use another account.
    // No guessed list index, no hard-coded coordinates and no saved identity row.
    static (int X,int Y)? VerifiedAnotherAccountPoint(string xml)
    {
        try
        {
            var root=XDocument.Parse(xml).Root;
            if(root is null)return null;
            foreach(var node in root.DescendantsAndSelf().Where(IsUseAnotherAccountNode))
            {
                foreach(var element in node.AncestorsAndSelf().Take(4))
                {
                    var match=Regex.Match((string?)element.Attribute("bounds")??"",
                        @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]");
                    if(!match.Success)continue;
                    int left=int.Parse(match.Groups[1].Value), top=int.Parse(match.Groups[2].Value);
                    int right=int.Parse(match.Groups[3].Value),bottom=int.Parse(match.Groups[4].Value);
                    int width=right-left,height=bottom-top;
                    bool clickable=string.Equals((string?)element.Attribute("clickable"),"true",
                        StringComparison.OrdinalIgnoreCase);
                    // A normal row or its text is accepted only if semantic and
                    // bounded; never click a huge container including saved IDs.
                    if(width is >=40 and <=1000 && height is >=20 and <=115 &&
                       (clickable || element==node))
                        return ((left+right)/2,(top+bottom)/2);
                }
            }
        }
        catch(System.Xml.XmlException){}
        return null;
    }

    async Task<bool> FindAndTapUseAnotherAccountAsync(
        AppiumSession session,CancellationToken ct,int maxScrolls=4)
    {
        int actualScrolls=0;
        for(int viewport=0;viewport<=maxScrolls;viewport++)
        {
            ct.ThrowIfCancellationRequested();
            if(!await VerifyGoogleContext(session,ct))
            {
                log($"GOOGLE_ACCOUNT_PICKER_CONTEXT_LOST: scrolls={actualScrolls}; " +
                    "no saved identity selected.");
                return false;
            }
            string xml=await session.SourceAsync(ct);
            bool chooserVisible=IsVerifiedGoogleChooserBody(xml) ||
                await Exists(session,"googleChooser",ct);
            if(!chooserVisible)
            {
                // The header can disappear while the chooser is animating;
                // make one bounded reread rather than misreport all 4 scrolls.
                await Task.Delay(650,ct);
                xml=await session.SourceAsync(ct);
                chooserVisible=IsVerifiedGoogleChooserBody(xml) ||
                    await Exists(session,"googleChooser",ct);
                if(!chooserVisible)
                {
                    log($"GOOGLE_ACCOUNT_PICKER_UI_UNCONFIRMED: scrolls={actualScrolls}; " +
                        "no blind tap; save XML/screenshot on failure.");
                    return false;
                }
            }
            bool targetVisible=false;
            try{targetVisible=XDocument.Parse(xml).Root?.DescendantsAndSelf().Any(IsUseAnotherAccountNode) ?? false;}
            catch(System.Xml.XmlException){/* retry once after animation */}
            bool semanticTarget=targetVisible || await Exists(session,"useAnotherAccountLabel",ct);
            if(semanticTarget && await VerifyGoogleContext(session,ct))
            {
                bool clicked=await WaitTap(session,"useAnotherAccount",3,ct);
                string method=clicked?"SEMANTIC_LOCATOR":"NONE";
                if(!clicked && targetVisible)
                {
                    var point=VerifiedAnotherAccountPoint(xml);
                    if(point.HasValue)
                    {
                        await session.TapUiPointAsync(point.Value.X,point.Value.Y,ct);
                        clicked=true;
                        method="VERIFIED_XML_ROW";
                    }
                }
                if(clicked)
                {
                    log($"GOOGLE_ACCOUNT_PICKER_TARGET_CLICKED: viewport={viewport+1}; " +
                        $"method={method}; scrolls={actualScrolls}; " +
                        "target=ADD_OR_USE_ANOTHER_ACCOUNT; savedIdentitySelected=false.");
                    return true;
                }
                log($"GOOGLE_PICKER_VISIBLE_TARGET_NOT_CLICKED: viewport={viewport+1}; " +
                    "semanticLabel=true; no saved identity selected.");
            }
            if(viewport==maxScrolls)break;
            var bounds=GoogleChooserListBounds(xml);
            var window=await session.WindowRectAsync(ct);
            int left=bounds?.Left??window.X+window.Width*8/100;
            int top=bounds?.Top??window.Y+window.Height*18/100;
            int width=bounds?.Width??window.Width*84/100;
            int height=bounds?.Height??window.Height*68/100;
            log($"GOOGLE_ACCOUNT_PICKER_SCROLL: attempt={actualScrolls+1}/{maxScrolls}; " +
                $"targetVisible={targetVisible}; listBoundsVerified={bounds.HasValue}; direction=UP.");
            await session.SwipeGoogleAccountChooserUpAsync(left,top,width,height,ct);
            actualScrolls++;
            await Task.Delay(1100,ct);
        }
        log($"GOOGLE_ACCOUNT_PICKER_TARGET_NOT_FOUND: actualScrolls={actualScrolls}; " +
            "savedIdentitySelected=false.");
        return false;
    }

    // Appium Inspector can report a TextView for Google's text-field hit target.
    // Focus it, then prefer a real EditText before trying the locator itself.
    async Task<bool> FillGoogleField(AppiumSession session,string step,string value,CancellationToken ct)
    {
        string focusStep=step+"Focus";
        if(await Exists(session,focusStep,ct))
        {
            if(!await Tap(session,focusStep,ct))return false;
            await Task.Delay(400,ct);
        }
        // An edit field is only searched AFTER the expected Google page's hit target was found.
        if(await WaitFor(session,"googleFocusedInput",2,ct))
            return await session.SetTextAnyAsync(selectors.Get("googleFocusedInput"),value,ct);
        if(await Exists(session,"googleEditableInput",ct))
            return await session.SetTextAnyAsync(selectors.Get("googleEditableInput"),value,ct);
        return await session.SetTextAnyAsync(selectors.Get(step),value,ct);
    }

    // Keep input credentials within the method scope; never expose them through log().
    Task<bool> FillField(AppiumSession session,string step,string value,CancellationToken ct)
        => FillGoogleField(session,step,value,ct);

    /// <summary>
    /// Dismiss only TikTok's Android system contacts/accounts permission prompt.
    /// The button can have one of two resource IDs depending on the Android build.
    /// Never deny an unrelated permission based on a generic "Don't allow" label.
    /// This does NOT certify that the TikTok account has logged in.
    /// </summary>
    public async Task<bool> DismissTikTokContactsPermissionIfPresentAsync(
        AppiumSession session,CancellationToken ct)
    {
        if(!await Exists(session,"androidPermissionDeny",ct)) return false;
        string xml=await session.SourceAsync(ct);
        bool tikTokPrompt=xml.Contains("TikTok",StringComparison.OrdinalIgnoreCase);
        bool contactsPrompt=xml.Contains("contacts",StringComparison.OrdinalIgnoreCase) ||
            xml.Contains("danh bạ",StringComparison.OrdinalIgnoreCase);
        if(!tikTokPrompt || !contactsPrompt)
            return false; // Do not dismiss unrelated Android permission prompts.

        if(!await Tap(session,"androidPermissionDeny",ct))
        {
            log("Android contacts permission: có hộp thoại nhưng không nhấn được Don't allow.");
            return false;
        }
        log("Android contacts permission: đã nhấn Don't allow; chờ đóng hộp thoại.");
        var timer=Stopwatch.StartNew();
        while(timer.Elapsed<TimeSpan.FromSeconds(6))
        {
            ct.ThrowIfCancellationRequested();
            if(!await Exists(session,"androidPermissionDeny",ct))
                return true;
            await Task.Delay(PollMs,ct);
        }
        log("Android contacts permission: hộp thoại vẫn xuất hiện sau khi nhấn Don't allow.");
        return false;
    }

    public Task<LoginResult> LoginAsync(AppiumSession session,ManagedAccount account,CancellationToken ct)
        => LoginAttemptAsync(session,account,ct,verifyIdentityRetryCount:0);

    async Task<LoginResult?> RetryAfterGoogleVerifyIfPresentAsync(
        AppiumSession session,ManagedAccount account,int verifyIdentityRetryCount,CancellationToken ct)
    {
        string source=await session.SourceAsync(ct);
        if(!IsGoogleVerifyIdentitySource(source))return null;
        string package=await session.CurrentPackageAsync(ct);
        if(!IsGoogleAuthenticationPackage(package))return null;
        log($"GOOGLE_WAIT_VERIFY_SCREEN_MATCHED: foreground={package}; " +
            "currentUrlRequired=false; action=DISMISS_X.");
        if(verifyIdentityRetryCount>=1)
            return new(LoginOutcome.NeedsHuman,
                "GOOGLE_VERIFY_IDENTITY_REPEATED: challenge xuất hiện lại sau 1 lần đóng/thử lại; " +
                "không đánh dấu tài khoản INVALID và không lặp vô tận.");
        if(!await DismissGoogleVerifyIdentityAsync(session,source,ct))
            return new(LoginOutcome.NeedsHuman,
                "GOOGLE_VERIFY_IDENTITY_CLOSE_FAILED: không đóng được challenge; accountInvalid=false.");
        return await LoginAttemptAsync(session,account,ct,verifyIdentityRetryCount+1);
    }

    async Task<LoginResult> LoginAttemptAsync(AppiumSession session,ManagedAccount account,
        CancellationToken ct,int verifyIdentityRetryCount)
    {
        try
        {
            // A previous inspection may already have left Google's account chooser open.
            bool chooser=await Exists(session,"googleChooser",ct);
            bool emailPage=await Exists(session,"googleEmailFocus",ct);
            int googleLaunchClicks=0;
            if(!chooser && !emailPage)
            {
                // TikTok's logged-out UI has more than one stage:
                //   Profile -> "Welcome back <old handle>" -> Add another account (z78)
                //   -> login methods -> Continue with Google -> Google chooser/email.
                // Never tap the big red Log in button on the Welcome back sheet:
                // it would reuse the remembered TikTok identity, not the assigned one.
                // After each tap, wait for an observable stage change; do not spam taps.
                bool profileTapped=false;
                bool entryTapped=false;
                bool tiktokAnotherTapped=false;
                bool continueGoogleTapped=false;
                int loginExpandAttempts=0;
                var stageTimer=Stopwatch.StartNew();
                while(stageTimer.Elapsed < TimeSpan.FromSeconds(65))
                {
                    ct.ThrowIfCancellationRequested();
                    chooser=await Exists(session,"googleChooser",ct);
                    emailPage=await Exists(session,"googleEmailFocus",ct);
                    if(chooser || emailPage)
                    {
                        log("Google: đã hiển thị Choose an account / Email.");
                        break;
                    }

                    // Do not press TikTok selectors while a Google window or
                    // another app is in the foreground during navigation.
                    if(!await TikTokForegroundProbe.IsVisibleAsync(session,ct))
                    {
                        var verifyRetry=await RetryAfterGoogleVerifyIfPresentAsync(
                            session,account,verifyIdentityRetryCount,ct);
                        if(verifyRetry is not null)return verifyRetry;
                        var invalid=await DetectGoogleAccountInvalidAsync(session,ct);
                        if(invalid is not null)return invalid;
                        await Task.Delay(PollMs,ct);
                        continue;
                    }

                    // The Welcome back sheet has an Add another account action
                    // with resource-id z78, even though older layouts used z78
                    // for a generic login entry. Identify the *screen first*.
                    if(TikTokForegroundProbe.IsLoggedOutWelcomeBack(await session.SourceAsync(ct)))
                    {
                        if(!tiktokAnotherTapped)
                        {
                            if(!await WaitTap(session,"tiktokAddAnotherAccount",10,ct))
                                return new(LoginOutcome.TechnicalFailure,
                                    "Thấy TikTok Welcome back nhưng không nhấn được Add another account (z78).");
                            tiktokAnotherTapped=true;
                            log("TikTok: Welcome back -> đã chọn Add another account (z78); chờ màn hình phương thức đăng nhập.");
                        }
                        await Task.Delay(PollMs,ct);
                        continue;
                    }

                    // After pm clear, Profile can open the login-method sheet
                    // directly. Do not require Welcome back or an intermediate
                    // Log in button: the actual labeled Google choice is enough.
                    // The positional Compose selector is a last fallback, only
                    // after a positive method-screen observation or prior step.
                    string loginSource=await session.SourceAsync(ct);
                    bool googleOptionVisible=IsGoogleLoginOptionVisible(loginSource);
                    bool methodsExpected=entryTapped || tiktokAnotherTapped ||
                        IsTikTokLoginMethodsShell(loginSource) ||
                        TikTokForegroundProbe.IsTikTokLoginMethodsScreen(loginSource) ||
                        await Exists(session,"tiktokLoginMethods",ct);
                    if(methodsExpected && !continueGoogleTapped && googleOptionVisible &&
                       await Tap(session,"continueGoogle",ct))
                    {
                        continueGoogleTapped=true;
                        googleLaunchClicks++;
                        log("TikTok: đã nhấn Continue with Google; chờ Google Choose an account / Email.");
                        break;
                    }
                    if(methodsExpected && !continueGoogleTapped && !googleOptionVisible &&
                       loginExpandAttempts<2)
                    {
                        loginExpandAttempts++;
                        log($"GOOGLE_LOGIN_OPTION_HIDDEN: mở danh sách phương thức đăng nhập " +
                            $"({loginExpandAttempts}/2).");
                        if(await ExpandTikTokLoginOptionsAsync(session,loginSource,ct))
                        {
                            string expandedSource=await session.SourceAsync(ct);
                            if(IsGoogleLoginOptionVisible(expandedSource) &&
                               await Tap(session,"continueGoogle",ct))
                            {
                                continueGoogleTapped=true;
                                googleLaunchClicks++;
                                log("TikTok: đã mở rộng và nhấn Continue with Google; chờ Google Choose an account / Email.");
                                break;
                            }
                        }
                    }

                    if(!entryTapped && !tiktokAnotherTapped &&
                        await Exists(session,"loginEntry",ct))
                    {
                        if(!await Tap(session,"loginEntry",ct))
                            return new(LoginOutcome.TechnicalFailure,
                                "Thấy nút mở đăng nhập TikTok nhưng không click được.");
                        entryTapped=true;
                        log("TikTok: đã nhấn nút mở đăng nhập; đang xác định màn hình tiếp theo.");
                        await Task.Delay(PollMs,ct);
                        continue;
                    }

                    if(!profileTapped && !entryTapped && !tiktokAnotherTapped &&
                        await Tap(session,"profile",ct))
                    {
                        profileTapped=true;
                        log("TikTok: đã mở Profile; chờ giao diện đăng nhập.");
                    }
                    await Task.Delay(PollMs,ct);
                }

                if(!continueGoogleTapped && !chooser && !emailPage)
                {
                    string foreground=await session.CurrentPackageAsync(ct);
                    string lastStage=tiktokAnotherTapped ? "sau Add another account (z78)" :
                        entryTapped ? "sau nút mở đăng nhập" :
                        profileTapped ? "sau khi mở Profile" : "trước khi mở Profile";
                    return new(LoginOutcome.TechnicalFailure,
                        $"Không đến được Continue with Google trong 65 giây ({lastStage}; foreground={foreground}). " +
                        "Cần kiểm tra UI Welcome back, nút Add another account và màn hình phương thức đăng nhập.");
                }
            }

            // Slow devices can take over a minute to render Google's native
            // account picker. At 45 seconds perform one verified retry only;
            // the full transition has a hard 90-second ceiling.
            var wait=Stopwatch.StartNew();
            bool slowRetryHandled=false;
            bool oneTapHandled=false;
            while(wait.Elapsed<TimeSpan.FromSeconds(90))
            {
                ct.ThrowIfCancellationRequested();
                string waitingSource=await session.SourceAsync(ct);
                // Security verification takes priority over the generic
                // "Sign in with Google" header shared by several Google UIs.
                var verifyRetry=await RetryAfterGoogleVerifyIfPresentAsync(
                    session,account,verifyIdentityRetryCount,ct);
                if(verifyRetry is not null)return verifyRetry;
                if(IsGoogleOneTapSource(waitingSource))
                {
                    if(oneTapHandled)
                        return new(LoginOutcome.NeedsHuman,
                            "GOOGLE_ONE_TAP_REPEATED: modal tài khoản lưu sẵn xuất hiện lại sau một lần đóng/thử lại; không chọn nhầm account.");
                    oneTapHandled=true;
                    if(!await DismissGoogleOneTapAsync(session,waitingSource,ct))
                        return new(LoginOutcome.NeedsHuman,
                            "GOOGLE_ONE_TAP_DISMISS_FAILED: không đóng được modal; savedIdentitySelected=false.");
                    if(googleLaunchClicks<2 && await RetryContinueWithGoogleAsync(session,ct))
                        googleLaunchClicks++;
                    continue;
                }
                var invalid=await DetectGoogleAccountInvalidAsync(session,ct);
                if(invalid is not null)return invalid;
                chooser=await Exists(session,"googleChooser",ct);
                emailPage=await Exists(session,"googleEmailFocus",ct);
                if(chooser||emailPage)break;
                if(!slowRetryHandled && wait.Elapsed>=TimeSpan.FromSeconds(45))
                {
                    slowRetryHandled=true;
                    if(googleLaunchClicks<2 && await RetryContinueWithGoogleAsync(session,ct))
                        googleLaunchClicks++;
                    else
                        log($"GOOGLE_PICKER_RETRY_SKIPPED: clickCount={googleLaunchClicks}; " +
                            "verifiedTikTokLoginSheet=false; continueWaiting=true.");
                }
                await Task.Delay(PollMs,ct);
            }
            if(!chooser && !emailPage)
                return new(LoginOutcome.TechnicalFailure,
                    $"GOOGLE_PICKER_TIMEOUT_AFTER_2_ATTEMPTS: chưa thấy Choose an account/Email sau 90 giây; " +
                    $"continueGoogleClicks={googleLaunchClicks}; worker dừng có giới hạn, không lặp vô tận.");

            if(!await VerifyGoogleContext(session,ct))
                return new(LoginOutcome.NeedsHuman,"Chưa xác thực được ứng dụng/trang Google; không tự nhập thông tin đăng nhập.");

            if(chooser)
            {
                // NEVER use (RecyclerView/.../container)[2] or [3]. Those positions
                // are saved Google identities, depending on how many are cached.
                // Search the current viewport first, then scroll only the verified
                // Google list up to four times. A compact overlay gets one safe
                // Back/reopen only AFTER the bounded scroll search is exhausted.
                bool anotherAccountClicked=await FindAndTapUseAnotherAccountAsync(session,ct);
                if(!anotherAccountClicked)
                {
                    string chooserSource=await session.SourceAsync(ct);
                    bool compactSheet=chooserSource.Contains("Choose an account for TikTok",StringComparison.OrdinalIgnoreCase) &&
                        chooserSource.Contains("Sign-in options",StringComparison.OrdinalIgnoreCase);
                    if(compactSheet && await VerifyGoogleContext(session,ct))
                    {
                        log("GOOGLE_CHOOSER_OVERLAY_DETECTED: target absent after bounded scroll; " +
                            "close/reopen once without selecting a saved identity.");
                        await session.BackAsync(ct);
                        await Task.Delay(950,ct);
                        string returned=await session.SourceAsync(ct);
                        if(TikTokForegroundProbe.IsTikTokLoginMethodsScreen(returned) &&
                           await TikTokForegroundProbe.IsVisibleAsync(session,ct) &&
                           await Tap(session,"continueGoogle",ct))
                        {
                            log("GOOGLE_CHOOSER_REOPEN: đã mở Continue with Google lần hai; " +
                                "tiếp tục tìm có cuộn.");
                            await Task.Delay(1200,ct);
                            chooser=await Exists(session,"googleChooser",ct);
                            emailPage=await Exists(session,"googleEmailFocus",ct);
                            if(chooser && !emailPage)
                                anotherAccountClicked=await FindAndTapUseAnotherAccountAsync(session,ct);
                        }
                    }
                }
                if(!anotherAccountClicked)
                    return new(LoginOutcome.NeedsHuman,
                        "Google Choose an account: đã cuộn tối đa 4 lần nhưng không thấy/click được " +
                        "Add another account hoặc Use another account; không chọn tài khoản lưu sẵn.");
                // Clicking is NOT proof that the intended row was selected.
                // A saved identity would return to TikTok without an email form.
                if(!await WaitFor(session,"googleEmailFocus",35,ct))
                    return new(LoginOutcome.NeedsHuman,
                        "Sau khi chọn Add another account, chưa thấy biểu mẫu email Google sau 35 giây. Dừng để tránh sử dụng nhầm tài khoản đã lưu.");
                log("Google: đã mở biểu mẫu nhập email qua Add another account (không chọn tài khoản lưu sẵn).");
            }

            if(!await WaitFor(session,"googleEmailFocus",35,ct))
                return new(LoginOutcome.NeedsHuman,"Chưa thấy màn hình nhập email Google sau 35 giây.");
            if(!await VerifyGoogleContext(session,ct))
                return new(LoginOutcome.NeedsHuman,"Màn hình email không thuộc ngữ cảnh Google đã xác thực.");
            if(!await FillField(session,"googleEmail",account.Email,ct))
                return new(LoginOutcome.NeedsHuman,"Không nhập được Email; cần kiểm tra ô nhập liệu bằng Inspector.");
            log("Google: đã điền email của tài khoản được phân công.");
            if(!await WaitTap(session,"googleEmailNext",12,ct))
                return new(LoginOutcome.NeedsHuman,"Không tìm thấy Next sau bước email.");

            bool passwordVisible=false;
            var passwordWait=Stopwatch.StartNew();
            while(passwordWait.Elapsed<TimeSpan.FromSeconds(40))
            {
                ct.ThrowIfCancellationRequested();
                var verifyRetry=await RetryAfterGoogleVerifyIfPresentAsync(
                    session,account,verifyIdentityRetryCount,ct);
                if(verifyRetry is not null)return verifyRetry;
                var invalid=await DetectGoogleAccountInvalidAsync(session,ct);
                if(invalid is not null)return invalid;
                if(await Exists(session,"googlePasswordFocus",ct))
                { passwordVisible=true;break; }
                await Task.Delay(PollMs,ct);
            }
            if(!passwordVisible)
                return new(LoginOutcome.NeedsHuman,"Chưa đến màn hình mật khẩu Google. Có thể cần xác minh hoặc email bị từ chối.");
            if(!await VerifyGoogleContext(session,ct))
                return new(LoginOutcome.NeedsHuman,"Màn hình mật khẩu không thuộc ngữ cảnh Google đã xác thực.");
            if(!await FillField(session,"googlePassword",account.Password,ct))
                return new(LoginOutcome.NeedsHuman,"Không nhập được mật khẩu; cần kiểm tra ô nhập liệu hoặc xác minh thủ công.");
            log("Google: đã điền mật khẩu (không ghi giá trị vào nhật ký).");
            if(!await WaitTap(session,"googlePasswordNext",12,ct))
                return new(LoginOutcome.NeedsHuman,"Không tìm thấy Next sau bước mật khẩu.");

            // Google can show consent or a human-only challenge, or return to TikTok directly.
            var consentWatch=Stopwatch.StartNew();
            while(consentWatch.Elapsed<TimeSpan.FromSeconds(25))
            {
                ct.ThrowIfCancellationRequested();
                var verifyRetry=await RetryAfterGoogleVerifyIfPresentAsync(
                    session,account,verifyIdentityRetryCount,ct);
                if(verifyRetry is not null)return verifyRetry;
                var invalid=await DetectGoogleAccountInvalidAsync(session,ct);
                if(invalid is not null)return invalid;
                if(await Exists(session,"googleConsent",ct))
                {
                    if(!await VerifyGoogleContext(session,ct))
                        return new(LoginOutcome.NeedsHuman,"Có yêu cầu đồng ý nhưng chưa xác thực được nguồn Google.");
                    if(!await Tap(session,"googleConsent",ct))
                        return new(LoginOutcome.NeedsHuman,"Nút I agree chưa sẵn sàng.");
                    log("Google: đã xác nhận I agree trên màn hình Google.");
                    break;
                }
                if(await Exists(session,"androidPermissionDeny",ct))break;
                if(await session.CurrentPackageAsync(ct)=="com.ss.android.ugc.trill")break;
                await Task.Delay(PollMs,ct);
            }
            // TikTok can show the contacts/accounts permission prompt *after*
            // Google returns, sometimes later than the previous 5-second window.
            // Handle only the expected TikTok contacts permission. Reconciler also
            // checks before every Profile inspection, in case it appears later.
            var permissionWatch=Stopwatch.StartNew();
            // After this short immediate check, the shared post-Google
            // onboarding loop handles contacts permission at every UI poll.
            // Avoid a separate 12-second wait when there is no prompt yet.
            while(permissionWatch.Elapsed<TimeSpan.FromSeconds(3))
            {
                ct.ThrowIfCancellationRequested();
                var verifyRetry=await RetryAfterGoogleVerifyIfPresentAsync(
                    session,account,verifyIdentityRetryCount,ct);
                if(verifyRetry is not null)return verifyRetry;
                var invalid=await DetectGoogleAccountInvalidAsync(session,ct);
                if(invalid is not null)return invalid;
                if(await DismissTikTokContactsPermissionIfPresentAsync(session,ct))
                    break;
                await Task.Delay(PollMs,ct);
            }

            // The OAuth return can land on first-run gender/interests pages.
            // Existing pm-clear onboarding runs BEFORE Google login; run the
            // same bounded state machine here as well so Profile recovery
            // cannot misinterpret those screens as navigation failures.
            var googlePostWatch=Stopwatch.StartNew();
            bool postGoogleReady=await TikTokForegroundProbe.CompletePostGoogleOnboardingAsync(
                session,selectors,ct,log,seconds:105);
            log($"FAST_POST_GOOGLE_RESULT: elapsedMs={googlePostWatch.ElapsedMilliseconds}; " +
                $"uiReady={postGoogleReady}; identityStillRequiresVerification=true.");

            // Google sign-in completing does not prove TikTok identity. Never
            // use a substring search on PageSource to mark login as confirmed.
            // The SessionReconciler owns exact username verification.
            return new(LoginOutcome.NeedsHuman,
                "Đã đi qua Google Login. Đang chờ SessionReconciler xác minh đúng username TikTok tự động.");
        }
        catch(OperationCanceledException){throw;}
        catch(Exception ex)
        {
            // Never log raw Appium errors during credential entry; they can include input values.
            return new(LoginOutcome.TechnicalFailure,$"Lỗi kỹ thuật Google Login ({ex.GetType().Name}); kiểm tra Inspector và nhật ký đã che dữ liệu nhạy cảm.");
        }
    }

    // Read only UI metadata to identify whether a scroll changed the currently
    // exposed rows. Do not record raw page source or any account information.
    static string SettingsUiSignature(string xml)
    {
        try
        {
            var root=XDocument.Parse(xml);
            var nodes=root.Descendants().Select(node=>
                string.Join("|",new[] {
                    node.Name.LocalName,
                    (string?)node.Attribute("text") ?? "",
                    (string?)node.Attribute("content-desc") ?? "",
                    (string?)node.Attribute("bounds") ?? ""
                })).Where(s=>!string.IsNullOrEmpty(s));
            string body=string.Join("\n",nodes);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
        }
        catch {return "";}
    }

    static async Task<string> SafeSettingsSignatureAsync(AppiumSession session,CancellationToken ct)
    {
        try {return SettingsUiSignature(await session.SourceAsync(ct));}
        catch(OperationCanceledException){throw;}
        catch(Exception) {return "";}
    }

    /// <summary>
    /// The logout row is often below the viewport. The Boolean returned by
    /// scrollGesture cannot be used alone as evidence of reaching the bottom:
    /// if the gesture hit the wrong nested view it can immediately be false.
    /// Try a bounded alternate upward swipe and verify the visible UI changed.
    /// </summary>
    async Task<bool> TapSettingsLogoutWithScrollAsync(AppiumSession session,CancellationToken ct)
    {
        // Wait for Settings transition animation before looking for its last row.
        await Task.Delay(900,ct);
        if(await WaitTap(session,"logout",3,ct))
        {
            log("Logout: đã chọn mục Log out trong Settings.");
            return true;
        }
        const int maxScrolls=16;
        int stuckCount=0;
        string previous=await SafeSettingsSignatureAsync(session,ct);
        for(int attempt=1;attempt<=maxScrolls;attempt++)
        {
            ct.ThrowIfCancellationRequested();
            if(!string.Equals(await session.CurrentPackageAsync(ct),
                "com.ss.android.ugc.trill",StringComparison.Ordinal))
            {
                log("Logout: TikTok không còn foreground; ngừng thao tác để tránh cuộn nhầm ứng dụng khác.");
                return false;
            }

            bool? gestureCanContinue=null;
            try
            {
                gestureCanContinue=await session.ScrollDownAsync(ct);
                await Task.Delay(800,ct);
            }
            catch(OperationCanceledException){throw;}
            catch(Exception ex)
            {
                log($"Logout: scrollGesture lượt {attempt} gặp {ex.GetType().Name}; thử vuốt dự phòng.");
            }
            if(await WaitTap(session,"logout",2,ct))
            {
                log($"Logout: đã tìm và chọn Log out sau {attempt} lượt cuộn Settings.");
                return true;
            }

            string current=await SafeSettingsSignatureAsync(session,ct);
            bool changed=previous.Length>0 && current.Length>0 && current!=previous;
            if(!changed || gestureCanContinue==false)
            {
                // A false Appium scroll result is not proof of end-of-list.
                // Attempt a real upward swipe in an alternate body region.
                try
                {
                    await session.SwipeUpSettingsAsync(ct,alternateArea:attempt%2==0);
                    await Task.Delay(850,ct);
                }
                catch(OperationCanceledException){throw;}
                catch(Exception ex)
                {
                    log($"Logout: vuốt Settings dự phòng lượt {attempt} lỗi {ex.GetType().Name}.");
                }
                if(await WaitTap(session,"logout",2,ct))
                {
                    log($"Logout: đã tìm và chọn Log out sau vuốt dự phòng lượt {attempt}.");
                    return true;
                }
                string afterSwipe=await SafeSettingsSignatureAsync(session,ct);
                changed=changed || (current.Length>0 && afterSwipe.Length>0 && current!=afterSwipe);
                if(afterSwipe.Length>0)current=afterSwipe;
            }
            if(changed)stuckCount=0;
            else stuckCount++;
            if(current.Length>0)previous=current;
            log($"Logout: cuộn Settings lượt {attempt}/{maxScrolls}; Appium còn cuộn="+
                $"{(gestureCanContinue.HasValue?(gestureCanContinue.Value?"yes":"no/uncertain"):"unknown")}; "+
                $"UI thay đổi={(changed?"yes":"no/unknown")}; không đổi liên tiếp={stuckCount}.");
            if(stuckCount>=4)
            {
                log("Logout: đã thử nhiều vùng nhưng nội dung Settings không di chuyển; "+
                    "có thể thao tác chưa đúng vùng cuộn. Không kết luận đã đến cuối danh sách.");
                break;
            }
        }
        log("Logout: chưa tìm thấy Log out sau khi cuộn có giới hạn; cần kiểm tra vùng scrollable của Settings.");
        return false;
    }

    /// <summary>
    /// Read-only login gate fallback for a post-logout @null/profile placeholder.
    /// Merely opens the comments UI and focuses its input. Does NOT write or
    /// publish a comment. Only a visible Welcome back/Login gate is success.
    /// </summary>
    public async Task<bool> TryOpenLoginPromptWithoutPostingAsync(AppiumSession session,CancellationToken ct)
    {
        var source=await session.SourceAsync(ct);
        if(TikTokStateDetector.ContainsProtectionPrompt(source))return false;
        if(TikTokForegroundProbe.IsLoggedOutWelcomeBack(source) || await Exists(session,"loginEntry",ct))
            return true;
        if(await session.CurrentPackageAsync(ct)!=TikTokForegroundProbe.TikTokPackage)return false;

        // Leave the incomplete Profile and return to the video feed once.
        try { await session.BackAsync(ct); }
        catch(OperationCanceledException) { throw; }
        catch(Exception ex) { log("Logout: Back từ Profile @null chưa thành công ("+ex.GetType().Name+")."); }
        await Task.Delay(1000,ct);
        if(await Exists(session,"homeTab",ct))await Tap(session,"homeTab",ct);
        await Task.Delay(1100,ct);
        source=await session.SourceAsync(ct);
        if(TikTokStateDetector.ContainsProtectionPrompt(source))return false;
        if(TikTokForegroundProbe.IsLoggedOutWelcomeBack(source))return true;
        if(!await WaitTap(session,"commentEntry",8,ct))
        {
            log("Logout: không có nút mở bình luận trên Home; không thao tác đoán.");
            return false;
        }
        log("Logout: đã mở bảng bình luận để kiểm tra login gate (không đăng bình luận).");
        if(await WaitFor(session,"commentInput",5,ct))
            await Tap(session,"commentInput",ct); // focus only; no text or send
        var watch=Stopwatch.StartNew();
        while(watch.Elapsed<TimeSpan.FromSeconds(12))
        {
            ct.ThrowIfCancellationRequested();
            source=await session.SourceAsync(ct);
            if(TikTokForegroundProbe.IsLoggedOutWelcomeBack(source) || await Exists(session,"loginEntry",ct))
            {
                log("Logout: đã thấy màn hình đăng nhập sau khi mở bình luận; không gửi bình luận.");
                return true;
            }
            if(TikTokStateDetector.ContainsProtectionPrompt(source))return false;
            await Task.Delay(750,ct);
        }
        log("Logout: mở bình luận không xuất hiện login gate; giữ nguyên tài khoản và dừng an toàn.");
        return false;
    }

    /// <summary>
    /// Logout for a previously verified, approved account. A subsequent login
    /// requires independently observing a Welcome back or Log in screen.
    /// </summary>
    public async Task<bool> LogoutAsync(AppiumSession session,CancellationToken ct)
    {
        if(!await WaitTap(session,"profile",20,ct))
        {
            log("Logout: không mở được Profile.");
            return false;
        }
        if(!await WaitTap(session,"profileMenu",15,ct))
        {
            log("Logout: không tìm thấy Profile menu.");
            return false;
        }
        log("Logout: đã mở Profile menu.");
        if(!await WaitTap(session,"settingsPrivacy",18,ct))
        {
            log("Logout: không tìm thấy Settings and privacy.");
            return false;
        }
        log("Logout: đã mở Settings and privacy.");
        // In TikTok Settings the Log out row is at the very bottom. Search
        // and scroll to it instead of waiting 20 seconds on the first page.
        if(!await TapSettingsLogoutWithScrollAsync(session,ct))
            return false;
        // First Log out is a View in Settings. The second is a Button in
        // the confirmation modal. Never treat a missing confirmation as success.
        if(!await WaitTap(session,"logoutConfirm",18,ct))
        {
            log("Logout: chưa xác nhận được nút Log out ở hộp thoại.");
            return false;
        }
        log("Logout: đã xác nhận hộp thoại; chờ TikTok về màn hình chưa đăng nhập.");

        // Some devices show a stale Profile with @null immediately after logout.
        // Detect it early rather than waiting 90s and then claiming it is an
        // unknown third-party account. The reconciler will restart TikTok once.
        var ghostWatch=Stopwatch.StartNew();
        while(ghostWatch.Elapsed<TimeSpan.FromSeconds(9))
        {
            ct.ThrowIfCancellationRequested();
            var ui=await session.SourceAsync(ct);
            if(TikTokForegroundProbe.IsLoggedOutWelcomeBack(ui))
            {
                log("Logout: Welcome back / Add another account đã xuất hiện; đăng xuất xác minh thành công.");
                return true;
            }
            if(TikTokForegroundProbe.IsGhostProfileAfterLogout(ui) &&
               ghostWatch.Elapsed>=TimeSpan.FromSeconds(5))
            {
                log("Logout: Profile @null vẫn còn sau 5 giây; chuyển sang phục hồi có giới hạn.");
                return false;
            }
            await Task.Delay(950,ct);
        }

        // TikTok normally lands on Home, not on the login screen. The package
        // may be foreground while the cloud-phone UI is still blank/loading.
        // Wait for actual navigation/login controls before attempting Profile.
        log("Logout: chờ TikTok tải xong Home hoặc giao diện đăng nhập (tối đa 90 giây).");
        if(!await TikTokForegroundProbe.WaitForReadyUiAsync(session,ct,90))
        {
            log("Logout: sau 90 giây chưa thấy UI TikTok sẵn sàng; không click Profile hoặc chuyển tài khoản.");
            return false;
        }

        var timer=Stopwatch.StartNew();
        while(timer.Elapsed<TimeSpan.FromSeconds(35))
        {
            ct.ThrowIfCancellationRequested();
            // TikTok's remembered-account picker is a valid logged-out state,
            // even if it offers old account cards instead of a Log in button.
            // Require both a Welcome back title and Add another account label.
            // Do not tap any stored-account row when verifying logout.
            string currentUi=await session.SourceAsync(ct);
            if(TikTokForegroundProbe.IsLoggedOutWelcomeBack(currentUi))
            {
                log("Logout: đã xác minh trạng thái Welcome back / Add another account; phiên trước đã đăng xuất.");
                return true;
            }
            // If TikTok directly shows the login page, a visible Log in control
            // proves there is a logged-out entry point.
            if(await Exists(session,"loginEntry",ct))
            {
                log("Logout: đã xác nhận màn hình Log in.");
                return true;
            }
            // Otherwise enter Profile from Home and verify its login control.
            if(await Tap(session,"profile",ct))
            {
                if(TikTokForegroundProbe.IsLoggedOutWelcomeBack(await session.SourceAsync(ct)))
                {
                    log("Logout: Profile hiện Welcome back / Add another account; đã đăng xuất.");
                    return true;
                }
                if(await WaitFor(session,"loginEntry",8,ct))
                {
                    log("Logout: Profile ở trạng thái chưa đăng nhập.");
                    return true;
                }
            }
            await Task.Delay(PollMs,ct);
        }
        log("Logout: chưa thấy trang Profile chưa đăng nhập; không chuyển tài khoản.");
        return false;
    }
}
