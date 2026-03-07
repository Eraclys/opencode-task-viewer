# ADR-0001: Ban `object`/`JsonObject` and Fix Primitive Obsession

- Status: In Progress
- Date: 2026-03-07
- Decision Drivers:
  - Ongoing hexagonal refactor needs strict boundaries and predictable contracts.
  - `object` and `JsonObject` signatures hide intent, expand runtime failure modes, and make tests noisy.
  - String/int primitive obsession spreads parsing and validation across layers.

## Context

Current hotspots include:

- `src/TaskViewer.Server/Application/Orchestration/IOrchestrationUseCases.cs` (`Task<object>`, `object GetPublicConfig`)
- `src/TaskViewer.Server/SonarOrchestrator.cs` (public methods taking `object?` and returning `object`)
- `src/TaskViewer.Server/Program.cs` (raw `JsonObject` parsing and shape branching)
- `src/TaskViewer.Server/Application/Sessions/SessionTodoViewService.cs` (`NormalizeTodo(JsonObject? todo)`)

## Decision

We will remove `object` and `JsonObject` from production code contracts.

Rules:

1. No `object` in application/domain/infrastructure public APIs.
2. No `JsonObject` in application/domain/infrastructure production code.
3. External transport parsing must map directly into typed transport DTOs.
4. Unknown upstream fields are tolerated through schema-tolerant typed deserialization (`JsonDocument`/`JsonElement` and targeted readers), not `JsonObject`.
5. Replace primitive obsession with value objects for IDs, states, and paging.

### Allowed JSON Types

- `JsonNode`/`JsonDocument`/`JsonElement` may be used only inside dedicated transport adapter parsing internals.
- These parsing internals must not leak untyped values past adapter boundaries.

## Consequences

### Positive

- Compile-time guarantees for request/response contracts.
- Centralized validation and normalization.
- Easier refactoring and reduced defensive null/string parsing.
- Better tests: simpler setup, fewer shape-dependent edge cases.

### Negative

- Short-term increase in DTO/value-object count.
- Temporary duplication during migration (old and new contracts side by side).
- Requires coordinated updates across endpoints, use-cases, and orchestrator integration tests.

## Implementation Plan

### Phase 1 - Remove `object` from orchestration contracts

- Introduce typed request/response DTOs for:
  - config
  - rules list
  - issues list
  - queue overview
  - enqueue operations
  - instruction profiles
- Update:
  - `IOrchestrationUseCases`
  - internal orchestration gateway seam
  - `OrchestrationUseCases`
  - `SonarOrchestrator` direct typed gateway surface
  - `SonarOrchestrator` public surface

Status update:

- Completed for orchestration contracts and responses.
- `Task<object>` / `object?` signatures were replaced with typed DTOs and typed IDs/paging inputs.

### Phase 2 - Eliminate `JsonObject` in session/todo/message flows

- Replace `JsonObject`-based parsers in `Program.cs` with typed transport DTO mappers.
- Refactor `SessionTodoViewService.NormalizeTodo` to accept typed input model.
- Refactor message parsing (`AssistantMessageParser`) to typed transport message DTOs.

### Phase 3 - Replace primitives with value objects

- Add value objects:
  - `MappingId`, `QueueItemId`, `SessionId`
  - `IssueType`, `IssueStatus`, `QueueState`, `RuntimeType`
  - `PageIndex`, `PageSize`, `RuleKeySet`
- Move parse/validation to constructors/factory methods.
- Remove repeated `ParseIntSafe` and string-state checks from business logic.

Status update:

- Started with temporal primitives: orchestration write-time parameters (`now`, `updatedAt`, `nextAttemptAt`) were moved from `string` to `DateTimeOffset` / `DateTimeOffset?` in repository and service boundaries.
- Remaining temporal string hotspots are concentrated in persisted read models and session/message payload parsing paths.

### Phase 4 - Guardrails

- Add tests that fail if `object` or `JsonObject` appears in production project source.
- Keep exception for test code only when intentionally probing malformed payloads.

Status update:

- Completed for production source regression coverage via `tests/TaskViewer.Server.Tests/ProductionSourceGuardrailTests.cs`.

## Progress Snapshot (2026-03-08)

- Done:
  - Production source has zero `object` / `JsonObject` usages under `src/TaskViewer.Server`.
  - Guardrail tests now fail the server test suite if `object` or `JsonObject` reappears in production source.
  - Session/todo/message/project parsing paths were migrated off `JsonObject` to `JsonNode` + typed normalization.
  - Orchestration APIs no longer expose `object`/`JsonObject` contracts.
  - SSE send/broadcast APIs no longer take `object`; they use typed generics.
  - Temporal write-path primitives were migrated to `DateTimeOffset`/`DateTimeOffset?` for queue/mapping repositories and dispatch policy.
- Next:
  - Introduce dedicated value objects for IDs/states and temporal fields in read models (`MappingRecord`, `QueueItemRecord`, `SessionSummaryDto`, `OpenCodeSessionDto`, `LastAssistantMessage`).
  - Replace remaining string-based time fields in read models/contracts with typed time values plus explicit edge serialization.
  - Continue reducing primitive obsession beyond timestamps by introducing typed IDs, states, and paging/value objects.

## Acceptance Criteria

- Zero `object` usages in `src/TaskViewer.Server/**/*.cs` (excluding test-only helper internals if needed).
- Zero `JsonObject` usages in `src/TaskViewer.Server/**/*.cs`.
- API wire contract remains stable for existing UI/E2E consumers.
- `dotnet test TaskViewer.slnx` stays green.

## Rollout Strategy

- Ship in small slices behind unchanged endpoint contracts.
- Prioritize orchestration interfaces first (highest concentration of `object`).
- Follow with session/message/todo transport mapping.

## Alternatives Considered

- Keep `JsonObject` at transport edges: rejected due to continued shape leakage and runtime fragility.
- Keep `object` for endpoint convenience: rejected because it blocks clear contracts and increases primitive obsession.
