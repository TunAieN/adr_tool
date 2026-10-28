using System.Diagnostics;

namespace AndroidMultiDeviceV1.Core;

public sealed record LiveCommentAutoSendResult(int SentCount,int CompletedCycles,string FinishReason);

/// <summary>
/// Phase 5 auto-comment loop for the LIVE room accepted by Phase 3. Every UI
/// action is delegated to the verified one-shot implementation; this class
/// owns only draft rotation, timing, cancellation and loop recovery.
/// </summary>
public static class LiveCommentAutoSender
{
    const int ActionDelayMinMilliseconds=999;
    const int ActionDelayMaxMilliseconds=2111;
    const int RoundDelayMinMilliseconds=1500;
    const int RoundDelayMaxMilliseconds=4000;

    public static async Task<LiveCommentAutoSendResult> ExecuteAsync(
        AppiumSession session,List<string> drafts,int durationSeconds,
        Action<string> log,CancellationToken ct,
        Func<CancellationToken,Task<string?>>? roomStateCheckpoint=null,
        Action? onCommentSent=null,
        Func<IReadOnlyList<string>>? draftProvider=null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(drafts);
        ArgumentNullException.ThrowIfNull(log);
        if(durationSeconds<=0)throw new ArgumentOutOfRangeException(nameof(durationSeconds));

        var library=NormalizeDrafts(drafts);
        if(library.Count==0 && draftProvider is null)
        {
            log("PHASE5 AUTO_STOP: COMMENT_LIBRARY_EMPTY");
            return new LiveCommentAutoSendResult(0,0,"COMMENT_LIBRARY_EMPTY");
        }

        var unused=new List<string>();
        var stopwatch=Stopwatch.StartNew();
        var limit=TimeSpan.FromSeconds(durationSeconds);
        int sentCount=0;
        int completedCycles=0;
        int consecutiveComposerFails=0;
        bool cycleStarted=false;
        bool emptyLibraryLogged=false;
        string? lastSelected=null;

        using var deadlineCts=CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadlineCts.CancelAfter(limit);
        CancellationToken loopToken=deadlineCts.Token;

        log($"PHASE5 AUTO_START: drafts={library.Count}; durationSeconds={durationSeconds}");
        try
        {
            while(stopwatch.Elapsed<limit)
            {
                loopToken.ThrowIfCancellationRequested();
                if(draftProvider is not null)
                {
                    try
                    {
                        var latest=NormalizeDrafts(draftProvider());
                        if(!library.SequenceEqual(latest,StringComparer.Ordinal))
                        {
                            int previousCount=library.Count;
                            library=latest;
                            unused.Clear();
                            cycleStarted=false;
                            emptyLibraryLogged=false;
                            log($"PHASE5 COMMENT_CONFIG_HOT_RELOADED: " +
                                $"previous={previousCount}; current={library.Count}; " +
                                "apply=NEXT_COMMENT.");
                        }
                    }
                    catch(OperationCanceledException) when(loopToken.IsCancellationRequested){throw;}
                    catch(Exception ex)
                    {
                        // Atomic config saving normally prevents partial reads.
                        // If disk/JSON is briefly unavailable, retain the last
                        // known-good library and retry before the next comment.
                        log($"PHASE5 COMMENT_CONFIG_RELOAD_SKIPPED: {ex.GetType().Name}; " +
                            $"usingLastKnownGood={library.Count}.");
                    }
                }

                // Read-only in-room checkpoint is opt-in; all existing callers
                // without it retain precisely the original Phase 5 behavior.
                if(roomStateCheckpoint is not null)
                {
                    string? stopReason=await roomStateCheckpoint(loopToken);
                    if(!string.IsNullOrWhiteSpace(stopReason))
                    {
                        log($"PHASE5 ROOM_GUARD_STOP: reason={stopReason}; " +
                            "no action requested in an unverified or restricted room.");
                        return new LiveCommentAutoSendResult(sentCount,completedCycles,stopReason);
                    }
                }
                if(library.Count==0)
                {
                    if(!emptyLibraryLogged)
                    {
                        log("PHASE5 COMMENT_LIBRARY_EMPTY_WAITING: hotReload=true; " +
                            "worker remains active until config is saved or room timer ends.");
                        emptyLibraryLogged=true;
                    }
                    await DelayWithinDeadlineAsync(stopwatch,limit,1500,loopToken);
                    continue;
                }
                if(unused.Count==0)
                {
                    if(cycleStarted)
                    {
                        completedCycles++;
                        log($"PHASE5 COMMENT_CYCLE_RESET: completedCycles={completedCycles}");
                    }
                    unused.AddRange(library);
                    Shuffle(unused,lastSelected);
                    cycleStarted=true;
                }

                // Pop now so one broken draft/UI state cannot pin the loop.
                // Once every draft was attempted, a fresh shuffle begins.
                string comment=unused[^1];
                unused.RemoveAt(unused.Count-1);
                lastSelected=comment;
                log($"PHASE5 DRAFT_SELECTED: cycleRemaining={unused.Count}");

                try
                {
                    int actionDelay=Random.Shared.Next(
                        ActionDelayMinMilliseconds,ActionDelayMaxMilliseconds+1);
                    string result=await LiveCommentOneShot.ExecuteAsync(
                        session,comment,log,TimeSpan.FromMilliseconds(actionDelay),loopToken);
                    if(string.Equals(result,"ROOM_MUTED_CONFIRMED",StringComparison.Ordinal))
                    {
                        log("PHASE5 AUTO_STOP: ROOM_MUTED_CONFIRMED; " +
                            "stop current room immediately and request room transition.");
                        return new LiveCommentAutoSendResult(
                            sentCount,completedCycles,"ROOM_MUTED_CONFIRMED");
                    }
                    bool composerStrike=result is "COMMENT_COMPOSER_NOT_CONFIRMED" or
                        "COMMENT_INPUT_NOT_EDITABLE";
                    if(composerStrike)
                    {
                        consecutiveComposerFails++;
                        log($"PHASE5 COMPOSER_FAIL_STRIKE: {consecutiveComposerFails}/2; " +
                            $"reason={result}");
                        if(consecutiveComposerFails>=2)
                        {
                            log("ROOM_MUTED_CONFIRMED: suy luận theo hành vi sau 2 lần liên tiếp " +
                                "không thể mở hoặc thao tác ô bình luận.");
                            log("PHASE5 AUTO_STOP: ROOM_MUTED_CONFIRMED; " +
                                "source=COMPOSER_FAIL_STRIKE; stop current room immediately.");
                            return new LiveCommentAutoSendResult(
                                sentCount,completedCycles,"ROOM_MUTED_CONFIRMED");
                        }
                    }
                    else
                    {
                        if(consecutiveComposerFails>0)
                            log($"PHASE5 COMPOSER_FAIL_STRIKE_RESET: " +
                                $"previous={consecutiveComposerFails}; reason={result}");
                        consecutiveComposerFails=0;
                    }
                    if(!string.Equals(result,"SEND_REQUESTED",StringComparison.Ordinal))
                        throw new InvalidOperationException("one-shot result="+result);

                    consecutiveComposerFails=0;
                    sentCount++;
                    onCommentSent?.Invoke();
                    log($"PHASE5 COMMENT_SENT_SUCCESS: sent={sentCount}; cycleRemaining={unused.Count}");
                }
                catch(OperationCanceledException){throw;}
                catch(Exception ex) when(AppiumFailureClassifier.IsFatalSessionFailure(ex))
                {
                    log($"PHASE5 FATAL_APPIUM_SESSION: {ex.GetType().Name}: {ex.Message}");
                    throw;
                }
                catch(Exception ex)
                {
                    log($"PHASE5 LOOP_ACTION_FAILED: {ex.GetType().Name}: {ex.Message}");
                    await CloseKeyboardBestEffortAsync(session,log,loopToken);
                    await DelayWithinDeadlineAsync(stopwatch,limit,
                        Random.Shared.Next(RoundDelayMinMilliseconds,
                            RoundDelayMaxMilliseconds+1),loopToken);
                    continue;
                }

                await DelayWithinDeadlineAsync(stopwatch,limit,
                    Random.Shared.Next(RoundDelayMinMilliseconds,
                        RoundDelayMaxMilliseconds+1),loopToken);
            }
        }
        catch(OperationCanceledException) when(deadlineCts.IsCancellationRequested &&
            !ct.IsCancellationRequested)
        {
            // Reaching the room deadline is normal completion for Phase 5.
        }

        ct.ThrowIfCancellationRequested();
        log($"PHASE5 AUTO_END_TIME_REACHED: sent={sentCount}; elapsed={stopwatch.Elapsed.TotalSeconds:F1}s");
        return new LiveCommentAutoSendResult(sentCount,completedCycles,"AUTO_END_TIME_REACHED");
    }

    static List<string> NormalizeDrafts(IEnumerable<string> drafts) => drafts
        .Select(value=>value?.Trim()??"")
        .Where(value=>value.Length is >0 and <=500)
        .ToList();

    static void Shuffle(List<string> values,string? lastSelected)
    {
        for(int i=values.Count-1;i>0;i--)
        {
            int j=Random.Shared.Next(i+1);
            (values[i],values[j])=(values[j],values[i]);
        }
        // Values are popped from the end. Avoid an immediate repeat across a
        // cycle boundary when the library has another available sentence.
        if(values.Count>1 && string.Equals(values[^1],lastSelected,StringComparison.Ordinal))
            (values[^1],values[0])=(values[0],values[^1]);
    }

    static async Task CloseKeyboardBestEffortAsync(
        AppiumSession session,Action<string> log,CancellationToken ct)
    {
        try
        {
            bool hidden=await session.HideKeyboardAsync(ct);
            log($"PHASE5 KEYBOARD_CLOSE_AFTER_FAILURE: requested={hidden}");
        }
        catch(OperationCanceledException){throw;}
        catch(Exception ex) when(AppiumFailureClassifier.IsFatalSessionFailure(ex)){throw;}
        catch(Exception ex)
        {
            log($"PHASE5 KEYBOARD_CLOSE_FAILED: {ex.GetType().Name}: {ex.Message}");
        }
    }

    static async Task DelayWithinDeadlineAsync(
        Stopwatch stopwatch,TimeSpan limit,int milliseconds,CancellationToken ct)
    {
        TimeSpan remaining=limit-stopwatch.Elapsed;
        if(remaining<=TimeSpan.Zero)return;
        TimeSpan requested=TimeSpan.FromMilliseconds(milliseconds);
        await Task.Delay(requested<remaining?requested:remaining,ct);
    }
}
