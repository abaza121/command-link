namespace CrossCut.CommandLink
{
    /// <summary>
    /// Lockstep-facing API used by integration code to submit inputs, gate ticks,
    /// read resolved inputs, and broadcast checksums.
    /// </summary>
    public interface ILockstepCommandLinkBridge
    {
        void SubmitLocalInput(in DeterministicInputFrame localInput);
        bool AllInputsReady(uint tick);
        bool TryGetResolvedFrame(uint tick, out ResolvedInputFrame resolvedFrame);
        void BroadcastChecksum(uint tick, uint checksum);
    }
}
