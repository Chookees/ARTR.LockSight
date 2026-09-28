namespace ARTR.LockSight.Cli;

/// <summary>
/// Top-level verb. Dispatch stays a flat switch.
/// </summary>
public enum AppCommand
{
    None = 0,
    Help = 1,
    Drift = 2,
    Explain = 3,
    Fix = 4,
    Diff = 5,
    Version = 6,
}
