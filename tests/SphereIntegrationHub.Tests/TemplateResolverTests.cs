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
}
