using System;
using System.Globalization;

public static class ShopMoneyFormatter
{
    public static string Format(long amount)
    {
        double magnitude = Math.Abs((double)amount);
        if (magnitude < 100000) return amount.ToString(CultureInfo.InvariantCulture);
        double divisor = magnitude >= 1000000000 ? 1000000000 : magnitude >= 1000000 ? 1000000 : 1000;
        string suffix = divisor == 1000000000 ? "G" : divisor == 1000000 ? "M" : "k";
        double shortened = Math.Truncate(amount / divisor * 10) / 10;
        return shortened.ToString("0.#", CultureInfo.InvariantCulture).Replace('.', ',') + suffix;
    }
}
