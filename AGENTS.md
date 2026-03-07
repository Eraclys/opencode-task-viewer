# Agent Guide (opencode-task-viewer)
This repo is an ASP.NET Core server plus a single-page UI served from `public/index.html` (no build step).

## Quick Layout
- `src/TaskViewer.Server/Program.cs`: ASP.NET Core host + OpenCode API proxying + SSE fanout + caching
- `src/TaskViewer.Server/SonarOrchestrator.cs`: Sonar queue/orchestration runtime + SQLite persistence
- `public/index.html`: UI (inline CSS + inline JS; no bundler)
- `tests/TaskViewer.E2E.Tests/*`: xUnit + Playwright browser E2E tests
- `tests/TaskViewer.MockOpenCode/Program.cs`: .NET mock OpenCode API + SSE
- `tests/TaskViewer.MockSonarQube/Program.cs`: .NET mock SonarQube API

## Requirements
- .NET SDK: `>= 8.0`

## Build / Run
```bash
dotnet build TaskViewer.slnx
dotnet run --project src/TaskViewer.Server/TaskViewer.Server.csproj
```

Environment (examples):
```bash
PORT=8080 HOST=127.0.0.1 dotnet run --project src/TaskViewer.Server/TaskViewer.Server.csproj
OPENCODE_URL=http://localhost:4096 dotnet run --project src/TaskViewer.Server/TaskViewer.Server.csproj
OPENCODE_USERNAME=opencode OPENCODE_PASSWORD=... dotnet run --project src/TaskViewer.Server/TaskViewer.Server.csproj
```

## Test
Run all tests:
```bash
dotnet test TaskViewer.slnx
```

Run E2E only:
```bash
dotnet test tests/TaskViewer.E2E.Tests/TaskViewer.E2E.Tests.csproj
```

Playwright browser install (first run on Windows):
```bash
dotnet build tests/TaskViewer.E2E.Tests/TaskViewer.E2E.Tests.csproj
powershell ./tests/TaskViewer.E2E.Tests/bin/Debug/net8.0/playwright.ps1 install chromium
```

## Lint / Sanity
No linter is configured. Use build + tests:
```bash
dotnet build TaskViewer.slnx
dotnet test TaskViewer.slnx
```

## Code Style (match existing files)

### C# / .NET
- Prefer modern C# with nullable awareness enabled.
- Use runtime validation and defensive parsing for upstream OpenCode/Sonar responses.
- Keep route handlers small and extract helpers for non-trivial logic.
- Normalize Windows paths to forward slashes when used as `directory` query/header values.

### Error handling
- Server routes should return stable JSON errors (`{ error: "..." }`) on failures.
- Include context in upstream failure messages for diagnostics.

### HTTP/API
- Viewer API routes live under `/api/*` in `src/TaskViewer.Server/Program.cs`.
- Keep response shapes stable; UI in `public/index.html` depends on exact fields.

### UI (`public/index.html`)
- Keep single-file architecture (inline CSS/JS).
- Keep selectors stable and add `data-testid` for test interactions.

### Tests
- E2E tests use Playwright .NET + xUnit.
- Mocked upstream behavior should be implemented in .NET mock projects under `tests/`.

## Making Changes Safely
- If you change `/api/*` response shapes in `src/TaskViewer.Server/Program.cs`, update `public/index.html` and E2E tests.
- Avoid unnecessary dependencies; keep the project lightweight.
