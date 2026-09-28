namespace ARTR.LockSight.Drift;

/// <summary>
/// Reads .sln and .slnx project lists. Paths are normalized so Linux can open Windows solutions.
/// </summary>
internal static class SolutionGraph
{
    public const int MaxProjects = 2_000;
    private const int MaxLines = 100_000;

    public static bool IsSolution(string path)
    {
        string ext = Path.GetExtension(path);
        return ext.Equals(".sln", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".slnx", StringComparison.OrdinalIgnoreCase);
    }

    public static void Read(string solutionPath, List<string> projects, List<string> missing)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(missing);

        if (solutionPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            ReadSlnx(solutionPath, projects, missing);
            return;
        }

        ReadSln(solutionPath, projects, missing);
    }

    private static void ReadSln(string solutionPath, List<string> projects, List<string> missing)
    {
        using var reader = new StreamReader(solutionPath);
        int lines = 0;
        while (reader.ReadLine() is string line && lines < MaxLines)
        {
            lines++;
            if (projects.Count + missing.Count >= MaxProjects)
            {
                return;
            }

            if (!TryReadProjectPath(line, out string relative))
            {
                continue;
            }

            AddResolved(solutionPath, relative, projects, missing);
        }
    }

    private static void ReadSlnx(string solutionPath, List<string> projects, List<string> missing)
    {
        System.Xml.Linq.XDocument doc = System.Xml.Linq.XDocument.Load(solutionPath);
        int count = 0;
        foreach (System.Xml.Linq.XElement element in doc.Descendants("Project"))
        {
            if (count >= MaxProjects)
            {
                return;
            }

            string? relative = (string?)element.Attribute("Path");
            if (string.IsNullOrWhiteSpace(relative) || !IsProjectPath(relative))
            {
                continue;
            }

            AddResolved(solutionPath, relative, projects, missing);
            count++;
        }
    }

    private static bool TryReadProjectPath(string line, out string relativePath)
    {
        relativePath = string.Empty;
        if (!line.StartsWith("Project(", StringComparison.Ordinal))
        {
            return false;
        }

        int equals = line.IndexOf('=');
        if (equals < 0)
        {
            return false;
        }

        // Project("{type}") = "Name", "path\\App.csproj", "{guid}"
        string? path = QuotedField(line, equals, fieldIndex: 1);
        if (path is null || !IsProjectPath(path))
        {
            return false;
        }

        relativePath = path;
        return true;
    }

    private static string? QuotedField(string line, int startAt, int fieldIndex)
    {
        int search = startAt;
        string? current = null;
        for (int field = 0; field <= fieldIndex; field++)
        {
            int start = line.IndexOf('"', search);
            if (start < 0)
            {
                return null;
            }

            int end = line.IndexOf('"', start + 1);
            if (end < 0)
            {
                return null;
            }

            current = line.Substring(start + 1, end - start - 1);
            search = end + 1;
        }

        return current;
    }

    private static void AddResolved(string solutionPath, string relative, List<string> projects, List<string> missing)
    {
        string? directory = Path.GetDirectoryName(solutionPath);
        string normalized = relative.Replace('\\', Path.DirectorySeparatorChar);
        string full = directory is null
            ? Path.GetFullPath(normalized)
            : Path.GetFullPath(Path.Combine(directory, normalized));

        if (File.Exists(full))
        {
            projects.Add(full);
            return;
        }

        missing.Add(full);
    }

    private static bool IsProjectPath(string path)
    {
        string ext = Path.GetExtension(path);
        return ext.Equals(".csproj", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".fsproj", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".vbproj", StringComparison.OrdinalIgnoreCase);
    }
}
