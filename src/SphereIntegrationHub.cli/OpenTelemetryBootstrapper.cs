using System.Diagnostics;

using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace SphereIntegrationHub.cli;

internal sealed class OpenTelemetryBootstrapper : IOpenTelemetryBootstrapper
{
    public OpenTelemetryHandle Start(WorkflowConfig config)
    {
        if (!config.Features.OpenTelemetry)
        {
            return OpenTelemetryHandle.Disabled;
        }

        var serviceName = string.IsNullOrWhiteSpace(config.OpenTelemetry.ServiceName)
            ? CliConstants.DefaultServiceName
            : config.OpenTelemetry.ServiceName!;
        var resource = ResourceBuilder.CreateDefault().AddService(serviceName);

        var tracerProviderBuilder = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(resource)
            .AddSource(CliConstants.ActivitySourceName);

        var meterProviderBuilder = Sdk.CreateMeterProviderBuilder()
            .SetResourceBuilder(resource)
            .AddMeter(CliConstants.MeterName);

        if (config.OpenTelemetry.ConsoleExporter || config.OpenTelemetry.DebugConsole)
        {
            tracerProviderBuilder = tracerProviderBuilder.AddConsoleExporter();
            meterProviderBuilder = meterProviderBuilder.AddConsoleExporter();
        }

        if (!string.IsNullOrWhiteSpace(config.OpenTelemetry.Endpoint))
        {
            tracerProviderBuilder = tracerProviderBuilder.AddOtlpExporter(options =>
                options.Endpoint = new Uri(config.OpenTelemetry.Endpoint!));
            meterProviderBuilder = meterProviderBuilder.AddOtlpExporter(options =>
                options.Endpoint = new Uri(config.OpenTelemetry.Endpoint!));
        }

        var tracerProvider = tracerProviderBuilder.Build();
        var meterProvider = meterProviderBuilder.Build();
        return new OpenTelemetryHandle(tracerProvider, meterProvider);
    }
}

internal sealed class OpenTelemetryHandle : IDisposable
{
    public static OpenTelemetryHandle Disabled { get; } = new(null, null);

    private readonly TracerProvider? _tracerProvider;
    private readonly MeterProvider? _meterProvider;

    public OpenTelemetryHandle(TracerProvider? tracerProvider, MeterProvider? meterProvider)
    {
        _tracerProvider = tracerProvider;
        _meterProvider = meterProvider;
    }

    public void Dispose()
    {
        _meterProvider?.Dispose();
        _tracerProvider?.Dispose();
    }
}
