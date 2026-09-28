using ARTR.LockSight.Cli;
using ARTR.LockSight.Drift;
using ARTR.LockSight.Explain;
using ARTR.LockSight.Fix;
using ARTR.LockSight.Reporting;

namespace ARTR.LockSight;

/// <summary>
/// Entry point for the artr-locksight dotnet tool.
/// Exit 0: success. Exit 1: --ci found blocking drift or diff changes. Exit 2: usage or runtime error.
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
                AppCommand.Version => WriteVersion(),
                AppCommand.Explain => RunExplain(options),
                AppCommand.Fix => RunFix(options),
                AppCommand.Diff => RunDiff(options),
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

    private static int WriteVersion()
    {
        Console.Out.WriteLine($"ARTR.LockSight {ToolVersion.Current}");
        return 0;
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
        WriteDrift(options, findings);
        int exitCode = CiExit(options, DriftReporter.CountBlocking(findings, options.Strict), "drift finding(s)");
        if (!options.FixRequested)
        {
            return exitCode;
        }

        Console.Out.WriteLine();
        int fixCode = RestoreFixer.RunForceEvaluate(options.TargetPath, Console.Out, Console.Error);
        if (fixCode != 0 || !options.CiMode)
        {
            return fixCode != 0 ? fixCode : exitCode;
        }

        IReadOnlyList<DriftFinding> after = DriftDetector.Scan(options.TargetPath);
        WriteDrift(options, after);
        int still = DriftReporter.CountBlocking(after, options.Strict);
        if (still > 0)
        {
            Console.Error.WriteLine($"ARTR.LockSight --ci: still {still} blocking finding(s) after --fix.");
            return 1;
        }

        return 0;
    }

    private static int RunDiff(AppOptions options)
    {
        IReadOnlyList<LockDiffEntry> changes = LockfileDiffer.Compare(options.TargetPath, options.ComparePath);
        TextWriter? annotations = options.Format == OutputFormat.Json ? Console.Error : null;
        DiffReporter.WriteReport(changes, Console.Out, options.CiMode, options.Format, annotations);
        return CiExit(options, changes.Count, "diff change(s)");
    }

    private static void WriteDrift(AppOptions options, IReadOnlyList<DriftFinding> findings)
    {
        TextWriter? annotations = options.Format == OutputFormat.Json ? Console.Error : null;
        DriftReporter.WriteReport(findings, Console.Out, options.CiMode, options.Format, annotations);
    }

    private static int CiExit(AppOptions options, int blocking, string noun)
    {
        if (!options.CiMode || blocking == 0)
        {
            return 0;
        }

        Console.Error.WriteLine($"ARTR.LockSight --ci: exiting 1 because {blocking} blocking {noun} were reported.");
        return 1;
    }

    private static int WriteHelpAndExit()
    {
        WriteHelp(Console.Out);
        return 0;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine($"ARTR.LockSight {ToolVersion.Current} — local NuGet lockfile doctor (packages.lock.json)");
        writer.WriteLine();
        writer.WriteLine("Usage:");
        writer.WriteLine("  artr-locksight drift|scan [path] [--ci] [--strict] [--fix] [--format text|json]");
        writer.WriteLine("  artr-locksight diff <left> <right> [--ci] [--format text|json]");
        writer.WriteLine("  artr-locksight explain|why [topic]");
        writer.WriteLine("  artr-locksight fix [path]");
        writer.WriteLine("  artr-locksight --fix [path]");
        writer.WriteLine("  artr-locksight --version");
        writer.WriteLine();
        writer.WriteLine("Commands:");
        writer.WriteLine("  drift     Scan for lockfile drift (pins, TFMs, project references, orphans).");
        writer.WriteLine("  diff      Compare two lockfiles, projects, or directories. No restore.");
        writer.WriteLine("  explain   NU1004 / RestoreLockedMode guide (alias: why).");
        writer.WriteLine("  fix       Run dotnet restore --force-evaluate to regenerate lockfiles.");
        writer.WriteLine();
        writer.WriteLine("Flags:");
        writer.WriteLine("  --ci      Exit 1 when blocking drift or diff changes are found.");
        writer.WriteLine("  --strict  With --ci, warnings also fail the run.");
        writer.WriteLine("  --fix     With drift, regenerate lockfiles after the report.");
        writer.WriteLine("  --format  text (default) or json. --json is the same as --format json.");
        writer.WriteLine("  --path    Solution, project, or directory. Also accepted as a positional.");
        writer.WriteLine();
        writer.WriteLine("Exit codes: 0 success, 1 blocking findings in --ci, 2 usage or runtime error.");
        writer.WriteLine("This tool diagnoses locked restore locally. It does not replace Dependabot.");
    }
}
