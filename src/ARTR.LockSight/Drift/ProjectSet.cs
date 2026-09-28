namespace ARTR.LockSight.Drift;

/// <summary>
/// Projects selected by a directory walk or a solution file.
/// </summary>
public sealed class ProjectSet
{
    public string OriginPath { get; }
    public IReadOnlyList<string> Projects { get; }
    public IReadOnlyList<string> MissingProjects { get; }

    public ProjectSet(string originPath, IReadOnlyList<string> projects, IReadOnlyList<string> missingProjects)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originPath);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(missingProjects);

        OriginPath = originPath;
        Projects = projects;
        MissingProjects = missingProjects;
    }
}
