using SphereIntegrationHub.Definitions;

namespace SphereIntegrationHub.Services.Interfaces;

public interface IRandomValueService
{
    string Generate(RandomValueDefinition definition, PayloadProcessorContext context, RandomValueFormattingOptions formatting);
}
