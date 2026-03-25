using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CrossCut.CommandLink
{
    /// <summary>
    /// Chooses whether the command-link bridge boots a real network session or a one-peer local offline session.
    /// </summary>
    public enum CommandLinkSessionMode : byte
    {
        Networked = 0,
        LocalOffline = 1,
    }

    [DefaultExecutionOrder(-80)]
    public sealed class CommandLinkRunnerBridge : MonoBehaviour
    {
        private static readonly Func<uint, bool> LocalOfflineGateCheck = _ => true;
        private const ushort MinimumSafeInputDelayTicks = 2;

        [Header("Network Role")]
        [SerializeField] private bool isHost = true;
        [SerializeField] private CommandLinkSessionMode sessionMode = CommandLinkSessionMode.Networked;

        [Header("Endpoints")]
        [SerializeField] private string listenAddress = "127.0.0.1";
        [SerializeField] private ushort listenPort = 7777;
        [SerializeField] private string remoteAddress = "127.0.0.1";
        [SerializeField] private ushort remotePort = 7777;

        [Header("Session")]
        [SerializeField] private uint matchSeed = 1234;
        [SerializeField] private ushort tickRate = 20;
        [SerializeField] private ushort inputDelayTicks = 2;
        [SerializeField] private ushort maxPlayers = 8;
        [SerializeField] private ushort checksumIntervalTicks = 10;

        [Header("Lifecycle")]
        [SerializeField] private bool persistAcrossScenes = true;
        [SerializeField] private bool autoInitializeOnStart = true;
        [SerializeField] private bool autoJoinOnStart = true;
        [SerializeField] private bool autoSignalReady = true;
        [SerializeField] private bool autoLoadGameplaySceneOnSessionStart = true;
        [SerializeField] private string gameplaySceneName = "SkirmishScene";

        private CommandLinkNetworkEngine _engine;
        private bool _offlineSessionInitialized;
        private bool _autoReadySent;
        private bool _gameplaySceneLoadRequested;
        private LockstepSessionState _lastSessionState = LockstepSessionState.None;

        public bool IsHost => isHost;
        public bool HasActiveSession => _engine != null || IsLocalOfflineSessionActive;

        public CommandLinkSessionState SessionState => _engine != null
            ? _engine.SessionState
            : BuildLocalOfflineSessionState();

        /// <summary>
        /// Configures whether this bridge should run a real network session or a local offline loop.
        /// Must be called before the bridge initializes a session.
        /// </summary>
        public void ConfigureSessionMode(CommandLinkSessionMode mode)
        {
            if (HasActiveSession)
            {
                Debug.LogWarning("[CommandLink] Cannot change session mode while a session is active.");
                return;
            }

            sessionMode = mode;
        }

        /// <summary>
        /// Configures the listen and remote endpoints used for the next session startup.
        /// Must be called before the bridge initializes a session.
        /// </summary>
        public void ConfigureEndpoints(string newListenAddress, ushort newListenPort, string newRemoteAddress, ushort newRemotePort)
        {
            if (HasActiveSession)
            {
                Debug.LogWarning("[CommandLink] Cannot change transport endpoints while a session is active.");
                return;
            }

            listenAddress = string.IsNullOrWhiteSpace(newListenAddress) ? listenAddress : newListenAddress;
            listenPort = newListenPort;
            remoteAddress = string.IsNullOrWhiteSpace(newRemoteAddress) ? remoteAddress : newRemoteAddress;
            remotePort = newRemotePort;
        }

        /// <summary>
        /// Configures the automatic initialization/join/ready/scene-loading behaviors used on Start and Update.
        /// Must be called before the bridge initializes a session.
        /// </summary>
        public void ConfigureAutoLifecycle(bool shouldAutoInitializeOnStart, bool shouldAutoJoinOnStart, bool shouldAutoSignalReady, bool shouldAutoLoadGameplaySceneOnSessionStart)
        {
            if (HasActiveSession)
            {
                Debug.LogWarning("[CommandLink] Cannot change auto lifecycle settings while a session is active.");
                return;
            }

            autoInitializeOnStart = shouldAutoInitializeOnStart;
            autoJoinOnStart = shouldAutoJoinOnStart;
            autoSignalReady = shouldAutoSignalReady;
            autoLoadGameplaySceneOnSessionStart = shouldAutoLoadGameplaySceneOnSessionStart;
        }

        /// <summary>
        /// Configures how many peers must join before the host starts the lockstep session.
        /// Must be called before the bridge initializes a session.
        /// </summary>
        public void ConfigureExpectedPlayers(ushort expectedPlayers)
        {
            if (HasActiveSession)
            {
                Debug.LogWarning("[CommandLink] Cannot change expected player count while a session is active.");
                return;
            }

            maxPlayers = expectedPlayers;
        }

        /// <summary>
        /// Returns whether the bridge is intentionally running in local offline mode with no network engine.
        /// </summary>
        private bool IsLocalOfflineSessionActive => sessionMode == CommandLinkSessionMode.LocalOffline && _offlineSessionInitialized;

        private void Awake()
        {
            if (persistAcrossScenes)
            {
                DontDestroyOnLoad(gameObject);
            }
        }

        /// <summary>
        /// Initializes the configured session mode automatically when the bridge starts.
        /// </summary>
        private void Start()
        {
            if (!autoInitializeOnStart)
            {
                return;
            }

            if (!InitializeEngineIfNeeded())
            {
                return;
            }

            if (autoJoinOnStart && !isHost && _engine != null)
            {
                _engine.RequestJoin();
            }

            if (autoSignalReady && isHost)
            {
                SignalReady();
            }
        }

        /// <summary>
        /// Drives the active network session or offline orchestration path once per frame.
        /// </summary>
        private void Update()
        {
            if (_engine != null)
            {
                DriveEngineFrame();
                TryAutoSignalReady();
            }
            else if (!IsLocalOfflineSessionActive)
            {
                return;
            }

            TryLoadGameplaySceneWhenRunning();
        }

        /// <summary>
        /// Starts a host-side session using the currently selected session mode.
        /// </summary>
        public bool StartHost()
        {
            isHost = true;
            return InitializeEngineIfNeeded();
        }

        /// <summary>
        /// Connects to a remote host when networked mode is enabled, or initializes local offline mode otherwise.
        /// </summary>
        public bool ConnectToHost(string hostAddress, ushort hostPort)
        {
            isHost = false;
            remoteAddress = hostAddress;
            remotePort = hostPort;

            if (!InitializeEngineIfNeeded())
            {
                return false;
            }

            if (_engine != null)
            {
                _engine.RequestJoin();
            }

            return true;
        }

        /// <summary>
        /// Marks the local peer ready, or no-ops in local offline mode where readiness is implicit.
        /// </summary>
        public void SignalReady()
        {
            if (IsLocalOfflineSessionActive)
            {
                _autoReadySent = true;
                return;
            }

            if (_engine == null)
            {
                Debug.LogWarning("[CommandLink] Cannot mark ready before creating a session.");
                return;
            }

            var sessionState = _engine.SessionState;
            if (!isHost && sessionState.LocalPeerId == 0)
            {
                Debug.LogWarning("[CommandLink] Client has not received JoinAccept yet.");
                return;
            }

            _engine.SignalReady();
            _autoReadySent = true;
        }

        private void DriveEngineFrame()
        {
            _engine.Poll();

            if (!TryBuildPendingLocalInput(_engine, out var localInput))
            {
                return;
            }

            _engine.SubmitLocalInputsUpTo(localInput);
        }

        /// <summary>
        /// Builds the next outbound local input only when the engine can actually submit it for the current observed tick.
        /// </summary>
        public static bool TryBuildPendingLocalInput(CommandLinkNetworkEngine engine, out DeterministicInputFrame localInput)
        {
            localInput = default;
            if (engine == null || engine.SessionState.SessionState != LockstepSessionState.Running || !engine.CanSubmitLocalInputForObservedTick())
            {
                return false;
            }

            var payload = new FixedList128Bytes<byte>();
            DeterministicCommandPayload.BuildPayload(ref payload);
            localInput = new DeterministicInputFrame
            {
                Payload = payload,
            };

            return true;
        }

        /// <summary>
        /// Creates the configured network engine, or initializes the synthetic offline session when selected.
        /// </summary>
        private bool InitializeEngineIfNeeded()
        {
            if (_engine != null || IsLocalOfflineSessionActive)
            {
                return true;
            }

            if (sessionMode == CommandLinkSessionMode.LocalOffline)
            {
                InitializeLocalOfflineSession();
                return true;
            }

            if (!CommandLinkRuntimeRegistry.RuntimeHooks.SupportsTickCallbacks)
            {
                Debug.LogWarning("[CommandLink] No ICommandLinkRuntimeHooks are registered for networked mode.");
                return false;
            }

            var commandConfig = CommandLinkConfig.Default;
            commandConfig.IsHost = isHost;

            var lockstepConfig = BuildConfiguredSessionConfig();

            var endpointProvider = new StaticEndpointProvider(
                new CommandLinkEndpoint { Address = listenAddress, Port = listenPort },
                new CommandLinkEndpoint { Address = remoteAddress, Port = remotePort });

            _engine = new CommandLinkNetworkEngine(commandConfig, lockstepConfig, new UnityTransportNetworkDriverFactory(), endpointProvider);
            CommandLinkRuntimeRegistry.Engine = _engine;
            CommandLinkRuntimeRegistry.DriveFromMonoBehaviour = true;

            _autoReadySent = false;
            _gameplaySceneLoadRequested = false;
            _lastSessionState = _engine.SessionState.SessionState;

            return true;
        }

        /// <summary>
        /// Builds the session configuration from the serialized inspector values.
        /// </summary>
        private LockstepSessionConfig BuildConfiguredSessionConfig()
        {
            var lockstepConfig = LockstepSessionConfig.Default;
            lockstepConfig.MatchSeed = matchSeed;
            lockstepConfig.TickRate = tickRate;
            lockstepConfig.InputDelayTicks = ResolveSafeInputDelayTicks();
            lockstepConfig.MaxPlayers = ResolveSafeMaxPlayers();
            lockstepConfig.ChecksumIntervalTicks = checksumIntervalTicks;
            return lockstepConfig;
        }

        /// <summary>
        /// Clamps the configured input delay to a deterministic-safe minimum so zero-delay sessions cannot deadlock on same-tick backlog.
        /// </summary>
        private ushort ResolveSafeInputDelayTicks()
        {
            if (inputDelayTicks >= MinimumSafeInputDelayTicks)
            {
                return inputDelayTicks;
            }

            Debug.LogWarning($"[CommandLink] inputDelayTicks={inputDelayTicks} is too low for stable lockstep. Clamping to {MinimumSafeInputDelayTicks}.");
            return MinimumSafeInputDelayTicks;
        }

        /// <summary>
        /// Clamps the expected peer count to a sane range so the host cannot wait for zero or an unsupported number of players.
        /// </summary>
        private ushort ResolveSafeMaxPlayers()
        {
            ushort maxSupportedPlayers = (ushort)Math.Max(1, CommandLinkConfig.Default.MaxPeers);
            if (maxPlayers >= 1 && maxPlayers <= maxSupportedPlayers)
            {
                return maxPlayers;
            }

            ushort clampedPlayers = Math.Clamp(maxPlayers, (ushort)1, maxSupportedPlayers);
            Debug.LogWarning($"[CommandLink] maxPlayers={maxPlayers} is outside the supported range. Clamping to {clampedPlayers}.");
            return clampedPlayers;
        }

        /// <summary>
        /// Enables local offline testing by keeping the lockstep runner ungated and leaving the network engine absent.
        /// </summary>
        private void InitializeLocalOfflineSession()
        {
            CommandLinkRuntimeRegistry.Engine = null;
            CommandLinkRuntimeRegistry.DriveFromMonoBehaviour = true;
            CommandLinkRuntimeRegistry.RuntimeHooks.SetGateCheck(LocalOfflineGateCheck);

            _offlineSessionInitialized = true;
            _autoReadySent = true;
            _gameplaySceneLoadRequested = false;
            _lastSessionState = LockstepSessionState.None;
        }

        /// <summary>
        /// Synthesizes a running one-peer session so local offline mode can share the same orchestration flow.
        /// </summary>
        private CommandLinkSessionState BuildLocalOfflineSessionState()
        {
            if (!IsLocalOfflineSessionActive)
            {
                return new CommandLinkSessionState
                {
                    SessionState = LockstepSessionState.None
                };
            }

            var sessionConfig = BuildConfiguredSessionConfig();
            sessionConfig.MaxPlayers = 1;

            var connectedPeers = new FixedList32Bytes<byte>();
            connectedPeers.Add(0);

            var readyPeers = new FixedList32Bytes<byte>();
            readyPeers.Add(0);

            return new CommandLinkSessionState
            {
                SessionState = LockstepSessionState.Running,
                LocalPeerId = 0,
                HostPeerId = 0,
                SessionConfig = sessionConfig,
                ConnectedPeerIds = connectedPeers,
                ReadyPeerIds = readyPeers
            };
        }

        private void TryAutoSignalReady()
        {
            if (!autoSignalReady || _autoReadySent || _engine == null)
            {
                return;
            }

            var sessionState = _engine.SessionState;
            if (!isHost && sessionState.LocalPeerId == 0)
            {
                return;
            }

            _engine.SignalReady();
            _autoReadySent = true;
        }

        /// <summary>
        /// Loads the gameplay scene the first frame the current session reaches the running state.
        /// </summary>
        private void TryLoadGameplaySceneWhenRunning()
        {
            var currentState = SessionState.SessionState;
            if (!autoLoadGameplaySceneOnSessionStart
                || _gameplaySceneLoadRequested
                || string.IsNullOrWhiteSpace(gameplaySceneName))
            {
                _lastSessionState = currentState;
                return;
            }

            if (currentState != LockstepSessionState.Running)
            {
                _lastSessionState = currentState;
                return;
            }

            if (_lastSessionState == LockstepSessionState.Running)
            {
                return;
            }

            _lastSessionState = currentState;

            var activeScene = SceneManager.GetActiveScene();
            if (activeScene.name == gameplaySceneName)
            {
                return;
            }

            _gameplaySceneLoadRequested = true;
            SceneManager.LoadScene(gameplaySceneName);
        }

        /// <summary>
        /// Disposes the network engine and clears any local offline gate overrides during teardown.
        /// </summary>
        private void OnDestroy()
        {
            _engine?.Dispose();
            if (CommandLinkRuntimeRegistry.Engine == _engine)
            {
                CommandLinkRuntimeRegistry.Engine = null;
            }

            CommandLinkRuntimeRegistry.RuntimeHooks.ClearGateCheck(LocalOfflineGateCheck);

            _engine = null;
            _offlineSessionInitialized = false;
            _autoReadySent = false;
            _gameplaySceneLoadRequested = false;
            _lastSessionState = LockstepSessionState.None;
            CommandLinkRuntimeRegistry.DriveFromMonoBehaviour = true;
        }
    }
}
