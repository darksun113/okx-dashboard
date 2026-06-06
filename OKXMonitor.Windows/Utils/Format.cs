using System.Globalization;

namespace OKXMonitor.Utils;

/// Display formatting, always InvariantCulture (handoff §10.7).
public static class Format
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Money(string? s) =>
        double.TryParse(s, NumberStyles.Any, Inv, out var v) ? v.ToString("N2", Inv) : "—";

    public static string SignedMoney(double v) => (v > 0 ? "+" : "") + v.ToString("N2", Inv);

    public static string Rate(double v) => v.ToString("0.000", Inv) + "%";

    public static string Percent(double v, bool withSign = false) =>
        (withSign && v > 0 ? "+" : "") + v.ToString("0.00", Inv) + "%";

    public static string TrimNum(string? s) =>
        double.TryParse(s, NumberStyles.Any, Inv, out var v) ? v.ToString("0.######", Inv) : "—";

    public static string Number(double v) => v.ToString("0.######", Inv);

    public static string Symbol(string? instId) => (instId ?? "—").Replace("-SWAP", "");
}
