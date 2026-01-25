namespace SphereIntegrationHub.Services;

public sealed class MockedSelfJumpException : InvalidOperationException
{
    public MockedSelfJumpException(string workflowName, string stageName, string jumpTarget)
        : base($"Stage '{workflowName}/{stageName}' mock caused a self-jump to '{jumpTarget}', which would loop indefinitely under --mocked.")
    {
    }
}
