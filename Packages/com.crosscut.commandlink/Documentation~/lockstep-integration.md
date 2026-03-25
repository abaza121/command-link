# Lockstep Integration Notes

This package is already in a good place for first use, but a few targeted changes would
make it much easier to integrate with a separate lockstep simulation package.

None of the points below are blockers for initial adoption. They are the clearest
follow-up improvements if the goal is a cleaner long-term package boundary.

## 1. Move The Command Payload Model Out Of The Core Package

Today the package still owns concrete command concepts in
`Runtime/Deterministic/DeterministicCommandPayload.cs`, including:

- `Move`
- `BuildPlace`
- `Recruit`
- a static pending-intent queue
- the outbound payload builder used by `CommandLinkRunnerBridge`

Why this matters:

- a reusable lockstep networking package should not need to know the host simulation's
  gameplay command schema
- a separate simulation package will want to define its own payload format and encode
  local commands itself

Suggested direction:

- let the host provide opaque deterministic payloads
- or introduce a host-provided codec / payload builder contract
- keep the current payload helper as a sample or optional add-on instead of core runtime

`INetworkSerializer<TMessage>` already exists as a useful starting point for this kind of
generalization, but the runtime is not using it yet.

## 2. Let The Host Build The Next Local Input Frame

`ICommandLinkRuntimeHooks` already covers gate checks, resolved-frame application, and
checksums, but it does not let the host produce the next local input payload.

That gap is why:

- `CommandLinkRunnerBridge` builds outbound input from the package-owned payload helper
- the sample also builds its offline loopback frame directly

Why this matters:

- a separate simulation package should own how local intent becomes deterministic input
- the networking layer should accept the result, not author it

Suggested direction:

- add a hook such as `TryBuildLocalInput`
- optionally add a dedicated offline-loopback helper contract too

## 3. Reduce Global Static State

`CommandLinkRuntimeRegistry` currently stores the active runtime hooks, active engine,
and a `DriveFromMonoBehaviour` flag as globals. `DeterministicCommandPayload` also keeps
a global static queue of pending intents.

Why this matters:

- a separate simulation package may want cleaner lifecycle ownership
- multiple simulations, test fixtures, or worlds become harder to isolate
- static state makes integration order matter more than necessary

Suggested direction:

- keep the engine instance-based
- move runtime composition toward instance-owned bootstrap rather than global registry
- scope pending local input to a session or integration object instead of a static queue

## 4. Move Gameplay-Specific Diagnostics Out Of The Reusable Core

The diagnostics layer is correctly separated into `Diagnostics/`, but some of its
content is still shaped around build-placement behavior:

- `BuildSyncStage`
- `PendingIntentSummary`
- build trace records
- build-specific counters inside the runtime engine

Why this matters:

- transport and session diagnostics are reusable
- build-placement diagnostics are project-specific

Suggested direction:

- keep generic lockstep and session diagnostics in CommandLink
- move gameplay trace types to the host simulation package or an optional diagnostics
  extension package

## 5. Offer A More Headless Bootstrap Path

`CommandLinkNetworkEngine` already accepts `INetworkDriverFactory` and
`INetworkEndpointProvider`, which is a strong seam. The default bridge path is more
opinionated:

- `CommandLinkRunnerBridge` creates the Unity Transport driver
- it uses `StaticEndpointProvider`
- it assumes MonoBehaviour lifecycle
- it optionally loads scenes

Why this matters:

- a separate lockstep package often wants to own bootstrap, scene flow, and transport
  selection itself

Suggested direction:

- keep `CommandLinkRunnerBridge` as the quick-start path
- add a documented headless startup path for advanced hosts
- let host projects create the engine and own lifecycle directly when needed

## 6. Surface And Enforce Capacity Limits

There are several important limits in the current runtime:

- `MaxCommandsPerFrame = 2`
- `FixedList128Bytes` and `FixedList512Bytes` payload sizes
- startup warmup ticks in the engine
- a minimum input-delay clamp in the runner bridge

`CommandLinkConfig` also contains fields such as `MaxPayloadBytes` and
`DisconnectTimeoutSeconds`, but the Unity Transport driver does not fully enforce them
today.

Why this matters:

- once the simulation package owns the command schema, it also needs clear ownership of
  payload and timing limits
- hidden limits are hard to tune and hard to document for integrators

Suggested direction:

- make the important constraints explicit in package docs
- expose the limits through config where possible
- ensure transport/runtime code consistently honors the configured values

## 7. Make Peer-Loss Policy Explicit

Right now, when a peer disconnects, the runtime removes that peer from the connected set
and continues with the remaining peer count.

Why this matters:

- some lockstep games should continue after a drop
- others should pause, abort, or refuse membership changes after the match starts

Suggested direction:

- define session policy explicitly in config or hooks
- decide whether disconnect means continue, pause, or terminate
- document the default behavior so host packages know what semantics they are adopting

## Suggested Priority

If these improvements are tackled in stages, the highest-leverage order is:

1. externalize the command payload model
2. let the host build the next local input frame
3. reduce global static state
4. split generic diagnostics from gameplay diagnostics
5. add a headless bootstrap path
6. surface and enforce runtime limits
7. make peer-loss policy configurable
