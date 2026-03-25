using System;
using System.Collections.Generic;
using System.Text;
using CrossCut.CommandLink.Diagnostics;
using Unity.Collections;
using UnityEngine;

namespace CrossCut.CommandLink
{
    /// <summary>
    /// Owns the transport-facing lockstep session, including peer lifecycle, input exchange, and checksum reporting.
    /// </summary>
    public sealed class CommandLinkNetworkEngine : ILockstepCommandLinkBridge, IDisposable
    {
        private const byte DeterministicNoopPayloadVersion = 1;
        private const int StartupWarmupExtraTicks = 60;
        private const float PreSessionGateLogIntervalSeconds = 1f;
        private const float BackfillLogIntervalSeconds = 1f;
        private const float InputFrameDiagnosticIntervalSeconds = 1f;

        private readonly CommandLinkConfig _config;
        private LockstepSessionConfig _sessionConfig;
        private readonly INetworkDriver _driver;
        private readonly ICommandLinkRuntimeHooks _runtimeHooks;

        private readonly Dictionary<uint, Dictionary<byte, DeterministicInputFrame>> _inputsByTick = new Dictionary<uint, Dictionary<byte, DeterministicInputFrame>>();
        private readonly Dictionary<uint, ResolvedInputFrame> _resolvedByTick = new Dictionary<uint, ResolvedInputFrame>();
        private readonly Dictionary<uint, uint> _checksumsByTick = new Dictionary<uint, uint>();
        private readonly Dictionary<uint, List<DeterministicInputFrame>> _pendingResendsByTick = new Dictionary<uint, List<DeterministicInputFrame>>();
        private readonly HashSet<byte> _connectedPeers = new HashSet<byte>();
        private readonly HashSet<byte> _readyPeers = new HashSet<byte>();
        private readonly SortedDictionary<uint, DeterministicInputFrame> _pendingLocalInputFramesByTick = new SortedDictionary<uint, DeterministicInputFrame>();
        private readonly SortedDictionary<uint, PendingAckState> _pendingAckStatesByTick = new SortedDictionary<uint, PendingAckState>();

        private byte _localPeerId;
        private byte _hostPeerId;
        private uint _sequence;
        private LockstepSessionState _state;
        private bool _joinRequested;
        private bool _joinSent;
        private bool _startupWarmupSeeded;
        private float _nextPreSessionGateLogTime;
        private float _nextBackfillLogTime;
        private float _nextInputFrameDiagnosticTime;
        private float _nextAckWaitLogTime;
        private float _nextInputCoalesceLogTime;
        private int _inputFramesSent;
        private int _inputFramesReceived;
        private int _coalescedLocalInputFramesSinceLastLog;
        private uint _latestObservedTick;
        private bool _hasObservedTick;
        private uint _lastSubmittedObservedTick;
        private bool _hasSubmittedObservedTick;

        private static readonly List<DecodedMoveCommand> DiagnosticMoveCommands = new List<DecodedMoveCommand>(8);
        private static readonly List<DecodedBuildPlaceCommand> DiagnosticBuildCommands = new List<DecodedBuildPlaceCommand>(8);
        private static readonly List<DecodedRecruitCommand> DiagnosticRecruitCommands = new List<DecodedRecruitCommand>(8);

        /// <summary>
        /// Returns a snapshot of the current session state for UI and orchestration callers.
        /// </summary>
        public CommandLinkSessionState SessionState => new CommandLinkSessionState
        {
            SessionState = _state,
            LocalPeerId = _localPeerId,
            HostPeerId = _hostPeerId,
            SessionConfig = _sessionConfig,
            ConnectedPeerIds = ToFixedList(_connectedPeers),
            ReadyPeerIds = ToFixedList(_readyPeers)
        };

        /// <summary>
        /// Returns the latest copied diagnostics snapshot for editor and runtime tooling.
        /// </summary>
        public CommandLinkDiagnosticsSnapshot CopyDiagnosticsSnapshot()
        {
            return CommandLinkDiagnosticsService.CopySnapshot();
        }

        /// <summary>
        /// Creates the network engine and hooks it into the lockstep runner callbacks.
        /// </summary>
        public CommandLinkNetworkEngine(
            CommandLinkConfig config,
            LockstepSessionConfig sessionConfig,
            INetworkDriverFactory driverFactory,
            INetworkEndpointProvider endpointProvider)
        {
            CommandLinkDiagnosticsService.Reset();
            _config = config;
            _sessionConfig = sessionConfig;
            _driver = driverFactory.Create(config, sessionConfig, endpointProvider);
            _runtimeHooks = CommandLinkRuntimeRegistry.RuntimeHooks ?? NullCommandLinkRuntimeHooks.Instance;
            _state = LockstepSessionState.WaitingForPeers;

            if (_config.IsHost)
            {
                _hostPeerId = 0;
                _localPeerId = 0;
                _connectedPeers.Add(0);
            }

            if (!_runtimeHooks.SupportsTickCallbacks)
            {
                throw new InvalidOperationException("[CommandLink] Network sessions require ICommandLinkRuntimeHooks with tick callbacks.");
            }

            _runtimeHooks.SetGateCheck(AllInputsReady);
            _runtimeHooks.AddPreTick(OnPreTick);
            _runtimeHooks.AddPostTick(OnPostTick);
        }

        /// <summary>
        /// Polls transport, processes inbound packets, and advances session-side housekeeping.
        /// </summary>
        public void Poll()
        {
            _driver.Poll();
            while (_driver.TryDequeue(out var packet))
            {
                ProcessPacket(packet);
            }

            TrySendPendingJoinRequest();
            TryStartSession();
            PumpResendQueue(GetObservedTick());
            TrySendQueuedInputFrames();
            LogInputFrameDiagnostics();
            PushDiagnosticsState();
        }

        /// <summary>
        /// Submits one local input frame using the configured input delay offset.
        /// </summary>
        public void SubmitLocalInput(in DeterministicInputFrame localInput)
        {
            if (!TryBeginSubmission(out uint currentTick))
            {
                return;
            }

            uint targetTick = currentTick + _sessionConfig.InputDelayTicks;
            SubmitLocalInputForTargetTick(targetTick, localInput);
        }

        /// <summary>
        /// Returns whether the engine is currently able to accept one new local submission for the latest observed tick.
        /// </summary>
        public bool CanSubmitLocalInputForObservedTick()
        {
            if (!_hasObservedTick)
            {
                return false;
            }

            uint currentTick = GetObservedTick();
            return !_hasSubmittedObservedTick || currentTick != _lastSubmittedObservedTick;
        }

        /// <summary>
        /// Fills any missing local future frames with deterministic noops before submitting the latest input.
        /// </summary>
        public void SubmitLocalInputsUpTo(in DeterministicInputFrame latestInput)
        {
            if (!TryBeginSubmission(out uint currentTick))
            {
                return;
            }

            uint targetTick = currentTick + _sessionConfig.InputDelayTicks;
            uint firstDynamicTick = _sessionConfig.InputDelayTicks;
            int catchUpFrames = 0;

            for (uint fillTick = firstDynamicTick; fillTick < targetTick; fillTick++)
            {
                if (HasInputForPeer(fillTick, _localPeerId))
                {
                    continue;
                }

                var noopInput = new DeterministicInputFrame
                {
                    Payload = BuildNoopPayload()
                };

                SubmitLocalInputForTargetTick(fillTick, noopInput);
                catchUpFrames++;
            }

            SubmitLocalInputForTargetTick(targetTick, latestInput);

            if (catchUpFrames > 0)
            {
                float now = Time.realtimeSinceStartup;
                if (now >= _nextBackfillLogTime)
                {
                    _nextBackfillLogTime = now + BackfillLogIntervalSeconds;
                    Debug.LogWarning($"[CommandLink] Backfilled {catchUpFrames} local input frame(s) through tick {targetTick - 1}.");
                }
            }
        }

        private bool TryBeginSubmission(out uint currentTick)
        {
            currentTick = GetObservedTick();
            if (!_hasObservedTick)
            {
                return false;
            }

            if (_hasSubmittedObservedTick && currentTick == _lastSubmittedObservedTick)
            {
                return false;
            }

            _lastSubmittedObservedTick = currentTick;
            _hasSubmittedObservedTick = true;
            return true;
        }

        private uint GetObservedTick()
        {
            return _hasObservedTick ? _latestObservedTick : 0;
        }

        private void ObserveTick(uint tick)
        {
            _latestObservedTick = tick;
            _hasObservedTick = true;
        }

        private void SubmitLocalInputForTargetTick(uint targetTick, in DeterministicInputFrame localInput)
        {
            var inputFrame = localInput;
            inputFrame.TargetTick = targetTick;
            inputFrame.PeerId = _localPeerId;
            inputFrame.Sequence = ++_sequence;

            CacheInput(targetTick, inputFrame);
            if (_pendingLocalInputFramesByTick.TryGetValue(targetTick, out var replacedFrame))
            {
                _pendingLocalInputFramesByTick[targetTick] = inputFrame;
                LogLocalInputCoalesced(targetTick, replacedFrame.Sequence, inputFrame.Sequence);
            }
            else
            {
                _pendingLocalInputFramesByTick[targetTick] = inputFrame;
            }

            RecordQueuedBuildFrames(inputFrame);
            TrySendQueuedInputFrames();
        }

        private bool HasInputForPeer(uint tick, byte peerId)
        {
            return _inputsByTick.TryGetValue(tick, out var byPeer) && byPeer.ContainsKey(peerId);
        }

        /// <summary>
        /// Returns whether the current tick has a frame from every connected peer and can safely advance.
        /// </summary>
        public bool AllInputsReady(uint tick)
        {
            ObserveTick(tick);

            if (_state != LockstepSessionState.Running)
            {
                LogPreSessionGateBlock(tick);
                return false;
            }

            if (!_inputsByTick.TryGetValue(tick, out var byPeer))
            {
                LogMissingInputs(tick, 0);
                return false;
            }

            if (byPeer.Count < RequiredPeerCount())
            {
                LogMissingInputs(tick, byPeer.Count);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Returns the cached resolved frame for a tick, building it on demand from per-peer inputs when needed.
        /// </summary>
        public bool TryGetResolvedFrame(uint tick, out ResolvedInputFrame resolvedFrame)
        {
            if (_resolvedByTick.TryGetValue(tick, out resolvedFrame))
            {
                return true;
            }

            if (!_inputsByTick.TryGetValue(tick, out var byPeer) || byPeer.Count == 0)
            {
                resolvedFrame = default;
                return false;
            }

            resolvedFrame = BuildResolvedFrame(tick, byPeer);
            _resolvedByTick[tick] = resolvedFrame;
            return true;
        }

        /// <summary>
        /// Broadcasts the local simulation checksum for the requested tick.
        /// </summary>
        public void BroadcastChecksum(uint tick, uint checksum)
        {
            _checksumsByTick[tick] = checksum;
            var message = new ChecksumMessage { PeerId = _localPeerId, Tick = tick, Checksum = checksum };
            SendMessage(message);
        }

        /// <summary>
        /// Marks the local peer ready so the host can begin the lockstep session.
        /// </summary>
        public void SignalReady()
        {
            _readyPeers.Add(_localPeerId);
            var ready = new ReadyMessage { PeerId = _localPeerId };
            SendMessage(ready);
            Debug.Log($"[CommandLink] Peer {_localPeerId} marked ready.");
        }

        /// <summary>
        /// Requests admission to the host session when running as a client.
        /// </summary>
        public void RequestJoin()
        {
            if (_config.IsHost)
            {
                return;
            }

            _joinRequested = true;
            _joinSent = false;

            if (_driver.IsHostConnectionReady)
            {
                Debug.Log("[CommandLink] Join requested. Waiting for poll to send now that transport is ready.");
            }
            else
            {
                Debug.Log("[CommandLink] Join requested. Waiting for transport connect event before sending.");
            }
        }

        /// <summary>
        /// Routes one inbound network packet to the matching session message handler.
        /// </summary>
        private void ProcessPacket(in CommandLinkPacket packet)
        {
            if (packet.Kind == CommandLinkPacketKind.TransportDisconnect)
            {
                HandlePeerDisconnected(packet.PeerId, "transport");
                return;
            }

            if (!CommandLinkMessageSerializer.TryReadMessageType(packet.Payload, out var messageType))
            {
                return;
            }

            switch (messageType)
            {
                case CommandLinkMessageType.JoinRequest:
                    if (_config.IsHost && CommandLinkMessageSerializer.TryDeserializeJoinRequest(packet.Payload, out _))
                    {
                        AcceptJoiningPeer(packet.PeerId);
                    }
                    break;
                case CommandLinkMessageType.JoinAccept:
                    if (CommandLinkMessageSerializer.TryDeserializeJoinAccept(packet.Payload, out var joinAccept))
                    {
                        _localPeerId = joinAccept.AssignedPeerId;
                        _hostPeerId = joinAccept.HostPeerId;
                        _sessionConfig.MatchSeed = joinAccept.MatchSeed;
                        _sessionConfig.TickRate = joinAccept.TickRate;
                        _sessionConfig.InputDelayTicks = joinAccept.InputDelayTicks;
                        _sessionConfig.MaxPlayers = joinAccept.MaxPlayers;
                        _sessionConfig.ChecksumIntervalTicks = joinAccept.ChecksumIntervalTicks;
                        _connectedPeers.Add(_hostPeerId);
                        _connectedPeers.Add(_localPeerId);
                        _joinRequested = false;
                        _joinSent = false;
                    }
                    break;
                case CommandLinkMessageType.SessionStart:
                    if (CommandLinkMessageSerializer.TryDeserializeSessionStart(packet.Payload, out _))
                    {
                        EnterRunningState();
                        Debug.Log("[CommandLink] Session started.");
                    }
                    break;
                case CommandLinkMessageType.InputFrame:
                    if (CommandLinkMessageSerializer.TryDeserializeInputFrame(packet.Payload, out var inputFrameMessage))
                    {
                        byte senderPeerId = ResolveTransportPeerId(inputFrameMessage.PeerId, packet.PeerId, messageType);
                        var frame = new DeterministicInputFrame
                        {
                            PeerId = senderPeerId,
                            TargetTick = inputFrameMessage.Tick,
                            Sequence = inputFrameMessage.Sequence,
                            Payload = CopyTo128(inputFrameMessage.Payload)
                        };

                        RecordReceivedBuildFrames(frame);
                        CacheInput(frame.TargetTick, frame);
                        _inputFramesReceived++;

                        var ack = new InputAckMessage
                        {
                            PeerId = _localPeerId,
                            AckedSequence = inputFrameMessage.Sequence,
                            AckedTick = inputFrameMessage.Tick
                        };
                        // The transport source is the authoritative return path for targeted acknowledgments.
                        SendMessage(ack, packet.PeerId);
                    }
                    break;
                case CommandLinkMessageType.InputAck:
                    if (CommandLinkMessageSerializer.TryDeserializeInputAck(packet.Payload, out var ackMessage))
                    {
                        ResolveTransportPeerId(ackMessage.PeerId, packet.PeerId, messageType);
                        ClearAckedResends(ackMessage.AckedTick, ackMessage.AckedSequence);
                        MarkInputFrameAcknowledged(packet.PeerId, ackMessage.AckedTick, ackMessage.AckedSequence);
                    }
                    break;
                case CommandLinkMessageType.Checksum:
                    if (CommandLinkMessageSerializer.TryDeserializeChecksum(packet.Payload, out var checksumMessage))
                    {
                        byte senderPeerId = ResolveTransportPeerId(checksumMessage.PeerId, packet.PeerId, messageType);
                        if (_checksumsByTick.TryGetValue(checksumMessage.Tick, out var localChecksum) && localChecksum != checksumMessage.Checksum)
                        {
                            ReportChecksumMismatch(senderPeerId, checksumMessage.Tick, localChecksum, checksumMessage.Checksum);
                        }
                    }
                    break;
                case CommandLinkMessageType.DisconnectNotice:
                    if (CommandLinkMessageSerializer.TryDeserializeDisconnect(packet.Payload, out var disconnectMessage))
                    {
                        HandlePeerDisconnected(ResolveTransportPeerId(disconnectMessage.PeerId, packet.PeerId, messageType), "message");
                    }
                    break;
                case CommandLinkMessageType.Ready:
                    if (CommandLinkMessageSerializer.TryDeserializeReady(packet.Payload, out var readyMessage))
                    {
                        _readyPeers.Add(ResolveTransportPeerId(readyMessage.PeerId, packet.PeerId, messageType));
                    }
                    break;
            }
        }

        /// <summary>
        /// Accepts a newly connected transport peer and sends it the host-authored session config while the session is still forming.
        /// </summary>
        private void AcceptJoiningPeer(byte transportPeerId)
        {
            if (transportPeerId == 0)
            {
                return;
            }

            if (_state != LockstepSessionState.WaitingForPeers)
            {
                Debug.LogWarning($"[CommandLink] Rejecting peer {transportPeerId} because the session is already {_state}.");
                return;
            }

            if (_connectedPeers.Contains(transportPeerId))
            {
                return;
            }

            if (_connectedPeers.Count >= ExpectedPeerCount())
            {
                Debug.LogWarning($"[CommandLink] Rejecting peer {transportPeerId} because the session is already full ({_connectedPeers.Count}/{ExpectedPeerCount()}).");
                return;
            }

            byte nextPeerId = transportPeerId;
            _connectedPeers.Add(nextPeerId);

            var joinAccept = new JoinAcceptMessage
            {
                AssignedPeerId = nextPeerId,
                HostPeerId = _hostPeerId,
                MatchSeed = _sessionConfig.MatchSeed,
                TickRate = _sessionConfig.TickRate,
                InputDelayTicks = _sessionConfig.InputDelayTicks,
                MaxPlayers = _sessionConfig.MaxPlayers,
                ChecksumIntervalTicks = _sessionConfig.ChecksumIntervalTicks
            };
            SendMessage(joinAccept, transportPeerId);
            Debug.Log($"[CommandLink] Accepted peer {nextPeerId}.");
        }

        /// <summary>
        /// Sends the pending join request as soon as the transport reports the host connection ready.
        /// </summary>
        private void TrySendPendingJoinRequest()
        {
            if (_config.IsHost || !_joinRequested || _joinSent || _localPeerId != 0)
            {
                return;
            }

            if (!_driver.IsHostConnectionReady)
            {
                return;
            }

            var request = new JoinRequestMessage
            {
                RequestedTickRate = _sessionConfig.TickRate,
                RequestedInputDelay = _sessionConfig.InputDelayTicks
            };

            Debug.Log("[CommandLink] Sending JoinRequest (transport ready).");
            SendMessage(request);
            _joinSent = true;
        }
        /// <summary>
        /// Starts the session once the host observes every expected peer in the ready set.
        /// </summary>
        private void TryStartSession()
        {
            if (_state == LockstepSessionState.Running || !_config.IsHost)
            {
                return;
            }

            int expectedPeerCount = ExpectedPeerCount();
            if (_connectedPeers.Count < expectedPeerCount)
            {
                return;
            }

            if (_readyPeers.Count < expectedPeerCount)
            {
                return;
            }

            _state = LockstepSessionState.Starting;
            var startMessage = new SessionStartMessage
            {
                MatchSeed = _sessionConfig.MatchSeed
            };

            SendMessage(startMessage);
            EnterRunningState();
            Debug.Log($"[CommandLink] Session start broadcast. seed={_sessionConfig.MatchSeed} tickRate={_sessionConfig.TickRate} inputDelay={_sessionConfig.InputDelayTicks}");
        }

        /// <summary>
        /// Transitions the engine into the running state and seeds initial warmup frames.
        /// </summary>
        private void EnterRunningState()
        {
            _state = LockstepSessionState.Running;
            _hasSubmittedObservedTick = false;
            SeedStartupWarmupFrames();
        }

        /// <summary>
        /// Pre-seeds early ticks with noop frames so the simulation can start immediately after session launch.
        /// </summary>
        private void SeedStartupWarmupFrames()
        {
            if (_startupWarmupSeeded)
            {
                return;
            }

            _startupWarmupSeeded = true;

            int startupWarmupTicks = _sessionConfig.InputDelayTicks + StartupWarmupExtraTicks;
            if (startupWarmupTicks <= 0)
            {
                return;
            }

            var peers = new List<byte>(_connectedPeers);
            peers.Sort();
            if (peers.Count == 0)
            {
                peers.Add(_localPeerId);
            }

            var noopPayload = BuildNoopPayload();
            int seededFrames = 0;

            for (uint tick = 0; tick < startupWarmupTicks; tick++)
            {
                if (!_inputsByTick.TryGetValue(tick, out var byPeer))
                {
                    byPeer = new Dictionary<byte, DeterministicInputFrame>();
                    _inputsByTick[tick] = byPeer;
                }

                for (int i = 0; i < peers.Count; i++)
                {
                    byte peerId = peers[i];
                    if (byPeer.ContainsKey(peerId))
                    {
                        continue;
                    }

                    byPeer[peerId] = new DeterministicInputFrame
                    {
                        PeerId = peerId,
                        TargetTick = tick,
                        Sequence = 0,
                        Payload = noopPayload
                    };

                    seededFrames++;
                }
            }

            Debug.Log($"[CommandLink] Seeded startup warmup frames. ticks=0..{startupWarmupTicks - 1} peers={peers.Count} seeded={seededFrames}");
        }

        /// <summary>
        /// Builds the canonical noop payload used for warmup and catch-up frames.
        /// </summary>
        private static FixedList128Bytes<byte> BuildNoopPayload()
        {
            var payload = new FixedList128Bytes<byte>();
            payload.Add(DeterministicNoopPayloadVersion);
            payload.Add(0);
            return payload;
        }

        private void LogPreSessionGateBlock(uint tick)
        {
            float now = Time.realtimeSinceStartup;
            if (now < _nextPreSessionGateLogTime)
            {
                return;
            }

            _nextPreSessionGateLogTime = now + PreSessionGateLogIntervalSeconds;
            Debug.Log($"[CommandLink] Gate blocked at tick {tick} because session state is {_state}.");
        }

        private void LogInputFrameDiagnostics()
        {
            if (_state != LockstepSessionState.Running)
            {
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (now < _nextInputFrameDiagnosticTime)
            {
                return;
            }

            _nextInputFrameDiagnosticTime = now + InputFrameDiagnosticIntervalSeconds;

            uint currentTick = GetObservedTick();
            uint targetTick = currentTick + _sessionConfig.InputDelayTicks;
            int requiredPeers = RequiredPeerCount();
            int currentTickInputs = _inputsByTick.TryGetValue(currentTick, out var currentByPeer) ? currentByPeer.Count : 0;
            int targetTickInputs = _inputsByTick.TryGetValue(targetTick, out var targetByPeer) ? targetByPeer.Count : 0;
            int currentBuildCommands = CountResolvedBuildCommands(currentTick);
            int targetBuildCommands = CountResolvedBuildCommands(targetTick);
            string inFlight = TryGetOldestPendingAckState(out var pendingAckState)
                ? $"{pendingAckState.Frame.TargetTick}:{pendingAckState.Frame.Sequence}/acks={FormatPeers(pendingAckState.PendingPeers)}"
                : "none";

            // Debug.Log($"[CommandLink][Frames] state={_state} currentTick={currentTick} targetTick={targetTick} peers={FormatPeers(_connectedPeers)} sent={_inputFramesSent} recv={_inputFramesReceived} currentInputs={currentTickInputs}/{requiredPeers} targetInputs={targetTickInputs}/{requiredPeers} currentBuilds={currentBuildCommands} targetBuilds={targetBuildCommands} queuedLocal={_pendingLocalInputFramesByTick.Count} pendingTicks={FormatPendingLocalTicks()} inFlight={inFlight}");
        }

        private void LogMissingInputs(uint tick, int presentCount)
        {
            float now = Time.realtimeSinceStartup;
            if (now < _nextPreSessionGateLogTime)
            {
                return;
            }

            _nextPreSessionGateLogTime = now + PreSessionGateLogIntervalSeconds;
            Debug.LogWarning($"[CommandLink] Missing input frames at tick {tick}. present={presentCount}/{RequiredPeerCount()} peers={FormatPeers(_connectedPeers)}");
        }

        /// <summary>
        /// Resends outstanding frames until they are acknowledged or exceed the resend budget.
        /// </summary>
        private void PumpResendQueue(uint currentTick)
        {
            var expiredTicks = new List<uint>();
            foreach (var kvp in _pendingResendsByTick)
            {
                if (kvp.Key > currentTick)
                {
                    continue;
                }

                foreach (var frame in kvp.Value)
                {
                    SendInputFrame(frame, true);
                }

                if (currentTick - kvp.Key > _config.MaxResendAttempts)
                {
                    expiredTicks.Add(kvp.Key);
                }
            }

            for (int i = 0; i < expiredTicks.Count; i++)
            {
                _pendingResendsByTick.Remove(expiredTicks[i]);
            }

            ExpireStalePendingAcknowledgments(currentTick);
        }

        /// <summary>
        /// Drops ack-tracking entries once they are older than the resend window so diagnostics do not latch onto stale frames forever.
        /// </summary>
        private void ExpireStalePendingAcknowledgments(uint currentTick)
        {
            if (_pendingAckStatesByTick.Count == 0)
            {
                return;
            }

            var expiredTicks = new List<uint>();
            foreach (var kvp in _pendingAckStatesByTick)
            {
                if (kvp.Key > currentTick)
                {
                    break;
                }

                if (currentTick - kvp.Key <= _config.MaxResendAttempts)
                {
                    continue;
                }

                expiredTicks.Add(kvp.Key);
            }

            for (int i = 0; i < expiredTicks.Count; i++)
            {
                _pendingAckStatesByTick.Remove(expiredTicks[i]);
            }
        }

        /// <summary>
        /// Stores one peer input frame under the authoritative target tick.
        /// </summary>
        private void CacheInput(uint tick, in DeterministicInputFrame frame)
        {
            if (!_inputsByTick.TryGetValue(tick, out var byPeer))
            {
                byPeer = new Dictionary<byte, DeterministicInputFrame>();
                _inputsByTick[tick] = byPeer;
            }

            byPeer[frame.PeerId] = frame;
            _resolvedByTick.Remove(tick);
        }

        /// <summary>
        /// Queues one outbound input frame for resend tracking until an acknowledgment arrives.
        /// </summary>
        private void QueueForResend(uint tick, in DeterministicInputFrame frame)
        {
            if (!_pendingResendsByTick.TryGetValue(tick, out var frames))
            {
                frames = new List<DeterministicInputFrame>();
                _pendingResendsByTick[tick] = frames;
            }

            frames.Add(frame);
        }

        /// <summary>
        /// Pops the lowest pending target tick so local frames are always sent in deterministic tick order.
        /// </summary>
        private bool TryPopNextPendingLocalInputFrame(out DeterministicInputFrame frame)
        {
            frame = default;
            if (_pendingLocalInputFramesByTick.Count == 0)
            {
                return false;
            }

            // SortedDictionary iteration returns keys in ascending order, so the first entry is always the oldest tick.
            uint nextTick = uint.MaxValue;
            foreach (var kvp in _pendingLocalInputFramesByTick)
            {
                nextTick = kvp.Key;
                frame = kvp.Value;
                break;
            }

            if (nextTick == uint.MaxValue)
            {
                return false;
            }

            _pendingLocalInputFramesByTick.Remove(nextTick);
            return true;
        }

        /// <summary>
        /// Throttles diagnostics when same-tick local inputs are coalesced in the pending map.
        /// </summary>
        private void LogLocalInputCoalesced(uint tick, uint replacedSequence, uint newSequence)
        {
            _coalescedLocalInputFramesSinceLastLog++;

            float now = Time.realtimeSinceStartup;
            if (now < _nextInputCoalesceLogTime)
            {
                return;
            }

            _nextInputCoalesceLogTime = now + InputFrameDiagnosticIntervalSeconds;
            Debug.Log($"[CommandLink][Coalesce] tick={tick} replacedSequence={replacedSequence} newSequence={newSequence} pendingTicks={FormatPendingLocalTicks()} coalescedSinceLastLog={_coalescedLocalInputFramesSinceLastLog}");
            _coalescedLocalInputFramesSinceLastLog = 0;
        }

        /// <summary>
        /// Formats a compact pending local-input summary with one entry per target tick.
        /// </summary>
        private string FormatPendingLocalTicks()
        {
            if (_pendingLocalInputFramesByTick.Count == 0)
            {
                return "none";
            }

            const int maxTicksToShow = 8;
            var sb = new StringBuilder(maxTicksToShow * 8);
            int shown = 0;

            foreach (var kvp in _pendingLocalInputFramesByTick)
            {
                if (shown > 0)
                {
                    sb.Append(',');
                }

                sb.Append(kvp.Key);
                sb.Append(":1");
                shown++;

                if (shown >= maxTicksToShow)
                {
                    if (_pendingLocalInputFramesByTick.Count > maxTicksToShow)
                    {
                        sb.Append(",...");
                    }

                    break;
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Decodes the local payload and records any build commands assigned to the queued frame.
        /// </summary>
        private void RecordQueuedBuildFrames(in DeterministicInputFrame inputFrame)
        {
            DeterministicCommandPayload.DecodeSinglePayload(inputFrame.Payload, inputFrame.PeerId, DiagnosticMoveCommands, DiagnosticBuildCommands, DiagnosticRecruitCommands);
            for (int i = 0; i < DiagnosticBuildCommands.Count; i++)
            {
                var build = DiagnosticBuildCommands[i];
                CommandLinkDiagnosticsService.RecordLocalFrameQueued(
                    inputFrame.PeerId,
                    inputFrame.TargetTick,
                    inputFrame.Sequence,
                    build.BuildingTypeId,
                    build.TargetCell.x,
                    build.TargetCell.y,
                    _pendingLocalInputFramesByTick.Count);
            }
        }

        /// <summary>
        /// Decodes an inbound frame and records any build commands observed before lockstep resolution.
        /// </summary>
        private void RecordReceivedBuildFrames(in DeterministicInputFrame inputFrame)
        {
            DeterministicCommandPayload.DecodeSinglePayload(inputFrame.Payload, inputFrame.PeerId, DiagnosticMoveCommands, DiagnosticBuildCommands, DiagnosticRecruitCommands);
            for (int i = 0; i < DiagnosticBuildCommands.Count; i++)
            {
                var build = DiagnosticBuildCommands[i];
                CommandLinkDiagnosticsService.RecordRemoteFrameReceived(
                    inputFrame.PeerId,
                    inputFrame.TargetTick,
                    inputFrame.Sequence,
                    build.BuildingTypeId,
                    build.TargetCell.x,
                    build.TargetCell.y);
            }
        }

        /// <summary>
        /// Pushes the current network and queue state into the shared diagnostics service.
        /// </summary>
        private void PushDiagnosticsState()
        {
            uint currentTick = GetObservedTick();
            uint targetTick = currentTick + _sessionConfig.InputDelayTicks;
            int requiredPeers = RequiredPeerCount();
            int currentTickInputs = _inputsByTick.TryGetValue(currentTick, out var currentByPeer) ? currentByPeer.Count : 0;
            int currentBuildCommands = CountResolvedBuildCommands(currentTick);
            int targetTickInputs = _inputsByTick.TryGetValue(targetTick, out var targetByPeer) ? targetByPeer.Count : 0;
            int targetBuildCommands = CountResolvedBuildCommands(targetTick);
            bool hasOutstandingAck = TryGetOldestPendingAckState(out var oldestPendingAckState);
            int inFlightInputsPresent = hasOutstandingAck && _inputsByTick.TryGetValue(oldestPendingAckState.Frame.TargetTick, out var inFlightByPeer)
                ? inFlightByPeer.Count
                : 0;
            int missingInputs = Math.Max(0, requiredPeers - currentTickInputs);
            TryGetQueuedTickRange(out uint oldestQueuedTick, out uint newestQueuedTick);

            CommandLinkDiagnosticsService.RecordNetworkState(
                _state.ToString(),
                currentTick,
                _localPeerId,
                _hostPeerId,
                requiredPeers,
                FormatPeers(_connectedPeers),
                _pendingLocalInputFramesByTick.Count,
                oldestQueuedTick,
                newestQueuedTick,
                hasOutstandingAck,
                hasOutstandingAck ? oldestPendingAckState.Frame.TargetTick : 0,
                hasOutstandingAck ? oldestPendingAckState.Frame.Sequence : 0,
                inFlightInputsPresent,
                currentTickInputs,
                BuildPeerMask(currentByPeer),
                currentBuildCommands,
                targetTickInputs,
                targetBuildCommands,
                CountResendFrames(),
                missingInputs,
                hasOutstandingAck ? oldestPendingAckState.PendingPeers.Count : 0,
                hasOutstandingAck ? FormatPeers(oldestPendingAckState.PendingPeers) : string.Empty,
                FormatPendingLocalTicks(),
                DeterministicCommandPayload.CopyPendingIntentSummary());
        }

        /// <summary>
        /// Counts the number of frames still tracked by the resend backlog.
        /// </summary>
        private int CountResendFrames()
        {
            int count = 0;
            foreach (var kvp in _pendingResendsByTick)
            {
                count += kvp.Value.Count;
            }

            return count;
        }

        /// <summary>
        /// Returns the oldest and newest currently queued target ticks.
        /// </summary>
        private void TryGetQueuedTickRange(out uint oldestQueuedTick, out uint newestQueuedTick)
        {
            oldestQueuedTick = 0;
            newestQueuedTick = 0;
            if (_pendingLocalInputFramesByTick.Count == 0)
            {
                return;
            }

            foreach (var kvp in _pendingLocalInputFramesByTick)
            {
                oldestQueuedTick = kvp.Key;
                break;
            }

            foreach (var kvp in _pendingLocalInputFramesByTick)
            {
                newestQueuedTick = kvp.Key;
            }
        }

        /// <summary>
        /// Builds the per-tick peer bitmask used by the diagnostics heatmap.
        /// </summary>
        private static uint BuildPeerMask(Dictionary<byte, DeterministicInputFrame> byPeer)
        {
            if (byPeer == null || byPeer.Count == 0)
            {
                return 0;
            }

            uint mask = 0;
            foreach (var peerId in byPeer.Keys)
            {
                mask |= (uint)(1 << peerId);
            }

            return mask;
        }

        /// <summary>
        /// Sends every queued local input frame in ascending tick order and tracks acknowledgments per frame.
        /// </summary>
        private void TrySendQueuedInputFrames()
        {
            if (_pendingLocalInputFramesByTick.Count == 0)
            {
                LogAckWait();
                return;
            }

            while (TryPopNextPendingLocalInputFrame(out var nextFrame))
            {
                TrackPendingAcknowledgments(nextFrame);
                SendInputFrame(nextFrame, false);
                QueueForResend(nextFrame.TargetTick, nextFrame);
            }

            LogAckWait();
        }

        /// <summary>
        /// Tracks acknowledgments for one outbound local input frame until every connected remote peer confirms receipt.
        /// </summary>
        private void TrackPendingAcknowledgments(in DeterministicInputFrame frame)
        {
            if (!_pendingAckStatesByTick.TryGetValue(frame.TargetTick, out var pendingAckState))
            {
                pendingAckState = new PendingAckState();
                _pendingAckStatesByTick[frame.TargetTick] = pendingAckState;
            }

            pendingAckState.Frame = frame;
            pendingAckState.PendingPeers.Clear();

            foreach (var peerId in _connectedPeers)
            {
                if (peerId == _localPeerId)
                {
                    continue;
                }

                pendingAckState.PendingPeers.Add(peerId);
            }
        }

        /// <summary>
        /// Tracks acknowledgments for the matching outbound local input frame.
        /// </summary>
        private void MarkInputFrameAcknowledged(byte peerId, uint tick, uint sequence)
        {
            if (!_pendingAckStatesByTick.TryGetValue(tick, out var pendingAckState)
                || pendingAckState.Frame.Sequence != sequence)
            {
                return;
            }

            pendingAckState.PendingPeers.Remove(peerId);
            if (pendingAckState.PendingPeers.Count == 0)
            {
                _pendingAckStatesByTick.Remove(tick);
            }
        }

        /// <summary>
        /// Removes a disconnected peer from every outstanding ack set so dead peers cannot block resend cleanup.
        /// </summary>
        private void RemovePeerFromPendingAcknowledgments(byte peerId)
        {
            if (_pendingAckStatesByTick.Count == 0)
            {
                return;
            }

            var completedTicks = new List<uint>();
            foreach (var kvp in _pendingAckStatesByTick)
            {
                kvp.Value.PendingPeers.Remove(peerId);
                if (kvp.Value.PendingPeers.Count == 0)
                {
                    completedTicks.Add(kvp.Key);
                }
            }

            for (int i = 0; i < completedTicks.Count; i++)
            {
                _pendingAckStatesByTick.Remove(completedTicks[i]);
            }
        }

        /// <summary>
        /// Returns the oldest frame that is still waiting on at least one remote acknowledgment.
        /// </summary>
        private bool TryGetOldestPendingAckState(out PendingAckState pendingAckState)
        {
            foreach (var kvp in _pendingAckStatesByTick)
            {
                pendingAckState = kvp.Value;
                return true;
            }

            pendingAckState = null;
            return false;
        }

        /// <summary>
        /// Throttles diagnostics while the sender still has one or more frames awaiting acknowledgments.
        /// </summary>
        private void LogAckWait()
        {
            if (!TryGetOldestPendingAckState(out var pendingAckState))
            {
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (now < _nextAckWaitLogTime)
            {
                return;
            }

            _nextAckWaitLogTime = now + InputFrameDiagnosticIntervalSeconds;
            Debug.Log($"[CommandLink][AckWait] waitingFor={FormatPeers(pendingAckState.PendingPeers)} tick={pendingAckState.Frame.TargetTick} sequence={pendingAckState.Frame.Sequence} queuedLocal={_pendingLocalInputFramesByTick.Count} pendingTicks={FormatPendingLocalTicks()} outstandingAckFrames={_pendingAckStatesByTick.Count}");
        }

        /// <summary>
        /// Removes resend entries up to the acknowledged sequence for the specified tick.
        /// </summary>
        private void ClearAckedResends(uint tick, uint sequence)
        {
            if (!_pendingResendsByTick.TryGetValue(tick, out var frames))
            {
                return;
            }

            frames.RemoveAll(frame => frame.Sequence <= sequence);
            if (frames.Count == 0)
            {
                _pendingResendsByTick.Remove(tick);
            }
        }

        /// <summary>
        /// Wraps and transmits one deterministic input frame to the transport layer.
        /// </summary>
        private void SendInputFrame(in DeterministicInputFrame frame, bool isResend)
        {
            var message = new InputFrameMessage
            {
                PeerId = frame.PeerId,
                Tick = frame.TargetTick,
                Sequence = frame.Sequence,
                Payload = CopyTo512(frame.Payload)
            };
            _inputFramesSent++;
            CommandLinkDiagnosticsService.RecordLocalFrameSent(frame.PeerId, frame.TargetTick, frame.Sequence, isResend);
            SendMessage(message);
        }

        private void SendMessage(in JoinRequestMessage message)
        {
            var payload = new FixedList512Bytes<byte>();
            CommandLinkMessageSerializer.TrySerializeJoinRequest(message, ref payload);
            SendPayload(payload, _hostPeerId);
        }

        private void SendMessage(in JoinAcceptMessage message, byte targetPeerId)
        {
            var payload = new FixedList512Bytes<byte>();
            CommandLinkMessageSerializer.TrySerializeJoinAccept(message, ref payload);
            SendPayload(payload, targetPeerId);
        }

        private void SendMessage(in SessionStartMessage message)
        {
            var payload = new FixedList512Bytes<byte>();
            CommandLinkMessageSerializer.TrySerializeSessionStart(message, ref payload);
            SendPayload(payload, 0);
        }

        private void SendMessage(in InputAckMessage message, byte targetPeerId)
        {
            var payload = new FixedList512Bytes<byte>();
            CommandLinkMessageSerializer.TrySerializeInputAck(message, ref payload);
            SendPayload(payload, targetPeerId);
        }

        private void SendMessage(in ChecksumMessage message)
        {
            var payload = new FixedList512Bytes<byte>();
            CommandLinkMessageSerializer.TrySerializeChecksum(message, ref payload);
            SendPayload(payload, 0);
        }

        private void SendMessage(in DisconnectNoticeMessage message)
        {
            var payload = new FixedList512Bytes<byte>();
            CommandLinkMessageSerializer.TrySerializeDisconnect(message, ref payload);
            SendPayload(payload, 0);
        }

        private void SendMessage(in ReadyMessage message)
        {
            var payload = new FixedList512Bytes<byte>();
            CommandLinkMessageSerializer.TrySerializeReady(message, ref payload);
            SendPayload(payload, 0);
        }

        private void SendMessage(in InputFrameMessage message)
        {
            var payload = new FixedList512Bytes<byte>();
            CommandLinkMessageSerializer.TrySerializeInputFrame(message, ref payload);
            SendPayload(payload, 0);
        }

        /// <summary>
        /// Serializes and sends the supplied payload to the requested peer id or broadcast target.
        /// </summary>
        private void SendPayload(in FixedList512Bytes<byte> payload, byte targetPeerId)
        {
            var packet = new CommandLinkPacket
            {
                PeerId = _localPeerId,
                Payload = payload
            };

            _driver.Send(targetPeerId, packet);
        }

        /// <summary>
        /// Pushes the resolved input frame into the simulation inbox immediately before the tick runs.
        /// </summary>
        private void OnPreTick(uint tick)
        {
            ObserveTick(tick);

            if (!TryGetResolvedFrame(tick, out var resolvedFrame))
            {
                Debug.LogWarning($"[CommandLink] Missing resolved frame on pre-tick {tick}.");
                return;
            }

            SimulationCommandInboxBridge.ApplyResolvedFrame(tick, resolvedFrame);
        }

        /// <summary>
        /// Computes and broadcasts periodic checksums after the simulation finishes the tick.
        /// </summary>
        private void OnPostTick(uint tick)
        {
            ObserveTick(tick);

            if (_sessionConfig.ChecksumIntervalTicks > 0 && tick % _sessionConfig.ChecksumIntervalTicks == 0)
            {
                if (!_runtimeHooks.TryComputeSimulationChecksum(out uint checksum))
                {
                    return;
                }

                BroadcastChecksum(tick, checksum);
            }
        }

        /// <summary>
        /// Reports a checksum mismatch along with the resolved input frame payload for the divergent tick.
        /// </summary>
        private void ReportChecksumMismatch(byte remotePeerId, uint tick, uint localChecksum, uint remoteChecksum)
        {
            string frameHex = "missing";
            if (TryGetResolvedFrame(tick, out var resolvedFrame))
            {
                frameHex = ToHex(in resolvedFrame.PackedPayload);
            }

            string peers = FormatPeers(_connectedPeers);
            Debug.LogError($"[CommandLink][Desync] tick={tick} peer={remotePeerId} local={localChecksum} remote={remoteChecksum} connectedPeers=[{peers}] frame={frameHex}");
        }

        private static string FormatPeers(HashSet<byte> peers)
        {
            if (peers == null || peers.Count == 0)
            {
                return string.Empty;
            }

            var ordered = new List<byte>(peers);
            ordered.Sort();

            var sb = new StringBuilder(ordered.Count * 3);
            for (int i = 0; i < ordered.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append(ordered[i]);
            }

            return sb.ToString();
        }

        private static string ToHex(in FixedList512Bytes<byte> payload)
        {
            if (payload.Length == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder(payload.Length * 2);
            for (int i = 0; i < payload.Length; i++)
            {
                sb.Append(payload[i].ToString("X2"));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Prefers the transport sender id for peer-authored messages and logs when payload metadata disagrees.
        /// </summary>
        private static byte ResolveTransportPeerId(byte payloadPeerId, byte transportPeerId, CommandLinkMessageType messageType)
        {
            if (payloadPeerId != transportPeerId)
            {
                Debug.LogWarning($"[CommandLink] Ignoring {messageType} payload peer {payloadPeerId}; using transport peer {transportPeerId}.");
            }

            return transportPeerId;
        }

        /// <summary>
        /// Removes a peer from live session bookkeeping after either a graceful or transport-level disconnect.
        /// </summary>
        private void HandlePeerDisconnected(byte peerId, string source)
        {
            _connectedPeers.Remove(peerId);
            _readyPeers.Remove(peerId);
            RemovePeerFromPendingAcknowledgments(peerId);
            Debug.LogWarning($"[CommandLink] Peer {peerId} disconnected via {source}.");
        }

        /// <summary>
        /// Returns the number of peers that must contribute an input frame before a tick can advance.
        /// </summary>
        private int RequiredPeerCount()
        {
            return Math.Max(1, _connectedPeers.Count);
        }

        /// <summary>
        /// Returns the number of peers the host expects before it is allowed to start the session.
        /// </summary>
        private int ExpectedPeerCount()
        {
            return Math.Max(1, Math.Min(_config.MaxPeers, _sessionConfig.MaxPlayers));
        }

        /// <summary>
        /// Counts build placement commands present in the resolved frame cache or current partial input set for one tick.
        /// </summary>
        private int CountResolvedBuildCommands(uint tick)
        {
            if (!TryGetResolvedFrame(tick, out var resolvedFrame))
            {
                return 0;
            }

            DeterministicCommandPayload.DecodeResolvedFrame(resolvedFrame, DiagnosticMoveCommands, DiagnosticBuildCommands, DiagnosticRecruitCommands);
            return DiagnosticBuildCommands.Count;
        }

        /// <summary>
        /// Packs per-peer frames into the canonical resolved frame order shared by every client.
        /// </summary>
        private static ResolvedInputFrame BuildResolvedFrame(uint tick, Dictionary<byte, DeterministicInputFrame> byPeer)
        {
            var resolved = new ResolvedInputFrame
            {
                Tick = tick,
                PeerMask = 0,
                PackedPayload = new FixedList512Bytes<byte>()
            };

            var orderedPeers = new List<byte>(byPeer.Keys);
            orderedPeers.Sort();

            for (int pIndex = 0; pIndex < orderedPeers.Count; pIndex++)
            {
                byte peerId = orderedPeers[pIndex];
                var frame = byPeer[peerId];

                resolved.PeerMask |= (uint)(1 << peerId);
                resolved.PackedPayload.Add(peerId);
                resolved.PackedPayload.Add((byte)frame.Payload.Length);
                for (int i = 0; i < frame.Payload.Length; i++)
                {
                    resolved.PackedPayload.Add(frame.Payload[i]);
                }
            }

            return resolved;
        }

        private static FixedList128Bytes<byte> CopyTo128(in FixedList512Bytes<byte> source)
        {
            var destination = new FixedList128Bytes<byte>();
            int length = Math.Min(source.Length, destination.Capacity);
            for (int i = 0; i < length; i++)
            {
                destination.Add(source[i]);
            }

            return destination;
        }

        private static FixedList512Bytes<byte> CopyTo512(in FixedList128Bytes<byte> source)
        {
            var destination = new FixedList512Bytes<byte>();
            for (int i = 0; i < source.Length; i++)
            {
                destination.Add(source[i]);
            }

            return destination;
        }

        private static FixedList32Bytes<byte> ToFixedList(HashSet<byte> peerIds)
        {
            var result = new FixedList32Bytes<byte>();
            foreach (var peerId in peerIds)
            {
                if (result.Length >= result.Capacity)
                {
                    break;
                }

                result.Add(peerId);
            }

            return result;
        }

        /// <summary>
        /// Tracks the remaining remote peers that still need to acknowledge a sent local frame.
        /// </summary>
        private sealed class PendingAckState
        {
            public DeterministicInputFrame Frame;
            public HashSet<byte> PendingPeers = new HashSet<byte>();
        }

        /// <summary>
        /// Unhooks runner callbacks, notifies peers, and shuts down the underlying transport driver.
        /// </summary>
        public void Dispose()
        {
            var disconnect = new DisconnectNoticeMessage { PeerId = _localPeerId };
            SendMessage(disconnect);

            _runtimeHooks.RemovePreTick(OnPreTick);
            _runtimeHooks.RemovePostTick(OnPostTick);
            _runtimeHooks.ClearGateCheck(AllInputsReady);

            _driver.Shutdown();
            _state = LockstepSessionState.Closed;
            PushDiagnosticsState();
        }
    }
}





