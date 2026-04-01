using System.Text.Json;

using SphereIntegrationHub.Services;

namespace SphereIntegrationHub.Tests;

public sealed class WorkflowExpressionEvaluatorTests
{
    [Fact]
    public void Evaluate_SupportsJsonFunctions()
    {
        var resolver = new TemplateResolver();
        var evaluator = new WorkflowExpressionEvaluator(resolver);
        using var json = JsonDocument.Parse("""[{"id":"a-1"},{"id":"a-2"}]""");
        var context = new TemplateContext(
            new Dictionary<string, string>
            {
                ["items"] = json.RootElement.GetRawText()
            },
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, string>(),
            new Dictionary<string, JsonElement>
            {
                ["items"] = json.RootElement.Clone()
            });

        Assert.True(evaluator.Evaluate("jsonLength({{input.items}}) == 2", context));
        Assert.True(evaluator.Evaluate("exists(first({{input.items}}))", context));
        Assert.True(evaluator.Evaluate("jsonLength(first({{input.items}})) == 1", context));
        Assert.False(evaluator.Evaluate("isEmptyJson({{input.items}})", context));
    }
}
