using AndroidMultiDeviceV1.Core;

static void Check(bool ok,string name)
{
    if(!ok)throw new Exception("FAILED: "+name);
    Console.WriteLine("PASS: "+name);
}
static async Task<IAsyncDisposable> Deadline(Task<IAsyncDisposable> task,string name)
{
    var done=await Task.WhenAny(task,Task.Delay(TimeSpan.FromSeconds(3)));
    Check(ReferenceEquals(done,task),name+" awakened");
    return await task;
}

// 4->5: existing queued request starts immediately, no restart.
using(var manager=new DeviceConcurrencyManager(4))
{
    var a=new List<IAsyncDisposable>();
    for(int i=0;i<4;i++)a.Add(await manager.AcquireAsync(CancellationToken.None));
    Check(manager.Snapshot()==(4,4,0),"four active");
    var sixth=manager.AcquireAsync(CancellationToken.None);
    Check(manager.WaitingCount==1 && !sixth.IsCompleted,"queued fifth request");
    manager.UpdateLimit(5);
    a.Add(await Deadline(sixth,"4 to 5 dynamic increase"));
    Check(manager.Snapshot()==(5,5,0),"five active without restart");
    foreach(var l in a)await l.DisposeAsync();
    Check(manager.ActiveCount==0,"all leases returned");
}
// Graceful decrease 5->3. Existing leases continue until count drops below 3.
using(var manager=new DeviceConcurrencyManager(5))
{
    var a=new List<IAsyncDisposable>();
    for(int i=0;i<5;i++)a.Add(await manager.AcquireAsync(CancellationToken.None));
    manager.UpdateLimit(3);
    var waiting=manager.AcquireAsync(CancellationToken.None);
    Check(!waiting.IsCompleted && manager.ActiveCount==5,"reduction does not stop leases");
    await a[0].DisposeAsync();
    await a[1].DisposeAsync();
    Check(manager.ActiveCount==3 && !waiting.IsCompleted,"no extra leases at new limit");
    await a[2].DisposeAsync();
    var lease=await Deadline(waiting,"graceful drain grant");
    Check(manager.ActiveCount==3,"grant when below new limit");
    await lease.DisposeAsync();
    await a[3].DisposeAsync();
    await a[4].DisposeAsync();
    Check(manager.ActiveCount==0,"no leak after graceful drain");
}
// FIFO, cancellation, and double Dispose.
using(var manager=new DeviceConcurrencyManager(1))
{
    var first=await manager.AcquireAsync(CancellationToken.None);
    using var cancel=new CancellationTokenSource();
    var aborted=manager.AcquireAsync(cancel.Token);
    var next=manager.AcquireAsync(CancellationToken.None);
    Check(manager.WaitingCount==2,"two waiting");
    cancel.Cancel();
    try {await aborted;throw new Exception("Canceled waiter succeeded");}
    catch(OperationCanceledException) {Console.WriteLine("PASS: queued cancellation");}
    Check(manager.WaitingCount==1,"canceled waiter removed");
    await first.DisposeAsync();
    await first.DisposeAsync();
    var last=await Deadline(next,"next FIFO waiter");
    Check(manager.ActiveCount==1,"single grant");
    await last.DisposeAsync();
    Check(manager.ActiveCount==0,"double dispose harmless");
}
// Multiple concurrent updates/requests should never exceed the effective limit
// once the active leases are at or below that limit.
using(var manager=new DeviceConcurrencyManager(3))
{
    var leases=new List<IAsyncDisposable>();
    for(int i=0;i<3;i++)leases.Add(await manager.AcquireAsync(CancellationToken.None));
    var waiting=Enumerable.Range(0,12).Select(_=>manager.AcquireAsync(CancellationToken.None)).ToArray();
    manager.UpdateLimit(6);
    Check(manager.ActiveCount==6,"bulk increase bounded");
    manager.UpdateLimit(2);
    Check(manager.ActiveCount==6,"bulk decrease safe");
    foreach(var item in leases)await item.DisposeAsync();
    for(int i=0;i<3;i++)await (await waiting[i]).DisposeAsync();
    var gained=await Deadline(waiting[3],"FIFO drain after decrease");
    await gained.DisposeAsync();
    foreach(var task in waiting.Skip(4))await (await Deadline(task,"remaining FIFO task")).DisposeAsync();
    Check(manager.ActiveCount==0 && manager.WaitingCount==0,"no leak after concurrent resize");
}
Console.WriteLine("ALL CONCURRENCY TESTS PASSED");
