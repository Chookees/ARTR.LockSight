using System.Diagnostics;

namespace ARTR.LockSight.Drift;

/// <summary>
/// Compares working-tree packages.lock.json files with a git revision. HEAD is the usual revision.
/// </summary>
public static class GitLockDiff
{
    private const int MaxFiles = 2_000;
    private const int TimeoutMs = 60_000;

    public static IReadOnlyList<LockDiffEntry> CompareToRevision(string path, string revision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ValidateRevision(revision);

        string full = Path.GetFullPath(path);
        if (!File.Exists(full) && !Directory.Exists(full))
        {
            throw new DirectoryNotFoundException($"Target path not found: {full}");
        }

        string scope = Directory.Exists(full) ? full : Path.GetDirectoryName(full) ?? full;
        GitResult root = Run(scope, "rev-parse", "--show-toplevel");
        if (root.ExitCode != 0)
        {
            throw new InvalidDataException("diff --git needs a git repository. " + Trim(root.Stderr));
        }

        string toplevel = root.Stdout.Trim();
        string mirror = Path.Combine(Path.GetTempPath(), "artr-locksight-git", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(mirror);
        try
        {
            CopyRevisionLockfiles(toplevel, scope, revision, mirror);
            return LockfileDiffer.Compare(mirror, scope);
        }
        finally
        {
            TryDelete(mirror);
        }
    }

    private static void CopyRevisionLockfiles(string toplevel, string scope, string revision, string mirror)
    {
        GitResult list = Run(toplevel, "ls-tree", "-r", "--name-only", revision);
        if (list.ExitCode != 0)
        {
            throw new InvalidDataException($"git ls-tree {revision} failed. " + Trim(list.Stderr));
        }

        using var reader = new StringReader(list.Stdout);
        int count = 0;
        while (reader.ReadLine() is string relative && count < MaxFiles)
        {
            if (!relative.EndsWith("packages.lock.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string absolute = Path.GetFullPath(Path.Combine(toplevel, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsUnder(scope, absolute))
            {
                continue;
            }

            GitResult blob = Run(toplevel, "show", revision + ":" + relative.Replace('\\', '/'));
            if (blob.ExitCode != 0)
            {
                continue;
            }

            string dest = Path.Combine(mirror, Path.GetRelativePath(scope, absolute));
            string? destDir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            File.WriteAllText(dest, blob.Stdout);
            count++;
        }
    }

    private static bool IsUnder(string scope, string absolute)
    {
        string root = Path.GetFullPath(scope);
        if (string.Equals(root, absolute, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return absolute.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateRevision(string revision)
    {
        if (revision.Length == 0 || revision.Length > 128 || revision.StartsWith('-'))
        {
            throw new InvalidDataException($"Unsupported git revision '{revision}'.");
        }

        for (int i = 0; i < revision.Length; i++)
        {
            char c = revision[i];
            bool ok = char.IsLetterOrDigit(c) || c is '.' or '_' or '/' or '~' or '^' or '-';
            if (!ok)
            {
                throw new InvalidDataException($"Unsupported git revision '{revision}'.");
            }
        }
    }

    private static GitResult Run(string workingDirectory, string arg1, string arg2, string? arg3 = null, string? arg4 = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(arg1);
        startInfo.ArgumentList.Add(arg2);
        if (arg3 is not null)
        {
            startInfo.ArgumentList.Add(arg3);
        }

        if (arg4 is not null)
        {
            startInfo.ArgumentList.Add(arg4);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return new GitResult(2, string.Empty, "Failed to start git.");
            }
        }
        catch (Exception ex)
        {
            return new GitResult(2, string.Empty, ex.Message);
        }

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeoutMs))
        {
            TryKill(process);
            return new GitResult(2, string.Empty, "git timed out.");
        }

        return new GitResult(process.ExitCode, stdoutTask.GetAwaiter().GetResult(), stderrTask.GetAwaiter().GetResult());
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

    private static void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not a scan result.
        }
    }

    private static string Trim(string value)
    {
        string text = value.Trim();
        return text.Length <= 300 ? text : text[..300];
    }

    private readonly struct GitResult
    {
        public int ExitCode { get; }
        public string Stdout { get; }
        public string Stderr { get; }

        public GitResult(int exitCode, string stdout, string stderr)
        {
            ExitCode = exitCode;
            Stdout = stdout;
            Stderr = stderr;
        }
    }
}
