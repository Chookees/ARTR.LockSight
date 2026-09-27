using ARTR.LockSight.Cli;
using ARTR.LockSight.Drift;
using ARTR.LockSight.Explain;
using ARTR.LockSight.Fix;
using ARTR.LockSight.Reporting;

namespace ARTR.LockSight;

/// <summary>
/// Entry point for the artr-locksight dotnet tool.
/// Local NuGet lockfile doctor — not a Dependabot replacement.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (!ArgParser.TryParse(args, out AppOptions options, out string error))
        {
            Console.Error.WriteLine($"error: {error}");
            WriteHelp(Console.Error);
            return 2;
        }

        try
        {
            return options.Command switch
            {
                AppCommand.Help => WriteHelpAndExit(),
                AppCommand.Explain => RunExplain(options),
                AppCommand.Fix => RunFix(options),
                AppCommand.Drift => RunDrift(options),
                _ => WriteHelpAndExit(),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 2;
        }
    }

    private static int RunExplain(AppOptions options)
    {
        Nu1004Explainer.WriteExplanation(options.ExplainTopic, Console.Out);
        return 0;
    }

    private static int RunFix(AppOptions options)
    {
        return RestoreFixer.RunForceEvaluate(options.TargetPath, Console.Out, Console.Error);
    }

    private static int RunDrift(AppOptions options)
    {
        IReadOnlyList<DriftFinding> findings = DriftDetector.Scan(options.TargetPath);
        DriftReporter.WriteReport(findings, Console.Out, options.CiMode);

        int exitCode = 0;
        if (options.CiMode && findings.Count > 0)
        {
            exitCode = 1;
            Console.Error.WriteLine(
                $"ARTR.LockSight --ci: exiting {exitCode} because {findings.Count} drift finding(s) were reported.");
        }

        if (options.FixRequested)
        {
            Console.Out.WriteLine();
            int fixCode = RestoreFixer.RunForceEvaluate(options.TargetPath, Console.Out, Console.Error);
            if (fixCode != 0)
            {
                return fixCode;
            }

            // Re-scan after fix when in CI so the pipeline still fails if drift remains.
            if (options.CiMode)
            {
                IReadOnlyList<DriftFinding> after = DriftDetector.Scan(options.TargetPath);
                DriftReporter.WriteReport(after, Console.Out, ciMode: true);
                if (after.Count > 0)
                {
                    Console.Error.WriteLine(
                        $"ARTR.LockSight --ci: still {after.Count} finding(s) after --fix.");
                    return 1;
                }

                return 0;
            }
        }

        return exitCode;
    }

    private static int WriteHelpAndExit()
    {
        WriteHelp(Console.Out);
        return 0;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("ARTR.LockSight — local NuGet lockfile doctor (packages.lock.json)");
        writer.WriteLine();
        writer.WriteLine("Usage:");
        writer.WriteLine("  artr-locksight drift [path] [--ci] [--fix]");
        writer.WriteLine("  artr-locksight explain|why [topic]");
        writer.WriteLine("  artr-locksight fix [path]");
        writer.WriteLine("  artr-locksight --fix [path]");
        writer.WriteLine("  artr-locksight --help");
        writer.WriteLine();
        writer.WriteLine("Commands:");
        writer.WriteLine("  drift     Scan for lockfile drift (pin-drift, multi-TFM, ProjectReference).");
        writer.WriteLine("  explain   Human-readable NU1004 / RestoreLockedMode guide (alias: why).");
        writer.WriteLine("  fix       Run dotnet restore --force-evaluate to regenerate lockfiles.");
        writer.WriteLine();
        writer.WriteLine("Flags:");
        writer.WriteLine("  --ci      Non-zero exit when drift is found; CI-friendly annotations.");
        writer.WriteLine("  --fix     After drift (or alone), wrap restore --force-evaluate.");
        writer.WriteLine("  --path    Explicit solution/project/directory (also accepted as positional).");
        writer.WriteLine();
        writer.WriteLine("This tool diagnoses locked restore failures locally. It does not replace Dependabot.");
    }
}
