using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

using SphereIntegrationHub.Definitions;

namespace SphereIntegrationHub.OpenAIPlugin;

public sealed class OpenAIStagePlugin : StagePluginBase
{
    private const string PluginId = "openai";
    private const string DefaultResponsesEndpoint = "/responses";
    private const string ProviderName = "openai";
    private const int EstimatedCharsPerToken = 4;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public OpenAIStagePlugin()
        : base(PluginId, StagePluginCapabilities.ConnectionStage, WorkflowStageKind.Llm, "OpenAI")
    {
    }

    public override void ValidateStage(
        WorkflowStageDefinition stage,
        StagePluginValidationContext context,
        List<string> errors,
        List<string> warnings)
    {
        var connectionRef = stage.GetConfigString("connectionRef") ?? stage.GetConfigString("connection");
        var apiRef = stage.GetConfigString("apiRef") ?? stage.ApiRef;
        if (string.IsNullOrWhiteSpace(connectionRef) && string.IsNullOrWhiteSpace(apiRef))
        {
            errors.Add($"Stage '{stage.Name}' connectionRef is required for OpenAI stages.");
        }

        var model = stage.GetConfigString("model");
        if (string.IsNullOrWhiteSpace(model) &&
            context.CatalogVersion?.Connections?.Count > 0 &&
            !TryFindConnectionConfigString(context.CatalogVersion, connectionRef, "model", out _))
        {
            errors.Add($"Stage '{stage.Name}' config.model is required for OpenAI stages unless the referenced connection defines config.model.");
        }

        var inlinePrompt = GetNestedString(stage.Config, "prompts", "input", "text") ??
            stage.GetConfigString("inputPrompt") ??
            stage.GetConfigString("prompt");
        var promptFile = GetNestedString(stage.Config, "prompts", "input", "file") ??
            stage.GetConfigString("inputPromptFile") ??
            stage.GetConfigString("promptFile");
        if (string.IsNullOrWhiteSpace(inlinePrompt) && string.IsNullOrWhiteSpace(promptFile))
        {
            errors.Add($"Stage '{stage.Name}' input prompt text or file is required for OpenAI stages.");
        }

        if (!string.IsNullOrWhiteSpace(inlinePrompt) && !string.IsNullOrWhiteSpace(promptFile))
        {
            errors.Add($"Stage '{stage.Name}' cannot define both input prompt text and file.");
        }

        var outputPrompt = GetNestedString(stage.Config, "prompts", "output", "text") ??
            stage.GetConfigString("outputPrompt");
        var outputPromptFile = GetNestedString(stage.Config, "prompts", "output", "file") ??
            stage.GetConfigString("outputPromptFile");
        if (!string.IsNullOrWhiteSpace(outputPrompt) && !string.IsNullOrWhiteSpace(outputPromptFile))
        {
            errors.Add($"Stage '{stage.Name}' cannot define both output prompt text and file.");
        }

        if (GetNestedInt(stage.Config, "limits", "maxInputTokens") is not null)
        {
            warnings.Add($"Stage '{stage.Name}' maxInputTokens is enforced with a local token estimate before the provider request.");
        }
    }

    public override async Task<StagePluginExecutionResult> ExecuteAsync(
        WorkflowStageDefinition stage,
        StagePluginExecutionContext context,
        CancellationToken cancellationToken)
    {
        var requestConfig = BuildRequestConfig(stage, context);
        ValidateEstimatedTokenLimits(stage.Name, requestConfig);

        using var timeoutCts = CreateTimeoutTokenSource(requestConfig.TimeoutSeconds, cancellationToken);
        var effectiveCancellationToken = timeoutCts?.Token ?? cancellationToken;

        var requestBody = BuildRequestBody(requestConfig);
        var requestJson = requestBody.ToJsonString(JsonOptions);
        var requestUri = BuildRequestUrl(requestConfig.BaseUrl, requestConfig.Endpoint);
        var headers = BuildHeaders(requestConfig);

        var stopwatch = Stopwatch.StartNew();
        StageTransportResponse response;
        try
        {
            response = await context.SendAsync(
                new StageTransportRequest("POST", requestUri, headers, requestJson, "application/json"),
                effectiveCancellationToken);
        }
        catch (OperationCanceledException) when (timeoutCts?.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Stage '{stage.Name}' timed out after {requestConfig.TimeoutSeconds}s.");
        }
        finally
        {
            stopwatch.Stop();
        }

        if (response.StatusCode < 200 || response.StatusCode >= 300)
        {
            return new StagePluginExecutionResult(
                new ResponseContext(response.StatusCode, response.Body, response.Headers, TryParseJson(response.Body)),
                response.RequestUri,
                response.Method,
                response.RequestBody);
        }

        var normalized = NormalizeResponse(response, requestConfig, stopwatch.ElapsedMilliseconds);
        ValidateActualTokenLimits(stage.Name, requestConfig, normalized.Usage);

        var body = normalized.Body.ToJsonString(JsonOptions);
        return new StagePluginExecutionResult(
            new ResponseContext(response.StatusCode, body, response.Headers, JsonDocument.Parse(body)),
            response.RequestUri,
            response.Method,
            response.RequestBody,
            normalized.Output,
            normalized.OutputJson);
    }

    private static OpenAIRequestConfig BuildRequestConfig(WorkflowStageDefinition stage, StagePluginExecutionContext context)
    {
        var connectionRef = stage.GetConfigString("connectionRef") ?? stage.GetConfigString("connection");
        var apiRef = stage.GetConfigString("apiRef") ?? stage.ApiRef;
        var connectionName = connectionRef ?? apiRef;
        if (string.IsNullOrWhiteSpace(connectionName))
        {
            throw new InvalidOperationException($"Stage '{stage.Name}' connectionRef is required.");
        }

        var hasConnection = context.ConnectionBaseUrls.TryGetValue(connectionName, out var baseUrl);
        var hasApiDefinition = !hasConnection && context.ApiBaseUrls.TryGetValue(connectionName, out baseUrl);
        if (!hasConnection && !hasApiDefinition)
        {
            throw new InvalidOperationException($"Stage '{stage.Name}' connectionRef '{connectionName}' was not found in api.catalog connections.");
        }

        context.Connections.TryGetValue(connectionName, out var connection);
        context.ApiDefinitions.TryGetValue(connectionName, out var apiDefinition);
        if (!string.IsNullOrWhiteSpace(connection?.Provider) &&
            !string.Equals(connection.Provider, ProviderName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Stage '{stage.Name}' connectionRef '{connectionName}' provider '{connection.Provider}' is not supported by the OpenAI plugin.");
        }

        var apiKeyTemplate = stage.GetConfigString("apiKey") ??
            stage.GetConfigString("apiKeySecret") ??
            connection?.ApiKeySecret ??
            connection?.ApiKey ??
            apiDefinition?.ApiKeySecret ??
            apiDefinition?.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKeyTemplate))
        {
            throw new InvalidOperationException($"Stage '{stage.Name}' requires apiKey/apiKeySecret in stage config or api.catalog.");
        }

        var inputPrompt = ResolvePrompt(stage, context, "input", "inputPrompt", "inputPromptFile", "prompt", "promptFile");
        var outputPrompt = ResolvePrompt(stage, context, "output", "outputPrompt", "outputPromptFile", null, null);
        var systemPrompt = ResolvePrompt(stage, context, "system", "systemPrompt", "systemPromptFile", null, null);
        var developerPrompt = ResolvePrompt(stage, context, "developer", "developerPrompt", "developerPromptFile", null, null);
        var connectionConfig = connection?.Config;

        return new OpenAIRequestConfig(
            BaseUrl: baseUrl!,
            Endpoint: stage.GetConfigString("endpoint") ??
                GetConfigString(connectionConfig, "endpoint") ??
                DefaultResponsesEndpoint,
            ApiKey: context.ResolveTemplate(apiKeyTemplate),
            Model: Require(stage.GetConfigString("model") ?? GetConfigString(connectionConfig, "model"), stage.Name, "model"),
            SystemPrompt: systemPrompt,
            DeveloperPrompt: developerPrompt,
            InputPrompt: inputPrompt,
            OutputPrompt: outputPrompt,
            ReasoningEffort: GetNestedString(stage.Config, "reasoning", "effort") ??
                stage.GetConfigString("thinkingMode") ??
                stage.GetConfigString("reasoningEffort") ??
                GetNestedString(connectionConfig, "reasoning", "effort") ??
                GetConfigString(connectionConfig, "thinkingMode") ??
                GetConfigString(connectionConfig, "reasoningEffort"),
            Temperature: GetNestedDecimal(stage.Config, "generation", "temperature") ?? GetConfigDecimal(stage, "temperature"),
            TopP: GetNestedDecimal(stage.Config, "generation", "topP") ?? GetConfigDecimal(stage, "topP"),
            MaxInputTokens: GetNestedInt(stage.Config, "limits", "maxInputTokens") ?? GetConfigInt(stage, "maxInputTokens"),
            MaxOutputTokens: GetNestedInt(stage.Config, "limits", "maxOutputTokens") ??
                GetNestedInt(stage.Config, "generation", "maxOutputTokens") ??
                GetConfigInt(stage, "maxOutputTokens"),
            MaxTotalTokens: GetNestedInt(stage.Config, "limits", "maxTotalTokens") ?? GetConfigInt(stage, "maxTotalTokens"),
            TimeoutSeconds: GetNestedInt(stage.Config, "limits", "timeoutSeconds") ??
                GetNestedInt(stage.Config, "runtime", "timeoutSeconds") ??
                GetConfigInt(stage, "timeoutSeconds"),
            ResponseFormat: GetNestedString(stage.Config, "generation", "responseFormat") ??
                GetNestedString(stage.Config, "output", "responseFormat") ??
                stage.GetConfigString("responseFormat"),
            JsonSchema: ResolveSchema(stage, context),
            JsonSchemaName: GetNestedString(stage.Config, "output", "schemaName") ??
                stage.GetConfigString("schemaName") ??
                "stage_output",
            JsonSchemaStrict: GetNestedBool(stage.Config, "output", "schemaStrict") ??
                GetConfigBool(stage, "schemaStrict"),
            Organization: stage.GetConfigString("organization") ?? GetConfigString(connectionConfig, "organization"),
            Project: stage.GetConfigString("project") ?? GetConfigString(connectionConfig, "project"));
    }

    private static JsonObject BuildRequestBody(OpenAIRequestConfig config)
    {
        var body = new JsonObject
        {
            ["model"] = config.Model,
            ["input"] = BuildInputMessages(config)
        };

        if (!string.IsNullOrWhiteSpace(config.ReasoningEffort))
        {
            body["reasoning"] = new JsonObject
            {
                ["effort"] = config.ReasoningEffort
            };
        }

        if (config.MaxOutputTokens is not null)
        {
            body["max_output_tokens"] = config.MaxOutputTokens.Value;
        }

        if (config.Temperature is not null)
        {
            body["temperature"] = config.Temperature.Value;
        }

        if (config.TopP is not null)
        {
            body["top_p"] = config.TopP.Value;
        }

        var text = BuildTextFormat(config);
        if (text is not null)
        {
            body["text"] = text;
        }

        return body;
    }

    private static JsonArray BuildInputMessages(OpenAIRequestConfig config)
    {
        var messages = new JsonArray();
        AddMessage(messages, "system", config.SystemPrompt);
        AddMessage(messages, "developer", config.DeveloperPrompt);

        var userText = string.IsNullOrWhiteSpace(config.OutputPrompt)
            ? config.InputPrompt
            : $"{config.InputPrompt}{Environment.NewLine}{Environment.NewLine}{config.OutputPrompt}";
        AddMessage(messages, "user", userText);
        return messages;
    }

    private static void AddMessage(JsonArray messages, string role, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        messages.Add(new JsonObject
        {
            ["role"] = role,
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "input_text",
                    ["text"] = text
                }
            }
        });
    }

    private static JsonObject? BuildTextFormat(OpenAIRequestConfig config)
    {
        var responseFormat = config.ResponseFormat?.Trim();
        if (string.IsNullOrWhiteSpace(responseFormat) && config.JsonSchema is null)
        {
            return null;
        }

        if (config.JsonSchema is not null ||
            string.Equals(responseFormat, "schema", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(responseFormat, "json_schema", StringComparison.OrdinalIgnoreCase))
        {
            if (config.JsonSchema is null)
            {
                throw new InvalidOperationException("responseFormat 'schema' requires output.schema, schema, jsonSchema, or schemaFile.");
            }

            var format = new JsonObject
            {
                ["type"] = "json_schema",
                ["name"] = config.JsonSchemaName,
                ["schema"] = JsonNode.Parse(config.JsonSchema.RootElement.GetRawText())
            };

            if (config.JsonSchemaStrict is not null)
            {
                format["strict"] = config.JsonSchemaStrict.Value;
            }

            return new JsonObject { ["format"] = format };
        }

        if (string.Equals(responseFormat, "json", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(responseFormat, "json_object", StringComparison.OrdinalIgnoreCase))
        {
            return new JsonObject
            {
                ["format"] = new JsonObject { ["type"] = "json_object" }
            };
        }

        return new JsonObject
        {
            ["format"] = new JsonObject { ["type"] = "text" }
        };
    }

    private static NormalizedOpenAIResponse NormalizeResponse(
        StageTransportResponse response,
        OpenAIRequestConfig config,
        long durationMs)
    {
        using var responseJson = JsonDocument.Parse(response.Body);
        var root = responseJson.RootElement;
        var text = ExtractOutputText(root);
        var outputJson = TryParseJsonElement(text);
        var usage = ExtractUsage(root);
        var requestId = TryGetHeader(response.Headers, "x-request-id");
        var finishReason = ExtractFinishReason(root);

        var body = new JsonObject
        {
            ["output"] = new JsonObject
            {
                ["text"] = text,
                ["json"] = outputJson is null ? null : JsonNode.Parse(outputJson.Value.GetRawText())
            },
            ["usage"] = new JsonObject
            {
                ["inputTokens"] = usage.InputTokens,
                ["outputTokens"] = usage.OutputTokens,
                ["totalTokens"] = usage.TotalTokens,
                ["cachedInputTokens"] = usage.CachedInputTokens,
                ["reasoningTokens"] = usage.ReasoningTokens,
                ["model"] = config.Model,
                ["provider"] = ProviderName
            },
            ["finishReason"] = finishReason,
            ["durationMs"] = durationMs,
            ["requestId"] = requestId
        };

        var output = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["text"] = text,
            ["inputTokens"] = FormatInt(usage.InputTokens),
            ["outputTokens"] = FormatInt(usage.OutputTokens),
            ["totalTokens"] = FormatInt(usage.TotalTokens),
            ["cachedInputTokens"] = FormatInt(usage.CachedInputTokens),
            ["reasoningTokens"] = FormatInt(usage.ReasoningTokens),
            ["model"] = config.Model,
            ["provider"] = ProviderName,
            ["finishReason"] = finishReason,
            ["durationMs"] = durationMs.ToString(CultureInfo.InvariantCulture)
        };

        if (!string.IsNullOrWhiteSpace(requestId))
        {
            output["requestId"] = requestId;
        }

        var outputJsonMap = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (outputJson is not null)
        {
            outputJsonMap["json"] = outputJson.Value.Clone();
        }

        outputJsonMap["usage"] = JsonSerializer.SerializeToElement(body["usage"], JsonOptions);
        return new NormalizedOpenAIResponse(body, output, outputJsonMap, usage);
    }

    private static Dictionary<string, string> BuildHeaders(OpenAIRequestConfig config)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Authorization"] = $"Bearer {config.ApiKey}"
        };

        if (!string.IsNullOrWhiteSpace(config.Organization))
        {
            headers["OpenAI-Organization"] = config.Organization;
        }

        if (!string.IsNullOrWhiteSpace(config.Project))
        {
            headers["OpenAI-Project"] = config.Project;
        }

        return headers;
    }

    private static string ResolvePrompt(
        WorkflowStageDefinition stage,
        StagePluginExecutionContext context,
        string promptName,
        string inlineKey,
        string fileKey,
        string? legacyInlineKey,
        string? legacyFileKey)
    {
        var text = GetNestedString(stage.Config, "prompts", promptName, "text") ??
            stage.GetConfigString(inlineKey) ??
            (legacyInlineKey is null ? null : stage.GetConfigString(legacyInlineKey));
        if (!string.IsNullOrWhiteSpace(text))
        {
            return context.ResolveTemplate(text);
        }

        var file = GetNestedString(stage.Config, "prompts", promptName, "file") ??
            stage.GetConfigString(fileKey) ??
            (legacyFileKey is null ? null : stage.GetConfigString(legacyFileKey));
        if (string.IsNullOrWhiteSpace(file))
        {
            return string.Empty;
        }

        return context.ResolveTemplate(context.LoadDataFile(file));
    }

    private static JsonDocument? ResolveSchema(WorkflowStageDefinition stage, StagePluginExecutionContext context)
    {
        var schemaFile = GetNestedString(stage.Config, "output", "schemaFile") ?? stage.GetConfigString("schemaFile");
        if (!string.IsNullOrWhiteSpace(schemaFile))
        {
            return JsonDocument.Parse(context.ResolveTemplate(context.LoadDataFile(schemaFile)));
        }

        var schema = GetNestedValue(stage.Config, "output", "schema") ??
            GetConfigValue(stage.Config, "schema") ??
            GetConfigValue(stage.Config, "jsonSchema");
        if (schema is null)
        {
            return null;
        }

        if (schema is string schemaText)
        {
            return JsonDocument.Parse(context.ResolveTemplate(schemaText));
        }

        return JsonDocument.Parse(JsonSerializer.Serialize(schema, JsonOptions));
    }

    private static void ValidateEstimatedTokenLimits(string stageName, OpenAIRequestConfig config)
    {
        var estimatedInputTokens = EstimateTokens(config.SystemPrompt) +
            EstimateTokens(config.DeveloperPrompt) +
            EstimateTokens(config.InputPrompt) +
            EstimateTokens(config.OutputPrompt);

        if (config.MaxInputTokens is not null && estimatedInputTokens > config.MaxInputTokens.Value)
        {
            throw new InvalidOperationException(
                $"Stage '{stageName}' estimated input tokens {estimatedInputTokens} exceed maxInputTokens {config.MaxInputTokens.Value}.");
        }

        if (config.MaxTotalTokens is not null)
        {
            var plannedOutputTokens = config.MaxOutputTokens ?? 0;
            var plannedTotalTokens = estimatedInputTokens + plannedOutputTokens;
            if (plannedTotalTokens > config.MaxTotalTokens.Value)
            {
                throw new InvalidOperationException(
                    $"Stage '{stageName}' estimated total tokens {plannedTotalTokens} exceed maxTotalTokens {config.MaxTotalTokens.Value}.");
            }
        }
    }

    private static void ValidateActualTokenLimits(string stageName, OpenAIRequestConfig config, OpenAIUsage usage)
    {
        if (config.MaxTotalTokens is not null &&
            usage.TotalTokens is not null &&
            usage.TotalTokens.Value > config.MaxTotalTokens.Value)
        {
            throw new InvalidOperationException(
                $"Stage '{stageName}' consumed {usage.TotalTokens.Value} tokens, exceeding maxTotalTokens {config.MaxTotalTokens.Value}.");
        }
    }

    private static CancellationTokenSource? CreateTimeoutTokenSource(int? timeoutSeconds, CancellationToken cancellationToken)
    {
        if (timeoutSeconds is null or <= 0)
        {
            return null;
        }

        var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds.Value));
        return timeoutCts;
    }

    private static string BuildRequestUrl(string baseUrl, string endpoint)
        => $"{baseUrl.TrimEnd('/')}/{endpoint.TrimStart('/')}";

    private static string Require(string? value, string stageName, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Stage '{stageName}' config.{key} is required.");
        }

        return value;
    }

    private static int EstimateTokens(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        return Math.Max(1, (int)Math.Ceiling(text.Length / (double)EstimatedCharsPerToken));
    }

    private static string ExtractOutputText(JsonElement root)
    {
        if (root.TryGetProperty("output_text", out var outputText) &&
            outputText.ValueKind == JsonValueKind.String)
        {
            return outputText.GetString() ?? string.Empty;
        }

        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var contentItem in content.EnumerateArray())
            {
                if (contentItem.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                {
                    parts.Add(text.GetString() ?? string.Empty);
                }
            }
        }

        return string.Join(Environment.NewLine, parts.Where(static part => part.Length > 0));
    }

    private static string ExtractFinishReason(JsonElement root)
    {
        var status = root.TryGetProperty("status", out var statusElement) && statusElement.ValueKind == JsonValueKind.String
            ? statusElement.GetString()
            : null;
        if (root.TryGetProperty("incomplete_details", out var incomplete) &&
            incomplete.ValueKind == JsonValueKind.Object &&
            incomplete.TryGetProperty("reason", out var reason) &&
            reason.ValueKind == JsonValueKind.String)
        {
            return reason.GetString() ?? status ?? string.Empty;
        }

        return status ?? string.Empty;
    }

    private static OpenAIUsage ExtractUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return new OpenAIUsage(null, null, null, null, null);
        }

        var cachedInputTokens = TryGetInt(usage, "input_tokens_details", "cached_tokens");
        var reasoningTokens = TryGetInt(usage, "output_tokens_details", "reasoning_tokens");
        return new OpenAIUsage(
            TryGetInt(usage, "input_tokens"),
            TryGetInt(usage, "output_tokens"),
            TryGetInt(usage, "total_tokens"),
            cachedInputTokens,
            reasoningTokens);
    }

    private static int? TryGetInt(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    private static int? TryGetInt(JsonElement element, string objectProperty, string property)
        => element.TryGetProperty(objectProperty, out var nested) && nested.ValueKind == JsonValueKind.Object
            ? TryGetInt(nested, property)
            : null;

    private static JsonDocument? TryParseJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonElement? TryParseJsonElement(string text)
    {
        using var json = TryParseJson(text);
        return json?.RootElement.Clone();
    }

    private static string? TryGetHeader(IReadOnlyDictionary<string, string> headers, string name)
        => headers.TryGetValue(name, out var value) ? value : null;

    private static string FormatInt(int? value)
        => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static bool TryFindConnectionConfigString(
        ApiCatalogVersion catalogVersion,
        string? connectionRef,
        string key,
        out string? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(connectionRef))
        {
            return false;
        }

        var connection = catalogVersion.Connections?.FirstOrDefault(item =>
            string.Equals(item.Name, connectionRef, StringComparison.OrdinalIgnoreCase));
        value = GetConfigString(connection?.Config, key);
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string? GetConfigString(Dictionary<string, object?>? config, string key)
        => ConvertToString(GetConfigValue(config, key));

    private static int? GetConfigInt(WorkflowStageDefinition stage, string key)
        => TryParseInt(stage.GetConfigString(key));

    private static decimal? GetConfigDecimal(WorkflowStageDefinition stage, string key)
        => TryParseDecimal(stage.GetConfigString(key));

    private static bool? GetConfigBool(WorkflowStageDefinition stage, string key)
        => TryParseBool(stage.GetConfigString(key));

    private static int? GetNestedInt(Dictionary<string, object?>? config, params string[] path)
        => TryParseInt(ConvertToString(GetNestedValue(config, path)));

    private static decimal? GetNestedDecimal(Dictionary<string, object?>? config, params string[] path)
        => TryParseDecimal(ConvertToString(GetNestedValue(config, path)));

    private static bool? GetNestedBool(Dictionary<string, object?>? config, params string[] path)
        => TryParseBool(ConvertToString(GetNestedValue(config, path)));

    private static string? GetNestedString(Dictionary<string, object?>? config, params string[] path)
        => ConvertToString(GetNestedValue(config, path));

    private static object? GetNestedValue(Dictionary<string, object?>? config, params string[] path)
    {
        object? current = config;
        foreach (var segment in path)
        {
            current = GetConfigValue(current, segment);
            if (current is null)
            {
                return null;
            }
        }

        return current;
    }

    private static object? GetConfigValue(object? value, string key)
    {
        return value switch
        {
            Dictionary<string, object?> dictionary when dictionary.TryGetValue(key, out var found) => found,
            IDictionary<object, object> dictionary => dictionary
                .FirstOrDefault(pair => string.Equals(pair.Key.ToString(), key, StringComparison.OrdinalIgnoreCase))
                .Value,
            _ => null
        };
    }

    private static string? ConvertToString(object? value)
    {
        return value switch
        {
            null => null,
            string text => text,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        };
    }

    private static int? TryParseInt(string? value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static decimal? TryParseDecimal(string? value)
        => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static bool? TryParseBool(string? value)
        => bool.TryParse(value, out var result) ? result : null;

    private sealed record OpenAIRequestConfig(
        string BaseUrl,
        string Endpoint,
        string ApiKey,
        string Model,
        string SystemPrompt,
        string DeveloperPrompt,
        string InputPrompt,
        string OutputPrompt,
        string? ReasoningEffort,
        decimal? Temperature,
        decimal? TopP,
        int? MaxInputTokens,
        int? MaxOutputTokens,
        int? MaxTotalTokens,
        int? TimeoutSeconds,
        string? ResponseFormat,
        JsonDocument? JsonSchema,
        string JsonSchemaName,
        bool? JsonSchemaStrict,
        string? Organization,
        string? Project);

    private sealed record OpenAIUsage(
        int? InputTokens,
        int? OutputTokens,
        int? TotalTokens,
        int? CachedInputTokens,
        int? ReasoningTokens);

    private sealed record NormalizedOpenAIResponse(
        JsonObject Body,
        IReadOnlyDictionary<string, string> Output,
        IReadOnlyDictionary<string, JsonElement> OutputJson,
        OpenAIUsage Usage);
}
