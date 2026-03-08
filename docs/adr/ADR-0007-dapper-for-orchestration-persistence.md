# ADR-0007: Use Dapper for Orchestration Persistence

- Status: Completed
- Date: 2026-03-08
- Depends on: ADR-0002, ADR-0006
- Related: ADR-0003

## Decision

Keep SQLite as the orchestration persistence store, but replace manual low-level ADO.NET command and reader code with Dapper in the orchestration repositories.

The first slice applies only to the orchestration persistence layer in `src/TaskViewer.Server`, specifically mapping and queue storage.

## Context

The current orchestration repositories own the right behavior boundaries, but they still carry a large amount of repetitive database ceremony:

- manual `SqliteCommand` creation for nearly every query
- repeated parameter null handling helpers
- repeated `SqliteDataReader` field extraction and date parsing
- multi-step read-after-write flows obscured by plumbing rather than intent

This makes the persistence layer harder to read and change than the underlying business rules require.

The problem is not SQL itself. The SQL is explicit and understandable. The problem is the amount of surrounding mapping and parameter boilerplate needed to execute that SQL with raw ADO.NET.

## Scope

This ADR covers:

- `src/TaskViewer.Server/Infrastructure/Orchestration/SqliteMappingRepository.cs`
- `src/TaskViewer.Server/Infrastructure/Orchestration/SqliteQueueRepository.cs`
- schema initialization in `src/TaskViewer.Server/SonarOrchestrator.cs`

This ADR does not require:

- changing the database engine
- introducing EF Core or another ORM
- changing repository interfaces or API response shapes
- rewriting non-database orchestration logic

## Plan

1. Add `Dapper` via central package management and reference it from `src/TaskViewer.Server/TaskViewer.Server.csproj`.
2. Keep SQL explicit inside orchestration repositories.
3. Replace manual reader and parameter plumbing with Dapper query and execute APIs.
4. Centralize SQLite-specific null and date conversion helpers where repository rows still need normalization.
5. Preserve existing schema, repository contracts, queue semantics, and tests.
6. Keep the current repository lock strategy for the first slice so behavior changes stay small.

## Consequences

### Positive

- repository methods express query intent more directly
- less duplicated null, parameter, and date parsing code
- lower maintenance cost for queue and mapping persistence
- easier future additions to queue queries and update flows

### Negative

- introduces one more dependency in the server project
- Dapper row mapping still requires deliberate aliasing and conversion discipline
- some transactional flows still need explicit thought; Dapper reduces ceremony but does not replace correctness concerns

## Progress Snapshot (2026-03-08)

- Completed:
  - Added `Dapper` to `Directory.Packages.props` and `src/TaskViewer.Server/TaskViewer.Server.csproj`.
  - Converted schema initialization in `src/TaskViewer.Server/SonarOrchestrator.cs` from raw command execution to a Dapper execute call.
  - Reworked `src/TaskViewer.Server/Infrastructure/Orchestration/SqliteMappingRepository.cs` around Dapper queries and updates while preserving repository contracts.
  - Reworked `src/TaskViewer.Server/Infrastructure/Orchestration/SqliteQueueRepository.cs` around Dapper queries, updates, scalar reads, and an explicit transaction for queue claiming.
  - Added `src/TaskViewer.Server/Infrastructure/Orchestration/SqliteOrchestrationDataMapper.cs` to centralize SQLite string/date normalization used by Dapper row mapping.
  - Added focused repository coverage in `tests/TaskViewer.Server.Tests/SqliteQueueRepositoryTests.cs` for enqueue, duplicate skipping, oldest-first claiming, retry/failure transitions, session creation persistence, cancellation, and queue stats.
  - Verified the migration with `dotnet build TaskViewer.slnx` and `dotnet test TaskViewer.slnx`.
- Follow-up considerations:
  - If future persistence queries grow more complex, consider extracting shared queue SQL fragments into small internal helpers without hiding the SQL itself.
  - If `slopwatch` becomes part of the repo toolchain later, add it as a local tool so persistence refactors can be checked automatically in CI and local sessions.

## Acceptance Criteria

- Orchestration repositories no longer use manual `SqliteDataReader` mapping.
- SQL remains explicit and local to repository methods.
- Queue claim, retry, cancellation, and failure semantics remain unchanged.
- Existing tests continue to pass without API or schema changes.
