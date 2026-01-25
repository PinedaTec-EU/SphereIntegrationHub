using SphereIntegrationHub.Services;

namespace SphereIntegrationHub.Tests;

public sealed class RunIfParserTests
{
    [Fact]
    public void TryParse_ValidExpression_ReturnsParts()
    {
        var ok = RunIfParser.TryParse("{{input.id}} == null", out var token, out var op, out var raw);

        Assert.True(ok);
        Assert.Equal("input.id", token);
        Assert.Equal("==", op);
        Assert.Equal("null", raw, ignoreCase: true);
    }

    [Fact]
    public void TryParse_InvalidExpression_ReturnsFalse()
    {
        var ok = RunIfParser.TryParse("input.id == null", out _, out _, out _);

        Assert.False(ok);
    }

    [Fact]
    public void TryParse_InListExpression_ReturnsParts()
    {
        var ok = RunIfParser.TryParse("{{stage:create.output.http_status}} in [200, 201]", out var token, out var op, out var raw);

        Assert.True(ok);
        Assert.Equal("stage:create.output.http_status", token);
        Assert.Equal("in", op);
        Assert.Equal("[200, 201]", raw);
    }

    [Fact]
    public void TryParse_NotInListExpression_ReturnsParts()
    {
        var ok = RunIfParser.TryParse("{{stage:create.output.http_status}} not in [200, 201]", out var token, out var op, out var raw);

        Assert.True(ok);
        Assert.Equal("stage:create.output.http_status", token);
        Assert.Equal("not in", op);
        Assert.Equal("[200, 201]", raw);
    }

    [Fact]
    public void TryParse_InListWithStrings_ReturnsFalse()
    {
        var ok = RunIfParser.TryParse("{{stage:create.output.http_status}} in [\"200\"]", out _, out _, out _);

        Assert.False(ok);
    }
}
