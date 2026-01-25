using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services;
using SphereIntegrationHub.Services.Interfaces;
using ExecutionContext = SphereIntegrationHub.Services.ExecutionContext;

namespace SphereIntegrationHub.Tests;

public sealed class RunIfEvaluatorTests
{
    [Fact]
    public void ShouldRunStage_UsesEnvironmentVariables()
    {
        var evaluator = new RunIfEvaluator(new TestSystemTimeProvider());
        var stage = new WorkflowStageDefinition
        {
            Name = "stage",
            RunIf = "{{env:FLAG}} == 'enabled'"
        };
        var context = new ExecutionContext(
            new Dictionary<string, string>(),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["FLAG"] = "enabled"
            });

        var shouldRun = evaluator.ShouldRunStage(stage, context);

        Assert.True(shouldRun);
    }

    [Fact]
    public void ShouldRunStage_UsesSystemToken()
    {
        var systemProvider = new TestSystemTimeProvider(
            new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero));
        var evaluator = new RunIfEvaluator(systemProvider);
        var stage = new WorkflowStageDefinition
        {
            Name = "stage",
            RunIf = "{{system:date.utcnow}} == '2024-01-02'"
        };
        var context = new ExecutionContext(
            new Dictionary<string, string>(),
            new Dictionary<string, string>());

        var shouldRun = evaluator.ShouldRunStage(stage, context);

        Assert.True(shouldRun);
    }

    [Fact]
    public void ShouldRunStage_EvaluatesListMembership()
    {
        var evaluator = new RunIfEvaluator(new TestSystemTimeProvider());
        var stage = new WorkflowStageDefinition
        {
            Name = "stage",
            RunIf = "{{input:flag}} in [200, 201]"
        };
        var context = new ExecutionContext(
            new Dictionary<string, string>
            {
                ["flag"] = "200"
            },
            new Dictionary<string, string>());

        var shouldRun = evaluator.ShouldRunStage(stage, context);

        Assert.True(shouldRun);
    }

    [Fact]
    public void ShouldRunStage_ThrowsForInvalidExpression()
    {
        var evaluator = new RunIfEvaluator(new TestSystemTimeProvider());
        var stage = new WorkflowStageDefinition
        {
            Name = "stage",
            RunIf = "{{input:flag}} === 'bad'"
        };
        var context = new ExecutionContext(
            new Dictionary<string, string>(),
            new Dictionary<string, string>());

        Action action = () => evaluator.ShouldRunStage(stage, context);

        Assert.Throws<InvalidOperationException>(action);
    }

    private sealed class TestSystemTimeProvider : ISystemTimeProvider
    {
        public TestSystemTimeProvider(DateTimeOffset? now = null)
        {
            Now = now ?? new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
            UtcNow = Now;
        }

        public DateTimeOffset Now { get; }
        public DateTimeOffset UtcNow { get; }
    }
}
