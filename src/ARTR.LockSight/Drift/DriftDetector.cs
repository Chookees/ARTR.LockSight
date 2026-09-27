namespace ARTR.LockSight.Drift;

/// <summary>
/// Compares project/CPM pins against packages.lock.json and reports drift findings.
/// </summary>
public static class DriftDetector
{
    private const int MaxFindings = 2_000;

    /// <summary>
    /// Scans <paramref name="rootPath"/> and returns all drift findings (may be empty).
    /// </summary>
    public static IReadOnlyList<DriftFinding> Scan(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        string fullRoot = Path.GetFullPath(rootPath);
        string startDir = File.Exists(fullRoot)
            ? Path.GetDirectoryName(fullRoot) ?? fullRoot
            : fullRoot;

        IReadOnlyList<string> projects = WorkspaceScanner.FindProjectFiles(fullRoot);
        string? cpmPath = WorkspaceScanner.FindCentralPackageManagementFile(startDir);
        IReadOnlyDictionary<string, PackagePin> cpmPins = CentralPackageManagementReader.ReadPins(cpmPath);

        var findings = new List<DriftFinding>(capacity: 32);
        var snapshots = new Dictionary<string, ProjectSnapshot>(StringComparer.OrdinalIgnoreCase);

        int projectLimit = Math.Min(projects.Count, 2_000);
        for (int i = 0; i < projectLimit; i++)
        {
            if (findings.Count >= MaxFindings)
            {
                break;
            }

            ProjectSnapshot snapshot = ProjectFileReader.Read(projects[i], cpmPins);
            snapshots[snapshot.ProjectPath] = snapshot;
            AnalyzeProject(snapshot, findings);
        }

        // Second pass: ProjectReference-related lock issues need neighbor snapshots.
        foreach (KeyValuePair<string, ProjectSnapshot> pair in snapshots)
        {
            if (findings.Count >= MaxFindings)
            {
                break;
            }

            AnalyzeProjectReferences(pair.Value, snapshots, findings);
        }

        return findings;
    }

    private static void AnalyzeProject(ProjectSnapshot project, List<DriftFinding> findings)
    {
        if (!project.RestorePackagesWithLockFile)
        {
            return;
        }

        if (project.LockfilePath is null)
        {
            AddFinding(
                findings,
                project.ProjectPath,
                JoinFrameworks(project.TargetFrameworks),
                "(project)",
                DriftReason.MissingLockfile,
                "RestorePackagesWithLockFile is enabled but packages.lock.json is missing.");
            return;
        }

        LockfileDocument lockfile = LockfileReader.Read(project.LockfilePath);
        ComparePinsToLockfile(project, lockfile, findings);
        DetectMultiTfmMismatches(project, lockfile, findings);
    }

    private static void ComparePinsToLockfile(
        ProjectSnapshot project,
        LockfileDocument lockfile,
        List<DriftFinding> findings)
    {
        IReadOnlyList<string> frameworks = ResolveFrameworks(project, lockfile);
        int frameworkLimit = Math.Min(frameworks.Count, 64);
        for (int f = 0; f < frameworkLimit; f++)
        {
            string tfm = frameworks[f];
            if (!lockfile.ByFramework.TryGetValue(tfm, out IReadOnlyDictionary<string, LockDependency>? deps))
            {
                AddFinding(
                    findings,
                    project.ProjectPath,
                    tfm,
                    "(tfm)",
                    DriftReason.MultiTfmMismatch,
                    $"Project targets '{tfm}' but that TFM is absent from packages.lock.json.");
                continue;
            }

            int pinLimit = Math.Min(project.PackageReferences.Count, 2_000);
            for (int p = 0; p < pinLimit; p++)
            {
                if (findings.Count >= MaxFindings)
                {
                    return;
                }

                PackagePin pin = project.PackageReferences[p];
                if (!deps.TryGetValue(pin.PackageId, out LockDependency? locked))
                {
                    AddFinding(
                        findings,
                        project.ProjectPath,
                        tfm,
                        pin.PackageId,
                        DriftReason.PinDrift,
                        $"Package is referenced at {pin.Version} (from {DescribeSource(pin.Source)}) but is not present in the lockfile for {tfm}.");
                    continue;
                }

                if (!VersionsCompatible(pin.Version, locked.Resolved, locked.Requested))
                {
                    AddFinding(
                        findings,
                        project.ProjectPath,
                        tfm,
                        pin.PackageId,
                        DriftReason.PinDrift,
                        $"Pinned {pin.Version} (from {DescribeSource(pin.Source)}) but lockfile resolved {locked.Resolved} (requested {locked.Requested}).");
                }
            }
        }
    }

    private static void DetectMultiTfmMismatches(
        ProjectSnapshot project,
        LockfileDocument lockfile,
        List<DriftFinding> findings)
    {
        if (lockfile.ByFramework.Count < 2)
        {
            return;
        }

        // For each direct package, ensure resolved versions agree across TFMs when the package appears in multiple.
        var directIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int pinLimit = Math.Min(project.PackageReferences.Count, 2_000);
        for (int i = 0; i < pinLimit; i++)
        {
            directIds.Add(project.PackageReferences[i].PackageId);
        }

        string[] frameworks = new string[lockfile.ByFramework.Count];
        int frameworkIndex = 0;
        foreach (string key in lockfile.ByFramework.Keys)
        {
            frameworks[frameworkIndex] = key;
            frameworkIndex++;
        }

        foreach (string packageId in directIds)
        {
            if (findings.Count >= MaxFindings)
            {
                return;
            }

            string? firstResolved = null;
            string? firstTfm = null;
            for (int i = 0; i < frameworks.Length; i++)
            {
                IReadOnlyDictionary<string, LockDependency> deps = lockfile.ByFramework[frameworks[i]];
                if (!deps.TryGetValue(packageId, out LockDependency? dep) || !dep.IsDirect)
                {
                    continue;
                }

                if (firstResolved is null)
                {
                    firstResolved = dep.Resolved;
                    firstTfm = frameworks[i];
                    continue;
                }

                if (!string.Equals(firstResolved, dep.Resolved, StringComparison.OrdinalIgnoreCase))
                {
                    AddFinding(
                        findings,
                        project.ProjectPath,
                        frameworks[i],
                        packageId,
                        DriftReason.MultiTfmMismatch,
                        $"Resolved version differs across TFMs: {firstTfm}={firstResolved}, {frameworks[i]}={dep.Resolved}.");
                    break;
                }
            }
        }
    }

    private static void AnalyzeProjectReferences(
        ProjectSnapshot project,
        IReadOnlyDictionary<string, ProjectSnapshot> snapshots,
        List<DriftFinding> findings)
    {
        if (!project.RestorePackagesWithLockFile)
        {
            return;
        }

        int refLimit = Math.Min(project.ProjectReferences.Count, 512);
        for (int i = 0; i < refLimit; i++)
        {
            if (findings.Count >= MaxFindings)
            {
                return;
            }

            string referencedPath = project.ProjectReferences[i];
            if (!File.Exists(referencedPath))
            {
                AddFinding(
                    findings,
                    project.ProjectPath,
                    JoinFrameworks(project.TargetFrameworks),
                    Path.GetFileName(referencedPath),
                    DriftReason.ProjectReferenceLockIssue,
                    $"ProjectReference path does not exist: {referencedPath}");
                continue;
            }

            if (!snapshots.TryGetValue(referencedPath, out ProjectSnapshot? referenced))
            {
                // Referenced project may be outside the scan root; still check for a sibling lockfile.
                string siblingLock = Path.Combine(
                    Path.GetDirectoryName(referencedPath) ?? ".",
                    "packages.lock.json");
                if (!File.Exists(siblingLock))
                {
                    continue;
                }

                // Outside snapshot but has a lockfile — no further local comparison.
                continue;
            }

            if (referenced.RestorePackagesWithLockFile && referenced.LockfilePath is null)
            {
                AddFinding(
                    findings,
                    project.ProjectPath,
                    JoinFrameworks(project.TargetFrameworks),
                    Path.GetFileName(referenced.ProjectPath),
                    DriftReason.ProjectReferenceLockIssue,
                    $"Referenced project '{referenced.ProjectPath}' expects a lockfile but packages.lock.json is missing. RestoreLockedMode can fail transitively (NU1004).");
            }
        }
    }

    private static IReadOnlyList<string> ResolveFrameworks(ProjectSnapshot project, LockfileDocument lockfile)
    {
        if (project.TargetFrameworks.Count > 0)
        {
            return project.TargetFrameworks;
        }

        var keys = new List<string>(lockfile.ByFramework.Count);
        foreach (string key in lockfile.ByFramework.Keys)
        {
            keys.Add(key);
        }

        return keys;
    }

    /// <summary>
    /// Returns true when the project pin matches the lockfile resolved version,
    /// or when the pin is a floating range that still contains the resolved version (best-effort).
    /// </summary>
    internal static bool VersionsCompatible(string pinned, string resolved, string requested)
    {
        if (string.IsNullOrWhiteSpace(resolved))
        {
            return false;
        }

        if (string.Equals(pinned, resolved, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Exact pin inside NuGet range brackets, e.g. "[1.2.3]".
        if (pinned.StartsWith('[') && pinned.EndsWith(']') && !pinned.Contains(','))
        {
            string inner = pinned.Trim('[', ']').Trim();
            return string.Equals(inner, resolved, StringComparison.OrdinalIgnoreCase);
        }

        // If the lockfile still records the same requested string as the pin, treat as compatible.
        if (!string.IsNullOrWhiteSpace(requested) &&
            string.Equals(NormalizeRange(pinned), NormalizeRange(requested), StringComparison.OrdinalIgnoreCase))
        {
            // Floating pins can resolve differently after --force-evaluate; flag only when resolved clearly differs
            // from a concrete pin. For ranges like "1.0.*", we cannot prove drift offline — skip.
            if (pinned.Contains('*') || pinned.Contains(',') || pinned.StartsWith('[') || pinned.StartsWith('('))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeRange(string value)
    {
        return value.Trim();
    }

    private static string DescribeSource(string sourcePath)
    {
        return Path.GetFileName(sourcePath);
    }

    private static string JoinFrameworks(IReadOnlyList<string> frameworks)
    {
        if (frameworks.Count == 0)
        {
            return "-";
        }

        return string.Join(';', frameworks);
    }

    private static void AddFinding(
        List<DriftFinding> findings,
        string projectPath,
        string tfm,
        string packageId,
        DriftReason reason,
        string detail)
    {
        if (findings.Count >= MaxFindings)
        {
            return;
        }

        findings.Add(new DriftFinding(projectPath, tfm, packageId, reason, detail));
    }
}
