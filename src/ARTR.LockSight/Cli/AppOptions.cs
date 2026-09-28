namespace ARTR.LockSight.Cli;

/// <summary>
/// Parsed CLI options. Properties are init-only so the parser is not forced past five parameters.
/// </summary>
public sealed class AppOptions
{
    public AppCommand Command { get; init; } = AppCommand.Help;
    public string TargetPath { get; init; } = "";
    public string ComparePath { get; init; } = "";
    public bool CiMode { get; init; }
    public bool FixRequested { get; init; }
    public bool Strict { get; init; }
    public bool Evaluate { get; init; }
    public bool GitDiff { get; init; }
    public string Revision { get; init; } = "HEAD";
    public OutputFormat Format { get; init; } = OutputFormat.Text;
    public string ExplainTopic { get; init; } = "nu1004";
}
