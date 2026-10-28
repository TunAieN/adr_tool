namespace AndroidMultiDeviceV1.Core;

public enum CaptchaDismissStatus { NotPresent, Dismissed, Failed }
public sealed record CaptchaDismissResult(CaptchaDismissStatus Status,string Source);

/// <summary>Bounded cleanup for TikTok's one-time, in-app LIVE policy dialog.</summary>
public static class LivePopupHandler
{
    static readonly UiLocator VirtualItemsGotItButton=new("xpath",
        "//android.widget.Button[@text=\"Got it\"]");
    static readonly UiLocator[] CaptchaCloseButtons=
    [
        new("xpath","//android.widget.Button[@content-desc=\"Close\"]"),
        new("xpath","//android.widget.ImageView[@content-desc=\"Close\"]"),
        new("xpath","//*[@content-desc=\"Close\"]"),
        new("xpath","//android.widget.Button[@text=\"×\" or @text=\"X\"]")
    ];

    public static bool IsVerifyCaptcha(string source) =>
        source.Contains("Verify to continue",StringComparison.OrdinalIgnoreCase) ||
        source.Contains("content-desc=\"Close\"",StringComparison.Ordinal) &&
        (source.Contains("captcha",StringComparison.OrdinalIgnoreCase) ||
         source.Contains("puzzle",StringComparison.OrdinalIgnoreCase) ||
         source.Contains("verify",StringComparison.OrdinalIgnoreCase));

    public static async Task<CaptchaDismissResult> DismissVerifyCaptchaAsync(
        AppiumSession session,Action<string> log,CancellationToken ct,string source,string scope)
    {
        ct.ThrowIfCancellationRequested();
        bool detected=IsVerifyCaptcha(source);
        // Some TikTok versions expose only the accessible Close button while
        // the puzzle title is rendered graphically. Probe the exact X only.
        if(!detected && !source.Contains("content-desc=\"Close\"",StringComparison.Ordinal))
            return new(CaptchaDismissStatus.NotPresent,source);

        log($"{scope} CAPTCHA_DETECTED: Verify to continue/puzzle; tìm nút X chính xác.");
        try
        {
            string? closeElement=null;
            foreach(var locator in CaptchaCloseButtons)
            {
                closeElement=await session.FindAsync(locator,ct,2);
                if(closeElement is not null)break;
            }
            if(closeElement is null)
            {
                log($"{scope} CAPTCHA_DISMISS_FAILED: không tìm thấy nút X/Close; UI giữ trạng thái FAILED.");
                return new(CaptchaDismissStatus.Failed,source);
            }

            await session.ClickElementAsync(closeElement,ct);
            await Task.Delay(1400,ct);
            string fresh=await session.SourceAsync(ct);
            if(IsVerifyCaptcha(fresh))
            {
                log($"{scope} CAPTCHA_DISMISS_FAILED: đã click X nhưng popup vẫn tồn tại; không tiếp tục đánh giá UI.");
                return new(CaptchaDismissStatus.Failed,fresh);
            }
            log($"{scope} CAPTCHA_DISMISSED: nút X đã đóng popup; UI sạch được xác minh bằng source mới.");
            return new(CaptchaDismissStatus.Dismissed,fresh);
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
        catch(Exception ex) when(AppiumFailureClassifier.IsFatalSessionFailure(ex)){throw;}
        catch(Exception ex)
        {
            log($"{scope} CAPTCHA_DISMISS_FAILED: {ex.GetType().Name}; không tiếp tục đánh giá UI.");
            return new(CaptchaDismissStatus.Failed,source);
        }
    }

    public static async Task<bool> DismissVirtualItemsPoliciesAsync(
        AppiumSession session,Action<string> log,CancellationToken ct,string? sourceHint=null)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            if(sourceHint is not null &&
               !sourceHint.Contains("text=\"Got it\"",StringComparison.Ordinal))
                return false;
            string? button=await session.FindAsync(VirtualItemsGotItButton,ct,2);
            if(button is null)return false;
            log("VIRTUAL_ITEMS_POLICY_DETECTED: popup=Virtual Items and Rewards Policies update.");
            await session.ClickElementAsync(button,ct);
            await Task.Delay(650,ct);
            log("VIRTUAL_ITEMS_POLICY_DISMISSED: Got it clicked; continue current workflow.");
            return true;
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
        catch(Exception ex) when(AppiumFailureClassifier.IsFatalSessionFailure(ex)){throw;}
        catch(Exception ex)
        {
            // This is a one-time optional popup. A short timeout, stale node,
            // or an already-dismissed dialog must not reject the LIVE room.
            log($"VIRTUAL_ITEMS_POLICY_SKIPPED: {ex.GetType().Name}; continue current workflow.");
            return false;
        }
    }
}
