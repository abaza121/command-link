using System.Collections.Generic;
using CrossCut.CommandLink.Diagnostics;
using Unity.Collections;
using Unity.Mathematics;

namespace CrossCut.CommandLink
{
    /// <summary>
    /// Enumerates the deterministic command kinds supported by the lockstep payload stream.
    /// </summary>
    public enum SimCommandType : byte
    {
        Move = 1,
        BuildPlace = 2,
        Recruit = 3,
    }

    /// <summary>
    /// Encodes one local move request before it is serialized into the per-frame payload.
    /// </summary>
    public struct MoveCommandIntent
    {
        public int TargetCellX;
        public int TargetCellY;
        public byte Queued;
        public FixedList64Bytes<uint> OrderedTargetIds;
    }

    /// <summary>
    /// Encodes one local build placement request before it is serialized into the per-frame payload.
    /// </summary>
    public struct BuildPlaceCommandIntent
    {
        public ushort BuildingTypeId;
        public int TargetCellX;
        public int TargetCellY;
        public byte Orientation;
    }

    /// <summary>
    /// Encodes one local recruit request before it is serialized into the per-frame payload.
    /// </summary>
    public struct RecruitCommandIntent
    {
        public uint ProducerSimNetId;
        public ushort UnitTypeId;
        public byte QueueMode;
    }

    /// <summary>
    /// Represents one decoded move command after the resolved peer payloads have been unpacked.
    /// </summary>
    public struct DecodedMoveCommand
    {
        public byte PeerId;
        public int2 TargetCell;
        public byte Queued;
        public uint SimNetId;
    }

    /// <summary>
    /// Represents one decoded build placement command after the resolved peer payloads have been unpacked.
    /// </summary>
    public struct DecodedBuildPlaceCommand
    {
        public byte PeerId;
        public ushort BuildingTypeId;
        public int2 TargetCell;
        public byte Orientation;
    }

    /// <summary>
    /// Represents one decoded recruit command after the resolved peer payloads have been unpacked.
    /// </summary>
    public struct DecodedRecruitCommand
    {
        public byte PeerId;
        public uint ProducerSimNetId;
        public ushort UnitTypeId;
        public byte QueueMode;
    }

    internal struct CommandIntentEnvelope
    {
        public SimCommandType Type;
        public MoveCommandIntent Move;
        public BuildPlaceCommandIntent BuildPlace;
        public RecruitCommandIntent Recruit;
    }

    /// <summary>
    /// Builds and decodes the compact deterministic payload format exchanged between lockstep peers each frame.
    /// </summary>
    public static class DeterministicCommandPayload
    {
        private const byte PayloadVersion = 1;
        private const int MaxCommandsPerFrame = 2;
        private const int MaxMoveTargets = 12;

        private static readonly Queue<CommandIntentEnvelope> PendingIntents = new Queue<CommandIntentEnvelope>(128);

        /// <summary>
        /// Queues a move intent that will be serialized into the next outbound local input frame.
        /// </summary>
        public static void EnqueueMove(int targetCellX, int targetCellY, bool queued, in FixedList64Bytes<uint> orderedTargetIds)
        {
            var intent = new MoveCommandIntent
            {
                TargetCellX = targetCellX,
                TargetCellY = targetCellY,
                Queued = queued ? (byte)1 : (byte)0,
                OrderedTargetIds = orderedTargetIds,
            };

            PendingIntents.Enqueue(new CommandIntentEnvelope
            {
                Type = SimCommandType.Move,
                Move = intent,
            });
        }

        /// <summary>
        /// Queues a build placement intent that will be serialized into the next outbound local input frame.
        /// </summary>
        public static void EnqueueBuildPlace(ushort buildingTypeId, int targetCellX, int targetCellY, byte orientation)
        {
            PendingIntents.Enqueue(new CommandIntentEnvelope
            {
                Type = SimCommandType.BuildPlace,
                BuildPlace = new BuildPlaceCommandIntent
                {
                    BuildingTypeId = buildingTypeId,
                    TargetCellX = targetCellX,
                    TargetCellY = targetCellY,
                    Orientation = orientation,
                },
            });

            CommandLinkDiagnosticsService.RecordBuildIntentQueued(buildingTypeId, targetCellX, targetCellY, CopyPendingIntentSummary());
        }

        /// <summary>
        /// Queues a recruit intent that will be serialized into the next outbound local input frame.
        /// </summary>
        public static void EnqueueRecruit(uint producerSimNetId, ushort unitTypeId, byte queueMode)
        {
            PendingIntents.Enqueue(new CommandIntentEnvelope
            {
                Type = SimCommandType.Recruit,
                Recruit = new RecruitCommandIntent
                {
                    ProducerSimNetId = producerSimNetId,
                    UnitTypeId = unitTypeId,
                    QueueMode = queueMode,
                },
            });
        }

        /// <summary>
        /// Writes the current pending-intent queue into the compact versioned payload sent for one local frame.
        /// </summary>
        public static void BuildPayload(ref FixedList128Bytes<byte> payload)
        {
            payload.Clear();
            payload.Add(PayloadVersion);
            payload.Add(0); // command count placeholder

            byte commandCount = 0;

            // Commands are packed from the front of the queue so starvation is visible through diagnostics summaries.
            while (PendingIntents.Count > 0 && commandCount < MaxCommandsPerFrame)
            {
                var envelope = PendingIntents.Peek();
                if (!TryEncodeCommand(envelope, ref payload))
                {
                    break;
                }

                if (envelope.Type == SimCommandType.BuildPlace)
                {
                    CommandLinkDiagnosticsService.RecordPayloadPacked(envelope.BuildPlace.BuildingTypeId, envelope.BuildPlace.TargetCellX, envelope.BuildPlace.TargetCellY);
                }

                PendingIntents.Dequeue();
                commandCount++;
            }

            payload[1] = commandCount;
        }

        /// <summary>
        /// Returns a read-only summary of the current pending-intent queue for diagnostics.
        /// </summary>
        public static PendingIntentSummary CopyPendingIntentSummary()
        {
            var summary = new PendingIntentSummary();
            int position = 0;

            foreach (var envelope in PendingIntents)
            {
                position++;
                summary.TotalIntentCount++;
                if (envelope.Type != SimCommandType.BuildPlace)
                {
                    continue;
                }

                summary.BuildIntentCount++;
                if (summary.FirstBuildIntentPosition != 0)
                {
                    continue;
                }

                summary.FirstBuildIntentPosition = position;
                summary.FirstBuildBuildingTypeId = envelope.BuildPlace.BuildingTypeId;
                summary.FirstBuildTargetCellX = envelope.BuildPlace.TargetCellX;
                summary.FirstBuildTargetCellY = envelope.BuildPlace.TargetCellY;
            }

            return summary;
        }

        /// <summary>
        /// Decodes the resolved peer payload stream back into per-command lists for simulation ingestion.
        /// </summary>
        public static void DecodeResolvedFrame(
            in ResolvedInputFrame resolvedFrame,
            List<DecodedMoveCommand> moveCommands,
            List<DecodedBuildPlaceCommand> buildPlaceCommands,
            List<DecodedRecruitCommand> recruitCommands)
        {
            moveCommands.Clear();
            buildPlaceCommands.Clear();
            recruitCommands.Clear();

            int index = 0;
            var packed = resolvedFrame.PackedPayload;

            while (index + 2 <= packed.Length)
            {
                byte peerId = packed[index++];
                int payloadLength = packed[index++];
                if (payloadLength <= 0 || index + payloadLength > packed.Length)
                {
                    break;
                }

                var payload = new FixedList128Bytes<byte>();
                int clampedLength = math.min(payloadLength, payload.Capacity);
                for (int i = 0; i < clampedLength; i++)
                {
                    payload.Add(packed[index + i]);
                }

                DecodePeerPayload(payload, peerId, moveCommands, buildPlaceCommands, recruitCommands);
                index += payloadLength;
            }
        }

        /// <summary>
        /// Decodes one local payload without requiring it to be wrapped in a resolved frame.
        /// </summary>
        public static void DecodeSinglePayload(
            in FixedList128Bytes<byte> payload,
            byte peerId,
            List<DecodedMoveCommand> moveCommands,
            List<DecodedBuildPlaceCommand> buildPlaceCommands,
            List<DecodedRecruitCommand> recruitCommands)
        {
            moveCommands.Clear();
            buildPlaceCommands.Clear();
            recruitCommands.Clear();
            DecodePeerPayload(payload, peerId, moveCommands, buildPlaceCommands, recruitCommands);
        }

        /// <summary>
        /// Dispatches one pending command envelope to the correct serializer for the outbound payload.
        /// </summary>
        private static bool TryEncodeCommand(CommandIntentEnvelope envelope, ref FixedList128Bytes<byte> payload)
        {
            switch (envelope.Type)
            {
                case SimCommandType.Move:
                    return TryEncodeMove(envelope.Move, ref payload);
                case SimCommandType.BuildPlace:
                    return TryEncodeBuildPlace(envelope.BuildPlace, ref payload);
                case SimCommandType.Recruit:
                    return TryEncodeRecruit(envelope.Recruit, ref payload);
                default:
                    return false;
            }
        }

        /// <summary>
        /// Encodes one move command into the outbound payload buffer.
        /// </summary>
        private static bool TryEncodeMove(MoveCommandIntent move, ref FixedList128Bytes<byte> payload)
        {
            int targetCount = math.min(move.OrderedTargetIds.Length, MaxMoveTargets);
            int requiredBytes = 1 + 2 + 2 + 1 + 1 + (targetCount * 4);
            if (payload.Length + requiredBytes > payload.Capacity)
            {
                return false;
            }

            payload.Add((byte)SimCommandType.Move);
            WriteInt16((short)move.TargetCellX, ref payload);
            WriteInt16((short)move.TargetCellY, ref payload);
            payload.Add(move.Queued);
            payload.Add((byte)targetCount);

            for (int i = 0; i < targetCount; i++)
            {
                WriteUInt32(move.OrderedTargetIds[i], ref payload);
            }

            return true;
        }

        /// <summary>
        /// Encodes one build placement command into the outbound payload buffer.
        /// </summary>
        private static bool TryEncodeBuildPlace(BuildPlaceCommandIntent buildPlace, ref FixedList128Bytes<byte> payload)
        {
            const int requiredBytes = 1 + 2 + 2 + 2 + 1;
            if (payload.Length + requiredBytes > payload.Capacity)
            {
                return false;
            }

            payload.Add((byte)SimCommandType.BuildPlace);
            WriteUInt16(buildPlace.BuildingTypeId, ref payload);
            WriteInt16((short)buildPlace.TargetCellX, ref payload);
            WriteInt16((short)buildPlace.TargetCellY, ref payload);
            payload.Add(buildPlace.Orientation);
            return true;
        }

        /// <summary>
        /// Encodes one recruit command into the outbound payload buffer.
        /// </summary>
        private static bool TryEncodeRecruit(RecruitCommandIntent recruit, ref FixedList128Bytes<byte> payload)
        {
            const int requiredBytes = 1 + 4 + 2 + 1;
            if (payload.Length + requiredBytes > payload.Capacity)
            {
                return false;
            }

            payload.Add((byte)SimCommandType.Recruit);
            WriteUInt32(recruit.ProducerSimNetId, ref payload);
            WriteUInt16(recruit.UnitTypeId, ref payload);
            payload.Add(recruit.QueueMode);
            return true;
        }

        /// <summary>
        /// Decodes one peer-authored payload into typed command records while preserving the peer id as issuer.
        /// </summary>
        private static void DecodePeerPayload(
            in FixedList128Bytes<byte> payload,
            byte peerId,
            List<DecodedMoveCommand> moveCommands,
            List<DecodedBuildPlaceCommand> buildPlaceCommands,
            List<DecodedRecruitCommand> recruitCommands)
        {
            if (payload.Length < 2)
            {
                return;
            }

            if (payload[0] != PayloadVersion)
            {
                return;
            }

            int index = 1;
            int commandCount = payload[index++];

            for (int cmd = 0; cmd < commandCount && index < payload.Length; cmd++)
            {
                var commandType = (SimCommandType)payload[index++];

                if (commandType == SimCommandType.Move)
                {
                    if (index + 6 > payload.Length)
                    {
                        break;
                    }

                    int targetX = ReadInt16(in payload, ref index);
                    int targetY = ReadInt16(in payload, ref index);
                    byte queued = payload[index++];
                    int targetCount = payload[index++];

                    for (int i = 0; i < targetCount; i++)
                    {
                        if (index + 4 > payload.Length)
                        {
                            break;
                        }

                        uint simNetId = ReadUInt32(in payload, ref index);
                        moveCommands.Add(new DecodedMoveCommand
                        {
                            PeerId = peerId,
                            TargetCell = new int2(targetX, targetY),
                            Queued = queued,
                            SimNetId = simNetId,
                        });
                    }

                    continue;
                }

                if (commandType == SimCommandType.BuildPlace)
                {
                    if (index + 7 > payload.Length)
                    {
                        break;
                    }

                    ushort buildingTypeId = ReadUInt16(in payload, ref index);
                    int targetX = ReadInt16(in payload, ref index);
                    int targetY = ReadInt16(in payload, ref index);
                    byte orientation = payload[index++];

                    buildPlaceCommands.Add(new DecodedBuildPlaceCommand
                    {
                        PeerId = peerId,
                        BuildingTypeId = buildingTypeId,
                        TargetCell = new int2(targetX, targetY),
                        Orientation = orientation,
                    });

                    continue;
                }

                if (commandType == SimCommandType.Recruit)
                {
                    if (index + 7 > payload.Length)
                    {
                        break;
                    }

                    uint producerSimNetId = ReadUInt32(in payload, ref index);
                    ushort unitTypeId = ReadUInt16(in payload, ref index);
                    byte queueMode = payload[index++];

                    recruitCommands.Add(new DecodedRecruitCommand
                    {
                        PeerId = peerId,
                        ProducerSimNetId = producerSimNetId,
                        UnitTypeId = unitTypeId,
                        QueueMode = queueMode,
                    });
                }
            }
        }

        /// <summary>
        /// Writes a signed 16-bit integer in little-endian order into the payload buffer.
        /// </summary>
        private static void WriteInt16(short value, ref FixedList128Bytes<byte> destination)
        {
            destination.Add((byte)(value & 0xFF));
            destination.Add((byte)((value >> 8) & 0xFF));
        }

        /// <summary>
        /// Reads a signed 16-bit integer in little-endian order from the payload buffer.
        /// </summary>
        private static short ReadInt16(in FixedList128Bytes<byte> source, ref int index)
        {
            short value = (short)(source[index] | (source[index + 1] << 8));
            index += 2;
            return value;
        }

        /// <summary>
        /// Writes an unsigned 16-bit integer in little-endian order into the payload buffer.
        /// </summary>
        private static void WriteUInt16(ushort value, ref FixedList128Bytes<byte> destination)
        {
            destination.Add((byte)(value & 0xFF));
            destination.Add((byte)((value >> 8) & 0xFF));
        }

        /// <summary>
        /// Reads an unsigned 16-bit integer in little-endian order from the payload buffer.
        /// </summary>
        private static ushort ReadUInt16(in FixedList128Bytes<byte> source, ref int index)
        {
            ushort value = (ushort)(source[index] | (source[index + 1] << 8));
            index += 2;
            return value;
        }

        /// <summary>
        /// Writes an unsigned 32-bit integer in little-endian order into the payload buffer.
        /// </summary>
        private static void WriteUInt32(uint value, ref FixedList128Bytes<byte> destination)
        {
            destination.Add((byte)(value & 0xFF));
            destination.Add((byte)((value >> 8) & 0xFF));
            destination.Add((byte)((value >> 16) & 0xFF));
            destination.Add((byte)((value >> 24) & 0xFF));
        }

        /// <summary>
        /// Reads an unsigned 32-bit integer in little-endian order from the payload buffer.
        /// </summary>
        private static uint ReadUInt32(in FixedList128Bytes<byte> source, ref int index)
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
