# Getting Started

`com.crosscut.commandlink` is a session and transport package for deterministic lockstep
games. It owns peer lifecycle, delayed input exchange, ack/resend behavior, checksums,
and the package-owned network world. It does not own your simulation loop, simulation
world, command application, or checksum logic.

## Before You Integrate

You should already have:

- A deterministic simulation that can advance in fixed ticks
- A place to store gate checks plus pre-tick and post-tick callbacks
- A way to apply one resolved input frame to your simulation
- A checksum function for periodic validation

This package currently targets Unity `2022.3` and depends on:

- `com.unity.entities` `1.4.2`
- `com.unity.collections` `2.6.2`
- `com.unity.mathematics` `1.3.3`
- `com.unity.transport` `2.4.0`

## Installation

This package is ready for initial local use, but the distribution channel is not yet
formalized. The practical first options are:

- keep the package as an embedded package under `Packages/com.crosscut.commandlink`
- reference it from another Unity project as a local package path

If you want a working reference scene first, import the `Two-Peer Arena` sample from
Package Manager after the package is available in the host project.

## 1. Implement `ICommandLinkRuntimeHooks`

The host project integrates through `ICommandLinkRuntimeHooks`. Your implementation
should store the gate checks and tick callbacks that CommandLink registers, then expose
host-side helpers that your simulation runner can call each tick.

```csharp
using System;
using System.Collections.Generic;
using CrossCut.CommandLink;

public sealed class YourRuntimeHooks : ICommandLinkRuntimeHooks
{
    private readonly List<Func<uint, bool>> gateChecks = new();
    private readonly List<Action<uint>> preTickCallbacks = new();
    private readonly List<Action<uint>> postTickCallbacks = new();

    public bool SupportsTickCallbacks => true;
    public bool IsSimulationReady { get; private set; }

    public void SetSimulationReady(bool ready) => IsSimulationReady = ready;

    public void SetGateCheck(Func<uint, bool> gateCheck)
    {
        if (gateCheck != null && !gateChecks.Contains(gateCheck))
        {
            gateChecks.Add(gateCheck);
        }
    }

    public void ClearGateCheck(Func<uint, bool> gateCheck) => gateChecks.Remove(gateCheck);
    public void AddPreTick(Action<uint> callback) { if (callback != null && !preTickCallbacks.Contains(callback)) preTickCallbacks.Add(callback); }
    public void RemovePreTick(Action<uint> callback) => preTickCallbacks.Remove(callback);
    public void AddPostTick(Action<uint> callback) { if (callback != null && !postTickCallbacks.Contains(callback)) postTickCallbacks.Add(callback); }
    public void RemovePostTick(Action<uint> callback) => postTickCallbacks.Remove(callback);

    public bool CanAdvanceTick(uint tick)
    {
        for (int i = 0; i < gateChecks.Count; i++)
        {
            if (!gateChecks[i].Invoke(tick))
            {
                return false;
            }
        }

        return true;
    }

    public void InvokePreTick(uint tick)
    {
        for (int i = 0; i < preTickCallbacks.Count; i++)
        {
            preTickCallbacks[i].Invoke(tick);
        }
    }

    public void InvokePostTick(uint tick)
    {
        for (int i = 0; i < postTickCallbacks.Count; i++)
        {
            postTickCallbacks[i].Invoke(tick);
        }
    }

    public bool TryApplyResolvedFrame(uint tick, in ResolvedInputFrame resolvedFrame)
    {
        // Decode the resolved frame and stage it into your simulation inbox here.
        return true;
    }

    public bool TryComputeSimulationChecksum(out uint checksum)
    {
        checksum = 0;
        // Replace with your deterministic checksum.
        return true;
    }
}
```

## 2. Register The Hooks Early

Register your runtime hooks before starting a session:

```csharp
using CrossCut.CommandLink;

public static class YourGameBootstrap
{
    public static YourRuntimeHooks Hooks { get; } = new YourRuntimeHooks();

    [UnityEngine.RuntimeInitializeOnLoadMethod]
    private static void Install()
    {
        CommandLinkRuntimeRegistry.RuntimeHooks = Hooks;
    }
}
```

`NetworkBootstrapper` waits for `RuntimeHooks.IsSimulationReady` before creating the
package-owned network world, so keep that flag aligned with the real simulation state.

## 3. Add A `CommandLinkRunnerBridge`

`CommandLinkRunnerBridge` is the easiest first entry point. Add it to a bootstrap
GameObject, then configure the session before starting host or client mode.

```csharp
using CrossCut.CommandLink;
using UnityEngine;

public sealed class YourNetworkLauncher : MonoBehaviour
{
    [SerializeField] private CommandLinkRunnerBridge runnerBridge;

    private void Awake()
    {
        runnerBridge.ConfigureAutoLifecycle(
            shouldAutoInitializeOnStart: false,
            shouldAutoJoinOnStart: false,
            shouldAutoSignalReady: true,
            shouldAutoLoadGameplaySceneOnSessionStart: false);

        runnerBridge.ConfigureExpectedPlayers(2);
        runnerBridge.ConfigureEndpoints("127.0.0.1", 7777, "127.0.0.1", 7777);
    }

    public void StartHost()
    {
        runnerBridge.ConfigureSessionMode(CommandLinkSessionMode.Networked);
        runnerBridge.StartHost();
    }

    public void StartClient()
    {
        runnerBridge.ConfigureSessionMode(CommandLinkSessionMode.Networked);
        runnerBridge.ConnectToHost("127.0.0.1", 7777);
    }

    public void StartOffline()
    {
        runnerBridge.ConfigureSessionMode(CommandLinkSessionMode.LocalOffline);
        runnerBridge.StartHost();
    }
}
```

For networked mode, the registered runtime hooks must support tick callbacks. Offline
mode still uses the same hook boundary, but runs without a network engine.

## 4. Drive The Simulation Tick Loop

CommandLink does not advance your simulation for you. Your host runner must:

1. Check whether the current tick is allowed to advance
2. Invoke the registered pre-tick callbacks
3. Advance the simulation
4. Invoke the registered post-tick callbacks

That is the key handoff point where CommandLink stages resolved inputs before the tick
and broadcasts checksums after the tick.

The included `Two-Peer Arena` sample demonstrates this pattern end-to-end.

## 5. Queue Local Commands

The package currently ships with a default deterministic payload helper in
`Runtime/Deterministic/DeterministicCommandPayload.cs`. The sample uses it like this:

```csharp
var orderedTargetIds = new Unity.Collections.FixedList64Bytes<uint>();
orderedTargetIds.Add(simNetId);
DeterministicCommandPayload.EnqueueMove(targetX, targetY, false, orderedTargetIds);
```

At runtime, `CommandLinkRunnerBridge` builds the next outbound input frame from the
current pending payload, and `CommandLinkNetworkEngine` handles exchange, resend,
resolution, and checksum flow.

## 6. Validate The First Integration

Recommended first-pass checks:

- Run host and client in two Unity Editor instances against the same port
- Confirm both peers reach `Running`
- Confirm both peers resolve the same inputs and produce matching checksums
- Test offline mode too, because it uses the same runtime hooks and resolved-frame path

The sample README in `Samples~/TwoPeerArena/README.md` gives a lightweight validation
checklist you can mirror in your own host project.

## Reference Files

If you want concrete examples in this package, start with:

- `Runtime/Runtime/ICommandLinkRuntimeHooks.cs`
- `Runtime/Runtime/CommandLinkRunnerBridge.cs`
- `Runtime/Runtime/CommandLinkNetworkEngine.cs`
- `Runtime/Bootstrap/NetworkBootstrapper.cs`
- `Samples~/TwoPeerArena/Runtime/TwoPeerArenaRuntimeHooks.cs`
- `Samples~/TwoPeerArena/Runtime/TwoPeerArenaSessionController.cs`
