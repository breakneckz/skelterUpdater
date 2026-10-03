using System.Globalization;

namespace SkelterLauncher;

internal static class Format
{
    public static string Bytes(long value)
    {
        var units = Loc.ByteUnits;
        double size = value;
        var unit = 0;

        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        var digits = unit >= 2 && size < 100 ? 1 : 0;
        return string.Create(CultureInfo.InvariantCulture, $"{Math.Round(size, digits)} {units[unit]}");
    }

    public static string Speed(double bytesPerSecond) => Loc.PerSecond(Bytes((long)bytesPerSecond));

    public static string Eta(long remaining, double bytesPerSecond)
    {
        if (bytesPerSecond <= 1)
            return "";

        var seconds = remaining / bytesPerSecond;
        if (seconds < 1 || double.IsInfinity(seconds))
            return "";

        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1
            ? Loc.Hours((int)span.TotalHours, span.Minutes)
            : span.TotalMinutes >= 1
                ? Loc.Minutes((int)span.TotalMinutes)
                : Loc.Seconds(span.Seconds);
    }
}
