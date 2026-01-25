using SphereIntegrationHub.Services;

namespace SphereIntegrationHub.Tests;

public sealed class VarsFileLoaderTests
{
    [Fact]
    public void Load_ThrowsWhenEnvironmentMissingAndNoGlobal()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.wfvars");
        File.WriteAllText(tempPath, """
        pre:
          username: "pre-user"
        """);

        try
        {
            var loader = new VarsFileLoader();
            Assert.Throws<InvalidOperationException>(() => loader.Load(tempPath, "dev"));
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void LoadWithDetails_ResolvesOverridesByEnvironmentAndVersion()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.wfvars");
        File.WriteAllText(tempPath, """
        global:
          shared: "global"
          onlyGlobal: "g"

        pre:
          shared: "env"
          onlyEnv: "e"
          version: 3.10
          shared: "ver"
          onlyVer: "v"
        """);

        try
        {
            var loader = new VarsFileLoader();
            var resolution = loader.LoadWithDetails(tempPath, "pre", "3.10");

            Assert.Equal("ver", resolution.Values["shared"]);
            Assert.Equal("e", resolution.Values["onlyEnv"]);
            Assert.Equal("v", resolution.Values["onlyVer"]);
            Assert.Equal("g", resolution.Values["onlyGlobal"]);

            Assert.Equal(VarsFileSource.ForVersion("pre", "3.10"), resolution.Sources["shared"]);
            Assert.Equal(VarsFileSource.ForEnvironment("pre"), resolution.Sources["onlyEnv"]);
            Assert.Equal(VarsFileSource.ForVersion("pre", "3.10"), resolution.Sources["onlyVer"]);
            Assert.Equal(VarsFileSource.Global, resolution.Sources["onlyGlobal"]);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void LoadWithDetails_UsesEnvironmentWhenVersionMissing()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.wfvars");
        File.WriteAllText(tempPath, """
        global:
          shared: "global"

        pre:
          shared: "env"
          version: 3.10
          shared: "ver"
        """);

        try
        {
            var loader = new VarsFileLoader();
            var resolution = loader.LoadWithDetails(tempPath, "pre", "9.99");

            Assert.Equal("env", resolution.Values["shared"]);
            Assert.Equal(VarsFileSource.ForEnvironment("pre"), resolution.Sources["shared"]);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}
