using System;
using System.Collections.Generic;
using System.IO;

// Checks that ShopMoneyFormatter truncates without floating point undershoot (2300 must show 2,3k, not 2,29k).
public static class BughuntShopFormatChecks
{
    static readonly List<string> checks = new();
    static void Expect(long amount, string expected)
    {
        string actual = ShopMoneyFormatter.Format(amount);
        if (actual != expected) throw new Exception(amount + " ergibt \"" + actual + "\" statt \"" + expected + "\"");
        checks.Add("PASS " + amount + " -> " + expected);
    }

    public static object Main()
    {
        checks.Clear();
        try
        {
            // Plain amounts below 1000 stay unchanged.
            Expect(0, "0"); Expect(999, "999"); Expect(-999, "-999");
            // Round amounts that double arithmetic used to undershoot by 0,01k.
            Expect(1130, "1,13k"); Expect(1150, "1,15k"); Expect(2010, "2,01k"); Expect(2300, "2,3k");
            Expect(4100, "4,1k"); Expect(8200, "8,2k"); Expect(9200, "9,2k"); Expect(-2300, "-2,3k");
            // Truncation (not rounding) is intended and stays.
            Expect(1000, "1k"); Expect(1009, "1k"); Expect(2299, "2,29k"); Expect(999999, "999,99k"); Expect(-1159, "-1,15k");
            // Larger ranges keep one decimal and still truncate.
            Expect(1000000, "1M"); Expect(2300000, "2,3M"); Expect(2399999, "2,3M"); Expect(-2300000, "-2,3M");
            Expect(999999999, "999,9M"); Expect(1000000000, "1G"); Expect(2300000000, "2,3G"); Expect(long.MinValue, "-9223372036,8G");

            // Every multiple of 10 in the k range must match the exact integer truncation.
            int mismatches = 0; string first = null;
            for (long amount = 1000; amount < 1000000; amount += 10)
            {
                long units = amount / 10;
                string expected = (units / 100).ToString(System.Globalization.CultureInfo.InvariantCulture);
                long cents = units % 100;
                if (cents != 0) expected += "," + (cents % 10 == 0 ? (cents / 10).ToString() : cents.ToString("00"));
                expected += "k";
                if (ShopMoneyFormatter.Format(amount) != expected) { mismatches++; first ??= amount + " -> " + ShopMoneyFormatter.Format(amount); }
            }
            if (mismatches > 0) throw new Exception(mismatches + " Zehnerbeträge im k-Bereich falsch, z. B. " + first);
            checks.Add("PASS alle Zehnerbeträge 1'000-999'990 exakt abgeschnitten");

            string report = "PASS (" + checks.Count + ")\n" + string.Join("\n", checks);
            File.WriteAllText("Temp/BughuntShopFormatChecks.txt", report); return report.Split('\n')[0];
        }
        catch (Exception ex) { File.WriteAllText("Temp/BughuntShopFormatChecks.txt", "FAIL " + ex.Message + "\n" + string.Join("\n", checks)); throw; }
    }
}
