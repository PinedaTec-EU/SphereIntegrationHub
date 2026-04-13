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

    // -------------------------------------------------------------------------
    // Mejora 4: var: tokens in runIf expressions
    // -------------------------------------------------------------------------

    [Fact]
    public void Evaluate_WorkflowVar_UsedInRunIfExpression()
    {
        var evaluator = CreateEvaluator();
        var endpointOutputs = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["create-b"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["id"] = "sub-tenant-002" }
        };
        var context = CreateContextWithOutputsAndSkipped(
            endpointOutputs,
            skippedStages: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "create-a" },
            workflowVars: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["subscriptionId"] = "{{coalesce(stage:create-a.output.id, stage:create-b.output.id)}}"
            });

        Assert.True(evaluator.Evaluate("{{var:subscriptionId}} != null", context));
        Assert.True(evaluator.Evaluate("{{var:subscriptionId}} == 'sub-tenant-002'", context));
    }

    [Fact]
    public void Evaluate_WorkflowVar_EmptyVarActsAsNullInExpression()
    {
        var evaluator = CreateEvaluator();
        var context = CreateContextWithOutputsAndSkipped(
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            skippedStages: null,
            workflowVars: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["emptyVar"] = "{{coalesce(stage:missing-a.output.id, stage:missing-b.output.id)}}"
            });

        // coalesce finds nothing → empty string → null-like in expression
        Assert.True(evaluator.Evaluate("empty({{var:emptyVar}})", context));
    }

    // -------------------------------------------------------------------------
    // Mejora 5: onSkip.output — downstream runIf sees the registered value
    // -------------------------------------------------------------------------

    [Fact]
    public void Evaluate_OnSkipOutput_DownstreamRunIfSeesValue()
    {
        var evaluator = CreateEvaluator();
        // create-a was skipped but registered onSkip.output — value comes from create-b
        var endpointOutputs = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["create-b"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["id"] = "sub-456" },
            ["create-a"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["id"] = "sub-456" }
        };
        var context = CreateContextWithOutputsAndSkipped(
            endpointOutputs,
            skippedStages: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "create-a" });

        // Both names resolve to the same value — downstream stage can use either
        Assert.True(evaluator.Evaluate("{{stage:create-a.output.id}} != null", context));
        Assert.True(evaluator.Evaluate("{{stage:create-b.output.id}} == {{stage:create-a.output.id}}", context));
    }

    // -------------------------------------------------------------------------
    // Mejora 1: skipped stages appear as null/missing in runIf expressions
    // -------------------------------------------------------------------------

    [Fact]
    public void Evaluate_SkippedStage_TokenResolvesToNull()
    {
        var evaluator = CreateEvaluator();
        var context = CreateContextWithSkipped(
            skippedStages: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "create-item" });

        // A skipped stage token is null-like — comparisons against null work
        Assert.True(evaluator.Evaluate("{{stage:create-item.output.id}} == null", context));
        Assert.False(evaluator.Evaluate("{{stage:create-item.output.id}} != null", context));
        Assert.False(evaluator.Evaluate("exists({{stage:create-item.output.id}})", context));
        Assert.True(evaluator.Evaluate("empty({{stage:create-item.output.id}})", context));
    }

    [Fact]
    public void Evaluate_SkippedStage_CoalescePicksFallback()
    {
        var evaluator = CreateEvaluator();
        var context = CreateContextWithSkipped(
            skippedStages: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "create-a" });

        // coalesce picks first non-null — skipped stage counts as null
        Assert.True(evaluator.Evaluate("coalesce({{stage:create-a.output.id}}, 'fallback') == 'fallback'", context));
    }

    [Fact]
    public void Evaluate_SkippedStage_ExclusiveBranchRunIfPattern()
    {
        var evaluator = CreateEvaluator();
        var endpointOutputs = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["create-b"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["subscriptionId"] = "sub-456"
            }
        };
        var context = CreateContextWithOutputsAndSkipped(
            endpointOutputs,
            skippedStages: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "create-a" });

        // The downstream runIf pattern: either branch produced an ID
        Assert.True(evaluator.Evaluate(
            "{{stage:create-a.output.subscriptionId}} != null || {{stage:create-b.output.subscriptionId}} != null",
            context));
        Assert.True(evaluator.Evaluate(
            "coalesce({{stage:create-a.output.subscriptionId}}, {{stage:create-b.output.subscriptionId}}, '') != ''",
            context));
    }

    // -------------------------------------------------------------------------
    // Mejora 3: safe navigation in expression context
    // -------------------------------------------------------------------------

    [Fact]
    public void Evaluate_SafeStageNav_ReturnsNullWhenStageAbsent()
    {
        var evaluator = CreateEvaluator();
        var context = CreateContext();

        Assert.True(evaluator.Evaluate("{{stage:missing?.output.id}} == null", context));
        Assert.False(evaluator.Evaluate("exists({{stage:missing?.output.id}})", context));
    }

    [Fact]
    public void Evaluate_SafeKeyNav_ReturnsNullWhenKeyAbsent()
    {
        var evaluator = CreateEvaluator();
        var endpointOutputs = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["my-stage"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            // no "id" key
        };
        var context = CreateContextWithOutputsAndSkipped(endpointOutputs, null);

        Assert.True(evaluator.Evaluate("{{stage:my-stage.output.id?}} == null", context));
        Assert.False(evaluator.Evaluate("exists({{stage:my-stage.output.id?}})", context));
    }

    [Fact]
    public void Evaluate_SafeNav_StillResolvesWhenPresent()
    {
        var evaluator = CreateEvaluator();
        var endpointOutputs = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["my-stage"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = "abc-123"
            }
        };
        var context = CreateContextWithOutputsAndSkipped(endpointOutputs, null);

        Assert.True(evaluator.Evaluate("{{stage:my-stage?.output.id}} == 'abc-123'", context));
        Assert.True(evaluator.Evaluate("{{stage:my-stage.output.id?}} == 'abc-123'", context));
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

    private static TemplateContext CreateContextWithSkipped(HashSet<string> skippedStages)
    {
        return new TemplateContext(
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, string>(),
            SkippedStages: skippedStages);
    }

    private static TemplateContext CreateContextWithOutputsAndSkipped(
        Dictionary<string, IReadOnlyDictionary<string, string>> endpointOutputs,
        HashSet<string>? skippedStages,
        Dictionary<string, string>? workflowVars = null)
    {
        return new TemplateContext(
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            endpointOutputs,
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(),
            new Dictionary<string, string>(),
            SkippedStages: skippedStages,
            WorkflowVars: workflowVars);
    }
}
