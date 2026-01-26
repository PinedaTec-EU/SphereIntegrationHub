using System;
using System.Threading;
using System.Threading.Tasks;

using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services;
using SphereIntegrationHub.Services.Interfaces;

namespace SphereIntegrationHub.Services.Plugins;

public sealed class StageExecutionContext
{
    public StageExecutionContext(
        WorkflowDocument document,
        ApiCatalogVersion catalogVersion,
        string environment,
        ExecutionContext execution,
        TemplateResolver templateResolver,
        MockPayloadService mockPayloadService,
        WorkflowLoader workflowLoader,
        VarsFileLoader varsFileLoader,
        ISystemTimeProvider systemTimeProvider,
        IEndpointInvoker endpointInvoker,
        IExecutionLogger logger,
        StageMessageEmitter stageMessageEmitter,
        Func<WorkflowDocument, ExecutionContext, CancellationToken, Task<WorkflowExecutionOutcome>> executeNestedWorkflowAsync,
        bool varsOverrideActive,
        bool mocked,
        bool verbose,
        bool debug)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        CatalogVersion = catalogVersion ?? throw new ArgumentNullException(nameof(catalogVersion));
        Environment = environment ?? throw new ArgumentNullException(nameof(environment));
        Execution = execution ?? throw new ArgumentNullException(nameof(execution));
        TemplateResolver = templateResolver ?? throw new ArgumentNullException(nameof(templateResolver));
        MockPayloadService = mockPayloadService ?? throw new ArgumentNullException(nameof(mockPayloadService));
        WorkflowLoader = workflowLoader ?? throw new ArgumentNullException(nameof(workflowLoader));
        VarsFileLoader = varsFileLoader ?? throw new ArgumentNullException(nameof(varsFileLoader));
        SystemTimeProvider = systemTimeProvider ?? throw new ArgumentNullException(nameof(systemTimeProvider));
        EndpointInvoker = endpointInvoker ?? throw new ArgumentNullException(nameof(endpointInvoker));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        StageMessageEmitter = stageMessageEmitter ?? throw new ArgumentNullException(nameof(stageMessageEmitter));
        ExecuteNestedWorkflowAsync = executeNestedWorkflowAsync ?? throw new ArgumentNullException(nameof(executeNestedWorkflowAsync));
        VarsOverrideActive = varsOverrideActive;
        Mocked = mocked;
        Verbose = verbose;
        Debug = debug;
    }

    public WorkflowDocument Document { get; }
    public WorkflowDefinition Definition => Document.Definition;
    public ApiCatalogVersion CatalogVersion { get; }
    public string Environment { get; }
    public ExecutionContext Execution { get; }
    public TemplateResolver TemplateResolver { get; }
    public MockPayloadService MockPayloadService { get; }
    public WorkflowLoader WorkflowLoader { get; }
    public VarsFileLoader VarsFileLoader { get; }
    public ISystemTimeProvider SystemTimeProvider { get; }
    public IEndpointInvoker EndpointInvoker { get; }
    public IExecutionLogger Logger { get; }
    public StageMessageEmitter StageMessageEmitter { get; }
    public Func<WorkflowDocument, ExecutionContext, CancellationToken, Task<WorkflowExecutionOutcome>> ExecuteNestedWorkflowAsync { get; }
    public bool VarsOverrideActive { get; }
    public bool Mocked { get; }
    public bool Verbose { get; }
    public bool Debug { get; }
}
