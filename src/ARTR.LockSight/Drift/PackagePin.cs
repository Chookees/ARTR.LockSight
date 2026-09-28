namespace ARTR.LockSight.Drift;

/// <summary>
/// A PackageReference pin. <see cref="Version"/> may be empty when CPM did not supply one.
/// <see cref="Condition"/> is the raw MSBuild condition, which this tool only partially understands.
/// </summary>
public sealed class PackagePin
{
    public string PackageId { get; }
    public string Version { get; }
    public string Source { get; }
    public string Condition { get; }

    public PackagePin(string packageId, string version, string source, string condition = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(condition);

        PackageId = packageId;
        Version = version;
        Source = source;
        Condition = condition;
    }

    public bool HasResolvableVersion => !string.IsNullOrWhiteSpace(Version);
}
