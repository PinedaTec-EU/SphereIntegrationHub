using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services;
using SphereIntegrationHub.Services.Interfaces;

namespace SphereIntegrationHub.Tests;

public sealed class WorkflowAssertionTests
{
    [Fact]
    public async Task ExecuteAsync_WithPassingStageAndEndStageAssertions_RecordsAssertionResults()
    {
        var reportWriter = new CapturingFakeReportWriter();
        var executor = CreateExecutor(reportWriter);
        var document = CreateDocument(new List<WorkflowAssertionDefinition>
        {
            new()
            {
                Name = "account id is returned",
                Actual = "{{stage:create-account.output.accountId}}",
                Operator = "notEmpty"
            },
            new()
            {
                Name = "login round-trips",
                Actual = "{{stage:create-account.output.login}}",
                Operator = "equals",
                Expected = "{{input.accountLogin}}"
            },
            new()
            {
                Name = "create response is accepted",
                Actual = "{{stage:create-account.output.httpStatus}}",
                Operator = "in",
                Expected = new List<object?> { 201, 409 }
            }
        },
        new List<WorkflowAssertionDefinition>
        {
            new()
            {
                Name = "workflow produced account id",
                Expression = "{{stage:create-account.output.accountId}} != null && {{stage:create-account.output.status}} == 'active'"
            }
        });

        await executor.ExecuteAsync(
            document,
            CreateCatalogVersion(),
            "test",
            new Dictionary<string, string>
            {
                ["accountLogin"] = "demo@example.com"
            },
            varsOverrideActive: false,
            mocked: true,
            verbose: false,
            debug: false,
            cancellationToken: CancellationToken.None);

        Assert.NotNull(reportWriter.Report);
        Assert.Equal("Ok", reportWriter.Report!.Result);
        Assert.Equal(4, reportWriter.Report.Metrics.TotalAssertions);
        Assert.Equal(4, reportWriter.Report.Metrics.PassedAssertions);
        Assert.Equal(0, reportWriter.Report.Metrics.FailedAssertions);
        Assert.All(reportWriter.Report.Assertions, assertion => Assert.Equal("Passed", assertion.Status));

        var stage = Assert.Single(reportWriter.Report.Stages);
        Assert.Equal(3, stage.Assertions.Count);
        Assert.Equal("account id is returned", stage.Assertions[0].Name);
        Assert.Equal("EndStage", reportWriter.Report.Assertions[^1].Scope);
    }

    [Fact]
    public async Task ExecuteAsync_WithFailingAssertion_FailsWorkflowAndKeepsReportDiagnostics()
    {
        var reportWriter = new CapturingFakeReportWriter();
        var executor = CreateExecutor(reportWriter);
        var document = CreateDocument(new List<WorkflowAssertionDefinition>
        {
            new()
            {
                Name = "account is suspended",
                Actual = "{{stage:create-account.output.status}}",
                Operator = "equals",
                Expected = "suspended"
            }
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(
            document,
            CreateCatalogVersion(),
            "test",
            new Dictionary<string, string>
            {
                ["accountLogin"] = "demo@example.com"
            },
            varsOverrideActive: false,
            mocked: true,
            verbose: false,
            debug: false,
            cancellationToken: CancellationToken.None));

        Assert.Contains("Stage 'create-account' assertion failed: account is suspended", exception.Message, StringComparison.Ordinal);
        Assert.NotNull(reportWriter.Report);
        Assert.Equal("Error", reportWriter.Report!.Result);
        Assert.Equal(1, reportWriter.Report.Metrics.TotalAssertions);
        Assert.Equal(0, reportWriter.Report.Metrics.PassedAssertions);
        Assert.Equal(1, reportWriter.Report.Metrics.FailedAssertions);
        Assert.Equal("Failed", reportWriter.Report.Assertions[0].Status);
        Assert.Equal("Assertion evaluated to false.", reportWriter.Report.Assertions[0].Message);
        Assert.Equal("Error", reportWriter.Report.Stages[0].Status);
    }

    [Fact]
    public async Task ExecuteAsync_WithNonBlockingAssertionFailure_CompletesWorkflowAndRecordsWarning()
    {
        var reportWriter = new CapturingFakeReportWriter();
        using var output = new StringWriter();
        var executor = CreateExecutor(reportWriter, assertionFailuresBlock: false, output);
        var document = CreateDocument(new List<WorkflowAssertionDefinition>
        {
            new()
            {
                Name = "account is suspended",
                Actual = "{{stage:create-account.output.status}}",
                Operator = "equals",
                Expected = "suspended"
            }
        });

        await executor.ExecuteAsync(
            document,
            CreateCatalogVersion(),
            "test",
            new Dictionary<string, string>
            {
                ["accountLogin"] = "demo@example.com"
            },
            varsOverrideActive: false,
            mocked: true,
            verbose: false,
            debug: false,
            cancellationToken: CancellationToken.None);

        Assert.NotNull(reportWriter.Report);
        Assert.Equal("Ok", reportWriter.Report!.Result);
        Assert.Equal(1, reportWriter.Report.Metrics.TotalAssertions);
        Assert.Equal(1, reportWriter.Report.Metrics.FailedAssertions);
        Assert.Equal(1, reportWriter.Report.Metrics.WarningAssertions);
        Assert.False(reportWriter.Report.Assertions[0].Blocking);
        Assert.Contains("blocking is disabled", reportWriter.Report.Assertions[0].WarningMessage, StringComparison.Ordinal);
        Assert.Contains("Warning:", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_WithAssertionBlockingFalse_OverridesBlockingDefaultTrue()
    {
        var reportWriter = new CapturingFakeReportWriter();
        var executor = CreateExecutor(reportWriter, assertionFailuresBlock: true);
        var document = CreateDocument(new List<WorkflowAssertionDefinition>
        {
            new()
            {
                Name = "account is suspended",
                Actual = "{{stage:create-account.output.status}}",
                Operator = "equals",
                Expected = "suspended",
                Blocking = false
            }
        });

        await executor.ExecuteAsync(
            document,
            CreateCatalogVersion(),
            "test",
            new Dictionary<string, string>
            {
                ["accountLogin"] = "demo@example.com"
            },
            varsOverrideActive: false,
            mocked: true,
            verbose: false,
            debug: false,
            cancellationToken: CancellationToken.None);

        Assert.NotNull(reportWriter.Report);
        Assert.Equal("Ok", reportWriter.Report!.Result);
        Assert.False(reportWriter.Report.Assertions[0].Blocking);
        Assert.Equal(1, reportWriter.Report.Metrics.WarningAssertions);
    }

    [Fact]
    public async Task ExecuteAsync_WithAssertionBlockingTrue_OverridesBlockingDefaultFalse()
    {
        var reportWriter = new CapturingFakeReportWriter();
        var executor = CreateExecutor(reportWriter, assertionFailuresBlock: false);
        var document = CreateDocument(new List<WorkflowAssertionDefinition>
        {
            new()
            {
                Name = "account is suspended",
                Actual = "{{stage:create-account.output.status}}",
                Operator = "equals",
                Expected = "suspended",
                Blocking = true
            }
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(
            document,
            CreateCatalogVersion(),
            "test",
            new Dictionary<string, string>
            {
                ["accountLogin"] = "demo@example.com"
            },
            varsOverrideActive: false,
            mocked: true,
            verbose: false,
            debug: false,
            cancellationToken: CancellationToken.None));

        Assert.NotNull(reportWriter.Report);
        Assert.Equal("Error", reportWriter.Report!.Result);
        Assert.True(reportWriter.Report.Assertions[0].Blocking);
        Assert.Equal(0, reportWriter.Report.Metrics.WarningAssertions);
    }

    [Theory]
    [MemberData(nameof(InvalidAssertions))]
    public async Task ExecuteAsync_WithInvalidAssertionConfiguration_FailsWithRecordedMessage(
        WorkflowAssertionDefinition assertion,
        string expectedMessage)
    {
        var reportWriter = new CapturingFakeReportWriter();
        var executor = CreateExecutor(reportWriter);
        var document = CreateDocument(new List<WorkflowAssertionDefinition> { assertion });

        await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(
            document,
            CreateCatalogVersion(),
            "test",
            new Dictionary<string, string>
            {
                ["accountLogin"] = "demo@example.com"
            },
            varsOverrideActive: false,
            mocked: true,
            verbose: false,
            debug: false,
            cancellationToken: CancellationToken.None));

        Assert.NotNull(reportWriter.Report);
        var recorded = Assert.Single(reportWriter.Report!.Assertions);
        Assert.Equal("Failed", recorded.Status);
        Assert.Contains(expectedMessage, recorded.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_WithNullAssertionName_UsesStableFallbackName()
    {
        var reportWriter = new CapturingFakeReportWriter();
        var executor = CreateExecutor(reportWriter);
        var document = CreateDocument(new List<WorkflowAssertionDefinition>
        {
            new()
            {
                Name = null,
                Actual = "{{stage:create-account.output.accountId}}",
                Operator = "notEmpty"
            }
        });

        await executor.ExecuteAsync(
            document,
            CreateCatalogVersion(),
            "test",
            new Dictionary<string, string>
            {
                ["accountLogin"] = "demo@example.com"
            },
            varsOverrideActive: false,
            mocked: true,
            verbose: false,
            debug: false,
            cancellationToken: CancellationToken.None);

        Assert.NotNull(reportWriter.Report);
        Assert.Equal("assertion-1", reportWriter.Report!.Assertions[0].Name);
        Assert.Equal("Passed", reportWriter.Report.Assertions[0].Status);
    }

    [Fact]
    public void Load_WithYamlAssertions_DeserializesStageAndEndStageAssertions()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"sih-assertions-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var workflowPath = Path.Combine(tempRoot, "assertions.workflow");
        File.WriteAllText(workflowPath, """
version: "3.11"
id: "assertions-demo"
name: "assertions-demo"
stages:
  - name: "create-account"
    kind: "Endpoint"
    assertions:
      - name: "account id is returned"
        actual: "{{stage:create-account.output.accountId}}"
        operator: "notEmpty"
        blocking: false
      - expression: "{{stage:create-account.output.accountId}} != null"
endStage:
  assertions:
    - name: "workflow produced account id"
      actual: "{{stage:create-account.output.accountId}}"
      operator: "notEmpty"
""");

        try
        {
            var document = new WorkflowLoader().Load(workflowPath);

            Assert.NotNull(document.Definition.Stages);
            var stage = Assert.Single(document.Definition.Stages!);
            Assert.Equal(2, stage.Assertions!.Count);
            Assert.Equal("notEmpty", stage.Assertions[0].Operator);
            Assert.False(stage.Assertions[0].Blocking);
            Assert.Null(stage.Assertions[1].Name);
            Assert.NotNull(document.Definition.EndStage?.Assertions);
            Assert.Single(document.Definition.EndStage!.Assertions!);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    public static IEnumerable<object[]> InvalidAssertions()
    {
        yield return
        [
            new WorkflowAssertionDefinition
            {
                Name = "operator is missing",
                Actual = "{{stage:create-account.output.accountId}}",
                Operator = null
            },
            "operator is required"
        ];
        yield return
        [
            new WorkflowAssertionDefinition
            {
                Name = "actual is missing",
                Actual = null,
                Operator = "notEmpty"
            },
            "actual is required"
        ];
        yield return
        [
            new WorkflowAssertionDefinition
            {
                Name = "operator is invalid",
                Actual = "{{stage:create-account.output.accountId}}",
                Operator = "definitelyInvalid"
            },
            "Unsupported assertion operator"
        ];
        yield return
        [
            new WorkflowAssertionDefinition
            {
                Name = "actual token is invalid",
                Actual = "{{stage:create-account.output.missing.path}}",
                Operator = "notEmpty"
            },
            "not found"
        ];
        yield return
        [
            new WorkflowAssertionDefinition
            {
                Name = "expected is missing",
                Actual = "{{stage:create-account.output.status}}",
                Operator = "equals",
                Expected = null
            },
            "expected value is required"
        ];
    }

    private static WorkflowExecutor CreateExecutor(
        IWorkflowExecutionReportWriter reportWriter,
        bool assertionFailuresBlock = true,
        TextWriter? output = null)
    {
        return new WorkflowExecutor(
            new HttpClient(),
            new DynamicValueService(),
            logger: new ConsoleExecutionLogger(output ?? TextWriter.Null, TextWriter.Null),
            reportWriter: reportWriter,
            reportOptions: new WorkflowExecutionReportOptions(false, ExecutionReportFormat.None, ExecutionHttpCaptureMode.None, true, false),
            assertionFailuresBlock: assertionFailuresBlock);
    }

    private static WorkflowDocument CreateDocument(
        List<WorkflowAssertionDefinition>? stageAssertions = null,
        List<WorkflowAssertionDefinition>? endStageAssertions = null)
    {
        var definition = new WorkflowDefinition
        {
            Version = "3.11",
            Id = "test-workflow",
            Name = "test-workflow",
            Output = true,
            Input = new List<WorkflowInputDefinition>
            {
                new()
                {
                    Name = "accountLogin",
                    Type = RandomValueType.Text,
                    Required = true
                }
            },
            References = new WorkflowReference
            {
                Apis = new List<ApiReferenceItem>
                {
                    new()
                    {
                        Name = "accounts",
                        Definition = "accounts"
                    }
                }
            },
            Stages = new List<WorkflowStageDefinition>
            {
                new()
                {
                    Name = "create-account",
                    Kind = WorkflowStageKind.Endpoint,
                    ApiRef = "accounts",
                    Endpoint = "/api/accounts",
                    HttpVerb = "POST",
                    ExpectedStatuses = [201, 409],
                    Mock = new WorkflowStageMockDefinition
                    {
                        Status = 201,
                        Payload = """
{
  "appId": "acc-001",
  "login": "{{input.accountLogin}}",
  "status": "active"
}
"""
                    },
                    Output = new Dictionary<string, string>
                    {
                        ["accountId"] = "{{response.body.appId?}}",
                        ["login"] = "{{response.body.login?}}",
                        ["status"] = "{{response.body.status?}}",
                        ["httpStatus"] = "{{response.status}}"
                    },
                    Assertions = stageAssertions
                }
            },
            EndStage = new WorkflowEndStage
            {
                Output = new Dictionary<string, string>
                {
                    ["accountId"] = "{{stage:create-account.output.accountId}}"
                },
                Assertions = endStageAssertions
            }
        };

        return new WorkflowDocument(
            definition,
            "/tmp/test.workflow",
            new Dictionary<string, string>());
    }

    private static ApiCatalogVersion CreateCatalogVersion()
    {
        return new ApiCatalogVersion
        {
            Version = "test",
            Definitions = new List<ApiDefinition>
            {
                new()
                {
                    Name = "accounts",
                    SwaggerUrl = "http://unused",
                    BaseUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["test"] = "http://unused"
                    }
                }
            }
        };
    }

    private sealed class CapturingFakeReportWriter : IWorkflowExecutionReportWriter
    {
        public WorkflowExecutionReport? Report { get; private set; }

        public Task<WorkflowExecutionArtifacts> WriteAsync(
            WorkflowExecutionReport report,
            WorkflowDocument document,
            WorkflowExecutionReportOptions options,
            CancellationToken cancellationToken)
        {
            Report = report;

            return Task.FromResult(new WorkflowExecutionArtifacts(null, null));
        }
    }
}
