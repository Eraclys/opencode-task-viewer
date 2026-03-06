# OpenCode Task Viewer

A real-time Kanban board for observing OpenCode Web Mode sessions across all projects.

## What It Shows

- Sessions (across all OpenCode projects)
- A Kanban view using the existing four columns (Pending / In Progress / Completed / Cancelled)
- Session status is derived from runtime + whether an assistant ever responded:
  - `In Progress`: runtime `busy|retry`
  - `Pending`: no assistant response in session message history
  - `Completed`: assistant has responded at least once
- Project filter with local storage persistence
- Default card ordering: project, then session name
- Single and bulk archive actions
- Detail panel: last agent message + direct link to OpenCode Web session
- Optional SonarQube orchestration queue (per-issue session creation)
- Live updates via SSE (proxied from OpenCode `/global/event`)

## Requirements

- Node.js >= 18
- OpenCode Web Mode server running (default: `http://localhost:4096`)

## Run

```bash
npm install
npm start
```

Open http://127.0.0.1:3456

## Configuration

```bash
# Viewer server
PORT=8080 npm start
HOST=127.0.0.1 npm start

# OpenCode server
OPENCODE_URL=http://localhost:4096 npm start

# SonarQube orchestration (optional)
SONARQUBE_URL=http://localhost:9000 SONARQUBE_TOKEN=... npm start
ORCHESTRATOR_DB_PATH=./data/orchestrator.sqlite npm start
ORCH_MAX_ACTIVE=3 ORCH_POLL_MS=3000 ORCH_MAX_ATTEMPTS=3 npm start

# If your OpenCode server uses Basic Auth
OPENCODE_USERNAME=opencode OPENCODE_PASSWORD=... npm start
```

## API (Viewer)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/sessions` | GET | List sessions with derived kanban status + runtime status |
| `/api/sessions/:sessionId/last-assistant-message` | GET | Fetch latest assistant message text for a session |
| `/api/orch/config` | GET | SonarQube orchestration runtime config/status |
| `/api/orch/mappings` | GET/POST | List and upsert Sonar project key -> worktree mappings |
| `/api/orch/issues` | GET | Fetch SonarQube issues for selected mapping and issue type |
| `/api/orch/enqueue` | POST | Queue per-issue orchestration jobs with custom instructions |
| `/api/orch/queue` | GET | Read queue items and queue stats |
| `/api/orch/queue/:queueId/cancel` | POST | Cancel queued/dispatching queue item |
| `/api/orch/queue/retry-failed` | POST | Requeue failed items |
| `/api/tasks/all` | GET | (Legacy) Get todos across sessions |
| `/api/events` | GET | SSE stream (proxied from OpenCode global events) |

## Notes

- The UI currently ignores OpenCode todos and focuses on sessions only.
- Session archive actions are supported (single and bulk archive).
- When SonarQube orchestration is configured, queued jobs appear as synthetic Pending cards until an OpenCode session is created.

## Tests

```bash
npm run test:e2e:install
npm run test:e2e
```
