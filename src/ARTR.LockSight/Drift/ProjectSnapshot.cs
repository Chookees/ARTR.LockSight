namespace ARTR.LockSight.Drift;

/// <summary>
/// Snapshot of one MSBuild project relevant to lockfile diagnostics.
/// </summary>
public sealed class ProjectSnapshot
{
    public string ProjectPath { get; }
    public IReadOnlyList<string> TargetFrameworks { get; }
    public IReadOnlyList<PackagePin> PackageReferences { get; }
    public IReadOnlyList<string> ProjectReferences { get; }
    public bool RestorePackagesWithLockFile { get; }
    public string? LockfilePath { get; }

    public ProjectSnapshot(
        string projectPath,
        IReadOnlyList<string> targetFrameworks,
        IReadOnlyList<PackagePin> packageReferences,
        IReadOnlyList<string> projectReferences,
        bool restorePackagesWithLockFile,
        string? lockfilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentNullException.ThrowIfNull(targetFrameworks);
        ArgumentNullException.ThrowIfNull(packageReferences);
        ArgumentNullException.ThrowIfNull(projectReferences);

        ProjectPath = projectPath;
        TargetFrameworks = targetFrameworks;
        PackageReferences = packageReferences;
        ProjectReferences = projectReferences;
        RestorePackagesWithLockFile = restorePackagesWithLockFile;
        LockfilePath = lockfilePath;
    }
}
