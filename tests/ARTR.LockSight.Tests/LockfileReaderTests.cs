using ARTR.LockSight.Drift;

namespace ARTR.LockSight.Tests;

public sealed class LockfileReaderTests
{
    [Fact]
    public void Reads_direct_dependency_per_tfm()
    {
        string dir = CreateTempDir();
        string lockPath = Path.Combine(dir, "packages.lock.json");
        File.WriteAllText(lockPath,
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

        LockfileDocument doc = LockfileReader.Read(lockPath);

        Assert.True(doc.ByFramework.ContainsKey("net10.0"));
        LockDependency dep = doc.ByFramework["net10.0"]["Newtonsoft.Json"];
        Assert.Equal("13.0.1", dep.Resolved);
        Assert.True(dep.IsDirect);
    }

    private static string CreateTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "artr-locksight-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
