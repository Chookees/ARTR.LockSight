using ARTR.LockSight.Cli;

namespace ARTR.LockSight.Tests;

public sealed class ArgParserTests
{
    [Fact]
    public void Parses_drift_with_ci_and_path()
    {
        bool ok = ArgParser.TryParse(["drift", "./src", "--ci"], out AppOptions options, out string error);

        Assert.True(ok, error);
        Assert.Equal(AppCommand.Drift, options.Command);
        Assert.True(options.CiMode);
        Assert.Equal("./src", options.TargetPath);
        Assert.False(options.FixRequested);
    }

    [Fact]
    public void Parses_why_as_explain()
    {
        bool ok = ArgParser.TryParse(["why", "nu1004"], out AppOptions options, out string error);

        Assert.True(ok, error);
        Assert.Equal(AppCommand.Explain, options.Command);
        Assert.Equal("nu1004", options.ExplainTopic);
    }

    [Fact]
    public void Parses_bare_fix_flag_as_fix_command()
    {
        bool ok = ArgParser.TryParse(["--fix", "./App.sln"], out AppOptions options, out string error);

        Assert.True(ok, error);
        Assert.Equal(AppCommand.Fix, options.Command);
        Assert.True(options.FixRequested);
        Assert.Equal("./App.sln", options.TargetPath);
    }

    [Fact]
    public void Rejects_unknown_flag()
    {
        bool ok = ArgParser.TryParse(["drift", "--nope"], out _, out string error);

        Assert.False(ok);
        Assert.Contains("--nope", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parses_diff_paths_format_and_strict()
    {
        bool ok = ArgParser.TryParse(
            ["diff", "left.lock", "right.lock", "--ci", "--strict", "--json"],
            out AppOptions options,
            out string error);

        Assert.True(ok, error);
        Assert.Equal(AppCommand.Diff, options.Command);
        Assert.Equal("left.lock", options.TargetPath);
        Assert.Equal("right.lock", options.ComparePath);
        Assert.True(options.CiMode);
        Assert.True(options.Strict);
        Assert.Equal(OutputFormat.Json, options.Format);
    }

    [Fact]
    public void Parses_scan_as_drift()
    {
        bool ok = ArgParser.TryParse(["scan", "--format", "json"], out AppOptions options, out string error);

        Assert.True(ok, error);
        Assert.Equal(AppCommand.Drift, options.Command);
        Assert.Equal(OutputFormat.Json, options.Format);
    }

    [Fact]
    public void Rejects_diff_with_one_path()
    {
        bool ok = ArgParser.TryParse(["diff", "only.lock"], out _, out string error);

        Assert.False(ok);
        Assert.Contains("two paths", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parses_verify_as_locked_restore()
    {
        bool ok = ArgParser.TryParse(["verify", "./src"], out AppOptions options, out string error);

        Assert.True(ok, error);
        Assert.Equal(AppCommand.Drift, options.Command);
        Assert.True(options.Evaluate);
        Assert.Equal("./src", options.TargetPath);
    }

    [Fact]
    public void Parses_evaluate_flag_on_drift()
    {
        bool ok = ArgParser.TryParse(["drift", "--evaluate"], out AppOptions options, out string error);

        Assert.True(ok, error);
        Assert.Equal(AppCommand.Drift, options.Command);
        Assert.True(options.Evaluate);
    }

    [Fact]
    public void Parses_diff_git_revision()
    {
        bool ok = ArgParser.TryParse(
            ["diff", "--git", ".", "--revision", "abc123"],
            out AppOptions options,
            out string error);

        Assert.True(ok, error);
        Assert.Equal(AppCommand.Diff, options.Command);
        Assert.True(options.GitDiff);
        Assert.Equal(".", options.TargetPath);
        Assert.Equal("abc123", options.Revision);
    }

    [Fact]
    public void Rejects_diff_git_with_two_paths()
    {
        bool ok = ArgParser.TryParse(["diff", "--git", "left", "right"], out _, out string error);

        Assert.False(ok);
        Assert.Contains("second path", error, StringComparison.Ordinal);
    }
}
