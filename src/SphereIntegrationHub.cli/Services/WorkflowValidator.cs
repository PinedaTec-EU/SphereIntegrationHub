using System;
using System.Collections.Generic;

using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services.Plugins;

namespace SphereIntegrationHub.Services;

public sealed class WorkflowValidator
{
    private readonly WorkflowLoader _loader;
    private readonly MockPayloadService _mockPayloadService;
    private readonly IReadOnlyList<IWorkflowValidationStep> _steps;
    private readonly StagePluginRegistry _stagePlugins;
    private readonly StageValidatorRegistry _stageValidators;

    public WorkflowValidator(WorkflowLoader loader, StagePluginRegistry stagePlugins, StageValidatorRegistry stageValidators)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _mockPayloadService = new MockPayloadService();
        _stagePlugins = stagePlugins ?? throw new ArgumentNullException(nameof(stagePlugins));
        _stageValidators = stageValidators ?? throw new ArgumentNullException(nameof(stageValidators));
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
        var context = new WorkflowValidationContext(document, _loader, _mockPayloadService, _stagePlugins, _stageValidators);

        foreach (var step in _steps)
        {
            step.Validate(context, errors);
        }

        return errors;
    }
}
