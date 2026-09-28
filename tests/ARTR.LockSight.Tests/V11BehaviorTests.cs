using System.Diagnostics;
using ARTR.LockSight.Drift;
using ARTR.LockSight.Evaluate;
using ARTR.LockSight.Fix;

namespace ARTR.LockSight.Tests;

public sealed class V11BehaviorTests
{
    [Fact]
    public void Custom_lockfile_path_is_the_file_that_is_scanned()
    {
        string root = CreateTempDir();
        WriteProject(root, "Demo.csproj", LockProject("custom.lock.json"));
        WriteJson(root, "packages.lock.json", LockJson("[13.0.3, )", "13.0.3"));
        WriteJson(root, "custom.lock.json", LockJson("[13.0.1, )", "13.0.1"));

        IReadOnlyList<DriftFinding> findings = DriftDetector.Scan(root);

        Assert.Contains(findings, f => f.Reason == DriftReason.PinDrift && f.PackageId == "Newtonsoft.Json");
    }

    [Fact]
    public void Expands_MSBuildProjectDirectory_in_lockfile_path()
    {
        string root = CreateTempDir();
        string locks = Path.Combine(root, "locks");
        Directory.CreateDirectory(locks);
        WriteProject(root, "Demo.csproj", LockProject("$(MSBuildProjectDirectory)/locks/packages.lock.json"));
        WriteJson(locks, "packages.lock.json", LockJson("[13.0.1, )", "13.0.1"));

        IReadOnlyList<DriftFinding> findings = DriftDetector.Scan(root);

        Assert.Contains(findings, f => f.Reason == DriftReason.PinDrift);
        Assert.DoesNotContain(findings, f => f.Reason == DriftReason.MissingLockfile);
        Assert.DoesNotContain(findings, f => f.Reason == DriftReason.LockfilePathUnresolved);
    }

    [Fact]
    public void Unresolved_lockfile_expression_warns_and_falls_back()
    {
        string root = CreateTempDir();
        WriteProject(root, "Demo.csproj", LockProject("$(CustomDir)/packages.lock.json"));
        WriteJson(root, "packages.lock.json", LockJson("[13.0.3, )", "13.0.3"));

        IReadOnlyList<DriftFinding> findings = DriftDetector.Scan(root);

        Assert.Contains(findings, f => f.Reason == DriftReason.LockfilePathUnresolved);
        Assert.DoesNotContain(findings, f => f.Reason == DriftReason.MissingLockfile);
        Assert.DoesNotContain(findings, f => f.Reason == DriftReason.PinDrift);
    }

    [Fact]
    public void Solution_filter_lists_only_included_projects()
    {
        string root = CreateTempDir();
        string app = Path.Combine(root, "App");
        string lib = Path.Combine(root, "Lib");
        Directory.CreateDirectory(app);
        Directory.CreateDirectory(lib);
        WriteProject(app, "App.csproj", BareProject());
        WriteProject(lib, "Lib.csproj", LockProject(null));
        WriteJson(lib, "packages.lock.json", LockJson("[13.0.1, )", "13.0.1"));
        File.WriteAllText(Path.Combine(root, "Demo.sln"), "Microsoft Visual Studio Solution File");
        File.WriteAllText(Path.Combine(root, "Demo.slnf"),
            """
            {
              "solution": {
                "path": "Demo.sln",
                "projects": [ "App\\App.csproj", "Gone\\Gone.csproj" ]
              }
            }
            """);

        IReadOnlyList<DriftFinding> findings = DriftDetector.Scan(Path.Combine(root, "Demo.slnf"));

        Assert.Contains(findings, f => f.Reason == DriftReason.MissingLockfile && f.ProjectPath.EndsWith("App.csproj", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Reason == DriftReason.ProjectReferenceLockIssue && f.PackageId == "Gone.csproj");
        Assert.DoesNotContain(findings, f => f.ProjectPath.Contains("Lib.csproj", StringComparison.Ordinal));
    }

    [Fact]
    public void Git_diff_reports_a_lockfile_updated_since_head()
    {
        string root = CreateTempDir();
        WriteJson(root, "packages.lock.json", LockJson("[13.0.3, )", "13.0.3"));
        Git(root, "init", "-b", "main");
        Git(root, "config", "user.email", "locksight-test@example.com");
        Git(root, "config", "user.name", "LockSight Test");
        Git(root, "config", "commit.gpgsign", "false");
        Git(root, "add", "packages.lock.json");
        Git(root, "commit", "-m", "lock");
        WriteJson(root, "packages.lock.json", LockJson("[13.0.3, )", "13.0.4"));

        IReadOnlyList<LockDiffEntry> changes = GitLockDiff.CompareToRevision(root, "HEAD");

        Assert.Contains(changes, c => c.Change == LockChange.Updated && c.PackageId == "Newtonsoft.Json");
    }

    [Fact]
    public void Parses_nu1004_project_paths()
    {
        const string transcript =
            "/src/App/App.csproj : error NU1004: The packages lock file is inconsistent.\n" +
            "App.csproj(1,1): error NU1004: second message\n";

        IReadOnlyList<RestoreDiagnostic> rows = Nu1004Parser.Parse(transcript);

        Assert.Equal(2, rows.Count);
        Assert.Equal("/src/App/App.csproj", rows[0].ProjectPath);
        Assert.Contains("inconsistent", rows[0].Message, StringComparison.Ordinal);
        Assert.Equal("App.csproj", rows[1].ProjectPath);
        Assert.Equal("second message", rows[1].Message);
    }

    [Fact]
    public void Failure_report_keeps_static_rows_for_the_named_project()
    {
        string app = Path.GetFullPath(Path.Combine("src", "App", "App.csproj"));
        string other = Path.GetFullPath(Path.Combine("src", "Other", "Other.csproj"));
        IReadOnlyList<RestoreDiagnostic> diagnostics = Nu1004Parser.Parse("src/App/App.csproj : error NU1004: inconsistent");
        var staticFindings = new List<DriftFinding>
        {
            new(app, "net10.0", "Newtonsoft.Json", DriftReason.PinDrift, "pin"),
            new(other, "net10.0", "Other.Pkg", DriftReason.PinDrift, "other"),
        };

        IReadOnlyList<DriftFinding> rows = EvaluateReporter.BuildFailureFindings(diagnostics, staticFindings);

        Assert.Contains(rows, f => f.Reason == DriftReason.LockedRestoreFailed);
        Assert.Contains(rows, f => f.PackageId == "Newtonsoft.Json");
        Assert.DoesNotContain(rows, f => f.PackageId == "Other.Pkg");
    }

    [Fact]
    public void Locked_mode_arguments_are_not_force_evaluate()
    {
        var locked = new ProcessStartInfo();
        RestoreFixer.AddLockedModeArguments(locked, "App.sln");
        Assert.Contains("--locked-mode", locked.ArgumentList);
        Assert.DoesNotContain("--force-evaluate", locked.ArgumentList);

        var force = new ProcessStartInfo();
        RestoreFixer.AddForceEvaluateArguments(force, "App.sln");
        Assert.Contains("--force-evaluate", force.ArgumentList);
        Assert.DoesNotContain("--locked-mode", force.ArgumentList);
    }

    private static string LockProject(string? lockPath)
    {
        string pathProperty = lockPath is null
            ? ""
            : $"    <NuGetLockFilePath>{lockPath}</NuGetLockFilePath>\n";
        return $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
            {pathProperty}  </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
              </ItemGroup>
            </Project>
            """;
    }

    private static string BareProject()
    {
        return """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
              </PropertyGroup>
            </Project>
            """;
    }

    private static string LockJson(string requested, string resolved)
    {
        return $$"""
            {
              "version": 1,
              "dependencies": {
                "net10.0": {
                  "Newtonsoft.Json": {
                    "type": "Direct",
                    "requested": "{{requested}}",
                    "resolved": "{{resolved}}",
                    "contentHash": "abc"
                  }
                }
              }
            }
            """;
    }

    private static string CreateTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "artr-locksight-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteProject(string dir, string fileName, string contents)
    {
        File.WriteAllText(Path.Combine(dir, fileName), contents);
    }

    private static void WriteJson(string dir, string fileName, string contents)
    {
        File.WriteAllText(Path.Combine(dir, fileName), contents);
    }

    private static void Git(string directory, params string[] args)
    {
        var start = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        for (int i = 0; i < args.Length; i++)
        {
            start.ArgumentList.Add(args[i]);
        }

        using var process = new Process { StartInfo = start };
        Assert.True(process.Start());
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(30_000));
        Assert.True(process.ExitCode == 0, stderr.GetAwaiter().GetResult() + stdout.GetAwaiter().GetResult());
    }
}
