using System;
using System.IO;
using Unity.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CrossCut.CommandLink.Samples.TwoPeerArena
{
    public enum TwoPeerArenaLaunchMode : byte
    {
        None = 0,
        Host = 1,
        Client = 2,
        Offline = 3,
    }

    [DisallowMultipleComponent]
    public sealed class TwoPeerArenaSessionController : MonoBehaviour
    {
        private const string LauncherSceneFileName = "TwoPeerArena_Launcher.unity";
        private const string ArenaSceneFileName = "TwoPeerArena_Arena.unity";
        private const int MaxTicksPerFrame = 6;
        private const int MaxBufferedTicks = MaxTicksPerFrame * 4;
        private const ushort DefaultPort = 7777;

        public static TwoPeerArenaSessionController Instance { get; private set; }

        private TwoPeerArenaRuntimeHooks _runtimeHooks;
        private CommandLinkRunnerBridge _runnerBridge;
        private float _tickAccumulator;
        private uint _simulationTick;

        public TwoPeerArenaRuntimeHooks RuntimeHooks => _runtimeHooks;
        public TwoPeerArenaSimulation Simulation => _runtimeHooks.Simulation;
        public TwoPeerArenaLaunchMode LaunchMode { get; private set; }
        public string LauncherScenePath { get; private set; } = string.Empty;
        public string ArenaScenePath { get; private set; } = string.Empty;
        public string StatusMessage { get; private set; } = "Open the launcher scene and start a host, client, or offline run.";
        public float BufferedTickBacklogSeconds => _tickAccumulator;
        public float BufferedTickIntervalSeconds => ResolveTickInterval(SessionState.SessionConfig.TickRate);
        public int BufferedTickCountEstimate => EstimateBufferedTickCount(_tickAccumulator, BufferedTickIntervalSeconds);
        public bool IsBufferedTickBacklogSaturated => IsBufferedTickBacklogAtCap(_tickAccumulator, BufferedTickIntervalSeconds);
        public string TickResilienceSummary => BuildTickResilienceSummary(SessionState.SessionState, _tickAccumulator, BufferedTickIntervalSeconds);

        public bool HasActiveSession => _runnerBridge != null && _runnerBridge.HasActiveSession;
        public CommandLinkSessionState SessionState => _runnerBridge != null ? _runnerBridge.SessionState : default;

        public static TwoPeerArenaSessionController EnsureInstance()
        {
            if (Instance != null)
            {
                return Instance;
            }

            var root = new GameObject(nameof(TwoPeerArenaSessionController));
            return root.AddComponent<TwoPeerArenaSessionController>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            _runtimeHooks = new TwoPeerArenaRuntimeHooks();
            _runtimeHooks.ResetSimulation();
            CommandLinkRuntimeRegistry.RuntimeHooks = _runtimeHooks;

            _runnerBridge = gameObject.GetComponent<CommandLinkRunnerBridge>();
            if (_runnerBridge == null)
            {
                _runnerBridge = gameObject.AddComponent<CommandLinkRunnerBridge>();
            }

            _runnerBridge.ConfigureAutoLifecycle(false, false, true, false);
        }

        private void Update()
        {
            if (CommandLinkRuntimeRegistry.RuntimeHooks != _runtimeHooks)
            {
                CommandLinkRuntimeRegistry.RuntimeHooks = _runtimeHooks;
            }

            bool isArenaSceneActive = IsArenaSceneActive();
            _runtimeHooks.SetSimulationReady(isArenaSceneActive);
            if (!isArenaSceneActive || !HasActiveSession)
            {
                return;
            }

            var sessionState = SessionState;
            if (!ShouldDriveSimulationTicks(sessionState.SessionState))
            {
                _tickAccumulator = 0f;
                return;
            }

            float tickRate = sessionState.SessionConfig.TickRate > 0 ? sessionState.SessionConfig.TickRate : 20f;
            float tickInterval = 1f / tickRate;
            _tickAccumulator = ClampBufferedTickAccumulator(_tickAccumulator + Time.unscaledDeltaTime, tickInterval);

            int ticksProcessed = 0;
            while (_tickAccumulator >= tickInterval && ticksProcessed < MaxTicksPerFrame)
            {
                if (!_runtimeHooks.CanAdvanceTick(_simulationTick))
                {
                    _tickAccumulator = ClampBufferedTickAccumulator(_tickAccumulator, tickInterval);
                    break;
                }

                if (CommandLinkRuntimeRegistry.Engine == null)
                {
                    ApplyOfflineLoopbackFrame(_simulationTick);
                }

                _runtimeHooks.InvokePreTick(_simulationTick);
                Simulation.AdvanceTick(_simulationTick);
                _runtimeHooks.InvokePostTick(_simulationTick);

                _simulationTick++;
                _tickAccumulator = Mathf.Max(0f, _tickAccumulator - tickInterval);
                ticksProcessed++;
            }
        }

        /// <summary>
        /// Returns whether the sample should advance simulation time for the supplied session state.
        /// </summary>
        public static bool ShouldDriveSimulationTicks(LockstepSessionState sessionState)
        {
            return sessionState == LockstepSessionState.Running;
        }

        /// <summary>
        /// Preserves bounded catch-up backlog so temporary stalls do not silently discard multiple ticks.
        /// </summary>
        public static float ClampBufferedTickAccumulator(float accumulator, float tickInterval)
        {
            if (tickInterval <= 0f)
            {
                return 0f;
            }

            return Mathf.Clamp(accumulator, 0f, tickInterval * MaxBufferedTicks);
        }

        /// <summary>
        /// Estimates the current buffered backlog in whole-tick units for HUD and diagnostics display.
        /// </summary>
        public static int EstimateBufferedTickCount(float accumulator, float tickInterval)
        {
            if (tickInterval <= 0f || accumulator <= 0f)
            {
                return 0;
            }

            return Mathf.FloorToInt(accumulator / tickInterval);
        }

        /// <summary>
        /// Returns whether the buffered backlog is currently pinned at the sample's catch-up cap.
        /// </summary>
        public static bool IsBufferedTickBacklogAtCap(float accumulator, float tickInterval)
        {
            if (tickInterval <= 0f)
            {
                return false;
            }

            return accumulator >= (tickInterval * MaxBufferedTicks) - 0.0001f;
        }

        /// <summary>
        /// Produces a compact human-readable summary of the sample's current tick buffering behavior.
        /// </summary>
        public static string BuildTickResilienceSummary(LockstepSessionState sessionState, float accumulator, float tickInterval)
        {
            if (!ShouldDriveSimulationTicks(sessionState))
            {
                return "Tick Drive: idle until session reaches Running";
            }

            int bufferedTicks = EstimateBufferedTickCount(accumulator, tickInterval);
            if (IsBufferedTickBacklogAtCap(accumulator, tickInterval))
            {
                return $"Tick Drive: catch-up backlog capped at {bufferedTicks} ticks";
            }

            if (bufferedTicks > 0)
            {
                return $"Tick Drive: catching up {bufferedTicks} buffered tick(s)";
            }

            return "Tick Drive: steady";
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void RegisterLauncherScene(string scenePath)
        {
            LauncherScenePath = scenePath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(ArenaScenePath))
            {
                ArenaScenePath = BuildSiblingScenePath(scenePath, ArenaSceneFileName);
            }
        }

        public void RegisterArenaScene(string scenePath)
        {
            ArenaScenePath = scenePath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(LauncherScenePath))
            {
                LauncherScenePath = BuildSiblingScenePath(scenePath, LauncherSceneFileName);
            }
        }

        public bool StartHost(string address, ushort port, string arenaScenePath)
        {
            return StartSession(TwoPeerArenaLaunchMode.Host, CommandLinkSessionMode.Networked, address, port, arenaScenePath);
        }

        public bool StartClient(string address, ushort port, string arenaScenePath)
        {
            return StartSession(TwoPeerArenaLaunchMode.Client, CommandLinkSessionMode.Networked, address, port, arenaScenePath);
        }

        public bool StartOffline(string arenaScenePath)
        {
            return StartSession(TwoPeerArenaLaunchMode.Offline, CommandLinkSessionMode.LocalOffline, "127.0.0.1", DefaultPort, arenaScenePath);
        }

        public bool StartOfflineIfIdle(string arenaScenePath)
        {
            if (HasActiveSession)
            {
                return true;
            }

            StatusMessage = "No active launcher session was found, so the sample started in offline mode.";
            return StartOffline(arenaScenePath);
        }

        public bool TryQueueLocalMove(int deltaX, int deltaY)
        {
            var sessionState = SessionState;
            if (sessionState.SessionState != LockstepSessionState.Running)
            {
                StatusMessage = "Waiting for the session to reach Running before accepting move input.";
                return false;
            }

            byte localPeerId = sessionState.LocalPeerId;
            if (!Simulation.TryGetTokenState(localPeerId, out var token))
            {
                StatusMessage = $"No token is registered for local peer {localPeerId}.";
                return false;
            }

            int targetX = Mathf.Clamp(token.CellX + deltaX, 0, TwoPeerArenaSimulation.BoardWidth - 1);
            int targetY = Mathf.Clamp(token.CellY + deltaY, 0, TwoPeerArenaSimulation.BoardHeight - 1);
            if (targetX == token.CellX && targetY == token.CellY)
            {
                return false;
            }

            var orderedTargetIds = new FixedList64Bytes<uint>();
            orderedTargetIds.Add(token.SimNetId);
            DeterministicCommandPayload.EnqueueMove(targetX, targetY, false, orderedTargetIds);

            Simulation.RecordSubmittedMove(localPeerId, targetX, targetY);
            StatusMessage = $"Queued move for peer {localPeerId} -> ({targetX},{targetY}).";
            return true;
        }

        public bool IsPeerConnected(byte peerId)
        {
            var sessionState = SessionState;
            for (int i = 0; i < sessionState.ConnectedPeerIds.Length; i++)
            {
                if (sessionState.ConnectedPeerIds[i] == peerId)
                {
                    return true;
                }
            }

            return LaunchMode == TwoPeerArenaLaunchMode.Offline && peerId == 0;
        }

        private bool StartSession(TwoPeerArenaLaunchMode launchMode, CommandLinkSessionMode sessionMode, string address, ushort port, string arenaScenePath)
        {
            if (HasActiveSession)
            {
                StatusMessage = "A session is already active. Restart play mode to switch roles.";
                return false;
            }

            string sanitizedAddress = string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address.Trim();
            ushort sanitizedPort = port == 0 ? DefaultPort : port;

            RegisterArenaScene(arenaScenePath);
            _runnerBridge.ConfigureSessionMode(sessionMode);
            _runnerBridge.ConfigureExpectedPlayers(sessionMode == CommandLinkSessionMode.LocalOffline ? (ushort)1 : (ushort)2);
            _runnerBridge.ConfigureEndpoints(sanitizedAddress, sanitizedPort, sanitizedAddress, sanitizedPort);

            _runtimeHooks.ResetSimulation();
            _simulationTick = 0;
            _tickAccumulator = 0f;
            LaunchMode = launchMode;

            bool started = launchMode == TwoPeerArenaLaunchMode.Client
                ? _runnerBridge.ConnectToHost(sanitizedAddress, sanitizedPort)
                : _runnerBridge.StartHost();

            if (!started)
            {
                LaunchMode = TwoPeerArenaLaunchMode.None;
                StatusMessage = $"Failed to start {launchMode} session on {sanitizedAddress}:{sanitizedPort}.";
                return false;
            }

            StatusMessage = BuildLaunchMessage(launchMode, sanitizedAddress, sanitizedPort);

            if (!IsArenaSceneActive() && !string.IsNullOrWhiteSpace(ArenaScenePath))
            {
                SceneManager.LoadScene(ArenaScenePath);
            }

            return true;
        }

        private void ApplyOfflineLoopbackFrame(uint tick)
        {
            var payload = new FixedList128Bytes<byte>();
            DeterministicCommandPayload.BuildPayload(ref payload);

            var resolvedFrame = new ResolvedInputFrame
            {
                Tick = tick,
                PeerMask = 1,
                PackedPayload = new FixedList512Bytes<byte>(),
            };

            resolvedFrame.PackedPayload.Add(0);
            resolvedFrame.PackedPayload.Add((byte)payload.Length);
            for (int i = 0; i < payload.Length; i++)
            {
                resolvedFrame.PackedPayload.Add(payload[i]);
            }

            _runtimeHooks.TryApplyResolvedFrame(tick, resolvedFrame);
        }

        private bool IsArenaSceneActive()
        {
            var activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid())
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(ArenaScenePath) && string.Equals(activeScene.path, ArenaScenePath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return string.Equals(activeScene.name, Path.GetFileNameWithoutExtension(ArenaSceneFileName), StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildLaunchMessage(TwoPeerArenaLaunchMode launchMode, string address, ushort port)
        {
            switch (launchMode)
            {
                case TwoPeerArenaLaunchMode.Host:
                    return $"Hosting the arena sample on {address}:{port}.";
                case TwoPeerArenaLaunchMode.Client:
                    return $"Connecting the client sample to {address}:{port}.";
                case TwoPeerArenaLaunchMode.Offline:
                    return "Running the arena sample in offline loopback mode.";
                default:
                    return "Preparing the arena sample.";
            }
        }

        private static float ResolveTickInterval(ushort tickRate)
        {
            float resolvedTickRate = tickRate > 0 ? tickRate : 20f;
            return resolvedTickRate > 0f ? 1f / resolvedTickRate : 0f;
        }

        private static string BuildSiblingScenePath(string scenePath, string siblingFileName)
        {
            if (string.IsNullOrWhiteSpace(scenePath))
            {
                return siblingFileName;
            }

            string directory = Path.GetDirectoryName(scenePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return siblingFileName;
            }

            return Path.Combine(directory, siblingFileName).Replace('\\', '/');
        }
    }
}
