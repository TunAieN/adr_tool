using System.Collections.Concurrent;

namespace AndroidMultiDeviceV1.Core;

public enum DeviceWorkerState
{
    Idle,
    Starting,
    WaitingForSlot,
    Running,
    Stopping,
    Error,
    Disconnected
}

/// <summary>
/// Immutable, device-scoped dependencies passed to a worker.  Existing business
/// workflows continue to receive their original AppiumSession/DeviceRecord
/// parameters; this context only owns isolation, diagnostics and cancellation.
/// </summary>
public sealed class DeviceContext
{
    readonly Action<string> _log;

    public DeviceContext(Settings settings,DeviceRecord device,AdbClient adb,Action<string> log)
    {
        Settings=settings;
        Device=device;
        Adb=adb;
        _log=log;
    }

    public Settings Settings { get; }
    public DeviceRecord Device { get; }
    public AdbClient Adb { get; }
    public string Serial => Device.Serial;
    public string DiagnosticKey => string.Concat(Serial.Select(c=>char.IsLetterOrDigit(c)?c:'_'));
    public void Log(string message) => _log($"[{Serial}] [{Device.Name}] {message}");
}

/// <summary>
/// FIFO runtime-resizable concurrency limiter. No underlying semaphore has a
/// fixed maxCount, so editing the limit never invalidates existing leases.
/// Updating downward drains naturally; running sessions are not interrupted.
/// Cancellation removes a queued request without consuming a permit.
/// </summary>
public sealed class DeviceConcurrencyManager : IDisposable
{
    readonly object _gate=new();
    readonly LinkedList<Waiter> _queue=new();
    int _limit;
    int _active;
    bool _disposed;

    sealed class Waiter
    {
        public LinkedListNode<Waiter>? Node;
        public TaskCompletionSource<IAsyncDisposable> Completion { get; }=
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public DeviceConcurrencyManager(int limit) => _limit=Math.Clamp(limit,1,100);

    public int Limit { get {lock(_gate)return _limit;} }
    public int ActiveCount { get {lock(_gate)return _active;} }
    public int WaitingCount { get {lock(_gate)return _queue.Count;} }
    public (int Limit,int Active,int Waiting) Snapshot()
    {
        lock(_gate)return (_limit,_active,_queue.Count);
    }

    public (int OldLimit,int NewLimit,int Active,int Waiting) UpdateLimit(int limit)
    {
        int adjusted=Math.Clamp(limit,1,100);
        lock(_gate)
        {
            ThrowIfDisposed();
            int previous=_limit;
            _limit=adjusted;
            GrantWaitingUnderLock();
            return (previous,_limit,_active,_queue.Count);
        }
    }

    public async Task<IAsyncDisposable> AcquireAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Waiter waiter;
        lock(_gate)
        {
            ThrowIfDisposed();
            if(_queue.Count==0 && _active<_limit)
            {
                _active++;
                return new Lease(this);
            }
            waiter=new Waiter();
            waiter.Node=_queue.AddLast(waiter);
        }
        // Registration is outside the lock: a canceled token can invoke the
        // callback synchronously. On completion it is disposed automatically.
        using var registration=ct.Register(()=>CancelWaiter(waiter,ct));
        return await waiter.Completion.Task.ConfigureAwait(false);
    }

    void CancelWaiter(Waiter waiter,CancellationToken ct)
    {
        lock(_gate)
        {
            if(waiter.Node is null)return; // already granted/disposed
            _queue.Remove(waiter.Node);
            waiter.Node=null;
            waiter.Completion.TrySetCanceled(ct);
        }
    }

    void GrantWaitingUnderLock()
    {
        while(_active<_limit && _queue.First is not null)
        {
            var node=_queue.First;
            var waiter=node!.Value;
            _queue.RemoveFirst();
            waiter.Node=null;
            _active++;
            waiter.Completion.TrySetResult(new Lease(this));
        }
    }

    void Release()
    {
        lock(_gate)
        {
            if(_active<=0)throw new InvalidOperationException("Concurrency lease released twice.");
            _active--;
            if(!_disposed)GrantWaitingUnderLock();
        }
    }

    void ThrowIfDisposed()
    {
        if(_disposed)throw new ObjectDisposedException(nameof(DeviceConcurrencyManager));
    }

    public void Dispose()
    {
        lock(_gate)
        {
            if(_disposed)return;
            _disposed=true;
            foreach(var waiter in _queue)
            {
                waiter.Node=null;
                waiter.Completion.TrySetException(new ObjectDisposedException(nameof(DeviceConcurrencyManager)));
            }
            _queue.Clear();
        }
    }

    sealed class Lease(DeviceConcurrencyManager owner) : IAsyncDisposable
    {
        DeviceConcurrencyManager? _owner=owner;
        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _owner,null)?.Release();
            return ValueTask.CompletedTask;
        }
    }
}

public sealed class AppiumSessionFactory
{
    readonly ConcurrentDictionary<int,string> _ports=new();
    const int FirstRecoveryPort=8300;
    const int LastRecoveryPort=8999;
    const int MaxBusyPortRetries=5;

    static bool IsBusySystemPort(Exception ex)
    {
        for(Exception? current=ex;current is not null;current=current.InnerException)
        {
            string message=current.Message;
            if(message.Contains("systemPort",StringComparison.OrdinalIgnoreCase) &&
               (message.Contains("busy",StringComparison.OrdinalIgnoreCase) ||
                message.Contains("occupied",StringComparison.OrdinalIgnoreCase) ||
                message.Contains("already in use",StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        return false;
    }

    static bool IsTcpPortAvailable(int port)
    {
        System.Net.Sockets.TcpListener? probe=null;
        try
        {
            probe=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,port);
            probe.Start();
            return true;
        }
        catch(System.Net.Sockets.SocketException){return false;}
        finally {probe?.Stop();}
    }

    int ReserveAlternativePort(DeviceContext context,HashSet<int> attempted)
    {
        for(int port=FirstRecoveryPort;port<=LastRecoveryPort;port++)
        {
            if(attempted.Contains(port) ||
               context.Settings.Devices.Any(d=>d.DeviceId!=context.Device.DeviceId && d.SystemPort==port) ||
               _ports.ContainsKey(port) || !IsTcpPortAvailable(port))continue;
            if(_ports.TryAdd(port,context.Serial))return port;
        }
        throw new InvalidOperationException(
            "Không tìm được systemPort trống trong dải 8300-8999; không dừng Appium của máy khác.");
    }

    public async Task<AppiumSessionLease> CreateAsync(DeviceContext context,CancellationToken ct)
    {
        int originalPort=context.Device.SystemPort;
        if(originalPort is < 1024 or > 65535)
            throw new InvalidOperationException($"systemPort {originalPort} không hợp lệ cho {context.Serial}.");
        var attempted=new HashSet<int>();
        for(int attempt=0;attempt<=MaxBusyPortRetries;attempt++)
        {
            ct.ThrowIfCancellationRequested();
            int port;
            if(attempt==0 && !_ports.ContainsKey(originalPort) && IsTcpPortAvailable(originalPort) &&
                _ports.TryAdd(originalPort,context.Serial))
                port=originalPort;
            else
                port=ReserveAlternativePort(context,attempted);
            attempted.Add(port);
            var session=new AppiumSession(context.Settings.AppiumUrl);
            try
            {
                context.Device.AppiumStatus="CONNECTING";
                await session.CreateAsync(context.Device,ct,port);
                context.Device.SystemPort=port;
                if(port!=originalPort)
                {
                    // The new port is assigned ONLY after Appium accepts it.
                    Storage.SaveSettings(context.Settings);
                    context.Log($"SYSTEM_PORT_AUTO_REALLOCATED: old={originalPort}; new={port}; " +
                        $"attempt={attempt+1}; deviceId={context.Device.DeviceId}; settingsSaved=true.");
                }
                context.Device.AppiumStatus="CONNECTED";
                context.Log($"APPIUM_SESSION_CREATED: systemPort={port}");
                return new AppiumSessionLease(session,port,_ports,context);
            }
            catch(Exception ex)
            {
                // If persistence failed after a successful Appium handshake,
                // revert the in-memory assignment before exposing the error.
                context.Device.SystemPort=originalPort;
                context.Device.AppiumStatus="ERROR";
                _ports.TryRemove(new KeyValuePair<int,string>(port,context.Serial));
                await session.DisposeAsync();
                if(IsBusySystemPort(ex) && attempt<MaxBusyPortRetries)
                {
                    context.Log($"APPIUM_SYSTEM_PORT_BUSY_RECOVERY: port={port}; " +
                        $"attempt={attempt+1}/{MaxBusyPortRetries+1}; selectNewPort=true; otherWorkersUntouched=true.");
                    continue;
                }
                throw;
            }
        }
        throw new InvalidOperationException("Không tạo được phiên Appium sau khi thử cổng dự phòng.");
    }

    public static bool IsFatalSessionException(Exception ex)
    {
        if(AppiumFailureClassifier.IsFatalSessionFailure(ex))return true;
        for(Exception? current=ex;current is not null;current=current.InnerException)
        {
            string message=current.Message;
            if(message.Contains("invalid session id",StringComparison.OrdinalIgnoreCase) ||
               message.Contains("connection refused",StringComparison.OrdinalIgnoreCase) ||
               message.Contains("actively refused",StringComparison.OrdinalIgnoreCase) ||
               message.Contains("No connection could be made",StringComparison.OrdinalIgnoreCase) ||
               message.Contains("ADB chưa kết nối",StringComparison.OrdinalIgnoreCase) ||
               message.Contains("device offline",StringComparison.OrdinalIgnoreCase) ||
               message.Contains("device not found",StringComparison.OrdinalIgnoreCase) ||
               message.Contains("systemPort",StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}

public sealed class AppiumSessionLease : IAsyncDisposable
{
    readonly int _port;
    readonly ConcurrentDictionary<int,string> _ports;
    readonly DeviceContext _context;
    int _disposed;

    internal AppiumSessionLease(AppiumSession session,int port,
        ConcurrentDictionary<int,string> ports,DeviceContext context)
    {
        Session=session;_port=port;_ports=ports;_context=context;
    }

    public AppiumSession Session { get; }

    public async ValueTask DisposeAsync()
    {
        if(Interlocked.Exchange(ref _disposed,1)!=0)return;
        await Session.DisposeAsync();
        _ports.TryRemove(new KeyValuePair<int,string>(_port,_context.Serial));
        _context.Device.AppiumStatus="DISCONNECTED";
        _context.Log($"APPIUM_SESSION_RELEASED: systemPort={_port}");
    }
}

public sealed class DeviceWorker : IDisposable
{
    readonly CancellationTokenSource _cts=new();
    int _state=(int)DeviceWorkerState.Idle;

    public DeviceWorker(DeviceContext context)
    {
        Context=context;
        CreatedAt=DateTimeOffset.Now;
    }

    public DeviceContext Context { get; }
    public CancellationToken Token => _cts.Token;
    public CancellationTokenSource Cancel => _cts;
    public Task Work { get; private set; }=Task.CompletedTask;
    public DateTimeOffset CreatedAt { get; }
    public DeviceWorkerState State => (DeviceWorkerState)Volatile.Read(ref _state);
    public TimeSpan Uptime => DateTimeOffset.Now-CreatedAt;

    public void Attach(Task work)
    {
        Work=work;
        if(State==DeviceWorkerState.Idle)SetState(DeviceWorkerState.Starting);
    }

    public void SetState(DeviceWorkerState state)
    {
        // Stop may race with a permit being granted. Never expose Running
        // again after cancellation has been requested.
        if(_cts.IsCancellationRequested && state is not (DeviceWorkerState.Stopping or DeviceWorkerState.Error))
            return;
        Volatile.Write(ref _state,(int)state);
        Context.Device.WorkerState=state.ToString();
    }

    public void Stop()
    {
        SetState(DeviceWorkerState.Stopping);
        _cts.Cancel();
    }

    public void Dispose()=>_cts.Dispose();
}

public sealed class DeviceWorkerManager
{
    readonly ConcurrentDictionary<string,DeviceWorker> _workers=new(StringComparer.OrdinalIgnoreCase);

    public int RunningCount => _workers.Count;
    public bool Contains(string serial)=>_workers.ContainsKey(serial);
    public bool TryAdd(DeviceWorker worker)=>_workers.TryAdd(worker.Context.Serial,worker);
    public bool TryGet(string serial,out DeviceWorker? worker)=>_workers.TryGetValue(serial,out worker);
    public bool TryRemove(string serial,out DeviceWorker? worker)=>_workers.TryRemove(serial,out worker);
    public IReadOnlyList<DeviceWorker> Snapshot()=>_workers.Values.ToArray();

    public bool Stop(string serial)
    {
        if(!_workers.TryGetValue(serial,out var worker))return false;
        worker.Stop();
        return true;
    }

    public void StopAll()
    {
        foreach(var worker in _workers.Values)worker.Stop();
    }
}
