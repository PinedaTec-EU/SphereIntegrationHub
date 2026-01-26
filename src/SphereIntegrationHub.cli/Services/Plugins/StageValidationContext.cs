using System;
using System.Collections.Generic;

using SphereIntegrationHub.Definitions;

namespace SphereIntegrationHub.Services.Plugins;

public sealed class StageValidationContext
{
    public StageValidationContext(
        WorkflowDocument document,
        WorkflowLoader loader,
        MockPayloadService mockPayloadService,
        IReadOnlyDictionary<string, string> workflowReferences,
        ISet<string> apiReferences)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        Loader = loader ?? throw new ArgumentNullException(nameof(loader));
        MockPayloadService = mockPayloadService ?? throw new ArgumentNullException(nameof(mockPayloadService));
        WorkflowReferences = workflowReferences ?? throw new ArgumentNullException(nameof(workflowReferences));
        ApiReferences = apiReferences ?? throw new ArgumentNullException(nameof(apiReferences));
    }

    public WorkflowDocument Document { get; }
    public WorkflowDefinition Definition => Document.Definition;
    public WorkflowLoader Loader { get; }
    public MockPayloadService MockPayloadService { get; }
    public IReadOnlyDictionary<string, string> WorkflowReferences { get; }
    public ISet<string> ApiReferences { get; }
    public string WorkflowPath => Document.FilePath;
    public IReadOnlyDictionary<string, string> EnvironmentVariables => Document.EnvironmentVariables;
    public WorkflowResilienceDefinition? Resilience => Document.Definition.Resilience;
}
