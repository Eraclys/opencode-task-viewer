# ADR-0004: External API Adapters with Schema-Tolerant Typed Deserialization

- Status: Completed
- Date: 2026-03-07
- Depends on: ADR-0001

## Decision

OpenCode and Sonar integrations will use dedicated adapter modules that deserialize into typed transport DTOs with tolerant parsers for optional/variant fields.

## Plan

1. Create adapter-specific transport DTOs for OpenCode project/session/status/message/todo payloads.
2. Create adapter-specific transport DTOs for Sonar rules/issues payloads.
3. Use targeted parsing helpers (`JsonDocument`/`JsonElement`) for known variant fields.
4. Map transport DTOs to application DTOs/value objects at adapter boundary.

## Progress Snapshot (2026-03-08)

- Completed:
  - OpenCode session/project/status/message array parsing was extracted from `src/TaskViewer.Server/Infrastructure/OpenCode/OpenCodeSessionSearchService.cs` into `src/TaskViewer.Server/Infrastructure/OpenCode/OpenCodeResponseParsers.cs`.
  - Parser responsibilities for array-shape normalization, status map tolerance, archived timestamp handling, assistant-message discovery, project search entry derivation, and session projection are now centralized in one adapter-layer module.
  - OpenCode assistant-message payload parsing was moved out of the generic application layer into `src/TaskViewer.Server/Infrastructure/OpenCode/OpenCodeMessageParsers.cs`.
  - Focused tests were added in `tests/TaskViewer.Server.Tests/OpenCodeResponseParsersTests.cs` to lock in observed payload variants.
  - OpenCode event payload parsing was extracted from `src/TaskViewer.Server/Infrastructure/OpenCode/OpenCodeEventHandler.cs` into `src/TaskViewer.Server/Infrastructure/OpenCode/OpenCodeEventParser.cs` with a typed `OpenCodeEventEnvelope`.
  - Focused tests were added in `tests/TaskViewer.Server.Tests/OpenCodeEventParserTests.cs` for missing-type, legacy `sessionID`, nested status, and fallback status-type variants.
  - Focused tests were added in `tests/TaskViewer.Server.Tests/OpenCodeMessageParsersTests.cs` for role, nested parts, nested markdown/value text, and timestamp variants.
  - Sonar issue/rule response-shape parsing was extracted into `src/TaskViewer.Server/Infrastructure/Orchestration/SonarResponseParsers.cs` and reused by issue, rule, enqueue-all, and cached-rule readers.
  - Focused tests were added in `tests/TaskViewer.Server.Tests/SonarResponseParsersTests.cs` for issue arrays, paging fallbacks, rule-name parsing, rule-key parsing, and normalized issue mapping.
  - Sonar raw issue normalization was moved from `src/TaskViewer.Server/Application/Orchestration/SonarIssueNormalizer.cs` into `src/TaskViewer.Server/Infrastructure/Orchestration/SonarIssueNormalizer.cs`.
  - Focused tests were added in `tests/TaskViewer.Server.Tests/SonarIssueNormalizerTests.cs` for `issueKey` fallback, `file` fallback, default issue type, and component-to-path normalization variants.
  - Inbound orchestration request-body parsing for mapping upserts, instruction-profile upserts, enqueue, and enqueue-all was extracted into `src/TaskViewer.Server/Infrastructure/Orchestration/OrchestrationRequestParsers.cs`.
  - Focused tests were added in `tests/TaskViewer.Server.Tests/OrchestrationRequestParsersTests.cs` for legacy field aliases, nullable/optional fields, issue arrays, and rule fallback variants.
  - OpenCode working-session status parsing used by orchestration backpressure was extracted from `src/TaskViewer.Server/Application/Orchestration/WorkingSessionsReadService.cs` into `src/TaskViewer.Server/Infrastructure/OpenCode/OpenCodeStatusParsers.cs`.
  - Focused tests were added in `tests/TaskViewer.Server.Tests/OpenCodeStatusParsersTests.cs` for lowercase normalization, missing-type filtering, and invalid-payload fallbacks.
  - OpenCode todo-shape normalization was moved from `src/TaskViewer.Server/Application/TodoNormalization.cs` into `src/TaskViewer.Server/Infrastructure/OpenCode/OpenCodeTodoParsers.cs`.
  - Focused tests were added in `tests/TaskViewer.Server.Tests/OpenCodeTodoParsersTests.cs` for status normalization, priority normalization, and todo projection.
  - OpenCode session-creation response parsing used by queue dispatch was extracted from `src/TaskViewer.Server/Application/Orchestration/QueueDispatchService.cs` into `src/TaskViewer.Server/Infrastructure/OpenCode/OpenCodeDispatchParsers.cs`.
  - Focused tests were added in `tests/TaskViewer.Server.Tests/OpenCodeDispatchParsersTests.cs` for trimmed, missing, and blank session-id variants.
  - Typed request contracts now flow through orchestration use-case/gateway/orchestrator signatures for mapping upsert, instruction-profile upsert, enqueue, and enqueue-all instead of raw `JsonNode` payloads crossing those layers.
  - Endpoint wiring and direct orchestrator tests were updated to construct typed orchestration request contracts at the boundary.
  - The enqueue pipeline now passes `NormalizedIssue` values instead of raw issue JSON after boundary normalization, so queue-enqueue and enqueue-all flows no longer pass raw external issue payloads through those layers.
  - Invalid issue inputs are now identified at the orchestration boundary while preserving the existing `invalid-issue` skip contract in enqueue responses.
  - `WorkingSessionsReadService` now depends on a typed `IOpenCodeStatusReader` adapter instead of a raw OpenCode fetch delegate returning `JsonNode`.
  - `QueueDispatchService` now depends on a typed `IOpenCodeDispatchClient` adapter instead of a raw OpenCode fetch delegate returning `JsonNode`.
  - OpenCode queue-dispatch write calls for session creation and prompt submission are now isolated in `src/TaskViewer.Server/Infrastructure/OpenCode/OpenCodeDispatchClient.cs`.
  - The SonarQube service boundary no longer exposes raw `JsonNode` fetches to application services for issue search and rule lookup.
  - Typed Sonar transport contracts for issue search and rule lookup now live in `src/TaskViewer.Server/Infrastructure/Orchestration/SonarTransportContracts.cs`.
  - The SonarQube client boundary now returns typed issue-search and rule-detail responses, keeping raw Sonar JSON parsing inside the infrastructure boundary.
  - Sonar issue, rule-summary, enqueue-all, and cached-rule readers now consume typed Sonar transport responses instead of traversing raw response JSON in application services.
  - Enqueue request parsing now converts inbound raw issue arrays into typed `SonarIssueTransport` items at the HTTP boundary instead of carrying raw `JsonArray` issue payloads through orchestration layers.
  - `SonarOrchestratorOptions` no longer exposes a raw `OpenCodeFetch` delegate for orchestration fallback wiring.
  - Orchestrator fallback wiring now depends on typed OpenCode collaborators (`IOpenCodeStatusReader`, `IOpenCodeDispatchClient`) with explicit disabled implementations when OpenCode access is not configured.
  - OpenCode project caching/search no longer stores raw project `JsonNode` values in viewer state; it now caches typed `OpenCodeProjectTransport` values.
  - OpenCode project search entry derivation now supports typed project transports directly, keeping raw project payload traversal localized to parsing.
- Next:
  - ADR-0004 is complete for the intended adapter-boundary scope; any further transport typing can now be handled as opportunistic cleanup rather than part of this ADR rollout.

## Acceptance Criteria

- No untyped payload shape checks in use-cases.
- Schema variance handled in one adapter parsing layer.
- Integration tests cover observed payload variants.
