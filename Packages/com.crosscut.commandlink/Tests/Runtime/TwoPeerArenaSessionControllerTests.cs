using CrossCut.CommandLink.Samples.TwoPeerArena;
using NUnit.Framework;

namespace CrossCut.CommandLink.Tests
{
    public sealed class TwoPeerArenaSessionControllerTests
    {
        [Test]
        public void ShouldDriveSimulationTicksOnlyWhenSessionIsRunning()
        {
            Assert.That(TwoPeerArenaSessionController.ShouldDriveSimulationTicks(LockstepSessionState.None), Is.False);
            Assert.That(TwoPeerArenaSessionController.ShouldDriveSimulationTicks(LockstepSessionState.WaitingForPeers), Is.False);
            Assert.That(TwoPeerArenaSessionController.ShouldDriveSimulationTicks(LockstepSessionState.Starting), Is.False);
            Assert.That(TwoPeerArenaSessionController.ShouldDriveSimulationTicks(LockstepSessionState.Closed), Is.False);
            Assert.That(TwoPeerArenaSessionController.ShouldDriveSimulationTicks(LockstepSessionState.Running), Is.True);
        }

        [Test]
        public void ClampBufferedTickAccumulatorPreservesMultiTickBacklogInsteadOfDroppingToOneTick()
        {
            float tickInterval = 0.05f;
            float accumulator = tickInterval * 4f;

            float clamped = TwoPeerArenaSessionController.ClampBufferedTickAccumulator(accumulator, tickInterval);

            Assert.That(clamped, Is.EqualTo(accumulator).Within(0.0001f));
            Assert.That(clamped, Is.GreaterThan(tickInterval));
        }

        [Test]
        public void ClampBufferedTickAccumulatorCapsBacklogToBoundedCatchUpWindow()
        {
            float tickInterval = 0.05f;
            float accumulator = 10f;

            float clamped = TwoPeerArenaSessionController.ClampBufferedTickAccumulator(accumulator, tickInterval);

            Assert.That(clamped, Is.EqualTo(tickInterval * 24f).Within(0.0001f));
        }

        [Test]
        public void ClampBufferedTickAccumulatorReturnsZeroForInvalidTickInterval()
        {
            Assert.That(TwoPeerArenaSessionController.ClampBufferedTickAccumulator(1f, 0f), Is.EqualTo(0f));
            Assert.That(TwoPeerArenaSessionController.ClampBufferedTickAccumulator(1f, -0.1f), Is.EqualTo(0f));
        }

        [Test]
        public void EstimateBufferedTickCountRoundsDownWholeTicks()
        {
            Assert.That(TwoPeerArenaSessionController.EstimateBufferedTickCount(0.19f, 0.05f), Is.EqualTo(3));
            Assert.That(TwoPeerArenaSessionController.EstimateBufferedTickCount(0.01f, 0.05f), Is.EqualTo(0));
        }

        [Test]
        public void IsBufferedTickBacklogAtCapDetectsSaturatedCatchUpWindow()
        {
            Assert.That(TwoPeerArenaSessionController.IsBufferedTickBacklogAtCap(1.2f, 0.05f), Is.True);
            Assert.That(TwoPeerArenaSessionController.IsBufferedTickBacklogAtCap(0.2f, 0.05f), Is.False);
        }

        [Test]
        public void BuildTickResilienceSummaryDescribesIdleCatchUpAndSteadyStates()
        {
            Assert.That(
                TwoPeerArenaSessionController.BuildTickResilienceSummary(LockstepSessionState.WaitingForPeers, 0f, 0.05f),
                Is.EqualTo("Tick Drive: idle until session reaches Running"));

            StringAssert.Contains(
                "catching up 3 buffered tick(s)",
                TwoPeerArenaSessionController.BuildTickResilienceSummary(LockstepSessionState.Running, 0.19f, 0.05f));

            StringAssert.Contains(
                "capped at 24 ticks",
                TwoPeerArenaSessionController.BuildTickResilienceSummary(LockstepSessionState.Running, 1.2f, 0.05f));

            Assert.That(
                TwoPeerArenaSessionController.BuildTickResilienceSummary(LockstepSessionState.Running, 0.01f, 0.05f),
                Is.EqualTo("Tick Drive: steady"));
        }
    }
}
