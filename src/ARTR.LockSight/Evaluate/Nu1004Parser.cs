namespace ARTR.LockSight.Evaluate;

/// <summary>
/// One NU1004 line from <c>dotnet restore --locked-mode</c>.
/// </summary>
internal sealed class RestoreDiagnostic
{
    public string ProjectPath { get; }
    public string Message { get; }

    public RestoreDiagnostic(string projectPath, string message)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ProjectPath = projectPath;
        Message = message;
    }
}

/// <summary>
/// Pulls NU1004 lines out of restore output. NuGet's message names the project, not the package.
/// </summary>
internal static class Nu1004Parser
{
    private const int MaxLines = 20_000;
    private const string Marker = "error NU1004";

    public static IReadOnlyList<RestoreDiagnostic> Parse(string transcript)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        var found = new List<RestoreDiagnostic>();
        using var reader = new StringReader(transcript);
        int lines = 0;
        while (reader.ReadLine() is string line && lines < MaxLines)
        {
            lines++;
            if (TryParseLine(line, out RestoreDiagnostic? diagnostic))
            {
                found.Add(diagnostic);
            }
        }

        return found;
    }

    private static bool TryParseLine(string line, out RestoreDiagnostic diagnostic)
    {
        diagnostic = new RestoreDiagnostic("-", "NU1004");
        int marker = line.IndexOf(Marker, StringComparison.Ordinal);
        if (marker < 0)
        {
            return false;
        }

        string message = line[(marker + Marker.Length)..].TrimStart(':', ' ', '\t');
        if (message.Length == 0)
        {
            message = "The packages lock file is inconsistent with the project dependencies.";
        }

        string project = line[..marker].Trim();
        if (project.EndsWith(':'))
        {
            project = project[..^1].Trim();
        }

        int paren = project.LastIndexOf('(');
        if (paren > 0 && project.EndsWith(')'))
        {
            project = project[..paren].Trim();
        }

        diagnostic = new RestoreDiagnostic(project, message);
        return true;
    }
}
