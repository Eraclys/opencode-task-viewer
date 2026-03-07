# ADR-0003: Stable Typed API Contracts

- Status: Completed
- Date: 2026-03-07
- Depends on: ADR-0001

## Decision

Replace anonymous response object construction in use-cases/mappers with explicit DTOs for every `/api/*` contract while preserving wire field names.

## Plan

1. Introduce DTOs for orchestration responses currently produced by `OrchestrationResponseMapper`.
2. Replace `Task<object>` returns with typed return contracts in use-cases.
3. Keep JSON property names stable through serializer attributes where needed.
4. Add contract-focused tests for key routes (`/api/sessions`, `/api/tasks/all`, `/api/orch/*`).

## Progress Snapshot (2026-03-08)

- Completed:
  - Endpoint modules under `src/TaskViewer.Server/Api` no longer construct anonymous JSON response payloads for standard API responses.
  - Shared typed contracts were added in `src/TaskViewer.Server/Api/ApiContracts.cs` for stable error, health, archive, queue-action, and SSE handshake payloads.
  - Stable OpenCode transport payloads for viewer update broadcasts and archive request bodies now use explicit transport contract types instead of anonymous objects.
  - Wire field names were preserved via property naming and serializer attributes where needed.
  - Contract-focused tests now assert representative serialized field names for health, session errors, assistant message payloads, orchestration config, rules, issues, enqueue responses, queue overview payloads, orchestration mapping failures, and orchestration queue action/error branches.
  - The new contract tests run on the `.NET 9` / central-package-management baseline established for the solution.
- Next:
  - Keep broadening contract-focused tests when new `/api/*` payload shapes are introduced.
  - Consider snapshot-style approval coverage if the API surface grows enough to make field-by-field assertions repetitive.

## Acceptance Criteria

- No anonymous-object response mapping in application use-cases.
- Endpoint response shapes are backward compatible.
- E2E tests remain green.
