# ADR-0008: Durable Task Backend for Sonar Orchestration

- Status: In Progress
- Date: 2026-03-08
- Depends on: ADR-0002, ADR-0003, ADR-0004, ADR-0007
- Related: ADR-0005, ADR-0006

## Decision

Move Sonar orchestration from an issue-level FIFO queue to a durable task backend that batches work by `project + file + rule` and separates scheduler responsibilities from runner responsibilities.

The first migration slice is backend only:

- keep SQLite and Dapper as the persistence stack
- preserve existing HTTP/UI contracts where practical
- expose compatibility-shaped queue/session data while durable task internals evolve underneath

## Context

The current orchestration model assumes one Sonar issue becomes one queue row and usually one OpenCode session. That breaks down when a project contains thousands of warnings:

- too many tiny sessions
- poor fairness across repositories
- weak recovery if a worker dies mid-run
- no durable lease model
- no path-level exclusivity for code changes
- no explicit review-oriented lifecycle

The application now needs a task model that can:

- batch related warnings into one durable unit of work
- schedule fairly across projects instead of simple FIFO
- lease work instead of permanently dequeuing it on start
- recover from stale or crashed workers
- prevent conflicting edits on the same file
- leave the frontend/API stable while the backend is upgraded incrementally

## Scope

This ADR covers the backend orchestration model in `src/TaskViewer.Server`:

- durable task persistence in `src/TaskViewer.Server/Infrastructure/Orchestration`
- scheduler/runner/reconciler orchestration in `src/TaskViewer.Server/SonarOrchestrator.cs` and `src/TaskViewer.Server/Application/Orchestration`
- compatibility mapping back to existing queue/session-shaped API responses

This ADR does not yet require:

- a task-native frontend rewrite
- replacing SSE with SignalR
- a full review UI for approve/reject/requeue actions
- changing away from SQLite for phase 1

## Durable Task Model

### Task Unit

Default batching unit: `project + file + rule`

This preserves prompt quality and review granularity while collapsing issue-level fanout.

### Scheduling Model

Use weighted-fair scheduling rather than FIFO.

Phase 1 scheduler signals:

- global concurrency cap
- per-project concurrency cap
- age bonus
- cheap-fix bonus for small grouped tasks
- noisy-project penalty
- file/path lock exclusion

### Lease Model

Use leases instead of destructive dequeue:

- task becomes `leased` before runner starts work
- runner heartbeats the lease
- stale lease can be recovered and retried
- retry continues to use backoff / attempt limits

### Runner Model

Runner is responsible only for one leased task:

- create or attach OpenCode session
- send prompt for the grouped task
- persist runtime state and output linkage
- move completed work toward review-compatible states

### Readiness Gate

Before launch, validate lightweight readiness checks such as:

- repository path available
- file still exists when path is known
- no merge/rebase markers in the repository
- linked Sonar issues still exist when the Sonar gateway is available

For testability in this phase, readiness may be overridden in tests so existing mock-driven orchestrator tests do not require full real repository fixtures.

## Compatibility Strategy

Phase 1 intentionally keeps old contracts alive where possible.

- existing queue endpoints remain available
- compatibility DTOs continue to expose legacy fields like `dispatching` / `session_created`
- durable states are projected back into those fields until the UI is rewritten
- the frontend may still say “queue item” while the backend persists grouped tasks

This is deliberate technical debt to make the backend migration safe and incremental.

## Review-State Phase

After the backend-only durable task slice is stable, the next phase introduces task-native review actions while still preserving compatibility projections.

Phase goals:

- add explicit task review transitions such as `awaiting_review`, `done`, `rejected`, and `queued` requeue
- keep compatibility queue/session reads functioning for the current UI
- add backend/API support first before attempting a full task-native review UI
- persist lightweight review history so the dashboard can explain why a task was rejected, requeued, or completed

Initial review actions:

- approve task
- reject task
- requeue task

Phase constraints:

- preserve existing `/api/orch/queue*` routes for now
- avoid a full kanban rewrite in the same slice
- keep current compatibility stats available while also exposing task-native metadata
- prefer append-only review history records over repeatedly overloading `last_error` for review intent

## Plan

1. Introduce durable grouped task persistence over SQLite/Dapper.
2. Batch ingestion by `project + file + rule` and deduplicate on stable task keys.
3. Add scheduler service for lease acquisition, fairness, and lock-aware selection.
4. Add runner flow for one leased task and OpenCode execution.
5. Add reconciler flow for stale lease recovery and runtime reconciliation.
6. Preserve compatibility-shaped queue/session contracts only as a temporary migration aid.
7. Add or update tests to cover grouped-task creation, leasing, retries, and compatibility projections.
8. Add backend/API review-state transitions with compatibility preserved.
9. Persist review history and surface latest review metadata in task reads.
10. Add mapping deletion through the orchestration management surface.
11. Make the main kanban task-native and stop showing non-orchestrated OpenCode sessions in the main board.
12. In a later ADR slice, clean up route naming and remaining session-centric compatibility artifacts.

## Consequences

### Positive

- fewer OpenCode sessions for large Sonar result sets
- fairer scheduling across projects
- safer crash recovery through leases and reconciliation
- simpler future extension to review-centric task states
- file-level batching better matches how code changes are actually reviewed

### Negative

- compatibility projection adds temporary complexity
- old queue terminology remains visible for a while even though internals are task-based
- some tests must explicitly override readiness instead of using fake paths directly
- grouped-task semantics change counts and state timing compared with issue-level queue rows
- route naming may temporarily lag behind semantics while `/api/sessions` becomes task-backed

## Progress Snapshot (2026-03-08)

- Started:
  - backend migration from issue-level queue rows toward durable grouped tasks
  - `project + file + rule` batching policy in orchestration persistence
  - scheduler / runner / reconciler separation in orchestration services
  - lease and task metadata added to queue records for backend-only phase
- Current phase focus:
  - expose task-native review actions in the current UI
  - persist lightweight review history and latest review metadata for task detail rendering
  - keep review actions lightweight: reject, requeue, reprompt, and complete/approve
  - remove Sonar project mappings from the orchestration settings flow
  - make the main kanban task-native and remove raw non-orchestrated OpenCode sessions from it
  - move the main board toward task-native route naming instead of session-centric route naming
  - move task detail fetches toward task-native routes where the UI is already task-backed
  - make approval mean task completion plus OpenCode session cleanup, with the task disappearing from the main board
- Next phase focus:
  - keep route compatibility where practical while board semantics are task-native
- Next:
  - add richer review-history presentation in the UI if lightweight metadata is no longer sufficient
  - clean up leftover session-centric route naming after the task-native board settles

## Acceptance Criteria

- grouped durable tasks are created using `project + file + rule`
- scheduler leases work instead of destructive dequeue
- runner processes one leased task at a time
- reconciler can recover stale leased/running work
- compatibility APIs remain functional for the current UI and tests
- approve / reject / requeue review actions are supported on durable tasks
- latest review metadata is queryable for task detail rendering
- full build and test verification passes for the backend-only slice
