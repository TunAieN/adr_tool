using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AndroidMultiDeviceV1.Core;

public sealed class AdbClient(Func<string> getAdbPath)
{
    public Action<string>? PerformanceLog { get; set; }
    public Task<string> RunAsync(CancellationToken ct, params string[] args)
        => RunWithTimeoutAsync(ct, TimeSpan.FromSeconds(25), args);

    public async Task<string> RunWithTimeoutAsync(CancellationToken ct, TimeSpan maxDuration, params string[] args)
    {
        string exe = getAdbPath();
        if (!File.Exists(exe)) throw new FileNotFoundException("Không tìm thấy adb.exe; kiểm tra Android SDK.", exe);
        var watch=Stopwatch.StartNew();
        bool success=false;
        using var p = new Process();
        p.StartInfo = new ProcessStartInfo(exe)
        {
            UseShellExecute=false, CreateNoWindow=true, RedirectStandardOutput=true,
            RedirectStandardError=true, WorkingDirectory=Path.GetDirectoryName(exe)!
        };
        foreach(var arg in args) p.StartInfo.ArgumentList.Add(arg);
        if(!p.Start()) throw new InvalidOperationException("Không khởi động được adb.exe");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(maxDuration);
        try
        {
            var outputTask=p.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask=p.StandardError.ReadToEndAsync(timeout.Token);
            await p.WaitForExitAsync(timeout.Token);
            string output=await outputTask, error=await errorTask;
            if(p.ExitCode!=0) throw new InvalidOperationException($"ADB ({p.ExitCode}): {error.Trim()} {output.Trim()}");
            success=true;
            return output.Trim();
        }
        catch(OperationCanceledException)
        {
            if(!p.HasExited) p.Kill(entireProcessTree:true);
            throw;
        }
        finally
        {
            if(watch.ElapsedMilliseconds>=2000 || !success)
            {
                // No command arguments, ADB output, serials or credentials in logs.
                string category=args.Contains("devices",StringComparer.Ordinal)?"DEVICES":
                    args.Contains("connect",StringComparer.Ordinal)?"CONNECT":
                    args.Contains("shell",StringComparer.Ordinal)?"SHELL":"OTHER";
                try{PerformanceLog?.Invoke($"PERF_ADB: action={category}; ms={watch.ElapsedMilliseconds}; ok={success}");}
                catch { /* monitoring cannot interrupt normal device operations */ }
            }
        }
    }
    public async Task<IReadOnlyList<string>> DevicesAsync(CancellationToken ct)
    {
        var output=await RunAsync(ct,"devices","-l");
        return output.Split('\n',StringSplitOptions.RemoveEmptyEntries)
            .Select(s=>s.Trim()).Where(s=>Regex.IsMatch(s,@"^\S+\s+device(?:\s|$)"))
            .Select(s=>s.Split(' ',StringSplitOptions.RemoveEmptyEntries)[0])
            .ToArray();
    }
    public async Task<string> ConnectAsync(string endpoint, CancellationToken ct)
    {
        if(!Regex.IsMatch(endpoint,@"^[A-Za-z0-9_.-]+:\d{1,5}$"))
            throw new ArgumentException("Nhập địa chỉ dạng host:port (ví dụ adb.xcloudphone.net:24187)");
        return await RunAsync(ct,"connect",endpoint);
    }
    public async Task<string> DisconnectAsync(string endpoint,CancellationToken ct)
    {
        if(!Regex.IsMatch(endpoint,@"^[A-Za-z0-9_.-]+:\d{1,5}$"))
            throw new ArgumentException("ADB disconnect chỉ áp dụng cho endpoint host:port.");
        return await RunAsync(ct,"disconnect",endpoint);
    }
    public Task<string> LaunchTikTokAsync(string serial,CancellationToken ct)
        => RunAsync(ct,"-s",serial,"shell","monkey","-p","com.ss.android.ugc.trill",
            "-c","android.intent.category.LAUNCHER","1");

    public async Task<string> ModelAsync(string serial,CancellationToken ct)
    {
        string model=await RunAsync(ct,"-s",serial,"shell","getprop","ro.product.model");
        return string.IsNullOrWhiteSpace(model)?"Android":model.Trim();
    }

    public async Task RestartTikTokAsync(string serial,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await ForceStopTikTokAsync(serial,ct);
        await Task.Delay(900,ct);
        await LaunchTikTokAsync(serial,ct);
        await Task.Delay(2200,ct);
    }

    public Task<string> ForceStopTikTokAsync(string serial,CancellationToken ct) =>
        RunAsync(ct,"-s",serial,"shell","am","force-stop","com.ss.android.ugc.trill");

    /// <summary>
    /// Stops only the UiAutomator2 instrumentation packages on one device.
    /// The next Appium session creation starts a fresh server/test process.
    /// The shared desktop Appium server and other device workers are untouched.
    /// </summary>
    public async Task RestartUiAutomator2DriverAsync(string serial,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await RunAsync(ct,"-s",serial,"shell","am","force-stop",
            "io.appium.uiautomator2.server.test");
        await RunAsync(ct,"-s",serial,"shell","am","force-stop",
            "io.appium.uiautomator2.server");
        await Task.Delay(750,ct);
    }

    /// <summary>
    /// Destructive, device-scoped replacement for logout when deliberately
    /// switching an APPROVED TikTok account. Removes TikTok local storage,
    /// sessions, drafts and preferences. Does not erase other apps or the
    /// Windows account vault. Caller must verify account
    /// ownership and stop concurrent activity BEFORE invoking this method.
    /// </summary>
    public async Task ClearTikTokAppDataAsync(string serial,CancellationToken ct)
    {
        const string package="com.ss.android.ugc.trill";
        if(string.IsNullOrWhiteSpace(serial))
            throw new ArgumentException("Không có ADB serial của điện thoại cần xóa dữ liệu.",nameof(serial));
        string installed=await RunAsync(ct,"-s",serial,"shell","pm","path",package);
        if(!installed.Contains("package:",StringComparison.Ordinal))
            throw new InvalidOperationException("Không tìm thấy ứng dụng TikTok trên điện thoại này; KHÔNG xóa dữ liệu.");
        await RunAsync(ct,"-s",serial,"shell","am","force-stop",package);
        string result=await RunWithTimeoutAsync(ct,TimeSpan.FromSeconds(75),
            "-s",serial,"shell","pm","clear",package);
        if(!result.Trim().Equals("Success",StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Xóa dữ liệu TikTok chưa được ADB xác nhận: "+
                (result.Length>180 ? result[..180] : result));
    }

    /// <summary>
    /// Destructive, device-scoped Google sign-in reset used only together with
    /// an approved TikTok CLEAR_DATA/account switch. Clears Google Play Services
    /// local data and tokens on the target serial; it is never used by cache-only
    /// or navigation recovery paths.
    /// </summary>
    public async Task ClearGooglePlayServicesDataAsync(string serial,CancellationToken ct)
    {
        const string package="com.google.android.gms";
        if(string.IsNullOrWhiteSpace(serial))
            throw new ArgumentException("Không có ADB serial của điện thoại cần xóa Google Play Services.",nameof(serial));
        string installed=await RunAsync(ct,"-s",serial,"shell","pm","path",package);
        if(!installed.Contains("package:",StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Không tìm thấy Google Play Services trên điện thoại này; dừng CLEAR_DATA để tránh trạng thái đăng nhập nửa vời.");
        await RunAsync(ct,"-s",serial,"shell","am","force-stop",package);
        string result=await RunWithTimeoutAsync(ct,TimeSpan.FromSeconds(75),
            "-s",serial,"shell","pm","clear",package);
        if(!result.Trim().Equals("Success",StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Xóa dữ liệu Google Play Services chưa được ADB xác nhận: "+
                (result.Length>180 ? result[..180] : result));
    }

    /// <summary>
    /// ONLY TikTok cache, never the app's preferences/databases/accounts.
    /// Old Android pm parsers may silently ignore unsupported options, so
    /// check BOTH API level and the device's actual "pm help" syntax FIRST.
    /// Call only after confirmed logout click + an unresolved stale profile.
    /// </summary>
    public async Task<bool> ClearTikTokCacheOnlyAsync(string serial,CancellationToken ct)
    {
        string sdk=await RunAsync(ct,"-s",serial,"shell","getprop","ro.build.version.sdk");
        if(!int.TryParse(sdk.Trim(),out int api) || api<33)
            throw new NotSupportedException("Không thực hiện clear cache: SDK Android dưới 33 hoặc không xác định.");
        string help=await RunAsync(ct,"-s",serial,"shell","pm","help");
        // Require a clear-command declaration containing --cache-only on ONE line.
        // Never assume support from Android version alone or silently remove flag.
        bool supported=Regex.IsMatch(help,
            @"(?im)^\s*clear\s+[^\r\n]*--cache-only[^\r\n]*$");
        if(!supported)
            throw new NotSupportedException("Máy không quảng bá 'pm clear --cache-only'; bỏ qua để tránh xóa dữ liệu TikTok.");
        string result=await RunAsync(ct,"-s",serial,"shell","pm","clear","--cache-only","com.ss.android.ugc.trill");
        if(!result.Equals("Success",StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Xóa cache riêng TikTok không được xác nhận: "+
                (result.Length>180?result[..180]:result));
        return true;
    }
}

/// <summary>Minimal W3C/Appium WebDriver client; deliberately no NuGet dependency.</summary>
public sealed class AppiumSession : IAsyncDisposable
{
    const int InstrumentationFailureLimit=3;
    readonly HttpClient _http;
    string? _sessionId;
    int _consecutiveUiServerFailures;
    // Opt-in diagnostics. No logging unless enabled by the caller.
    public AppiumPerformanceMonitor? Performance { get; set; }
    public string DeviceKey { get; private set; }="unknown-device";

    public AppiumSession(string appiumUrl)
    {
        _http=new HttpClient { BaseAddress=new Uri(appiumUrl.TrimEnd('/')+"/"), Timeout=TimeSpan.FromSeconds(150) };
    }
    static async Task<JsonDocument> ResponseAsync(HttpResponseMessage response,CancellationToken ct)
    {
        string body=await response.Content.ReadAsStringAsync(ct);
        if(!response.IsSuccessStatusCode)
        {
            string message="Lỗi Appium "+(int)response.StatusCode;
            try
            {
                using var parsed=JsonDocument.Parse(body);
                if(parsed.RootElement.TryGetProperty("value",out var val))
                {
                    if(val.TryGetProperty("error",out var code))message+=" ["+code.GetString()+"]";
                    if(val.TryGetProperty("message",out var desc))message+=": "+desc.GetString();
                }
            }
            catch(JsonException){ /* Do not log full HTML or UI source */ }
            throw new InvalidOperationException(message.Length>480?message[..480]:message);
        }
        return JsonDocument.Parse(body);
    }
    async Task<JsonDocument> SendAsync(HttpMethod method,string path,object? payload,CancellationToken ct)
    {
        // Stopwatch wraps network + response parsing, including cancellations.
        // It only reports operation CATEGORY, never body/path/element ID.
        var watch=Stopwatch.StartNew();
        bool success=false;
        try
        {
            using var request=new HttpRequestMessage(method,path);
            if(payload is not null)request.Content=JsonContent.Create(payload);
            using var response=await _http.SendAsync(request,ct);
            var result=await ResponseAsync(response,ct);
            success=true;
            return result;
        }
        finally
        {
            Performance?.Record(AppiumPerformanceMonitor.Category(method.Method,path),
                watch.ElapsedMilliseconds,success);
        }
    }
    // A slow/blocked UiAutomator2 HTTP command must not freeze a device worker for
    // the generic 150s HttpClient timeout. Limit *UI observation* calls only;
    // creating and shutting down sessions still use their normal limits.
    async Task<JsonDocument> SendUiQueryAsync(HttpMethod method,string path,
        object? payload,CancellationToken ct,int seconds=30)
    {
        using var timed=CancellationTokenSource.CreateLinkedTokenSource(ct);
        timed.CancelAfter(TimeSpan.FromSeconds(seconds));
        try
        {
            var response=await SendAsync(method,path,payload,timed.Token);
            Interlocked.Exchange(ref _consecutiveUiServerFailures,0);
            return response;
        }
        catch(OperationCanceledException) when(!ct.IsCancellationRequested)
        {
            string operation=path.EndsWith("/source",StringComparison.Ordinal)?"đọc cây UI" :
                path.EndsWith("/element",StringComparison.Ordinal)?"tìm phần tử" :
                path.EndsWith("/elements",StringComparison.Ordinal)?"tìm các phần tử" :
                path.EndsWith("/current_package",StringComparison.Ordinal)?"đọc ứng dụng foreground" :
                "thao tác UI";
            throw new TimeoutException($"Appium quá {seconds} giây khi {operation}. Kiểm tra ADB, UiAutomator2 và màn hình điện thoại.");
        }
        catch(Exception ex) when(AppiumFailureClassifier.IsRawInstrumentationCrash(ex))
        {
            int strike=Interlocked.Increment(ref _consecutiveUiServerFailures);
            if(strike>=InstrumentationFailureLimit)
                throw new AppiumInstrumentationCrashedException(strike);
            // Do not retain the raw Appium 500 marker in this transient wrapper:
            // fatal classifiers must escalate only on the third consecutive
            // failure, not on strike one.
            throw new AppiumInstrumentationUnavailableException(
                strike,InstrumentationFailureLimit);
        }
        catch(Exception ex) when(
            AppiumFailureClassifier.IsRawAccessibilityTreeFailure(ex) ||
            AppiumFailureClassifier.IsRawAppiumServer500(ex))
        {
            int strike=Interlocked.Increment(ref _consecutiveUiServerFailures);
            string category=AppiumFailureClassifier.IsRawAccessibilityTreeFailure(ex)
                ? "ACCESSIBILITY_ROOT_TIMEOUT" : "APPIUM_HTTP_500";
            if(strike>=InstrumentationFailureLimit)
                throw new AppiumUiTreeStalledException(strike,category);
            // Remove the raw HTTP-500/Accessibility marker from transient
            // exceptions. Fatal classifiers must trip only at the threshold.
            throw new AppiumUiTreeUnavailableException(
                strike,InstrumentationFailureLimit,category);
        }
    }

    public async Task CreateAsync(DeviceRecord device,CancellationToken ct,int? systemPortOverride=null)
    {
        DeviceKey=string.Concat(device.Serial.Select(c=>char.IsLetterOrDigit(c)?c:'_'));
        var caps=new Dictionary<string,object>
        {
            ["platformName"]="Android",["appium:automationName"]="UiAutomator2",
            ["appium:deviceName"]=device.Name,["appium:udid"]=device.Serial,
            ["appium:systemPort"]=systemPortOverride??device.SystemPort,["appium:noReset"]=true,
            ["appium:newCommandTimeout"]=Math.Max(900,Math.Clamp(device.SessionMinutes,1,1440)*60+180)
        };
        using var doc=await SendAsync(HttpMethod.Post,"session",new {capabilities=new {alwaysMatch=caps}},ct);
        var value=doc.RootElement.GetProperty("value");
        if(value.TryGetProperty("sessionId",out var s))_sessionId=s.GetString();
        if(string.IsNullOrEmpty(_sessionId) && doc.RootElement.TryGetProperty("sessionId",out var legacy))_sessionId=legacy.GetString();
        if(string.IsNullOrEmpty(_sessionId))throw new InvalidOperationException("Appium không trả sessionId");
    }
    string SessionPath(string suffix) => $"session/{Uri.EscapeDataString(_sessionId??throw new InvalidOperationException("Chưa có Appium session"))}" + (suffix.Length==0 ? "" : "/"+suffix);

    /// <summary>
    /// Read only the UiAutomator2 idle setting in the CURRENT session.
    /// This is deliberately not a global Appium or device configuration.
    /// </summary>
    public async Task<long> GetIdleTimeoutAsync(CancellationToken ct)
    {
        using var doc=await SendUiQueryAsync(HttpMethod.Get,SessionPath("appium/settings"),null,ct,30);
        var value=doc.RootElement.GetProperty("value");
        if(!value.TryGetProperty("waitForIdleTimeout",out var setting))
            throw new InvalidOperationException("Appium không trả waitForIdleTimeout; không thể thử an toàn.");
        if(setting.ValueKind==JsonValueKind.Number && setting.TryGetInt64(out long number))
            return number;
        if(setting.ValueKind==JsonValueKind.String &&
           long.TryParse(setting.GetString(),System.Globalization.NumberStyles.Integer,
               System.Globalization.CultureInfo.InvariantCulture,out number))
            return number;
        throw new InvalidOperationException("Giá trị waitForIdleTimeout không hợp lệ.");
    }

    /// <summary>
    /// Update UiAutomator2 idle wait through official Appium Settings API.
    /// Used only by the opt-in read-only diagnostic; never affects normal
    /// search, login, profile updates or comment workflows.
    /// </summary>
    public async Task SetIdleTimeoutAsync(long timeoutMs,CancellationToken ct)
    {
        if(timeoutMs<0 || timeoutMs>60000)
            throw new ArgumentOutOfRangeException(nameof(timeoutMs));
        var payload=new {settings=new Dictionary<string,long> {{"waitForIdleTimeout",timeoutMs}}};
        using var _=await SendUiQueryAsync(HttpMethod.Post,SessionPath("appium/settings"),payload,ct,30);
    }
    public async Task<string?> FindAsync(UiLocator locator,CancellationToken ct,int timeoutSeconds=25)
    {
        if(timeoutSeconds is < 1 or > 60)throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        // WebDriver JSON requires literal `using`, so a dictionary rather than anonymous property.
        var args=new Dictionary<string,string>{{"using", locator.By},{"value",locator.Value}};
        try
        {
            using var doc=await SendUiQueryAsync(HttpMethod.Post,SessionPath("element"),args,ct,timeoutSeconds);
            var value=doc.RootElement.GetProperty("value");
            return value.TryGetProperty("element-6066-11e4-a52e-4f735466cecf",out var id)?id.GetString():
                   value.TryGetProperty("ELEMENT",out var old)?old.GetString():null;
        }
        catch(InvalidOperationException e) when(e.Message.Contains("no such element",StringComparison.OrdinalIgnoreCase)) { return null; }
    }
    // Query all matching nodes, not only the first: several @handles may appear in UI.
    // Never guess the current identity from a generic page-source substring.
    public async Task<IReadOnlyList<string>> FindAllAnyAsync(IEnumerable<UiLocator> alternatives,CancellationToken ct)
    {
        var ids=new HashSet<string>(StringComparer.Ordinal);
        foreach(var loc in alternatives)
        {
            var args=new Dictionary<string,string>{{"using",loc.By},{"value",loc.Value}};
            try
            {
                using var doc=await SendUiQueryAsync(HttpMethod.Post,SessionPath("elements"),args,ct,25);
                foreach(var node in doc.RootElement.GetProperty("value").EnumerateArray())
                {
                    string? id=node.TryGetProperty("element-6066-11e4-a52e-4f735466cecf",out var e)
                        ? e.GetString() : node.TryGetProperty("ELEMENT",out var old) ? old.GetString() : null;
                    if(!string.IsNullOrWhiteSpace(id))ids.Add(id);
                }
            }
            catch(InvalidOperationException e) when(e.Message.Contains("no such element",StringComparison.OrdinalIgnoreCase)){}
        }
        return ids.ToArray();
    }
    public async Task<string> ElementTextAsync(string element,CancellationToken ct)
    {
        try
        {
            using var doc=await SendUiQueryAsync(HttpMethod.Get,
                SessionPath($"element/{Uri.EscapeDataString(element)}/text"),null,ct,25);
            string value=doc.RootElement.GetProperty("value").GetString()??"";
            if(!string.IsNullOrWhiteSpace(value))return value.Trim();
            using var attribute=await SendUiQueryAsync(HttpMethod.Get,
                SessionPath($"element/{Uri.EscapeDataString(element)}/attribute/content-desc"),null,ct,25);
            return attribute.RootElement.GetProperty("value").GetString()?.Trim()??"";
        }
        catch(InvalidOperationException e) when(e.Message.Contains("stale element",StringComparison.OrdinalIgnoreCase))
        { return ""; }
    }
    public async Task<bool> ExistsAnyAsync(IEnumerable<UiLocator> alternatives,CancellationToken ct)
    {
        foreach(var loc in alternatives)if(await FindAsync(loc,ct) is not null)return true;
        return false;
    }
    public async Task<bool> ClickAnyAsync(IEnumerable<UiLocator> alternatives,CancellationToken ct)
    {
        foreach(var loc in alternatives)
        {
            string? element=await FindAsync(loc,ct);
            if(element is null)continue;
            try
            {
                using var _=await SendAsync(HttpMethod.Post,SessionPath($"element/{Uri.EscapeDataString(element)}/click"),new {},ct);
                return true;
            }
            catch(InvalidOperationException){ /* stale/hidden alternative; try next */ }
        }
        return false;
    }
    /// <summary>
    /// Use for preflight navigation on slowly starting devices. A matched
    /// element can exist in XML but be hidden / not yet clickable.
    /// </summary>
    public async Task<bool> ClickVisibleAnyAsync(IEnumerable<UiLocator> alternatives,CancellationToken ct)
    {
        foreach(var loc in alternatives)
        {
            string? element=await FindAsync(loc,ct);
            if(element is null)continue;
            try
            {
                using var visible=await SendUiQueryAsync(HttpMethod.Get,
                    SessionPath($"element/{Uri.EscapeDataString(element)}/displayed"),null,ct,25);
                if(visible.RootElement.GetProperty("value").ValueKind!=JsonValueKind.True)continue;
                using var _=await SendUiQueryAsync(HttpMethod.Post,
                    SessionPath($"element/{Uri.EscapeDataString(element)}/click"),new {},ct,25);
                return true;
            }
            catch(InvalidOperationException ex) when(
                ex.Message.Contains("stale element",StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("element not interactable",StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("element click intercepted",StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("no such element",StringComparison.OrdinalIgnoreCase))
            { /* Try the next locator or wait for UI to finish rendering. */ }
        }
        return false;
    }

    public async Task<bool> SetTextAnyAsync(IEnumerable<UiLocator> alternatives,string text,CancellationToken ct)
    {
        foreach(var loc in alternatives)
        {
            string? element=await FindAsync(loc,ct);
            if(element is null)continue;
            try
            {
                using var _=await SendAsync(HttpMethod.Post,SessionPath($"element/{Uri.EscapeDataString(element)}/value"),new {text},ct);
                return true;
            }
            catch(InvalidOperationException ex) when (
                ex.Message.Contains("invalid element state",StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("element not interactable",StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("stale element",StringComparison.OrdinalIgnoreCase))
            {
                // A Google TextView can be a focus target rather than an editable field.
                // Try the next locator, but never fall back to blind keyboard input.
            }
        }
        return false;
    }
    public async Task<bool> ClearAnyAsync(IEnumerable<UiLocator> alternatives,CancellationToken ct)
    {
        foreach(var loc in alternatives)
        {
            var element=await FindAsync(loc,ct);
            if(element is null)continue;
            try
            {
                using var _=await SendAsync(HttpMethod.Post,
                    SessionPath($"element/{Uri.EscapeDataString(element)}/clear"),new {},ct);
                return true;
            }
            catch(InvalidOperationException e) when(e.Message.Contains("element",StringComparison.OrdinalIgnoreCase) ||
                e.Message.Contains("stale",StringComparison.OrdinalIgnoreCase)){}
        }
        return false;
    }
    /// <summary>
    /// Scroll TikTok Settings in the actual Android scrollable view when it is
    /// exposed, falling back to a bounded area gesture for Compose screens.
    /// A false result is ONLY a gesture hint; it is not proof the UI is at end.
    /// </summary>
    public async Task<bool> ScrollDownAsync(CancellationToken ct)
    {
        string? scrollingElement=await FindAsync(
            new UiLocator("xpath","//*[@scrollable='true']"),ct);
        object gesture;
        if(!string.IsNullOrEmpty(scrollingElement))
        {
            gesture=new {elementId=scrollingElement,direction="down",percent=0.90};
        }
        else
        {
            var (left,top,width,height)=await SettingsScrollRectAsync(ct);
            gesture=new {left,top,width,height,direction="down",percent=0.90};
        }
        using var result=await SendUiQueryAsync(HttpMethod.Post,SessionPath("execute/sync"),
            new {script="mobile: scrollGesture",args=new object[]{gesture}},ct,30);
        return result.RootElement.GetProperty("value").ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new InvalidOperationException("scrollGesture phải trả về true/false.")
        };
    }

    async Task<(int left,int top,int width,int height)> SettingsScrollRectAsync(CancellationToken ct)
    {
        using var rectDoc=await SendUiQueryAsync(HttpMethod.Get,SessionPath("window/rect"),null,ct,20);
        var rect=rectDoc.RootElement.GetProperty("value");
        int x=rect.GetProperty("x").GetInt32();
        int y=rect.GetProperty("y").GetInt32();
        int width=rect.GetProperty("width").GetInt32();
        int height=rect.GetProperty("height").GetInt32();
        if(width<160 || height<240)
            throw new InvalidOperationException("Kích thước màn hình không hợp lệ để cuộn Settings.");
        // Target the scrollable *body* rather than the system bars/top toolbar.
        return (x+width*12/100,y+height*19/100,width*76/100,height*69/100);
    }

    /// <summary>
    /// Secondary gesture for Compose and nested views where scrollGesture can
    /// report false even while Settings still has offscreen rows.
    /// </summary>
    public async Task SwipeUpSettingsAsync(CancellationToken ct,bool alternateArea=false)
    {
        var (left,top,width,height)=await SettingsScrollRectAsync(ct);
        if(alternateArea)
        {
            // Try a different safe interior stripe in case a nested view absorbs
            // center gestures. Never swipe status bar or Android navigation area.
            left+=width*43/100;
            width=width*48/100;
        }
        var gesture=new {left,top,width,height,direction="up",percent=0.78};
        using var _=await SendUiQueryAsync(HttpMethod.Post,SessionPath("execute/sync"),
            new {script="mobile: swipeGesture",args=new object[]{gesture}},ct,30);
    }

    /// <summary>
    /// Scrolls only inside Google's verified account-picker list. The supplied
    /// rectangle comes from fresh XML; invalid/missing bounds fall back to the
    /// safe central list body, away from system bars and chooser actions.
    /// </summary>
    public async Task SwipeGoogleAccountChooserUpAsync(
        int left,int top,int width,int height,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (x,y,w,h)=await WindowRectAsync(ct);
        if(w<180 || h<300)
            throw new InvalidOperationException("Màn hình quá nhỏ để cuộn Google account picker.");

        int safeLeft=x+w*8/100,safeTop=y+h*18/100;
        int safeRight=x+w*92/100,safeBottom=y+h*86/100;
        int requestedRight=left+width,requestedBottom=top+height;
        left=Math.Max(safeLeft,left);
        top=Math.Max(safeTop,top);
        requestedRight=Math.Min(safeRight,requestedRight);
        requestedBottom=Math.Min(safeBottom,requestedBottom);
        width=requestedRight-left;height=requestedBottom-top;
        if(width<80 || height<180)
        {
            left=safeLeft;top=safeTop;width=safeRight-safeLeft;height=safeBottom-safeTop;
        }
        var gesture=new {left,top,width,height,direction="up",percent=0.72};
        using var _=await SendUiQueryAsync(HttpMethod.Post,SessionPath("execute/sync"),
            new {script="mobile: swipeGesture",args=new object[]{gesture}},ct,30);
    }

    /// <summary>
    /// Bounded first-run feed tutorial gesture. Only call after positively
    /// identifying TikTok's Home/For You feed, never during Google login,
    /// profile verification, security prompts, or LIVE rooms.
    /// The initial TikTok coachmark says "Swipe up for more"; a single upward
    /// gesture is safer than blindly clicking the overlay or Android nav bar.
    /// </summary>
    public async Task SwipeTikTokFirstRunFeedTutorialAsync(CancellationToken ct)
    {
        var (x,y,w,h)=await WindowRectAsync(ct);
        if(w<180 || h<300)
            throw new InvalidOperationException("Màn hình quá nhỏ để thử vuốt hướng dẫn TikTok.");
        int centerX=x+w/2;
        int startY=y+h*72/100;
        int endY=y+h*30/100;
        var gesture=new {startX=centerX,startY,endX=centerX,endY,speed=350};
        using var _=await SendUiQueryAsync(HttpMethod.Post,SessionPath("execute/sync"),
            new {script="mobile: dragGesture",args=new object[]{gesture}},ct,30);
    }

    // Appium screenshot and WebDriver element geometry share Android screenshot
    // coordinates on ordinary native contexts. Scale cautiously when they differ.
    public async Task<byte[]> ScreenshotPngAsync(CancellationToken ct)
    {
        using var doc=await SendUiQueryAsync(HttpMethod.Get,SessionPath("screenshot"),null,ct,35);
        return Convert.FromBase64String(doc.RootElement.GetProperty("value").GetString() ?? "");
    }

    public async Task<(int X,int Y,int Width,int Height)> ElementRectAsync(string element,CancellationToken ct)
    {
        using var doc=await SendUiQueryAsync(HttpMethod.Get,
            SessionPath($"element/{Uri.EscapeDataString(element)}/rect"),null,ct,25);
        var rect=doc.RootElement.GetProperty("value");
        return (rect.GetProperty("x").GetInt32(),rect.GetProperty("y").GetInt32(),
            rect.GetProperty("width").GetInt32(),rect.GetProperty("height").GetInt32());
    }
    public async Task<(int X,int Y,int Width,int Height)> WindowRectAsync(CancellationToken ct)
    {
        using var doc=await SendUiQueryAsync(HttpMethod.Get,SessionPath("window/rect"),null,ct,25);
        var rect=doc.RootElement.GetProperty("value");
        return (rect.GetProperty("x").GetInt32(),rect.GetProperty("y").GetInt32(),
            rect.GetProperty("width").GetInt32(),rect.GetProperty("height").GetInt32());
    }
    public async Task ClickElementAsync(string element,CancellationToken ct)
    {
        using var _=await SendUiQueryAsync(HttpMethod.Post,
            SessionPath($"element/{Uri.EscapeDataString(element)}/click"),new {},ct,25);
    }
    public async Task SwipeGalleryUpAsync(CancellationToken ct)
    {
        var (x,y,w,h)=await WindowRectAsync(ct);
        // Stay within the image grid, away from status bar, toolbar and navigation bar.
        var gesture=new {left=x+w/12,top=y+h/4,width=w*5/6,height=h*11/20,
            direction="up",percent=0.72};
        using var _=await SendUiQueryAsync(HttpMethod.Post,SessionPath("execute/sync"),
            new {script="mobile: swipeGesture",args=new object[]{gesture}},ct,30);
    }

    /// <summary>
    /// Reveal offscreen categories (e.g. LIVE) in TikTok Search's horizontal
    /// q1m tab strip. The caller MUST verify the q1m HorizontalScrollView in
    /// fresh UI XML before invoking this method. No content/video swipes.
    /// </summary>
    public async Task SwipeLiveSearchCategoryTabsLeftAsync(
        int left,int top,int width,int height,CancellationToken ct)
    {
        var (x,y,w,h)=await WindowRectAsync(ct);
        if(w<180 || h<300 || width<170 || height<22)
            throw new InvalidOperationException("Thanh danh mục LIVE Search quá nhỏ để vuốt ngang.");
        int innerLeft=Math.Max(x+8,left+8);
        int innerRight=Math.Min(x+w-8,left+width-8);
        int touchY=top+height/2;
        // Header only. Reject stale bounds; never drag across the video grid,
        // Google login or the Android system bars.
        if(innerRight-innerLeft<125 || touchY<y+h*6/100 || touchY>y+h*32/100)
            throw new InvalidOperationException("Tọa độ thanh danh mục q1m không hợp lệ.");
        int startX=innerLeft+(innerRight-innerLeft)*87/100;
        int endX=innerLeft+(innerRight-innerLeft)*13/100;
        var gesture=new {startX,startY=touchY,endX,endY=touchY,speed=280};
        using var _=await SendUiQueryAsync(HttpMethod.Post,SessionPath("execute/sync"),
            new {script="mobile: dragGesture",args=new object[]{gesture}},ct,30);
    }

    /// <summary>
    /// Controlled medium drag inside TikTok Search LIVE result cards, not a fling.
    /// Configurable 1..75% of device height. Large drags can skip rows;
    /// the lower endpoint stays above the top search controls.
    /// The gesture only executes when the search engine has real card metadata.
    /// </summary>
    public async Task SwipeLiveSearchUpAsync(CancellationToken ct,int swipePercent=30)
    {
        var (x,y,w,h)=await WindowRectAsync(ct);
        if(w<180 || h<300)throw new InvalidOperationException("Màn hình quá nhỏ để cuộn LIVE Search.");
        if(swipePercent is < 1 or > 75)throw new ArgumentOutOfRangeException(nameof(swipePercent));
        // Respect the actual user percentage without silently clamping it to 42%.
        int distance=Math.Max(1,(int)Math.Round(h*swipePercent/100.0));
        int startX=x+w/2;
        // Keep gesture fully inside the LIVE result grid: below Search tabs,
        // above the Android navigation bar. The drag has no momentum fling.
        int startY=y+h*87/100;
        int endY=startY-distance;
        // Appium UiAutomator2 dragGesture supports explicit speed and endpoints.
        // Unlike swipeGesture's default speed, this avoids a long momentum fling.
        var gesture=new {startX,startY,endX=startX,endY,speed=180};
        using var _=await SendUiQueryAsync(HttpMethod.Post,SessionPath("execute/sync"),
            new {script="mobile: dragGesture",args=new object[]{gesture}},ct,30);
    }

    public async Task SwipeLiveSearchDownAsync(CancellationToken ct,int swipePercent=30)
    {
        var (x,y,w,h)=await WindowRectAsync(ct);
        if(swipePercent is < 1 or > 75)throw new ArgumentOutOfRangeException(nameof(swipePercent));
        int distance=Math.Max(1,(int)Math.Round(h*swipePercent/100.0));
        int endY=y+h*87/100;
        int startY=endY-distance;
        var gesture=new {startX=x+w/2,startY,endX=x+w/2,endY,speed=180};
        using var _=await SendUiQueryAsync(HttpMethod.Post,SessionPath("execute/sync"),
            new {script="mobile: dragGesture",args=new object[]{gesture}},ct,30);
    }

    /// <summary>
    /// Browse from one already-verified LIVE room to the next. Never use on
    /// the Search Grid, a login screen, or while a modal is active.
    /// </summary>
    public async Task SwipeLiveRoomUpAsync(CancellationToken ct)
    {
        var (x,y,w,h)=await WindowRectAsync(ct);
        if(w<180 || h<360)throw new InvalidOperationException("Màn hình không phù hợp để đổi LIVE.");
        // W3C touch flick: no pause after pointerDown, a short 180ms move, and
        // X=85% keeps the gesture away from central LIVE interaction controls.
        int flickX=x+w*85/100;
        int startY=y+h*74/100;
        int endY=y+h*27/100;
        object[] actions=
        [
            new
            {
                type="pointer",id="live-room-flick",
                parameters=new {pointerType="touch"},
                actions=new object[]
                {
                    new {type="pointerMove",duration=0,x=flickX,y=startY,origin="viewport"},
                    new {type="pointerDown",button=0},
                    new {type="pointerMove",duration=180,x=flickX,y=endY,origin="viewport"},
                    new {type="pointerUp",button=0}
                }
            }
        ];
        using var _=await SendUiQueryAsync(HttpMethod.Post,SessionPath("actions"),
            new {actions},ct,15);
    }

    public async Task TapLiveCardAtAsync(int tapX,int tapY,CancellationToken ct)
    {
        var (x,y,w,h)=await WindowRectAsync(ct);
        if(tapX<x+6 || tapX>=x+w-6 || tapY<y+h*17/100 || tapY>y+h*92/100)
            throw new InvalidOperationException("Tọa độ nhấn ngoài vùng card LIVE hợp lệ.");
        using var _=await SendUiQueryAsync(HttpMethod.Post,SessionPath("execute/sync"),
            new {script="mobile: clickGesture",args=new object[]{new {x=tapX,y=tapY}}},ct,25);
    }

    // A single bounded tap at a fresh UI-source element's center. Avoids repeated
    // XPath find/displayed/click round trips; only call after bounds validation.
    public async Task TapUiPointAsync(int x,int y,CancellationToken ct)
    {
        using var _=await SendUiQueryAsync(HttpMethod.Post,SessionPath("execute/sync"),
            new {script="mobile: clickGesture",args=new object[]{new {x,y}}},ct,12);
    }

    public async Task BackAsync(CancellationToken ct)
    {
        using var _=await SendAsync(HttpMethod.Post,SessionPath("back"),new {},ct);
    }

    public async Task<bool> HideKeyboardAsync(CancellationToken ct)
    {
        try
        {
            using var _=await SendUiQueryAsync(HttpMethod.Post,
                SessionPath("appium/device/hide_keyboard"),new {},ct,12);
            return true;
        }
        catch(InvalidOperationException ex) when(
            ex.Message.Contains("keyboard",StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("not shown",StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("unknown command",StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("unsupported",StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
    }

    public async Task<string> SourceAsync(CancellationToken ct)
    {
        using var doc=await SendUiQueryAsync(HttpMethod.Get,SessionPath("source"),null,ct,35);
        return doc.RootElement.GetProperty("value").GetString()??"";
    }
    public async Task<string> CurrentPackageAsync(CancellationToken ct)
    {
        using var doc=await SendUiQueryAsync(HttpMethod.Get,SessionPath("appium/device/current_package"),null,ct,20);
        return doc.RootElement.GetProperty("value").GetString()??"";
    }
    public async Task<string> CurrentUrlAsync(CancellationToken ct)
    {
        try
        {
            using var doc=await SendAsync(HttpMethod.Get,SessionPath("url"),null,ct);
            return doc.RootElement.GetProperty("value").GetString()??"";
        }
        catch(InvalidOperationException){return "";}
    }
    public async ValueTask DisposeAsync()
    {
        if(_sessionId is not null)
        {
            using var closeTimeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try{using var _=await SendAsync(HttpMethod.Delete,SessionPath(""),null,closeTimeout.Token);}catch{}
            _sessionId=null;
        }
        Performance?.SaveSummary();
        _http.Dispose();
    }
}
