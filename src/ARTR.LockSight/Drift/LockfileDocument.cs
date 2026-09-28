namespace ARTR.LockSight.Drift;

/// <summary>
/// Parsed packages.lock.json contents keyed by TFM then package id.
/// </summary>
public sealed class LockfileDocument
{
    public string FilePath { get; }
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, LockDependency>> ByFramework { get; }

    public LockfileDocument(
        string filePath,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, LockDependency>> byFramework)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(byFramework);

        FilePath = filePath;
        ByFramework = byFramework;
    }
}
