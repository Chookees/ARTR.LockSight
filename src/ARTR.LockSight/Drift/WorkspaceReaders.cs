using System.Text.Json;
using System.Xml.Linq;

namespace ARTR.LockSight.Drift;

/// <summary>
/// File-system discovery for projects, CPM props, and lockfiles.
/// All loops have fixed upper bounds (Power of Ten rule 2).
/// </summary>
public static class WorkspaceScanner
{
    private const int MaxFilesToScan = 10_000;
    private const int MaxDirectoryDepth = 32;

    private static readonly string[] ProjectExtensions =
    [
        ".csproj",
        ".fsproj",
        ".vbproj",
    ];

    /// <summary>
    /// Finds project files under <paramref name="rootPath"/> (non-recursive into bin/obj/.git).
    /// </summary>
    public static IReadOnlyList<string> FindProjectFiles(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        string fullRoot = Path.GetFullPath(rootPath);
        if (!Directory.Exists(fullRoot) && !File.Exists(fullRoot))
        {
            throw new DirectoryNotFoundException($"Target path not found: {fullRoot}");
        }

        if (File.Exists(fullRoot))
        {
            return IsProjectFile(fullRoot)
                ? [fullRoot]
                : Array.Empty<string>();
        }

        var results = new List<string>(capacity: 64);
        CollectProjectsIterative(fullRoot, results);
        return results;
    }

    /// <summary>
    /// Walks upward from <paramref name="startDirectory"/> looking for Directory.Packages.props.
    /// </summary>
    public static string? FindCentralPackageManagementFile(string startDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(startDirectory);

        string? current = Path.GetFullPath(startDirectory);
        for (int i = 0; i < MaxDirectoryDepth && current is not null; i++)
        {
            string candidate = Path.Combine(current, "Directory.Packages.props");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            DirectoryInfo? parent = Directory.GetParent(current);
            current = parent?.FullName;
        }

        return null;
    }

    /// <summary>
    /// Breadth-first directory walk with fixed bounds (no recursion).
    /// </summary>
    private static void CollectProjectsIterative(string rootDirectory, List<string> results)
    {
        var pending = new Queue<(string Path, int Depth)>();
        pending.Enqueue((rootDirectory, 0));

        while (pending.Count > 0 && results.Count < MaxFilesToScan)
        {
            (string directory, int depth) = pending.Dequeue();
            if (depth > MaxDirectoryDepth)
            {
                continue;
            }

            string name = Path.GetFileName(directory);
            if (depth > 0 && ShouldSkipDirectory(name))
            {
                continue;
            }

            if (!TryListFiles(directory, out string[] files))
            {
                continue;
            }

            int fileLimit = Math.Min(files.Length, MaxFilesToScan);
            for (int i = 0; i < fileLimit; i++)
            {
                if (results.Count >= MaxFilesToScan)
                {
                    return;
                }

                if (IsProjectFile(files[i]))
                {
                    results.Add(files[i]);
                }
            }

            if (!TryListDirectories(directory, out string[] dirs))
            {
                continue;
            }

            int dirLimit = Math.Min(dirs.Length, MaxFilesToScan);
            for (int i = 0; i < dirLimit; i++)
            {
                pending.Enqueue((dirs[i], depth + 1));
            }
        }
    }

    private static bool TryListFiles(string directory, out string[] files)
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

    private static bool TryListDirectories(string directory, out string[] dirs)
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

    private static bool ShouldSkipDirectory(string name)
    {
        return name is "bin" or "obj" or ".git" or "node_modules" or ".vs" or "packages";
    }

    private static bool IsProjectFile(string path)
    {
        string ext = Path.GetExtension(path);
        for (int i = 0; i < ProjectExtensions.Length; i++)
        {
            if (string.Equals(ext, ProjectExtensions[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Loads Directory.Packages.props PackageVersion pins.
/// </summary>
public static class CentralPackageManagementReader
{
    private const int MaxPackageVersions = 5_000;

    public static IReadOnlyDictionary<string, PackagePin> ReadPins(string? propsPath)
    {
        var map = new Dictionary<string, PackagePin>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(propsPath) || !File.Exists(propsPath))
        {
            return map;
        }

        XDocument doc = XDocument.Load(propsPath);
        IEnumerable<XElement> items = doc.Descendants("PackageVersion");
        int count = 0;
        foreach (XElement item in items)
        {
            if (count >= MaxPackageVersions)
            {
                break;
            }

            string? id = (string?)item.Attribute("Include") ?? (string?)item.Attribute("Update");
            string? version = (string?)item.Attribute("Version");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version))
            {
                continue;
            }

            map[id] = new PackagePin(id, version, propsPath);
            count++;
        }

        return map;
    }
}

/// <summary>
/// Parses a .csproj/.fsproj/.vbproj into a <see cref="ProjectSnapshot"/>.
/// </summary>
public static class ProjectFileReader
{
    private const int MaxItems = 2_000;

    public static ProjectSnapshot Read(string projectPath, IReadOnlyDictionary<string, PackagePin> cpmPins)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentNullException.ThrowIfNull(cpmPins);

        if (!File.Exists(projectPath))
        {
            throw new FileNotFoundException("Project file not found.", projectPath);
        }

        XDocument doc = XDocument.Load(projectPath);
        IReadOnlyList<string> tfms = ReadTargetFrameworks(doc);
        IReadOnlyList<PackagePin> packages = ReadPackageReferences(doc, projectPath, cpmPins);
        IReadOnlyList<string> projectRefs = ReadProjectReferences(doc, projectPath);
        bool useLockFile = ReadRestorePackagesWithLockFile(doc);
        string lockfilePath = Path.Combine(Path.GetDirectoryName(projectPath) ?? ".", "packages.lock.json");
        string? resolvedLock = File.Exists(lockfilePath) ? lockfilePath : null;

        // Presence of a lockfile implies the project uses lockfiles even if the property is unset.
        if (resolvedLock is not null)
        {
            useLockFile = true;
        }

        return new ProjectSnapshot(
            projectPath,
            tfms,
            packages,
            projectRefs,
            useLockFile,
            resolvedLock);
    }

    private static IReadOnlyList<string> ReadTargetFrameworks(XDocument doc)
    {
        var list = new List<string>(capacity: 4);
        string? single = FirstProperty(doc, "TargetFramework");
        if (!string.IsNullOrWhiteSpace(single))
        {
            list.Add(single.Trim());
            return list;
        }

        string? multi = FirstProperty(doc, "TargetFrameworks");
        if (string.IsNullOrWhiteSpace(multi))
        {
            return list;
        }

        string[] parts = multi.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int limit = Math.Min(parts.Length, 32);
        for (int i = 0; i < limit; i++)
        {
            if (!string.IsNullOrWhiteSpace(parts[i]))
            {
                list.Add(parts[i]);
            }
        }

        return list;
    }

    private static IReadOnlyList<PackagePin> ReadPackageReferences(
        XDocument doc,
        string projectPath,
        IReadOnlyDictionary<string, PackagePin> cpmPins)
    {
        var list = new List<PackagePin>(capacity: 32);
        int count = 0;
        foreach (XElement item in doc.Descendants("PackageReference"))
        {
            if (count >= MaxItems)
            {
                break;
            }

            string? id = (string?)item.Attribute("Include") ?? (string?)item.Attribute("Update");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            string? version = (string?)item.Attribute("Version") ?? (string?)item.Element("Version");
            string source = projectPath;
            if (string.IsNullOrWhiteSpace(version))
            {
                if (cpmPins.TryGetValue(id, out PackagePin? cpmPin))
                {
                    version = cpmPin.Version;
                    source = cpmPin.Source;
                }
                else
                {
                    continue;
                }
            }

            list.Add(new PackagePin(id, version, source));
            count++;
        }

        return list;
    }

    private static IReadOnlyList<string> ReadProjectReferences(XDocument doc, string projectPath)
    {
        string? projectDir = Path.GetDirectoryName(projectPath);
        var list = new List<string>(capacity: 16);
        int count = 0;
        foreach (XElement item in doc.Descendants("ProjectReference"))
        {
            if (count >= MaxItems)
            {
                break;
            }

            string? include = (string?)item.Attribute("Include");
            if (string.IsNullOrWhiteSpace(include))
            {
                continue;
            }

            string combined = projectDir is null
                ? include
                : Path.GetFullPath(Path.Combine(projectDir, include));
            list.Add(combined);
            count++;
        }

        return list;
    }

    private static bool ReadRestorePackagesWithLockFile(XDocument doc)
    {
        string? value = FirstProperty(doc, "RestorePackagesWithLockFile");
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FirstProperty(XDocument doc, string name)
    {
        foreach (XElement el in doc.Descendants(name))
        {
            string? text = el.Value;
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text.Trim();
            }
        }

        return null;
    }
}

/// <summary>
/// Parses packages.lock.json without network access.
/// </summary>
public static class LockfileReader
{
    private const int MaxFrameworks = 64;
    private const int MaxPackagesPerFramework = 10_000;

    public static LockfileDocument Read(string lockfilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockfilePath);
        if (!File.Exists(lockfilePath))
        {
            throw new FileNotFoundException("Lockfile not found.", lockfilePath);
        }

        string json = File.ReadAllText(lockfilePath);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        var byFramework = new Dictionary<string, IReadOnlyDictionary<string, LockDependency>>(
            StringComparer.OrdinalIgnoreCase);

        if (!root.TryGetProperty("dependencies", out JsonElement dependencies))
        {
            return new LockfileDocument(lockfilePath, byFramework);
        }

        int frameworkCount = 0;
        foreach (JsonProperty framework in dependencies.EnumerateObject())
        {
            if (frameworkCount >= MaxFrameworks)
            {
                break;
            }

            var packages = new Dictionary<string, LockDependency>(StringComparer.OrdinalIgnoreCase);
            int packageCount = 0;
            foreach (JsonProperty package in framework.Value.EnumerateObject())
            {
                if (packageCount >= MaxPackagesPerFramework)
                {
                    break;
                }

                string type = ReadStringProperty(package.Value, "type");
                string requested = ReadStringProperty(package.Value, "requested");
                string resolved = ReadStringProperty(package.Value, "resolved");
                packages[package.Name] = new LockDependency(package.Name, type, requested, resolved);
                packageCount++;
            }

            byFramework[framework.Name] = packages;
            frameworkCount++;
        }

        return new LockfileDocument(lockfilePath, byFramework);
    }

    private static string ReadStringProperty(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out JsonElement value) &&
            value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? string.Empty;
        }

        return string.Empty;
    }
}
