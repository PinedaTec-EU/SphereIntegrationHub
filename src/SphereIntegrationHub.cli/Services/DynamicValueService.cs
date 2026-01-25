using System.Globalization;

using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services.Interfaces;

namespace SphereIntegrationHub.Services;

public sealed class DynamicValueService : IRandomValueService
{
    private const int DefaultTextLength = 16;
    private readonly ISystemTimeProvider _systemProvider;

    public DynamicValueService(ISystemTimeProvider? systemProvider = null)
    {
        _systemProvider = systemProvider ?? new SystemTimeProvider();
    }

    public string Generate(RandomValueDefinition definition, PayloadProcessorContext context, RandomValueFormattingOptions formatting)
    {
        using var activity = Telemetry.ActivitySource.StartActivity(TelemetryConstants.ActivityRandomValueGenerate);
        activity?.SetTag(TelemetryConstants.TagRandomType, definition.Type.ToString());

        formatting ??= RandomValueFormattingOptions.Default;

        return definition.Type switch
        {
            RandomValueType.Fixed => ResolveFixedValue(definition),
            RandomValueType.Number => FormatNumber(GenerateRandomNumber(definition.Min ?? 1, definition.Max ?? 100), definition.Padding),
            RandomValueType.Text => GenerateRandomText(definition.Length ?? DefaultTextLength),
            RandomValueType.Guid => Guid.NewGuid().ToString(),
            RandomValueType.Ulid => Ulid.NewUlid().ToString(),
            RandomValueType.DateTime => ResolveDateTimeValue(definition, formatting),
            RandomValueType.Date => ResolveDateValue(definition, formatting),
            RandomValueType.Time => ResolveTimeValue(definition, formatting),
            RandomValueType.Sequence => GenerateSequenceValue(definition, context),
            _ => throw new InvalidOperationException("Unsupported variable type.")
        };
    }

    private static int GenerateRandomNumber(int min, int max)
    {
        if (max < min)
        {
            (min, max) = (max, min);
        }

        if (max == int.MaxValue)
        {
            var value = Random.Shared.NextInt64(min, (long)max + 1);
            return (int)value;
        }

        return Random.Shared.Next(min, max + 1);
    }

    private static string GenerateRandomText(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        Span<char> buffer = length <= 64 ? stackalloc char[length] : new char[length];
        for (var i = 0; i < length; i++)
        {
            buffer[i] = chars[Random.Shared.Next(chars.Length)];
        }

        return new string(buffer);
    }

    private DateTimeOffset GenerateRandomDateTime(DateTimeOffset? from, DateTimeOffset? to)
    {
        var start = from ?? (to.HasValue ? to.Value.AddMonths(-1) : _systemProvider.UtcNow.AddMonths(-1));
        var end = to ?? (from.HasValue ? start.AddMonths(1) : _systemProvider.UtcNow.AddMonths(1));
        if (end < start)
        {
            (start, end) = (end, start);
        }

        var offsetTicks = NextInt64Inclusive(0, end.Ticks - start.Ticks);
        return start.Add(TimeSpan.FromTicks(offsetTicks));
    }

    private DateOnly GenerateRandomDate(DateOnly? from, DateOnly? to)
    {
        var start = from ?? (to.HasValue ? to.Value.AddMonths(-1) : DateOnly.FromDateTime(_systemProvider.UtcNow.AddMonths(-1).DateTime));
        var end = to ?? (from.HasValue ? start.AddMonths(1) : DateOnly.FromDateTime(_systemProvider.UtcNow.AddMonths(1).DateTime));
        if (end < start)
        {
            (start, end) = (end, start);
        }

        var offsetDays = Random.Shared.Next(end.DayNumber - start.DayNumber + 1);
        return start.AddDays(offsetDays);
    }

    private static TimeOnly GenerateRandomTime(TimeOnly? from, TimeOnly? to)
    {
        var start = from ?? TimeOnly.MinValue;
        var end = to ?? TimeOnly.MaxValue;
        if (end < start)
        {
            (start, end) = (end, start);
        }

        var offsetTicks = NextInt64Inclusive(0, end.Ticks - start.Ticks);
        return start.Add(TimeSpan.FromTicks(offsetTicks));
    }

    private static long NextInt64Inclusive(long min, long max)
    {
        if (max <= min)
        {
            return min;
        }

        var range = max - min;
        var offset = Random.Shared.NextInt64(range + 1);
        return min + offset;
    }

    private static string FormatNumber(long value, int? padding)
    {
        if (padding is >= 1)
        {
            var format = $"D{padding}";
            return value.ToString(format, CultureInfo.InvariantCulture);
        }

        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string GenerateSequenceValue(RandomValueDefinition definition, PayloadProcessorContext context)
    {
        if (context is null)
        {
            throw new InvalidOperationException("Sequence variables require a valid context.");
        }

        long start = definition.Start ?? 1;
        long step = Math.Max(1, definition.Step ?? 1);
        var value = checked(start + (context.Index - 1) * step);
        return FormatNumber(value, definition.Padding);
    }

    private static string ResolveFixedValue(RandomValueDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Value))
        {
            throw new InvalidOperationException("Fixed variables require a value.");
        }

        return definition.Value;
    }

    private string ResolveDateTimeValue(RandomValueDefinition definition, RandomValueFormattingOptions formatting)
    {
        return GenerateRandomDateTime(definition.FromDateTime, definition.ToDateTime)
            .ToString(ResolveFormat(definition.Format, formatting.DateTimeFormat), CultureInfo.InvariantCulture);
    }

    private string ResolveDateValue(RandomValueDefinition definition, RandomValueFormattingOptions formatting)
    {
        return GenerateRandomDate(definition.FromDate, definition.ToDate)
            .ToString(ResolveFormat(definition.Format, formatting.DateFormat), CultureInfo.InvariantCulture);
    }

    private static string ResolveTimeValue(RandomValueDefinition definition, RandomValueFormattingOptions formatting)
    {
        return GenerateRandomTime(definition.FromTime, definition.ToTime)
            .ToString(ResolveFormat(definition.Format, formatting.TimeFormat), CultureInfo.InvariantCulture);
    }

    private static string ResolveFormat(string? overrideFormat, string fallback)
    {
        return string.IsNullOrWhiteSpace(overrideFormat) ? fallback : overrideFormat!;
    }
}
