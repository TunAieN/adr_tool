using System.Xml.Linq;
using System.Text.RegularExpressions;

namespace AndroidMultiDeviceV1.Core;

/// <summary>
/// Explicit one-shot UI check in the LIVE already selected by the operator.
/// Exactly one Send click can be requested per invocation; no repeat,
/// scheduling, room switching, or multi-device dispatch.
/// </summary>
public static class LiveCommentOneShot
{
    static string Attr(XElement e,string key)=>e.Attribute(key)?.Value??"";
    static bool HasId(XDocument doc,string suffix)=>doc.Descendants().Any(e=>
        Attr(e,"resource-id").EndsWith(":id/"+suffix,StringComparison.Ordinal));

    // An unrelated EditText (Search, hidden navigation, etc.) does not prove the
    // LIVE composer is open. Resolve an editable node in the lower LIVE overlay,
    // with explicit focus or an unambiguous editor AFTER tapping Type...
    static bool IsEditText(XElement e)=>
        (Attr(e,"class")=="android.widget.EditText" ||
         e.Name.LocalName=="android.widget.EditText") &&
        Attr(e,"displayed")=="true" && Attr(e,"enabled")!="false";

    static bool TryBounds(XElement e,out int x1,out int y1,out int x2,out int y2)
    {
        x1=y1=x2=y2=0;
        Match m=Regex.Match(Attr(e,"bounds"),@"^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$");
        return m.Success && int.TryParse(m.Groups[1].Value,out x1) &&
            int.TryParse(m.Groups[2].Value,out y1) &&
            int.TryParse(m.Groups[3].Value,out x2) &&
            int.TryParse(m.Groups[4].Value,out y2) && x2>x1 && y2>y1;
    }

    static bool IsSearchEdit(XElement e) =>
        new[]{"hwj","hvp"}.Any(id=>
            Attr(e,"resource-id").EndsWith(":id/"+id,StringComparison.Ordinal));

    static bool TryResolveComposer(XDocument doc,bool requireFocus,
        out XElement? editor,out string diagnostic)
    {
        editor=null;
        var visible=doc.Descendants().Where(e=>Attr(e,"displayed")=="true").ToArray();
        int screenBottom=visible.Select(e=>
        {
            return TryBounds(e,out _,out _,out _,out int b)?b:0;
        }).DefaultIfEmpty(0).Max();
        var editors=visible.Where(e=>IsEditText(e) && !IsSearchEdit(e) &&
            TryBounds(e,out _,out int top,out _,out int bottom) &&
            screenBottom>0 && (top+bottom)/2>=screenBottom*45/100).ToList();
        var focused=editors.Where(e=>Attr(e,"focused")=="true").ToList();
        diagnostic=$"candidates={editors.Count}; focused={focused.Count}; screenBottom={screenBottom}";
        if(focused.Count==1) {editor=focused[0];return true;}
        if(focused.Count>1) {diagnostic+="; MULTIPLE_FOCUSED";return false;}
        if(requireFocus) {diagnostic+="; NO_FOCUSED_COMPOSER";return false;}
        if(editors.Count==1) {editor=editors[0];return true;}
        diagnostic+=editors.Count==0?"; NO_COMPOSER_EDITOR":"; AMBIGUOUS_EDITORS";
        return false;
    }

    static UiLocator EditorLocator(XElement editor)
    {
        // Bind input to the single editor verified by the fresh XML, rather
        // than using //android.widget.EditText, which may target a Search field.
        string id=Attr(editor,"resource-id");
        string bounds=Attr(editor,"bounds");
        string selector="//android.widget.EditText";
        if(!string.IsNullOrEmpty(id))selector+="[@resource-id='"+id+"' and @bounds='"+bounds+"']";
        else selector+="[@bounds='"+bounds+"']";
        return new UiLocator("xpath",selector);
    }

    static bool HasEnteredText(XDocument doc,string comment)=>
        TryResolveComposer(doc,false,out XElement? editor,out _) &&
        editor is not null && Attr(editor,"text")==comment;

    static bool TryMutedNotice(XDocument doc,out string evidence)
    {
        evidence="";
        foreach(string value in doc.Descendants()
            .Where(e=>Attr(e,"displayed")!="false")
            .SelectMany(e=>new[]{Attr(e,"text"),Attr(e,"content-desc")})
            .Where(value=>!string.IsNullOrWhiteSpace(value)))
        {
            bool english=value.Contains("muted for the entire LIVE",StringComparison.OrdinalIgnoreCase) ||
                value.Contains("comment wasn't posted because you're muted",StringComparison.OrdinalIgnoreCase) ||
                value.Contains("you've been muted",StringComparison.OrdinalIgnoreCase) ||
                value.Contains("you are muted",StringComparison.OrdinalIgnoreCase);
            bool vietnamese=value.Contains("bị tắt tiếng trong toàn bộ LIVE",StringComparison.OrdinalIgnoreCase) ||
                value.Contains("bạn đã bị tắt tiếng",StringComparison.OrdinalIgnoreCase) ||
                value.Contains("bình luận của bạn không được đăng",StringComparison.OrdinalIgnoreCase) &&
                    value.Contains("tắt tiếng",StringComparison.OrdinalIgnoreCase) ||
                value.Contains("bị cấm",StringComparison.OrdinalIgnoreCase) &&
                    (value.Contains("bình luận",StringComparison.OrdinalIgnoreCase) ||
                     value.Contains("LIVE",StringComparison.OrdinalIgnoreCase));
            if(!english && !vietnamese)continue;
            evidence=value.Trim();
            return true;
        }
        return false;
    }

    // Only resolve Send after the composer text has been independently verified.
    // New TikTok UI: vzb. Keep vxc for older devices. Never select a generic
    // ImageView or guess coordinates if neither exact resource-id is present.
    static readonly string[] SendResourceIds = ["vzb", "vxc"];

    static bool TrySendPoint(XDocument doc,out int x,out int y,out string matchedBy)
    {
        x=0;y=0;matchedBy="none";
        foreach(string suffix in SendResourceIds)
        {
            XElement? icon=doc.Descendants().FirstOrDefault(e=>
                Attr(e,"class")=="android.widget.ImageView" &&
                Attr(e,"resource-id")=="com.ss.android.ugc.trill:id/"+suffix &&
                Attr(e,"displayed")=="true" && Attr(e,"enabled")=="true");
            if(icon is null)continue;
            // ImageView itself may not be clickable; its parent handles taps.
            // Use the ImageView's fresh verified bounds, never stale coordinates.
            Match m=Regex.Match(Attr(icon,"bounds"),@"^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$");
            if(!m.Success)continue;
            int x1=int.Parse(m.Groups[1].Value),y1=int.Parse(m.Groups[2].Value);
            int x2=int.Parse(m.Groups[3].Value),y2=int.Parse(m.Groups[4].Value);
            if(x2<=x1+2 || y2<=y1+2)continue;
            x=x1+(x2-x1)/2;
            y=y1+(y2-y1)/2;
            matchedBy=suffix;
            return true;
        }
        return false;
    }

    public static async Task<string> ExecuteAsync(
        AppiumSession session,string comment,Action<string> log,CancellationToken ct)
        => await ExecuteAsync(session,comment,log,TimeSpan.Zero,ct);

    /// <summary>
    /// Reuses the verified one-shot composer/input/send flow for Phase 5 auto mode.
    /// The optional delay is applied only after text and the exact Send target
    /// have been verified, immediately before the single Send tap.
    /// </summary>
    public static async Task<string> ExecuteAsync(
        AppiumSession session,string comment,Action<string> log,
        TimeSpan delayBeforeSend,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(comment) || comment.Length>500)
            return "COMMENT_INVALID";
        if(delayBeforeSend<TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(delayBeforeSend));

        string folder=Path.Combine(Storage.Folder,"diagnostics",
            session.DeviceKey,"phase5-one-shot-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(folder);
        string reason="UNKNOWN";
        int sendClicks=0;
        try
        {
            string foreground=await session.CurrentPackageAsync(ct);
            if(!string.Equals(foreground,"com.ss.android.ugc.trill",StringComparison.Ordinal))
            {
                reason="NOT_EXPECTED_FOREGROUND";
                log("PHASE5 STOP: "+reason+"; foreground="+foreground);
                return reason;
            }

            string before=await session.SourceAsync(ct);
            await File.WriteAllTextAsync(Path.Combine(folder,"before.xml"),before,ct);
            if(!before.Contains(":id/tv_online_audience_num",StringComparison.Ordinal))
            {
                reason="LIVE_ROOM_NOT_CONFIRMED";
                log("PHASE5 STOP: "+reason);
                return reason;
            }

            var beforeDoc=XDocument.Parse(before,LoadOptions.None);
            var target=LiveCommentUiProbe.Inspect(before);
            bool active=TryResolveComposer(beforeDoc,true,out _,out string beforeDetails);
            log($"PHASE5 COMPOSER_PRECHECK: active={active}; typeTarget={target.Reason}; {beforeDetails}");
            // If Type... is visible, actively tap it. Never treat arbitrary
            // EditText nodes elsewhere in the LIVE hierarchy as the composer.
            if(target.Reason=="READY")
            {
                log($"PHASE5 COMMENT_OPEN_RESOLVE: found={target.Found}; method={target.MatchedBy}; clickableParent={target.ParentResourceId??"none"}; status={target.Reason}");
                await session.TapUiPointAsync(target.TapX,target.TapY,ct);
                log($"PHASE5 COMMENT_OPEN_TAP_REQUESTED: method={target.MatchedBy}; x={target.TapX}; y={target.TapY}; oneTapOnly=true");
                await Task.Delay(650,ct);
            }
            else if(active && !target.Found)
                log("PHASE5 COMMENT_OPEN_ALREADY_ACTIVE: verifiedFocusedComposer=true");
            else
            {
                reason="COMMENT_OPEN_"+target.Reason;
                log("PHASE5 STOP: "+reason+"; no confirmed LIVE composer");
                return reason;
            }

            XDocument? openedDoc=null;
            XElement? composer=null;
            bool composerReady=false;
            for(int readyCheck=1;readyCheck<=3;readyCheck++)
            {
                string opened=await session.SourceAsync(ct);
                await File.WriteAllTextAsync(Path.Combine(folder,$"opened-{readyCheck:00}.xml"),opened,ct);
                openedDoc=XDocument.Parse(opened,LoadOptions.None);
                composerReady=TryResolveComposer(openedDoc,false,out composer,out string details);
                // When we explicitly tapped Type..., a pre-existing editor is
                // not proof the comment UI opened. Demand a measurable change:
                // the Type... label disappeared, or the verified editor was
                // absent/unfocused in the pre-tap hierarchy.
                if(composerReady && composer is not null && target.Reason=="READY")
                {
                    string id=Attr(composer,"resource-id"), bounds=Attr(composer,"bounds");
                    bool existingBefore=beforeDoc.Descendants().Any(e=>
                        IsEditText(e) && Attr(e,"resource-id")==id &&
                        Attr(e,"bounds")==bounds);
                    bool alreadyFocusedBefore=beforeDoc.Descendants().Any(e=>
                        IsEditText(e) && Attr(e,"resource-id")==id &&
                        Attr(e,"bounds")==bounds && Attr(e,"focused")=="true");
                    bool typeStillVisible=LiveCommentUiProbe.Inspect(opened).Found;
                    bool editorFocused=Attr(composer,"focused")=="true";
                    bool proof=(editorFocused && !alreadyFocusedBefore) ||
                        (!typeStillVisible && !existingBefore);
                    details+=$"; typeStillVisible={typeStillVisible}; editorFocused={editorFocused}; transitioned={proof}";
                    composerReady=proof;
                }
                bool mutedNotice=TryMutedNotice(openedDoc,out string mutedEvidence);
                bool h89=HasId(openedDoc,"h89");
                log($"PHASE5 COMPOSER_CHECK {readyCheck}/3: ready={composerReady}; " +
                    $"h89={h89}; mutedNotice={mutedNotice}; {details}");
                if(!composerReady && mutedNotice)
                {
                    reason="ROOM_MUTED_CONFIRMED";
                    log("ROOM_MUTED_CONFIRMED: Đã phát hiện thông báo cấm bình luận. " +
                        $"evidence=\"{mutedEvidence}\"");
                    return reason;
                }
                if(composerReady)break;
                if(readyCheck<3)await Task.Delay(650,ct);
            }
            if(!composerReady || composer is null || openedDoc is null)
            {
                reason="COMMENT_COMPOSER_NOT_CONFIRMED";
                log("PHASE5 STOP: "+reason);
                return reason;
            }
            bool h7r=HasId(openedDoc,"h7r");
            bool vzb=HasId(openedDoc,"vzb");
            bool vxc=HasId(openedDoc,"vxc");
            log($"PHASE5 INPUT_UI_CHECK: h7r={h7r}; composerVerified=True; focused={Attr(composer,"focused")}; editorId={Attr(composer,"resource-id")}; bounds={Attr(composer,"bounds")}; vzb={vzb}; vxc={vxc}");
            // Some TikTok layouts hide/disable Send until a nonempty draft exists.
            // Only the editable input must be ready at this stage; resolve the
            // Send target exclusively after the entered text is verified.
            log($"PHASE5 PRE_INPUT_READY: composerVerified=true; sendBeforeText={vzb || vxc}; sendCheck=DEFERRED_UNTIL_TEXT");
            UiLocator input=EditorLocator(composer);
            bool inputRequested=await session.SetTextAnyAsync([input],comment,ct);
            if(!inputRequested)
            {
                reason="COMMENT_INPUT_NOT_EDITABLE";
                log("PHASE5 STOP: "+reason+"; editor="+input.Value);
                return reason;
            }
            log("PHASE5 COMMENT_TEXT_ENTRY_REQUESTED: "+comment+"; verifiedEditor="+Attr(composer,"resource-id"));
            // A newly composed draft can cause TikTok to render the Send icon
            // asynchronously. Read XML a bounded number of times; do not retype
            // or click any unknown button while waiting.
            bool textVerified=false;
            bool sendReady=false;
            int sendX=0,sendY=0;
            string sendSelector="none";
            for(int check=1; check<=3; check++)
            {
                string entered=await session.SourceAsync(ct);
                await File.WriteAllTextAsync(
                    Path.Combine(folder,$"entered-{check:00}.xml"),entered,ct);
                var enteredDoc=XDocument.Parse(entered,LoadOptions.None);
                textVerified=HasEnteredText(enteredDoc,comment);
                sendReady=textVerified && TrySendPoint(enteredDoc,out sendX,out sendY,out sendSelector);
                log($"PHASE5 AFTER_TEXT_CHECK {check}/3: textMatch={textVerified}; " +
                    $"vzbPresent={HasId(enteredDoc,"vzb")}; vxcPresent={HasId(enteredDoc,"vxc")}; " +
                    $"sendReady={sendReady}; sendSelector={sendSelector}");
                if(sendReady)break;
                if(check<3)await Task.Delay(500,ct);
            }
            log("PHASE5 COMMENT_TEXT_VERIFY: "+(textVerified?"MATCH":"NOT_VERIFIED"));
            if(!textVerified)
            {
                reason="COMMENT_TEXT_NOT_VERIFIED";
                return reason;
            }
            if(!sendReady)
            {
                reason="COMMENT_SEND_NOT_FOUND_AFTER_TEXT";
                log("PHASE5 SEND_NOT_FOUND_AFTER_TEXT: neither vzb nor vxc was visible/enabled " +
                    "after the draft was verified; no tap attempted.");
                return reason;
            }

            // Exactly one publishing tap per invocation; never retry after tap.
            log($"PHASE5 SEND_TARGET_READY: selector={sendSelector}; x={sendX}; y={sendY}");
            if(delayBeforeSend>TimeSpan.Zero)
                await Task.Delay(delayBeforeSend,ct);
            await session.TapUiPointAsync(sendX,sendY,ct);
            sendClicks=1;
            reason="SEND_REQUESTED";
            log($"PHASE5 COMMENT_SEND_REQUESTED: text=\"{comment}\"; sendClicks=1; delivery=UNVERIFIED");
            // Auto mode must return immediately after the successful tap so a
            // room-deadline cancellation during optional diagnostics cannot
            // hide the successful iteration from its caller.
            if(delayBeforeSend>TimeSpan.Zero)return reason;
            await Task.Delay(400,ct);
            try
            {
                string after=await session.SourceAsync(ct);
                await File.WriteAllTextAsync(Path.Combine(folder,"after.xml"),after,ct);
            }
            catch(Exception ex) when(ex is not OperationCanceledException &&
                !AppiumFailureClassifier.IsFatalSessionFailure(ex))
            {
                log("PHASE5 AFTER_SEND_UI_READ_FAILED: "+ex.GetType().Name);
            }
            return reason;
        }
        catch(OperationCanceledException)
        {
            reason=sendClicks>0?"CANCELLED_AFTER_SEND_REQUEST":"CANCELLED_BEFORE_SEND";
            log("PHASE5 "+reason);
            throw;
        }
        catch(Exception ex) when(AppiumFailureClassifier.IsFatalSessionFailure(ex))
        {
            reason="FATAL_APPIUM_SESSION_LOST";
            log("PHASE5 FATAL_APPIUM_SESSION: "+ex.GetType().Name+": "+ex.Message);
            throw;
        }
        catch(Exception ex)
        {
            reason=sendClicks>0?"POST_SEND_ERROR":"ERROR_BEFORE_SEND";
            log("PHASE5 ONE_SHOT_ERROR: "+ex.GetType().Name+": "+ex.Message);
            return reason;
        }
        finally
        {
            // Capture visual evidence when no publish request was issued.
            // In particular, a misleading generic EditText must not cause a click.
            if(sendClicks==0 && (reason=="COMMENT_SEND_NOT_FOUND_AFTER_TEXT" ||
                reason=="COMMENT_TEXT_NOT_VERIFIED" ||
                reason=="COMMENT_COMPOSER_NOT_CONFIRMED" ||
                reason=="ROOM_MUTED_CONFIRMED" ||
                reason.StartsWith("COMMENT_OPEN_",StringComparison.Ordinal)))
            {
                try
                {
                    byte[] png=await session.ScreenshotPngAsync(CancellationToken.None);
                    await File.WriteAllBytesAsync(
                        Path.Combine(folder,"phase5-stopped.png"),
                        png,CancellationToken.None);
                }
                catch(Exception ex)
                {
                    log("PHASE5 SEND_DIAGNOSTIC_IMAGE_FAILED: "+ex.GetType().Name);
                }
            }
            try
            {
                string summary=$"Result={reason}{Environment.NewLine}SendClicks={sendClicks}{Environment.NewLine}Delivery=UNVERIFIED{Environment.NewLine}CapturedAt={DateTime.Now:O}{Environment.NewLine}";
                await File.WriteAllTextAsync(Path.Combine(folder,"diagnostic-summary.txt"),summary,CancellationToken.None);
                log("PHASE5 DIAGNOSTIC_SAVED: "+folder);
            }
            catch(Exception ex)
            {
                // Diagnostics must never turn a completed Send tap into a
                // failed loop iteration which could retry the same action.
                log("PHASE5 DIAGNOSTIC_SUMMARY_FAILED: "+ex.GetType().Name);
            }
            log($"PHASE5 COMPLETE: reason={reason}; sendClicks={sendClicks}; delivery=UNVERIFIED");
        }
    }
}
