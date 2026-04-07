using System.Diagnostics.Metrics;

namespace SphereIntegrationHub.Tests.Helpers;

/// <summary>
/// Captures metric measurements emitted by a named <see cref="Meter"/> during the lifetime
/// of this instance. Create one per test, dispose at the end of the test.
///
/// Observable instruments (gauges) do not push automatically — call
/// <see cref="RecordObservableInstruments"/> to read their current value.
/// </summary>
internal sealed class MetricsCollector : IDisposable
{
    private readonly MeterListener _listener;
    private readonly List<(string Name, double Value, KeyValuePair<string, object?>[] Tags)> _measurements = [];
    private readonly object _syncRoot = new();

    public MetricsCollector(string meterName)
    {
        _listener = new MeterListener();
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == meterName)
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>(Record);
        _listener.SetMeasurementEventCallback<int>(Record);
        _listener.SetMeasurementEventCallback<double>(Record);
        _listener.Start();
    }

    private void Record<T>(Instrument instrument, T value, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? _)
        where T : struct
    {
        var d = Convert.ToDouble(value);
        lock (_syncRoot)
            _measurements.Add((instrument.Name, d, tags.ToArray()));
    }

    /// <summary>Triggers all registered observable instruments to report their current value.</summary>
    public void RecordObservableInstruments() => _listener.RecordObservableInstruments();

    /// <summary>
    /// Net sum of all recorded values for <paramref name="instrumentName"/>.
    /// Correct for <c>Counter</c> (always positive) and <c>UpDownCounter</c> (may be negative).
    /// </summary>
    public double Sum(string instrumentName)
    {
        lock (_syncRoot)
            return _measurements.Where(m => m.Name == instrumentName).Sum(m => m.Value);
    }

    /// <summary>Number of measurement events recorded for <paramref name="instrumentName"/>.</summary>
    public int Count(string instrumentName)
    {
        lock (_syncRoot)
            return _measurements.Count(m => m.Name == instrumentName);
    }

    /// <summary>All individual values recorded for <paramref name="instrumentName"/>.</summary>
    public IReadOnlyList<double> GetValues(string instrumentName)
    {
        lock (_syncRoot)
            return _measurements.Where(m => m.Name == instrumentName).Select(m => m.Value).ToList();
    }

    /// <summary>
    /// All recorded measurements (value + tags snapshot) for <paramref name="instrumentName"/>.
    /// </summary>
    public IReadOnlyList<(double Value, IReadOnlyDictionary<string, object?> Tags)> GetMeasurements(string instrumentName)
    {
        lock (_syncRoot)
            return _measurements
                .Where(m => m.Name == instrumentName)
                .Select(m => (m.Value, (IReadOnlyDictionary<string, object?>)m.Tags.ToDictionary(kv => kv.Key, kv => kv.Value)))
                .ToList();
    }

    public void Dispose() => _listener.Dispose();
}
