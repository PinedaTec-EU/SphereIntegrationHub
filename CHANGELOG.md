# Changelog

## [1.5.13] – 2026-04-08

- **Readiness estricto en preflight**: `healthCheck` deja de ser solo informativo; ahora aplica retry/timeout configurables y aborta la ejecución si agota la política.
- **Nuevo bloque `readiness` en `api-catalog.json`**: soporta `maxRetries`, `delayMs`, `timeoutMs` y `httpStatus` por API.
- **Swagger download resiliente**: la descarga del swagger remoto reutiliza la política de readiness cuando existe.
- **Execution report**: añade bloque `Preflight` con operaciones, intentos consecutivos, retries y duración acumulada.
- **MCP actualizado**: `get_api_definitions`, generación de catálogo, upsert de catálogo y lectura de execution reports exponen la nueva superficie de readiness/preflight.
- **Documentación alineada**: README, catálogo, dry-run y GitHub Action documentan el nuevo contrato y el requisito de esperar readiness del deployment en CI/CD.

## [1.5.12] – 2026-04-07

- **GitHub Action** `run-sphere-workflow`: composite action para ejecutar workflows desde cualquier pipeline CI/CD con opción de versión fija o latest.
- **Pre-check de endpoints mejorado**: al arrancar la tool se listan las base URLs resueltas por API referenciada en el workflow antes de empezar el caching swagger, y se emiten inmediatamente a consola.
- **Secret masking**: los valores marcados como `Secret` en inputs, variables y outputs se enmascaran en los reportes de ejecución y en consola.
- **Soporte de offset en tokens de fecha/hora**: `{{system:datetime.now+P1D}}`, `{{system:date.today-PT2H}}` con duraciones ISO 8601.
- Fix MCP: fallback a `baseUrl` a nivel de versión en generación de catálogo.
- MCP expone el flag `Secret` en análisis de scope de variables y en capacidades de plugins.
- Documentación del MCP actualizada: estado real (37 herramientas, todos los niveles productivos).

## [1.5.9 – 1.5.11] – 2026-03

- **Execution report mejorado**: incluye inputs, outputs y resultados por stage; selector de ejecución múltiple en el HTML viewer.
- **`sih report` como comando standalone**: genera el reporte HTML interactivo a partir de cualquier `.workflow.report.json` y lo abre en el navegador.
- HTML report con interfaz de tabs, dark mode, indicadores de estado por stage, versión de aplicación visible y mejoras de layout.
- Soporte de directorio como input en `sih report` (carga todos los reportes del directorio).
- `CliPipeline` refactorizado para emitir mensajes de forma progresiva durante la ejecución (en lugar de al final).

## [1.5.7 – 1.5.8] – 2026-02

- **Shared library** (`SphereIntegrationHub.Shared`): `ApiCatalogVersion` y `ApiDefinition` extraídos como tipos compartidos entre CLI y MCP, eliminando duplicación.
- **Request body contract processing**: validación del contrato del body contra la spec Swagger en tiempo de ejecución.
- **Per-definition `baseUrl`**: cada API del catálogo define sus propias URLs por entorno (elimina la URL única a nivel de versión); soporte de `basePath` y token `{{port}}`.
- **Swagger URI con plantillas**: `swaggerUrl` acepta tokens `{{baseUrl}}`, `{{baseUrl.env}}`, `{{port}}` para URLs dinámicas.
- MCP: herramientas de gestión de catálogo (`upsert_api_catalog_and_cache`, `generate_api_catalog_file`, `refresh_swagger_cache_from_catalog`, `repair_workflow_artifacts`).
- MCP: herramientas de análisis de variables (`get_available_variables`, `analyze_context_flow`).
- Fix: mensajes de error mejorados en fallos de descarga de swagger y en archivo de caché faltante.

## [1.5.5 – 1.5.6] – 2026-02

- **Execution reporting**: persistencia de cada ejecución como JSON + HTML con timeline, drill-down por stage, captura HTTP con redacción de datos sensibles.
- **`sih report`**: primer comando standalone para generar el HTML trace report.
- **Structured inputs** (`Object`, `Array`): workflows que aceptan objetos y arrays como parámetros de entrada tipados.
- **`healthCheck` en catálogo**: probe opcional por API antes del caching swagger; resultado visible en el arranque.
- **`ApiHealthCheckProbe`** integrado en el pipeline (dry-run y ejecución normal).
- MCP: herramientas de inspección de reportes (`list_execution_reports`, `read_execution_report`).
- MCP probe workflow de ejemplo (`mcp-probe.workflow`).

## [0.5.5] – 2026-01

- **`baseUrl` per-definition** (primera iteración): APIs con URLs propias por entorno en el catálogo.
- Fix: validación de URI al expandir swagger URL con plantillas.
- Fix: mensajes de error mejorados en swagger download.
- Tests añadidos para resolución de swagger URI con rutas relativas.

## [0.3.2] – 2025-12

- **MCP Server** publicado como dotnet tool (`SphereIntegrationHub.Mcp.Tool`) con 26 herramientas en 4 niveles de capacidad (L1–L4).
- **CLI publicado como dotnet tool** (`SphereIntegrationHub.Tool`, comando `sih`).
- Registro automático de herramientas MCP vía atributo `[McpTool]`.
- `CatalogUrlResolver` para resolución dinámica de URLs Swagger con soporte de puertos.
- `IStageGenerator` extraído como interfaz (DIP).
- Logging añadido a catch blocks silenciosos en MCP.
- Telemetry y usage ping service (OpenTelemetry, opt-in).
- Migración a .NET 10.
- CI/CD: pipeline de publicación en NuGet.
- Distribución unificada: launcher `sih` con MCP y CLI integrados.

## [0.3.1] – 2025-11

- **MCP Server inicial**: 26 herramientas para exploración de catálogo, validación de workflows, generación de stages, análisis semántico, síntesis de sistemas y optimización.
- Herramientas de idempotencia: `ensure`, `expectedStatuses`, `onStatus`, `jumpOnStatus`.
- `forEach` con `bodyFile` / `dataFile` para bootstraps de colecciones.
- Resultados agregados de `forEach` en workflow stages: `foreach_results`, `foreach_success_count`, `foreach_failed_count`.
- Propagación de fallos de workflows hijo al padre.
- Expresiones `runIf` con funciones: `exists()`, `empty()`, `coalesce()`, `first()`, `any()`, `jsonLength()`, `isEmptyJson()`.
- Segmentos de path opcionales con `?` en tokens (`{{response.body.item.id?}}`).
- Validación de tokens contra mock payloads en dry-run.
- `Object` y `Array` como tipos de input de workflow.
- Archivos de muestra en `samples/` (parent/child, conditional, bootstrap, secrets).
- Branding renombrado a SphereIntegrationHub.

## [0.3.0] – 2025-10 (baseline)

- CLI inicial: ejecución de workflows YAML contra catálogo de APIs versionado.
- Workflow composition: stages de tipo `Endpoint` y `Workflow` (hijo).
- Variables: `input`, `context`, `global`, `env`, `system`, stage outputs.
- Dry-run con validación de endpoints contra caché Swagger.
- Retry policies y circuit breakers por stage.
- `.wfvars` y `.env` para inputs externos.
- Catálogo de APIs con Swagger caching por versión.
- Tests unitarios iniciales.
