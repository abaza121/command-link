# Overview

`com.crosscut.commandlink` exists to keep deterministic multiplayer session logic
reusable without forcing the networking package to own the host simulation.

## Philosophy

The project is built around a few simple rules:

- CommandLink owns session correctness, transport flow, and resolved-frame delivery
- The host project owns simulation state, tick advancement, command application, and
  checksums
- Determinism is more important than convenience
- Integration happens through explicit seams instead of hidden package-side behavior
- Diagnostics are observers, not authorities

This makes CommandLink a good fit when you already have a deterministic simulation and
want a reusable lockstep session layer around it.

## Package Boundaries

CommandLink owns:

- Join, ready, start, and disconnect flow
- Delayed input submission and buffering
- Ack/resend handling for deterministic input frames
- Checksum message exchange
- Transport abstraction plus the Unity Transport adapter
- A package-owned ECS world for network polling and input submission

The host project owns:

- Simulation world lifetime
- Tick lifecycle and simulation advancement
- Command authoring and resolved-frame decoding
- Simulation checksum calculation
- Game-specific UI, matchmaking, and scene flow

## Architecture

The package is split into a few clear layers:

```text
Host Simulation
  |
  |  ICommandLinkRuntimeHooks
  v
CommandLinkRunnerBridge / NetworkBootstrapper
  |
  v
CommandLinkNetworkEngine
  |
  +-- message serialization
  +-- session state
  +-- input buffering / ack / resend
  +-- checksum flow
  |
  v
INetworkDriver
  |
  v
UnityTransportNetworkDriver

Diagnostics observes the runtime from the side and does not drive lockstep behavior.
```

### Host Integration Boundary

`ICommandLinkRuntimeHooks` is the main contract between CommandLink and the host
simulation. Through that interface, the host reports:

- whether tick callbacks are supported
- whether the simulation is ready
- how CommandLink should register gate checks
- how CommandLink should register pre-tick and post-tick callbacks
- how one resolved frame is applied to the simulation
- how a deterministic checksum is computed

This boundary is the main reason the package stays decoupled from the host game.

### Orchestration Layer

`CommandLinkRunnerBridge` is the main MonoBehaviour-facing entry point. It chooses
networked versus offline mode, builds the session configuration, creates the network
engine, and drives polling plus local input submission each frame.

`NetworkBootstrapper` creates the package-owned ECS world only after the host reports
that the simulation is ready. That keeps the networking world separate from the host
simulation world.

### Core Session Engine

`CommandLinkNetworkEngine` is the core of the package. It owns:

- peer lifecycle and host/client startup
- join, ready, and session-start messages
- buffering local inputs for future ticks
- gating ticks until all required peer inputs are present
- ack tracking and resend behavior
- resolved-frame construction
- checksum broadcast and mismatch reporting

This is where most of the lockstep-specific behavior lives.

### Transport Layer

The transport is hidden behind `INetworkDriver` and `INetworkDriverFactory`. The current
package includes `UnityTransportNetworkDriver`, which maps peer ids to Unity Transport
connections and forwards packets to the engine.

That abstraction is intentional. It keeps the session engine reusable if Relay or another
transport adapter is added later.

### Diagnostics Sidecar

Diagnostics live alongside the runtime instead of inside the authoritative simulation
path. Runtime overlays, editor tooling, and rolling frame snapshots are all observers of
session state rather than drivers of state.

That separation is important for deterministic packages because debugging tools should
not change lockstep behavior.

## Runtime Flow

The typical flow is:

1. The host registers `CommandLinkRuntimeRegistry.RuntimeHooks`.
2. The host marks the simulation ready.
3. `CommandLinkRunnerBridge` starts host, client, or offline mode.
4. `CommandLinkNetworkEngine` begins join/ready/start orchestration.
5. The host simulation tries to advance a tick.
6. CommandLink gate checks hold that tick until every required peer input is present.
7. On pre-tick, the resolved input frame is applied to the simulation.
8. The host simulation advances deterministically.
9. On post-tick, CommandLink optionally broadcasts the checksum.

Offline mode keeps the same broad orchestration model, but synthesizes a one-peer local
session instead of using a transport-backed engine.

## Strengths In The Current Design

- The package boundary is explicit and readable
- The session engine is mostly decoupled from the simulation
- The transport seam is already abstracted
- Offline and networked modes share the same conceptual flow
- Diagnostics are intentionally non-authoritative

## Current Architectural Tensions

The package is close to being broadly reusable, but a few project-shaped decisions are
still visible:

- `DeterministicCommandPayload` still defines concrete `Move`, `BuildPlace`, and
  `Recruit` command formats inside the package
- parts of diagnostics still track build-placement workflows directly
- `CommandLinkRuntimeRegistry` and the deterministic pending-intent queue are both
  global static seams

Those choices are workable for initial use, but they are also the main areas to revisit
if CommandLink needs to pair cleanly with a separate lockstep simulation package.
