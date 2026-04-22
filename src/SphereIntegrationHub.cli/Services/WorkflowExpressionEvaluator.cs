using System.Globalization;
using System.Text;
using System.Text.Json;

using SphereIntegrationHub.Definitions;

namespace SphereIntegrationHub.Services;

public sealed class WorkflowExpressionEvaluator
{
    private readonly TemplateResolver _templateResolver;

    public WorkflowExpressionEvaluator(TemplateResolver templateResolver)
    {
        _templateResolver = templateResolver;
    }

    public bool Evaluate(string expression, TemplateContext context, ResponseContext? responseContext = null)
    {
        var parser = new Parser(expression, _templateResolver, context, responseContext);
        var value = parser.ParseExpression();
        parser.EnsureCompleted();
        return value.ToBoolean();
    }

    public static IReadOnlyList<string> ExtractTemplateTokens(string expression)
    {
        return TemplateResolver.ExtractTokens(expression).ToArray();
    }

    private sealed class Parser
    {
        private readonly string _expression;
        private readonly TemplateResolver _resolver;
        private readonly TemplateContext _context;
        private readonly ResponseContext? _responseContext;
        private int _index;

        public Parser(string expression, TemplateResolver resolver, TemplateContext context, ResponseContext? responseContext)
        {
            _expression = expression ?? string.Empty;
            _resolver = resolver;
            _context = context;
            _responseContext = responseContext;
        }

        public Value ParseExpression() => ParseOr();

        public void EnsureCompleted()
        {
            SkipWhitespace();
            if (_index < _expression.Length)
            {
                throw new InvalidOperationException($"Unexpected token near '{_expression[_index..]}'.");
            }
        }

        private Value ParseOr()
        {
            var value = ParseAnd();
            while (true)
            {
                SkipWhitespace();
                if (!TryConsume("||"))
                {
                    return value;
                }

                var right = ParseAnd();
                value = Value.FromBoolean(value.ToBoolean() || right.ToBoolean());
            }
        }

        private Value ParseAnd()
        {
            var value = ParseUnary();
            while (true)
            {
                SkipWhitespace();
                if (!TryConsume("&&"))
                {
                    return value;
                }

                var right = ParseUnary();
                value = Value.FromBoolean(value.ToBoolean() && right.ToBoolean());
            }
        }

        private Value ParseUnary()
        {
            SkipWhitespace();
            if (TryConsume("!"))
            {
                return Value.FromBoolean(!ParseUnary().ToBoolean());
            }

            return ParseComparison();
        }

        private Value ParseComparison()
        {
            var left = ParsePrimary();
            SkipWhitespace();

            if (TryConsumeWord("not"))
            {
                RequireWord("in");
                var rightNotIn = ParsePrimary();
                return Value.FromBoolean(!Contains(rightNotIn, left));
            }

            if (TryConsumeWord("in"))
            {
                var rightIn = ParsePrimary();
                return Value.FromBoolean(Contains(rightIn, left));
            }

            if (TryConsume("=="))
            {
                return Value.FromBoolean(Value.Equals(left, ParsePrimary()));
            }

            if (TryConsume("!="))
            {
                return Value.FromBoolean(!Value.Equals(left, ParsePrimary()));
            }

            if (TryConsume(">="))
            {
                return Value.FromBoolean(EvaluateRelational(left, ParsePrimary(), comparison => comparison >= 0));
            }

            if (TryConsume("<="))
            {
                return Value.FromBoolean(EvaluateRelational(left, ParsePrimary(), comparison => comparison <= 0));
            }

            if (TryConsume(">"))
            {
                return Value.FromBoolean(EvaluateRelational(left, ParsePrimary(), comparison => comparison > 0));
            }

            if (TryConsume("<"))
            {
                return Value.FromBoolean(EvaluateRelational(left, ParsePrimary(), comparison => comparison < 0));
            }

            return left;
        }

        private Value ParsePrimary()
        {
            SkipWhitespace();
            if (_index >= _expression.Length)
            {
                throw new InvalidOperationException("Unexpected end of expression.");
            }

            if (TryConsume("("))
            {
                var nested = ParseExpression();
                SkipWhitespace();
                if (!TryConsume(")"))
                {
                    throw new InvalidOperationException("Missing closing ')' in expression.");
                }

                return nested;
            }

            if (Peek("{{"))
            {
                return ParseTemplateToken();
            }

            if (_expression[_index] == '[')
            {
                return ParseList();
            }

            if (_expression[_index] == '"' || _expression[_index] == '\'')
            {
                return Value.FromString(ParseQuotedString());
            }

            if (char.IsDigit(_expression[_index]) || _expression[_index] == '-')
            {
                return ParseNumber();
            }

            var identifier = ParseIdentifier();
            return identifier.ToLowerInvariant() switch
            {
                "true" => Value.FromBoolean(true),
                "false" => Value.FromBoolean(false),
                "null" => Value.Null,
                _ when Peek("(") => ParseFunction(identifier),
                _ => Value.FromString(identifier)
            };
        }

        private Value ParseTemplateToken()
        {
            _index += 2;
            var closeIndex = _expression.IndexOf("}}", _index, StringComparison.Ordinal);
            if (closeIndex < 0)
            {
                throw new InvalidOperationException("Unclosed template token in expression.");
            }

            var token = _expression[_index..closeIndex].Trim();
            _index = closeIndex + 2;

            try
            {
                var value = _resolver.ResolveTokenValue(token, _context, _responseContext, allowJsonStage: true);
                return Value.FromResolved(value);
            }
            catch (InvalidOperationException ex) when (IsSafeMissingTokenFailure(ex))
            {
                return Value.Missing;
            }
        }

        private Value ParseList()
        {
            _index++;
            var values = new List<Value>();
            while (true)
            {
                SkipWhitespace();
                if (TryConsume("]"))
                {
                    return Value.FromList(values);
                }

                values.Add(ParsePrimary());
                SkipWhitespace();
                if (TryConsume("]"))
                {
                    return Value.FromList(values);
                }

                if (!TryConsume(","))
                {
                    throw new InvalidOperationException("Expected ',' in list expression.");
                }
            }
        }

        private Value ParseNumber()
        {
            var start = _index;
            if (_expression[_index] == '-')
            {
                _index++;
            }

            while (_index < _expression.Length && char.IsDigit(_expression[_index]))
            {
                _index++;
            }

            if (_index < _expression.Length && _expression[_index] == '.')
            {
                _index++;
                while (_index < _expression.Length && char.IsDigit(_expression[_index]))
                {
                    _index++;
                }
            }

            var raw = _expression[start.._index];
            if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
            {
                throw new InvalidOperationException($"Invalid numeric literal '{raw}'.");
            }

            return Value.FromNumber(number);
        }

        private Value ParseFunction(string name)
        {
            Require("(");
            var arguments = new List<Value>();
            while (true)
            {
                SkipWhitespace();
                if (TryConsume(")"))
                {
                    return EvaluateFunction(name, arguments);
                }

                arguments.Add(ParseExpression());
                SkipWhitespace();
                if (TryConsume(")"))
                {
                    return EvaluateFunction(name, arguments);
                }

                if (!TryConsume(","))
                {
                    throw new InvalidOperationException($"Expected ',' in function '{name}'.");
                }
            }
        }

        private Value EvaluateFunction(string name, List<Value> arguments)
        {
            return name.ToLowerInvariant() switch
            {
                "exists" => Value.FromBoolean(arguments.Count == 1 && arguments[0].Exists),
                "empty" => Value.FromBoolean(arguments.Count == 1 && arguments[0].IsEmpty()),
                "coalesce" => ResolveCoalesce(arguments),
                "isemptyjson" => Value.FromBoolean(arguments.Count == 1 && arguments[0].TryGetJson(out var emptyJson) && JsonValueHelper.IsEmpty(emptyJson)),
                "jsonlength" => Value.FromNumber(arguments.Count == 1 && arguments[0].TryGetJson(out var sizedJson) ? JsonValueHelper.GetLength(sizedJson) : 0),
                "first" => ResolveFirst(arguments),
                "any" => ResolveAny(arguments),
                _ => throw new InvalidOperationException($"Unknown function '{name}'.")
            };
        }

        private static Value ResolveFirst(List<Value> arguments)
        {
            if (arguments.Count != 1)
            {
                throw new InvalidOperationException("Function 'first' expects a single argument.");
            }

            var source = arguments[0];
            if (!source.TryGetJson(out var json) || !JsonValueHelper.TryGetFirst(json, out var first))
            {
                return Value.Null;
            }

            return Value.FromJson(first);
        }

        private static Value ResolveAny(List<Value> arguments)
        {
            if (arguments.Count != 1)
            {
                throw new InvalidOperationException("Function 'any' expects a single argument.");
            }

            var source = arguments[0];
            if (source.Kind == ValueKind.List)
            {
                return Value.FromBoolean(source.ListValue?.Count > 0);
            }

            if (source.TryGetJson(out var json))
            {
                return Value.FromBoolean(JsonValueHelper.Any(json));
            }

            return Value.FromBoolean(source.ToBoolean());
        }

        private static Value ResolveCoalesce(List<Value> arguments)
        {
            foreach (var argument in arguments)
            {
                if (!argument.IsNullLike())
                {
                    return argument;
                }
            }

            return Value.Null;
        }

        private static bool Contains(Value haystack, Value needle)
        {
            if (haystack.Kind == ValueKind.List && haystack.ListValue is not null)
            {
                return haystack.ListValue.Any(item => Value.Equals(item, needle));
            }

            if (haystack.TryGetJson(out var json) && json.ValueKind == JsonValueKind.Array)
            {
                return json.EnumerateArray().Any(item => Value.Equals(Value.FromJson(item), needle));
            }

            return false;
        }

        private static bool EvaluateRelational(Value left, Value right, Func<int, bool> predicate)
        {
            if (left.IsNullLike() || right.IsNullLike())
            {
                return false;
            }

            return predicate(left.CompareTo(right));
        }

        private string ParseIdentifier()
        {
            SkipWhitespace();
            var start = _index;
            while (_index < _expression.Length &&
                   (char.IsLetterOrDigit(_expression[_index]) || _expression[_index] == '_' || _expression[_index] == ':'))
            {
                _index++;
            }

            if (start == _index)
            {
                throw new InvalidOperationException($"Unexpected token near '{_expression[_index..]}'.");
            }

            return _expression[start.._index];
        }

        private string ParseQuotedString()
        {
            var quote = _expression[_index++];
            var builder = new StringBuilder();
            while (_index < _expression.Length)
            {
                var current = _expression[_index++];
                if (current == quote)
                {
                    return builder.ToString();
                }

                if (current == '\\' && _index < _expression.Length)
                {
                    builder.Append(_expression[_index++]);
                    continue;
                }

                builder.Append(current);
            }

            throw new InvalidOperationException("Unclosed string literal.");
        }

        private bool Peek(string token)
        {
            SkipWhitespace();
            return _expression.AsSpan(_index).StartsWith(token, StringComparison.Ordinal);
        }

        private bool TryConsume(string token)
        {
            SkipWhitespace();
            if (!_expression.AsSpan(_index).StartsWith(token, StringComparison.Ordinal))
            {
                return false;
            }

            _index += token.Length;
            return true;
        }

        private bool TryConsumeWord(string word)
        {
            SkipWhitespace();
            if (!_expression.AsSpan(_index).StartsWith(word, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var end = _index + word.Length;
            if (end < _expression.Length && char.IsLetterOrDigit(_expression[end]))
            {
                return false;
            }

            _index = end;
            return true;
        }

        private void Require(string token)
        {
            if (!TryConsume(token))
            {
                throw new InvalidOperationException($"Expected '{token}' in expression.");
            }
        }

        private void RequireWord(string word)
        {
            if (!TryConsumeWord(word))
            {
                throw new InvalidOperationException($"Expected '{word}' in expression.");
            }
        }

        private void SkipWhitespace()
        {
            while (_index < _expression.Length && char.IsWhiteSpace(_expression[_index]))
            {
                _index++;
            }
        }

        private static bool IsSafeMissingTokenFailure(InvalidOperationException exception)
        {
            return exception.Message.Contains("was not found", StringComparison.OrdinalIgnoreCase) ||
                   exception.Message.Contains("were not found", StringComparison.OrdinalIgnoreCase);
        }
    }

    private enum ValueKind
    {
        Null,
        String,
        Number,
        Boolean,
        Json,
        List
    }

    private sealed class Value
    {
        public static Value Missing { get; } = new(ValueKind.Null, null, null, null, null, exists: false);
        public static Value Null { get; } = new(ValueKind.Null, null, null, null, null, exists: true);

        private Value(ValueKind kind, string? stringValue, decimal? numberValue, bool? boolValue, JsonElement? jsonValue, bool exists)
        {
            Kind = kind;
            StringValue = stringValue;
            NumberValue = numberValue;
            BoolValue = boolValue;
            JsonValue = jsonValue;
            Exists = exists;
        }

        public ValueKind Kind { get; }
        public string? StringValue { get; }
        public decimal? NumberValue { get; }
        public bool? BoolValue { get; }
        public JsonElement? JsonValue { get; }
        public List<Value>? ListValue { get; private init; }
        public bool Exists { get; }

        public static Value FromString(string? value) => value is null ? Null : new(ValueKind.String, value, null, null, null, exists: true);
        public static Value FromNumber(decimal value) => new(ValueKind.Number, null, value, null, null, exists: true);
        public static Value FromBoolean(bool value) => new(ValueKind.Boolean, null, null, value, null, exists: true);
        public static Value FromJson(JsonElement value) => new(ValueKind.Json, null, null, null, value.Clone(), exists: true);
        public static Value FromList(List<Value> value) => new(ValueKind.List, null, null, null, null, exists: true) { ListValue = value };

        public static Value FromResolved(ResolvedTokenValue value)
        {
            if (!value.Exists)
            {
                return Missing;
            }

            if (value.JsonValue.HasValue)
            {
                return FromJson(value.JsonValue.Value);
            }

            return FromString(value.StringValue);
        }

        public bool TryGetJson(out JsonElement json)
        {
            if (JsonValue.HasValue)
            {
                json = JsonValue.Value;
                return true;
            }

            if (Kind == ValueKind.String && JsonValueHelper.TryParse(StringValue, out json))
            {
                return true;
            }

            json = default;
            return false;
        }

        public bool IsNullLike()
        {
            return !Exists ||
                   Kind == ValueKind.Null ||
                   (TryGetJson(out var json) && json.ValueKind == JsonValueKind.Null);
        }

        public bool IsEmpty()
        {
            if (IsNullLike())
            {
                return true;
            }

            return Kind switch
            {
                ValueKind.String => string.IsNullOrEmpty(StringValue),
                ValueKind.List => ListValue?.Count is not > 0,
                ValueKind.Json when TryGetJson(out var json) => JsonValueHelper.IsEmpty(json),
                _ => false
            };
        }

        public bool ToBoolean()
        {
            return Kind switch
            {
                ValueKind.Boolean => BoolValue ?? false,
                ValueKind.Number => NumberValue.GetValueOrDefault() != 0,
                ValueKind.String => !string.IsNullOrEmpty(StringValue),
                ValueKind.Json when TryGetJson(out var json) => JsonValueHelper.Any(json),
                ValueKind.List => ListValue?.Count > 0,
                _ => false
            };
        }

        public int CompareTo(Value other)
        {
            if (IsNullLike() || other.IsNullLike())
            {
                return 0;
            }

            if (TryGetDecimal(this, out var left) && TryGetDecimal(other, out var right))
            {
                return left.CompareTo(right);
            }

            return string.Compare(ToComparableString(), other.ToComparableString(), StringComparison.Ordinal);
        }

        private string ToComparableString()
        {
            if (Kind == ValueKind.Json && TryGetJson(out var json))
            {
                return JsonValueHelper.ToDisplayString(json);
            }

            return Kind switch
            {
                ValueKind.String => StringValue ?? string.Empty,
                ValueKind.Number => NumberValue?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                ValueKind.Boolean => BoolValue == true ? "true" : "false",
                _ => string.Empty
            };
        }

        public static bool Equals(Value left, Value right)
        {
            if (left.IsNullLike() || right.IsNullLike())
            {
                return left.IsNullLike() && right.IsNullLike();
            }

            if (TryGetDecimal(left, out var leftNumber) && TryGetDecimal(right, out var rightNumber))
            {
                return leftNumber == rightNumber;
            }

            if (left.Kind == ValueKind.Boolean || right.Kind == ValueKind.Boolean)
            {
                return left.ToBoolean() == right.ToBoolean();
            }

            if (left.Kind == ValueKind.Json && left.TryGetJson(out var leftJson) &&
                right.Kind == ValueKind.Json && right.TryGetJson(out var rightJson))
            {
                return leftJson.GetRawText() == rightJson.GetRawText();
            }

            return string.Equals(left.ToComparableString(), right.ToComparableString(), StringComparison.Ordinal);
        }

        private static bool TryGetDecimal(Value value, out decimal number)
        {
            if (value.Kind == ValueKind.Number && value.NumberValue.HasValue)
            {
                number = value.NumberValue.Value;
                return true;
            }

            if (value.Kind == ValueKind.String &&
                decimal.TryParse(value.StringValue, NumberStyles.Number, CultureInfo.InvariantCulture, out number))
            {
                return true;
            }

            if (value.Kind == ValueKind.Json &&
                value.TryGetJson(out var json) &&
                json.ValueKind == JsonValueKind.Number &&
                json.TryGetDecimal(out number))
            {
                return true;
            }

            number = 0;
            return false;
        }
    }
}
