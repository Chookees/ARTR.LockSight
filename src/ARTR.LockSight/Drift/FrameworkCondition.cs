namespace ARTR.LockSight.Drift;

/// <summary>
/// Best-effort filter for PackageReference conditions. Full MSBuild evaluation is intentionally out of scope.
/// A condition that mentions TargetFramework and quotes a TFM applies only to those TFMs.
/// Every other condition is treated as true so we do not hide real drift.
/// </summary>
internal static class FrameworkCondition
{
    private const int MaxQuotes = 16;

    public static bool AppliesTo(string condition, string tfm)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(tfm);

        if (condition.Length == 0)
        {
            return true;
        }

        if (!condition.Contains("TargetFramework", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var quotes = new List<string>(capacity: 4);
        CollectQuoted(condition, '\'', quotes);
        CollectQuoted(condition, '"', quotes);

        bool sawTfm = false;
        bool matched = false;
        int limit = Math.Min(quotes.Count, MaxQuotes);
        for (int i = 0; i < limit; i++)
        {
            if (!quotes[i].StartsWith("net", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            sawTfm = true;
            if (string.Equals(quotes[i], tfm, StringComparison.OrdinalIgnoreCase))
            {
                matched = true;
            }
        }

        return !sawTfm || matched;
    }

    private static void CollectQuoted(string text, char delimiter, List<string> quotes)
    {
        int search = 0;
        while (quotes.Count < MaxQuotes)
        {
            int start = text.IndexOf(delimiter, search);
            if (start < 0)
            {
                return;
            }

            int end = text.IndexOf(delimiter, start + 1);
            if (end < 0)
            {
                return;
            }

            quotes.Add(text.Substring(start + 1, end - start - 1));
            search = end + 1;
        }
    }
}
