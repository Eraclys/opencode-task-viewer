# ADR-0005: Cache Unification and Invalidation Policy

- Status: In Progress
- Date: 2026-03-07
- Depends on: ADR-0002, ADR-0003

## Decision

Unify cache ownership in one service with typed cache keys, explicit TTL policies, and event-driven invalidation rules.

## Plan

1. Replace scattered lock/dictionary cache state with a cache coordinator.
2. Centralize key construction (`directory/session`) and TTL definitions.
3. Move SSE-triggered invalidation logic into a single policy class.
4. Add targeted tests for invalidation by event type.

## Progress Snapshot (2026-03-08)

- Completed:
  - SSE-driven OpenCode cache invalidation decisions were extracted from `src/TaskViewer.Server/Infrastructure/OpenCode/OpenCodeEventHandler.cs` into `src/TaskViewer.Server/Infrastructure/OpenCode/OpenCodeCacheInvalidationPolicy.cs`.
  - `OpenCodeEventHandler` now applies a typed invalidation decision instead of embedding event-type branching and cache mutation policy inline.
  - Focused tests were added in `tests/TaskViewer.Server.Tests/OpenCodeCacheInvalidationPolicyTests.cs` for todo updates, session-status updates, message updates, and unknown events.
  - `OpenCodeViewerState` now exposes dedicated APIs for status-map, todo-list, and per-directory session cache reads/writes instead of requiring callers to mutate cache dictionaries directly.
  - `OpenCodeSessionSearchService` now uses those viewer-state APIs for three cache families: status maps, session todos, and per-directory session lists.
  - Assistant-response presence caching and in-flight request deduplication are now coordinated through `OpenCodeViewerState` instead of direct dictionary access from `OpenCodeSessionSearchService`.
  - Handler-level tests now cover `OpenCodeEventHandler` applying invalidation decisions and emitting viewer update broadcasts for todo, session-status, and message events.
  - Shared cache-key construction for normalized directory keys and `directory::sessionId` keys now lives in `src/TaskViewer.Server/Infrastructure/OpenCode/OpenCodeCacheKeys.cs` and is reused across viewer-state and session/read paths.
  - The remaining OpenCode viewer-state cache dictionaries are now private implementation details; callers interact through dedicated viewer-state methods instead of reaching into cache collections directly.
  - Task-overview cache state is now private inside `OpenCodeViewerState`, with targeted tests added for cache-key helpers and task-overview cache freshness/invalidation behavior.
  - Cache-facing services now depend on `src/TaskViewer.Server/Infrastructure/OpenCode/OpenCodeViewerCacheCoordinator.cs`, a dedicated coordinator that fronts `OpenCodeViewerState` for cache reads, writes, and invalidation operations.
  - `OpenCodeSessionSearchService`, `OpenCodeTasksOverviewService`, `OpenCodeSessionRuntimeService`, `OpenCodeEventHandler`, and `OpenCodeViewerUpdateNotifier` no longer depend on `OpenCodeViewerState` directly.
  - Coordinator-level tests now cover task-overview freshness, session-todo invalidation, and full cache-family invalidation behavior.
  - Cache TTL ownership now lives in `src/TaskViewer.Server/Infrastructure/OpenCode/OpenCodeViewerCachePolicy.cs`, which the coordinator consumes so cache consumers no longer thread TTL arguments through every read path.
  - `OpenCodeSessionSearchService` and `OpenCodeTasksOverviewService` now rely on coordinator-owned cache policy for session, project, status, todo, task-overview, assistant-presence, and status-override freshness checks.
  - Additional coordinator tests now verify that task-overview freshness obeys the injected cache policy.
- Next:
  - Consider narrowing `OpenCodeViewerState` further to pure storage primitives once coordinator behavior is fully established.
  - Consider moving session/project pagination and fetch limits into a dedicated read-policy object so OpenCode read semantics are configured alongside cache policy.

## Acceptance Criteria

- Cache state is not directly manipulated from endpoint handlers.
- Invalidation behavior is deterministic and test-covered.
- No regression in freshness/performance for `/api/sessions` and `/api/tasks/all`.
