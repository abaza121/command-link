using System;
using System.Collections.Generic;

namespace CrossCut.CommandLink.Diagnostics
{
    /// <summary>
    /// Collects non-authoritative synchronization diagnostics for CommandLink without mutating lockstep behavior.
    /// </summary>
    public static class CommandLinkDiagnosticsService
    {
        private const int MaxFrameSamples = 128;
        private const int MaxBuildTraces = 64;

        private static readonly FrameWindowEntry[] FrameSamples = new FrameWindowEntry[MaxFrameSamples];
        private static readonly List<BuildTraceState> TraceStates = new List<BuildTraceState>(MaxBuildTraces);
        private static readonly List<ulong> PendingPackTraceIds = new List<ulong>(MaxBuildTraces);
        private static readonly List<ulong> PendingFrameTraceIds = new List<ulong>(MaxBuildTraces);
        private static readonly Dictionary<string, List<ulong>> TraceIdsByFrameKey = new Dictionary<string, List<ulong>>(MaxBuildTraces);
        private static readonly CommandLinkDiagnosticsSnapshot SnapshotBuffer = new CommandLinkDiagnosticsSnapshot();

        private static int _nextFrameSampleIndex;
        private static int _frameSampleCount;
        private static uint _lastRecordedFrameTick = uint.MaxValue;
        private static ulong _nextTraceId = 1;

        private static string _sessionState = "None";
        private static uint _currentTick;
        private static byte _localPeerId;
        private static byte _hostPeerId;
        private static int _requiredPeerCount;
        private static string _connectedPeers = string.Empty;
        private static int _queuedLocalCount;
        private static uint _oldestQueuedTick;
        private static uint _newestQueuedTick;
        private static bool _hasInFlightFrame;
        private static uint _inFlightTick;
        private static uint _inFlightSequence;
        private static int _inFlightInputsPresent;
        private static int _currentInputsPresent;
        private static int _currentBuildCommands;
        private static int _targetInputsPresent;
        private static int _targetBuildCommands;
        private static int _resendBacklogCount;
        private static int _missingInputsAtCurrentTick;
        private static int _pendingAckPeerCount;
        private static string _pendingAckPeers = string.Empty;
        private static string _pendingTicksSummary = string.Empty;
        private static string _inferredStallReason = "none";
        private static PendingIntentSummary _pendingIntentSummary;

        /// <summary>
        /// Clears all buffered diagnostics so a new match starts with a clean history.
        /// </summary>
        public static void Reset()
        {
            TraceStates.Clear();
            PendingPackTraceIds.Clear();
            PendingFrameTraceIds.Clear();
            TraceIdsByFrameKey.Clear();
            Array.Clear(FrameSamples, 0, FrameSamples.Length);

            _nextFrameSampleIndex = 0;
            _frameSampleCount = 0;
            _lastRecordedFrameTick = uint.MaxValue;
            _nextTraceId = 1;

            _sessionState = "None";
            _currentTick = 0;
            _localPeerId = 0;
            _hostPeerId = 0;
            _requiredPeerCount = 0;
            _connectedPeers = string.Empty;
            _queuedLocalCount = 0;
            _oldestQueuedTick = 0;
            _newestQueuedTick = 0;
            _hasInFlightFrame = false;
            _inFlightTick = 0;
            _inFlightSequence = 0;
            _inFlightInputsPresent = 0;
            _currentInputsPresent = 0;
            _currentBuildCommands = 0;
            _targetInputsPresent = 0;
            _targetBuildCommands = 0;
            _resendBacklogCount = 0;
            _missingInputsAtCurrentTick = 0;
            _pendingAckPeerCount = 0;
            _pendingAckPeers = string.Empty;
            _pendingTicksSummary = string.Empty;
            _inferredStallReason = "none";
            _pendingIntentSummary = default;
        }

        /// <summary>
        /// Records the latest session and queue state and appends a frame-window sample when the simulation tick advances.
        /// </summary>
        public static void RecordNetworkState(
            string sessionState,
            uint currentTick,
            byte localPeerId,
            byte hostPeerId,
            int requiredPeerCount,
            string connectedPeers,
            int queuedLocalCount,
            uint oldestQueuedTick,
            uint newestQueuedTick,
            bool hasInFlightFrame,
            uint inFlightTick,
            uint inFlightSequence,
            int inFlightInputsPresent,
            int currentInputsPresent,
            uint currentPeerMask,
            int currentBuildCommands,
            int targetInputsPresent,
            int targetBuildCommands,
            int resendBacklogCount,
            int missingInputsAtCurrentTick,
            int pendingAckPeerCount,
            string pendingAckPeers,
            string pendingTicksSummary,
            in PendingIntentSummary pendingIntentSummary)
        {
            _sessionState = sessionState ?? "None";
            _currentTick = currentTick;
            _localPeerId = localPeerId;
            _hostPeerId = hostPeerId;
            _requiredPeerCount = requiredPeerCount;
            _connectedPeers = connectedPeers ?? string.Empty;
            _queuedLocalCount = queuedLocalCount;
            _oldestQueuedTick = oldestQueuedTick;
            _newestQueuedTick = newestQueuedTick;
            _hasInFlightFrame = hasInFlightFrame;
            _inFlightTick = inFlightTick;
            _inFlightSequence = inFlightSequence;
            _inFlightInputsPresent = inFlightInputsPresent;
            _currentInputsPresent = currentInputsPresent;
            _currentBuildCommands = currentBuildCommands;
            _targetInputsPresent = targetInputsPresent;
            _targetBuildCommands = targetBuildCommands;
            _resendBacklogCount = resendBacklogCount;
            _missingInputsAtCurrentTick = missingInputsAtCurrentTick;
            _pendingAckPeerCount = pendingAckPeerCount;
            _pendingAckPeers = pendingAckPeers ?? string.Empty;
            _pendingTicksSummary = pendingTicksSummary ?? string.Empty;
            _pendingIntentSummary = pendingIntentSummary;
            _inferredStallReason = InferGlobalStallReason();

            if (_lastRecordedFrameTick != currentTick)
            {
                FrameSamples[_nextFrameSampleIndex] = new FrameWindowEntry
                {
                    Tick = currentTick,
                    PresentPeerMask = currentPeerMask,
                    InputsPresent = currentInputsPresent,
                    RequiredPeerCount = requiredPeerCount,
                    BuildCommandCount = currentBuildCommands,
                    QueuedLocalCount = queuedLocalCount,
                    ResendBacklogCount = resendBacklogCount,
                    MissingInputs = missingInputsAtCurrentTick,
                    WaitingForAck = hasInFlightFrame && pendingAckPeerCount > 0,
                    WaitingForRemoteFrame = missingInputsAtCurrentTick > 0,
                    HasPendingBuildIntent = pendingIntentSummary.HasBuildIntent,
                    StallReason = _inferredStallReason,
                };

                _nextFrameSampleIndex = (_nextFrameSampleIndex + 1) % MaxFrameSamples;
                _frameSampleCount = Math.Min(_frameSampleCount + 1, MaxFrameSamples);
                _lastRecordedFrameTick = currentTick;
            }

            UpdateActiveTraceWaitReasons();
        }

        /// <summary>
        /// Records that a new local build intent has entered the pending-intent queue.
        /// </summary>
        public static void RecordBuildIntentQueued(ushort buildingTypeId, int cellX, int cellY, in PendingIntentSummary pendingIntentSummary)
        {
            var trace = CreateTrace(buildingTypeId, cellX, cellY);
            trace.LocalIntentQueuePosition = Math.Max(1, pendingIntentSummary.BuildIntentCount);
            UpdateTraceStage(trace, BuildSyncStage.LocalIntentQueued, 0, 0, "local_intent_queued");
            PendingPackTraceIds.Add(trace.TraceId);
            _pendingIntentSummary = pendingIntentSummary;
        }

        /// <summary>
        /// Records that a queued build intent was packed into a deterministic payload.
        /// </summary>
        public static void RecordPayloadPacked(ushort buildingTypeId, int cellX, int cellY)
        {
            var trace = TakePendingTrace(PendingPackTraceIds, buildingTypeId, cellX, cellY)
                ?? FindOrCreateTrace(0, 0, buildingTypeId, cellX, cellY);

            UpdateTraceStage(trace, BuildSyncStage.PayloadPacked, 0, 0, "payload_packed");
            PendingFrameTraceIds.Add(trace.TraceId);
        }

        /// <summary>
        /// Records that a build-bearing payload was assigned a target tick and sequence in the local send queue.
        /// </summary>
        public static void RecordLocalFrameQueued(
            byte peerId,
            uint targetTick,
            uint sequence,
            ushort buildingTypeId,
            int cellX,
            int cellY,
            int queuedLocalCount)
        {
            var trace = TakePendingTrace(PendingFrameTraceIds, buildingTypeId, cellX, cellY)
                ?? FindOrCreateTrace(peerId, targetTick, buildingTypeId, cellX, cellY);

            trace.PeerId = peerId;
            trace.InputTargetTick = targetTick;
            trace.Sequence = sequence;
            trace.QueuedLocalCount = queuedLocalCount;

            UpdateTraceStage(trace, BuildSyncStage.LocalFrameQueued, targetTick, sequence, "frame_queued");
            AddTraceToFrameKey(trace);
        }

        /// <summary>
        /// Records a local frame send or resend so build traces can show transport progress.
        /// </summary>
        public static void RecordLocalFrameSent(byte peerId, uint targetTick, uint sequence, bool isResend)
        {
            foreach (var trace in FindTracesByFrameKey(peerId, targetTick, sequence))
            {
                trace.ResendBacklogCount = _resendBacklogCount;
                UpdateTraceStage(trace, isResend ? BuildSyncStage.FrameResent : BuildSyncStage.LocalFrameSent, targetTick, sequence, isResend ? "frame_resent" : "frame_sent");
            }
        }

        /// <summary>
        /// Records that a remote peer input frame carrying a build command was received from the transport.
        /// </summary>
        public static void RecordRemoteFrameReceived(byte peerId, uint targetTick, uint sequence, ushort buildingTypeId, int cellX, int cellY)
        {
            var trace = FindOrCreateTrace(peerId, targetTick, buildingTypeId, cellX, cellY);
            trace.PeerId = peerId;
            trace.InputTargetTick = targetTick;
            trace.Sequence = sequence;
            UpdateTraceStage(trace, BuildSyncStage.RemoteFrameReceived, targetTick, sequence, "remote_frame_received");
            AddTraceToFrameKey(trace);
        }

        /// <summary>
        /// Records that a build command exists in a resolved frame immediately before the simulation inbox is rebuilt.
        /// </summary>
        public static void RecordResolvedFrameReady(uint tick, byte peerId, ushort buildingTypeId, int cellX, int cellY)
        {
            var trace = FindOrCreateTrace(peerId, tick, buildingTypeId, cellX, cellY);
            UpdateTraceStage(trace, BuildSyncStage.ResolvedFrameReady, tick, trace.Sequence, "resolved_frame_ready");
        }

        /// <summary>
        /// Records that a resolved build command was copied into the simulation inbox for the current tick.
        /// </summary>
        public static void RecordSimulationInbox(uint tick, byte peerId, int playerId, ushort buildingTypeId, int cellX, int cellY)
        {
            var trace = FindOrCreateTrace(peerId, tick, buildingTypeId, cellX, cellY);
            trace.PlayerId = playerId;
            trace.SimulationTick = tick;
            trace.MissingInputsAtStage = _missingInputsAtCurrentTick;
            UpdateTraceStage(trace, BuildSyncStage.SimulationInbox, tick, trace.Sequence, "simulation_inbox");
        }

        /// <summary>
        /// Records the deterministic simulation-side build placement outcome.
        /// </summary>
        public static void RecordBuildResult(
            uint tick,
            byte peerId,
            int playerId,
            ushort buildingTypeId,
            int cellX,
            int cellY,
            BuildSyncStage stage,
            string failureReason)
        {
            var trace = FindOrCreateTrace(peerId, tick, buildingTypeId, cellX, cellY);
            trace.PlayerId = playerId;
            trace.SimulationTick = tick;
            trace.FailureReason = failureReason ?? string.Empty;
            trace.MissingInputsAtStage = _missingInputsAtCurrentTick;
            UpdateTraceStage(trace, stage, tick, trace.Sequence, string.IsNullOrWhiteSpace(failureReason) ? "simulation_result" : failureReason);
        }

        /// <summary>
        /// Records that a build spawn command has been observed while draining the shared spawn queue.
        /// </summary>
        public static void RecordSpawnQueued(uint tick, byte peerId, ushort buildingTypeId, int cellX, int cellY)
        {
            var trace = FindOrCreateTrace(peerId, tick, buildingTypeId, cellX, cellY);
            UpdateTraceStage(trace, BuildSyncStage.SpawnQueued, tick, trace.Sequence, "spawn_queued");
        }

        /// <summary>
        /// Records that the presentation mirror was created for a build spawn.
        /// </summary>
        public static void RecordMirrorCreated(uint tick, byte peerId, ushort buildingTypeId, int cellX, int cellY)
        {
            var trace = FindOrCreateTrace(peerId, tick, buildingTypeId, cellX, cellY);
            UpdateTraceStage(trace, BuildSyncStage.MirrorCreated, tick, trace.Sequence, "mirror_created");
        }

        /// <summary>
        /// Records that the simulation-to-presentation entity link was flushed back into the simulation world.
        /// </summary>
        public static void RecordLinkFlushed(uint tick, byte peerId, ushort buildingTypeId, int cellX, int cellY)
        {
            var trace = FindOrCreateTrace(peerId, tick, buildingTypeId, cellX, cellY);
            UpdateTraceStage(trace, BuildSyncStage.LinkFlushed, tick, trace.Sequence, "link_flushed");
        }

        /// <summary>
        /// Copies the latest diagnostics state into a serializable snapshot for UI and export tooling.
        /// </summary>
        public static CommandLinkDiagnosticsSnapshot CopySnapshot()
        {
            SnapshotBuffer.SessionState = _sessionState;
            SnapshotBuffer.CurrentTick = _currentTick;
            SnapshotBuffer.LocalPeerId = _localPeerId;
            SnapshotBuffer.HostPeerId = _hostPeerId;
            SnapshotBuffer.RequiredPeerCount = _requiredPeerCount;
            SnapshotBuffer.ConnectedPeers = _connectedPeers;
            SnapshotBuffer.QueuedLocalCount = _queuedLocalCount;
            SnapshotBuffer.OldestQueuedTick = _oldestQueuedTick;
            SnapshotBuffer.NewestQueuedTick = _newestQueuedTick;
            SnapshotBuffer.HasInFlightFrame = _hasInFlightFrame;
            SnapshotBuffer.InFlightTick = _inFlightTick;
            SnapshotBuffer.InFlightSequence = _inFlightSequence;
            SnapshotBuffer.InFlightInputsPresent = _inFlightInputsPresent;
            SnapshotBuffer.CurrentInputsPresent = _currentInputsPresent;
            SnapshotBuffer.CurrentBuildCommands = _currentBuildCommands;
            SnapshotBuffer.TargetInputsPresent = _targetInputsPresent;
            SnapshotBuffer.TargetBuildCommands = _targetBuildCommands;
            SnapshotBuffer.ResendBacklogCount = _resendBacklogCount;
            SnapshotBuffer.MissingInputsAtCurrentTick = _missingInputsAtCurrentTick;
            SnapshotBuffer.PendingAckPeerCount = _pendingAckPeerCount;
            SnapshotBuffer.PendingAckPeers = _pendingAckPeers;
            SnapshotBuffer.PendingTicksSummary = _pendingTicksSummary;
            SnapshotBuffer.InferredStallReason = _inferredStallReason;
            SnapshotBuffer.PendingIntentSummary = _pendingIntentSummary;
            SnapshotBuffer.FrameWindow.Entries = CopyFrameWindowEntries();
            SnapshotBuffer.BuildTraces = CopyTraceRecords();
            SnapshotBuffer.LastUpdatedUtc = DateTime.UtcNow.ToString("o");
            return SnapshotBuffer;
        }

        private static FrameWindowEntry[] CopyFrameWindowEntries()
        {
            var entries = new FrameWindowEntry[_frameSampleCount];
            for (int i = 0; i < _frameSampleCount; i++)
            {
                int sourceIndex = (_nextFrameSampleIndex - _frameSampleCount + i + MaxFrameSamples) % MaxFrameSamples;
                entries[i] = FrameSamples[sourceIndex];
            }

            return entries;
        }

        private static BuildSyncTraceRecord[] CopyTraceRecords()
        {
            var ordered = new List<BuildTraceState>(TraceStates.Count);
            for (int i = 0; i < TraceStates.Count; i++)
            {
                ordered.Add(TraceStates[i]);
            }

            ordered.Sort((a, b) =>
            {
                int tickCompare = b.LastObservedTick.CompareTo(a.LastObservedTick);
                return tickCompare != 0 ? tickCompare : b.TraceId.CompareTo(a.TraceId);
            });

            var records = new BuildSyncTraceRecord[ordered.Count];
            for (int i = 0; i < ordered.Count; i++)
            {
                var trace = ordered[i];
                int latencyTicks = 0;
                if (trace.InputTargetTick > 0)
                {
                    uint latencySourceTick = trace.SimulationTick > 0
                        ? trace.SimulationTick
                        : (trace.LastObservedTick >= trace.InputTargetTick ? trace.LastObservedTick : trace.InputTargetTick);
                    if (latencySourceTick >= trace.InputTargetTick)
                    {
                        latencyTicks = (int)(latencySourceTick - trace.InputTargetTick);
                    }
                }

                records[i] = new BuildSyncTraceRecord
                {
                    TraceId = trace.TraceId,
                    PeerId = trace.PeerId,
                    PlayerId = trace.PlayerId,
                    BuildingTypeId = trace.BuildingTypeId,
                    TargetCellX = trace.TargetCellX,
                    TargetCellY = trace.TargetCellY,
                    InputTargetTick = trace.InputTargetTick,
                    SimulationTick = trace.SimulationTick,
                    Sequence = trace.Sequence,
                    SourceTick = trace.SourceTick,
                    Stage = trace.Stage,
                    LatencyTicks = latencyTicks,
                    LocalIntentQueuePosition = trace.LocalIntentQueuePosition,
                    QueuedLocalCount = trace.QueuedLocalCount,
                    ResendBacklogCount = trace.ResendBacklogCount,
                    MissingInputsAtStage = trace.MissingInputsAtStage,
                    FailureReason = trace.FailureReason,
                    StageTimeline = trace.StageTimeline,
                    InferredWaitReason = trace.InferredWaitReason,
                };
            }

            return records;
        }

        private static BuildTraceState CreateTrace(ushort buildingTypeId, int cellX, int cellY)
        {
            if (TraceStates.Count >= MaxBuildTraces)
            {
                RemoveTraceState(TraceStates[0]);
            }

            var trace = new BuildTraceState
            {
                TraceId = _nextTraceId++,
                PeerId = 0,
                PlayerId = -1,
                BuildingTypeId = buildingTypeId,
                TargetCellX = cellX,
                TargetCellY = cellY,
                FailureReason = string.Empty,
                StageTimeline = string.Empty,
                InferredWaitReason = "none",
                SourceTick = _currentTick,
                LastObservedTick = _currentTick,
            };

            TraceStates.Add(trace);
            return trace;
        }

        private static void RemoveTraceState(BuildTraceState trace)
        {
            if (trace == null)
            {
                return;
            }

            TraceStates.Remove(trace);
            PendingPackTraceIds.Remove(trace.TraceId);
            PendingFrameTraceIds.Remove(trace.TraceId);

            var emptyKeys = new List<string>();
            foreach (var kvp in TraceIdsByFrameKey)
            {
                kvp.Value.Remove(trace.TraceId);
                if (kvp.Value.Count == 0)
                {
                    emptyKeys.Add(kvp.Key);
                }
            }

            for (int i = 0; i < emptyKeys.Count; i++)
            {
                TraceIdsByFrameKey.Remove(emptyKeys[i]);
            }
        }

        private static BuildTraceState TakePendingTrace(List<ulong> pendingTraceIds, ushort buildingTypeId, int cellX, int cellY)
        {
            for (int i = 0; i < pendingTraceIds.Count; i++)
            {
                var trace = FindTraceById(pendingTraceIds[i]);
                if (trace == null)
                {
                    pendingTraceIds.RemoveAt(i);
                    i--;
                    continue;
                }

                if (trace.BuildingTypeId != buildingTypeId
                    || trace.TargetCellX != cellX
                    || trace.TargetCellY != cellY)
                {
                    continue;
                }

                pendingTraceIds.RemoveAt(i);
                return trace;
            }

            return null;
        }

        private static BuildTraceState FindOrCreateTrace(byte peerId, uint inputTargetTick, ushort buildingTypeId, int cellX, int cellY)
        {
            for (int i = TraceStates.Count - 1; i >= 0; i--)
            {
                var trace = TraceStates[i];
                if (trace.PeerId != peerId
                    || trace.BuildingTypeId != buildingTypeId
                    || trace.TargetCellX != cellX
                    || trace.TargetCellY != cellY)
                {
                    continue;
                }

                if (inputTargetTick != 0 && trace.InputTargetTick != 0 && trace.InputTargetTick != inputTargetTick)
                {
                    continue;
                }

                return trace;
            }

            var created = CreateTrace(buildingTypeId, cellX, cellY);
            created.PeerId = peerId;
            created.InputTargetTick = inputTargetTick;
            return created;
        }

        private static BuildTraceState FindTraceById(ulong traceId)
        {
            for (int i = 0; i < TraceStates.Count; i++)
            {
                if (TraceStates[i].TraceId == traceId)
                {
                    return TraceStates[i];
                }
            }

            return null;
        }

        private static void AddTraceToFrameKey(BuildTraceState trace)
        {
            string frameKey = ComposeFrameKey(trace.PeerId, trace.InputTargetTick, trace.Sequence);
            if (!TraceIdsByFrameKey.TryGetValue(frameKey, out var traceIds))
            {
                traceIds = new List<ulong>(4);
                TraceIdsByFrameKey[frameKey] = traceIds;
            }

            if (!traceIds.Contains(trace.TraceId))
            {
                traceIds.Add(trace.TraceId);
            }
        }

        private static IEnumerable<BuildTraceState> FindTracesByFrameKey(byte peerId, uint targetTick, uint sequence)
        {
            string frameKey = ComposeFrameKey(peerId, targetTick, sequence);
            if (!TraceIdsByFrameKey.TryGetValue(frameKey, out var traceIds))
            {
                yield break;
            }

            for (int i = 0; i < traceIds.Count; i++)
            {
                var trace = FindTraceById(traceIds[i]);
                if (trace != null)
                {
                    yield return trace;
                }
            }
        }

        private static string ComposeFrameKey(byte peerId, uint targetTick, uint sequence)
        {
            return peerId + "|" + targetTick + "|" + sequence;
        }

        private static void UpdateTraceStage(BuildTraceState trace, BuildSyncStage stage, uint stageTick, uint sequence, string contextLabel)
        {
            if (trace == null)
            {
                return;
            }

            trace.Stage = stage;
            trace.Sequence = sequence != 0 ? sequence : trace.Sequence;
            trace.LastObservedTick = Math.Max(_currentTick, stageTick);
            trace.SourceTick = trace.SourceTick == 0 ? _currentTick : trace.SourceTick;
            trace.QueuedLocalCount = _queuedLocalCount;
            trace.ResendBacklogCount = _resendBacklogCount;
            trace.MissingInputsAtStage = _missingInputsAtCurrentTick;
            trace.StageTimeline = AppendStageTimeline(trace.StageTimeline, stage, stageTick, sequence, contextLabel);
            trace.InferredWaitReason = InferTraceWaitReason(trace);
        }

        private static string AppendStageTimeline(string existingTimeline, BuildSyncStage stage, uint tick, uint sequence, string contextLabel)
        {
            string entry = tick > 0
                ? $"{stage}@{tick}#{sequence}"
                : $"{stage}#{sequence}";

            if (!string.IsNullOrWhiteSpace(contextLabel))
            {
                entry += $"({contextLabel})";
            }

            return string.IsNullOrWhiteSpace(existingTimeline) ? entry : existingTimeline + " -> " + entry;
        }

        private static void UpdateActiveTraceWaitReasons()
        {
            for (int i = 0; i < TraceStates.Count; i++)
            {
                TraceStates[i].InferredWaitReason = InferTraceWaitReason(TraceStates[i]);
            }
        }

        private static string InferTraceWaitReason(BuildTraceState trace)
        {
            if (trace == null)
            {
                return "none";
            }

            if (trace.Stage == BuildSyncStage.LocalIntentQueued)
            {
                return "never_packed";
            }

            if (trace.Stage == BuildSyncStage.PayloadPacked)
            {
                return "waiting_for_frame_queue";
            }

            if (trace.Stage == BuildSyncStage.RemoteFrameReceived)
            {
                if (trace.InputTargetTick > _currentTick)
                {
                    return "waiting_for_target_tick";
                }

                if (_missingInputsAtCurrentTick > 0)
                {
                    return "waiting_for_resolved_frame";
                }
            }

            if (trace.Stage == BuildSyncStage.LocalFrameQueued
                || trace.Stage == BuildSyncStage.LocalFrameSent
                || trace.Stage == BuildSyncStage.FrameResent)
            {
                if (_hasInFlightFrame
                    && trace.InputTargetTick == _inFlightTick
                    && trace.Sequence == _inFlightSequence
                    && _pendingAckPeerCount > 0)
                {
                    return "waiting_for_ack";
                }

                if (trace.InputTargetTick != 0
                    && trace.InputTargetTick <= _currentTick
                    && _missingInputsAtCurrentTick > 0)
                {
                    return "waiting_for_remote_frame";
                }

                if (_hasInFlightFrame && trace.InputTargetTick > _inFlightTick)
                {
                    return "queued_behind_older_frame";
                }
            }

            return "none";
        }

        private static string InferGlobalStallReason()
        {
            if (_pendingIntentSummary.HasBuildIntent && _pendingIntentSummary.FirstBuildIntentPosition > 1)
            {
                return "build_waiting_in_pending_intents";
            }

            if (_hasInFlightFrame && _pendingAckPeerCount > 0)
            {
                return "waiting_for_ack";
            }

            if (_missingInputsAtCurrentTick > 0)
            {
                return "waiting_for_remote_frame";
            }

            if (_queuedLocalCount > 0)
            {
                return "queued_behind_older_frame";
            }

            return "none";
        }

        private sealed class BuildTraceState
        {
            public ulong TraceId;
            public byte PeerId;
            public int PlayerId;
            public ushort BuildingTypeId;
            public int TargetCellX;
            public int TargetCellY;
            public uint InputTargetTick;
            public uint SimulationTick;
            public uint Sequence;
            public uint SourceTick;
            public BuildSyncStage Stage;
            public int LocalIntentQueuePosition;
            public int QueuedLocalCount;
            public int ResendBacklogCount;
            public int MissingInputsAtStage;
            public string FailureReason;
            public string StageTimeline;
            public string InferredWaitReason;
            public uint LastObservedTick;
        }
    }
}
