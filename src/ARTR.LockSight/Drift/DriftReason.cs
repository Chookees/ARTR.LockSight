namespace ARTR.LockSight.Drift;

/// <summary>
/// Why a project and its packages.lock.json disagree.
/// Warnings are real, but they often do not fail RestoreLockedMode by themselves.
/// </summary>
public enum DriftReason
{
    None = 0,
    PinDrift = 1,
    MultiTfmMismatch = 2,
    ProjectReferenceLockIssue = 3,
    MissingLockfile = 4,
    OrphanLockEntry = 5,
    MultiTfmVersionSkew = 6,
    StaleTfm = 7,
    LockfileInconsistent = 8,
    UnreadableInput = 9,
}
