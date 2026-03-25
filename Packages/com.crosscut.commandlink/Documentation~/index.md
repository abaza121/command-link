# CrossCut CommandLink

`com.crosscut.commandlink` is a reusable deterministic lockstep networking package for
Unity projects that already own their simulation loop and game-specific command model.

## Start Here

- [Getting Started](getting-started.md): install the package, wire `ICommandLinkRuntimeHooks`,
  register the runtime, start host/client/offline sessions, and validate the first run.
- [Overview](overview.md): project philosophy, package boundaries, runtime flow, and
  architecture.
- [Lockstep Integration Notes](lockstep-integration.md): concrete improvements that would
  make CommandLink easier to pair with a separate lockstep simulation package.
- [Testing](testing.md): how to expose and run the package tests from a host project.

## What The Package Owns

- Peer session flow for host/client startup, ready state, and disconnect handling
- Delayed input submission, buffering, and deterministic frame exchange
- Ack and resend bookkeeping for input frames
- Checksum message flow
- Transport abstractions with a Unity Transport implementation
- ECS bootstrap for the package-owned networking world

## What The Host Project Owns

- Simulation world lifetime
- Command authoring and application logic
- Checksum calculation
- Tick lifecycle hooks
- Any project-specific UI or matchmaking flow

The host project integrates through `ICommandLinkRuntimeHooks` and registers its
implementation in `CommandLinkRuntimeRegistry.RuntimeHooks`.

## Package Layout

- `Runtime/`: core networking runtime, contracts, bootstrap, serialization, and transport
- `Diagnostics/Runtime/`: diagnostic snapshot models and runtime services
- `Diagnostics/Editor/`: editor window for inspecting CommandLink state
- `Samples~/`: sample integration and reference scenes
- `Tests/`: smoke tests for runtime and editor integration

## Current Status

This package is in an early packaged state. The core runtime and diagnostics code are
embedded and versioned, and a minimal `Two-Peer Arena` sample is included under
`Samples~/`.
