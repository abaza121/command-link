using Unity.Collections;

namespace CrossCut.CommandLink
{
    public static class CommandLinkMessageSerializer
    {
        public static bool TrySerializeJoinRequest(in JoinRequestMessage message, ref FixedList512Bytes<byte> destination)
        {
            destination.Clear();
            destination.Add((byte)CommandLinkMessageType.JoinRequest);
            WriteUInt16(message.RequestedTickRate, ref destination);
            WriteUInt16(message.RequestedInputDelay, ref destination);
            return true;
        }

        public static bool TrySerializeJoinAccept(in JoinAcceptMessage message, ref FixedList512Bytes<byte> destination)
        {
            destination.Clear();
            destination.Add((byte)CommandLinkMessageType.JoinAccept);
            destination.Add(message.AssignedPeerId);
            destination.Add(message.HostPeerId);
            WriteUInt32(message.MatchSeed, ref destination);
            WriteUInt16(message.TickRate, ref destination);
            WriteUInt16(message.InputDelayTicks, ref destination);
            WriteUInt16(message.MaxPlayers, ref destination);
            WriteUInt16(message.ChecksumIntervalTicks, ref destination);
            return true;
        }

        public static bool TrySerializeSessionStart(in SessionStartMessage message, ref FixedList512Bytes<byte> destination)
        {
            destination.Clear();
            destination.Add((byte)CommandLinkMessageType.SessionStart);
            WriteUInt32(message.MatchSeed, ref destination);
            return true;
        }

        public static bool TrySerializeInputFrame(in InputFrameMessage message, ref FixedList512Bytes<byte> destination)
        {
            destination.Clear();
            destination.Add((byte)CommandLinkMessageType.InputFrame);
            destination.Add(message.PeerId);
            WriteUInt32(message.Tick, ref destination);
            WriteUInt32(message.Sequence, ref destination);
            WriteUInt16((ushort)message.Payload.Length, ref destination);
            for (int i = 0; i < message.Payload.Length; i++)
            {
                destination.Add(message.Payload[i]);
            }

            return true;
        }

        public static bool TrySerializeInputAck(in InputAckMessage message, ref FixedList512Bytes<byte> destination)
        {
            destination.Clear();
            destination.Add((byte)CommandLinkMessageType.InputAck);
            destination.Add(message.PeerId);
            WriteUInt32(message.AckedSequence, ref destination);
            WriteUInt32(message.AckedTick, ref destination);
            return true;
        }

        public static bool TrySerializeChecksum(in ChecksumMessage message, ref FixedList512Bytes<byte> destination)
        {
            destination.Clear();
            destination.Add((byte)CommandLinkMessageType.Checksum);
            destination.Add(message.PeerId);
            WriteUInt32(message.Tick, ref destination);
            WriteUInt32(message.Checksum, ref destination);
            return true;
        }

        public static bool TrySerializeDisconnect(in DisconnectNoticeMessage message, ref FixedList512Bytes<byte> destination)
        {
            destination.Clear();
            destination.Add((byte)CommandLinkMessageType.DisconnectNotice);
            destination.Add(message.PeerId);
            return true;
        }

        public static bool TrySerializeReady(in ReadyMessage message, ref FixedList512Bytes<byte> destination)
        {
            destination.Clear();
            destination.Add((byte)CommandLinkMessageType.Ready);
            destination.Add(message.PeerId);
            return true;
        }

        public static bool TryReadMessageType(in FixedList512Bytes<byte> source, out CommandLinkMessageType messageType)
        {
            messageType = default;
            if (source.Length < 1)
            {
                return false;
            }

            messageType = (CommandLinkMessageType)source[0];
            return true;
        }

        public static bool TryDeserializeJoinRequest(in FixedList512Bytes<byte> source, out JoinRequestMessage message)
        {
            message = default;
            if (!HasTypeAndSize(in source, CommandLinkMessageType.JoinRequest, 5)) return false;

            int index = 1;
            message.RequestedTickRate = ReadUInt16(in source, ref index);
            message.RequestedInputDelay = ReadUInt16(in source, ref index);
            return true;
        }

        public static bool TryDeserializeJoinAccept(in FixedList512Bytes<byte> source, out JoinAcceptMessage message)
        {
            message = default;
            if (!HasTypeAndSize(in source, CommandLinkMessageType.JoinAccept, 15)) return false;

            int index = 1;
            message.AssignedPeerId = source[index++];
            message.HostPeerId = source[index++];
            message.MatchSeed = ReadUInt32(in source, ref index);
            message.TickRate = ReadUInt16(in source, ref index);
            message.InputDelayTicks = ReadUInt16(in source, ref index);
            message.MaxPlayers = ReadUInt16(in source, ref index);
            message.ChecksumIntervalTicks = ReadUInt16(in source, ref index);
            return true;
        }

        public static bool TryDeserializeSessionStart(in FixedList512Bytes<byte> source, out SessionStartMessage message)
        {
            message = default;
            if (!HasTypeAndSize(in source, CommandLinkMessageType.SessionStart, 5)) return false;

            int index = 1;
            message.MatchSeed = ReadUInt32(in source, ref index);
            return true;
        }

        public static bool TryDeserializeInputFrame(in FixedList512Bytes<byte> source, out InputFrameMessage message)
        {
            message = default;
            if (!HasTypeAndSize(in source, CommandLinkMessageType.InputFrame, 12)) return false;

            int index = 1;
            message.PeerId = source[index++];
            message.Tick = ReadUInt32(in source, ref index);
            message.Sequence = ReadUInt32(in source, ref index);
            ushort length = ReadUInt16(in source, ref index);
            if (index + length > source.Length) return false;

            for (int i = 0; i < length; i++)
            {
                message.Payload.Add(source[index + i]);
            }

            return true;
        }

        public static bool TryDeserializeInputAck(in FixedList512Bytes<byte> source, out InputAckMessage message)
        {
            message = default;
            if (!HasTypeAndSize(in source, CommandLinkMessageType.InputAck, 10)) return false;

            int index = 1;
            message.PeerId = source[index++];
            message.AckedSequence = ReadUInt32(in source, ref index);
            message.AckedTick = ReadUInt32(in source, ref index);
            return true;
        }

        public static bool TryDeserializeChecksum(in FixedList512Bytes<byte> source, out ChecksumMessage message)
        {
            message = default;
            if (!HasTypeAndSize(in source, CommandLinkMessageType.Checksum, 10)) return false;

            int index = 1;
            message.PeerId = source[index++];
            message.Tick = ReadUInt32(in source, ref index);
            message.Checksum = ReadUInt32(in source, ref index);
            return true;
        }

        public static bool TryDeserializeDisconnect(in FixedList512Bytes<byte> source, out DisconnectNoticeMessage message)
        {
            message = default;
            if (!HasTypeAndSize(in source, CommandLinkMessageType.DisconnectNotice, 2)) return false;

            message.PeerId = source[1];
            return true;
        }

        public static bool TryDeserializeReady(in FixedList512Bytes<byte> source, out ReadyMessage message)
        {
            message = default;
            if (!HasTypeAndSize(in source, CommandLinkMessageType.Ready, 2)) return false;

            message.PeerId = source[1];
            return true;
        }

        private static bool HasTypeAndSize(in FixedList512Bytes<byte> source, CommandLinkMessageType type, int minimumLength)
        {
            return source.Length >= minimumLength && source[0] == (byte)type;
        }

        private static void WriteUInt16(ushort value, ref FixedList512Bytes<byte> destination)
        {
            destination.Add((byte)(value & 0xFF));
            destination.Add((byte)((value >> 8) & 0xFF));
        }

        private static ushort ReadUInt16(in FixedList512Bytes<byte> source, ref int index)
        {
            ushort value = (ushort)(source[index] | (source[index + 1] << 8));
            index += 2;
            return value;
        }

        private static void WriteUInt32(uint value, ref FixedList512Bytes<byte> destination)
        {
            destination.Add((byte)(value & 0xFF));
            destination.Add((byte)((value >> 8) & 0xFF));
            destination.Add((byte)((value >> 16) & 0xFF));
            destination.Add((byte)((value >> 24) & 0xFF));
        }

        private static uint ReadUInt32(in FixedList512Bytes<byte> source, ref int index)
        {
            uint value = source[index];
            value |= (uint)source[index + 1] << 8;
            value |= (uint)source[index + 2] << 16;
            value |= (uint)source[index + 3] << 24;
            index += 4;
            return value;
        }
    }
}
