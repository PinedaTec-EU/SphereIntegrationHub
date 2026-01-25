using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services;
using SphereIntegrationHub.Services.Interfaces;

namespace SphereIntegrationHub.Tests;

public sealed class DynamicValueServiceTests
{
    [Fact]
    public void Generate_Fixed_ReturnsValue()
    {
        var service = new DynamicValueService();
        var definition = new RandomValueDefinition(RandomValueType.Fixed, "fixed");

        var value = service.Generate(definition, new PayloadProcessorContext(1, string.Empty, string.Empty, string.Empty, string.Empty), RandomValueFormattingOptions.Default);

        Assert.Equal("fixed", value);
    }

    [Fact]
    public void Generate_DateTime_WithFromOnly_UsesFromPlusOneMonth()
    {
        var service = new DynamicValueService();
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var definition = new RandomValueDefinition(RandomValueType.DateTime, FromDateTime: from);

        var value = service.Generate(definition, new PayloadProcessorContext(1, string.Empty, string.Empty, string.Empty, string.Empty), RandomValueFormattingOptions.Default);

        Assert.True(DateTimeOffset.TryParse(value, out var parsed));
        Assert.InRange(parsed, from, from.AddMonths(1));
    }

    [Fact]
    public void Generate_DateTime_WithToOnly_UsesToMinusOneMonth()
    {
        var service = new DynamicValueService();
        var to = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var definition = new RandomValueDefinition(RandomValueType.DateTime, ToDateTime: to);

        var value = service.Generate(definition, new PayloadProcessorContext(1, string.Empty, string.Empty, string.Empty, string.Empty), RandomValueFormattingOptions.Default);

        Assert.True(DateTimeOffset.TryParse(value, out var parsed));
        Assert.InRange(parsed, to.AddMonths(-1), to);
    }

    [Fact]
    public void Generate_Date_WithRange_ReturnsWithinBounds()
    {
        var service = new DynamicValueService();
        var from = new DateOnly(2025, 1, 1);
        var to = new DateOnly(2025, 2, 1);
        var definition = new RandomValueDefinition(RandomValueType.Date, FromDate: from, ToDate: to);

        var value = service.Generate(definition, new PayloadProcessorContext(1, string.Empty, string.Empty, string.Empty, string.Empty), RandomValueFormattingOptions.Default);

        Assert.True(DateOnly.TryParse(value, out var parsed));
        Assert.InRange(parsed, from, to);
    }

    [Fact]
    public void Generate_DateTime_UsesSystemTimeProviderForDefaults()
    {
        var systemTimeProvider = new TestSystemTimeProvider(
            new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var service = new DynamicValueService(systemTimeProvider);
        var definition = new RandomValueDefinition(RandomValueType.DateTime);

        var value = service.Generate(definition, new PayloadProcessorContext(1, string.Empty, string.Empty, string.Empty, string.Empty), RandomValueFormattingOptions.Default);

        Assert.True(DateTimeOffset.TryParse(value, out var parsed));
        Assert.InRange(parsed, systemTimeProvider.UtcNow.AddMonths(-1), systemTimeProvider.UtcNow.AddMonths(1));
    }

    [Fact]
    public void Generate_Sequence_Increments()
    {
        var service = new DynamicValueService();
        var definition = new RandomValueDefinition(RandomValueType.Sequence, Start: 10, Step: 2);

        var value = service.Generate(definition, new PayloadProcessorContext(3, string.Empty, string.Empty, string.Empty, string.Empty), RandomValueFormattingOptions.Default);

        Assert.Equal("14", value);
    }

    private sealed class TestSystemTimeProvider : ISystemTimeProvider
    {
        public TestSystemTimeProvider(DateTimeOffset now)
        {
            Now = now;
            UtcNow = now;
        }

        public DateTimeOffset Now { get; }
        public DateTimeOffset UtcNow { get; }
    }
}
