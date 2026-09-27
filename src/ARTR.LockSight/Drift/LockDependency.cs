namespace ARTR.LockSight.Drift;

/// <summary>
/// One dependency row from packages.lock.json for a single TFM.
/// </summary>
public sealed class LockDependency
{
    public string PackageId { get; }
    public string Type { get; }
    public string Requested { get; }
    public string Resolved { get; }

    public LockDependency(string packageId, string type, string requested, string resolved)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(requested);
        ArgumentNullException.ThrowIfNull(resolved);

        PackageId = packageId;
        Type = type;
        Requested = requested;
        Resolved = resolved;
    }

    public bool IsDirect =>
        string.Equals(Type, "Direct", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Type, "Project", StringComparison.OrdinalIgnoreCase);
}
