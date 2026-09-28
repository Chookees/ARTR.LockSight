namespace ARTR.LockSight.Cli;

/// <summary>
/// Top-level verb selected by the user.
/// Kept as a small enum so dispatch stays a flat switch (Power of Ten: simple control flow).
/// </summary>
public enum AppCommand
{
    None = 0,
    Help = 1,
    Drift = 2,
    Explain = 3,
    Fix = 4,
}
