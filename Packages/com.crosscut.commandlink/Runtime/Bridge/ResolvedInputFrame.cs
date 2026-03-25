using Unity.Collections;

namespace CrossCut.CommandLink
{
    /// <summary>
    /// Input frame resolved across required peers for a single simulation tick.
    /// PackedPayload format is serializer-defined to avoid sim-layer coupling.
    /// </summary>
    public struct ResolvedInputFrame
    {
        public uint Tick;
        public uint PeerMask;
        public FixedList512Bytes<byte> PackedPayload;
    }
}
