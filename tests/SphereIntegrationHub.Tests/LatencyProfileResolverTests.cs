using SphereIntegrationHub.Definitions;

namespace SphereIntegrationHub.Tests;

public sealed class LatencyProfileResolverTests
{
    [Theory]
    [InlineData(0, "green")]
    [InlineData(200, "green")]
    [InlineData(201, "amber")]
    [InlineData(500, "amber")]
    [InlineData(501, "red")]
    [InlineData(99999, "red")]
    public void ResolveBand_UsesInclusiveRangeBoundaries(long durationMs, string expectedBand)
    {
        var profile = new LatencyProfileDefinition
        {
            Name = "semaphore-default",
            Bands =
            [
                new LatencyBandDefinition { Name = "green", MinMs = 0, MaxMs = 200, Color = "green" },
                new LatencyBandDefinition { Name = "amber", MinMs = 201, MaxMs = 500, Color = "amber" },
                new LatencyBandDefinition { Name = "red", MinMs = 501, Color = "red" }
            ]
        };

        var band = LatencyProfileResolver.ResolveBand(profile, durationMs);

        Assert.NotNull(band);
        Assert.Equal(expectedBand, band!.Name);
    }

    [Fact]
    public void ResolveProfile_PrefersWorkflowProfileOverCatalogProfile()
    {
        var workflowProfile = new LatencyProfileDefinition
        {
            Name = "semaphore-default",
            Bands = [new LatencyBandDefinition { Name = "amber", MinMs = 0, MaxMs = 1000, Color = "amber" }]
        };
        var catalogProfile = new LatencyProfileDefinition
        {
            Name = "semaphore-default",
            Bands = [new LatencyBandDefinition { Name = "green", MinMs = 0, MaxMs = 1000, Color = "green" }]
        };

        var resolved = LatencyProfileResolver.ResolveProfile(
            profileName: "semaphore-default",
            workflowProfiles: new Dictionary<string, LatencyProfileDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["semaphore-default"] = workflowProfile
            },
            catalogProfiles: new Dictionary<string, LatencyProfileDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["semaphore-default"] = catalogProfile
            });

        Assert.Same(workflowProfile, resolved);
    }
}
