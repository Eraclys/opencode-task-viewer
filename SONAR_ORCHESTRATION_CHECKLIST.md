# SonarQube Orchestration Checklist

This checklist captures the agreed plan before implementation.

## Scope and outcomes

- [ ] Build a standalone orchestration service for SonarQube -> OpenCode automation.
- [ ] Keep `opencode-task-viewer` as the UI layer, and integrate orchestrator queue visibility.
- [ ] Process items per issue (not per file) to minimize prompt scope and token usage.
- [ ] Allow users to pick a mapped project, filter by issue type, provide instructions, and enqueue.
- [ ] Show queued items in Pending even before OpenCode sessions are created.

## Architecture and boundaries

- [ ] Create a new service (`opencode-orchestrator`) with clear module boundaries:
  - Sonar client
  - Queue store
  - Worker/dispatcher
  - OpenCode client
  - API routes
- [ ] Keep Sonar/OpenCode credentials server-side only (never exposed to browser).
- [ ] Add a small integration contract between viewer and orchestrator (`/api/orch/*`).

## Durable queue (SQLite)

- [ ] Add SQLite storage for durable background jobs.
- [ ] Create schema for `project_mappings`.
- [ ] Create schema for `instruction_profiles`.
- [ ] Create schema for `queue_items` with lifecycle states:
  - `queued`
  - `dispatching`
  - `session_created`
  - `done`
  - `failed`
  - `cancelled`
- [ ] Persist retries, attempt counts, last error, timestamps.
- [ ] Enforce de-duplication for active queue items by Sonar `issueKey`.

## Project mapping and filters

- [ ] Support multiple Sonar project keys mapped to OpenCode directories.
- [ ] Add optional per-mapping branch support.
- [ ] Normalize directory paths for OpenCode compatibility (forward slashes).
- [ ] Add APIs to create/list/update mappings.
- [ ] Add APIs to fetch Sonar issues by selected mapping and filters (including issue type).

## Instruction flow (before enqueue)

- [ ] Add UI step to enter custom instructions after selecting issue type.
- [ ] Save reusable instruction profiles per mapping + issue type.
- [ ] Snapshot instructions onto each queue item at enqueue time.
- [ ] Include issue metadata in prompt construction:
  - project key
  - issue key
  - rule
  - file path
  - line
  - message
  - severity/type

## Worker and dispatching

- [ ] Implement background worker loop with configurable concurrency.
- [ ] Add capped dispatch (`max active`) to avoid resolving all issues at once.
- [ ] Add retry with exponential backoff and max-attempt guardrails.
- [ ] Add cancel and retry-failed controls.
- [ ] Track and expose queue stats (queued, running, failed, done).

## OpenCode session orchestration

- [ ] For each queued issue, create one OpenCode session in mapped directory.
- [ ] Send prompt via OpenCode async endpoint and record returned session context.
- [ ] Generate and persist OpenCode web link for each created session.
- [ ] Transition queue state from queued item to session-backed progress.

## Viewer integration

- [ ] Add orchestrator API consumption in `opencode-task-viewer`.
- [ ] Render queued synthetic cards in Pending when no session exists yet.
- [ ] Replace synthetic queued card with real session card once session is created.
- [ ] Expose queue error state clearly (failed/cancelled view).
- [ ] Keep existing session board behavior unchanged for non-orchestrated sessions.

## API endpoints (orchestrator)

- [ ] `GET /api/orch/mappings`
- [ ] `POST /api/orch/mappings`
- [ ] `GET /api/orch/issues`
- [ ] `POST /api/orch/enqueue`
- [ ] `GET /api/orch/queue`
- [ ] `POST /api/orch/queue/:id/cancel`
- [ ] `POST /api/orch/queue/retry-failed`

## Reliability and observability

- [ ] Add structured logging for queue transitions and OpenCode dispatch results.
- [ ] Add health endpoint with queue/worker status.
- [ ] Add startup recovery (resume queued/dispatching items safely after restart).
- [ ] Add timeout handling for Sonar and OpenCode API calls.

## Testing

- [ ] Add mock SonarQube test server for deterministic E2E.
- [ ] Add queue lifecycle tests (enqueue -> dispatch -> created/done/failed).
- [ ] Add restart persistence tests (jobs survive process restart).
- [ ] Add viewer integration tests for queued Pending cards.
- [ ] Add partial-failure and retry behavior tests.

## Documentation and rollout

- [ ] Document environment variables and mapping setup.
- [ ] Document operational runbook (queue limits, retries, failure handling).
- [ ] Document first-run local setup and test commands.
- [ ] Define extraction criteria for moving from hybrid mode to fully standalone deployment.
