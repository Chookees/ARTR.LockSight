namespace ARTR.LockSight.Drift;

/// <summary>
/// One reported drift finding. Designed for table/CI line output.
/// </summary>
public sealed class DriftFinding
{
    public string ProjectPath { get; }
    public string TargetFramework { get; }
    public string PackageId { get; }
    public DriftReason Reason { get; }
    public string Detail { get; }

    public DriftFinding(
        string projectPath,
        string targetFramework,
        string packageId,
        DriftReason reason,
        string detail)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        ArgumentNullException.ThrowIfNull(targetFramework);
        ArgumentNullException.ThrowIfNull(packageId);
        ArgumentNullException.ThrowIfNull(detail);

        ProjectPath = projectPath;
        TargetFramework = targetFramework;
        PackageId = packageId;
        Reason = reason;
        Detail = detail;
    }
}
