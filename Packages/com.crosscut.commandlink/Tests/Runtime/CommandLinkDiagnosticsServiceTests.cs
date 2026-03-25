using CrossCut.CommandLink.Diagnostics;
using NUnit.Framework;

namespace CrossCut.CommandLink.Tests
{
    public sealed class CommandLinkDiagnosticsServiceTests
    {
        [SetUp]
        public void SetUp()
        {
            CommandLinkDiagnosticsService.Reset();
        }

        [Test]
        public void ResetClearsSnapshotStateAndTraceBuffers()
        {
            var pendingIntentSummary = new PendingIntentSummary
            {
                TotalIntentCount = 2,
                BuildIntentCount = 1,
                FirstBuildIntentPosition = 2,
                FirstBuildBuildingTypeId = 7,
                FirstBuildTargetCellX = 3,
                FirstBuildTargetCellY = 4
            };

            CommandLinkDiagnosticsService.RecordNetworkState(
                sessionState: "Running",
                currentTick: 12,
                localPeerId: 1,
                hostPeerId: 0,
                requiredPeerCount: 2,
                connectedPeers: "0,1",
                queuedLocalCount: 1,
                oldestQueuedTick: 12,
                newestQueuedTick: 14,
                hasInFlightFrame: true,
                inFlightTick: 14,
                inFlightSequence: 22,
                inFlightInputsPresent: 1,
                currentInputsPresent: 2,
                currentPeerMask: 3,
                currentBuildCommands: 1,
                targetInputsPresent: 1,
                targetBuildCommands: 0,
                resendBacklogCount: 1,
                missingInputsAtCurrentTick: 0,
                pendingAckPeerCount: 1,
                pendingAckPeers: "0",
                pendingTicksSummary: "14",
                pendingIntentSummary: pendingIntentSummary);

            CommandLinkDiagnosticsService.RecordBuildIntentQueued(7, 3, 4, pendingIntentSummary);

            var populated = CommandLinkDiagnosticsService.CopySnapshot();
            Assert.That(populated.FrameWindow.Entries.Length, Is.EqualTo(1));
            Assert.That(populated.BuildTraces.Length, Is.EqualTo(1));
            Assert.That(populated.InferredStallReason, Is.EqualTo("build_waiting_in_pending_intents"));

            CommandLinkDiagnosticsService.Reset();

            var cleared = CommandLinkDiagnosticsService.CopySnapshot();
            Assert.That(cleared.SessionState, Is.EqualTo("None"));
            Assert.That(cleared.CurrentTick, Is.EqualTo(0u));
            Assert.That(cleared.ConnectedPeers, Is.EqualTo(string.Empty));
            Assert.That(cleared.InferredStallReason, Is.EqualTo("none"));
            Assert.That(cleared.FrameWindow.Entries, Is.Empty);
            Assert.That(cleared.BuildTraces, Is.Empty);
        }

        [Test]
        public void RecordNetworkStateDoesNotDuplicateSameTickAndRetainsLatest128Ticks()
        {
            var emptyPendingIntentSummary = default(PendingIntentSummary);

            CommandLinkDiagnosticsService.RecordNetworkState(
                sessionState: "Running",
                currentTick: 5,
                localPeerId: 0,
                hostPeerId: 0,
                requiredPeerCount: 2,
                connectedPeers: "0,1",
                queuedLocalCount: 0,
                oldestQueuedTick: 0,
                newestQueuedTick: 0,
                hasInFlightFrame: false,
                inFlightTick: 0,
                inFlightSequence: 0,
                inFlightInputsPresent: 0,
                currentInputsPresent: 1,
                currentPeerMask: 1,
                currentBuildCommands: 0,
                targetInputsPresent: 0,
                targetBuildCommands: 0,
                resendBacklogCount: 0,
                missingInputsAtCurrentTick: 1,
                pendingAckPeerCount: 0,
                pendingAckPeers: string.Empty,
                pendingTicksSummary: string.Empty,
                pendingIntentSummary: emptyPendingIntentSummary);

            CommandLinkDiagnosticsService.RecordNetworkState(
                sessionState: "Running",
                currentTick: 5,
                localPeerId: 0,
                hostPeerId: 0,
                requiredPeerCount: 2,
                connectedPeers: "0,1",
                queuedLocalCount: 3,
                oldestQueuedTick: 5,
                newestQueuedTick: 7,
                hasInFlightFrame: true,
                inFlightTick: 7,
                inFlightSequence: 9,
                inFlightInputsPresent: 1,
                currentInputsPresent: 2,
                currentPeerMask: 3,
                currentBuildCommands: 1,
                targetInputsPresent: 1,
                targetBuildCommands: 1,
                resendBacklogCount: 1,
                missingInputsAtCurrentTick: 0,
                pendingAckPeerCount: 1,
                pendingAckPeers: "1",
                pendingTicksSummary: "7",
                pendingIntentSummary: emptyPendingIntentSummary);

            var deduped = CommandLinkDiagnosticsService.CopySnapshot();
            Assert.That(deduped.FrameWindow.Entries.Length, Is.EqualTo(1));
            Assert.That(deduped.CurrentTick, Is.EqualTo(5u));
            Assert.That(deduped.QueuedLocalCount, Is.EqualTo(3));

            for (uint tick = 6; tick <= 133; tick++)
            {
                CommandLinkDiagnosticsService.RecordNetworkState(
                    sessionState: "Running",
                    currentTick: tick,
                    localPeerId: 0,
                    hostPeerId: 0,
                    requiredPeerCount: 2,
                    connectedPeers: "0,1",
                    queuedLocalCount: 0,
                    oldestQueuedTick: 0,
                    newestQueuedTick: 0,
                    hasInFlightFrame: false,
                    inFlightTick: 0,
                    inFlightSequence: 0,
                    inFlightInputsPresent: 0,
                    currentInputsPresent: 2,
                    currentPeerMask: 3,
                    currentBuildCommands: 0,
                    targetInputsPresent: 2,
                    targetBuildCommands: 0,
                    resendBacklogCount: 0,
                    missingInputsAtCurrentTick: 0,
                    pendingAckPeerCount: 0,
                    pendingAckPeers: string.Empty,
                    pendingTicksSummary: string.Empty,
                    pendingIntentSummary: emptyPendingIntentSummary);
            }

            var overflowed = CommandLinkDiagnosticsService.CopySnapshot();
            Assert.That(overflowed.FrameWindow.Entries.Length, Is.EqualTo(128));
            Assert.That(overflowed.FrameWindow.Entries[0].Tick, Is.EqualTo(6u));
            Assert.That(overflowed.FrameWindow.Entries[127].Tick, Is.EqualTo(133u));
        }

        [Test]
        public void InferGlobalStallReasonUsesExpectedPriorityOrder()
        {
            AssertStallReason(
                expectedReason: "build_waiting_in_pending_intents",
                hasInFlightFrame: true,
                missingInputsAtCurrentTick: 1,
                queuedLocalCount: 2,
                pendingAckPeerCount: 1,
                pendingIntentSummary: new PendingIntentSummary
                {
                    TotalIntentCount = 3,
                    BuildIntentCount = 1,
                    FirstBuildIntentPosition = 2
                });

            AssertStallReason(
                expectedReason: "waiting_for_ack",
                hasInFlightFrame: true,
                missingInputsAtCurrentTick: 1,
                queuedLocalCount: 2,
                pendingAckPeerCount: 1,
                pendingIntentSummary: default);

            AssertStallReason(
                expectedReason: "waiting_for_remote_frame",
                hasInFlightFrame: false,
                missingInputsAtCurrentTick: 1,
                queuedLocalCount: 2,
                pendingAckPeerCount: 0,
                pendingIntentSummary: default);

            AssertStallReason(
                expectedReason: "queued_behind_older_frame",
                hasInFlightFrame: false,
                missingInputsAtCurrentTick: 0,
                queuedLocalCount: 2,
                pendingAckPeerCount: 0,
                pendingIntentSummary: default);

            AssertStallReason(
                expectedReason: "none",
                hasInFlightFrame: false,
                missingInputsAtCurrentTick: 0,
                queuedLocalCount: 0,
                pendingAckPeerCount: 0,
                pendingIntentSummary: default);
        }

        [Test]
        public void BuildTraceLifecycleProducesOrderedTimelineAndAckWaitReason()
        {
            CommandLinkDiagnosticsService.RecordNetworkState(
                sessionState: "Running",
                currentTick: 5,
                localPeerId: 0,
                hostPeerId: 0,
                requiredPeerCount: 2,
                connectedPeers: "0,1",
                queuedLocalCount: 1,
                oldestQueuedTick: 7,
                newestQueuedTick: 7,
                hasInFlightFrame: true,
                inFlightTick: 7,
                inFlightSequence: 100,
                inFlightInputsPresent: 1,
                currentInputsPresent: 2,
                currentPeerMask: 3,
                currentBuildCommands: 0,
                targetInputsPresent: 1,
                targetBuildCommands: 1,
                resendBacklogCount: 0,
                missingInputsAtCurrentTick: 0,
                pendingAckPeerCount: 1,
                pendingAckPeers: "1",
                pendingTicksSummary: "7",
                pendingIntentSummary: default);

            var pendingIntentSummary = new PendingIntentSummary
            {
                TotalIntentCount = 1,
                BuildIntentCount = 1,
                FirstBuildIntentPosition = 1,
                FirstBuildBuildingTypeId = 9,
                FirstBuildTargetCellX = 3,
                FirstBuildTargetCellY = 4
            };

            CommandLinkDiagnosticsService.RecordBuildIntentQueued(9, 3, 4, pendingIntentSummary);
            CommandLinkDiagnosticsService.RecordPayloadPacked(9, 3, 4);
            CommandLinkDiagnosticsService.RecordLocalFrameQueued(1, 7, 100, 9, 3, 4, 1);
            CommandLinkDiagnosticsService.RecordLocalFrameSent(1, 7, 100, false);

            var snapshot = CommandLinkDiagnosticsService.CopySnapshot();
            Assert.That(snapshot.BuildTraces.Length, Is.EqualTo(1));

            var trace = snapshot.BuildTraces[0];
            Assert.That(trace.BuildingTypeId, Is.EqualTo((ushort)9));
            Assert.That(trace.TargetCellX, Is.EqualTo(3));
            Assert.That(trace.TargetCellY, Is.EqualTo(4));
            Assert.That(trace.Stage, Is.EqualTo(BuildSyncStage.LocalFrameSent));
            Assert.That(trace.InputTargetTick, Is.EqualTo(7u));
            Assert.That(trace.Sequence, Is.EqualTo(100u));
            Assert.That(trace.InferredWaitReason, Is.EqualTo("waiting_for_ack"));
            StringAssert.Contains("LocalIntentQueued#0(local_intent_queued)", trace.StageTimeline);
            StringAssert.Contains("PayloadPacked#0(payload_packed)", trace.StageTimeline);
            StringAssert.Contains("LocalFrameQueued@7#100(frame_queued)", trace.StageTimeline);
            StringAssert.Contains("LocalFrameSent@7#100(frame_sent)", trace.StageTimeline);
        }

        private static void AssertStallReason(
            string expectedReason,
            bool hasInFlightFrame,
            int missingInputsAtCurrentTick,
            int queuedLocalCount,
            int pendingAckPeerCount,
            in PendingIntentSummary pendingIntentSummary)
        {
            CommandLinkDiagnosticsService.Reset();
            CommandLinkDiagnosticsService.RecordNetworkState(
                sessionState: "Running",
                currentTick: 9,
                localPeerId: 0,
                hostPeerId: 0,
                requiredPeerCount: 2,
                connectedPeers: "0,1",
                queuedLocalCount: queuedLocalCount,
                oldestQueuedTick: queuedLocalCount > 0 ? 10u : 0u,
                newestQueuedTick: queuedLocalCount > 0 ? 12u : 0u,
                hasInFlightFrame: hasInFlightFrame,
                inFlightTick: hasInFlightFrame ? 10u : 0u,
                inFlightSequence: hasInFlightFrame ? 55u : 0u,
                inFlightInputsPresent: hasInFlightFrame ? 1 : 0,
                currentInputsPresent: missingInputsAtCurrentTick > 0 ? 1 : 2,
                currentPeerMask: missingInputsAtCurrentTick > 0 ? 1u : 3u,
                currentBuildCommands: 0,
                targetInputsPresent: 0,
                targetBuildCommands: 0,
                resendBacklogCount: 0,
                missingInputsAtCurrentTick: missingInputsAtCurrentTick,
                pendingAckPeerCount: pendingAckPeerCount,
                pendingAckPeers: pendingAckPeerCount > 0 ? "1" : string.Empty,
                pendingTicksSummary: queuedLocalCount > 0 ? "10,11,12" : string.Empty,
                pendingIntentSummary: pendingIntentSummary);

            Assert.That(CommandLinkDiagnosticsService.CopySnapshot().InferredStallReason, Is.EqualTo(expectedReason));
        }
    }
}
