namespace ARTR.LockSight.Tests;

public sealed class ProgramExitTests
{
    [Fact]
    public void Ci_exits_1_when_drift_is_blocking()
    {
        string root = Path.Combine(Path.GetTempPath(), "artr-locksight-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Demo.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
              </PropertyGroup>
            </Project>
            """);

        int code = Program.Main(["drift", root, "--ci"]);

        Assert.Equal(1, code);
    }

    [Fact]
    public void Unknown_argument_exits_2()
    {
        int code = Program.Main(["drift", "--nope"]);

        Assert.Equal(2, code);
    }

    [Fact]
    public void Version_exits_0()
    {
        int code = Program.Main(["--version"]);

        Assert.Equal(0, code);
    }
}
