namespace AndroidMultiDeviceV1.Core;

/// <summary>One verified account on ONE phone; changes are applied atomically to
/// the vault deadline and the worker-local cancellation token. Never owns Appium.</summary>
internal sealed class DeviceAccountClock
{
    private readonly object _gate = new();
    private readonly DeviceRecord _device;
    private readonly AccountVault _vault;
    private readonly CancellationTokenSource _deadlineSignal;
    private readonly Action _notify;
    private AccountLifecyclePolicy _policy;
    private bool _closed;
    public Guid AccountId { get; }
    public DateTimeOffset StartedUtc { get; }
    public DateTimeOffset DeadlineUtc { get { lock (_gate) return _device.LifecycleDeadlineUtc ?? StartedUtc; } }
    public AccountLifecyclePolicy Policy { get { lock(_gate) return _policy.Copy(); } }

    public DeviceAccountClock(DeviceRecord device, AccountVault vault, Guid accountId,
        DateTimeOffset startUtc, DateTimeOffset deadlineUtc, AccountLifecyclePolicy policy,
        CancellationTokenSource deadlineSignal, string handle, string mode, Action notify)
    {
        _device=device; _vault=vault; AccountId=accountId; StartedUtc=startUtc;
        _policy=policy.Copy(); _deadlineSignal=deadlineSignal; _notify=notify;
        device.LifecycleRuntimeMode=mode;
        device.LifecycleSessionStartedUtc=startUtc;
        device.LifecycleDeadlineUtc=deadlineUtc;
        device.LifecycleActiveLimitMinutes=policy.SessionLimitMinutes;
        device.LifecycleAccountHandle=handle;
        Arm(deadlineUtc);
        _notify();
    }
    private void Arm(DateTimeOffset deadline)
    {
        var remaining=deadline-DateTimeOffset.UtcNow;
        _deadlineSignal.CancelAfter(remaining>TimeSpan.Zero ? remaining : TimeSpan.Zero);
    }
    public bool ApplyNow(AccountLifecyclePolicy requested, out DateTimeOffset deadline)
    {
        lock(_gate)
        {
            var account=_vault.Snapshot(_device.Serial).Single(a=>a.Id==AccountId);
            deadline=StartedUtc.AddSeconds(
                Math.Max(0,Math.Clamp(requested.SessionLimitMinutes,1,10080)*60-account.AccumulatedRunSeconds));
            if(_closed || _deadlineSignal.IsCancellationRequested)return false;
            // Preserve the original verified start time, including after an app restart.
            _vault.UpdateActiveSessionDeadline(AccountId, deadline, requested.SessionLimitMinutes);
            _policy=requested.Copy();
            _device.LifecycleDeadlineUtc=deadline;
            _device.LifecycleActiveLimitMinutes=requested.SessionLimitMinutes;
            Arm(deadline);
        }
        _notify();
        return true;
    }
    public void Close()
    {
        lock(_gate)
        {
            _closed=true;
            _device.LifecycleRuntimeMode="CHUA_KICH_HOAT";
            _device.LifecycleSessionStartedUtc=null;
            _device.LifecycleDeadlineUtc=null;
            _device.LifecycleActiveLimitMinutes=0;
            _device.LifecycleAccountHandle="";
        }
        _notify();
    }
}
