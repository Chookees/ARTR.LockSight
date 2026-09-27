namespace ARTR.LockSight.Explain;

/// <summary>
/// Human-readable explanations for NU1004 / RestoreLockedMode failures.
/// This is a local doctor — not a Dependabot replacement.
/// </summary>
public static class Nu1004Explainer
{
    public static void WriteExplanation(string topic, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentNullException.ThrowIfNull(writer);

        string normalized = topic.Trim().ToLowerInvariant();
        if (normalized is "" or "nu1004" or "locked" or "restorelockedmode" or "lock")
        {
            WriteNu1004(writer);
            return;
        }

        writer.WriteLine($"No built-in topic '{topic}'. Showing the default NU1004 / RestoreLockedMode guide:");
        writer.WriteLine();
        WriteNu1004(writer);
    }

    private static void WriteNu1004(TextWriter writer)
    {
        writer.WriteLine("ARTR.LockSight — why NU1004 / RestoreLockedMode fails");
        writer.WriteLine(new string('=', 60));
        writer.WriteLine();
        writer.WriteLine("What RestoreLockedMode means");
        writer.WriteLine("----------------------------");
        writer.WriteLine("When RestoreLockedMode=true (common in CI), NuGet must restore using the exact");
        writer.WriteLine("graph recorded in packages.lock.json. It will not silently re-evaluate floating");
        writer.WriteLine("versions or pick newer packages. If the lockfile no longer matches the project");
        writer.WriteLine("inputs, restore fails with NU1004 (assets file / lockfile out of date).");
        writer.WriteLine();
        writer.WriteLine("Common causes");
        writer.WriteLine("-------------");
        writer.WriteLine("1. Pin drift — a PackageReference or Directory.Packages.props version changed");
        writer.WriteLine("   but packages.lock.json was not regenerated.");
        writer.WriteLine("2. Multi-TFM mismatch — TargetFramework(s) changed, or the same package resolves");
        writer.WriteLine("   to different versions across TFMs in ways the lockfile does not cover.");
        writer.WriteLine("3. ProjectReference ripple — a referenced project enables lockfiles / CPM and its");
        writer.WriteLine("   lockfile is missing or stale, so the outer restore cannot stay locked.");
        writer.WriteLine("4. New PackageReference added without updating the lockfile.");
        writer.WriteLine("5. Switching between CPM and per-project Version attributes without re-locking.");
        writer.WriteLine();
        writer.WriteLine("What to do next");
        writer.WriteLine("---------------");
        writer.WriteLine("1. Locally run:  artr-locksight drift <path>");
        writer.WriteLine("   to see per-project / TFM / reason findings without touching NuGet caches.");
        writer.WriteLine("2. Regenerate lockfiles:  artr-locksight fix <path>");
        writer.WriteLine("   (wraps: dotnet restore --force-evaluate).");
        writer.WriteLine("3. Commit the updated packages.lock.json files.");
        writer.WriteLine("4. In CI, run:  artr-locksight drift <path> --ci");
        writer.WriteLine("   so pipelines fail fast with actionable lines when drift returns.");
        writer.WriteLine();
        writer.WriteLine("How this differs from Dependabot");
        writer.WriteLine("--------------------------------");
        writer.WriteLine("Dependabot proposes dependency upgrades. ARTR.LockSight is a local CLI doctor:");
        writer.WriteLine("it explains and detects why locked restore fails. It does not open PRs or audit");
        writer.WriteLine("the full supply chain.");
    }
}
