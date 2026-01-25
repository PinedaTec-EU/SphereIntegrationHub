using SphereIntegrationHub.Services;

namespace SphereIntegrationHub.Tests;

public sealed class EnvironmentFileLoaderTests
{
    [Fact]
    public void Load_ParsesEntriesAndIgnoresComments()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.env");
        File.WriteAllText(tempPath, """
        # Comment
        export API_KEY=abc123
        NAME="Acme Corp"
        EMPTY=
        """);

        try
        {
            var loader = new EnvironmentFileLoader();
            var values = loader.Load(tempPath);

            Assert.Equal("abc123", values["API_KEY"]);
            Assert.Equal("Acme Corp", values["NAME"]);
            Assert.Equal(string.Empty, values["EMPTY"]);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void Load_InvalidLine_Throws()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.env");
        File.WriteAllText(tempPath, "INVALID");

        try
        {
            var loader = new EnvironmentFileLoader();
            Assert.Throws<InvalidOperationException>(() => loader.Load(tempPath));
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}
