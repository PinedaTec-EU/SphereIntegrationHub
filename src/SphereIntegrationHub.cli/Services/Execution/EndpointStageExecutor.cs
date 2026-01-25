using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SphereIntegrationHub.Services;

internal sealed class EndpointStageExecutor : IEndpointStageExecutor
{
    private readonly TemplateResolver _templateResolver;
    private readonly MockPayloadService _mockPayloadService;
    private readonly ISystemTimeProvider _systemProvider;
    private readonly IEndpointInvoker _endpointInvoker;
    private readonly IExecutionLogger _logger;
    private readonly StageMessageEmitter _stageMessageEmitter;

    private sealed record RetryPolicy(
        int MaxRetries,
        int DelayMs,
        IReadOnlyList<int> HttpStatus,
        string? OnExceptionMessage);

    private sealed record CircuitBreakerPolicy(
        string Name,
        int FailureThreshold,
        int BreakMs,
        IReadOnlyList<int> HttpStatus,
        string? OnOpenMessage,
        string? OnBlockedMessage,
        int CloseOnSuccessAttempts);

    public EndpointStageExecutor(
        TemplateResolver templateResolver,
        MockPayloadService mockPayloadService,
        ISystemTimeProvider systemProvider,
        IEndpointInvoker endpointInvoker,
        IExecutionLogger logger,
        StageMessageEmitter stageMessageEmitter)
    {
        _templateResolver = templateResolver ?? throw new ArgumentNullException(nameof(templateResolver));
        _mockPayloadService = mockPayloadService ?? throw new ArgumentNullException(nameof(mockPayloadService));
        _systemProvider = systemProvider ?? throw new ArgumentNullException(nameof(systemProvider));
        _endpointInvoker = endpointInvoker ?? throw new ArgumentNullException(nameof(endpointInvoker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _stageMessageEmitter = stageMessageEmitter ?? throw new ArgumentNullException(nameof(stageMessageEmitter));
    }

    public async Task<string?> ExecuteAsync(
        WorkflowDefinition definition,
        WorkflowStageDefinition stage,
        IReadOnlyDictionary<string, string> apiBaseUrls,
        ExecutionContext context,
        bool verbose,
        string workflowPath,
        bool mocked,
        CancellationToken cancellationToken)
    {
        var retryPolicy = ResolveRetryPolicy(definition, stage);
        var circuitPolicy = ResolveCircuitBreakerPolicy(definition, stage, retryPolicy);
        if (circuitPolicy is not null && IsCircuitOpen(circuitPolicy, context))
        {
            EmitCircuitBreakerMessage(circuitPolicy.OnBlockedMessage, context);
            throw new InvalidOperationException($"Circuit breaker '{circuitPolicy.Name}' is open for stage '{stage.Name}'.");
        }

        ResponseContext responseContext;
        var retriesUsed = 0;
        while (true)
        {
            if (mocked && stage.Mock is not null)
            {
                responseContext = BuildMockResponse(stage, context, workflowPath);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(stage.ApiRef) || !apiBaseUrls.TryGetValue(stage.ApiRef, out var baseUrl))
                {
                    throw new InvalidOperationException($"Stage '{stage.Name}' apiRef '{stage.ApiRef}' was not found in workflow references.");
                }

                EndpointInvocationResult invocation;
                try
                {
                    invocation = await _endpointInvoker.InvokeAsync(
                        stage,
                        baseUrl,
                        context.BuildTemplateContext(),
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    if (retryPolicy is not null && retriesUsed < retryPolicy.MaxRetries)
                    {
                        retriesUsed++;
                        _logger.Info($"{ExecutionLogFormatter.GetIndent(context)}{ExecutionLogFormatter.FormatStageTag(definition.Name, stage.Name)} retrying in {retryPolicy.DelayMs}ms after exception (retry {retriesUsed}/{retryPolicy.MaxRetries}).");
                        await Task.Delay(retryPolicy.DelayMs, cancellationToken);
                        continue;
                    }

                    EmitRetryExceptionMessage(retryPolicy, context);
                    throw new InvalidOperationException($"Stage '{stage.Name}' failed with exception: {ex.Message}", ex);
                }

                responseContext = invocation.Response;

                if (verbose)
                {
                    _logger.Info($"{ExecutionLogFormatter.GetIndent(context)}{ExecutionLogFormatter.FormatStageTag(definition.Name, stage.Name)} request: {invocation.HttpMethod} {invocation.RequestUri}");
                    _logger.Info($"{ExecutionLogFormatter.GetIndent(context)}{ExecutionLogFormatter.FormatStageTag(definition.Name, stage.Name)} response status: {responseContext.StatusCode}.");
                }

                if (responseContext.StatusCode == (int)System.Net.HttpStatusCode.BadRequest)
                {
                    var responseDump = string.IsNullOrWhiteSpace(responseContext.Body) ? "<empty>" : responseContext.Body;
                    _logger.Error($"{ExecutionLogFormatter.GetIndent(context)}{ExecutionLogFormatter.FormatStageTag(definition.Name, stage.Name)} returned 400. Response body: {responseDump}");
                    if (verbose)
                    {
                        var requestBody = string.IsNullOrWhiteSpace(invocation.RequestBody) ? "<empty>" : invocation.RequestBody;
                        _logger.Error($"{ExecutionLogFormatter.GetIndent(context)}{ExecutionLogFormatter.FormatStageTag(definition.Name, stage.Name)} request body: {requestBody}");
                    }
                }

                if (responseContext.StatusCode == (int)System.Net.HttpStatusCode.NotFound)
                {
                    _logger.Error($"{ExecutionLogFormatter.GetIndent(context)}{ExecutionLogFormatter.FormatStageTag(definition.Name, stage.Name)} returned 404 for url: {invocation.RequestUri}");
                }
            }

            if (retryPolicy is not null &&
                retryPolicy.HttpStatus.Contains(responseContext.StatusCode) &&
                retriesUsed < retryPolicy.MaxRetries)
            {
                retriesUsed++;
                _logger.Info($"{ExecutionLogFormatter.GetIndent(context)}{ExecutionLogFormatter.FormatStageTag(definition.Name, stage.Name)} retrying in {retryPolicy.DelayMs}ms (retry {retriesUsed}/{retryPolicy.MaxRetries}).");
                await Task.Delay(retryPolicy.DelayMs, cancellationToken);
                continue;
            }

            break;
        }

        if (circuitPolicy is not null)
        {
            UpdateCircuitBreaker(circuitPolicy, context, responseContext.StatusCode);
        }

        if (stage.ExpectedStatus.HasValue && responseContext.StatusCode != stage.ExpectedStatus.Value)
        {
            throw new InvalidOperationException(
                $"Stage '{stage.Name}' returned {responseContext.StatusCode} but expected {stage.ExpectedStatus.Value}.");
        }

        var stageOutput = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (stage.Output is not null)
        {
            foreach (var output in stage.Output)
            {
                var resolved = _templateResolver.ResolveTemplate(output.Value, context.BuildTemplateContext(), responseContext);
                stageOutput[output.Key] = resolved;
            }
        }

        if (!stageOutput.ContainsKey("http_status"))
        {
            stageOutput["http_status"] = responseContext.StatusCode.ToString();
        }

        context.EndpointOutputs[stage.Name] = stageOutput;
        _stageMessageEmitter.Emit(definition, stage, context, responseContext);

        if (stage.JumpOnStatus is not null &&
            stage.JumpOnStatus.TryGetValue(responseContext.StatusCode, out var jumpTarget))
        {
            return jumpTarget;
        }

        return null;
    }

    private RetryPolicy? ResolveRetryPolicy(WorkflowDefinition definition, WorkflowStageDefinition stage)
    {
        if (stage.Retry is null)
        {
            return null;
        }

        RetryPolicyDefinition? shared = null;
        if (!string.IsNullOrWhiteSpace(stage.Retry.Ref))
        {
            definition.Resilience?.Retries?.TryGetValue(stage.Retry.Ref, out shared);
        }

        var maxRetries = stage.Retry.MaxRetries ?? shared?.MaxRetries;
        var delayMs = stage.Retry.DelayMs ?? shared?.DelayMs;
        var httpStatus = stage.Retry.HttpStatus ?? Array.Empty<int>();

        if (maxRetries is null || delayMs is null || httpStatus.Length == 0)
        {
            return null;
        }

        var onExceptionMessage = stage.Retry.Messages?.OnException;
        return new RetryPolicy(maxRetries.Value, delayMs.Value, httpStatus, onExceptionMessage);
    }

    private CircuitBreakerPolicy? ResolveCircuitBreakerPolicy(
        WorkflowDefinition definition,
        WorkflowStageDefinition stage,
        RetryPolicy? retryPolicy)
    {
        if (stage.CircuitBreaker is null)
        {
            return null;
        }

        CircuitBreakerDefinition? shared = null;
        if (!string.IsNullOrWhiteSpace(stage.CircuitBreaker.Ref))
        {
            definition.Resilience?.CircuitBreakers?.TryGetValue(stage.CircuitBreaker.Ref, out shared);
        }

        var failureThreshold = stage.CircuitBreaker.FailureThreshold ?? shared?.FailureThreshold;
        var breakMs = stage.CircuitBreaker.BreakMs ?? shared?.BreakMs;
        var closeOnSuccessAttempts = stage.CircuitBreaker.CloseOnSuccessAttempts
            ?? shared?.CloseOnSuccessAttempts
            ?? 1;
        var httpStatus = retryPolicy?.HttpStatus ?? Array.Empty<int>();

        if (failureThreshold is null || breakMs is null || httpStatus.Count == 0)
        {
            return null;
        }

        var name = string.IsNullOrWhiteSpace(stage.CircuitBreaker.Ref)
            ? stage.Name
            : stage.CircuitBreaker.Ref;

        var onOpenMessage = stage.CircuitBreaker.Messages?.OnOpen;
        var onBlockedMessage = stage.CircuitBreaker.Messages?.OnBlocked;
        return new CircuitBreakerPolicy(
            name,
            failureThreshold.Value,
            breakMs.Value,
            httpStatus,
            onOpenMessage,
            onBlockedMessage,
            closeOnSuccessAttempts);
    }

    private bool IsCircuitOpen(CircuitBreakerPolicy policy, ExecutionContext context)
    {
        if (!context.CircuitBreakers.TryGetValue(policy.Name, out var state))
        {
            state = new CircuitBreakerState();
            context.CircuitBreakers[policy.Name] = state;
            return false;
        }

        if (state.OpenUntil is null)
        {
            return false;
        }

        if (_systemProvider.UtcNow >= state.OpenUntil.Value)
        {
            state.OpenUntil = null;
            state.ConsecutiveFailures = 0;
            state.ConsecutiveSuccesses = 0;
            state.HalfOpen = true;
            return false;
        }

        return true;
    }

    private void UpdateCircuitBreaker(CircuitBreakerPolicy policy, ExecutionContext context, int statusCode)
    {
        if (!context.CircuitBreakers.TryGetValue(policy.Name, out var state))
        {
            state = new CircuitBreakerState();
            context.CircuitBreakers[policy.Name] = state;
        }

        if (policy.HttpStatus.Contains(statusCode))
        {
            state.ConsecutiveSuccesses = 0;
            if (state.HalfOpen)
            {
                state.OpenUntil = _systemProvider.UtcNow.AddMilliseconds(policy.BreakMs);
                state.HalfOpen = false;
                state.ConsecutiveFailures = 0;
                EmitCircuitBreakerMessage(policy.OnOpenMessage, context);
                return;
            }

            state.ConsecutiveFailures++;
            if (state.ConsecutiveFailures >= policy.FailureThreshold)
            {
                state.OpenUntil = _systemProvider.UtcNow.AddMilliseconds(policy.BreakMs);
                state.ConsecutiveFailures = 0;
                EmitCircuitBreakerMessage(policy.OnOpenMessage, context);
            }
            return;
        }

        state.ConsecutiveFailures = 0;
        if (state.HalfOpen)
        {
            state.ConsecutiveSuccesses++;
            if (state.ConsecutiveSuccesses >= policy.CloseOnSuccessAttempts)
            {
                state.HalfOpen = false;
                state.ConsecutiveSuccesses = 0;
            }
            return;
        }

        state.ConsecutiveSuccesses = 0;
    }

    private void EmitCircuitBreakerMessage(string? message, ExecutionContext context)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var resolved = _templateResolver.ResolveTemplate(message, context.BuildTemplateContext());
        if (!string.IsNullOrWhiteSpace(resolved))
        {
            _logger.Info($"{ExecutionLogFormatter.GetIndent(context)}{resolved}");
        }
    }

    private void EmitRetryExceptionMessage(RetryPolicy? policy, ExecutionContext context)
    {
        if (policy is null || string.IsNullOrWhiteSpace(policy.OnExceptionMessage))
        {
            return;
        }

        var resolved = _templateResolver.ResolveTemplate(policy.OnExceptionMessage, context.BuildTemplateContext());
        if (!string.IsNullOrWhiteSpace(resolved))
        {
            _logger.Error($"{ExecutionLogFormatter.GetIndent(context)}{resolved}");
        }
    }

    private ResponseContext BuildMockResponse(WorkflowStageDefinition stage, ExecutionContext context, string workflowPath)
    {
        if (!string.IsNullOrWhiteSpace(stage.Mock?.Payload) && !string.IsNullOrWhiteSpace(stage.Mock?.PayloadFile))
        {
            throw new InvalidOperationException($"Stage '{stage.Name}' mock cannot define both payload and payloadFile.");
        }

        var rawPayload = string.IsNullOrWhiteSpace(stage.Mock?.PayloadFile)
            ? _mockPayloadService.LoadRawPayload(stage.Mock?.Payload ?? string.Empty, workflowPath)
            : _mockPayloadService.LoadRawPayloadFromFile(stage.Mock?.PayloadFile ?? string.Empty, workflowPath);

        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            throw new InvalidOperationException($"Stage '{stage.Name}' mock payload is required.");
        }
        var resolvedPayload = _templateResolver.ResolveTemplate(rawPayload, context.BuildTemplateContext());
        JsonDocument json;
        try
        {
            json = JsonDocument.Parse(resolvedPayload);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Stage '{stage.Name}' mock payload is not valid JSON: {ex.Message}");
        }

        var status = stage.Mock?.Status ?? stage.ExpectedStatus ?? 200;
        return new ResponseContext(
            status,
            resolvedPayload,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            json);
    }
}
