using Unity.Collections;

namespace CrossCut.CommandLink
{
    /// <summary>
    /// Per-peer deterministic input targeting a specific lockstep tick.
    /// </summary>
    public struct DeterministicInputFrame
    {
        public byte PeerId;
        public uint TargetTick;
        public uint Sequence;
        public FixedList128Bytes<byte> Payload;
    }
}
