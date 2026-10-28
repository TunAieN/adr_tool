namespace AndroidMultiDeviceV1.Core;

public sealed class AppiumInstrumentationUnavailableException(int strike,int limit)
    : InvalidOperationException(
        $"UiAutomator2 instrumentation tạm thời không phản hồi; strike={strike}/{limit}.")
{
    public int Strike { get; }=strike;
    public int Limit { get; }=limit;
}

public sealed class AppiumInstrumentationCrashedException(int strikes)
    : InvalidOperationException(
        $"APPIUM_INSTRUMENTATION_CRASH_THRESHOLD: UiAutomator2 instrumentation " +
        $"không chạy {strikes} lần liên tiếp; session phải được tạo lại.")
{
    public int Strikes { get; }=strikes;
}

public sealed class AppiumUiTreeUnavailableException(int strike,int limit,string category)
    : InvalidOperationException(
        $"UiAutomator2 không đọc được cây Accessibility; category={category}; " +
        $"strike={strike}/{limit}.")
{
    public int Strike { get; }=strike;
    public int Limit { get; }=limit;
}

public sealed class AppiumUiTreeStalledException(int strikes,string category)
    : InvalidOperationException(
        $"APPIUM_UI_TREE_STALL_THRESHOLD: AccessibilityNodeInfo/Appium 500 " +
        $"lỗi {strikes} lần liên tiếp; category={category}; cần hard reset session.")
{
    public int Strikes { get; }=strikes;
}

/// <summary>
/// Identifies failures which cannot be repaired by another UI read, tap or
/// swipe on the same Appium session.
/// </summary>
public static class AppiumFailureClassifier
{
    static readonly string[] FatalMarkers =
    [
        "invalid session id",
        "connection refused",
        "actively refused",
        "econnrefused",
        "session does not exist",
        "session is either terminated or not started"
    ];

    public static bool IsFatalSessionFailure(Exception exception)
    {
        for(Exception? current=exception;current is not null;current=current.InnerException)
        {
            if(current is AppiumInstrumentationCrashedException or AppiumUiTreeStalledException)
                return true;
            if(current is System.Net.Sockets.SocketException socket &&
               socket.SocketErrorCode==System.Net.Sockets.SocketError.ConnectionRefused)
                return true;
            string message=current.Message??"";
            if(FatalMarkers.Any(marker=>
                message.Contains(marker,StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        return false;
    }

    public static bool IsRawInstrumentationCrash(Exception exception)
    {
        for(Exception? current=exception;current is not null;current=current.InnerException)
        {
            string message=current.Message??"";
            if(message.Contains("instrumentation process is not running",StringComparison.OrdinalIgnoreCase) ||
               message.Contains("instrumentation process has crashed",StringComparison.OrdinalIgnoreCase) ||
               message.Contains("UiAutomator2 server process is not running",StringComparison.OrdinalIgnoreCase) ||
               message.Contains("instrumentation crashed",StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static bool IsRawAccessibilityTreeFailure(Exception exception)
    {
        for(Exception? current=exception;current is not null;current=current.InnerException)
        {
            string message=current.Message??"";
            if(message.Contains("waiting for the root AccessibilityNodeInfo",StringComparison.OrdinalIgnoreCase) ||
               message.Contains("root AccessibilityNodeInfo in the active window",StringComparison.OrdinalIgnoreCase) ||
               message.Contains("AccessibilityNodeInfo",StringComparison.OrdinalIgnoreCase) &&
               message.Contains("timed out",StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static bool IsRawAppiumServer500(Exception exception)
    {
        for(Exception? current=exception;current is not null;current=current.InnerException)
            if((current.Message??"").Contains("Lỗi Appium 500",StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    public static bool IsInstrumentationCrashFailure(Exception exception) =>
        exception is AppiumInstrumentationCrashedException ||
        exception.Message.Contains("APPIUM_INSTRUMENTATION_CRASH_THRESHOLD",StringComparison.Ordinal);

    public static bool IsUiAutomatorHardResetFailure(Exception exception) =>
        IsInstrumentationCrashFailure(exception) ||
        exception is AppiumUiTreeStalledException ||
        exception.Message.Contains("APPIUM_UI_TREE_STALL_THRESHOLD",StringComparison.Ordinal);

    public static bool IsConnectionRefusedFailure(Exception exception)
    {
        for(Exception? current=exception;current is not null;current=current.InnerException)
        {
            if(current is System.Net.Sockets.SocketException socket &&
               socket.SocketErrorCode==System.Net.Sockets.SocketError.ConnectionRefused)
                return true;
            string message=current.Message??"";
            if(message.Contains("actively refused",StringComparison.OrdinalIgnoreCase) ||
               message.Contains("connection refused",StringComparison.OrdinalIgnoreCase) ||
               message.Contains("No connection could be made",StringComparison.OrdinalIgnoreCase) ||
               message.Contains("econnrefused",StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
