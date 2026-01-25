using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services.Interfaces;
using System;

namespace SphereIntegrationHub.Services;

internal sealed class StageMessageEmitter
{
    private readonly TemplateResolver _templateResolver;
    private readonly IExecutionLogger _logger;

    public StageMessageEmitter(TemplateResolver templateResolver, IExecutionLogger logger)
    {
        _templateResolver = templateResolver ?? throw new ArgumentNullException(nameof(templateResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Emit(
        WorkflowDefinition definition,
        WorkflowStageDefinition stage,
        ExecutionContext context,
        ResponseContext? responseContext)
    {
        if (string.IsNullOrWhiteSpace(stage.Message))
        {
            return;
        }

        var resolved = _templateResolver.ResolveTemplate(stage.Message, context.BuildTemplateContext(), responseContext);
        if (string.IsNullOrWhiteSpace(resolved))
        {
            return;
        }

        _logger.Info($"{ExecutionLogFormatter.GetIndent(context)}{ExecutionLogFormatter.FormatStageTag(definition.Name, stage.Name)} message: {resolved}");
    }
}
