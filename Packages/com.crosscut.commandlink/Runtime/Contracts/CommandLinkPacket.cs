using Unity.Collections;

namespace CrossCut.CommandLink
{
    /// <summary>
    /// Distinguishes transport-level session events from normal data payload packets.
    /// </summary>
    public enum CommandLinkPacketKind : byte
    {
        Data = 0,
        TransportDisconnect = 1
    }

    /// <summary>
    /// Minimal transport packet envelope used by abstractions before message-type specializations are added.
    /// </summary>
    public struct CommandLinkPacket
    {
        public CommandLinkPacketKind Kind;
        public byte PeerId;
        public FixedList512Bytes<byte> Payload;
    }
}
