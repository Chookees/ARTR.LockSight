namespace ARTR.LockSight.Drift;

/// <summary>
/// How a package row differs between two lockfiles.
/// </summary>
public enum LockChange
{
    Added = 1,
    Removed = 2,
    Updated = 3,
}

/// <summary>
/// One row from <c>diff</c>.
/// </summary>
public sealed class LockDiffEntry
{
    public string Lockfile { get; }
    public string TargetFramework { get; }
    public string PackageId { get; }
    public LockChange Change { get; }
    public string Detail { get; }

    public LockDiffEntry(string lockfile, string targetFramework, string packageId, LockChange change, string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockfile);
        ArgumentNullException.ThrowIfNull(targetFramework);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(detail);

        Lockfile = lockfile;
        TargetFramework = targetFramework;
        PackageId = packageId;
        Change = change;
        Detail = detail;
    }
}

/// <summary>
/// Compares two lockfiles, two projects, or two directories. No restore and no network.
/// </summary>
public static class LockfileDiffer
{
    private const int MaxChanges = 2_000;
    private const int MaxLockfiles = 2_000;

    public static IReadOnlyList<LockDiffEntry> Compare(string leftPath, string rightPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leftPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(rightPath);

        Dictionary<string, LockfileDocument> left = Collect(leftPath);
        Dictionary<string, LockfileDocument> right = Collect(rightPath);
        EnsureSameShape(left, right);

        var changes = new List<LockDiffEntry>(capacity: 16);
        var keys = new List<string>(left.Count + right.Count);
        CollectKeys(left, keys);
        CollectKeys(right, keys);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int limit = Math.Min(keys.Count, MaxLockfiles);
        for (int i = 0; i < limit; i++)
        {
            if (changes.Count >= MaxChanges || !seen.Add(keys[i]))
            {
                continue;
            }

            CompareOneKey(keys[i], left, right, changes);
        }

        return changes;
    }

    private static void CollectKeys(Dictionary<string, LockfileDocument> map, List<string> keys)
    {
        foreach (string key in map.Keys)
        {
            if (keys.Count >= MaxLockfiles)
            {
                return;
            }

            keys.Add(key);
        }
    }

    private static void CompareOneKey(
        string key,
        Dictionary<string, LockfileDocument> left,
        Dictionary<string, LockfileDocument> right,
        List<LockDiffEntry> changes)
    {
        left.TryGetValue(key, out LockfileDocument? leftDoc);
        right.TryGetValue(key, out LockfileDocument? rightDoc);
        string label = key.Length == 0 ? "packages.lock.json" : key.Replace('\\', '/');
        if (leftDoc is null || rightDoc is null)
        {
            bool added = leftDoc is null;
            changes.Add(new LockDiffEntry(
                label,
                "-",
                "(lockfile)",
                added ? LockChange.Added : LockChange.Removed,
                added ? "Lockfile exists only on the right." : "Lockfile exists only on the left."));
            return;
        }

        CompareDocuments(label, leftDoc, rightDoc, changes);
    }

    private static void CompareDocuments(
        string label,
        LockfileDocument left,
        LockfileDocument right,
        List<LockDiffEntry> changes)
    {
        var frameworks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddFrameworkNames(left, frameworks);
        AddFrameworkNames(right, frameworks);
        foreach (string tfm in frameworks)
        {
            if (changes.Count >= MaxChanges)
            {
                return;
            }

            left.ByFramework.TryGetValue(tfm, out IReadOnlyDictionary<string, LockDependency>? leftDeps);
            right.ByFramework.TryGetValue(tfm, out IReadOnlyDictionary<string, LockDependency>? rightDeps);
            CompareFramework(label, tfm, leftDeps, rightDeps, changes);
        }
    }

    private static void CompareFramework(
        string label,
        string tfm,
        IReadOnlyDictionary<string, LockDependency>? leftDeps,
        IReadOnlyDictionary<string, LockDependency>? rightDeps,
        List<LockDiffEntry> changes)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddPackageIds(leftDeps, ids);
        AddPackageIds(rightDeps, ids);
        foreach (string id in ids)
        {
            if (changes.Count >= MaxChanges)
            {
                return;
            }

            LockDependency? left = Find(leftDeps, id);
            LockDependency? right = Find(rightDeps, id);
            if (left is null || right is null)
            {
                LockDependency present = left ?? right!;
                changes.Add(new LockDiffEntry(
                    label,
                    tfm,
                    id,
                    left is null ? LockChange.Added : LockChange.Removed,
                    $"resolved {Display(present.Resolved)}"));
                continue;
            }

            if (SameRow(left, right))
            {
                continue;
            }

            changes.Add(new LockDiffEntry(
                label,
                tfm,
                id,
                LockChange.Updated,
                $"resolved {Display(left.Resolved)} -> {Display(right.Resolved)}; requested {Display(left.Requested)} -> {Display(right.Requested)}"));
        }
    }

    private static Dictionary<string, LockfileDocument> Collect(string path)
    {
        string full = Path.GetFullPath(path);
        var map = new Dictionary<string, LockfileDocument>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(full))
        {
            map[""] = LockfileReader.Read(ResolveLockfile(full));
            return map;
        }

        if (!Directory.Exists(full))
        {
            throw new DirectoryNotFoundException($"Target path not found: {full}");
        }

        CollectDirectory(full, full, map);
        return map;
    }

    private static string ResolveLockfile(string filePath)
    {
        if (filePath.EndsWith("packages.lock.json", StringComparison.OrdinalIgnoreCase)
            || filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return filePath;
        }

        string? directory = Path.GetDirectoryName(filePath);
        string sibling = Path.Combine(directory ?? ".", "packages.lock.json");
        if (!File.Exists(sibling))
        {
            throw new FileNotFoundException($"packages.lock.json was not found next to {filePath}.", sibling);
        }

        return sibling;
    }

    private static void CollectDirectory(string root, string current, Dictionary<string, LockfileDocument> map)
    {
        // Same skip list as WorkspaceScanner so bin/obj lockfiles are ignored.
        var pending = new Queue<string>();
        pending.Enqueue(current);
        while (pending.Count > 0 && map.Count < MaxLockfiles)
        {
            string directory = pending.Dequeue();
            if (!TryFiles(directory, out string[] files) || !TryDirs(directory, out string[] dirs))
            {
                continue;
            }

            int fileLimit = Math.Min(files.Length, MaxLockfiles);
            for (int i = 0; i < fileLimit; i++)
            {
                if (!files[i].EndsWith($"{Path.DirectorySeparatorChar}packages.lock.json", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(Path.GetFileName(files[i]), "packages.lock.json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string relative = Path.GetRelativePath(root, files[i]);
                map[relative] = LockfileReader.Read(files[i]);
            }

            int dirLimit = Math.Min(dirs.Length, MaxLockfiles);
            for (int i = 0; i < dirLimit; i++)
            {
                string name = Path.GetFileName(dirs[i]);
                if (name is "bin" or "obj" or ".git" or "node_modules" or ".vs" or "packages")
                {
                    continue;
                }

                pending.Enqueue(dirs[i]);
            }
        }
    }

    private static void EnsureSameShape(
        Dictionary<string, LockfileDocument> left,
        Dictionary<string, LockfileDocument> right)
    {
        bool leftSingle = left.Count == 1 && left.ContainsKey("");
        bool rightSingle = right.Count == 1 && right.ContainsKey("");
        if (leftSingle != rightSingle)
        {
            throw new InvalidDataException("diff expects two lockfiles/projects or two directories, not a mix.");
        }
    }

    private static bool SameRow(LockDependency left, LockDependency right)
    {
        return string.Equals(left.Type, right.Type, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.Requested, right.Requested, StringComparison.Ordinal)
            && string.Equals(left.Resolved, right.Resolved, StringComparison.Ordinal)
            && string.Equals(left.ContentHash, right.ContentHash, StringComparison.Ordinal);
    }

    private static void AddFrameworkNames(LockfileDocument doc, HashSet<string> names)
    {
        foreach (string name in doc.ByFramework.Keys)
        {
            names.Add(name);
        }
    }

    private static void AddPackageIds(IReadOnlyDictionary<string, LockDependency>? deps, HashSet<string> ids)
    {
        if (deps is null)
        {
            return;
        }

        foreach (string id in deps.Keys)
        {
            ids.Add(id);
        }
    }

    private static LockDependency? Find(IReadOnlyDictionary<string, LockDependency>? deps, string id)
    {
        if (deps is null)
        {
            return null;
        }

        return deps.TryGetValue(id, out LockDependency? dep) ? dep : null;
    }

    private static string Display(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value;
    }

    private static bool TryFiles(string directory, out string[] files)
    {
        try
        {
            files = Directory.GetFiles(directory);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            files = [];
            return false;
        }
        catch (IOException)
        {
            files = [];
            return false;
        }
    }

    private static bool TryDirs(string directory, out string[] dirs)
    {
        try
        {
            dirs = Directory.GetDirectories(directory);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            dirs = [];
            return false;
        }
        catch (IOException)
        {
            dirs = [];
            return false;
        }
    }
}
