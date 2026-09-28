using ARTR.LockSight.Drift;

namespace ARTR.LockSight.Reporting;

/// <summary>
/// Formats drift findings for humans and CI logs.
/// </summary>
public static class DriftReporter
{
    private const int MaxRows = 2_000;

    public static void WriteReport(IReadOnlyList<DriftFinding> findings, TextWriter writer, bool ciMode)
    {
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(writer);

        if (findings.Count == 0)
        {
            writer.WriteLine("ARTR.LockSight drift: no lockfile drift detected.");
            return;
        }

        writer.WriteLine($"ARTR.LockSight drift: {findings.Count} finding(s).");
        writer.WriteLine();

        if (ciMode)
        {
            WriteCiLines(findings, writer);
            return;
        }

        WriteTable(findings, writer);
    }

    private static void WriteTable(IReadOnlyList<DriftFinding> findings, TextWriter writer)
    {
        writer.WriteLine(
            Pad("Project", 40) +
            Pad("TFM", 16) +
            Pad("Package", 28) +
            Pad("Reason", 28) +
            "Detail");
        writer.WriteLine(new string('-', 140));

        int limit = Math.Min(findings.Count, MaxRows);
        for (int i = 0; i < limit; i++)
        {
            DriftFinding f = findings[i];
            writer.WriteLine(
                Pad(ShortPath(f.ProjectPath), 40) +
                Pad(f.TargetFramework, 16) +
                Pad(f.PackageId, 28) +
                Pad(f.Reason.ToString(), 28) +
                f.Detail);
        }

        if (findings.Count > MaxRows)
        {
            writer.WriteLine($"... truncated after {MaxRows} rows.");
        }
    }

    private static void WriteCiLines(IReadOnlyList<DriftFinding> findings, TextWriter writer)
    {
        int limit = Math.Min(findings.Count, MaxRows);
        for (int i = 0; i < limit; i++)
        {
            DriftFinding f = findings[i];
            writer.WriteLine(
                $"::error title=ARTR.LockSight drift ({f.Reason})::" +
                $"project={f.ProjectPath}; tfm={f.TargetFramework}; package={f.PackageId}; {f.Detail}");
        }
    }

    private static string ShortPath(string path)
    {
        string file = Path.GetFileName(path);
        string? parent = Path.GetFileName(Path.GetDirectoryName(path));
        if (string.IsNullOrEmpty(parent))
        {
            return file;
        }

        return parent + "/" + file;
    }

    private static string Pad(string value, int width)
    {
        if (value.Length >= width)
        {
            return value[..(width - 1)] + " ";
        }

        return value.PadRight(width);
    }
}
