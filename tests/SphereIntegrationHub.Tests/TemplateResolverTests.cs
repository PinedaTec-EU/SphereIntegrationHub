using System.Text.Json;

using SphereIntegrationHub.Definitions;
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

    [Fact]
    public void ResolveTemplate_EnvWithColon_Resolves()
    {
        var resolver = new TemplateResolver();
        var context = new TemplateContext(
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ENVIRONMENT"] = "production"
            });

        var resolved = resolver.ResolveTemplate("{{env:ENVIRONMENT}}", context);

        Assert.Equal("production", resolved);
    }

    [Theory]
    [InlineData("{{env.ENVIRONMENT}}")]
    [InlineData("{{ENV.ENVIRONMENT}}")]
    public void ResolveTemplate_EnvWithDot_Throws(string template)
    {
        var resolver = new TemplateResolver();
        var context = new TemplateContext(
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ENVIRONMENT"] = "production"
            });

        var ex = Assert.Throws<InvalidOperationException>(
            () => resolver.ResolveTemplate(template, context));

        Assert.Contains("env:", ex.Message);
    }

    // -------------------------------------------------------------------------
    // Mejora 4: vars: block — {{var:name}} token
    // -------------------------------------------------------------------------

    [Fact]
    public void ResolveTemplate_WorkflowVar_ResolvesSimpleValue()
    {
        var resolver = new TemplateResolver();
        var context = new TemplateContext(
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, string>(),
            WorkflowVars: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["greeting"] = "Hello World"
            });

        Assert.Equal("Hello World", resolver.ResolveTemplate("{{var:greeting}}", context));
    }

    [Fact]
    public void ResolveTemplate_WorkflowVar_ResolvesCoalesceOverBranches()
    {
        var resolver = new TemplateResolver();
        var endpointOutputs = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["create-b"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["id"] = "sub-tenant-002" }
        };
        var context = new TemplateContext(
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            endpointOutputs,
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, string>(),
            SkippedStages: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "create-a" },
            WorkflowVars: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["subscriptionId"] = "{{coalesce(stage:create-a.output.id, stage:create-b.output.id)}}"
            });

        // var: picks coalesce result — branch A was skipped, branch B has the ID
        Assert.Equal("sub-tenant-002", resolver.ResolveTemplate("{{var:subscriptionId}}", context));
    }

    [Fact]
    public void ResolveTemplate_WorkflowVar_UsedMultipleTimesProducesSameResult()
    {
        var resolver = new TemplateResolver();
        var endpointOutputs = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["create-a"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["id"] = "abc-123" }
        };
        var context = new TemplateContext(
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            endpointOutputs,
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, string>(),
            WorkflowVars: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["subId"] = "{{coalesce(stage:create-a.output.id, stage:create-b.output.id)}}"
            });

        var result = resolver.ResolveTemplate("id={{var:subId}} ref={{var:subId}}", context);

        Assert.Equal("id=abc-123 ref=abc-123", result);
    }

    [Fact]
    public void ResolveTemplate_WorkflowVar_ThrowsWhenVarNotDeclared()
    {
        var resolver = new TemplateResolver();
        var context = EmptyContext();

        var ex = Assert.Throws<InvalidOperationException>(
            () => resolver.ResolveTemplate("{{var:undeclared}}", context));

        Assert.Contains("undeclared", ex.Message);
    }

    // -------------------------------------------------------------------------
    // Mejora 5: onSkip.output — registered outputs take precedence over empty
    // -------------------------------------------------------------------------

    [Fact]
    public void ResolveTemplate_OnSkipOutput_DownstreamSeesRegisteredValue()
    {
        var resolver = new TemplateResolver();
        // Simulate RecordSkippedStage registering onSkip.output for "create-a"
        // (the executor resolves the template at skip time and stores it in EndpointOutputs)
        var endpointOutputs = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            // "create-b" ran normally
            ["create-b"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["id"] = "sub-456" },
            // "create-a" was skipped but had onSkip.output with the value from create-b
            ["create-a"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["id"] = "sub-456" }
        };
        var context = new TemplateContext(
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            endpointOutputs,
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, string>(),
            SkippedStages: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "create-a" });

        // create-a was skipped but has onSkip.output — its output should be visible
        Assert.Equal("sub-456", resolver.ResolveTemplate("{{stage:create-a.output.id}}", context));
    }

    [Fact]
    public void ResolveTemplate_OnSkipOutput_EmptyWhenNoOnSkipDefined()
    {
        var resolver = new TemplateResolver();
        // "create-a" was skipped with no onSkip.output — nothing in EndpointOutputs
        var context = BuildContextWithSkipped(
            endpointOutputs: new(),
            skippedStages: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "create-a" });

        Assert.Equal(string.Empty, resolver.ResolveTemplate("{{stage:create-a.output.id}}", context));
    }

    // -------------------------------------------------------------------------
    // Mejora 1: skipped stages resolve to empty in template strings
    // -------------------------------------------------------------------------

    [Fact]
    public void ResolveTemplate_SkippedStage_ReturnsEmpty()
    {
        var resolver = new TemplateResolver();
        var context = BuildContextWithSkipped(
            endpointOutputs: new(),
            skippedStages: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "create-item" });

        var result = resolver.ResolveTemplate("id={{stage:create-item.output.id}}", context);

        Assert.Equal("id=", result);
    }

    [Fact]
    public void ResolveTemplate_SkippedStage_CoalescePicksSecondBranch()
    {
        var resolver = new TemplateResolver();
        var endpointOutputs = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["create-item-b"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["id"] = "456" }
        };
        var context = BuildContextWithSkipped(
            endpointOutputs,
            skippedStages: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "create-item-a" });

        // Mejora 1 + 2 combined: coalesce over a skipped stage
        var result = resolver.ResolveTemplate(
            "{{coalesce(stage:create-item-a.output.id, stage:create-item-b.output.id)}}",
            context);

        Assert.Equal("456", result);
    }

    // -------------------------------------------------------------------------
    // Mejora 2: coalesce() in template strings
    // -------------------------------------------------------------------------

    [Fact]
    public void ResolveTemplate_Coalesce_ReturnsFirstNonEmpty()
    {
        var resolver = new TemplateResolver();
        var endpointOutputs = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["step-a"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["id"] = "123" },
            ["step-b"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["id"] = "456" }
        };
        var context = BuildContextWithOutputs(endpointOutputs);

        var result = resolver.ResolveTemplate(
            "{{coalesce(stage:step-a.output.id, stage:step-b.output.id)}}",
            context);

        Assert.Equal("123", result);
    }

    [Fact]
    public void ResolveTemplate_Coalesce_SkipsFirstMissingKeyAndReturnsSecond()
    {
        var resolver = new TemplateResolver();
        var endpointOutputs = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["step-a"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            ["step-b"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["id"] = "789" }
        };
        var context = BuildContextWithOutputs(endpointOutputs);

        var result = resolver.ResolveTemplate(
            "{{coalesce(stage:step-a.output.id, stage:step-b.output.id)}}",
            context);

        Assert.Equal("789", result);
    }

    [Fact]
    public void ResolveTemplate_Coalesce_ReturnsEmptyWhenAllMissing()
    {
        var resolver = new TemplateResolver();
        var context = BuildContextWithOutputs(new());

        var result = resolver.ResolveTemplate(
            "{{coalesce(stage:missing-a.output.id, stage:missing-b.output.id)}}",
            context);

        Assert.Equal(string.Empty, result);
    }

    // -------------------------------------------------------------------------
    // Mejora 3: safe navigation with '?'
    // -------------------------------------------------------------------------

    [Fact]
    public void ResolveTemplate_SafeStageNav_ReturnsEmptyWhenStageAbsent()
    {
        var resolver = new TemplateResolver();
        var context = BuildContextWithOutputs(new());

        var result = resolver.ResolveTemplate("id={{stage:missing?.output.id}}", context);

        Assert.Equal("id=", result);
    }

    [Fact]
    public void ResolveTemplate_SafeStageNav_ResolvesWhenStagePresent()
    {
        var resolver = new TemplateResolver();
        var endpointOutputs = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["my-stage"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["id"] = "abc" }
        };
        var context = BuildContextWithOutputs(endpointOutputs);

        var result = resolver.ResolveTemplate("id={{stage:my-stage?.output.id}}", context);

        Assert.Equal("id=abc", result);
    }

    [Fact]
    public void ResolveTemplate_SafeKeyNav_ReturnsEmptyWhenKeyAbsent()
    {
        var resolver = new TemplateResolver();
        var endpointOutputs = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["my-stage"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };
        var context = BuildContextWithOutputs(endpointOutputs);

        var result = resolver.ResolveTemplate("id={{stage:my-stage.output.id?}}", context);

        Assert.Equal("id=", result);
    }

    [Fact]
    public void ResolveTemplate_SafeKeyNav_ResolvesWhenKeyPresent()
    {
        var resolver = new TemplateResolver();
        var endpointOutputs = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["my-stage"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["id"] = "xyz" }
        };
        var context = BuildContextWithOutputs(endpointOutputs);

        var result = resolver.ResolveTemplate("id={{stage:my-stage.output.id?}}", context);

        Assert.Equal("id=xyz", result);
    }

    [Fact]
    public void ResolveTemplate_SafeNav_StillThrowsForNonStageTokens()
    {
        var resolver = new TemplateResolver();
        var context = EmptyContext();

        // Missing input (not a stage token) must still throw — safe nav does not apply
        var ex = Assert.Throws<InvalidOperationException>(
            () => resolver.ResolveTemplate("{{input.missing}}", context));

        Assert.Contains("missing", ex.Message);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static TemplateContext BuildContextWithOutputs(
        Dictionary<string, IReadOnlyDictionary<string, string>> endpointOutputs) =>
        new(
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            endpointOutputs,
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, string>());

    private static TemplateContext BuildContextWithSkipped(
        Dictionary<string, IReadOnlyDictionary<string, string>> endpointOutputs,
        HashSet<string> skippedStages) =>
        new(
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            endpointOutputs,
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, string>(),
            SkippedStages: skippedStages);

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
