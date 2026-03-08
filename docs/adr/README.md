# Architecture Decision Records

This folder tracks architecture simplification decisions for `opencode-task-viewer`.

## ADR Index

- [ADR-0001](ADR-0001-no-object-no-jsonobject-and-primitives.md) - Ban `object`/`JsonObject` in production code and replace primitive obsession with value objects.
- [ADR-0002](ADR-0002-thin-program-composition-root.md) - Keep `Program.cs` as composition + routing only.
- [ADR-0003](ADR-0003-stable-typed-api-contracts.md) - Replace anonymous response construction with explicit DTO contracts.
- [ADR-0004](ADR-0004-external-api-adapters-with-schema-tolerant-typed-deserialization.md) - Typed OpenCode/Sonar adapters with schema-tolerant parsing.
- [ADR-0005](ADR-0005-cache-unification-and-invalidation-policy.md) - Unify cache ownership and invalidation semantics.
- [ADR-0006](ADR-0006-separated-external-client-projects-and-httpclient-factory.md) - Split OpenCode and SonarQube clients into dedicated projects with HttpClientFactory-based registration and simplified service boundaries.
- [ADR-0007](ADR-0007-dapper-for-orchestration-persistence.md) - Replace manual orchestration SQLite ADO.NET plumbing with Dapper while keeping SQL explicit.

## Current Status

- In Progress: ADR-0001
- In Progress: ADR-0005
- Completed: ADR-0002, ADR-0003
- Completed: ADR-0004
- Completed: ADR-0007
- In Progress: ADR-0006
- Latest ADR-0001 progress: production source regression tests now enforce the `object` / `JsonObject` ban.
- Latest ADR-0002 progress: `Program.cs` is now reduced to host setup, DI registration, route registration, static hosting, and lifecycle wiring.
- Latest ADR-0004 progress: queue dispatch now uses typed OpenCode adapter interfaces for session creation and prompt submission instead of passing raw fetch delegates through application services.
- Latest ADR-0004 progress: Sonar issue search and rule lookup now flow through typed gateway responses instead of raw `JsonNode` payloads crossing the application boundary.
- Latest ADR-0004 progress: OpenCode project cache/search now uses typed project transports, and ADR-0004 is complete.
- Latest ADR-0005 progress: cache ownership now flows through `OpenCodeViewerCacheCoordinator`, and TTL policy now lives beside the coordinator instead of being threaded through every caller.
- Latest ADR-0006 focus: split OpenCode and SonarQube HTTP integrations into dedicated projects, adopt singleton-safe `IHttpClientFactory` registration patterns, remove redundant pass-through client wrappers, and lock direct DI boundaries with client-project and server-composition tests.
- Latest ADR-0007 progress: orchestration SQLite persistence now uses Dapper end-to-end, with focused queue repository tests and full solution verification, and ADR-0007 is complete.
- Latest build/runtime baseline: the solution now targets `.NET 9`, pins SDK `9.0.311` via `global.json`, centralizes package versions in `Directory.Packages.props`, and centralizes shared MSBuild properties in `Directory.Build.props`.

## Roadmap Order

1. ADR-0001 (foundational)
2. ADR-0004 (typed integration layer)
3. ADR-0003 (contract cleanup)
4. ADR-0002 (extract Program orchestration)
5. ADR-0005 (cache consolidation)
6. ADR-0006 (external client project split)
7. ADR-0007 (orchestration persistence simplification)
