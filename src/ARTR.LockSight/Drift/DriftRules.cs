namespace ARTR.LockSight.Drift;

/// <summary>
/// Compares one project snapshot with its lockfile. Each check is separate so a failure mode stays obvious.
/// </summary>
internal static class DriftRules
{
    public const int MaxFindings = 2_000;

    public static void AnalyzeProject(ProjectSnapshot project, List<DriftFinding> findings)
    {
        if (!project.Policy.ExpectsLockfile)
        {
            return;
        }

        if (project.Policy.LockfilePathUnresolved)
        {
            Add(
                findings,
                project.ProjectPath,
                JoinFrameworks(project.TargetFrameworks),
                "(lockfile)",
                DriftReason.LockfilePathUnresolved,
                "NuGetLockFilePath uses an MSBuild expression this tool cannot expand. Checked packages.lock.json beside the project instead.");
        }

        if (project.Policy.LockfilePath is null)
        {
            Add(
                findings,
                project.ProjectPath,
                JoinFrameworks(project.TargetFrameworks),
                "(project)",
                DriftReason.MissingLockfile,
                $"Lockfiles or RestoreLockedMode are enabled, but {project.Policy.ExpectedLockfilePath} is missing.");
            return;
        }

        LockfileDocument lockfile;
        try
        {
            lockfile = LockfileReader.Read(project.Policy.LockfilePath);
        }
        catch (System.Text.Json.JsonException ex)
        {
            Add(findings, project.ProjectPath, "-", "(lockfile)", DriftReason.UnreadableInput, Trim(ex.Message));
            return;
        }

        CheckMissingFrameworks(project, lockfile, findings);
        CheckStaleFrameworks(project, lockfile, findings);
        CheckPins(project, lockfile, findings);
        CheckOrphans(project, lockfile, findings);
        CheckConsistency(project, lockfile, findings);
        CheckVersionSkew(project, lockfile, findings);
    }

    public static void AnalyzeProjectReferences(
        ProjectSnapshot project,
        IReadOnlyDictionary<string, ProjectSnapshot> snapshots,
        List<DriftFinding> findings)
    {
        if (!project.Policy.ExpectsLockfile)
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
                Add(
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
                continue;
            }

            if (referenced.Policy.ExpectsLockfile && referenced.Policy.LockfilePath is null)
            {
                Add(
                    findings,
                    project.ProjectPath,
                    JoinFrameworks(project.TargetFrameworks),
                    Path.GetFileName(referenced.ProjectPath),
                    DriftReason.ProjectReferenceLockIssue,
                    $"Referenced project '{Path.GetFileName(referenced.ProjectPath)}' expects a lockfile, but packages.lock.json is missing. RestoreLockedMode can fail on the graph (NU1004).");
            }
        }
    }

    public static void Add(
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

    private static void CheckMissingFrameworks(ProjectSnapshot project, LockfileDocument lockfile, List<DriftFinding> findings)
    {
        int limit = Math.Min(project.TargetFrameworks.Count, 64);
        for (int i = 0; i < limit; i++)
        {
            string tfm = project.TargetFrameworks[i];
            if (lockfile.ByFramework.ContainsKey(tfm))
            {
                continue;
            }

            Add(
                findings,
                project.ProjectPath,
                tfm,
                "(tfm)",
                DriftReason.MultiTfmMismatch,
                $"Project targets '{tfm}' but packages.lock.json has no '{tfm}' section.");
        }
    }

    private static void CheckStaleFrameworks(ProjectSnapshot project, LockfileDocument lockfile, List<DriftFinding> findings)
    {
        if (project.TargetFrameworks.Count == 0)
        {
            return;
        }

        foreach (string tfm in lockfile.ByFramework.Keys)
        {
            if (findings.Count >= MaxFindings)
            {
                return;
            }

            if (ContainsFramework(project.TargetFrameworks, tfm))
            {
                continue;
            }

            Add(
                findings,
                project.ProjectPath,
                tfm,
                "(tfm)",
                DriftReason.StaleTfm,
                $"packages.lock.json still has '{tfm}', which the project does not target. This is a warning; delete it by regenerating the lockfile if the TFM was removed.");
        }
    }

    private static void CheckPins(ProjectSnapshot project, LockfileDocument lockfile, List<DriftFinding> findings)
    {
        IReadOnlyList<string> frameworks = FrameworksFor(project, lockfile);
        int frameworkLimit = Math.Min(frameworks.Count, 64);
        for (int f = 0; f < frameworkLimit; f++)
        {
            string tfm = frameworks[f];
            if (!lockfile.ByFramework.TryGetValue(tfm, out IReadOnlyDictionary<string, LockDependency>? deps))
            {
                continue;
            }

            CheckPinsForFramework(project, tfm, deps, findings);
        }
    }

    private static void CheckPinsForFramework(
        ProjectSnapshot project,
        string tfm,
        IReadOnlyDictionary<string, LockDependency> deps,
        List<DriftFinding> findings)
    {
        int pinLimit = Math.Min(project.PackageReferences.Count, 2_000);
        for (int p = 0; p < pinLimit; p++)
        {
            if (findings.Count >= MaxFindings)
            {
                return;
            }

            PackagePin pin = project.PackageReferences[p];
            if (!FrameworkCondition.AppliesTo(pin.Condition, tfm))
            {
                continue;
            }

            if (!pin.HasResolvableVersion)
            {
                if (!deps.ContainsKey(pin.PackageId))
                {
                    Add(findings, project.ProjectPath, tfm, pin.PackageId, DriftReason.PinDrift,
                        "PackageReference has no Version and no Directory.Packages.props pin, and it is absent from the lockfile.");
                }

                continue;
            }

            if (!deps.TryGetValue(pin.PackageId, out LockDependency? locked))
            {
                Add(findings, project.ProjectPath, tfm, pin.PackageId, DriftReason.PinDrift,
                    $"Pinned {pin.Version} (from {Path.GetFileName(pin.Source)}) but the package is absent from the lockfile for {tfm}.");
                continue;
            }

            if (!PinCompatibility.IsCompatible(pin.Version, locked.Resolved, locked.Requested, out string detail))
            {
                Add(findings, project.ProjectPath, tfm, pin.PackageId, DriftReason.PinDrift,
                    $"Pinned {pin.Version} (from {Path.GetFileName(pin.Source)}). {detail}");
            }
        }
    }

    private static void CheckOrphans(ProjectSnapshot project, LockfileDocument lockfile, List<DriftFinding> findings)
    {
        IReadOnlyList<string> frameworks = FrameworksFor(project, lockfile);
        int frameworkLimit = Math.Min(frameworks.Count, 64);
        for (int f = 0; f < frameworkLimit; f++)
        {
            if (!lockfile.ByFramework.TryGetValue(frameworks[f], out IReadOnlyDictionary<string, LockDependency>? deps))
            {
                continue;
            }

            foreach (LockDependency dep in deps.Values)
            {
                if (findings.Count >= MaxFindings)
                {
                    return;
                }

                if (!dep.IsDirect || IsReferenced(project, dep.PackageId, frameworks[f]))
                {
                    continue;
                }

                Add(findings, project.ProjectPath, frameworks[f], dep.PackageId, DriftReason.OrphanLockEntry,
                    $"Lockfile lists {dep.PackageId} as Direct (resolved {dep.Resolved}) but no PackageReference applies to {frameworks[f]}. A removed reference leaves RestoreLockedMode inconsistent (NU1004).");
            }
        }
    }

    private static void CheckConsistency(ProjectSnapshot project, LockfileDocument lockfile, List<DriftFinding> findings)
    {
        foreach (KeyValuePair<string, IReadOnlyDictionary<string, LockDependency>> framework in lockfile.ByFramework)
        {
            foreach (LockDependency dep in framework.Value.Values)
            {
                if (findings.Count >= MaxFindings)
                {
                    return;
                }

                if (dep.RequiresContentHash && string.IsNullOrWhiteSpace(dep.ContentHash))
                {
                    Add(findings, project.ProjectPath, framework.Key, dep.PackageId, DriftReason.LockfileInconsistent,
                        "Lockfile entry has no contentHash. Locked restore checks the hash for package rows.");
                }

                if (dep.IsDirect || dep.IsProjectReference || string.IsNullOrWhiteSpace(dep.Requested))
                {
                    continue;
                }

                if (!PinCompatibility.RequestedContainsResolved(dep.Requested, dep.Resolved))
                {
                    Add(findings, project.ProjectPath, framework.Key, dep.PackageId, DriftReason.LockfileInconsistent,
                        $"Resolved {dep.Resolved} does not satisfy the lockfile requested range {dep.Requested}.");
                }
            }
        }
    }

    private static void CheckVersionSkew(ProjectSnapshot project, LockfileDocument lockfile, List<DriftFinding> findings)
    {
        if (project.TargetFrameworks.Count < 2)
        {
            return;
        }

        int pinLimit = Math.Min(project.PackageReferences.Count, 2_000);
        for (int p = 0; p < pinLimit; p++)
        {
            if (findings.Count >= MaxFindings)
            {
                return;
            }

            PackagePin pin = project.PackageReferences[p];
            if (!TryFindSkew(project, lockfile, pin, out string detail, out string tfm))
            {
                continue;
            }

            Add(findings, project.ProjectPath, tfm, pin.PackageId, DriftReason.MultiTfmVersionSkew, detail);
        }
    }

    private static bool TryFindSkew(
        ProjectSnapshot project,
        LockfileDocument lockfile,
        PackagePin pin,
        out string detail,
        out string tfm)
    {
        detail = string.Empty;
        tfm = string.Empty;
        string? firstResolved = null;
        string? firstTfm = null;
        int limit = Math.Min(project.TargetFrameworks.Count, 64);
        for (int i = 0; i < limit; i++)
        {
            string candidate = project.TargetFrameworks[i];
            if (!FrameworkCondition.AppliesTo(pin.Condition, candidate))
            {
                continue;
            }

            if (!lockfile.ByFramework.TryGetValue(candidate, out IReadOnlyDictionary<string, LockDependency>? deps))
            {
                continue;
            }

            if (!deps.TryGetValue(pin.PackageId, out LockDependency? dep) || string.IsNullOrWhiteSpace(dep.Resolved))
            {
                continue;
            }

            if (firstResolved is null)
            {
                firstResolved = dep.Resolved;
                firstTfm = candidate;
                continue;
            }

            if (!string.Equals(firstResolved, dep.Resolved, StringComparison.OrdinalIgnoreCase))
            {
                tfm = candidate;
                detail = $"Resolved version differs across TFMs: {firstTfm}={firstResolved}, {candidate}={dep.Resolved}. This is a warning; it can be legitimate.";
                return true;
            }
        }

        return false;
    }

    private static bool IsReferenced(ProjectSnapshot project, string packageId, string tfm)
    {
        int limit = Math.Min(project.PackageReferences.Count, 2_000);
        for (int i = 0; i < limit; i++)
        {
            PackagePin pin = project.PackageReferences[i];
            if (!string.Equals(pin.PackageId, packageId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (FrameworkCondition.AppliesTo(pin.Condition, tfm))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<string> FrameworksFor(ProjectSnapshot project, LockfileDocument lockfile)
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

    private static bool ContainsFramework(IReadOnlyList<string> frameworks, string tfm)
    {
        int limit = Math.Min(frameworks.Count, 64);
        for (int i = 0; i < limit; i++)
        {
            if (string.Equals(frameworks[i], tfm, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string JoinFrameworks(IReadOnlyList<string> frameworks)
    {
        if (frameworks.Count == 0)
        {
            return "-";
        }

        return string.Join(';', frameworks);
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
