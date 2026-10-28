using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

namespace AndroidMultiDeviceV1.Core;

/// <summary>
/// V2.1.67: diagnostic-only, read-only ABBA benchmark for UiAutomator2 idle waits.
/// This class never taps, swipes, types, comments, changes accounts or changes
/// settings in other Appium sessions. Always restores the original setting.
/// </summary>
public static class AppiumIdleBenchmark
{
    private const long BaselineMs = 10000;
    private const long CandidateMs = 1000;
    private const int Blocks = 10;
    private const int WarmupSamplesPerSetting = 2;
    private const int MinNodes = 5;
    private const double MaxNodeMedianDrift = 0.20;

    // Resource IDs are structural selectors only. Never record text, username,
    // room identifiers, UI XML, screenshots or credentials in benchmark files.
    private static readonly string[] DiagnosticAnchors =
    {
        "com.ss.android.ugc.trill:id/tv_online_audience_num",
        "com.ss.android.ugc.trill:id/h4s",
        "com.ss.android.ugc.trill:id/k1s",
        "com.ss.android.ugc.trill:id/user_name",
        "com.ss.android.ugc.trill:id/h89",
        "com.ss.android.ugc.trill:id/ka6",
        "com.ss.android.ugc.trill:id/hwj"
    };

    private sealed record Sample(
        string Mode, int Index, long SourceMs, long FindMs,
        int NodeCount, bool RootOk, bool FindOk,
        string[] Anchors);

    private sealed record Stats(long MedianMs, long P95Ms, long MinMs,
        long MaxMs, long AverageMs);

    private static Stats Summarize(IEnumerable<long> values)
    {
        long[] a = values.Order().ToArray();
        if(a.Length == 0) return new Stats(0, 0, 0, 0, 0);
        int midpoint = a.Length / 2;
        long median = a.Length % 2 == 1
            ? a[midpoint]
            : (a[midpoint - 1] + a[midpoint]) / 2;
        // Nearest-rank percentile: rank=ceil(0.95*N), 1-based.
        long p95 = a[Math.Clamp((int)Math.Ceiling(a.Length * 0.95) - 1,
            0, a.Length - 1)];
        return new Stats(median, p95, a[0], a[^1],
            (long)a.Average());
    }

    private static async Task<Sample> MeasureAsync(AppiumSession session,
        string mode, int index, CancellationToken ct)
    {
        // The commands are sequential on one session. The only measured
        // operations are source XML and a read-only FrameLayout lookup.
        var sw = Stopwatch.StartNew();
        string xml = await session.SourceAsync(ct);
        long sourceMs = sw.ElapsedMilliseconds;
        var doc = XDocument.Parse(xml, LoadOptions.None);
        int nodes = doc.Descendants().Count();
        bool rootOk = doc.Root?.Name.LocalName == "hierarchy";
        var ids = doc.Descendants().Attributes("resource-id")
            .Select(x => x.Value).ToHashSet(StringComparer.Ordinal);
        string[] anchors = DiagnosticAnchors.Where(ids.Contains).ToArray();

        sw.Restart();
        bool found = await session.FindAsync(
            new UiLocator("xpath", "//android.widget.FrameLayout"), ct) is not null;
        long findMs = sw.ElapsedMilliseconds;
        return new Sample(mode, index, sourceMs, findMs,
            nodes, rootOk, found, anchors);
    }

    private static string Format(Stats source, Stats find, int count,
        int nodesMin, int nodesMax)
        => $"n={count}; sourceMedianMs={source.MedianMs}; " +
           $"sourceP95Ms={source.P95Ms}; sourceAvgMs={source.AverageMs}; " +
           $"findMedianMs={find.MedianMs}; findP95Ms={find.P95Ms}; " +
           $"findAvgMs={find.AverageMs}; nodesMin={nodesMin}; nodesMax={nodesMax}";

    private static bool StructuralValidation(Sample[] baseline,
        Sample[] candidate, out string reason)
    {
        if(baseline.Length != Blocks * 2 || candidate.Length != Blocks * 2)
        {
            reason = "missing_samples";
            return false;
        }
        if(baseline.Concat(candidate).Any(s => !s.RootOk || !s.FindOk || s.NodeCount < MinNodes))
        {
            reason = "root_find_or_node_invalid";
            return false;
        }
        long baseMedian = Summarize(baseline.Select(x => (long)x.NodeCount)).MedianMs;
        long candidateMedian = Summarize(candidate.Select(x => (long)x.NodeCount)).MedianMs;
        double ratio = baseMedian > 0 ?
            Math.Abs(candidateMedian - baseMedian) / (double)baseMedian : 1;
        if(ratio > MaxNodeMedianDrift)
        {
            reason = "node_count_drift_over_20_percent";
            return false;
        }
        // If a critical selector is visible in >=90% of baseline samples,
        // require it in >=90% candidate samples too. This does not claim
        // the entire TikTok app/UI is correct, just the observed screen.
        foreach(string anchor in DiagnosticAnchors)
        {
            int baseHits = baseline.Count(s => s.Anchors.Contains(anchor));
            int candidateHits = candidate.Count(s => s.Anchors.Contains(anchor));
            if(baseHits >= 18 && candidateHits < 18)
            {
                reason = "stable_anchor_missing:" + anchor.Split(':').Last();
                return false;
            }
        }
        reason = "sampled_screen_only";
        return true;
    }

    public static async Task RunAsync(AppiumSession session,
        Action<string> log, CancellationToken ct)
    {
        long original = await session.GetIdleTimeoutAsync(ct);
        string diagnosticPath = Path.Combine(Storage.Folder, "diagnostics",
            "app-idle-abba-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
        log($"APP_IDLE_TEST_START: originalMs={original}; baselineMs={BaselineMs}; candidateMs={CandidateMs}; " +
            $"warmup={WarmupSamplesPerSetting}/setting; samplesPerSetting={Blocks * 2}; " +
            "design=ABBA; mode=READ_ONLY; restoreAfter=true");

        var baseline = new List<Sample>();
        var candidate = new List<Sample>();
        bool settingMayHaveChanged = false;
        bool completed = false;
        bool restored = false;
        string result = "not_completed";
        string validationReason = "incomplete";
        long current = original;
        var duration = Stopwatch.StartNew();

        async Task ApplyAsync(long value)
        {
            ct.ThrowIfCancellationRequested();
            if(current == value) return;
            // Mark before request: even a network failure might have applied
            // the setting on the server before its response was received.
            settingMayHaveChanged = true;
            await session.SetIdleTimeoutAsync(value, ct);
            long readback = await session.GetIdleTimeoutAsync(ct);
            if(readback != value)
                throw new InvalidOperationException("Appium không xác nhận idle setting được yêu cầu.");
            current = readback;
        }

        try
        {
            // Warm both settings, then exclude all warmups from statistics.
            foreach(long setting in new[] {BaselineMs, CandidateMs})
            {
                await ApplyAsync(setting);
                for(int i = 0; i < WarmupSamplesPerSetting; i++)
                {
                    var warmup = await MeasureAsync(session,"warmup",i+1,ct);
                    if(!warmup.RootOk || !warmup.FindOk || warmup.NodeCount < MinNodes)
                        throw new InvalidOperationException("Màn hình không hợp lệ ở bước warmup.");
                    log($"APP_IDLE_WARMUP: settingMs={setting}; n={i+1}/{WarmupSamplesPerSetting}; " +
                        $"sourceMs={warmup.SourceMs}; findMs={warmup.FindMs}; nodes={warmup.NodeCount}");
                }
            }
            // Each block ABBA counterbalances sampling order while limiting
            // setting churn. 10 blocks = 20 A and 20 B samples.
            for(int block = 0; block < Blocks; block++)
            {
                long[] order = {BaselineMs, CandidateMs, CandidateMs, BaselineMs};
                foreach(long setting in order)
                {
                    ct.ThrowIfCancellationRequested();
                    await ApplyAsync(setting);
                    bool isBase = setting == BaselineMs;
                    var list = isBase ? baseline : candidate;
                    string mode = isBase ? "baseline" : "candidate";
                    var point = await MeasureAsync(session, mode, list.Count+1,ct);
                    list.Add(point);
                    log($"APP_IDLE_SAMPLE: block={block+1}/{Blocks}; settingMs={setting}; " +
                        $"mode={mode}; n={list.Count}/{Blocks*2}; sourceMs={point.SourceMs}; " +
                        $"findMs={point.FindMs}; nodes={point.NodeCount}; " +
                        $"rootOk={point.RootOk}; findOk={point.FindOk}");
                    // Fail fast if the screen is lost. Never recommend a setting
                    // on the basis of a broken or partially captured UI tree.
                    if(!point.RootOk || !point.FindOk || point.NodeCount < MinNodes)
                        throw new InvalidOperationException("Không xác minh được cấu trúc UI trong benchmark.");
                }
            }
            var a = baseline.ToArray();
            var b = candidate.ToArray();
            var aSource = Summarize(a.Select(x => x.SourceMs));
            var bSource = Summarize(b.Select(x => x.SourceMs));
            var aFind = Summarize(a.Select(x => x.FindMs));
            var bFind = Summarize(b.Select(x => x.FindMs));
            bool shapeValid = StructuralValidation(a,b,out validationReason);
            double reductionPct = aSource.MedianMs > 0
                ? 100.0 * (aSource.MedianMs - bSource.MedianMs) / aSource.MedianMs
                : 0;
            result = shapeValid ? "sampled_screen_valid" : "sampled_screen_invalid";
            completed = true;
            log($"APP_IDLE_TEST_COMPARE: baseline=({Format(aSource,aFind,a.Length,a.Min(x=>x.NodeCount),a.Max(x=>x.NodeCount))}); " +
                $"candidate=({Format(bSource,bFind,b.Length,b.Min(x=>x.NodeCount),b.Max(x=>x.NodeCount))}); " +
                $"sourceReductionMedianPct={reductionPct.ToString("F1",CultureInfo.InvariantCulture)}; " +
                $"structurallyValid={shapeValid}; validation={validationReason}; " +
                "keepChange=false");
            log("APP_IDLE_TEST_SCOPE: chỉ xác minh màn hình hiện tại; " +
                "không tự cấu hình cho chức năng khác.");
        }
        catch(OperationCanceledException)
        {
            result = "cancelled";
            throw;
        }
        catch(Exception ex)
        {
            result = "error:" + ex.GetType().Name;
            log($"APP_IDLE_TEST_ERROR: {ex.GetType().Name}; giữ nguyên cấu hình hoạt động.");
            throw;
        }
        finally
        {
            // A cancellation must not prevent restoration. The caller always
            // disposes the isolated diagnostic session even on failure.
            try
            {
                if(settingMayHaveChanged)
                {
                    using var restore = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                    await session.SetIdleTimeoutAsync(original, restore.Token);
                    long check = await session.GetIdleTimeoutAsync(restore.Token);
                    if(check != original)
                        throw new InvalidOperationException("Không xác minh được việc khôi phục setting.");
                }
                restored = true;
                log($"APP_IDLE_TEST_RESTORED: currentMs={original}; verified=true");
            }
            catch(Exception ex)
            {
                restored = false;
                log($"APP_IDLE_TEST_RESTORE_ERROR: {ex.GetType().Name}; " +
                    "sẽ đóng session chẩn đoán; không tiếp tục thao tác.");
                throw;
            }
            finally
            {
                // Never write XML, text content, credentials, usernames or
                // other identifiers to disk: numerical timing + known IDs only.
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(diagnosticPath)!);
                    var a = baseline.ToArray();
                    var b = candidate.ToArray();
                    var aSource = Summarize(a.Select(x => x.SourceMs));
                    var bSource = Summarize(b.Select(x => x.SourceMs));
                    var aFind = Summarize(a.Select(x => x.FindMs));
                    var bFind = Summarize(b.Select(x => x.FindMs));
                    File.WriteAllText(diagnosticPath,JsonSerializer.Serialize(new
                    {
                        schema="appium-idle-abba-v2",
                        originalMs=original, baselineMs=BaselineMs, candidateMs=CandidateMs,
                        warmupPerSetting=WarmupSamplesPerSetting,
                        plannedSamplesPerSetting=Blocks*2,
                        design="ABBA", runtimeMs=duration.ElapsedMilliseconds,
                        completed, restored, result, validationReason,
                        baseline=new {source=aSource,find=aFind,samples=a},
                        candidate=new {source=bSource,find=bFind,samples=b}
                    },new JsonSerializerOptions { WriteIndented=true }));
                    log($"APP_IDLE_TEST_REPORT_SAVED: {diagnosticPath}; " +
                        $"baselineN={a.Length}; candidateN={b.Length}; restored={restored}");
                }
                catch(Exception ex)
                {
                    log($"APP_IDLE_TEST_REPORT_ERROR: {ex.GetType().Name}");
                }
            }
        }
    }
}
