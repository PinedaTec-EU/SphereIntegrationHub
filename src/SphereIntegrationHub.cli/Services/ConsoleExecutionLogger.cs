using SphereIntegrationHub.Services.Interfaces;

namespace SphereIntegrationHub.Services;

public sealed class ConsoleExecutionLogger : IExecutionLogger
{
    private readonly TextWriter _output;
    private readonly TextWriter _error;

    public ConsoleExecutionLogger()
        : this(Console.Out, Console.Error)
    {
    }

    public ConsoleExecutionLogger(TextWriter output, TextWriter error)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _error = error ?? throw new ArgumentNullException(nameof(error));
    }

    public void Info(string message) => _output.WriteLine(message);

    public void Error(string message) => _error.WriteLine(message);
}
