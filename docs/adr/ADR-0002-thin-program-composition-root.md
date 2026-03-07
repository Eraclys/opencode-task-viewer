# ADR-0002: Keep `Program.cs` as a Thin Composition Root

- Status: Completed
- Date: 2026-03-07
- Depends on: ADR-0001

## Decision

`Program.cs` will own only host setup, DI/composition, endpoint registration, and lifecycle wiring. Business logic and transport parsing move to dedicated services/adapters.

All service construction will flow through `IServiceCollection` registrations (prefer extension methods such as `AddTaskViewerServerApplication` and `AddTaskViewerServerInfrastructure`) instead of ad-hoc `new` wiring spread through `Program.cs`.

Non-sensitive runtime configuration should default to `appsettings.json` and be loaded through configuration abstractions. Sensitive values such as passwords and tokens should stay in environment variables (or other secret stores) and override config-file defaults where applicable.

## Plan

1. Extract OpenCode session/project read operations into a typed adapter service.
2. Extract cache orchestration into a dedicated cache coordinator service.
3. Extract SSE event handling into an event translator + invalidation service.
4. Move registrations into `IServiceCollection` extension methods and compose by module.
5. Keep endpoint handlers thin and delegate to use-cases.
6. Move non-secret bootstrap settings from direct environment-variable reads in `Program.cs` into `appsettings.json`-backed configuration, while keeping secrets environment-driven.
7. Do not reinvent configuration loading; use the standard Microsoft configuration providers/packages for JSON files and environment variables instead of custom ad hoc loading infrastructure.

## Acceptance Criteria

- `Program.cs` contains no domain/business branching logic.
- No direct OpenCode payload shape inspection in `Program.cs`.
- No manual object graph creation in `Program.cs` except unavoidable bootstrap primitives (config values).
- Existing routes and behavior remain unchanged.

## Progress Snapshot (2026-03-08)

- Done:
  - `Program.cs` now only performs host setup, module registration, endpoint registration, static file middleware, and lifecycle wiring.
  - Endpoint mapping resolves use-cases and infrastructure services via DI.
  - Session/status/todo/message parsing paths in `Program.cs` no longer depend on `JsonObject`.
- Completed on 2026-03-08:
  - Composition was split into module-level registrations: `AddTaskViewerServerInfrastructure` and `AddTaskViewerServerApplication`.
  - Concrete wiring for orchestrator, Sonar gateway/services, and use-cases moved out of `Program.cs` and into `IServiceCollection` factories.
  - Non-sensitive host/OpenCode/Sonar/orchestration defaults were moved to `src/TaskViewer.Server/appsettings.json` and loaded through `AppRuntimeSettingsLoader`, while sensitive values remain environment-driven.
  - Runtime settings are now registered once via `AddTaskViewerRuntimeSettings`, allowing infrastructure factories to consume shared configuration without rebuilding a temporary service provider.
  - Configuration loading now relies on the standard Microsoft configuration pipeline (`builder.Configuration`) rather than direct environment-variable reads inside the runtime settings loader.
  - Upstream OpenCode SSE connection/retry/parsing logic was extracted from `Program.cs` into `src/TaskViewer.Server/Infrastructure/OpenCode/OpenCodeUpstreamSseService.cs`.
  - Remaining session/runtime/queue helper delegates were moved into DI-backed services, and viewer-specific routes were extracted into `src/TaskViewer.Server/Api/ViewerEndpoints.cs`.
  - SPA static hosting now uses `src/TaskViewer.Server/wwwroot/index.html`, allowing ASP.NET Core default web-root hosting and removal of custom workspace-root probing.
  - The repo baseline was upgraded to `.NET 9`, with SDK pinning in `global.json`, shared build properties in `Directory.Build.props`, and centralized NuGet versions in `Directory.Packages.props`.

## Outcome

- `Program.cs` no longer contains business/domain branching logic.
- `Program.cs` no longer inspects OpenCode payload shapes directly.
- Manual object graph construction moved into `IServiceCollection` registrations.
- Existing API and UI behavior stayed stable under focused and full solution test runs.
