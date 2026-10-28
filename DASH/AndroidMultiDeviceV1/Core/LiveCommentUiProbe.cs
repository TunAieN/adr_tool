using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AndroidMultiDeviceV1.Core;

/// <summary>
/// Bounded, non-publishing comment-UI test. Parses the current XML once,
/// finds the displayed Type... surface, clicks its clickable ancestor once,
/// then inspects the resulting XML. Never types text or presses Send.
/// </summary>
public static class LiveCommentUiProbe
{
    public sealed record CommentTarget(
        bool Found, string Reason, string MatchedBy, string? ResourceId,
        string? ParentResourceId, bool ParentClickable,
        int TapX, int TapY, string? RawBounds);

    static readonly Regex BoundsPattern = new(
        @"^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$", RegexOptions.CultureInvariant);

    static string Attr(XElement element,string key) =>
        element.Attribute(key)?.Value ?? "";

    static bool IsVisible(XElement element) => Attr(element,"displayed") == "true";

    static bool TryCenter(string bounds,out int x,out int y)
    {
        x=0;y=0;
        Match m=BoundsPattern.Match(bounds);
        if(!m.Success)return false;
        int left=int.Parse(m.Groups[1].Value),top=int.Parse(m.Groups[2].Value);
        int right=int.Parse(m.Groups[3].Value),bottom=int.Parse(m.Groups[4].Value);
        if(right<=left+2 || bottom<=top+2)return false;
        x=left+(right-left)/2;y=top+(bottom-top)/2;
        return true;
    }

    public static CommentTarget Inspect(string source)
    {
        if(string.IsNullOrWhiteSpace(source))
            return new(false,"EMPTY_XML","none",null,null,false,0,0,null);
        XDocument doc;
        try { doc=XDocument.Parse(source,LoadOptions.None); }
        catch(System.Xml.XmlException)
        { return new(false,"INVALID_XML","none",null,null,false,0,0,null); }
        var nodes=doc.Descendants().Where(IsVisible).ToList();
        // New LIVE layout uses h4s; retain h42 and text for older layouts.
        XElement? label=null;
        string foundBy="none";
        foreach(var resource in new[]{"h4s","h42"})
        {
            label=nodes.FirstOrDefault(e=>
                Attr(e,"resource-id")=="com.ss.android.ugc.trill:id/"+resource &&
                (Attr(e,"class")=="android.widget.TextView" ||
                 e.Name.LocalName=="android.widget.TextView"));
            if(label is not null){foundBy=resource;break;}
        }
        if(label is null)
        {
            label=nodes.FirstOrDefault(e=>
            {
                string text=Attr(e,"text").Trim();
                return text.Equals("Type...",StringComparison.OrdinalIgnoreCase)
                    || text.Equals("Type…",StringComparison.OrdinalIgnoreCase);
            });
            foundBy="visible-text-Type";
        }
        if(label is null)
            return new(false,"NO_VISIBLE_TYPE_LABEL","none",null,null,false,0,0,null);

        // Resolve the actual tappable surface (the TextView may not be clickable).
        // Do not use generic coordinate guesses if the tree cannot provide bounds.
        XElement? clickable=label.AncestorsAndSelf()
            .FirstOrDefault(e=>IsVisible(e) && Attr(e,"clickable")=="true");
        if(clickable is null)
            return new(true,"NO_CLICKABLE_ANCESTOR",foundBy,Attr(label,"resource-id"),
                null,false,0,0,Attr(label,"bounds"));
        string labelBounds=Attr(label,"bounds");
        string clickableBounds=Attr(clickable,"bounds");
        // Center of Type text, when possible, avoids sibling emoji controls.
        if(!TryCenter(labelBounds,out int tapX,out int tapY)
            && !TryCenter(clickableBounds,out tapX,out tapY))
            return new(true,"INVALID_ELEMENT_BOUNDS",foundBy,Attr(label,"resource-id"),
                Attr(clickable,"resource-id"),true,0,0,clickableBounds);
        return new(true,"READY",foundBy,Attr(label,"resource-id"),
            Attr(clickable,"resource-id"),true,tapX,tapY,clickableBounds);
    }

    static bool HasResource(XDocument doc,string suffix)=>
        doc.Descendants().Any(e=>Attr(e,"resource-id").EndsWith(":id/"+suffix,StringComparison.Ordinal));

    public static async Task<string> RunAsync(AppiumSession session,Action<string> log,CancellationToken ct)
    {
        string folder=Path.Combine(Storage.Folder,"diagnostics",
            session.DeviceKey,"phase5-open-ui-probe-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(folder);
        var notes=new List<string> {
            "PHASE5 UI probe: one open tap only; no text entered, no Send click.",
            "CapturedAt="+DateTime.Now.ToString("O")
        };
        string reason="UNKNOWN";
        try
        {
            // CreateAsync may have entered a new session; verify current room.
            string package=await session.CurrentPackageAsync(ct);
            notes.Add("ForegroundPackage="+package);
            string before=await session.SourceAsync(ct);
            await File.WriteAllTextAsync(Path.Combine(folder,"before.xml"),before,ct);
            byte[] beforePng=await session.ScreenshotPngAsync(ct);
            await File.WriteAllBytesAsync(Path.Combine(folder,"before.png"),beforePng,ct);
            if(!before.Contains("tv_online_audience_num",StringComparison.Ordinal))
            {
                reason="LIVE_ROOM_NOT_CONFIRMED";
                log("PHASE5 UI_PROBE_STOP: "+reason);
                return reason;
            }
            var result=Inspect(before);
            notes.Add("OpenTargetFound="+result.Found);
            notes.Add("OpenTargetReason="+result.Reason);
            notes.Add("OpenTargetMatchedBy="+result.MatchedBy);
            notes.Add("OpenTargetResourceId="+result.ResourceId);
            notes.Add("OpenTargetParent="+result.ParentResourceId);
            notes.Add("OpenTargetParentClickable="+result.ParentClickable);
            notes.Add("OpenTargetBounds="+result.RawBounds);
            log($"PHASE5 OPEN_RESOLVE: found={result.Found}; method={result.MatchedBy}; clickableParent={result.ParentResourceId ?? "none"}; status={result.Reason}");
            if(result.Reason!="READY")
            {
                reason="COMMENT_OPEN_"+result.Reason;
                log("PHASE5 UI_PROBE_STOP: "+reason);
                return reason;
            }
            log($"PHASE5 OPEN_TAP_ATTEMPT: x={result.TapX}; y={result.TapY}; oneTapOnly=true");
            await session.TapUiPointAsync(result.TapX,result.TapY,ct);
            log("PHASE5 OPEN_TAP_REQUESTED");
            await Task.Delay(600,ct);
            string after=await session.SourceAsync(ct);
            await File.WriteAllTextAsync(Path.Combine(folder,"after.xml"),after,ct);
            byte[] afterPng=await session.ScreenshotPngAsync(ct);
            await File.WriteAllBytesAsync(Path.Combine(folder,"after.png"),afterPng,ct);
            var doc=XDocument.Parse(after,LoadOptions.None);
            bool h7r=HasResource(doc,"h7r");
            bool editText=doc.Descendants().Any(e=>
                Attr(e,"class")=="android.widget.EditText" && IsVisible(e));
            bool vzb=HasResource(doc,"vzb");
            bool vxc=HasResource(doc,"vxc");
            notes.Add("After_H7R="+h7r);
            notes.Add("After_VisibleEditText="+editText);
            notes.Add("After_VZB="+vzb);
            notes.Add("After_VXC="+vxc);
            log($"PHASE5 INPUT_UI_CHECK: h7r={h7r}; visibleEditText={editText}; vzb={vzb}; vxc={vxc}");
            reason=h7r || editText?"INPUT_UI_VISIBLE":"INPUT_UI_NOT_VISIBLE";
            log("PHASE5 UI_PROBE_RESULT: "+reason+"; sent=0");
            return reason;
        }
        catch(OperationCanceledException){reason="CANCELLED";throw;}
        catch(Exception ex)
        {
            reason=ex is TimeoutException?"APPUIUM_TIMEOUT":"UI_PROBE_ERROR";
            notes.Add("Exception="+ex.GetType().Name+": "+ex.Message);
            log("PHASE5 UI_PROBE_ERROR: "+ex.GetType().Name+": "+ex.Message);
            return reason;
        }
        finally
        {
            notes.Add("Result="+reason);
            await File.WriteAllLinesAsync(Path.Combine(folder,"diagnostic-summary.txt"),notes,
                CancellationToken.None);
            log("PHASE5 UI_PROBE_DIAGNOSTIC_SAVED: "+folder);
        }
    }
}
