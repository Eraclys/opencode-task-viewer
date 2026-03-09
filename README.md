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
- Queue-all action for a selected specific rule key + clear-queued failsafe
- Live updates via SSE (proxied from OpenCode `/global/event`)

## Requirements

- .NET SDK >= 9.0
- OpenCode Web Mode server running (default: `http://localhost:4096`)

## Run

```bash
dotnet run --project src/TaskViewer.Server/TaskViewer.Server.csproj
```

Open http://127.0.0.1:3456

## Configuration

```bash
# Viewer server
PORT=8080 dotnet run --project src/TaskViewer.Server/TaskViewer.Server.csproj
HOST=127.0.0.1 dotnet run --project src/TaskViewer.Server/TaskViewer.Server.csproj

# OpenCode server
OPENCODE_URL=http://localhost:4096 dotnet run --project src/TaskViewer.Server/TaskViewer.Server.csproj

# SonarQube orchestration (optional)
SONARQUBE_URL=http://localhost:9000 SONARQUBE_TOKEN=... dotnet run --project src/TaskViewer.Server/TaskViewer.Server.csproj
SONARQUBE_MODE=fake dotnet run --project src/TaskViewer.Server/TaskViewer.Server.csproj
ORCHESTRATOR_DB_PATH=./data/orchestrator.sqlite dotnet run --project src/TaskViewer.Server/TaskViewer.Server.csproj
ORCH_MAX_ACTIVE=3 ORCH_POLL_MS=3000 ORCH_MAX_ATTEMPTS=3 dotnet run --project src/TaskViewer.Server/TaskViewer.Server.csproj
ORCH_MAX_WORKING_GLOBAL=5 ORCH_WORKING_RESUME_BELOW=4 dotnet run --project src/TaskViewer.Server/TaskViewer.Server.csproj

# If your OpenCode server uses Basic Auth
OPENCODE_USERNAME=opencode OPENCODE_PASSWORD=... dotnet run --project src/TaskViewer.Server/TaskViewer.Server.csproj
```

## API (Viewer)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/tasks/board` | GET | List task board items for the main kanban |
| `/api/tasks/board/:taskId/last-assistant-message` | GET | Fetch latest assistant message text for a queue-backed task |
| `/api/sessions` | GET | Legacy compatibility route for task board items |
| `/api/health` | GET | Lightweight health check for the viewer host |
| `/api/sessions/:sessionId/last-assistant-message` | GET | Fetch latest assistant message text for a session |
| `/api/orch/config` | GET | SonarQube orchestration runtime config/status |
| `/api/orch/mappings` | GET/POST | List and upsert Sonar project key -> worktree mappings |
| `/api/orch/issues` | GET | Fetch SonarQube issues for selected mapping and issue type |
| `/api/orch/rules` | GET | Fetch rule keys with display names and counts for selected mapping/type |
| `/api/orch/enqueue` | POST | Queue per-issue orchestration jobs with custom instructions |
| `/api/orch/enqueue-all` | POST | Queue all matching issues for selected mapping/type/specific rule key |
| `/api/orch/queue` | GET | Read queue items, queue stats, and worker backpressure state |
| `/api/orch/queue/:queueId/cancel` | POST | Cancel queued/dispatching queue item |
| `/api/orch/queue/retry-failed` | POST | Requeue failed items |
| `/api/orch/queue/clear` | POST | Cancel all currently queued (not dispatching) items |
| `/api/tasks/all` | GET | (Legacy) Get todos across sessions |
| `/api/events` | GET | SSE stream (proxied from OpenCode global events) |

## Notes

- The main UI is task-native and is driven by orchestration queue/task state.
- `/api/sessions` is retained temporarily as a compatibility route while task-native route cleanup continues.
- When SonarQube orchestration is configured, main board cards represent durable tasks rather than raw OpenCode sessions.
- For local UI exploration without SonarQube, run with `SONARQUBE_MODE=fake` to use a built-in fake dataset (`gamma-key`, `alpha-key`) without starting the mock Sonar server.
- Dispatcher backpressure: dequeue pauses when active OpenCode working sessions reach `ORCH_MAX_WORKING_GLOBAL`, and resumes when below `ORCH_WORKING_RESUME_BELOW`.

## Tests

```bash
dotnet test TaskViewer.slnx
```

Playwright browser install (first run):

```bash
dotnet build tests/TaskViewer.E2E.Tests/TaskViewer.E2E.Tests.csproj
powershell ./tests/TaskViewer.E2E.Tests/bin/Debug/net9.0/playwright.ps1 install chromium
```

## Package Management

- NuGet package versions are centrally managed in `Directory.Packages.props`.
- Use `dotnet add package ...` and `dotnet remove package ...` so project files keep versionless `PackageReference` entries under CPM.
