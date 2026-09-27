namespace ARTR.LockSight.Drift;

/// <summary>
/// Package pin declared by a project or Directory.Packages.props.
/// </summary>
public sealed class PackagePin
{
    public string PackageId { get; }
    public string Version { get; }
    public string Source { get; }

    public PackagePin(string packageId, string version, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        PackageId = packageId;
        Version = version;
        Source = source;
    }
}
