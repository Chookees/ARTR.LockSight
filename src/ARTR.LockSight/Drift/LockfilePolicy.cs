namespace ARTR.LockSight.Drift;

/// <summary>
/// How this project relates to packages.lock.json.
/// A null <see cref="RestorePackagesWithLockFile"/> means the property was not set to a literal true/false.
/// </summary>
public sealed class LockfilePolicy
{
    public bool? RestorePackagesWithLockFile { get; }
    public bool RestoreLockedMode { get; }
    public string? LockfilePath { get; }

    public LockfilePolicy(bool? restorePackagesWithLockFile, bool restoreLockedMode, string? lockfilePath)
    {
        RestorePackagesWithLockFile = restorePackagesWithLockFile;
        RestoreLockedMode = restoreLockedMode;
        LockfilePath = lockfilePath;
    }

    /// <summary>
    /// True when locked restore would consult a lockfile.
    /// An explicit false wins over a leftover packages.lock.json on disk.
    /// </summary>
    public bool ExpectsLockfile
    {
        get
        {
            if (RestoreLockedMode)
            {
                return true;
            }

            if (RestorePackagesWithLockFile == false)
            {
                return false;
            }

            if (RestorePackagesWithLockFile == true)
            {
                return true;
            }

            return LockfilePath is not null;
        }
    }
}
