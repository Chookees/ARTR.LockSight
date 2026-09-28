namespace ARTR.LockSight.Cli;

/// <summary>
/// Product version shown by <c>--version</c> and JSON reports.
/// </summary>
public static class ToolVersion
{
    public static string Current { get; } = Resolve();

    private static string Resolve()
    {
        Version? version = typeof(ToolVersion).Assembly.GetName().Version;
        if (version is null)
        {
            return "0.0.0";
        }

        return version.ToString(3);
    }
}
