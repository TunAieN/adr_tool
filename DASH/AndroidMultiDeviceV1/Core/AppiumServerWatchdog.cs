using System.Diagnostics;
using System.Net.Sockets;

namespace AndroidMultiDeviceV1.Core;

/// <summary>
/// Process-wide watchdog for the shared local Appium server. Restarts are
/// serialized so several device workers cannot spawn competing servers.
/// Only a process previously spawned by this class is killed; unrelated
/// node.exe processes are never terminated.
/// </summary>
public static class AppiumServerWatchdog
{
    static readonly SemaphoreSlim RestartGate=new(1,1);
    static Process? _spawnedProcess;

    public static async Task<bool> EnsureRunningAsync(
        string appiumUrl,Action<string> log,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(log);
        if(!Uri.TryCreate(appiumUrl,UriKind.Absolute,out var uri) ||
           !IsLoopback(uri.Host))
        {
            log("APPIUM_SERVER_AUTO_RESTART_SKIPPED: URL không phải local loopback.");
            return false;
        }

        await RestartGate.WaitAsync(ct);
        try
        {
            if(await IsReadyAsync(uri,ct))
            {
                log($"APPIUM_SERVER_WATCHDOG_READY: port={uri.Port}; alreadyRunning=true.");
                return true;
            }

            log($"APPIUM_SERVER_RESTART_TRIGGERED: host={uri.Host}; port={uri.Port}; " +
                "reason=CONNECTION_REFUSED.");
            StopPreviouslySpawnedProcess(log);

            string? command=FindAppiumCommand();
            if(command is null)
            {
                log("APPIUM_SERVER_RESTART_FAILED: không tìm thấy appium.cmd/appium.exe trong PATH hoặc APPDATA\\npm.");
                return false;
            }

            _spawnedProcess=StartHidden(command,uri);
            log($"APPIUM_SERVER_PROCESS_STARTED: pid={_spawnedProcess.Id}; " +
                $"port={uri.Port}; hidden=true.");

            // Give Node/Appium its normal bootstrap window, then verify the
            // actual configured endpoint instead of assuming Process.Start was success.
            await Task.Delay(3000,ct);
            // Most machines are ready in 3-5 seconds. XCloudPhone hosts can
            // spend longer loading the UiAutomator2 driver, so keep polling
            // the real /status endpoint for a bounded 30-second window.
            var deadline=DateTimeOffset.UtcNow.AddSeconds(30);
            while(DateTimeOffset.UtcNow<deadline)
            {
                ct.ThrowIfCancellationRequested();
                if(_spawnedProcess.HasExited)
                {
                    log($"APPIUM_SERVER_RESTART_FAILED: processExited=true; code={_spawnedProcess.ExitCode}.");
                    return false;
                }
                if(await IsReadyAsync(uri,ct))
                {
                    log($"APPIUM_SERVER_READY: port={uri.Port}; statusVerified=true; " +
                        "sessionCreationAllowed=true.");
                    return true;
                }
                await Task.Delay(500,ct);
            }
            log($"APPIUM_SERVER_RESTART_FAILED: port={uri.Port}; statusTimeoutSeconds=33.");
            return false;
        }
        finally { RestartGate.Release(); }
    }

    static bool IsLoopback(string host) =>
        host.Equals("localhost",StringComparison.OrdinalIgnoreCase) ||
        host.Equals("127.0.0.1",StringComparison.OrdinalIgnoreCase) ||
        host.Equals("::1",StringComparison.OrdinalIgnoreCase);

    static async Task<bool> IsReadyAsync(Uri uri,CancellationToken ct)
    {
        try
        {
            using var tcp=new TcpClient();
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(1));
            await tcp.ConnectAsync(uri.Host,uri.Port,timeout.Token);

            string basePath=uri.AbsolutePath.TrimEnd('/');
            var statusUri=new UriBuilder(uri)
            {
                Path=(basePath.Length==0?"":"/"+basePath.Trim('/'))+"/status",
                Query=""
            }.Uri;
            using var http=new HttpClient {Timeout=TimeSpan.FromSeconds(2)};
            using var response=await http.GetAsync(statusUri,ct);
            return response.IsSuccessStatusCode;
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
        catch{return false;}
    }

    static string? FindAppiumCommand()
    {
        var candidates=new List<string>();
        string? appData=Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if(!string.IsNullOrWhiteSpace(appData))
        {
            candidates.Add(Path.Combine(appData,"npm","appium.cmd"));
            candidates.Add(Path.Combine(appData,"npm","appium.exe"));
        }
        foreach(string folder in (Environment.GetEnvironmentVariable("PATH")??"")
                     .Split(Path.PathSeparator,StringSplitOptions.RemoveEmptyEntries))
        {
            string clean=folder.Trim().Trim('"');
            if(clean.Length==0)continue;
            candidates.Add(Path.Combine(clean,"appium.cmd"));
            candidates.Add(Path.Combine(clean,"appium.exe"));
        }
        return candidates.FirstOrDefault(File.Exists);
    }

    static Process StartHidden(string command,Uri uri)
    {
        string basePath=uri.AbsolutePath.Trim('/');
        ProcessStartInfo info;
        if(command.EndsWith(".cmd",StringComparison.OrdinalIgnoreCase) ||
           command.EndsWith(".bat",StringComparison.OrdinalIgnoreCase))
        {
            // ShellExecute is required for npm's Windows .cmd shim. Passing the
            // shim as a single `/c` ArgumentList item makes cmd.exe strip the
            // wrong quote and immediately exit with code 1 on paths with spaces.
            info=new ProcessStartInfo(command)
            {
                UseShellExecute=true,WindowStyle=ProcessWindowStyle.Hidden
            };
            string arguments=$"--address {uri.Host} --port {uri.Port}";
            if(basePath.Length>0)arguments+=$" --base-path /{basePath}";
            info.Arguments=arguments;
        }
        else
        {
            info=new ProcessStartInfo(command)
            {
                UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden
            };
            info.ArgumentList.Add("--address");info.ArgumentList.Add(uri.Host);
            info.ArgumentList.Add("--port");info.ArgumentList.Add(uri.Port.ToString());
            if(basePath.Length>0)
            {info.ArgumentList.Add("--base-path");info.ArgumentList.Add("/"+basePath);}
        }
        return Process.Start(info)??throw new InvalidOperationException("Không khởi động được Appium process.");
    }

    static void StopPreviouslySpawnedProcess(Action<string> log)
    {
        Process? process=_spawnedProcess;
        _spawnedProcess=null;
        if(process is null)return;
        try
        {
            if(!process.HasExited)
            {
                process.Kill(entireProcessTree:true);
                process.WaitForExit(3000);
                log($"APPIUM_SERVER_OLD_PROCESS_KILLED: pid={process.Id}; ownedByTool=true.");
            }
        }
        catch(Exception ex)
        {log("APPIUM_SERVER_OLD_PROCESS_KILL_WARNING: "+ex.GetType().Name);}
        finally {process.Dispose();}
    }
}
