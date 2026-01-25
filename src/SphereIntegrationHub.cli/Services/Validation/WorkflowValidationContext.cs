using SphereIntegrationHub.Definitions;
using System;
using System.Collections.Generic;

namespace SphereIntegrationHub.Services;

internal sealed class WorkflowValidationContext
{
    public WorkflowValidationContext(
        WorkflowDocument document,
        WorkflowLoader loader,
        MockPayloadService mockPayloadService)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        Loader = loader ?? throw new ArgumentNullException(nameof(loader));
        MockPayloadService = mockPayloadService ?? throw new ArgumentNullException(nameof(mockPayloadService));
    }

    public WorkflowDocument Document { get; }
    public WorkflowDefinition Definition => Document.Definition;
    public WorkflowLoader Loader { get; }
    public MockPayloadService MockPayloadService { get; }
    public string WorkflowPath => Document.FilePath;
    public IReadOnlyDictionary<string, string> EnvironmentVariables => Document.EnvironmentVariables;
}
