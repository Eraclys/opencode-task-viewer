# ADR-0006: Separated External Client Projects and Simplified External Service Boundaries

- Status: In Progress
- Date: 2026-03-08
- Depends on: ADR-0002, ADR-0004
- Related: ADR-0005

## Decision

Move OpenCode and SonarQube HTTP integrations out of `TaskViewer.Server` into dedicated projects, register their outbound HTTP behavior through `IHttpClientFactory`, and collapse redundant pass-through wrappers so each external boundary exposes one meaningful service surface.

Use Microsoft-recommended client registration patterns:

- prefer typed clients for short-lived request clients
- avoid capturing typed clients inside long-lived singleton services
- use named clients or factory-backed service facades where singleton consumers must remain singleton
- configure each external integration in one place, including base address, auth headers, timeouts, and handler behavior
- avoid pass-through service shells whose only behavior is to forward calls unchanged to another client type

## Context

The original server implementation mixed outbound HTTP concerns, parsing, and service abstractions across the server project. During extraction, some wrappers remained that were technically safe for singleton composition but did not add behavior:

- `HttpSonarGateway` only forwarded calls to `SonarQubeApiClient`
- `OpenCodeDispatchClient` only forwarded calls to `OpenCodeApiClient`
- `OpenCodeStatusReader` only forwarded calls to `OpenCodeApiClient`

Those pass-through types created unnecessary naming and ownership noise at the client boundary.

The current DI wiring also reflects an incremental extraction state:

- one generic singleton `HttpClient` is registered in `src/TaskViewer.Server/DependencyInjection/TaskViewerServiceCollectionExtensions.cs`
- some extracted clients still use wrapper-on-wrapper registration instead of exposing the actual service boundary directly
- several consumers are singletons, which makes naive typed-client injection unsafe per Microsoft guidance

ADR-0004 completed the typed adapter boundary work, but it did not yet finish the assembly split and `HttpClientFactory` registration model.

## Plan

1. Create `src/TaskViewer.OpenCode/TaskViewer.OpenCode.csproj` for OpenCode-specific outbound client code.
2. Create `src/TaskViewer.SonarQube/TaskViewer.SonarQube.csproj` for SonarQube-specific outbound client code.
3. Keep `src/TaskViewer.Server/TaskViewer.Server.csproj` as composition root, host, viewer API, and application/orchestration entry point.
4. Move concrete HTTP implementations into the new projects first, then move supporting transport/parsing helpers where that improves dependency direction.
5. Replace the generic singleton `HttpClient` and manual gateway construction with `IHttpClientFactory` registrations.
6. Use typed clients only where the consuming lifetime is short-lived; for singleton consumers, use named clients or factory-created clients behind singleton-safe service facades.
7. Remove redundant pass-through wrappers when the underlying client already matches the intended boundary contract.
8. Give the OpenCode SSE stream its own client registration so long-running event streaming does not share the same configuration as regular API calls.
9. Introduce slimmer OpenCode and SonarQube options for the new client projects instead of depending on the full server runtime settings object.

## Project Boundaries

### OpenCode project

Initial extraction target:

- `src/TaskViewer.Server/Infrastructure/OpenCode/OpenCodeApiClient.cs`
- `src/TaskViewer.Server/Infrastructure/OpenCode/OpenCodeUpstreamSseService.cs`

Likely follow-up extraction candidates:

- OpenCode request contracts and transport helpers that are pure upstream-client concerns
- authentication/header setup shared by OpenCode API and SSE clients

Keep in `TaskViewer.Server`:

- viewer cache/state/coordinator logic
- session/task aggregation logic
- event handling and viewer update fanout unless later generalized

### SonarQube project

Initial extraction target:

- Sonar transport/parsing helpers that belong to the SonarQube client boundary

Keep in `TaskViewer.Server`:

- orchestration use cases
- queue/orchestrator runtime
- mapping and enqueue workflows

## Lifetime Strategy

This ADR intentionally does not require typed clients to be injected everywhere.

Microsoft guidance recommends typed clients for encapsulation, but also warns against capturing typed clients in singleton services. This codebase currently has multiple singleton services and long-lived coordinators, so the safer target shape is:

- typed clients for short-lived request executors
- named clients or `IHttpClientFactory`-backed singleton-safe service facades for singleton consumers
- separate streaming configuration for OpenCode SSE

Where a singleton-safe service facade already has the correct external-facing contract, that facade should implement the contract directly instead of introducing a second pass-through wrapper.

The preferred outcome is Microsoft-aligned configuration without destabilizing the existing singleton orchestration graph.

## Contract Cleanup Required

Before or during the move, clean up abstraction ownership where needed:

- move `IOpenCodeStatusReader` out of server infrastructure ownership so application/orchestrator code does not depend on an infrastructure-local interface declaration
- rename and simplify SonarQube service abstractions so the external-service contract reflects its actual ownership and purpose
- avoid carrying `AppRuntimeSettings` directly into the new client projects

If needed, a small follow-up abstractions project can be introduced later, but it is not required for the first ADR-0006 slices.

## Migration Slices

1. Add the two new projects and reference them from `TaskViewer.Server`.
2. Introduce client-specific options and DI registration helpers.
3. Move low-level OpenCode and SonarQube HTTP implementations.
4. Convert server DI to `AddHttpClient(...)` registrations.
5. Audit singleton consumers and switch unsafe typed-client captures to named-client or factory-backed service facades.
6. Remove redundant pass-through wrappers and rename contracts that currently point in the wrong direction.
7. Update tests, internals visibility, and project references as types move.

## Risks

- Injecting typed clients into singleton services can freeze client behavior for the process lifetime and undercut DNS/handler refresh benefits.
- OpenCode SSE is a long-running stream and may need different timeout and handler settings than ordinary request/response traffic.
- Interface and transport ownership is currently mixed in a few places and can create circular or awkward dependencies if moved carelessly.
- Over-preserving incremental wrappers can leave the extracted projects cleaner physically but still noisy conceptually.
- Tests that currently reference server-local concrete client types may need project-reference or visibility updates.

## Progress Snapshot (2026-03-08)

- Completed:
  - Added `src/TaskViewer.OpenCode/TaskViewer.OpenCode.csproj` and moved the low-level OpenCode request model and HTTP transport implementations into that project.
  - Added `src/TaskViewer.SonarQube/TaskViewer.SonarQube.csproj` and introduced a low-level SonarQube HTTP transport client in that project.
  - `src/TaskViewer.Server/TaskViewer.Server.csproj` now references the two new external-client projects.
  - `TaskViewer.slnx` now includes both new projects.
  - Server DI in `src/TaskViewer.Server/DependencyInjection/TaskViewerServiceCollectionExtensions.cs` now uses `IHttpClientFactory` registrations for OpenCode API traffic, OpenCode SSE traffic, and SonarQube API traffic.
  - OpenCode SSE now has a dedicated named client with streaming-oriented timeout behavior instead of sharing the generic request client registration.
  - Existing server tests and E2E tests were updated to construct the moved OpenCode and SonarQube transport clients through their new options/factory-based constructors.
  - OpenCode outbound HTTP now flows through explicit typed client classes in `src/TaskViewer.OpenCode/Infrastructure/OpenCode/OpenCodeApiHttpClient.cs` and `src/TaskViewer.OpenCode/Infrastructure/OpenCode/OpenCodeSseHttpClient.cs`.
  - SonarQube outbound HTTP now flows through an explicit typed client class in `src/TaskViewer.SonarQube/Infrastructure/Orchestration/SonarQubeTypedHttpClient.cs`.
  - `OpenCodeApiClient` now owns the OpenCode singleton-safe service boundary directly by implementing `IOpenCodeStatusReader` and `IOpenCodeDispatchClient`, removing redundant `OpenCodeStatusReader` and `OpenCodeDispatchClient` pass-through shells.
  - `SonarQubeApiClient` now owns the SonarQube singleton-safe service boundary directly by implementing `ISonarQubeService`, replacing the earlier `ISonarGateway` plus `HttpSonarGateway` wrapper pairing.
  - `SonarOrchestrator` now implements the internal `IOrchestrationGateway` seam directly, removing the rename-only `OrchestrationGatewayAdapter` wrapper from server composition.
  - `SessionsUseCases` now has a direct typed-service constructor for production composition, replacing the earlier large delegate bundle in DI while keeping an internal delegate-based constructor for focused tests.
  - `OrchestrationUseCases` production composition now resolves directly from `SonarOrchestrator`, while retaining `IOrchestrationGateway` only as an internal focused-test seam instead of a host-level registration surface.
  - `IOpenCodeDispatchClient`, `IOpenCodeStatusReader`, `ISonarQubeService`, OpenCode transport contracts, and Sonar transport contracts now live with the external client projects instead of being declared inside `TaskViewer.Server`.
  - Server-side OpenCode session search/read paths now consume typed OpenCode transports for status maps, todos, messages, projects, sessions, archive operations, and dispatch operations.
  - DI-focused tests now verify both extracted client projects and the final server composition register their direct service boundaries without reintroducing wrapper layers.
- Next:
  - Replace the remaining server-local OpenCode parsing helpers/tests with client-project-focused tests so transport-shape behavior is owned and validated next to the client implementation.
  - Continue auditing extracted client projects and server composition seams for wrappers that only rename or relay without adding lifetime, parsing, caching, retry, or policy behavior.
  - Add client-project-specific tests and, if needed, internals visibility for those assemblies instead of relying on server-test coverage alone.

## Acceptance Criteria

- Concrete OpenCode and SonarQube HTTP clients no longer live in `TaskViewer.Server`.
- `TaskViewer.Server` configures outbound HTTP through `IHttpClientFactory` registrations instead of a generic singleton `HttpClient` and manual gateway construction.
- Singleton services do not directly capture typed clients unless explicitly justified and documented.
- External client projects do not keep redundant pass-through wrapper services when the underlying singleton-safe service can own the boundary directly.
- OpenCode SSE uses a dedicated HTTP client configuration.
- Existing API behavior, orchestration behavior, and tests continue to pass.
