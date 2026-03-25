# Two-Peer Arena

`Two-Peer Arena` is a minimal `com.crosscut.commandlink` sample that shows how to:

- register `ICommandLinkRuntimeHooks`
- start a host, client, or offline session from a launcher scene
- decode resolved move frames into a tiny deterministic board simulation
- inspect session state and checksums while the sample runs

## Scenes

- `Scenes/TwoPeerArena_Launcher.unity`: choose `Host`, `Client`, or `Offline`
- `Scenes/TwoPeerArena_Arena.unity`: shared arena scene with the board, HUD, and input

## How To Run

1. Import the sample from Package Manager.
2. Open `TwoPeerArena_Launcher.unity`.
3. Press Play and choose one of:
   - `Start Host`
   - `Start Client`
   - `Start Offline`
4. For a networked test, run a second Unity Editor instance against the same project and start `Host` in one instance and `Client` in the other.

## Controls

- `W`, `A`, `S`, `D`
- Arrow keys

Each input queues a deterministic move for the local peer token. The arena HUD shows the last submitted move, the last resolved move frame, the last applied move summary, and the current checksum.

## Expected Validation

- `Host` and `Client` should both reach `Running`.
- Both peers should see the same token positions after moves are exchanged.
- `Offline` should use the same runtime hooks and resolved-frame path through a local loopback frame.
- The CommandLink diagnostics overlay and window should remain available while the sample runs.

## Notes

- The sample loads the arena scene by imported asset path, so it works cleanly in the Unity Editor after sample import.
- If you open the arena scene directly without using the launcher, the sample falls back to `Offline` mode automatically.
