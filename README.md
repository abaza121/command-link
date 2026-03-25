# CrossCut CommandLink

## Purpose
`CrossCut.CommandLink` is a deterministic session and transport package for lockstep-style multiplayer games.

It owns:
- Peer session flow: join, ready, start, disconnect
- Delayed input submission and buffering
- Ack/resend handling for deterministic input frames
- Checksum message exchange
- A transport abstraction with a Unity Transport implementation
- ECS-side network world bootstrap for polling/submission systems

It does not own your simulation runner, simulation world, inbox application logic, or checksum implementation. Those are provided by the host project through `ICommandLinkRuntimeHooks`.

## Proper Use Cases
Use CommandLink when:
- You already have a deterministic simulation and want a reusable multiplayer/session layer around it
- You want to package networking separately from your game-specific simulation code
- You need lockstep-style input exchange instead of state replication
- You want to keep transport details behind an interface so Relay or another backend can be added later

CommandLink is a poor fit when:
- Your game is authoritative-server or snapshot/interpolation based
- You need rollback netcode instead of delayed lockstep input scheduling
- Your package is supposed to own the simulation world and simulation data model end-to-end

## Integration Model
The host project must register `CommandLinkRuntimeRegistry.RuntimeHooks` with an implementation of `ICommandLinkRuntimeHooks`.

That hook implementation is responsible for:
- Reporting when the simulation is ready via `IsSimulationReady`
- Providing tick lifecycle callbacks via `AddPreTick` and `AddPostTick`
- Installing/removing the simulation gate check via `SetGateCheck` and `ClearGateCheck`
- Applying resolved frames into the simulation via `TryApplyResolvedFrame`
- Computing a simulation checksum via `TryComputeSimulationChecksum`

Networked mode requires `SupportsTickCallbacks == true`. If no hooks are registered, `CommandLinkRunnerBridge` will refuse to start a networked session.

## Runtime Namespace
All runtime code under this package uses:
- `CrossCut.CommandLink`

## Main Runtime Types
- `CommandLinkRunnerBridge`: MonoBehaviour entry point for host/client startup
- `CommandLinkNetworkEngine`: session orchestration, input exchange, ack/resend, checksums
- `ICommandLinkRuntimeHooks`: host-side integration contract for simulation/tick wiring
- `INetworkDriver` / `INetworkDriverFactory`: transport abstraction
- `UnityTransportNetworkDriver`: default Unity Transport-backed driver
- `NetworkBootstrapper`: creates the CommandLink network world once the host simulation reports ready

## Required Unity Packages
This package targets Unity `2022.3` and depends on:
- `com.unity.entities` `1.4.2`
- `com.unity.collections` `2.6.2`
- `com.unity.mathematics` `1.3.3`
- `com.unity.transport` `2.4.0`

## Install From GitHub
You can consume the package directly from this repository through Unity Package Manager.

Use `Add package from git URL...` with:

```text
https://github.com/abaza121/command-link.git?path=/Packages/com.crosscut.commandlink
```

Or add it to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.crosscut.commandlink": "https://github.com/abaza121/command-link.git?path=/Packages/com.crosscut.commandlink"
  }
}
```

That installs the package from the `Packages/com.crosscut.commandlink` folder in this repo while keeping the rest of the repository out of your Unity package manifest.

## Quick Start
1. Add the package to your project.
2. Implement `ICommandLinkRuntimeHooks` in the host project that owns your simulation runner/world.
3. Register it early:

```csharp
CommandLinkRuntimeRegistry.RuntimeHooks = new YourCommandLinkRuntimeHooks();
```

4. Add `CommandLinkRunnerBridge` to a bootstrap GameObject.
5. Configure host/client role and endpoints.
6. For networked play, ensure your hooks provide tick callbacks and resolved-frame application.
7. For local offline mode, ensure your hooks can still accept gate configuration and resolved-frame application if you want the same simulation path.

## Sample
- Import `Two-Peer Arena` from Package Manager to get a minimal launcher + arena example.
- The sample demonstrates host, client, and offline startup, a tiny deterministic move-only board, and the existing diagnostics overlay/window.

## Documentation
Package documentation lives under `Packages/com.crosscut.commandlink/Documentation~`.

Start with:
- `Packages/com.crosscut.commandlink/Documentation~/index.md`
- `Packages/com.crosscut.commandlink/Documentation~/getting-started.md`
- `Packages/com.crosscut.commandlink/Documentation~/overview.md`
- `Packages/com.crosscut.commandlink/Documentation~/lockstep-integration.md`
- `Packages/com.crosscut.commandlink/Documentation~/testing.md`

If you are browsing the package inside Unity, `Documentation~` is the standard package-docs folder for supplementary guides and reference material.

## Notes
- `NetworkBootstrapper` waits for `RuntimeHooks.IsSimulationReady` before creating the network world.
- `SimulationCommandInboxBridge` no longer writes directly into game-specific ECS inbox types; that is now the host project's responsibility through `ICommandLinkRuntimeHooks`.
- If `TryComputeSimulationChecksum` returns `false`, checksum broadcast is skipped for that tick.
- The package no longer depends directly on `LockstepFoundations.Runtime`.
