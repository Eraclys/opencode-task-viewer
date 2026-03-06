# OpenCode API Learnings

This document captures everything learned while building `opencode-task-viewer` against OpenCode Web Mode.

## Scope / Environment

- Observed against local OpenCode server at `http://127.0.0.1:4096`.
- Server version seen in session payloads: `1.2.15`.
- Findings are based on real responses plus integration testing in this repo.
- Some behavior is implementation-version specific; treat quirks as empirical, not guaranteed API contracts.

## Authentication

- OpenCode may run without auth in local mode.
- If Basic Auth is enabled, requests need:
  - `Authorization: Basic <base64(username:password)>`
- In this project we default username to `opencode` when password is set.

## Directory / Workspace Rules

- For API calls using `directory`, Windows paths must be normalized to forward slashes.
  - Works: `C:/GitRepositories/opencode-task-viewer`
  - Returns empty list: `C:\GitRepositories\opencode-task-viewer`
- For compatibility, pass directory both ways when possible:
  - Query: `?directory=...`
  - Header: `x-opencode-directory: ...`

## Session URL Routing in OpenCode Web

OpenCode web session links are workspace-prefixed:

`/<workspaceSlug>/session/<sessionId>`

Where `workspaceSlug` is base64url of UTF-8 directory text:

1. `base64(directory)`
2. Replace `+` with `-`
3. Replace `/` with `_`
4. Remove trailing `=`

Example:

- Directory: `C:/GitRepositories/opencode-task-viewer`
- Slug: `QzovR2l0UmVwb3NpdG9yaWVzL29wZW5jb2RlLXRhc2stdmlld2Vy`
- URL: `http://127.0.0.1:4096/QzovR2l0UmVwb3NpdG9yaWVzL29wZW5jb2RlLXRhc2stdmlld2Vy/session/ses_...`

## Endpoint Learnings

### `GET /project`

- Returns JSON array of projects.
- Important fields:
  - `id`
  - `worktree` (directory path; can be `/` for global pseudo-project)
  - `time`
- This is the reliable source for known project worktrees.

### `GET /project/:id`

- Returns HTML app shell in this build (not a JSON project detail payload).
- Do not rely on this endpoint for API metadata.

### `GET /experimental/session`

- Exists and returns sessions.
- `archived` query flag appears unreliable in this build:
  - observed `archived=false` and `archived=true` both increasing result count vs no flag.
- Session objects here did not reliably include `time.archived`.
- Conclusion: avoid this endpoint for archive filtering logic.

### `GET /session?directory=...`

- Returns sessions for a specific directory/worktree.
- Supports useful query params used in this project:
  - `roots=true`
  - `limit=<n>`
- Typical fields observed:
  - `id`
  - `slug`
  - `projectID`
  - `directory`
  - `title`
  - `version`
  - `summary`
  - `time.created`
  - `time.updated`
  - `time.archived` (when archived)
- Most reliable archive signal found: `time.archived` on this endpoint.

### `POST /session?directory=...`

- Creates a new session.
- Confirmed minimal working payload:
  - `{ "title": "..." }`
- Example successful response fields:
  - `id`, `slug`, `version`, `projectID`, `directory`, `title`, `time`
- Side effect note: this creates a real session immediately.

### `DELETE /session/:sessionID?directory=...`

- Deletes a session.
- Observed response body: `true` (JSON literal).
- Useful for cleanup during automated probes/tests.

### `GET /session/:sessionID`

- Returns a single session JSON object.
- Used for post-mutation verification (especially archive verification by checking `time.archived`).

### `PATCH /session/:sessionID`

- Archive mutation shape that worked in tests/mock and is attempted in integration:
  - `{ "time": { "archived": <timestamp> } }`
- Alternate shape attempted for compatibility:
  - `{ "archived": true }`

### `POST /session/:sessionID/archive`

- Treated as possible fallback route for archive operations.
- Kept as third archive attempt for compatibility across OpenCode builds.

### `GET /session/status?directory=...`

- Returns map keyed by session id.
- Example shape:
  - `{ "ses_...": { "type": "busy" } }`
- Observed status values: `busy`, `retry`, `idle` (and sometimes `running` via event context).
- If a session id is missing from map, treat as idle.

### `GET /session/:sessionID/todo`

- Returns todos for a session.
- Session viewer currently does not render todos as board cards, but endpoint is functional.

### `GET /session/:sessionID/message?limit=N`

- Returns session messages.
- Message role is available at `message.info.role` (e.g. `assistant`, `user`).
- In observed responses, `limit=1` returns the latest message.
- Returned messages are in chronological order (oldest -> newest) for the returned slice.
- Assistant message text may be in different shapes (`parts`, `text`, `content`, etc.), so extraction must be tolerant.

### `POST /session/:sessionID/message`

- Appends a message to the session.
- Confirmed working payload shape:
  - `{ "parts": [{ "type": "text", "text": "..." }], "noReply": true }`
- With `noReply: true`, this records a user message without starting an assistant run.
- Observed message response includes:
  - `info.id`, `info.role`, `info.sessionID`, `info.agent`, `info.model`, and `parts[]`.

### `POST /session/:sessionID/prompt_async`

- Fire-and-forget prompt execution endpoint.
- Confirmed minimal working payload:
  - `{ "parts": [{ "type": "text", "text": "..." }] }`
- Observed response status: `204` (no content).
- Session status transitions to `busy` shortly after.
- This is currently the most practical endpoint for bulk automation without holding open long HTTP requests.

### `POST /session/:sessionID/init`

- Used to initiate a response for an existing message.
- If required fields are missing, returns validation error (observed `400`) requiring:
  - `modelID` (string)
  - `providerID` (string)
- In local probing, this endpoint appeared long-running/blocking compared to `prompt_async`.
- Practical recommendation for automation: prefer `prompt_async` unless strict `init` semantics are required.

### `GET /global/event` (SSE)

- SSE endpoint for global updates.
- Event payload shape handled in this project:
  - `{ directory, payload: { type, properties } }`
- Event types seen/handled:
  - `todo.updated`
  - `session.status`
  - `session.created`
  - `session.updated`
  - `session.deleted`
  - `message.*`
- Important caveat: `message.*` events may not include a stable session id.

## Archive-Related Findings

- For filtering archived sessions, per-directory `/session` + `time.archived` is currently the most dependable strategy.
- Cross-project listing should be:
  1. `GET /project`
  2. For each project worktree (excluding `/`), call `GET /session?directory=...`
  3. Drop sessions with `time.archived`
- Archive mutation should be defensive across versions:
  1. `PATCH /session/:id` with `time.archived`
  2. `PATCH /session/:id` with `archived: true`
  3. `POST /session/:id/archive`
  4. verify with `GET /session/:id`

## Session Status Derivation Strategy Used Here

- Runtime `busy|retry|running` => board status `in_progress`.
- Otherwise, determine if assistant has ever responded by scanning messages.
  - Any assistant message => `completed`
  - None => `pending`
- If message lookup fails, fallback to time-window heuristic.

## Known Quirks / Caveats

- `GET /project/:id` is not a JSON detail endpoint in this build.
- `GET /experimental/session` archive filtering is unreliable.
- Backslash directory filtering can silently return no sessions.
- `message.*` SSE events can be coarse (session id not always present), so cache invalidation may need to be broad.
- Session-creation / prompt endpoints are side-effecting; probes should clean up temp sessions.
- `init` and `prompt_async` have different ergonomics (`init` validation + possible long request; `prompt_async` async + 204).

## Additional Endpoint Catalog (from OpenCode Web bundle introspection)

The web bundle exposes more session routes than this viewer currently uses, including:

- `POST /session/{sessionID}/command`
- `POST /session/{sessionID}/shell`
- `POST /session/{sessionID}/revert`
- `POST /session/{sessionID}/unrevert`
- `POST /session/{sessionID}/summarize`
- `POST /session/{sessionID}/fork`
- `POST /session/{sessionID}/share`
- `DELETE /session/{sessionID}/share`
- `POST /session/{sessionID}/permissions/{permissionID}`
- message-part mutation routes (`PATCH`/`DELETE` on part endpoints)

These were discovered from client bundle route definitions and not all were functionally probed here.

## Feasibility Notes: "JSON dump -> create sessions per file"

Given current findings, this feature is feasible with moderate effort.

High-level API flow per affected file:

1. Resolve file path to a project/worktree directory.
2. `POST /session?directory=<worktree>` with a generated title.
3. Send prompt payload including instructions + file/line context using either:
   - `POST /session/:id/prompt_async` (recommended), or
   - `POST /session/:id/message` then `POST /session/:id/init`.
4. Track progress via `GET /session/status` and/or SSE `GET /global/event`.
5. Build OpenCode URL via `/<base64url(directory)>/session/<id>`.

Main complexity drivers:

- Robust JSON schema validation and user feedback for malformed input.
- Grouping files by worktree when JSON includes mixed projects.
- Prompt templating (instructions + file/line data) and token-size guardrails.
- Concurrency/rate limits when creating many sessions at once.
- Partial failure handling and retry strategy.

Complexity estimate for this repo:

- Backend API support: medium.
- Frontend upload/preview UX: medium.
- End-to-end reliability + tests: medium-high.

## Practical Checklist for New Integrations

1. Normalize directories to forward-slash format.
2. Build project list from `GET /project`.
3. Enumerate sessions per worktree via `GET /session?directory=...`.
4. Use `time.archived` to exclude archived sessions.
5. Use `GET /session/status` map for runtime activity.
6. Use `GET /session/:id/message` to infer assistant response completion.
7. Subscribe to `GET /global/event` for live updates.
8. Generate web links as `/<base64url(directory)>/session/<sessionId>`.
