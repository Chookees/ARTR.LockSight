using System.Text.Json;
using ARTR.LockSight.Cli;
using ARTR.LockSight.Drift;

namespace ARTR.LockSight.Reporting;

/// <summary>
/// Formats drift findings for humans and CI logs.
/// </summary>
public static class DriftReporter
{
    private const int MaxRows = 2_000;

    public static void WriteReport(
        IReadOnlyList<DriftFinding> findings,
        TextWriter writer,
        bool ciMode,
        OutputFormat format = OutputFormat.Text,
        TextWriter? annotationWriter = null)
    {
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(writer);

        if (format == OutputFormat.Json)
        {
            JsonReportWriter.WriteDrift(findings, writer);
            if (ciMode && annotationWriter is not null)
            {
                WriteCiLines(findings, annotationWriter);
            }

            return;
        }

        WriteText(findings, writer, ciMode);
    }

    public static int CountBlocking(IReadOnlyList<DriftFinding> findings, bool strict)
    {
        ArgumentNullException.ThrowIfNull(findings);
        int count = 0;
        int limit = Math.Min(findings.Count, MaxRows);
        for (int i = 0; i < limit; i++)
        {
            if (strict || !findings[i].IsWarning)
            {
                count++;
            }
        }

        return count;
    }

    private static void WriteText(IReadOnlyList<DriftFinding> findings, TextWriter writer, bool ciMode)
    {
        if (findings.Count == 0)
        {
            writer.WriteLine("ARTR.LockSight drift: no lockfile drift detected.");
            return;
        }

        int warnings = findings.Count - CountBlocking(findings, strict: false);
        int errors = CountBlocking(findings, strict: false);
        writer.WriteLine($"ARTR.LockSight drift: {findings.Count} finding(s) ({errors} error(s), {warnings} warning(s)).");
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
            Pad("Project", 40) + Pad("TFM", 16) + Pad("Package", 28) + Pad("Reason", 28) + "Detail");
        writer.WriteLine(new string('-', 140));
        int limit = Math.Min(findings.Count, MaxRows);
        for (int i = 0; i < limit; i++)
        {
            DriftFinding finding = findings[i];
            string reason = finding.IsWarning ? "WARN " + finding.Reason : finding.Reason.ToString();
            writer.WriteLine(
                Pad(ShortPath(finding.ProjectPath), 40) +
                Pad(finding.TargetFramework, 16) +
                Pad(finding.PackageId, 28) +
                Pad(reason, 28) +
                finding.Detail);
        }
    }

    private static void WriteCiLines(IReadOnlyList<DriftFinding> findings, TextWriter writer)
    {
        int limit = Math.Min(findings.Count, MaxRows);
        for (int i = 0; i < limit; i++)
        {
            DriftFinding finding = findings[i];
            string level = finding.IsWarning ? "warning" : "error";
            string detail = finding.Detail.Replace('\r', ' ').Replace('\n', ' ');
            writer.WriteLine(
                $"::{level} title=ARTR.LockSight drift ({finding.Reason})::" +
                $"project={finding.ProjectPath}; tfm={finding.TargetFramework}; package={finding.PackageId}; {detail}");
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

/// <summary>
/// Formats lockfile diff rows.
/// </summary>
public static class DiffReporter
{
    private const int MaxRows = 2_000;

    public static void WriteReport(
        IReadOnlyList<LockDiffEntry> changes,
        TextWriter writer,
        bool ciMode,
        OutputFormat format = OutputFormat.Text,
        TextWriter? annotationWriter = null)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(writer);
        if (format == OutputFormat.Json)
        {
            JsonReportWriter.WriteDiff(changes, writer);
            if (ciMode && annotationWriter is not null)
            {
                WriteCiLines(changes, annotationWriter);
            }

            return;
        }

        if (changes.Count == 0)
        {
            writer.WriteLine("ARTR.LockSight diff: lockfiles match.");
            return;
        }

        writer.WriteLine($"ARTR.LockSight diff: {changes.Count} change(s).");
        writer.WriteLine();
        if (ciMode)
        {
            WriteCiLines(changes, writer);
            return;
        }

        int limit = Math.Min(changes.Count, MaxRows);
        for (int i = 0; i < limit; i++)
        {
            LockDiffEntry change = changes[i];
            writer.WriteLine($"{change.Change,-8} {change.Lockfile}  {change.TargetFramework}  {change.PackageId}  {change.Detail}");
        }
    }

    private static void WriteCiLines(IReadOnlyList<LockDiffEntry> changes, TextWriter writer)
    {
        int limit = Math.Min(changes.Count, MaxRows);
        for (int i = 0; i < limit; i++)
        {
            LockDiffEntry change = changes[i];
            writer.WriteLine(
                $"::error title=ARTR.LockSight diff ({change.Change})::" +
                $"lockfile={change.Lockfile}; tfm={change.TargetFramework}; package={change.PackageId}; {change.Detail}");
        }
    }
}

internal static class JsonReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static void WriteDrift(IReadOnlyList<DriftFinding> findings, TextWriter writer)
    {
        int errors = DriftReporter.CountBlocking(findings, strict: false);
        var items = new List<FindingJson>(findings.Count);
        int limit = Math.Min(findings.Count, 2_000);
        for (int i = 0; i < limit; i++)
        {
            DriftFinding finding = findings[i];
            items.Add(new FindingJson
            {
                Project = finding.ProjectPath,
                Tfm = finding.TargetFramework,
                Package = finding.PackageId,
                Reason = finding.Reason.ToString(),
                Severity = finding.IsWarning ? "warning" : "error",
                Detail = finding.Detail,
            });
        }

        var report = new DriftJsonReport
        {
            Version = ToolVersion.Current,
            FindingCount = findings.Count,
            ErrorCount = errors,
            WarningCount = findings.Count - errors,
            Findings = items,
        };
        writer.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
    }

    public static void WriteDiff(IReadOnlyList<LockDiffEntry> changes, TextWriter writer)
    {
        var items = new List<DiffJson>(changes.Count);
        int limit = Math.Min(changes.Count, 2_000);
        for (int i = 0; i < limit; i++)
        {
            LockDiffEntry change = changes[i];
            items.Add(new DiffJson
            {
                Lockfile = change.Lockfile,
                Tfm = change.TargetFramework,
                Package = change.PackageId,
                Change = change.Change.ToString(),
                Detail = change.Detail,
            });
        }

        var report = new DiffJsonReport
        {
            Version = ToolVersion.Current,
            ChangeCount = changes.Count,
            Changes = items,
        };
        writer.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
    }

    private sealed class DriftJsonReport
    {
        public string Tool { get; init; } = "ARTR.LockSight";
        public required string Version { get; init; }
        public int FindingCount { get; init; }
        public int ErrorCount { get; init; }
        public int WarningCount { get; init; }
        public required List<FindingJson> Findings { get; init; }
    }

    private sealed class FindingJson
    {
        public required string Project { get; init; }
        public required string Tfm { get; init; }
        public required string Package { get; init; }
        public required string Reason { get; init; }
        public required string Severity { get; init; }
        public required string Detail { get; init; }
    }

    private sealed class DiffJsonReport
    {
        public string Tool { get; init; } = "ARTR.LockSight";
        public required string Version { get; init; }
        public int ChangeCount { get; init; }
        public required List<DiffJson> Changes { get; init; }
    }

    private sealed class DiffJson
    {
        public required string Lockfile { get; init; }
        public required string Tfm { get; init; }
        public required string Package { get; init; }
        public required string Change { get; init; }
        public required string Detail { get; init; }
    }
}
