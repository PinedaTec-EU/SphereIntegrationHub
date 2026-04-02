using System.Text.Json;

using SphereIntegrationHub.Services;

namespace SphereIntegrationHub.Tests;

public sealed class WorkflowExpressionEvaluatorTests
{
    [Fact]
    public void Evaluate_SupportsJsonFunctions()
    {
        var evaluator = CreateEvaluator();
        using var json = JsonDocument.Parse("""[{"id":"a-1"},{"id":"a-2"}]""");
        var context = CreateContext(
            inputs: new Dictionary<string, string>
            {
                ["items"] = json.RootElement.GetRawText()
            },
            inputJson: new Dictionary<string, JsonElement>
            {
                ["items"] = json.RootElement.Clone()
            });

        Assert.True(evaluator.Evaluate("jsonLength({{input.items}}) == 2", context));
        Assert.True(evaluator.Evaluate("exists(first({{input.items}}))", context));
        Assert.True(evaluator.Evaluate("jsonLength(first({{input.items}})) == 1", context));
        Assert.False(evaluator.Evaluate("isEmptyJson({{input.items}})", context));
    }

    [Fact]
    public void Evaluate_SupportsMissingTokensAndControlHelpers()
    {
        var evaluator = CreateEvaluator();
        var context = CreateContext(
            inputs: new Dictionary<string, string>
            {
                ["flag"] = "yes"
            });

        Assert.True(evaluator.Evaluate("{{context:missing}} == null", context));
        Assert.False(evaluator.Evaluate("{{context:missing}} != null", context));
        Assert.False(evaluator.Evaluate("{{context:missing}} > 0", context));
        Assert.False(evaluator.Evaluate("exists({{context:missing}})", context));
        Assert.True(evaluator.Evaluate("empty({{context:missing}})", context));
        Assert.True(evaluator.Evaluate("coalesce({{context:missing}}, 'fallback') == 'fallback'", context));
        Assert.True(evaluator.Evaluate("({{input.flag}} == 'yes' && {{context:missing}} == null)", context));
        Assert.True(evaluator.Evaluate("false || true", context));
    }

    private static WorkflowExpressionEvaluator CreateEvaluator()
    {
        return new WorkflowExpressionEvaluator(new TemplateResolver());
    }

    private static TemplateContext CreateContext(
        IReadOnlyDictionary<string, string>? inputs = null,
        IReadOnlyDictionary<string, string>? globals = null,
        IReadOnlyDictionary<string, string>? contextValues = null,
        IReadOnlyDictionary<string, JsonElement>? inputJson = null)
    {
        return new TemplateContext(
            inputs ?? new Dictionary<string, string>(),
            globals ?? new Dictionary<string, string>(),
            contextValues ?? new Dictionary<string, string>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, string>(),
            inputJson);
    }
}
