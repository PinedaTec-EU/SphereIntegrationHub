using System.Reflection;

using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services;
using SphereIntegrationHub.Services.Interfaces;

namespace SphereIntegrationHub.Tests;

public sealed class WorkflowExecutorDependencyTests
{
    [Fact]
    public void Ctor_UsesInjectedDependencies()
    {
        using var httpClient = new HttpClient();
        var dynamicValueService = new DynamicValueService();
        var workflowLoader = new WorkflowLoader();
        var varsFileLoader = new VarsFileLoader();
        var templateResolver = new TemplateResolver();
        var mockPayloadService = new MockPayloadService();
        var formatting = new RandomValueFormattingOptions("date", "time", "datetime");
        var systemProvider = new TestSystemTimeProvider();

        var executor = new WorkflowExecutor(
            httpClient,
            dynamicValueService,
            TestStagePlugins.CreateRegistry(),
            workflowLoader,
            varsFileLoader,
            templateResolver,
            mockPayloadService,
            formatting,
            systemProvider);

        Assert.Same(workflowLoader, GetPrivateField<WorkflowLoader>(executor, "_workflowLoader"));
        Assert.Same(varsFileLoader, GetPrivateField<VarsFileLoader>(executor, "_varsFileLoader"));
        Assert.Same(templateResolver, GetPrivateField<TemplateResolver>(executor, "_templateResolver"));
        Assert.Same(mockPayloadService, GetPrivateField<MockPayloadService>(executor, "_mockPayloadService"));
        Assert.Same(formatting, GetPrivateField<RandomValueFormattingOptions>(executor, "_formatting"));
        Assert.Same(systemProvider, GetPrivateField<ISystemTimeProvider>(executor, "_systemProvider"));
    }

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (T)field!.GetValue(instance)!;
    }

    private sealed class TestSystemTimeProvider : ISystemTimeProvider
    {
        public DateTimeOffset Now => new(2024, 1, 1, 1, 2, 3, TimeSpan.Zero);
        public DateTimeOffset UtcNow => new(2024, 1, 1, 1, 2, 3, TimeSpan.Zero);
    }
}
