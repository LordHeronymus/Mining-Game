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
        // Truncate in integers: double division leaves e.g. 2300 at 229.999... and would show 2,29k.
        long step = divisor == 1000000000 ? 100000000 : divisor == 1000000 ? 100000 : 10;
        double shortened = amount / step / precision;
        return shortened.ToString(divisor == 1000 ? "0.##" : "0.#", CultureInfo.InvariantCulture).Replace('.', ',') + suffix;
    }
}
