namespace ARTR.LockSight.Cli;

/// <summary>
/// Minimal argv parser. Avoids heavy CLI frameworks so control flow stays explicit and bounded.
/// </summary>
public static class ArgParser
{
    private const int MaxArgs = 64;

    /// <summary>
    /// Parses argv into <see cref="AppOptions"/>. Returns false when argv is invalid.
    /// </summary>
    public static bool TryParse(string[] args, out AppOptions options, out string error)
    {
        options = new AppOptions(AppCommand.Help, Environment.CurrentDirectory, false, false, "nu1004");
        error = string.Empty;

        if (args is null)
        {
            error = "Arguments array must not be null.";
            return false;
        }

        if (args.Length > MaxArgs)
        {
            error = $"Too many arguments (max {MaxArgs}).";
            return false;
        }

        if (args.Length == 0)
        {
            options = new AppOptions(AppCommand.Help, Environment.CurrentDirectory, false, false, "nu1004");
            return true;
        }

        AppCommand command = AppCommand.None;
        string targetPath = Environment.CurrentDirectory;
        bool ciMode = false;
        bool fixRequested = false;
        string explainTopic = "nu1004";
        bool pathSeen = false;

        for (int i = 0; i < args.Length; i++)
        {
            string token = args[i];
            if (string.IsNullOrWhiteSpace(token))
            {
                error = "Empty argument is not allowed.";
                return false;
            }

            if (token is "-h" or "--help" or "help")
            {
                command = AppCommand.Help;
                continue;
            }

            if (token is "--ci")
            {
                ciMode = true;
                continue;
            }

            if (token is "--fix")
            {
                fixRequested = true;
                // Bare --fix without a verb means "run fix".
                if (command == AppCommand.None)
                {
                    command = AppCommand.Fix;
                }

                continue;
            }

            if (token is "-p" or "--path")
            {
                if (i + 1 >= args.Length)
                {
                    error = $"{token} requires a path value.";
                    return false;
                }

                i++;
                targetPath = args[i];
                pathSeen = true;
                continue;
            }

            if (IsVerb(token, out AppCommand verb))
            {
                if (command != AppCommand.None && command != AppCommand.Help)
                {
                    error = $"Unexpected second command '{token}'.";
                    return false;
                }

                command = verb;
                continue;
            }

            // Positional path (first non-flag after the verb).
            if (!pathSeen && !token.StartsWith('-'))
            {
                if (command is AppCommand.Explain)
                {
                    explainTopic = token;
                    continue;
                }

                targetPath = token;
                pathSeen = true;
                continue;
            }

            error = $"Unrecognized argument '{token}'.";
            return false;
        }

        if (command == AppCommand.None)
        {
            // Flags alone: --ci implies drift; --fix alone already set Fix above.
            command = ciMode ? AppCommand.Drift : AppCommand.Help;
        }

        // drift --fix: run drift then fix (handled by Program).
        options = new AppOptions(command, targetPath, ciMode, fixRequested, explainTopic);
        return true;
    }

    private static bool IsVerb(string token, out AppCommand command)
    {
        command = AppCommand.None;
        string normalized = token.Trim().ToLowerInvariant();

        if (normalized is "drift")
        {
            command = AppCommand.Drift;
            return true;
        }

        if (normalized is "explain" or "why")
        {
            command = AppCommand.Explain;
            return true;
        }

        if (normalized is "fix")
        {
            command = AppCommand.Fix;
            return true;
        }

        return false;
    }
}
