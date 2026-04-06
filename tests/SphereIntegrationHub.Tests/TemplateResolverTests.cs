using System.Text.Json;

using SphereIntegrationHub.Services;

namespace SphereIntegrationHub.Tests;

public sealed class TemplateResolverTests
{
    [Fact]
    public void ResolveTemplate_ResolvesBasicTokens()
    {
        var resolver = new TemplateResolver();
        var context = new TemplateContext(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["username"] = "user"
            },
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["account"] = "acme"
            },
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["tokenId"] = "jwt"
            },
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ORG"] = "org"
            });

        var template = "User={{input.username}} Org={{env:ORG}} Account={{global.account}} Token={{context.tokenId}}";
        var resolved = resolver.ResolveTemplate(template, context);

        Assert.Equal("User=user Org=org Account=acme Token=jwt", resolved);
    }

    [Fact]
    public void ResolveTemplate_ResolvesResponseJsonPath()
    {
        var resolver = new TemplateResolver();
        var context = new TemplateContext(
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, string>());

        using var document = JsonDocument.Parse("""
        {
          "facturas": {
            "id": "abc",
            "items": [
              { "ref": 1234 }
            ]
          }
        }
        """);

        var response = new ResponseContext(
            200,
            document.RootElement.GetRawText(),
            new Dictionary<string, string>(),
            document);

        var resolved = resolver.ResolveTemplate("{{response.facturas.items.0.ref}}", context, response);

        Assert.Equal("1234", resolved);
    }

    [Fact]
    public void ResolveTemplate_OptionalResponsePathReturnsEmptyWhenSegmentIsMissing()
    {
        var resolver = new TemplateResolver();
        var context = new TemplateContext(
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, string>());

        using var document = JsonDocument.Parse("""
        {
          "account": {
            "id": "a-1"
          }
        }
        """);

        var response = new ResponseContext(
            200,
            document.RootElement.GetRawText(),
            new Dictionary<string, string>(),
            document);

        var resolved = resolver.ResolveTemplate("{{response.account.status?}}", context, response);

        Assert.Equal(string.Empty, resolved);
    }

    [Fact]
    public void ResolveTemplate_ResolvesStageJsonToken()
    {
        var resolver = new TemplateResolver();
        var endpointOutputs = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["create"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["dto"] = """
                {
                  "id": "mocked-id",
                  "amount": 25
                }
                """
            }
        };

        var context = new TemplateContext(
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            endpointOutputs,
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, string>());

        var resolved = resolver.ResolveTemplate("{{stage:json(create.output.dto).id}}", context);

        Assert.Equal("mocked-id", resolved);
    }

    [Fact]
    public void ResolveTemplate_ResolvesStageOutputAlias()
    {
        var resolver = new TemplateResolver();
        var endpointOutputs = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["stageA"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["value"] = "123"
            }
        };

        var context = new TemplateContext(
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            endpointOutputs,
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, string>());

        var resolved = resolver.ResolveTemplate("{{stage:stageA.output.value}}", context);

        Assert.Equal("123", resolved);
    }

    [Fact]
    public void ResolveTemplate_ResolvesNestedJsonInputPath()
    {
        var resolver = new TemplateResolver();
        using var json = JsonDocument.Parse("""
        {
          "customer": {
            "id": "c-1"
          }
        }
        """);

        var context = new TemplateContext(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["payload"] = json.RootElement.GetRawText()
            },
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, string>(),
            new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["payload"] = json.RootElement.Clone()
            });

        var resolved = resolver.ResolveTemplate("{{input.payload.customer.id}}", context);

        Assert.Equal("c-1", resolved);
    }

    [Fact]
    public void ResolveTemplate_ResolvesWorkflowResultToken()
    {
        var resolver = new TemplateResolver();
        var workflowResults = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["child"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["status"] = "Ok",
                ["message"] = "done"
            }
        };

        var context = new TemplateContext(
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            workflowResults,
            new Dictionary<string, string>());

        var resolved = resolver.ResolveTemplate("{{stage:child.workflow.result.status}}", context);

        Assert.Equal("Ok", resolved);
    }

    [Fact]
    public void ResolveTemplate_ResolvesWorkflowOutputToken()
    {
        var resolver = new TemplateResolver();
        var workflowOutputs = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["child"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = "123"
            }
        };

        var context = new TemplateContext(
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            workflowOutputs,
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, string>());

        var resolved = resolver.ResolveTemplate("{{stage:child.workflow.output.id}}", context);

        Assert.Equal("123", resolved);
    }

    [Fact]
    public void ResolveTemplate_ResolvesSystemUtcNow()
    {
        var resolver = new TemplateResolver();
        var context = new TemplateContext(
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, string>());

        var resolved = resolver.ResolveTemplate("{{system:datetime.utcnow}}", context);

        Assert.True(DateTimeOffset.TryParse(resolved, out _));
    }

    [Theory]
    // P[nY][nM][nD]T[nH][nM][nS] — ISO 8601 duration
    [InlineData("system:datetime.utcnow + P1DT1H2M3S", 0, 0, 1,  1,   2, 3)]   // +1 day, 1h 2m 3s
    [InlineData("system:datetime.now + P1Y2M3D",        1, 2, 3,  0,   0, 0)]   // +1 year, 2 months, 3 days
    [InlineData("system:datetime.utcnow - P5DT30M",     0, 0, -5, 0, -30, 0)]   // -5 days, 30 min
    [InlineData("system:datetime.now + PT0S",           0, 0, 0,  0,   0, 0)]   // zero offset
    [InlineData("system:datetime.utcnow + PT2H",        0, 0, 0,  2,   0, 0)]   // time-only offset
    [InlineData("system:datetime.now - P1Y",            -1, 0, 0, 0,  0, 0)]   // year-only offset
    public void ResolveTemplate_SystemDatetime_AppliesIsoDuration(
        string token, int years, int months, int days, int hours, int minutes, int seconds)
    {
        var fixedNow = new DateTimeOffset(2026, 4, 6, 12, 0, 0, TimeSpan.Zero);
        var resolver = new TemplateResolver(new FixedTimeProvider(fixedNow));
        var context = EmptyContext();

        var resolved = resolver.ResolveTemplate($"{{{{{token}}}}}", context);

        var expected = fixedNow
            .AddYears(years).AddMonths(months).AddDays(days)
            .AddHours(hours).AddMinutes(minutes).AddSeconds(seconds);
        Assert.True(DateTimeOffset.TryParse(resolved, out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("system:date.now + P1Y6M15D",    2027, 10, 21)]   // +1 year, 6 months, 15 days
    [InlineData("system:date.utcnow - P1M",      2026,  3,  6)]   // -1 month
    [InlineData("system:date.now + P5D",         2026,  4, 11)]   // +5 days
    [InlineData("system:date.now + P0D",         2026,  4,  6)]   // zero offset
    public void ResolveTemplate_SystemDate_AppliesIsoDuration(
        string token, int expectedYear, int expectedMonth, int expectedDay)
    {
        var fixedNow = new DateTimeOffset(2026, 4, 6, 12, 0, 0, TimeSpan.Zero);
        var resolver = new TemplateResolver(new FixedTimeProvider(fixedNow));
        var context = EmptyContext();

        var resolved = resolver.ResolveTemplate($"{{{{{token}}}}}", context);

        Assert.Equal(new DateOnly(expectedYear, expectedMonth, expectedDay).ToString("yyyy-MM-dd"), resolved);
    }

    [Fact]
    public void ResolveTemplate_SystemDatetime_InvalidDuration_Throws()
    {
        var resolver = new TemplateResolver();
        var context = EmptyContext();

        Assert.Throws<InvalidOperationException>(
            () => resolver.ResolveTemplate("{{ system:datetime.now + badoffset }}", context));
    }

    private static TemplateContext EmptyContext() => new(
        new Dictionary<string, string>(),
        new Dictionary<string, string>(),
        new Dictionary<string, string>(),
        new Dictionary<string, IReadOnlyDictionary<string, string>>(),
        new Dictionary<string, IReadOnlyDictionary<string, string>>(),
        new Dictionary<string, IReadOnlyDictionary<string, string>>(),
        new Dictionary<string, string>());

    private sealed class FixedTimeProvider(DateTimeOffset value) : SphereIntegrationHub.Services.Interfaces.ISystemTimeProvider
    {
        public DateTimeOffset Now => value;
        public DateTimeOffset UtcNow => value.ToUniversalTime();
    }
}
