# CommandLink Build Sync + Ack Investigation (2026-03-23)

## Scope
- Investigate why building-related synchronization appeared to stall in multiplayer sessions.
- Review `commandlink-diagnostics-2.json` and `commandlink-diagnostics3.json`.
- Summarize the CommandLink runtime changes made during this debugging pass.

## Initial Finding
The first diagnostic snapshot pointed at a sender-side head-of-line stall:

- `QueuedLocalCount` kept growing.
- `PendingAckPeerCount` stayed non-zero.
- `InferredStallReason` reported `waiting_for_ack`.
- Newer build-bearing frames were stuck behind one older local frame that never cleared its in-flight ack gate.

At that point, the likely root cause was not the building spawn path itself, but the transport/ack lane inside `CommandLinkNetworkEngine`.

## Code Changes Made

### 1. Removed the single-frame local ack gate
`Assets/CommandLink/Runtime/Runtime/CommandLinkNetworkEngine.cs`

Previously the engine allowed only one local frame to be "in flight" at a time:

- one frame was popped from `_pendingLocalInputFramesByTick`
- that frame blocked all later frames until every remote peer acknowledged it

This created a lockstep bottleneck where one missed or mismatched ack could freeze all later build commands.

The runtime now:

- drains queued local input frames in ascending tick order
- tracks pending acknowledgments per target tick with `PendingAckState`
- continues sending later frames even if an older frame is still waiting on an ack
- keeps resend behavior active for unacknowledged older frames

### 2. Corrected targeted ack routing
`Assets/CommandLink/Runtime/Runtime/CommandLinkNetworkEngine.cs`

Ack replies now use the transport packet source peer (`packet.PeerId`) instead of the payload-authored peer id when sending a targeted acknowledgment back.

This makes the reply path match the actual transport source and reduces the chance of an ack being sent to the wrong peer mapping.

### 3. Expired stale pending-ack entries
`Assets/CommandLink/Runtime/Runtime/CommandLinkNetworkEngine.cs`

After the per-frame ack refactor, older ack-tracking entries could still linger in diagnostics after they were already outside the resend window.

The runtime now expires those stale pending-ack entries when the resend backlog ages out. This prevents diagnostics from permanently reporting an ancient "in-flight" frame as the active blocker.

## What The New Diagnostics Show
`Assets/commandlink-diagnostics3.json`

The latest snapshot changed the picture:

- `QueuedLocalCount = 0`
- `PendingTicksSummary = none`
- `ResendBacklogCount = 5`
- `HasInFlightFrame = true`
- `InFlightTick = 576`
- `CurrentTick = 722`
- `CurrentInputsPresent = 1`
- `MissingInputsAtCurrentTick = 1`

This suggests the original sender queue bottleneck is no longer the main problem. The more likely live blocker is:

- the current simulation tick is missing the remote peer's input frame

In other words, the session now appears to be stalling on missing remote input rather than on local queued build frames being trapped behind one in-flight frame.

## Current Best Hypothesis
The active problem is now one of these:

1. The remote peer is no longer producing current input frames.
2. The remote peer is sending them, but the host is not receiving them.
3. The session is partially connected, but peer state has drifted enough that lockstep input delivery is no longer progressing even though the peer still appears connected.

## Important Note
The building simulation spawn path itself was not the main issue in the latest snapshots. The earlier March simulation-to-presentation entity-link bug was already addressed in the current codebase, and the present evidence points much more strongly at CommandLink session/input delivery behavior.

## Validation Status
- `dotnet build "Assembly-CSharp.csproj" -nologo` succeeded after the runtime changes.
- Existing warning remains unrelated:
  - `Assets/Scripts/Views/SelectionBoxUI.cs`: unused `borderWidth` field.

## Recommended Next Step
Capture diagnostics from both peers in the same multiplayer run after the latest runtime changes.

What we want to compare:

- current tick on host vs client
- `CurrentInputsPresent`
- `MissingInputsAtCurrentTick`
- resend backlog counts
- whether one side stops advancing local frame production

That two-sided comparison should identify whether the fault is on frame production, transport delivery, or peer/session state progression.
