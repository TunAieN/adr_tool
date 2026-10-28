using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace AndroidMultiDeviceV1.Core;

/// <summary>
/// Read-only, metadata-only timing diagnostics. NEVER log HTTP payloads,
/// element locators, room IDs, credentials, or user-entered text.
/// </summary>
public sealed class AppiumPerformanceMonitor
{
    readonly Action<string> _log;
    readonly string _file;
    readonly ConcurrentDictionary<string,Counter> _totals=new(StringComparer.Ordinal);
    readonly Stopwatch _sessionElapsed=Stopwatch.StartNew();
    const long SlowThresholdMs=2000;
    sealed class Counter
    {
        public long Count;
        public long TotalMs;
        public long MaxMs;
        public long Errors;
        public long Slow;
        // Bounded recent latencies for median/P95 without growing memory
        // indefinitely during multi-hour sessions.
        const int MaxWindow=8192;
        public readonly Queue<long> Latencies=new();
        public readonly object Sync=new();
        public void Add(long ms,bool ok)
        {
            lock(Sync)
            {
                Count++;TotalMs+=ms;MaxMs=Math.Max(MaxMs,ms);
                if(!ok)Errors++;
                if(ms>=SlowThresholdMs)Slow++;
                Latencies.Enqueue(ms);
                if(Latencies.Count>MaxWindow)Latencies.Dequeue();
            }
        }
        public object Snapshot(string operation)
        {
            lock(Sync)
            {
                var sorted=Latencies.Order().ToArray();
                long median=0,p95=0;
                if(sorted.Length>0)
                {
                    int mid=sorted.Length/2;
                    median=sorted.Length%2==1?sorted[mid]:
                        (sorted[mid-1]+sorted[mid])/2;
                    p95=sorted[Math.Clamp((int)Math.Ceiling(sorted.Length*0.95)-1,
                        0,sorted.Length-1)];
                }
                return new {operation,count=Count,totalMs=TotalMs,
                    averageMs=Count==0?0:TotalMs/Count,
                    medianMs=median,p95Ms=p95,
                    latencySampleCount=sorted.Length,
                    percentileWindowIsTruncated=Count>sorted.Length,
                    maxMs=MaxMs,errors=Errors,slow=Slow};
            }
        }
    }
    public AppiumPerformanceMonitor(Action<string> log,string folder,string tag)
    {
        _log=log;
        string safe=new string(tag.Select(c=>char.IsLetterOrDigit(c)?c:'_').ToArray());
        safe=safe.Length>36?safe[..36]:safe;
        _file=Path.Combine(folder,"diagnostics","perf-appium-"+
            DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+safe+".json");
    }
    // Only stable categories; URLs may include element IDs (never log them).
    public static string Category(string method,string path)
    {
        if(path.EndsWith("/appium/settings",StringComparison.Ordinal))
            return method=="GET"?"IDLE_SETTINGS_READ":"IDLE_SETTINGS_UPDATE";
        if(path.EndsWith("/source",StringComparison.Ordinal))return "SOURCE_XML";
        if(path.EndsWith("/screenshot",StringComparison.Ordinal))return "SCREENSHOT";
        if(path.Contains("/element/",StringComparison.Ordinal) &&
           path.EndsWith("/value",StringComparison.Ordinal))return "TEXT_ENTRY";
        if(path.Contains("/element/",StringComparison.Ordinal) &&
           path.EndsWith("/click",StringComparison.Ordinal))return "ELEMENT_CLICK";
        if(path.Contains("/element/",StringComparison.Ordinal) &&
           path.EndsWith("/text",StringComparison.Ordinal))return "ELEMENT_TEXT";
        if(path.Contains("/element/",StringComparison.Ordinal))return "ELEMENT_ATTRIBUTE";
        if(path.EndsWith("/elements",StringComparison.Ordinal))return "FIND_ELEMENTS";
        if(path.EndsWith("/element",StringComparison.Ordinal))return "FIND_ELEMENT";
        if(path.EndsWith("/execute/sync",StringComparison.Ordinal))return "MOBILE_GESTURE";
        if(path.EndsWith("/back",StringComparison.Ordinal))return "BACK";
        if(path.EndsWith("/window/rect",StringComparison.Ordinal))return "WINDOW_RECT";
        if(path.EndsWith("/current_package",StringComparison.Ordinal))return "CURRENT_PACKAGE";
        if(path=="session")return "SESSION_CREATE";
        if(method=="DELETE")return "SESSION_CLOSE";
        return "OTHER_WEBDRIVER";
    }
    public void Record(string operation,long elapsedMs,bool success)
    {
        _totals.GetOrAdd(operation,_=>new Counter()).Add(elapsedMs,success);
        if(elapsedMs>=SlowThresholdMs || !success)
        {
            // Slow/error events are bounded by actual executed operations;
            // log no payload, XPath, comment text, usernames, or ADB serial.
            try{_log($"PERF_APPIUM: action={operation}; ms={elapsedMs}; ok={success}");}
            catch { /* diagnostics must never disrupt WebDriver */ }
        }
    }
    public void SaveSummary()
    {
        try
        {
            var totals=_totals.OrderBy(x=>x.Key)
                .Select(x=>x.Value.Snapshot(x.Key)).ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file,JsonSerializer.Serialize(new {
                schema="appium-perf-v1",elapsedMs=_sessionElapsed.ElapsedMilliseconds,
                slowThresholdMs=SlowThresholdMs, operations=totals},
                new JsonSerializerOptions{WriteIndented=true}));
            _log($"PERF_APPIUM_SUMMARY_SAVED: {_file}; groups={totals.Length}");
        }
        catch(Exception){ /* Never block cleanup or alter tool functionality. */ }
    }
}
