using ARTR.LockSight.Cli;
using ARTR.LockSight.Drift;
using ARTR.LockSight.Reporting;

namespace ARTR.LockSight.Tests;

public sealed class V1BehaviorTests
{
    [Fact]
    public void Higher_resolved_version_inside_the_pin_range_is_not_drift()
    {
        string root = Temp();
        WriteProject(root, "Demo.csproj", LockProject("net10.0",
            """
            <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
            """));
        WriteLock(root, "net10.0", "Newtonsoft.Json", "[13.0.3, )", "13.0.4", "abc");

        IReadOnlyList<DriftFinding> findings = DriftDetector.Scan(root);

        Assert.DoesNotContain(findings, f => f.Reason == DriftReason.PinDrift);
    }

    [Fact]
    public void Missing_tfm_section_is_an_error()
    {
        string root = Temp();
        WriteProject(root, "Demo.csproj", LockProject("net8.0;net10.0",
            """
            <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
            """,
            multi: true));
        WriteLock(root, "net10.0", "Newtonsoft.Json", "[13.0.3, )", "13.0.3", "abc");

        IReadOnlyList<DriftFinding> findings = DriftDetector.Scan(root);

        Assert.Contains(findings, f => f.Reason == DriftReason.MultiTfmMismatch && f.TargetFramework == "net8.0");
    }

    [Fact]
    public void Directory_build_props_enables_lockfile_check()
    {
        string root = Temp();
        File.WriteAllText(Path.Combine(root, "Directory.Build.props"),
            """
            <Project>
              <PropertyGroup>
                <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
              </PropertyGroup>
            </Project>
            """);
        WriteProject(root, "Demo.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        IReadOnlyList<DriftFinding> findings = DriftDetector.Scan(root);

        Assert.Contains(findings, f => f.Reason == DriftReason.MissingLockfile);
    }

    [Fact]
    public void Explicit_lockfile_disable_ignores_a_stale_file()
    {
        string root = Temp();
        WriteProject(root, "Demo.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RestorePackagesWithLockFile>false</RestorePackagesWithLockFile>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="9.0.0" />
              </ItemGroup>
            </Project>
            """);
        WriteLock(root, "net10.0", "Newtonsoft.Json", "[1.0.0, )", "1.0.0", "abc");

        IReadOnlyList<DriftFinding> findings = DriftDetector.Scan(root);

        Assert.Empty(findings);
    }

    [Fact]
    public void Reports_orphan_direct_dependency()
    {
        string root = Temp();
        WriteProject(root, "Demo.csproj", LockProject("net10.0", ""));
        WriteLock(root, "net10.0", "Left.Over", "[1.0.0, )", "1.0.0", "abc");

        IReadOnlyList<DriftFinding> findings = DriftDetector.Scan(root);

        Assert.Contains(findings, f => f.Reason == DriftReason.OrphanLockEntry && f.PackageId == "Left.Over");
    }

    [Fact]
    public void Reports_missing_content_hash()
    {
        string root = Temp();
        WriteProject(root, "Demo.csproj", LockProject("net10.0",
            """
            <PackageReference Include="Newtonsoft.Json" Version="13.0.1" />
            """));
        WriteLock(root, "net10.0", "Newtonsoft.Json", "[13.0.1, )", "13.0.1", "");

        IReadOnlyList<DriftFinding> findings = DriftDetector.Scan(root);

        Assert.Contains(findings, f => f.Reason == DriftReason.LockfileInconsistent);
    }

    [Fact]
    public void Version_override_wins_over_central_pin()
    {
        string root = Temp();
        File.WriteAllText(Path.Combine(root, "Directory.Packages.props"),
            """
            <Project>
              <ItemGroup>
                <PackageVersion Include="Newtonsoft.Json" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """);
        WriteProject(root, "Demo.csproj", LockProject("net10.0",
            """
            <PackageReference Include="Newtonsoft.Json" VersionOverride="2.0.0" />
            """));
        WriteLock(root, "net10.0", "Newtonsoft.Json", "[1.0.0, )", "1.0.0", "abc");

        IReadOnlyList<DriftFinding> findings = DriftDetector.Scan(root);

        Assert.Contains(findings, f => f.Reason == DriftReason.PinDrift && f.Detail.Contains("2.0.0", StringComparison.Ordinal));
    }

    [Fact]
    public void Solution_file_limits_the_scan_to_listed_projects()
    {
        string root = Temp();
        WriteProject(root, "Demo.csproj", LockProject("net10.0",
            """
            <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
            """));
        WriteLock(root, "net10.0", "Newtonsoft.Json", "[13.0.1, )", "13.0.1", "abc");
        // Same folder, not listed in the solution. A directory scan would see it; a solution scan must not.
        WriteProject(root, "Other.csproj", LockProject("net10.0",
            """
            <PackageReference Include="Outsider.Package" Version="2.0.0" />
            """));
        File.WriteAllText(Path.Combine(root, "Demo.sln"),
            """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Demo", "Demo.csproj", "{AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}"
            EndProject
            Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "Solution Items", "Solution Items", "{BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB}"
            EndProject
            """);

        IReadOnlyList<DriftFinding> findings = DriftDetector.Scan(Path.Combine(root, "Demo.sln"));

        Assert.Contains(findings, f => f.PackageId == "Newtonsoft.Json");
        Assert.DoesNotContain(findings, f => f.PackageId == "Outsider.Package");
    }

    [Fact]
    public void Slnx_reports_a_missing_project()
    {
        string root = Temp();
        File.WriteAllText(Path.Combine(root, "App.slnx"),
            """
            <Solution>
              <Project Path="Missing.csproj" />
            </Solution>
            """);

        IReadOnlyList<DriftFinding> findings = DriftDetector.Scan(Path.Combine(root, "App.slnx"));

        Assert.Contains(findings, f => f.Reason == DriftReason.ProjectReferenceLockIssue && f.PackageId == "Missing.csproj");
    }

    [Fact]
    public void Diff_reports_an_updated_resolved_version()
    {
        string left = Temp();
        string right = Temp();
        WriteLock(left, "net10.0", "Newtonsoft.Json", "[13.0.1, )", "13.0.1", "aaa");
        WriteLock(right, "net10.0", "Newtonsoft.Json", "[13.0.3, )", "13.0.3", "bbb");

        IReadOnlyList<LockDiffEntry> changes = LockfileDiffer.Compare(
            Path.Combine(left, "packages.lock.json"),
            Path.Combine(right, "packages.lock.json"));

        Assert.Contains(changes, c => c.Change == LockChange.Updated && c.PackageId == "Newtonsoft.Json");
    }

    [Fact]
    public void Json_report_keeps_machine_readable_stdout()
    {
        var findings = new List<DriftFinding>
        {
            new("App.csproj", "net10.0", "X", DriftReason.PinDrift, "pinned ahead"),
        };
        using var writer = new StringWriter();
        DriftReporter.WriteReport(findings, writer, ciMode: false, format: OutputFormat.Json);
        string output = writer.ToString();
        Assert.Contains("\"reason\": \"PinDrift\"", output, StringComparison.Ordinal);
        Assert.DoesNotContain("::error", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Warnings_do_not_count_as_blocking_unless_strict()
    {
        var findings = new List<DriftFinding>
        {
            new("App.csproj", "net8.0", "X", DriftReason.StaleTfm, "leftover tfm"),
        };

        Assert.Equal(0, DriftReporter.CountBlocking(findings, strict: false));
        Assert.Equal(1, DriftReporter.CountBlocking(findings, strict: true));
    }

    [Fact]
    public void Tfm_condition_skips_other_frameworks()
    {
        string root = Temp();
        WriteProject(root, "Demo.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
                <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Only.Net8" Version="1.0.0" Condition="'$(TargetFramework)' == 'net8.0'" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(root, "packages.lock.json"),
            """
            {
              "version": 1,
              "dependencies": {
                "net8.0": {
                  "Only.Net8": {
                    "type": "Direct",
                    "requested": "[1.0.0, )",
                    "resolved": "1.0.0",
                    "contentHash": "abc"
                  }
                },
                "net10.0": {}
              }
            }
            """);

        IReadOnlyList<DriftFinding> findings = DriftDetector.Scan(root);

        Assert.DoesNotContain(findings, f => f.Reason == DriftReason.PinDrift);
    }

    private static string LockProject(string tfm, string packageXml, bool multi = false)
    {
        string tfmElement = multi || tfm.Contains(';')
            ? $"<TargetFrameworks>{tfm}</TargetFrameworks>"
            : $"<TargetFramework>{tfm}</TargetFramework>";
        return $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                {tfmElement}
                <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
              </PropertyGroup>
              <ItemGroup>
                {packageXml}
              </ItemGroup>
            </Project>
            """;
    }

    private static void WriteLock(string dir, string tfm, string packageId, string requested, string resolved, string hash)
    {
        File.WriteAllText(Path.Combine(dir, "packages.lock.json"),
            $$"""
            {
              "version": 1,
              "dependencies": {
                "{{tfm}}": {
                  "{{packageId}}": {
                    "type": "Direct",
                    "requested": "{{requested}}",
                    "resolved": "{{resolved}}",
                    "contentHash": "{{hash}}"
                  }
                }
              }
            }
            """);
    }

    private static string Temp()
    {
        string dir = Path.Combine(Path.GetTempPath(), "artr-locksight-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteProject(string dir, string fileName, string contents)
    {
        File.WriteAllText(Path.Combine(dir, fileName), contents);
    }
}
