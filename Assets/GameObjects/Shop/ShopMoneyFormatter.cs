using System;
using System.Globalization;

public static class ShopMoneyFormatter
{
    public static string Format(long amount)
    {
        double magnitude = Math.Abs((double)amount);
        if (magnitude < 1000) return amount.ToString(CultureInfo.InvariantCulture);
        double divisor = magnitude >= 1000000000 ? 1000000000 : magnitude >= 1000000 ? 1000000 : 1000;
        string suffix = divisor == 1000000000 ? "G" : divisor == 1000000 ? "M" : "k";
        double precision = divisor == 1000 ? 100 : 10;
        double shortened = Math.Truncate(amount / divisor * precision) / precision;
        return shortened.ToString(divisor == 1000 ? "0.##" : "0.#", CultureInfo.InvariantCulture).Replace('.', ',') + suffix;
    }
}
