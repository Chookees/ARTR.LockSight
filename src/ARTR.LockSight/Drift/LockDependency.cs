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
    public string ContentHash { get; }

    public LockDependency(string packageId, string type, string requested, string resolved, string contentHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(requested);
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(contentHash);

        PackageId = packageId;
        Type = type;
        Requested = requested;
        Resolved = resolved;
        ContentHash = contentHash;
    }

    /// <summary>PackageReference row. Project references use <see cref="IsProjectReference"/>.</summary>
    public bool IsDirect => string.Equals(Type, "Direct", StringComparison.OrdinalIgnoreCase);

    public bool IsProjectReference => string.Equals(Type, "Project", StringComparison.OrdinalIgnoreCase);

    public bool RequiresContentHash => !IsProjectReference && Type.Length > 0;
}
