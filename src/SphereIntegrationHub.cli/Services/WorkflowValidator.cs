using SphereIntegrationHub.Definitions;
using System;
using System.Collections.Generic;

namespace SphereIntegrationHub.Services;

public sealed class WorkflowValidator
{
    private readonly WorkflowLoader _loader;
    private readonly MockPayloadService _mockPayloadService;
    private readonly IReadOnlyList<IWorkflowValidationStep> _steps;

    public WorkflowValidator(WorkflowLoader loader)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _mockPayloadService = new MockPayloadService();
        _steps = new IWorkflowValidationStep[]
        {
            new WorkflowMetadataValidationStep(),
            new WorkflowStageValidationStep(),
            new WorkflowTemplateValidationStep()
        };
    }

    public IReadOnlyList<string> Validate(WorkflowDocument document)
    {
        using var activity = Telemetry.ActivitySource.StartActivity(TelemetryConstants.ActivityWorkflowValidate);
        activity?.SetTag(TelemetryConstants.TagWorkflowName, document.Definition.Name);
        var errors = new List<string>();
        var context = new WorkflowValidationContext(document, _loader, _mockPayloadService);

        foreach (var step in _steps)
        {
            step.Validate(context, errors);
        }

        return errors;
    }
}
