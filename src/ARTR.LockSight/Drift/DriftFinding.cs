namespace ARTR.LockSight.Drift;

/// <summary>
/// One drift row. Severity is derived from <see cref="Reason"/> so callers stay at five arguments.
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

    /// <summary>
    /// Warnings are printed and do not fail <c>--ci</c> unless <c>--strict</c> is set.
    /// Cross-TFM version differences and leftover TFM sections are often legitimate.
    /// </summary>
    public bool IsWarning =>
        Reason is DriftReason.MultiTfmVersionSkew
            or DriftReason.StaleTfm
            or DriftReason.LockfilePathUnresolved;
}
