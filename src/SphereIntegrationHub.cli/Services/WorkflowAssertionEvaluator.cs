using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

using SphereIntegrationHub.Definitions;

namespace SphereIntegrationHub.Services;

public sealed class WorkflowAssertionEvaluator
{
    private const string DefaultOperator = "expression";
    private const string MissingActualMessage = "Assertion actual is required when expression is not provided.";
    private const string MissingOperatorMessage = "Assertion operator is required when expression is not provided.";
    private const string MissingExpectedMessage = "Assertion expected value is required for operator '{0}'.";

    private readonly TemplateResolver _templateResolver;
    private readonly WorkflowExpressionEvaluator _expressionEvaluator;

    public WorkflowAssertionEvaluator(
        TemplateResolver templateResolver,
        WorkflowExpressionEvaluator expressionEvaluator)
    {
        _templateResolver = templateResolver ?? throw new ArgumentNullException(nameof(templateResolver));
        _expressionEvaluator = expressionEvaluator ?? throw new ArgumentNullException(nameof(expressionEvaluator));
    }

    public IReadOnlyList<WorkflowAssertionExecutionRecord> Evaluate(
        IReadOnlyList<WorkflowAssertionDefinition>? assertions,
        TemplateContext templateContext,
        bool defaultBlocking,
        string scope,
        string workflowName,
        string? stageName = null)
    {
        if (assertions is null || assertions.Count == 0)
        {
            return Array.Empty<WorkflowAssertionExecutionRecord>();
        }

        var records = new List<WorkflowAssertionExecutionRecord>(assertions.Count);
        for (var index = 0; index < assertions.Count; index++)
        {
            records.Add(Evaluate(assertions[index], templateContext, defaultBlocking, scope, workflowName, stageName, index));
        }

        return records;
    }

    private WorkflowAssertionExecutionRecord Evaluate(
        WorkflowAssertionDefinition assertion,
        TemplateContext templateContext,
        bool defaultBlocking,
        string scope,
        string workflowName,
        string? stageName,
        int index)
    {
        var name = string.IsNullOrWhiteSpace(assertion.Name)
            ? $"assertion-{index + 1}"
            : assertion.Name!.Trim();
        var record = new WorkflowAssertionExecutionRecord
        {
            Scope = scope,
            WorkflowName = workflowName,
            StageName = stageName,
            Name = name,
            Operator = string.IsNullOrWhiteSpace(assertion.Expression) ? assertion.Operator : DefaultOperator,
            Expression = assertion.Expression,
            Blocking = assertion.Blocking ?? defaultBlocking
        };

        try
        {
            var passed = string.IsNullOrWhiteSpace(assertion.Expression)
                ? EvaluateOperatorAssertion(assertion, templateContext, record)
                : _expressionEvaluator.Evaluate(assertion.Expression!, templateContext);

            record.Status = passed ? "Passed" : "Failed";
            if (!passed && string.IsNullOrWhiteSpace(record.Message))
            {
                record.Message = "Assertion evaluated to false.";
            }

            if (!passed && !record.Blocking)
            {
                record.WarningMessage = "Assertion failure is non-blocking because blocking is disabled.";
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or RegexParseException)
        {
            record.Status = "Failed";
            record.Message = ex.Message;
            if (!record.Blocking)
            {
                record.WarningMessage = "Assertion failure is non-blocking because blocking is disabled.";
            }
        }

        return record;
    }

    private bool EvaluateOperatorAssertion(
        WorkflowAssertionDefinition assertion,
        TemplateContext templateContext,
        WorkflowAssertionExecutionRecord record)
    {
        if (string.IsNullOrWhiteSpace(assertion.Actual))
        {
            throw new InvalidOperationException(MissingActualMessage);
        }

        if (string.IsNullOrWhiteSpace(assertion.Operator))
        {
            throw new InvalidOperationException(MissingOperatorMessage);
        }

        var actual = ResolveTemplateValue(assertion.Actual!, templateContext);
        var operatorName = assertion.Operator.Trim();
        record.Actual = actual;
        record.Expected = ConvertExpected(assertion.Expected, templateContext);

        return operatorName.ToLowerInvariant() switch
        {
            "equals" => CompareWithExpected(actual, record.Expected, operatorName, AreEqual),
            "notequals" => CompareWithExpected(actual, record.Expected, operatorName, (left, right) => !AreEqual(left, right)),
            "contains" => CompareWithExpected(actual, record.Expected, operatorName, Contains),
            "notempty" => !IsEmpty(actual),
            "empty" => IsEmpty(actual),
            "in" => IsInExpected(actual, record.Expected, operatorName),
            "matches" => CompareWithExpected(actual, record.Expected, operatorName, Matches),
            _ => throw new InvalidOperationException($"Unsupported assertion operator '{assertion.Operator}'.")
        };
    }

    private object? ResolveTemplateValue(string template, TemplateContext templateContext)
    {
        var resolved = _templateResolver.ResolveTemplate(template, templateContext);

        return ConvertScalar(resolved);
    }

    private object? ConvertExpected(object? expected, TemplateContext templateContext)
    {
        return expected switch
        {
            null => null,
            string text => ResolveTemplateValue(text, templateContext),
            IEnumerable<object?> values => values.Select(value => ConvertExpected(value, templateContext)).ToArray(),
            _ => expected
        };
    }

    private static bool CompareWithExpected(
        object? actual,
        object? expected,
        string operatorName,
        Func<object?, object?, bool> predicate)
    {
        if (expected is null)
        {
            throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, MissingExpectedMessage, operatorName));
        }

        return predicate(actual, expected);
    }

    private static bool IsInExpected(object? actual, object? expected, string operatorName)
    {
        if (expected is null)
        {
            throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, MissingExpectedMessage, operatorName));
        }

        if (expected is not string && expected is IEnumerable<object?> values)
        {
            return values.Any(value => AreEqual(actual, value));
        }

        return AreEqual(actual, expected);
    }

    private static bool AreEqual(object? actual, object? expected)
    {
        if (actual is null || expected is null)
        {
            return actual is null && expected is null;
        }

        if (TryGetDecimal(actual, out var actualNumber) &&
            TryGetDecimal(expected, out var expectedNumber))
        {
            return actualNumber == expectedNumber;
        }

        return string.Equals(ToDisplayString(actual), ToDisplayString(expected), StringComparison.Ordinal);
    }

    private static bool Contains(object? actual, object? expected)
    {
        if (actual is null || expected is null)
        {
            return false;
        }

        return ToDisplayString(actual).Contains(ToDisplayString(expected), StringComparison.Ordinal);
    }

    private static bool Matches(object? actual, object? expected)
    {
        if (actual is null || expected is null)
        {
            return false;
        }

        return Regex.IsMatch(ToDisplayString(actual), ToDisplayString(expected), RegexOptions.CultureInvariant);
    }

    private static bool IsEmpty(object? value)
    {
        if (value is null)
        {
            return true;
        }

        if (value is string text)
        {
            return string.IsNullOrEmpty(text);
        }

        if (value is JsonElement json)
        {
            return JsonValueHelper.IsEmpty(json);
        }

        return false;
    }

    private static object? ConvertScalar(string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (JsonValueHelper.TryParse(value, out var json))
        {
            return json.ValueKind switch
            {
                JsonValueKind.String => json.GetString(),
                JsonValueKind.Number when json.TryGetDecimal(out var number) => number,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => json.Clone()
            };
        }

        return value;
    }

    private static bool TryGetDecimal(object value, out decimal number)
    {
        switch (value)
        {
            case decimal decimalValue:
                number = decimalValue;
                return true;
            case int intValue:
                number = intValue;
                return true;
            case long longValue:
                number = longValue;
                return true;
            case double doubleValue:
                number = (decimal)doubleValue;
                return true;
            case float floatValue:
                number = (decimal)floatValue;
                return true;
            case string text when decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out number):
                return true;
            case JsonElement json when json.ValueKind == JsonValueKind.Number && json.TryGetDecimal(out number):
                return true;
            default:
                number = 0;
                return false;
        }
    }

    private static string ToDisplayString(object value)
    {
        return value switch
        {
            JsonElement json => JsonValueHelper.ToDisplayString(json),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }
}
