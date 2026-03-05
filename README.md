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

# If your OpenCode server uses Basic Auth
OPENCODE_USERNAME=opencode OPENCODE_PASSWORD=... npm start
```

## API (Viewer)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/sessions` | GET | List sessions with derived kanban status + runtime status |
| `/api/sessions/:sessionId/last-assistant-message` | GET | Fetch latest assistant message text for a session |
| `/api/tasks/all` | GET | (Legacy) Get todos across sessions |
| `/api/events` | GET | SSE stream (proxied from OpenCode global events) |

## Notes

- The UI currently ignores OpenCode todos and focuses on sessions only.
- Session archive actions are supported (single and bulk archive).

## Tests

```bash
npm run test:e2e:install
npm run test:e2e
```
