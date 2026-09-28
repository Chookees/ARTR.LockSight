namespace ARTR.LockSight.Drift;

/// <summary>
/// Resolves NuGetLockFilePath without a full MSBuild evaluation.
/// Expands the three properties projects actually use for this setting.
/// Any other $(...) falls back to packages.lock.json beside the project.
/// </summary>
internal static class LockfilePathResolver
{
    public static bool TryResolve(
        string projectDirectory,
        string projectName,
        string? raw,
        string? definedInFile,
        out string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);

        string fallback = Path.Combine(projectDirectory, "packages.lock.json");
        if (string.IsNullOrWhiteSpace(raw))
        {
            path = fallback;
            return true;
        }

        string thisFileDirectory = projectDirectory;
        if (!string.IsNullOrWhiteSpace(definedInFile))
        {
            thisFileDirectory = Path.GetDirectoryName(definedInFile) ?? projectDirectory;
        }

        if (!thisFileDirectory.EndsWith(Path.DirectorySeparatorChar))
        {
            thisFileDirectory += Path.DirectorySeparatorChar;
        }

        string expanded = raw.Trim().Replace('\\', Path.DirectorySeparatorChar);
        expanded = ReplaceToken(expanded, "$(MSBuildProjectDirectory)", projectDirectory);
        expanded = ReplaceToken(expanded, "$(MSBuildProjectName)", projectName);
        expanded = ReplaceToken(expanded, "$(MSBuildThisFileDirectory)", thisFileDirectory);
        if (expanded.Contains("$(", StringComparison.Ordinal))
        {
            path = fallback;
            return false;
        }

        path = Path.IsPathRooted(expanded)
            ? Path.GetFullPath(expanded)
            : Path.GetFullPath(Path.Combine(projectDirectory, expanded));
        return true;
    }

    private static string ReplaceToken(string value, string token, string replacement)
    {
        int index = value.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return value;
        }

        return string.Concat(value.AsSpan(0, index), replacement, value.AsSpan(index + token.Length));
    }
}
