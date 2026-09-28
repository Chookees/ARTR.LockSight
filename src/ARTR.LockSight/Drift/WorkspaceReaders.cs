using System.Text.Json;
using System.Xml.Linq;

namespace ARTR.LockSight.Drift;

/// <summary>
/// File-system discovery for projects, props, and lockfiles.
/// Directory walks are iterative and bounded (no recursion).
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
    /// Resolves a directory, project, or solution into project paths.
    /// </summary>
    public static ProjectSet Discover(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        string fullRoot = Path.GetFullPath(rootPath);
        if (!Directory.Exists(fullRoot) && !File.Exists(fullRoot))
        {
            throw new DirectoryNotFoundException($"Target path not found: {fullRoot}");
        }

        var projects = new List<string>(capacity: 64);
        var missing = new List<string>(capacity: 8);
        if (Directory.Exists(fullRoot))
        {
            CollectProjectsIterative(fullRoot, projects);
            return new ProjectSet(fullRoot, projects, missing);
        }

        if (IsProjectFile(fullRoot))
        {
            projects.Add(fullRoot);
            return new ProjectSet(fullRoot, projects, missing);
        }

        if (SolutionGraph.IsSolution(fullRoot))
        {
            SolutionGraph.Read(fullRoot, projects, missing);
            return new ProjectSet(fullRoot, projects, missing);
        }

        throw new InvalidDataException($"Target is not a directory, project, or solution: {fullRoot}");
    }

    /// <summary>
    /// Walks upward from <paramref name="startDirectory"/> for the nearest file of this name.
    /// MSBuild and NuGet stop at the first ancestor, so this does too.
    /// </summary>
    public static string? FindAncestorFile(string startDirectory, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(startDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        string? current = Path.GetFullPath(startDirectory);
        for (int i = 0; i < MaxDirectoryDepth && current is not null; i++)
        {
            string candidate = Path.Combine(current, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            DirectoryInfo? parent = Directory.GetParent(current);
            current = parent?.FullName;
        }

        return null;
    }

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
        int count = 0;
        foreach (XElement item in doc.Descendants("PackageVersion"))
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
/// Parses a project plus the nearest Directory.Build.props / .targets and Directory.Packages.props.
/// Import order matches MSBuild: props, then the project, then targets.
/// </summary>
public static class ProjectFileReader
{
    public static ProjectSnapshot Read(string projectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        if (!File.Exists(projectPath))
        {
            throw new FileNotFoundException("Project file not found.", projectPath);
        }

        string fullProject = Path.GetFullPath(projectPath);
        string projectDir = Path.GetDirectoryName(fullProject) ?? ".";
        XDocument projectDoc = XDocument.Load(fullProject);

        string? propsPath = WorkspaceScanner.FindAncestorFile(projectDir, "Directory.Build.props");
        string? targetsPath = WorkspaceScanner.FindAncestorFile(projectDir, "Directory.Build.targets");
        string? cpmPath = WorkspaceScanner.FindAncestorFile(projectDir, "Directory.Packages.props");
        XDocument? propsDoc = LoadIfExists(propsPath);
        XDocument? targetsDoc = LoadIfExists(targetsPath);
        IReadOnlyDictionary<string, PackagePin> cpmPins = CentralPackageManagementReader.ReadPins(cpmPath);

        var packages = new Dictionary<string, PackagePin>(StringComparer.OrdinalIgnoreCase);
        if (propsDoc is not null && propsPath is not null)
        {
            PackageGraphReader.MergePackageReferences(propsDoc, propsPath, cpmPins, packages);
        }

        PackageGraphReader.MergePackageReferences(projectDoc, fullProject, cpmPins, packages);
        if (targetsDoc is not null && targetsPath is not null)
        {
            PackageGraphReader.MergePackageReferences(targetsDoc, targetsPath, cpmPins, packages);
        }

        IReadOnlyList<string> frameworks = LayerFrameworks(propsDoc, projectDoc, targetsDoc);
        IReadOnlyList<string> projectRefs = PackageGraphReader.ReadProjectReferences(projectDoc, fullProject);
        bool? useLockFile = LayerBool(propsDoc, projectDoc, targetsDoc, "RestorePackagesWithLockFile");
        bool lockedMode = LayerBool(propsDoc, projectDoc, targetsDoc, "RestoreLockedMode") == true;
        string lockCandidate = Path.Combine(projectDir, "packages.lock.json");
        string? lockfilePath = File.Exists(lockCandidate) ? lockCandidate : null;
        var policy = new LockfilePolicy(useLockFile, lockedMode, lockfilePath);
        var pinList = new List<PackagePin>(packages.Count);
        foreach (PackagePin pin in packages.Values)
        {
            pinList.Add(pin);
        }

        return new ProjectSnapshot(fullProject, frameworks, pinList, projectRefs, policy);
    }

    private static XDocument? LoadIfExists(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        return XDocument.Load(path);
    }

    private static IReadOnlyList<string> LayerFrameworks(XDocument? props, XDocument project, XDocument? targets)
    {
        IReadOnlyList<string> result = props is null ? [] : PackageGraphReader.ReadTargetFrameworks(props);
        IReadOnlyList<string> fromProject = PackageGraphReader.ReadTargetFrameworks(project);
        if (fromProject.Count > 0)
        {
            result = fromProject;
        }

        if (targets is null)
        {
            return result;
        }

        IReadOnlyList<string> fromTargets = PackageGraphReader.ReadTargetFrameworks(targets);
        return fromTargets.Count > 0 ? fromTargets : result;
    }

    private static bool? LayerBool(XDocument? props, XDocument project, XDocument? targets, string name)
    {
        OptionalBool layered = props is null ? OptionalBool.Absent : PackageGraphReader.ReadBoolProperty(props, name);
        OptionalBool fromProject = PackageGraphReader.ReadBoolProperty(project, name);
        if (fromProject.IsPresent)
        {
            layered = fromProject;
        }

        if (targets is not null)
        {
            OptionalBool fromTargets = PackageGraphReader.ReadBoolProperty(targets, name);
            if (fromTargets.IsPresent)
            {
                layered = fromTargets;
            }
        }

        return layered.IsPresent ? layered.Value : null;
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

            byFramework[framework.Name] = ReadFramework(framework.Value);
            frameworkCount++;
        }

        return new LockfileDocument(lockfilePath, byFramework);
    }

    private static IReadOnlyDictionary<string, LockDependency> ReadFramework(JsonElement framework)
    {
        var packages = new Dictionary<string, LockDependency>(StringComparer.OrdinalIgnoreCase);
        int packageCount = 0;
        foreach (JsonProperty package in framework.EnumerateObject())
        {
            if (packageCount >= MaxPackagesPerFramework)
            {
                break;
            }

            string type = ReadString(package.Value, "type");
            string requested = ReadString(package.Value, "requested");
            string resolved = ReadString(package.Value, "resolved");
            string contentHash = ReadString(package.Value, "contentHash");
            packages[package.Name] = new LockDependency(package.Name, type, requested, resolved, contentHash);
            packageCount++;
        }

        return packages;
    }

    private static string ReadString(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? string.Empty;
        }

        return string.Empty;
    }
}
