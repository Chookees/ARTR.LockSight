using System.Xml;

namespace ARTR.LockSight.Drift;

/// <summary>
/// Scans a directory, project, or solution for lockfile drift.
/// </summary>
public static class DriftDetector
{
    /// <summary>
    /// Returns every finding for <paramref name="rootPath"/>. The list is empty when nothing is wrong.
    /// </summary>
    public static IReadOnlyList<DriftFinding> Scan(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        ProjectSet discovered = WorkspaceScanner.Discover(rootPath);
        var findings = new List<DriftFinding>(capacity: 32);
        ReportMissingProjects(discovered, findings);

        var snapshots = new Dictionary<string, ProjectSnapshot>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>();
        EnqueueAll(discovered.Projects, pending);

        while (pending.Count > 0 && seen.Count < SolutionGraph.MaxProjects && findings.Count < DriftRules.MaxFindings)
        {
            string path = pending.Dequeue();
            if (!seen.Add(path))
            {
                continue;
            }

            ProjectSnapshot? snapshot = TryRead(path, findings);
            if (snapshot is null)
            {
                continue;
            }

            snapshots[snapshot.ProjectPath] = snapshot;
            DriftRules.AnalyzeProject(snapshot, findings);
            EnqueueAll(snapshot.ProjectReferences, pending);
        }

        foreach (ProjectSnapshot snapshot in snapshots.Values)
        {
            if (findings.Count >= DriftRules.MaxFindings)
            {
                break;
            }

            DriftRules.AnalyzeProjectReferences(snapshot, snapshots, findings);
        }

        return findings;
    }

    private static void ReportMissingProjects(ProjectSet discovered, List<DriftFinding> findings)
    {
        int limit = Math.Min(discovered.MissingProjects.Count, 200);
        for (int i = 0; i < limit; i++)
        {
            string missing = discovered.MissingProjects[i];
            DriftRules.Add(
                findings,
                discovered.OriginPath,
                "-",
                Path.GetFileName(missing),
                DriftReason.ProjectReferenceLockIssue,
                $"Solution lists '{missing}' but the file does not exist.");
        }
    }

    private static ProjectSnapshot? TryRead(string path, List<DriftFinding> findings)
    {
        try
        {
            return ProjectFileReader.Read(path);
        }
        catch (Exception ex) when (ex is XmlException or IOException or InvalidDataException)
        {
            DriftRules.Add(findings, path, "-", Path.GetFileName(path), DriftReason.UnreadableInput, Trim(ex.Message));
            return null;
        }
    }

    private static void EnqueueAll(IReadOnlyList<string> paths, Queue<string> pending)
    {
        int limit = Math.Min(paths.Count, 512);
        for (int i = 0; i < limit; i++)
        {
            if (pending.Count >= SolutionGraph.MaxProjects)
            {
                return;
            }

            pending.Enqueue(paths[i]);
        }
    }

    private static string Trim(string message)
    {
        if (message.Length <= 400)
        {
            return message;
        }

        return message[..400];
    }
}
