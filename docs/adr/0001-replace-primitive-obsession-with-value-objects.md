# ADR 0001: Replace Primitive Obsession with Value Objects

## Status

Accepted

## Context

The codebase passes queue/task state, OpenCode runtime status, and normalized directory-like values around as raw strings. That spreads normalization and branching logic across orchestration services, cache invalidation, UI mapping, and tests.

Examples in the current codebase include:

- queue state values such as `queued`, `leased`, `running`, `awaiting_review`, `rejected`, `done`, `failed`, and `cancelled`
- OpenCode runtime status values such as `busy`, `retry`, `running`, `working`, and `idle`
- duplicated directory normalization and slash-variant logic

This creates a few concrete problems:

- invalid values are easy to construct and hard to detect early
- behavior is encoded in repeated string comparisons instead of named domain concepts
- runtime status semantics have already drifted across components
- tests assert literals instead of domain behavior, which makes refactoring harder

One explicit domain decision for this ADR is that `working` must be treated as the same semantic runtime state as `running`, `busy`, and `retry`.

## Decision

We will replace the highest-value primitive string flows with small domain value objects while preserving existing JSON and SQLite string contracts at system boundaries.

The initial refactor will focus on:

1. `QueueState` for orchestration task lifecycle state
2. `SessionRuntimeStatus` for OpenCode runtime state semantics
3. stronger reuse of directory normalization logic, with a later follow-up to evolve `DirectoryPath` into a richer value object

These value objects will:

- own normalization and parsing
- expose domain predicates such as `IsRunning`, `IsActive`, `IsTerminal`, and UI classification helpers where appropriate
- keep invalid or unknown values from leaking deep into domain logic
- serialize back to the current external string values so API and persistence compatibility is preserved during migration

## Consequences

### Positive

- domain services stop branching on repeated magic strings
- tests can target semantic behavior instead of string trivia
- runtime status handling becomes consistent, especially for the `working` synonym
- queue-state transitions become easier to audit and refactor safely

### Negative

- there is some short-term mapping overhead at repository and API boundaries
- partial migration means both raw strings and typed values will coexist for a while
- care is required to avoid breaking stable response shapes relied on by the UI and contract tests

## Implementation Plan

### Phase 1: Queue state

- introduce `QueueState` as the canonical domain type for orchestration lifecycle state
- migrate core orchestration services and repository interfaces to consume typed state collections and transition results
- keep persisted `state` column values unchanged
- keep API DTO fields such as `queueState` and `taskState` unchanged

Status: implemented

### Phase 2: Runtime status

- centralize runtime-status normalization and semantics in `SessionRuntimeStatus`
- treat `working` as semantically equivalent to `running`, `busy`, and `retry`
- use typed runtime status in task overview, reconciliation, and cached status overrides
- keep `runtimeStatus.type` on the wire unchanged so the UI contract stays stable

Status: implemented

### Phase 3: Directory path

- remove duplicated string normalization and variant generation
- converge readers onto `DirectoryPath` helpers first
- follow up with a richer directory/path value object once the queue and runtime migrations have settled

Status: partially implemented

## Implementation Update

The refactor was implemented incrementally while preserving API and SQLite string contracts. The codebase now has typed value objects and typed accessors for the most error-prone state and issue metadata flows.

Implemented domain value objects:

- `QueueState`
- `SessionRuntimeStatus`
- `ViewerTaskStatus`
- `TaskReviewAction`
- `SonarIssueType`
- `SonarIssueSeverity`
- `SonarIssueStatus`
- richer `DirectoryPath` helpers/value wrapper

Implemented adoption points:

- orchestration queue lifecycle and dispatch/reconciliation logic now use `QueueState`
- OpenCode runtime parsing, status overrides, and session/task derivation now use `SessionRuntimeStatus`
- Sonar issue filtering, normalization, batching priority, and DTO parsing now use typed Sonar value objects
- review history and queue review transitions now use `TaskReviewAction`
- viewer/session/todo DTOs now expose parsed typed accessors while keeping existing string fields on the wire

Helper cleanup completed:

- added small helper methods such as `HasValue`, `OrNull()`, `Or(...)`, and `ToFilterList()` where useful
- replaced repeated empty-or-singleton list construction and fallback-string boilerplate in core services

Verification completed:

- targeted unit tests were added for value-object semantics and helper behavior
- existing server and Sonar/OpenCode unit tests were updated and kept passing
- response shapes and persisted text values were preserved for compatibility

Remaining follow-up work:

- `DirectoryPath` can still be pushed deeper so more services accept typed paths directly instead of raw strings
- some boundary/request models still accept raw strings first and then normalize internally; that is acceptable for now but could be tightened further later

## Guardrails

- preserve stable API field names and current wire string values
- preserve SQLite `TEXT` storage for state fields during the initial migration
- reject or normalize invalid values at boundaries rather than inside domain services
- update tests to assert typed semantics internally while keeping contract tests on current JSON output
