using System.Globalization;

namespace AndroidMultiDeviceV1.Core;

// V5.4: presentation-only helpers. The persisted lifecycle policy and the
// worker deadlines deliberately remain in whole minutes for compatibility.
public static class AccountDurationDisplay
{
    public const int MaxSessionMinutes = 10080;
    public const decimal MinInputHours = 0.02m; // display for a one-minute legacy limit
    public const decimal MaxInputHours = 168m;

    // Two fractional hour digits preserve EVERY integer minute when the
    // value is converted back with nearest-minute rounding (tested 1..10080).
    public static decimal MinutesToInputHours(int minutes) =>
        Math.Round(Math.Clamp(minutes, 1, MaxSessionMinutes) / 60m,
            2, MidpointRounding.AwayFromZero);

    public static int InputHoursToMinutes(decimal hours) =>
        Math.Clamp((int)Math.Round(hours * 60m, 0,
            MidpointRounding.AwayFromZero), 1, MaxSessionMinutes);

    public static string FormatHours(int minutes)
    {
        int limited = Math.Clamp(minutes, 1, MaxSessionMinutes);
        decimal hours = limited / 60m;
        return hours.ToString(limited % 60 == 0 ? "0" : "0.##",
            CultureInfo.CurrentCulture) + " giờ";
    }

    // Total hours intentionally do NOT wrap after 24 hours.
    public static string FormatElapsed(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0 || seconds > long.MaxValue / 2d)
            return "Chưa xác định";
        long total = (long)Math.Floor(seconds);
        return $"{total / 3600:00}:{total % 3600 / 60:00}:{total % 60:00}";
    }
}
