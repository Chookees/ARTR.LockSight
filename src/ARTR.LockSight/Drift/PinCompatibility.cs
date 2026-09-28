using NuGet.Versioning;

namespace ARTR.LockSight.Drift;

/// <summary>
/// Compares a PackageReference version with a lockfile row the way NuGet does.
/// A bare version such as 13.0.3 is the range [13.0.3, ). Drift is a requested-range
/// mismatch or a resolved version that falls outside the pin — not a raw string compare.
/// </summary>
internal static class PinCompatibility
{
    public static bool IsCompatible(string pinned, string resolved, string requested, out string detail)
    {
        ArgumentNullException.ThrowIfNull(pinned);
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(requested);
        detail = string.Empty;

        if (!VersionRange.TryParse(pinned, out VersionRange? pinRange))
        {
            detail = $"Pin '{pinned}' is not a NuGet version range.";
            return false;
        }

        if (!NuGetVersion.TryParse(resolved, out NuGetVersion? resolvedVersion))
        {
            detail = $"Lockfile resolved value '{resolved}' is not a NuGet version.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(requested))
        {
            if (!VersionRange.TryParse(requested, out VersionRange? requestedRange))
            {
                detail = $"Lockfile requested value '{requested}' is not a NuGet version range.";
                return false;
            }

            if (!RangesEqual(pinRange, requestedRange))
            {
                detail = $"Pin {pinned} is range {pinRange} but the lockfile requested {requestedRange} (resolved {resolved}).";
                return false;
            }
        }

        if (!pinRange.Satisfies(resolvedVersion))
        {
            detail = $"Resolved {resolved} does not satisfy pin {pinRange}.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// True when a non-direct lock row's resolved version sits inside its own requested range.
    /// </summary>
    public static bool RequestedContainsResolved(string requested, string resolved)
    {
        if (string.IsNullOrWhiteSpace(requested) || string.IsNullOrWhiteSpace(resolved))
        {
            return false;
        }

        if (!VersionRange.TryParse(requested, out VersionRange? range))
        {
            return false;
        }

        if (!NuGetVersion.TryParse(resolved, out NuGetVersion? version))
        {
            return false;
        }

        return range.Satisfies(version);
    }

    private static bool RangesEqual(VersionRange left, VersionRange right)
    {
        // Equals compares bounds and float metadata, not the original text.
        return left.Equals(right);
    }
}
