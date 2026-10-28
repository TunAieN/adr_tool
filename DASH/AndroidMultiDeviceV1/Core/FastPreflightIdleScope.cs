namespace AndroidMultiDeviceV1.Core;

/// <summary>
/// Session-local performance mode for the account TEST/preflight journey.
/// UiAutomator2 may wait for the infinite TikTok feed to become idle on every
/// XPath/query, multiplying a 10-second delay across many sequential calls.
/// Keep all login/handle verification requirements unchanged. Restore the
/// ORIGINAL Appium setting before handing control to the normal LIVE engine.
/// </summary>
public sealed class FastPreflightIdleScope : IAsyncDisposable
{
    private readonly AppiumSession _session;
    private readonly Action<string> _log;
    private long _original;
    private bool _disposed;
    private bool _mayHaveChanged;

    private FastPreflightIdleScope(AppiumSession session, Action<string> log)
    { _session=session; _log=log; }

    public static async Task<FastPreflightIdleScope> EnableAsync(
        AppiumSession session, Action<string> log, CancellationToken ct)
    {
        var scope=new FastPreflightIdleScope(session,log);
        try
        {
            scope._original=await session.GetIdleTimeoutAsync(ct);
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested) { throw; }
        catch(Exception ex)
        {
            log($"FAST_PREFLIGHT_IDLE_SKIP: settings unavailable ({ex.GetType().Name}); original behavior retained.");
            return scope;
        }
        if(scope._original==1000)
        {
            log("FAST_PREFLIGHT_IDLE_ALREADY_ACTIVE: currentMs=1000; no mutation.");
            return scope;
        }
        if(scope._original!=10000)
        {
            log($"FAST_PREFLIGHT_IDLE_SKIP: currentMs={scope._original}; nondefault setting preserved.");
            return scope;
        }
        try
        {
            scope._mayHaveChanged=true; // A cancelled command may still have reached Appium.
            await session.SetIdleTimeoutAsync(1000,ct);
            long confirmed=await session.GetIdleTimeoutAsync(ct);
            if(confirmed!=1000)
                throw new InvalidOperationException("UiAutomator2 did not confirm the 1000ms setting.");
            log("FAST_PREFLIGHT_IDLE_APPLIED: previousMs=10000; currentMs=1000; scope=ACCOUNT_PREFLIGHT; " +
                "restoreBeforeLive=true; identityVerification=unchanged.");
            return scope;
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested)
        {
            await scope.RestoreAsync();
            throw;
        }
        catch(Exception ex)
        {
            log($"FAST_PREFLIGHT_IDLE_FAILED: {ex.GetType().Name}; attempting restore.");
            await scope.RestoreAsync();
            return scope;
        }
    }

    public async Task RestoreAsync()
    {
        if(!_mayHaveChanged)return;
        // Never use the already-cancelled worker token when undoing an Appium
        // setting: Stop still needs to restore the setting before releasing it.
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await _session.SetIdleTimeoutAsync(_original,timeout.Token);
            var actual=await _session.GetIdleTimeoutAsync(timeout.Token);
            if(actual!=_original)
                throw new InvalidOperationException("Appium idle setting was not restored.");
            _mayHaveChanged=false;
            _log($"FAST_PREFLIGHT_IDLE_RESTORED: currentMs={actual}; verified=true.");
        }
        catch(Exception ex)
        {
            _log($"FAST_PREFLIGHT_IDLE_RESTORE_FAILED: {ex.GetType().Name}; " +
                "must dispose this Appium session before reuse.");
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if(_disposed)return;
        _disposed=true;
        if(_mayHaveChanged)
        {
            try { await RestoreAsync(); }
            catch(Exception ex)
            {
                // The caller is already unwinding a session that will be disposed;
                // do not replace an important login/cancellation exception.
                _log($"FAST_PREFLIGHT_IDLE_TEARDOWN_WARNING: {ex.GetType().Name}; session disposal required.");
            }
        }
    }
}
