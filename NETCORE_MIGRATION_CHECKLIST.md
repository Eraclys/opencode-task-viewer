# .NET Core Full Replacement Checklist

Goal: replace the Node/Express runtime (`server.js` + `sonar-orchestrator.js`) with an ASP.NET Core app while preserving the existing UI (`public/index.html`) and API behavior.

## 0) Scope and Guardrails

- [x] Keep `public/index.html` unchanged initially; backend parity first.
- [x] Preserve all current `/api/*` response shapes and status codes unless explicitly improved.
- [x] Preserve environment-variable-driven configuration semantics.
- [x] Preserve runtime behaviors: cache TTLs, SSE update fanout, OpenCode upstream SSE reconnect/backoff.
- [x] Keep Playwright E2E tests as the source of truth for parity.

## 1) Inventory and Baseline Lock

- [x] Record current endpoint inventory from `server.js`:
  - [x] `GET /api/sessions`
  - [x] `GET /api/sessions/:sessionId`
  - [x] `GET /api/sessions/:sessionId/last-assistant-message`
  - [x] `POST /api/sessions/:sessionId/archive`
  - [x] `GET /api/orch/config`
  - [x] `GET /api/orch/mappings`
  - [x] `POST /api/orch/mappings`
  - [x] `GET /api/orch/instructions`
  - [x] `POST /api/orch/instructions`
  - [x] `GET /api/orch/issues`
  - [x] `GET /api/orch/rules`
  - [x] `POST /api/orch/enqueue`
  - [x] `POST /api/orch/enqueue-all`
  - [x] `GET /api/orch/queue`
  - [x] `POST /api/orch/queue/:queueId/cancel`
  - [x] `POST /api/orch/queue/retry-failed`
  - [x] `POST /api/orch/queue/clear`
  - [x] `GET /api/tasks/all`
  - [x] `POST /api/tasks/:sessionId/:taskId/note` (currently 501)
  - [x] `DELETE /api/tasks/:sessionId/:taskId` (currently 501)
  - [x] `GET /api/events` (viewer SSE)
- [x] Capture baseline behavior examples with mock OpenCode and Playwright.
- [ ] Run and store baseline E2E results before migration work.

## 2) Create ASP.NET Core Host Skeleton

- [x] Create new ASP.NET Core web app project in repo (no UI build tooling).
- [x] Serve static files from `public/` (copy or mount existing assets).
- [x] Implement root route fallback to `public/index.html`.
- [x] Add options/config handling for env vars currently used by Node.
  - [x] `PORT`, `HOST`
  - [x] `OPENCODE_URL`, `OPENCODE_USERNAME`, `OPENCODE_PASSWORD`
  - [x] `SONARQUBE_URL`, `SONARQUBE_TOKEN`
  - [x] `ORCHESTRATOR_DB_PATH`, `ORCH_MAX_ACTIVE`, `ORCH_POLL_MS`, `ORCH_MAX_ATTEMPTS`, `ORCH_MAX_WORKING_GLOBAL`, `ORCH_WORKING_RESUME_BELOW`
- [x] Add OpenCode and SonarQube HTTP clients with auth support.

## 3) Core Utility Port (Behavioral Parity)

- [x] Port path/directory normalization helpers (including slash compatibility and cache keys).
- [x] Port tolerant parsing helpers for unstable OpenCode response shapes.
- [x] Port todo status/priority normalization.
- [x] Port OpenCode fetch wrapper behavior:
  - [x] Include `directory` in query and `x-opencode-directory` header.
  - [x] Preserve JSON/text/204 handling.
  - [x] Preserve contextual error handling for upstream failures.
- [x] Port in-memory caches and invalidation strategy.
- [x] Port assistant-presence cache + in-flight de-duplication behavior.

## 4) API Endpoint Migration (Feature Parity)

- [x] Implement `/api/sessions` with filtering, sorting, and cache semantics equivalent to Node.
- [x] Implement `/api/sessions/:sessionId` todo retrieval behavior.
- [x] Implement `/api/sessions/:sessionId/last-assistant-message` behavior.
- [x] Implement `/api/sessions/:sessionId/archive` mutation and cache invalidation.
- [x] Implement `/api/tasks/all` aggregation behavior and cache TTL behavior.
- [x] Implement unsupported todo mutation endpoints returning `501` with same error shape.
- [x] Implement all orchestration endpoints under `/api/orch/*`.
- [x] Ensure all endpoints return stable `{ error: "..." }` JSON on failure paths.
- [x] Ensure `Cache-Control: no-store` style headers remain on live-state endpoints.

## 5) SSE and Eventing Migration

- [x] Implement viewer-side SSE endpoint (`GET /api/events`) with connected handshake payload.
- [x] Implement upstream OpenCode `/global/event` SSE listener in .NET.
- [x] Implement SSE parser tolerant of framing/newline variations.
- [x] Implement reconnect loop with exponential backoff and reset-on-success behavior.
- [x] Port event handlers for:
  - [x] `todo.updated`
  - [x] `session.status`
  - [x] `session.created`
  - [x] `session.updated`
  - [x] `session.deleted`
  - [x] `message.*`
- [x] Preserve cache invalidation and targeted/full broadcast behavior for each event type.

## 6) Orchestrator Replacement Strategy

- [x] Decide storage engine equivalent to current `sql.js` file-backed DB.
  - [x] SQLite via `Microsoft.Data.Sqlite` + explicit schema creation.
- [x] Port schema and queue state machine from `sonar-orchestrator.js`:
  - [x] `project_mappings`
  - [x] `instruction_profiles`
  - [x] `queue_items`
- [x] Port background poller/tick loop with bounded concurrency.
- [x] Port attempt/backoff logic and retry/cancel/clear semantics.
- [x] Port Sonar rule/issue scanning and instruction profile resolution.
- [x] Port OpenCode session creation + prompt dispatch orchestration behavior.
- [x] Ensure orchestrator `onChange` behavior triggers cache invalidation + SSE broadcast.

## 7) Test Harness Migration

- [x] Replace JavaScript Playwright tests with .NET Playwright + xUnit tests.
- [x] Replace Node mock OpenCode and Node mock Sonar servers with .NET mock hosts.
- [x] Remove `.runtime.json` setup/teardown dependency by using xUnit fixture-managed process orchestration.
- [x] Run all Playwright parity tests and fix regressions.
- [ ] Add backend-level integration tests for critical API and SSE behavior in .NET.

## 8) Operational Readiness

- [ ] Add structured logging for upstream failures and reconnect loops.
- [x] Add health endpoint(s) used by local/dev automation.
- [x] Validate graceful shutdown for:
  - [x] SSE client connections
  - [x] Upstream SSE listener
  - [x] Orchestrator background worker
- [x] Validate environment compatibility on Windows path semantics.

## 9) Cutover and Cleanup

- [x] Switch default run scripts/docs to .NET host commands.
- [x] Remove obsolete JavaScript runtime and test harness files (`package.json`, JS Playwright specs/config, JS mock servers).
- [x] Remove obsolete Node server/orchestrator files once parity is confirmed.
- [x] Update README/AGENTS docs for new architecture and run/test commands.
- [x] Run final parity pass with Playwright and manual smoke checks.

## 10) Done Criteria

- [x] All existing Playwright E2E tests pass against the ASP.NET Core host.
- [x] API shapes consumed by `public/index.html` are unchanged.
- [x] Live update behavior (SSE) matches Node implementation under reconnect/error conditions.
- [x] Orchestration queue behaviors match expected semantics.
- [x] Node runtime can be removed without loss of functionality.

## Suggested Execution Order (Implementation Waves)

- [x] Wave 1: Host + static files + config + `/api/sessions` + `/api/tasks/all`.
- [x] Wave 2: Viewer SSE endpoint + upstream SSE listener + invalidation parity.
- [x] Wave 3: Remaining session endpoints + archive + parity hardening.
- [x] Wave 4: Full `/api/orch/*` port + queue/background worker.
- [x] Wave 5: Test harness migration + full regression sweep + cutover.
