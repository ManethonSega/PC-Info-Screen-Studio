using System.Globalization;
using Microsoft.Win32;

namespace PCInfoScreenStudio.Services;

public static class RegionalFormatService
{
    public static CultureInfo Culture => CultureInfo.CurrentCulture;

    public static bool UsesFahrenheit
    {
        get
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\International");
                    var iMeasure = key?.GetValue("iMeasure")?.ToString();
                    if (iMeasure == "1")
                        return true;
                    if (iMeasure == "0")
                        return false;
                }
            }
            catch
            {
            }

            try
            {
                return !new RegionInfo(Culture.Name).IsMetric;
            }
            catch
            {
                return false;
            }
        }
    }

    public static string TemperatureSuffix => UsesFahrenheit ? "°F" : "°C";

    public static double ConvertTemperatureFromCelsius(double celsius)
        => UsesFahrenheit ? celsius * 9d / 5d + 32d : celsius;

    public static string FormatTime(DateTime value)
        => value.ToString("T", Culture);

    public static string FormatShortTime(DateTime value)
        => value.ToString("t", Culture);

    public static string FormatDate(DateTime value)
        => value.ToString("d", Culture);

    public static string FormatDay(DateTime value)
        => value.ToString("dddd", Culture);
}
