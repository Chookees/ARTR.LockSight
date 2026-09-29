using System.Text.Json;

namespace ARTR.LockSight.Drift;

/// <summary>
/// Reads a Visual Studio solution filter (.slnf). Project paths are relative to the solution directory.
/// </summary>
internal static class SolutionFilterReader
{
    public static bool IsFilter(string path)
    {
        return path.EndsWith(".slnf", StringComparison.OrdinalIgnoreCase);
    }

    public static void Read(string filterPath, List<string> projects, List<string> missing)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filterPath);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(missing);

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(filterPath));
        if (!doc.RootElement.TryGetProperty("solution", out JsonElement solution))
        {
            throw new InvalidDataException($"Solution filter has no solution object: {filterPath}");
        }

        string? relativeSolution = ReadString(solution, "path");
        if (string.IsNullOrWhiteSpace(relativeSolution))
        {
            throw new InvalidDataException($"Solution filter has no solution path: {filterPath}");
        }

        string filterDir = Path.GetDirectoryName(Path.GetFullPath(filterPath)) ?? ".";
        string solutionPath = Path.GetFullPath(Path.Combine(filterDir, Normalize(relativeSolution)));
        if (!File.Exists(solutionPath))
        {
            throw new FileNotFoundException("Solution listed by the filter was not found.", solutionPath);
        }

        string solutionDir = Path.GetDirectoryName(solutionPath) ?? filterDir;
        if (!solution.TryGetProperty("projects", out JsonElement listed) || listed.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"Solution filter has no projects array: {filterPath}");
        }

        int count = 0;
        foreach (JsonElement item in listed.EnumerateArray())
        {
            if (count >= SolutionGraph.MaxProjects)
            {
                return;
            }

            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            string? relative = item.GetString();
            if (string.IsNullOrWhiteSpace(relative))
            {
                continue;
            }

            string full = Path.GetFullPath(Path.Combine(solutionDir, Normalize(relative)));
            if (File.Exists(full))
            {
                projects.Add(full);
            }
            else
            {
                missing.Add(full);
            }

            count++;
        }
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        return null;
    }

    private static string Normalize(string path)
    {
        return path.Replace('\\', Path.DirectorySeparatorChar);
    }
}
