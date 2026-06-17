# s&box Analytics SDK — Agent Context

This is the **game-side SDK** for s&box Analytics: a C# s&box library that game developers drop into their project to capture and ship gameplay events (sessions, deaths, level completions, custom events) to the analytics platform.

It is a **standalone git repository**. In the analytics monorepo it is mounted at `apps/sdk` via a Windows junction (and gitignored there) — when working from the monorepo, the parent `AGENTS.md` provides product context, but the TypeScript/Ultracite standards there do **not** apply here. This is an s&box C# project.

## Layout

- `analytics.sbproj` — s&box package manifest (`Type: library`, org `noot`, ident `analytics`).
- `Code/` — runtime SDK code that ships in the library. Compiled with `SANDBOX` define, root namespace `Sandbox`, nullables enabled. Files ending in `.Server.cs` compile under the `SERVER` define only.
- `Editor/` — editor-only tooling (menus, setup helpers). Never referenced by runtime code.
- `UnitTests/` — MSTest project. `TestInit` boots `Sandbox.TestAppSystem` once per assembly; tests can create real `Scene`/`GameObject` instances.
- `Assets/` — library assets.

> Current state: implemented. Public surface lives in `Code/` under namespace `Noot.Analytics`.

## Public API

Three ways to send events, all funneling into the same core:

- **`Analytics.Init(publishableKey, options)` / `Track(type, props?, scene?, position?, playerId?)` / `Flush()` / `Shutdown()`** — the standalone core. No component required.
- **`AnalyticsComponent`** — optional drop-in. Set `PublishableKey` in the inspector; it auto-`Init`s the core and emits `scene_loaded`, `player_connected`, `player_disconnected`. Owns `Shutdown` only if it did the `Init`.
- **`[Track("name")]`** on a method (emit on call; `Params = true` captures args) or property (emit `{ value }` on change). Codegen sugar over `Track`.

Default events: `session_start`/`session_end` (core), `scene_loaded` + `player_connected`/`player_disconnected` (component; connect/disconnect are host-only).

`player_id` is `AnonymousId.Hash(steamId)` — never raw SteamID.

## File map (`Code/`)

`AnalyticsEvent` (wire model) · `AnonymousId` (id hashing) · `EventBuffer` (buffer) · `IEventSender`/`HttpEventSender` (transport) · `AnalyticsOptions` (config) · `AnalyticsClient` (core) · `Analytics` (facade) · `TrackAttribute` (`[Track]`) · `AnalyticsComponent` (session/lifecycle drop-in) · `AnalyticsMovementComponent` + `MovementSampler` (per-entity position tracking, throttled, one event per sample). Aggregating spatial trackers (client-accumulate, flush one batch per window): `SpatialGrid`/`CellAccumulator`/`LineSimplifier` (pure helpers) · `AnalyticsDwellComponent` + `AnalyticsHeatmapComponent` (per-cell → `spatial_cells` batch with an open `kind` string) · `AnalyticsTrajectoryComponent` (RDP-simplified path → `trajectory` batch). Tests in `UnitTests/`; run with `dotnet test UnitTests/analytics.unittest.csproj`.

## What the SDK must do

Send batched events to the platform's Event Ingestion API:

```
POST /v1/events
Headers: X-Api-Key: {publishable_key}
Body: {
  "events": [
    {
      "type": "session_start",
      "timestamp": "2026-05-06T12:00:00Z",
      "session_id": "uuid",
      "player_id": "anon_hash",
      "properties": { "map": "de_dust2", "game_mode": "competitive" }
    }
  ]
}
```

- Canonical core event types: `session_start`, `session_end`, `player_death`, `level_complete`, `custom_event`. Arbitrary custom names are also accepted by the API.
- `player_id` is an anonymous hash — never send PII.
- The machine-checked contract lives in the monorepo at `packages/events/src/contract.ts`; the API surface is documented in `docs/agents/features.md`. When the wire format is in question, those are the source of truth — keep this SDK in lockstep with them, not the other way around.

## Working rules

- **Think before coding** — state assumptions; if multiple interpretations exist, present them.
- **Simplicity first** — minimum code that solves the problem; no speculative configurability.
- **Surgical changes** — touch only what the task requires; match existing style.
- **Verify** — for behavior changes, add or extend a test in `UnitTests/` and make it pass.

## s&box specifics

- Only s&box-whitelisted APIs are available at runtime; arbitrary .NET (raw sockets, filesystem) is sandboxed. Use s&box's `Http` facilities for network calls.
- Components extend `Sandbox.Component`; prefer `[Title]`/`[Property]` attributes for editor exposure.
- Game developers are the consumers: public API surface should be one-component-drop-in simple.
