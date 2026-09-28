using ARTR.LockSight.Cli;
using ARTR.LockSight.Drift;
using ARTR.LockSight.Evaluate;
using ARTR.LockSight.Explain;
using ARTR.LockSight.Fix;
using ARTR.LockSight.Reporting;

namespace ARTR.LockSight;

/// <summary>
/// Entry point for the artr-locksight dotnet tool.
/// Exit 0: success. Exit 1: --ci blocking drift, diff changes, or a failed locked restore.
/// Exit 2: usage or runtime error. verify follows NuGet even without --ci.
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
        if (options.Evaluate)
        {
            return RunEvaluate(options);
        }

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

    private static int RunEvaluate(AppOptions options)
    {
        IReadOnlyList<DriftFinding> staticFindings = DriftDetector.Scan(options.TargetPath);
        RestoreRun run = RestoreFixer.RunLockedMode(options.TargetPath, Console.Out, Console.Error);
        if (run.ExitCode == 0)
        {
            return ReportUnconfirmed(options, staticFindings);
        }

        IReadOnlyList<RestoreDiagnostic> diagnostics = Nu1004Parser.Parse(run.Transcript);
        IReadOnlyList<DriftFinding> rows = EvaluateReporter.BuildFailureFindings(diagnostics, staticFindings);
        WriteDrift(options, rows);
        Console.Error.WriteLine($"ARTR.LockSight verify: locked restore failed with exit code {run.ExitCode}.");
        return run.ExitCode;
    }

    private static int ReportUnconfirmed(AppOptions options, IReadOnlyList<DriftFinding> staticFindings)
    {
        Console.Out.WriteLine("ARTR.LockSight verify: locked restore succeeded.");
        if (staticFindings.Count == 0)
        {
            return 0;
        }

        Console.Out.WriteLine("Static findings were not confirmed by NuGet and do not fail this run.");
        WriteDrift(options, staticFindings);
        return 0;
    }

    private static int RunDiff(AppOptions options)
    {
        IReadOnlyList<LockDiffEntry> changes = options.GitDiff
            ? GitLockDiff.CompareToRevision(options.TargetPath, options.Revision)
            : LockfileDiffer.Compare(options.TargetPath, options.ComparePath);
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
        WriteUsage(writer);
        WriteCommandHelp(writer);
        WriteFlagHelp(writer);
    }

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("Usage:");
        writer.WriteLine("  artr-locksight drift|scan [path] [--ci] [--strict] [--fix] [--evaluate]");
        writer.WriteLine("  artr-locksight verify [path] [--format text|json]");
        writer.WriteLine("  artr-locksight diff <left> <right> [--ci]");
        writer.WriteLine("  artr-locksight diff --git [path] [--revision HEAD]");
        writer.WriteLine("  artr-locksight explain|why [topic]");
        writer.WriteLine("  artr-locksight fix [path]");
        writer.WriteLine("  artr-locksight --version");
        writer.WriteLine();
    }

    private static void WriteCommandHelp(TextWriter writer)
    {
        writer.WriteLine("Commands:");
        writer.WriteLine("  drift     Static scan (pins, TFMs, project references, orphans, .slnf).");
        writer.WriteLine("  verify    dotnet restore --locked-mode. NuGet decides pass or fail.");
        writer.WriteLine("  diff      Compare two lock trees, or the working tree to a git revision.");
        writer.WriteLine("  explain   NU1004 / RestoreLockedMode guide (alias: why).");
        writer.WriteLine("  fix       Run dotnet restore --force-evaluate to regenerate lockfiles.");
        writer.WriteLine();
    }

    private static void WriteFlagHelp(TextWriter writer)
    {
        writer.WriteLine("Flags:");
        writer.WriteLine("  --evaluate  With drift, same check as verify.");
        writer.WriteLine("  --git       With diff, compare to a git revision (default HEAD).");
        writer.WriteLine("  --revision  Git revision for diff --git. Letters, digits, . _ / ~ ^ -.");
        writer.WriteLine("  --ci        Exit 1 when blocking drift or diff changes are found.");
        writer.WriteLine("  --strict    With --ci, warnings also fail the static scan.");
        writer.WriteLine("  --fix       With drift, regenerate lockfiles after the report.");
        writer.WriteLine("  --format    text (default) or json. --json is the same as --format json.");
        writer.WriteLine("  --path      Solution, .slnf, project, or directory. Also a positional.");
        writer.WriteLine();
        writer.WriteLine("NuGetLockFilePath may use $(MSBuildProjectDirectory), $(MSBuildProjectName),");
        writer.WriteLine("or $(MSBuildThisFileDirectory). Any other $(...) warns and falls back.");
        writer.WriteLine("verify exits with NuGet's code even without --ci. A successful locked restore");
        writer.WriteLine("stays 0 when static findings were not confirmed. Exit 2 is usage or runtime.");
        writer.WriteLine("This tool diagnoses locked restore locally. It does not replace Dependabot.");
    }
}
