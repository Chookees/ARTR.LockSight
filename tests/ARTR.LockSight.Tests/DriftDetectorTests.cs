using ARTR.LockSight.Drift;
using ARTR.LockSight.Reporting;

namespace ARTR.LockSight.Tests;

public sealed class DriftDetectorTests
{
    [Fact]
    public void Detects_pin_drift_against_lockfile()
    {
        string root = CreateTempDir();
        WriteProject(root, "Demo.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
              </ItemGroup>
            </Project>
            """);
        WriteLockfile(root,
            """
            {
              "version": 1,
              "dependencies": {
                "net10.0": {
                  "Newtonsoft.Json": {
                    "type": "Direct",
                    "requested": "[13.0.1, )",
                    "resolved": "13.0.1",
                    "contentHash": "abc"
                  }
                }
              }
            }
            """);

        IReadOnlyList<DriftFinding> findings = DriftDetector.Scan(root);

        Assert.Contains(findings, f => f.Reason == DriftReason.PinDrift && f.PackageId == "Newtonsoft.Json");
    }

    [Fact]
    public void Detects_missing_lockfile_when_enabled()
    {
        string root = CreateTempDir();
        WriteProject(root, "Demo.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
              </PropertyGroup>
            </Project>
            """);

        IReadOnlyList<DriftFinding> findings = DriftDetector.Scan(root);

        Assert.Contains(findings, f => f.Reason == DriftReason.MissingLockfile);
    }

    [Fact]
    public void Detects_multi_tfm_resolved_mismatch()
    {
        string root = CreateTempDir();
        WriteProject(root, "Demo.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
                <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
              </ItemGroup>
            </Project>
            """);
        WriteLockfile(root,
            """
            {
              "version": 1,
              "dependencies": {
                "net8.0": {
                  "Newtonsoft.Json": {
                    "type": "Direct",
                    "requested": "[13.0.3, )",
                    "resolved": "13.0.3",
                    "contentHash": "a"
                  }
                },
                "net10.0": {
                  "Newtonsoft.Json": {
                    "type": "Direct",
                    "requested": "[13.0.3, )",
                    "resolved": "13.0.1",
                    "contentHash": "b"
                  }
                }
              }
            }
            """);

        IReadOnlyList<DriftFinding> findings = DriftDetector.Scan(root);

        Assert.Contains(findings, f => f.Reason == DriftReason.MultiTfmMismatch);
    }

    [Fact]
    public void Detects_project_reference_missing_lockfile()
    {
        string root = CreateTempDir();
        string libDir = Path.Combine(root, "Lib");
        Directory.CreateDirectory(libDir);

        WriteProject(libDir, "Lib.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
              </PropertyGroup>
            </Project>
            """);

        WriteProject(root, "App.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="Lib\Lib.csproj" />
              </ItemGroup>
            </Project>
            """);
        WriteLockfile(root,
            """
            {
              "version": 1,
              "dependencies": {
                "net10.0": {}
              }
            }
            """);

        IReadOnlyList<DriftFinding> findings = DriftDetector.Scan(root);

        Assert.Contains(findings, f => f.Reason == DriftReason.ProjectReferenceLockIssue);
    }

    [Fact]
    public void Uses_central_package_management_pins()
    {
        string root = CreateTempDir();
        File.WriteAllText(Path.Combine(root, "Directory.Packages.props"),
            """
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
              </PropertyGroup>
              <ItemGroup>
                <PackageVersion Include="Newtonsoft.Json" Version="13.0.3" />
              </ItemGroup>
            </Project>
            """);
        WriteProject(root, "Demo.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" />
              </ItemGroup>
            </Project>
            """);
        WriteLockfile(root,
            """
            {
              "version": 1,
              "dependencies": {
                "net10.0": {
                  "Newtonsoft.Json": {
                    "type": "Direct",
                    "requested": "[13.0.1, )",
                    "resolved": "13.0.1",
                    "contentHash": "abc"
                  }
                }
              }
            }
            """);

        IReadOnlyList<DriftFinding> findings = DriftDetector.Scan(root);

        Assert.Contains(findings, f => f.Reason == DriftReason.PinDrift && f.Detail.Contains("13.0.3", StringComparison.Ordinal));
    }

    [Fact]
    public void Reporter_writes_ci_annotations()
    {
        var findings = new List<DriftFinding>
        {
            new("App.csproj", "net10.0", "X", DriftReason.PinDrift, "pinned ahead of lock"),
        };
        using var writer = new StringWriter();
        DriftReporter.WriteReport(findings, writer, ciMode: true);
        string output = writer.ToString();
        Assert.Contains("::error", output, StringComparison.Ordinal);
        Assert.Contains("PinDrift", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("13.0.1", "13.0.1", "[13.0.1, )", true)]
    [InlineData("13.0.3", "13.0.1", "[13.0.1, )", false)]
    [InlineData("[13.0.1]", "13.0.1", "[13.0.1]", true)]
    public void VersionsCompatible_matches_expected(string pin, string resolved, string requested, bool expected)
    {
        Assert.Equal(expected, DriftDetector.VersionsCompatible(pin, resolved, requested));
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

    private static void WriteLockfile(string dir, string contents)
    {
        File.WriteAllText(Path.Combine(dir, "packages.lock.json"), contents);
    }
}
