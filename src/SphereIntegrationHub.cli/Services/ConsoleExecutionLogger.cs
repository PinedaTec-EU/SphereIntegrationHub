using SphereIntegrationHub.cli;
using SphereIntegrationHub.Services.Interfaces;

namespace SphereIntegrationHub.Services;

public sealed class ConsoleExecutionLogger : IExecutionLogger
{
    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly bool _useColors;

    public ConsoleExecutionLogger()
        : this(Console.Out, Console.Error, useColors: !Console.IsOutputRedirected && !Console.IsErrorRedirected && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NO_COLOR")))
    {
    }

    public ConsoleExecutionLogger(TextWriter output, TextWriter error, bool useColors = false)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _error = error ?? throw new ArgumentNullException(nameof(error));
        _useColors = useColors;
    }

    public void Info(string message) => _output.WriteLine(ConsoleMessageFormatter.FormatInfo(message, _useColors));

    public void Error(string message) => _error.WriteLine(ConsoleMessageFormatter.FormatError(message, _useColors));
}
