namespace ARTR.LockSight.Drift;

/// <summary>
/// Static snapshot of one project. This is not a full MSBuild evaluation.
/// </summary>
public sealed class ProjectSnapshot
{
    public string ProjectPath { get; }
    public IReadOnlyList<string> TargetFrameworks { get; }
    public IReadOnlyList<PackagePin> PackageReferences { get; }
    public IReadOnlyList<string> ProjectReferences { get; }
    public LockfilePolicy Policy { get; }

    public ProjectSnapshot(
        string projectPath,
        IReadOnlyList<string> targetFrameworks,
        IReadOnlyList<PackagePin> packageReferences,
        IReadOnlyList<string> projectReferences,
        LockfilePolicy policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentNullException.ThrowIfNull(targetFrameworks);
        ArgumentNullException.ThrowIfNull(packageReferences);
        ArgumentNullException.ThrowIfNull(projectReferences);
        ArgumentNullException.ThrowIfNull(policy);

        ProjectPath = projectPath;
        TargetFrameworks = targetFrameworks;
        PackageReferences = packageReferences;
        ProjectReferences = projectReferences;
        Policy = policy;
    }
}
