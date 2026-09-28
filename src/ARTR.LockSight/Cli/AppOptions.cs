namespace ARTR.LockSight.Cli;

/// <summary>
/// Parsed CLI options. Immutable after construction to keep call sites predictable.
/// </summary>
public sealed class AppOptions
{
    public AppCommand Command { get; }
    public string TargetPath { get; }
    public bool CiMode { get; }
    public bool FixRequested { get; }
    public string ExplainTopic { get; }

    public AppOptions(
        AppCommand command,
        string targetPath,
        bool ciMode,
        bool fixRequested,
        string explainTopic)
    {
        ArgumentNullException.ThrowIfNull(targetPath);
        ArgumentNullException.ThrowIfNull(explainTopic);

        Command = command;
        TargetPath = targetPath;
        CiMode = ciMode;
        FixRequested = fixRequested;
        ExplainTopic = explainTopic;
    }
}
