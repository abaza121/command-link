namespace CrossCut.CommandLink
{
    /// <summary>
    /// Runtime peer progress snapshot for ack/replay/gate bookkeeping.
    /// </summary>
    public struct PeerSessionState
    {
        public byte PeerId;
        public PeerConnectionState ConnectionState;
        public uint LastReceivedTick;
        public uint LastAckedTick;
        public uint LastChecksumTick;
    }
}
