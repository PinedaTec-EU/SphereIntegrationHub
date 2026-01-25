using System.Collections.Generic;

namespace SphereIntegrationHub.Services;

internal interface IWorkflowValidationStep
{
    void Validate(WorkflowValidationContext context, List<string> errors);
}
