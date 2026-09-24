using System.Globalization;

namespace SkelterLauncher;

internal static class Format
{
    private static readonly string[] Units = { "Б", "КБ", "МБ", "ГБ", "ТБ" };

    public static string Bytes(long value)
    {
        double size = value;
        var unit = 0;

        while (size >= 1024 && unit < Units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        var digits = unit >= 2 && size < 100 ? 1 : 0;
        return string.Create(CultureInfo.InvariantCulture, $"{Math.Round(size, digits)} {Units[unit]}");
    }

    public static string Speed(double bytesPerSecond) => $"{Bytes((long)bytesPerSecond)}/с";

    public static string Eta(long remaining, double bytesPerSecond)
    {
        if (bytesPerSecond <= 1)
            return "";

        var seconds = remaining / bytesPerSecond;
        if (seconds < 1 || double.IsInfinity(seconds))
            return "";

        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours} ч {span.Minutes} мин"
            : span.TotalMinutes >= 1
                ? $"{(int)span.TotalMinutes} мин"
                : $"{span.Seconds} с";
    }
}
