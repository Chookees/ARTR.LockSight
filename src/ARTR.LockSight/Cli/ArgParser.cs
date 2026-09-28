namespace ARTR.LockSight.Cli;

/// <summary>
/// Minimal argv parser. Control flow stays explicit and bounded; there is no CLI framework.
/// </summary>
public static class ArgParser
{
    private const int MaxArgs = 64;

    /// <summary>
    /// Parses argv. Returns false when argv is invalid; <paramref name="error"/> explains why.
    /// </summary>
    public static bool TryParse(string[] args, out AppOptions options, out string error)
    {
        options = new AppOptions();
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

        var state = new ParseState();
        for (int i = 0; i < args.Length; i++)
        {
            if (!Consume(args, ref i, state))
            {
                error = state.Error;
                return false;
            }
        }

        return Finish(state, out options, out error);
    }

    private static bool Consume(string[] args, ref int index, ParseState state)
    {
        string token = args[index];
        if (string.IsNullOrWhiteSpace(token))
        {
            state.Error = "Empty argument is not allowed.";
            return false;
        }

        if (TryApplyFlag(token, state))
        {
            return true;
        }

        if (TryConsumeValue(args, ref index, token, state))
        {
            return state.Error.Length == 0;
        }

        if (IsVerb(token, out AppCommand verb))
        {
            return AcceptVerb(token, verb, state);
        }

        return AcceptPositional(token, state);
    }

    private static bool TryApplyFlag(string token, ParseState state)
    {
        if (token is "-h" or "--help" or "help")
        {
            state.Command = AppCommand.Help;
            return true;
        }

        if (token is "--version")
        {
            state.Command = AppCommand.Version;
            return true;
        }

        if (token is "--ci")
        {
            state.CiMode = true;
            return true;
        }

        if (token is "--strict")
        {
            state.Strict = true;
            return true;
        }

        if (token is "--json")
        {
            state.Format = OutputFormat.Json;
            return true;
        }

        if (token is "--fix")
        {
            state.FixRequested = true;
            if (state.Command == AppCommand.None)
            {
                state.Command = AppCommand.Fix;
            }

            return true;
        }

        return false;
    }

    private static bool TryConsumeValue(string[] args, ref int index, string token, ParseState state)
    {
        if (token.StartsWith("--format=", StringComparison.Ordinal))
        {
            SetFormat(token["--format=".Length..], state);
            return true;
        }

        if (token is "--format")
        {
            return ReadValue(args, ref index, token, value => SetFormat(value, state), state);
        }

        if (token is "-p" or "--path" or "--left")
        {
            return ReadValue(args, ref index, token, value =>
            {
                state.TargetPath = value;
                state.PathSeen = true;
                return true;
            }, state);
        }

        if (token is "--right")
        {
            return ReadValue(args, ref index, token, value =>
            {
                state.ComparePath = value;
                state.CompareSeen = true;
                return true;
            }, state);
        }

        return false;
    }

    private static bool ReadValue(string[] args, ref int index, string token, Func<string, bool> apply, ParseState state)
    {
        if (index + 1 >= args.Length)
        {
            state.Error = $"{token} requires a value.";
            return true;
        }

        index++;
        apply(args[index]);
        return true;
    }

    private static bool SetFormat(string value, ParseState state)
    {
        if (value.Equals("text", StringComparison.OrdinalIgnoreCase))
        {
            state.Format = OutputFormat.Text;
            return true;
        }

        if (value.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            state.Format = OutputFormat.Json;
            return true;
        }

        state.Error = $"Unknown format '{value}'. Use text or json.";
        return false;
    }

    private static bool AcceptVerb(string token, AppCommand verb, ParseState state)
    {
        if (state.Command is not AppCommand.None and not AppCommand.Help and not AppCommand.Version)
        {
            state.Error = $"Unexpected second command '{token}'.";
            return false;
        }

        state.Command = verb;
        return true;
    }

    private static bool AcceptPositional(string token, ParseState state)
    {
        if (token.StartsWith('-'))
        {
            state.Error = $"Unrecognized argument '{token}'.";
            return false;
        }

        if (state.Command == AppCommand.Explain && !state.TopicSeen)
        {
            state.ExplainTopic = token;
            state.TopicSeen = true;
            return true;
        }

        if (!state.PathSeen)
        {
            state.TargetPath = token;
            state.PathSeen = true;
            return true;
        }

        if (state.Command == AppCommand.Diff && !state.CompareSeen)
        {
            state.ComparePath = token;
            state.CompareSeen = true;
            return true;
        }

        state.Error = $"Unrecognized argument '{token}'.";
        return false;
    }

    private static bool Finish(ParseState state, out AppOptions options, out string error)
    {
        error = string.Empty;
        if (state.Command == AppCommand.None)
        {
            state.Command = state.CiMode ? AppCommand.Drift : AppCommand.Help;
        }

        if (state.Command == AppCommand.Diff && (!state.PathSeen || !state.CompareSeen))
        {
            error = "diff requires two paths: diff <left> <right>.";
            options = new AppOptions();
            return false;
        }

        string target = state.PathSeen ? state.TargetPath : Environment.CurrentDirectory;
        options = new AppOptions
        {
            Command = state.Command,
            TargetPath = target,
            ComparePath = state.ComparePath,
            CiMode = state.CiMode,
            FixRequested = state.FixRequested,
            Strict = state.Strict,
            Format = state.Format,
            ExplainTopic = state.ExplainTopic,
        };
        return true;
    }

    private static bool IsVerb(string token, out AppCommand command)
    {
        command = AppCommand.None;
        string normalized = token.Trim().ToLowerInvariant();
        if (normalized is "drift" or "scan")
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

        if (normalized is "diff")
        {
            command = AppCommand.Diff;
            return true;
        }

        return false;
    }

    private sealed class ParseState
    {
        public AppCommand Command { get; set; } = AppCommand.None;
        public string TargetPath { get; set; } = "";
        public string ComparePath { get; set; } = "";
        public string ExplainTopic { get; set; } = "nu1004";
        public string Error { get; set; } = "";
        public bool CiMode { get; set; }
        public bool FixRequested { get; set; }
        public bool Strict { get; set; }
        public bool PathSeen { get; set; }
        public bool CompareSeen { get; set; }
        public bool TopicSeen { get; set; }
        public OutputFormat Format { get; set; } = OutputFormat.Text;
    }
}
