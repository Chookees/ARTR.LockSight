using System.Diagnostics;

namespace ARTR.LockSight.Fix;

/// <summary>
/// Wraps `dotnet restore --force-evaluate` with safe defaults and clear output.
/// </summary>
public static class RestoreFixer
{
    private const int DefaultTimeoutMs = 600_000;

    /// <summary>
    /// Runs restore --force-evaluate against a solution, project, or directory.
    /// Returns the process exit code (0 = success).
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
        string args = BuildArguments(restoreTarget);

        stdout.WriteLine("ARTR.LockSight fix: regenerating lockfiles via NuGet force-evaluate.");
        stdout.WriteLine($"  Command: dotnet {args}");
        stdout.WriteLine();

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Directory.Exists(fullPath)
                ? fullPath
                : Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory,
        };

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

        string standardOut = process.StandardOutput.ReadToEnd();
        string standardErr = process.StandardError.ReadToEnd();
        bool exited = process.WaitForExit(DefaultTimeoutMs);
        if (!exited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Already exited.
            }

            stderr.WriteLine($"dotnet restore timed out after {DefaultTimeoutMs} ms.");
            return 2;
        }

        if (!string.IsNullOrWhiteSpace(standardOut))
        {
            stdout.WriteLine(standardOut.TrimEnd());
        }

        if (!string.IsNullOrWhiteSpace(standardErr))
        {
            stderr.WriteLine(standardErr.TrimEnd());
        }

        stdout.WriteLine();
        if (process.ExitCode == 0)
        {
            stdout.WriteLine("ARTR.LockSight fix: restore --force-evaluate completed successfully.");
            stdout.WriteLine("Review and commit any updated packages.lock.json files.");
        }
        else
        {
            stderr.WriteLine($"ARTR.LockSight fix: restore failed with exit code {process.ExitCode}.");
        }

        return process.ExitCode;
    }

    private static string ResolveRestoreTarget(string fullPath)
    {
        if (File.Exists(fullPath))
        {
            return fullPath;
        }

        // Prefer a solution file in the directory when present.
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

        // Directory restore lets the SDK discover projects.
        return fullPath;
    }

    private static string BuildArguments(string restoreTarget)
    {
        // --force-evaluate regenerates lockfiles; --ignore-failed-sources keeps local diagnosis moving
        // when an optional feed is briefly unreachable (safe default for a doctor tool).
        return $"restore \"{restoreTarget}\" --force-evaluate --nologo";
    }
}
