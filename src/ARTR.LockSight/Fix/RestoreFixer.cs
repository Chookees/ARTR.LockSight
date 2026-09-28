using System.Diagnostics;

namespace ARTR.LockSight.Fix;

/// <summary>
/// Result of one dotnet restore invocation, including the text NU1004 is parsed from.
/// </summary>
public readonly struct RestoreRun
{
    public int ExitCode { get; }
    public string Transcript { get; }

    public RestoreRun(int exitCode, string transcript)
    {
        ExitCode = exitCode;
        Transcript = transcript;
    }
}

/// <summary>
/// Wraps dotnet restore. Child output is read asynchronously so a full pipe cannot deadlock.
/// </summary>
public static class RestoreFixer
{
    private const int DefaultTimeoutMs = 600_000;

    /// <summary>
    /// Regenerates lockfiles with <c>dotnet restore --force-evaluate</c>.
    /// </summary>
    public static int RunForceEvaluate(string targetPath, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);
        if (!TryCreate(targetPath, stdout, stderr, forceEvaluate: true, out ProcessStartInfo? startInfo))
        {
            return 2;
        }

        stdout.WriteLine("ARTR.LockSight fix: regenerating lockfiles via NuGet force-evaluate.");
        stdout.WriteLine("  Command: dotnet " + string.Join(' ', startInfo.ArgumentList));
        stdout.WriteLine();
        RestoreRun run = Execute(startInfo, stdout, stderr);
        if (run.ExitCode == 0)
        {
            stdout.WriteLine();
            stdout.WriteLine("ARTR.LockSight fix: restore --force-evaluate completed successfully.");
            stdout.WriteLine("Review and commit any updated packages.lock.json files.");
            return 0;
        }

        stderr.WriteLine($"ARTR.LockSight fix: restore failed with exit code {run.ExitCode}.");
        return run.ExitCode;
    }

    /// <summary>
    /// Runs <c>dotnet restore --locked-mode</c>. Exit 0 means NuGet accepts the lockfiles.
    /// </summary>
    public static RestoreRun RunLockedMode(string targetPath, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);
        if (!TryCreate(targetPath, stdout, stderr, forceEvaluate: false, out ProcessStartInfo? startInfo))
        {
            return new RestoreRun(2, string.Empty);
        }

        stdout.WriteLine("ARTR.LockSight verify: checking lockfiles with NuGet.");
        stdout.WriteLine("  Command: dotnet " + string.Join(' ', startInfo.ArgumentList));
        stdout.WriteLine();
        return Execute(startInfo, stdout, stderr);
    }

    internal static string ResolveRestoreTarget(string fullPath)
    {
        if (File.Exists(fullPath))
        {
            return fullPath;
        }

        string[] solutions = Directory.GetFiles(fullPath, "*.sln");
        if (solutions.Length == 1)
        {
            return solutions[0];
        }

        string[] slnx = Directory.GetFiles(fullPath, "*.slnx");
        if (slnx.Length == 1)
        {
            return slnx[0];
        }

        return fullPath;
    }

    internal static void AddForceEvaluateArguments(ProcessStartInfo startInfo, string restoreTarget)
    {
        AddCommonArguments(startInfo, restoreTarget);
        startInfo.ArgumentList.Add("--force-evaluate");
    }

    internal static void AddLockedModeArguments(ProcessStartInfo startInfo, string restoreTarget)
    {
        AddCommonArguments(startInfo, restoreTarget);
        startInfo.ArgumentList.Add("--locked-mode");
    }

    private static void AddCommonArguments(ProcessStartInfo startInfo, string restoreTarget)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentException.ThrowIfNullOrWhiteSpace(restoreTarget);
        startInfo.ArgumentList.Add("restore");
        startInfo.ArgumentList.Add(restoreTarget);
        startInfo.ArgumentList.Add("--nologo");
    }

    private static bool TryCreate(
        string targetPath,
        TextWriter stdout,
        TextWriter stderr,
        bool forceEvaluate,
        out ProcessStartInfo startInfo)
    {
        startInfo = new ProcessStartInfo();
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            stderr.WriteLine("Target path is required.");
            return false;
        }

        string fullPath = Path.GetFullPath(targetPath);
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            stderr.WriteLine($"Target path not found: {fullPath}");
            return false;
        }

        string restoreTarget = ResolveRestoreTarget(fullPath);
        startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Directory.Exists(fullPath)
                ? fullPath
                : Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory,
        };
        if (forceEvaluate)
        {
            AddForceEvaluateArguments(startInfo, restoreTarget);
        }
        else
        {
            AddLockedModeArguments(startInfo, restoreTarget);
        }

        _ = stdout;
        return true;
    }

    private static RestoreRun Execute(ProcessStartInfo startInfo, TextWriter stdout, TextWriter stderr)
    {
        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                stderr.WriteLine("Failed to start 'dotnet' process.");
                return new RestoreRun(2, string.Empty);
            }
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"Failed to start 'dotnet': {ex.Message}");
            return new RestoreRun(2, ex.Message);
        }

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(DefaultTimeoutMs))
        {
            TryKill(process);
            stderr.WriteLine($"dotnet restore timed out after {DefaultTimeoutMs} ms.");
            return new RestoreRun(2, string.Empty);
        }

        string output = stdoutTask.GetAwaiter().GetResult();
        string error = stderrTask.GetAwaiter().GetResult();
        WriteCaptured(stdout, output);
        WriteCaptured(stderr, error);
        return new RestoreRun(process.ExitCode, output + "\n" + error);
    }

    private static void WriteCaptured(TextWriter writer, string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            writer.WriteLine(text.TrimEnd());
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
    }
}
