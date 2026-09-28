using System.Diagnostics;

namespace ARTR.LockSight.Fix;

/// <summary>
/// Wraps <c>dotnet restore --force-evaluate</c>. Output is read asynchronously so a full
/// pipe buffer cannot deadlock the child process.
/// </summary>
public static class RestoreFixer
{
    private const int DefaultTimeoutMs = 600_000;

    /// <summary>
    /// Runs restore against a solution, project, or directory. Returns the process exit code.
    /// </summary>
    public static int RunForceEvaluate(string targetPath, TextWriter stdout, TextWriter stderr)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        string fullPath = Path.GetFullPath(targetPath);
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            stderr.WriteLine($"Target path not found: {fullPath}");
            return 2;
        }

        string restoreTarget = ResolveRestoreTarget(fullPath);
        var startInfo = new ProcessStartInfo
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
        AddRestoreArguments(startInfo, restoreTarget);
        stdout.WriteLine("ARTR.LockSight fix: regenerating lockfiles via NuGet force-evaluate.");
        stdout.WriteLine("  Command: dotnet " + string.Join(' ', startInfo.ArgumentList));
        stdout.WriteLine();
        return Run(startInfo, stdout, stderr);
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

    internal static void AddRestoreArguments(ProcessStartInfo startInfo, string restoreTarget)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentException.ThrowIfNullOrWhiteSpace(restoreTarget);
        startInfo.ArgumentList.Add("restore");
        startInfo.ArgumentList.Add(restoreTarget);
        startInfo.ArgumentList.Add("--force-evaluate");
        startInfo.ArgumentList.Add("--nologo");
    }

    private static int Run(ProcessStartInfo startInfo, TextWriter stdout, TextWriter stderr)
    {
        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                stderr.WriteLine("Failed to start 'dotnet' process.");
                return 2;
            }
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"Failed to start 'dotnet': {ex.Message}");
            return 2;
        }

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        bool exited = process.WaitForExit(DefaultTimeoutMs);
        if (!exited)
        {
            TryKill(process);
            stderr.WriteLine($"dotnet restore timed out after {DefaultTimeoutMs} ms.");
            return 2;
        }

        WriteCaptured(stdout, stdoutTask);
        WriteCaptured(stderr, stderrTask);
        stdout.WriteLine();
        if (process.ExitCode == 0)
        {
            stdout.WriteLine("ARTR.LockSight fix: restore --force-evaluate completed successfully.");
            stdout.WriteLine("Review and commit any updated packages.lock.json files.");
            return 0;
        }

        stderr.WriteLine($"ARTR.LockSight fix: restore failed with exit code {process.ExitCode}.");
        return process.ExitCode;
    }

    private static void WriteCaptured(TextWriter writer, Task<string> task)
    {
        string text = task.GetAwaiter().GetResult();
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
