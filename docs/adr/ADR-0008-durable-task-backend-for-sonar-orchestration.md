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
- a full review UI for approve/reject/split/merge actions
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

## Plan

1. Introduce durable grouped task persistence over SQLite/Dapper.
2. Batch ingestion by `project + file + rule` and deduplicate on stable task keys.
3. Add scheduler service for lease acquisition, fairness, and lock-aware selection.
4. Add runner flow for one leased task and OpenCode execution.
5. Add reconciler flow for stale lease recovery and runtime reconciliation.
6. Preserve compatibility-shaped queue/session contracts for the current UI and tests.
7. Add or update tests to cover grouped-task creation, leasing, retries, and compatibility projections.
8. In a later ADR slice, migrate the UI/API to task-native states and review workflow.

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

## Progress Snapshot (2026-03-08)

- Started:
  - backend migration from issue-level queue rows toward durable grouped tasks
  - `project + file + rule` batching policy in orchestration persistence
  - scheduler / runner / reconciler separation in orchestration services
  - lease and task metadata added to queue records for backend-only phase
- Current phase focus:
  - restore full compatibility for existing tests and API/UI contracts while durable internals remain in place
  - keep readiness strict in production but overridable in tests
- Next:
  - finish compatibility pass and get full solution tests green
  - document the later task-native UI/API cutover as a subsequent slice rather than forcing it into phase 1

## Acceptance Criteria

- grouped durable tasks are created using `project + file + rule`
- scheduler leases work instead of destructive dequeue
- runner processes one leased task at a time
- reconciler can recover stale leased/running work
- compatibility APIs remain functional for the current UI and tests
- full build and test verification passes for the backend-only slice
