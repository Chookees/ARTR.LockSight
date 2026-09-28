namespace ARTR.LockSight.Drift;

/// <summary>
/// Categorized reason a lockfile entry disagrees with project/CPM pins.
/// </summary>
public enum DriftReason
{
    None = 0,
    PinDrift = 1,
    MultiTfmMismatch = 2,
    ProjectReferenceLockIssue = 3,
    MissingLockfile = 4,
    OrphanLockEntry = 5,
}
