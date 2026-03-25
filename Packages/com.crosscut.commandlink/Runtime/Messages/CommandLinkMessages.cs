using Unity.Collections;

namespace CrossCut.CommandLink
{
    public struct JoinRequestMessage
    {
        public ushort RequestedTickRate;
        public ushort RequestedInputDelay;
    }

    public struct JoinAcceptMessage
    {
        public byte AssignedPeerId;
        public byte HostPeerId;
        public uint MatchSeed;
        public ushort TickRate;
        public ushort InputDelayTicks;
        public ushort MaxPlayers;
        public ushort ChecksumIntervalTicks;
    }

    public struct SessionStartMessage
    {
        public uint MatchSeed;
    }

    public struct InputFrameMessage
    {
        public byte PeerId;
        public uint Tick;
        public uint Sequence;
        public FixedList512Bytes<byte> Payload;
    }

    public struct InputAckMessage
    {
        public byte PeerId;
        public uint AckedSequence;
        public uint AckedTick;
    }

    public struct ChecksumMessage
    {
        public byte PeerId;
        public uint Tick;
        public uint Checksum;
    }

    public struct DisconnectNoticeMessage
    {
        public byte PeerId;
    }

    public struct ReadyMessage
    {
        public byte PeerId;
    }
}
