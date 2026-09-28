namespace ARTR.LockSight.Explain;

/// <summary>
/// Human-readable explanation of NU1004 / RestoreLockedMode. This is a local doctor, not Dependabot.
/// </summary>
public static class Nu1004Explainer
{
    public static void WriteExplanation(string topic, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentNullException.ThrowIfNull(writer);

        string normalized = topic.Trim().ToLowerInvariant();
        if (normalized is not ("" or "nu1004" or "locked" or "restorelockedmode" or "lock"))
        {
            writer.WriteLine($"No built-in topic '{topic}'. Showing the default NU1004 / RestoreLockedMode guide:");
            writer.WriteLine();
        }

        WriteMeaning(writer);
        WriteCauses(writer);
        WriteNext(writer);
    }

    private static void WriteMeaning(TextWriter writer)
    {
        writer.WriteLine("ARTR.LockSight — why NU1004 / RestoreLockedMode fails");
        writer.WriteLine(new string('=', 60));
        writer.WriteLine();
        writer.WriteLine("What RestoreLockedMode means");
        writer.WriteLine("----------------------------");
        writer.WriteLine("When RestoreLockedMode=true (common in CI), NuGet restores the graph recorded in");
        writer.WriteLine("packages.lock.json. It does not silently retarget floating versions. If the project");
        writer.WriteLine("inputs no longer match that file, restore fails with NU1004.");
        writer.WriteLine();
        writer.WriteLine("A PackageReference Version=\"1.2.3\" is the NuGet range [1.2.3, ). The lockfile stores");
        writer.WriteLine("that range as \"requested\" and the chosen package as \"resolved\". Drift means the");
        writer.WriteLine("requested range changed, the resolved version falls outside the pin, a TFM section");
        writer.WriteLine("is missing, or a direct package was added or removed without updating the lockfile.");
        writer.WriteLine();
    }

    private static void WriteCauses(TextWriter writer)
    {
        writer.WriteLine("Common causes");
        writer.WriteLine("-------------");
        writer.WriteLine("1. Pin drift — a PackageReference, VersionOverride, or Directory.Packages.props");
        writer.WriteLine("   version changed, and packages.lock.json still records the old requested range.");
        writer.WriteLine("2. Missing or extra TFM — TargetFramework(s) changed and the lockfile sections did not.");
        writer.WriteLine("3. ProjectReference ripple — a referenced project expects a lockfile and does not have one.");
        writer.WriteLine("4. Orphan direct entry — a PackageReference was removed but the lockfile still lists it.");
        writer.WriteLine("5. Broken lock row — a package row has no contentHash, or a transitive resolved");
        writer.WriteLine("   version sits outside its requested range.");
        writer.WriteLine("6. Lockfile policy lives in Directory.Build.props, so a single project file looks fine");
        writer.WriteLine("   while the imported property still demands a lockfile.");
        writer.WriteLine();
    }

    private static void WriteNext(TextWriter writer)
    {
        writer.WriteLine("What to do next");
        writer.WriteLine("---------------");
        writer.WriteLine("1. artr-locksight drift <path> [--format json]");
        writer.WriteLine("   Offline scan. Errors are RestoreLockedMode risks. Warnings (cross-TFM skew,");
        writer.WriteLine("   stale TFM sections) are printed and do not fail --ci unless you pass --strict.");
        writer.WriteLine("2. artr-locksight diff <left> <right>");
        writer.WriteLine("   Compare two lockfiles or two directories without restoring.");
        writer.WriteLine("3. artr-locksight fix <path>");
        writer.WriteLine("   Runs: dotnet restore --force-evaluate. Commit the updated packages.lock.json files.");
        writer.WriteLine("4. artr-locksight drift <path> --ci");
        writer.WriteLine("   Exit 1 when blocking drift is found. Use this in pipelines and the GitHub Action.");
        writer.WriteLine();
        writer.WriteLine("How this differs from Dependabot");
        writer.WriteLine("--------------------------------");
        writer.WriteLine("Dependabot proposes dependency upgrades. ARTR.LockSight explains and detects why a");
        writer.WriteLine("locked restore would fail, on your machine or in CI. It does not open upgrade PRs");
        writer.WriteLine("and it does not audit the supply chain.");
        writer.WriteLine();
        writer.WriteLine("Static limits");
        writer.WriteLine("-------------");
        writer.WriteLine("Conditions other than a quoted TargetFramework check are not evaluated. Package");
        writer.WriteLine("references that arrive through arbitrary MSBuild imports (other than the nearest");
        writer.WriteLine("Directory.Build.props, Directory.Build.targets, and Directory.Packages.props) are");
        writer.WriteLine("not visible. Those cases can still need dotnet restore --force-evaluate.");
    }
}
