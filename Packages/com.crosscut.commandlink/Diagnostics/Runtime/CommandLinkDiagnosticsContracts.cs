using System;

namespace CrossCut.CommandLink.Diagnostics
{
    /// <summary>
    /// Identifies the latest confirmed stage reached by a build command as it moves through CommandLink and lockstep.
    /// </summary>
    [Serializable]
    public enum BuildSyncStage
    {
        LocalIntentQueued = 0,
        PayloadPacked = 1,
        LocalFrameQueued = 2,
        LocalFrameSent = 3,
        FrameResent = 4,
        RemoteFrameReceived = 5,
        ResolvedFrameReady = 6,
        SimulationInbox = 7,
        Applied = 8,
        RejectedNotReady = 9,
        RejectedInvalidPlacement = 10,
        SpawnQueued = 11,
        MirrorCreated = 12,
        LinkFlushed = 13,
    }

    /// <summary>
    /// Summarizes the deterministic pending-intent queue so diagnostics can explain build starvation before packing.
    /// </summary>
    [Serializable]
    public struct PendingIntentSummary
    {
        /// <summary>
        /// Total number of currently queued deterministic intents.
        /// </summary>
        public int TotalIntentCount;

        /// <summary>
        /// Number of currently queued build placement intents.
        /// </summary>
        public int BuildIntentCount;

        /// <summary>
        /// One-based position of the first queued build placement intent, or zero when absent.
        /// </summary>
        public int FirstBuildIntentPosition;

        /// <summary>
        /// Building type of the first queued build placement intent, or zero when absent.
        /// </summary>
        public ushort FirstBuildBuildingTypeId;

        /// <summary>
        /// X coordinate of the first queued build placement intent, or zero when absent.
        /// </summary>
        public int FirstBuildTargetCellX;

        /// <summary>
        /// Y coordinate of the first queued build placement intent, or zero when absent.
        /// </summary>
        public int FirstBuildTargetCellY;

        /// <summary>
        /// Returns whether the pending-intent queue currently contains any build placement work.
        /// </summary>
        public bool HasBuildIntent => BuildIntentCount > 0;
    }

    /// <summary>
    /// One recorded tick in the rolling frame window used by the overlay and editor diagnostics.
    /// </summary>
    [Serializable]
    public struct FrameWindowEntry
    {
        public uint Tick;
        public uint PresentPeerMask;
        public int InputsPresent;
        public int RequiredPeerCount;
        public int BuildCommandCount;
        public int QueuedLocalCount;
        public int ResendBacklogCount;
        public int MissingInputs;
        public bool WaitingForAck;
        public bool WaitingForRemoteFrame;
        public bool HasPendingBuildIntent;
        public string StallReason;
    }

    /// <summary>
    /// Serializable snapshot of the rolling frame window history.
    /// </summary>
    [Serializable]
    public sealed class FrameWindowSnapshot
    {
        public FrameWindowEntry[] Entries = Array.Empty<FrameWindowEntry>();
    }

    /// <summary>
    /// Serializable summary of one build command's latest known synchronization state.
    /// </summary>
    [Serializable]
    public struct BuildSyncTraceRecord
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
        public int LatencyTicks;
        public int LocalIntentQueuePosition;
        public int QueuedLocalCount;
        public int ResendBacklogCount;
        public int MissingInputsAtStage;
        public string FailureReason;
        public string StageTimeline;
        public string InferredWaitReason;
    }

    /// <summary>
    /// Serializable top-level diagnostics snapshot consumed by the runtime overlay and editor window.
    /// </summary>
    [Serializable]
    public sealed class CommandLinkDiagnosticsSnapshot
    {
        public string SessionState = "None";
        public uint CurrentTick;
        public byte LocalPeerId;
        public byte HostPeerId;
        public int RequiredPeerCount;
        public string ConnectedPeers = string.Empty;
        public int QueuedLocalCount;
        public uint OldestQueuedTick;
        public uint NewestQueuedTick;
        public bool HasInFlightFrame;
        public uint InFlightTick;
        public uint InFlightSequence;
        public int InFlightInputsPresent;
        public int CurrentInputsPresent;
        public int CurrentBuildCommands;
        public int TargetInputsPresent;
        public int TargetBuildCommands;
        public int ResendBacklogCount;
        public int MissingInputsAtCurrentTick;
        public int PendingAckPeerCount;
        public string PendingAckPeers = string.Empty;
        public string PendingTicksSummary = string.Empty;
        public string InferredStallReason = "none";
        public PendingIntentSummary PendingIntentSummary;
        public FrameWindowSnapshot FrameWindow = new FrameWindowSnapshot();
        public BuildSyncTraceRecord[] BuildTraces = Array.Empty<BuildSyncTraceRecord>();
        public string LastUpdatedUtc = string.Empty;
    }
}
