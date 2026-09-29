using ARTR.LockSight.Drift;

namespace ARTR.LockSight.Evaluate;

/// <summary>
/// Turns NU1004 diagnostics plus the static scan into one failure list.
/// Static rows explain the project NuGet named. NuGet remains the pass/fail signal.
/// </summary>
internal static class EvaluateReporter
{
    private const int MaxRows = 2_000;

    public static IReadOnlyList<DriftFinding> BuildFailureFindings(
        IReadOnlyList<RestoreDiagnostic> diagnostics,
        IReadOnlyList<DriftFinding> staticFindings)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(staticFindings);

        var rows = new List<DriftFinding>(capacity: 8);
        int diagLimit = Math.Min(diagnostics.Count, MaxRows);
        for (int i = 0; i < diagLimit; i++)
        {
            RestoreDiagnostic diagnostic = diagnostics[i];
            string project = diagnostic.ProjectPath.Length == 0 ? "(restore)" : diagnostic.ProjectPath;
            rows.Add(new DriftFinding(project, "-", "(restore)", DriftReason.LockedRestoreFailed, diagnostic.Message));
            AppendMatches(diagnostic.ProjectPath, staticFindings, rows);
        }

        if (diagnostics.Count == 0)
        {
            AppendAll(staticFindings, rows);
        }

        return rows;
    }

    private static void AppendMatches(string diagnosticPath, IReadOnlyList<DriftFinding> staticFindings, List<DriftFinding> rows)
    {
        if (diagnosticPath.Length == 0)
        {
            return;
        }

        int limit = Math.Min(staticFindings.Count, MaxRows);
        for (int i = 0; i < limit; i++)
        {
            if (rows.Count >= MaxRows)
            {
                return;
            }

            if (SameProject(diagnosticPath, staticFindings[i].ProjectPath))
            {
                rows.Add(staticFindings[i]);
            }
        }
    }

    private static void AppendAll(IReadOnlyList<DriftFinding> staticFindings, List<DriftFinding> rows)
    {
        int limit = Math.Min(staticFindings.Count, MaxRows);
        for (int i = 0; i < limit; i++)
        {
            if (rows.Count >= MaxRows)
            {
                return;
            }

            rows.Add(staticFindings[i]);
        }
    }

    private static bool SameProject(string diagnosticPath, string findingPath)
    {
        if (string.Equals(diagnosticPath, findingPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string normalized = diagnosticPath.Replace('\\', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized))
        {
            normalized = Path.GetFullPath(normalized);
        }

        return string.Equals(normalized, findingPath, StringComparison.OrdinalIgnoreCase)
            || findingPath.EndsWith(normalized, StringComparison.OrdinalIgnoreCase);
    }
}
