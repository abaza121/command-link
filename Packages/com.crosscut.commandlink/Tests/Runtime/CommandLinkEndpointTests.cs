using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;

namespace CrossCut.CommandLink.Tests
{
    public sealed class CommandLinkEndpointTests
    {
        [Test]
        public void EndpointIsValidWhenAddressAndPortAreSet()
        {
            var endpoint = new CommandLinkEndpoint
            {
                Address = "127.0.0.1",
                Port = 7777
            };

            Assert.That(endpoint.IsValid(), Is.True);
            Assert.That(endpoint.ToString(), Is.EqualTo("127.0.0.1:7777"));
        }

        [Test]
        public void EndpointIsInvalidWhenAddressIsMissing()
        {
            var endpoint = new CommandLinkEndpoint
            {
                Address = " ",
                Port = 7777
            };

            Assert.That(endpoint.IsValid(), Is.False);
        }

        [Test]
        public void ResolvedFrameIsRebuiltWhenLaterPeerInputArrivesAfterPartialCache()
        {
            ICommandLinkRuntimeHooks previousHooks = CommandLinkRuntimeRegistry.RuntimeHooks;
            var runtimeHooks = new FakeRuntimeHooks();
            var driver = new FakeNetworkDriver();
            var driverFactory = new FakeNetworkDriverFactory(driver);

            CommandLinkRuntimeRegistry.RuntimeHooks = runtimeHooks;

            var config = CommandLinkConfig.Default;
            config.IsHost = true;
            config.MaxPeers = 2;

            var sessionConfig = LockstepSessionConfig.Default;
            sessionConfig.InputDelayTicks = 0;
            sessionConfig.MaxPlayers = 2;

            try
            {
                using var engine = new CommandLinkNetworkEngine(
                    config,
                    sessionConfig,
                    driverFactory,
                    new FakeEndpointProvider());

                engine.AllInputsReady(5);
                var localInput = new DeterministicInputFrame();
                localInput.Payload.Add(0x2A);
                engine.SubmitLocalInput(localInput);

                Assert.That(engine.TryGetResolvedFrame(5, out var partialFrame), Is.True);
                Assert.That(partialFrame.PeerMask, Is.EqualTo(1u));
                CollectionAssert.AreEqual(new byte[] { 0 }, ExtractPeerIds(partialFrame));

                driver.Enqueue(CreateInputFramePacket(packetPeerId: 1, payloadPeerId: 1, tick: 5, sequence: 42, payloadValue: 0x7B));
                engine.Poll();

                Assert.That(engine.TryGetResolvedFrame(5, out var rebuiltFrame), Is.True);
                Assert.That(rebuiltFrame.PeerMask, Is.EqualTo(3u));
                Assert.That(rebuiltFrame.PackedPayload.Length, Is.GreaterThan(partialFrame.PackedPayload.Length));
                CollectionAssert.AreEqual(new byte[] { 0, 1 }, ExtractPeerIds(rebuiltFrame));
            }
            finally
            {
                CommandLinkRuntimeRegistry.RuntimeHooks = previousHooks;
            }
        }

        [Test]
        public void SpoofedInputFrameUsesTransportPeerIdInsteadOfPayloadPeerId()
        {
            ICommandLinkRuntimeHooks previousHooks = CommandLinkRuntimeRegistry.RuntimeHooks;
            var runtimeHooks = new FakeRuntimeHooks();
            var driver = new FakeNetworkDriver();
            var driverFactory = new FakeNetworkDriverFactory(driver);

            CommandLinkRuntimeRegistry.RuntimeHooks = runtimeHooks;

            var config = CommandLinkConfig.Default;
            config.IsHost = true;
            config.MaxPeers = 2;

            var sessionConfig = LockstepSessionConfig.Default;
            sessionConfig.InputDelayTicks = 0;
            sessionConfig.MaxPlayers = 2;

            try
            {
                using var engine = new CommandLinkNetworkEngine(
                    config,
                    sessionConfig,
                    driverFactory,
                    new FakeEndpointProvider());

                engine.AllInputsReady(5);
                var localInput = new DeterministicInputFrame();
                localInput.Payload.Add(0x2A);
                engine.SubmitLocalInput(localInput);

                driver.Enqueue(CreateInputFramePacket(packetPeerId: 1, payloadPeerId: 0, tick: 5, sequence: 42, payloadValue: 0x7B));
                engine.Poll();

                Assert.That(engine.TryGetResolvedFrame(5, out var resolvedFrame), Is.True);
                Assert.That(resolvedFrame.PeerMask, Is.EqualTo(3u));
                CollectionAssert.AreEqual(new byte[] { 0, 1 }, ExtractPeerIds(resolvedFrame));
            }
            finally
            {
                CommandLinkRuntimeRegistry.RuntimeHooks = previousHooks;
            }
        }

        [Test]
        public void ReadyAndDisconnectMessagesUseTransportPeerIdInsteadOfPayloadPeerId()
        {
            ICommandLinkRuntimeHooks previousHooks = CommandLinkRuntimeRegistry.RuntimeHooks;
            var runtimeHooks = new FakeRuntimeHooks();
            var driver = new FakeNetworkDriver();
            var driverFactory = new FakeNetworkDriverFactory(driver);

            CommandLinkRuntimeRegistry.RuntimeHooks = runtimeHooks;

            var config = CommandLinkConfig.Default;
            config.IsHost = true;
            config.MaxPeers = 2;

            var sessionConfig = LockstepSessionConfig.Default;
            sessionConfig.MaxPlayers = 2;

            try
            {
                using var engine = new CommandLinkNetworkEngine(
                    config,
                    sessionConfig,
                    driverFactory,
                    new FakeEndpointProvider());

                engine.SignalReady();
                driver.Enqueue(CreateJoinRequestPacket(packetPeerId: 1));
                engine.Poll();

                driver.Enqueue(CreateReadyPacket(packetPeerId: 1, payloadPeerId: 7));
                engine.Poll();

                CommandLinkSessionState startedState = engine.SessionState;
                CollectionAssert.AreEquivalent(new byte[] { 0, 1 }, CopyPeers(startedState.ReadyPeerIds));
                CollectionAssert.DoesNotContain(CopyPeers(startedState.ReadyPeerIds), (byte)7);

                driver.Enqueue(CreateDisconnectPacket(packetPeerId: 1, payloadPeerId: 0));
                engine.Poll();

                CommandLinkSessionState disconnectedState = engine.SessionState;
                CollectionAssert.AreEquivalent(new byte[] { 0 }, CopyPeers(disconnectedState.ConnectedPeerIds));
            }
            finally
            {
                CommandLinkRuntimeRegistry.RuntimeHooks = previousHooks;
            }
        }

        [Test]
        public void TransportDisconnectEventClearsConnectedReadyAndPendingAckState()
        {
            ICommandLinkRuntimeHooks previousHooks = CommandLinkRuntimeRegistry.RuntimeHooks;
            var runtimeHooks = new FakeRuntimeHooks();
            var driver = new FakeNetworkDriver();
            var driverFactory = new FakeNetworkDriverFactory(driver);

            CommandLinkRuntimeRegistry.RuntimeHooks = runtimeHooks;

            var config = CommandLinkConfig.Default;
            config.IsHost = true;
            config.MaxPeers = 2;

            var sessionConfig = LockstepSessionConfig.Default;
            sessionConfig.InputDelayTicks = 0;
            sessionConfig.MaxPlayers = 2;

            try
            {
                using var engine = new CommandLinkNetworkEngine(
                    config,
                    sessionConfig,
                    driverFactory,
                    new FakeEndpointProvider());

                engine.SignalReady();
                driver.Enqueue(CreateJoinRequestPacket(packetPeerId: 1));
                engine.Poll();

                driver.Enqueue(CreateReadyPacket(packetPeerId: 1, payloadPeerId: 1));
                engine.Poll();

                engine.AllInputsReady(5);
                var localInput = new DeterministicInputFrame();
                localInput.Payload.Add(0x44);
                engine.SubmitLocalInput(localInput);
                engine.Poll();

                var beforeDisconnect = engine.CopyDiagnosticsSnapshot();
                Assert.That(beforeDisconnect.PendingAckPeerCount, Is.EqualTo(1));
                CollectionAssert.AreEquivalent(new byte[] { 0, 1 }, CopyPeers(engine.SessionState.ConnectedPeerIds));
                CollectionAssert.AreEquivalent(new byte[] { 0, 1 }, CopyPeers(engine.SessionState.ReadyPeerIds));

                driver.Enqueue(CreateTransportDisconnectPacket(peerId: 1));
                engine.Poll();

                var afterDisconnect = engine.CopyDiagnosticsSnapshot();
                Assert.That(afterDisconnect.PendingAckPeerCount, Is.EqualTo(0));
                CollectionAssert.AreEquivalent(new byte[] { 0 }, CopyPeers(engine.SessionState.ConnectedPeerIds));
                CollectionAssert.AreEquivalent(new byte[] { 0 }, CopyPeers(engine.SessionState.ReadyPeerIds));
            }
            finally
            {
                CommandLinkRuntimeRegistry.RuntimeHooks = previousHooks;
            }
        }

        [Test]
        public void SerializerRoundTripsEveryMessageType()
        {
            var joinRequest = new JoinRequestMessage
            {
                RequestedTickRate = 30,
                RequestedInputDelay = 4
            };
            var joinRequestPayload = SerializePayload((ref FixedList512Bytes<byte> payload) =>
                CommandLinkMessageSerializer.TrySerializeJoinRequest(joinRequest, ref payload));
            Assert.That(CommandLinkMessageSerializer.TryReadMessageType(joinRequestPayload, out var joinRequestType), Is.True);
            Assert.That(joinRequestType, Is.EqualTo(CommandLinkMessageType.JoinRequest));
            Assert.That(CommandLinkMessageSerializer.TryDeserializeJoinRequest(joinRequestPayload, out var deserializedJoinRequest), Is.True);
            Assert.That(deserializedJoinRequest.RequestedTickRate, Is.EqualTo(joinRequest.RequestedTickRate));
            Assert.That(deserializedJoinRequest.RequestedInputDelay, Is.EqualTo(joinRequest.RequestedInputDelay));

            var joinAccept = new JoinAcceptMessage
            {
                AssignedPeerId = 2,
                HostPeerId = 0,
                MatchSeed = 123456u,
                TickRate = 20,
                InputDelayTicks = 3,
                MaxPlayers = 4,
                ChecksumIntervalTicks = 8
            };
            var joinAcceptPayload = SerializePayload((ref FixedList512Bytes<byte> payload) =>
                CommandLinkMessageSerializer.TrySerializeJoinAccept(joinAccept, ref payload));
            Assert.That(CommandLinkMessageSerializer.TryReadMessageType(joinAcceptPayload, out var joinAcceptType), Is.True);
            Assert.That(joinAcceptType, Is.EqualTo(CommandLinkMessageType.JoinAccept));
            Assert.That(CommandLinkMessageSerializer.TryDeserializeJoinAccept(joinAcceptPayload, out var deserializedJoinAccept), Is.True);
            Assert.That(deserializedJoinAccept.AssignedPeerId, Is.EqualTo(joinAccept.AssignedPeerId));
            Assert.That(deserializedJoinAccept.HostPeerId, Is.EqualTo(joinAccept.HostPeerId));
            Assert.That(deserializedJoinAccept.MatchSeed, Is.EqualTo(joinAccept.MatchSeed));
            Assert.That(deserializedJoinAccept.TickRate, Is.EqualTo(joinAccept.TickRate));
            Assert.That(deserializedJoinAccept.InputDelayTicks, Is.EqualTo(joinAccept.InputDelayTicks));
            Assert.That(deserializedJoinAccept.MaxPlayers, Is.EqualTo(joinAccept.MaxPlayers));
            Assert.That(deserializedJoinAccept.ChecksumIntervalTicks, Is.EqualTo(joinAccept.ChecksumIntervalTicks));

            var sessionStart = new SessionStartMessage
            {
                MatchSeed = 987654u
            };
            var sessionStartPayload = SerializePayload((ref FixedList512Bytes<byte> payload) =>
                CommandLinkMessageSerializer.TrySerializeSessionStart(sessionStart, ref payload));
            Assert.That(CommandLinkMessageSerializer.TryReadMessageType(sessionStartPayload, out var sessionStartType), Is.True);
            Assert.That(sessionStartType, Is.EqualTo(CommandLinkMessageType.SessionStart));
            Assert.That(CommandLinkMessageSerializer.TryDeserializeSessionStart(sessionStartPayload, out var deserializedSessionStart), Is.True);
            Assert.That(deserializedSessionStart.MatchSeed, Is.EqualTo(sessionStart.MatchSeed));

            var inputFrame = new InputFrameMessage
            {
                PeerId = 3,
                Tick = 44,
                Sequence = 99
            };
            inputFrame.Payload.Add(0x10);
            inputFrame.Payload.Add(0x20);
            inputFrame.Payload.Add(0x30);
            var inputFramePayload = SerializePayload((ref FixedList512Bytes<byte> payload) =>
                CommandLinkMessageSerializer.TrySerializeInputFrame(inputFrame, ref payload));
            Assert.That(CommandLinkMessageSerializer.TryReadMessageType(inputFramePayload, out var inputFrameType), Is.True);
            Assert.That(inputFrameType, Is.EqualTo(CommandLinkMessageType.InputFrame));
            Assert.That(CommandLinkMessageSerializer.TryDeserializeInputFrame(inputFramePayload, out var deserializedInputFrame), Is.True);
            Assert.That(deserializedInputFrame.PeerId, Is.EqualTo(inputFrame.PeerId));
            Assert.That(deserializedInputFrame.Tick, Is.EqualTo(inputFrame.Tick));
            Assert.That(deserializedInputFrame.Sequence, Is.EqualTo(inputFrame.Sequence));
            CollectionAssert.AreEqual(CopyBytes(inputFrame.Payload), CopyBytes(deserializedInputFrame.Payload));

            var inputAck = new InputAckMessage
            {
                PeerId = 4,
                AckedSequence = 22,
                AckedTick = 11
            };
            var inputAckPayload = SerializePayload((ref FixedList512Bytes<byte> payload) =>
                CommandLinkMessageSerializer.TrySerializeInputAck(inputAck, ref payload));
            Assert.That(CommandLinkMessageSerializer.TryReadMessageType(inputAckPayload, out var inputAckType), Is.True);
            Assert.That(inputAckType, Is.EqualTo(CommandLinkMessageType.InputAck));
            Assert.That(CommandLinkMessageSerializer.TryDeserializeInputAck(inputAckPayload, out var deserializedInputAck), Is.True);
            Assert.That(deserializedInputAck.PeerId, Is.EqualTo(inputAck.PeerId));
            Assert.That(deserializedInputAck.AckedSequence, Is.EqualTo(inputAck.AckedSequence));
            Assert.That(deserializedInputAck.AckedTick, Is.EqualTo(inputAck.AckedTick));

            var checksum = new ChecksumMessage
            {
                PeerId = 5,
                Tick = 77,
                Checksum = 0xDEADBEEFu
            };
            var checksumPayload = SerializePayload((ref FixedList512Bytes<byte> payload) =>
                CommandLinkMessageSerializer.TrySerializeChecksum(checksum, ref payload));
            Assert.That(CommandLinkMessageSerializer.TryReadMessageType(checksumPayload, out var checksumType), Is.True);
            Assert.That(checksumType, Is.EqualTo(CommandLinkMessageType.Checksum));
            Assert.That(CommandLinkMessageSerializer.TryDeserializeChecksum(checksumPayload, out var deserializedChecksum), Is.True);
            Assert.That(deserializedChecksum.PeerId, Is.EqualTo(checksum.PeerId));
            Assert.That(deserializedChecksum.Tick, Is.EqualTo(checksum.Tick));
            Assert.That(deserializedChecksum.Checksum, Is.EqualTo(checksum.Checksum));

            var disconnect = new DisconnectNoticeMessage
            {
                PeerId = 6
            };
            var disconnectPayload = SerializePayload((ref FixedList512Bytes<byte> payload) =>
                CommandLinkMessageSerializer.TrySerializeDisconnect(disconnect, ref payload));
            Assert.That(CommandLinkMessageSerializer.TryReadMessageType(disconnectPayload, out var disconnectType), Is.True);
            Assert.That(disconnectType, Is.EqualTo(CommandLinkMessageType.DisconnectNotice));
            Assert.That(CommandLinkMessageSerializer.TryDeserializeDisconnect(disconnectPayload, out var deserializedDisconnect), Is.True);
            Assert.That(deserializedDisconnect.PeerId, Is.EqualTo(disconnect.PeerId));

            var ready = new ReadyMessage
            {
                PeerId = 7
            };
            var readyPayload = SerializePayload((ref FixedList512Bytes<byte> payload) =>
                CommandLinkMessageSerializer.TrySerializeReady(ready, ref payload));
            Assert.That(CommandLinkMessageSerializer.TryReadMessageType(readyPayload, out var readyType), Is.True);
            Assert.That(readyType, Is.EqualTo(CommandLinkMessageType.Ready));
            Assert.That(CommandLinkMessageSerializer.TryDeserializeReady(readyPayload, out var deserializedReady), Is.True);
            Assert.That(deserializedReady.PeerId, Is.EqualTo(ready.PeerId));
        }

        [Test]
        public void TryReadMessageTypeReturnsFalseForEmptyPayload()
        {
            var payload = new FixedList512Bytes<byte>();

            Assert.That(CommandLinkMessageSerializer.TryReadMessageType(payload, out _), Is.False);
        }

        [Test]
        public void DeserializersRejectWrongMessageTypes()
        {
            var readyPayload = SerializePayload((ref FixedList512Bytes<byte> payload) =>
                CommandLinkMessageSerializer.TrySerializeReady(new ReadyMessage { PeerId = 9 }, ref payload));

            Assert.That(CommandLinkMessageSerializer.TryDeserializeJoinRequest(readyPayload, out _), Is.False);
            Assert.That(CommandLinkMessageSerializer.TryDeserializeJoinAccept(readyPayload, out _), Is.False);
            Assert.That(CommandLinkMessageSerializer.TryDeserializeSessionStart(readyPayload, out _), Is.False);
            Assert.That(CommandLinkMessageSerializer.TryDeserializeInputFrame(readyPayload, out _), Is.False);
            Assert.That(CommandLinkMessageSerializer.TryDeserializeInputAck(readyPayload, out _), Is.False);
            Assert.That(CommandLinkMessageSerializer.TryDeserializeChecksum(readyPayload, out _), Is.False);
            Assert.That(CommandLinkMessageSerializer.TryDeserializeDisconnect(readyPayload, out _), Is.False);
            Assert.That(CommandLinkMessageSerializer.TryDeserializeReady(readyPayload, out _), Is.True);
        }

        [Test]
        public void DeserializersRejectTruncatedPayloads()
        {
            AssertTruncatedPayloadFails(
                SerializePayload((ref FixedList512Bytes<byte> payload) =>
                    CommandLinkMessageSerializer.TrySerializeJoinRequest(new JoinRequestMessage { RequestedTickRate = 20, RequestedInputDelay = 3 }, ref payload)),
                truncated => CommandLinkMessageSerializer.TryDeserializeJoinRequest(truncated, out _));

            AssertTruncatedPayloadFails(
                SerializePayload((ref FixedList512Bytes<byte> payload) =>
                    CommandLinkMessageSerializer.TrySerializeJoinAccept(
                        new JoinAcceptMessage
                        {
                            AssignedPeerId = 1,
                            HostPeerId = 0,
                            MatchSeed = 1,
                            TickRate = 20,
                            InputDelayTicks = 3,
                            MaxPlayers = 2,
                            ChecksumIntervalTicks = 5
                        },
                        ref payload)),
                truncated => CommandLinkMessageSerializer.TryDeserializeJoinAccept(truncated, out _));

            AssertTruncatedPayloadFails(
                SerializePayload((ref FixedList512Bytes<byte> payload) =>
                    CommandLinkMessageSerializer.TrySerializeSessionStart(new SessionStartMessage { MatchSeed = 55 }, ref payload)),
                truncated => CommandLinkMessageSerializer.TryDeserializeSessionStart(truncated, out _));

            AssertTruncatedPayloadFails(
                SerializePayload((ref FixedList512Bytes<byte> payload) =>
                {
                    var message = new InputFrameMessage { PeerId = 2, Tick = 8, Sequence = 13 };
                    message.Payload.Add(0xAA);
                    message.Payload.Add(0xBB);
                    return CommandLinkMessageSerializer.TrySerializeInputFrame(message, ref payload);
                }),
                truncated => CommandLinkMessageSerializer.TryDeserializeInputFrame(truncated, out _));

            AssertTruncatedPayloadFails(
                SerializePayload((ref FixedList512Bytes<byte> payload) =>
                    CommandLinkMessageSerializer.TrySerializeInputAck(new InputAckMessage { PeerId = 1, AckedSequence = 2, AckedTick = 3 }, ref payload)),
                truncated => CommandLinkMessageSerializer.TryDeserializeInputAck(truncated, out _));

            AssertTruncatedPayloadFails(
                SerializePayload((ref FixedList512Bytes<byte> payload) =>
                    CommandLinkMessageSerializer.TrySerializeChecksum(new ChecksumMessage { PeerId = 1, Tick = 2, Checksum = 3 }, ref payload)),
                truncated => CommandLinkMessageSerializer.TryDeserializeChecksum(truncated, out _));

            AssertTruncatedPayloadFails(
                SerializePayload((ref FixedList512Bytes<byte> payload) =>
                    CommandLinkMessageSerializer.TrySerializeDisconnect(new DisconnectNoticeMessage { PeerId = 1 }, ref payload)),
                truncated => CommandLinkMessageSerializer.TryDeserializeDisconnect(truncated, out _));

            AssertTruncatedPayloadFails(
                SerializePayload((ref FixedList512Bytes<byte> payload) =>
                    CommandLinkMessageSerializer.TrySerializeReady(new ReadyMessage { PeerId = 1 }, ref payload)),
                truncated => CommandLinkMessageSerializer.TryDeserializeReady(truncated, out _));
        }

        [Test]
        public void InputFrameDeserializerRejectsDeclaredPayloadLongerThanAvailableBytes()
        {
            var payload = new FixedList512Bytes<byte>();
            payload.Add((byte)CommandLinkMessageType.InputFrame);
            payload.Add(3);
            payload.Add(8);
            payload.Add(0);
            payload.Add(0);
            payload.Add(0);
            payload.Add(9);
            payload.Add(0);
            payload.Add(0);
            payload.Add(0);
            payload.Add(4);
            payload.Add(0);
            payload.Add(0x11);
            payload.Add(0x22);

            Assert.That(CommandLinkMessageSerializer.TryDeserializeInputFrame(payload, out _), Is.False);
        }

        [Test]
        public void PendingMoveIntentIsNotConsumedUntilEngineCanSubmitForObservedTick()
        {
            FlushPendingIntents();

            ICommandLinkRuntimeHooks previousHooks = CommandLinkRuntimeRegistry.RuntimeHooks;
            var runtimeHooks = new FakeRuntimeHooks();
            var driver = new FakeNetworkDriver();
            var driverFactory = new FakeNetworkDriverFactory(driver);

            CommandLinkRuntimeRegistry.RuntimeHooks = runtimeHooks;

            var config = CommandLinkConfig.Default;
            config.IsHost = true;
            config.MaxPeers = 1;

            var sessionConfig = LockstepSessionConfig.Default;
            sessionConfig.MaxPlayers = 1;
            sessionConfig.InputDelayTicks = 2;

            try
            {
                using var engine = new CommandLinkNetworkEngine(
                    config,
                    sessionConfig,
                    driverFactory,
                    new FakeEndpointProvider());

                engine.SignalReady();
                engine.Poll();

                var orderedTargetIds = new FixedList64Bytes<uint>();
                orderedTargetIds.Add(1000u);
                DeterministicCommandPayload.EnqueueMove(3, 2, false, orderedTargetIds);

                Assert.That(CommandLinkRunnerBridge.TryBuildPendingLocalInput(engine, out _), Is.False);
                Assert.That(DeterministicCommandPayload.CopyPendingIntentSummary().TotalIntentCount, Is.EqualTo(1));

                Assert.That(engine.AllInputsReady(0), Is.True);
                Assert.That(CommandLinkRunnerBridge.TryBuildPendingLocalInput(engine, out var localInput), Is.True);
                Assert.That(DeterministicCommandPayload.CopyPendingIntentSummary().TotalIntentCount, Is.EqualTo(0));
                Assert.That(localInput.Payload[0], Is.EqualTo(1));
                Assert.That(localInput.Payload[1], Is.EqualTo(1));
                Assert.That(localInput.Payload[2], Is.EqualTo((byte)SimCommandType.Move));
                Assert.That(localInput.Payload[3], Is.EqualTo(3));
                Assert.That(localInput.Payload[4], Is.EqualTo(0));
                Assert.That(localInput.Payload[5], Is.EqualTo(2));
                Assert.That(localInput.Payload[6], Is.EqualTo(0));
            }
            finally
            {
                FlushPendingIntents();
                CommandLinkRuntimeRegistry.RuntimeHooks = previousHooks;
            }
        }

        private static byte[] ExtractPeerIds(in ResolvedInputFrame frame)
        {
            var peerIds = new List<byte>();
            int index = 0;
            while (index < frame.PackedPayload.Length)
            {
                peerIds.Add(frame.PackedPayload[index++]);
                int payloadLength = frame.PackedPayload[index++];
                index += payloadLength;
            }

            return peerIds.ToArray();
        }

        private static byte[] CopyBytes(in FixedList512Bytes<byte> payload)
        {
            var result = new byte[payload.Length];
            for (int i = 0; i < payload.Length; i++)
            {
                result[i] = payload[i];
            }

            return result;
        }

        private static FixedList512Bytes<byte> SerializePayload(SerializerAction serializer)
        {
            var payload = new FixedList512Bytes<byte>();
            Assert.That(serializer(ref payload), Is.True);
            return payload;
        }

        private static void AssertTruncatedPayloadFails(FixedList512Bytes<byte> payload, TruncatedDeserializer deserializer)
        {
            for (int length = 0; length < payload.Length; length++)
            {
                Assert.That(deserializer(Truncate(payload, length)), Is.False, $"Expected truncation to length {length} to fail.");
            }
        }

        private static FixedList512Bytes<byte> Truncate(in FixedList512Bytes<byte> payload, int length)
        {
            var result = new FixedList512Bytes<byte>();
            for (int i = 0; i < length; i++)
            {
                result.Add(payload[i]);
            }

            return result;
        }

        private static void FlushPendingIntents()
        {
            while (DeterministicCommandPayload.CopyPendingIntentSummary().TotalIntentCount > 0)
            {
                var payload = new FixedList128Bytes<byte>();
                DeterministicCommandPayload.BuildPayload(ref payload);
            }
        }

        private static byte[] CopyPeers(in FixedList32Bytes<byte> peerIds)
        {
            var result = new byte[peerIds.Length];
            for (int i = 0; i < peerIds.Length; i++)
            {
                result[i] = peerIds[i];
            }

            return result;
        }

        private static CommandLinkPacket CreateInputFramePacket(byte packetPeerId, byte payloadPeerId, uint tick, uint sequence, byte payloadValue)
        {
            var message = new InputFrameMessage
            {
                PeerId = payloadPeerId,
                Tick = tick,
                Sequence = sequence
            };
            message.Payload.Add(payloadValue);

            var packet = new CommandLinkPacket
            {
                PeerId = packetPeerId
            };
            CommandLinkMessageSerializer.TrySerializeInputFrame(message, ref packet.Payload);
            return packet;
        }

        private static CommandLinkPacket CreateJoinRequestPacket(byte packetPeerId)
        {
            var message = new JoinRequestMessage
            {
                RequestedTickRate = 20,
                RequestedInputDelay = 2
            };

            var packet = new CommandLinkPacket
            {
                PeerId = packetPeerId
            };
            CommandLinkMessageSerializer.TrySerializeJoinRequest(message, ref packet.Payload);
            return packet;
        }

        private static CommandLinkPacket CreateReadyPacket(byte packetPeerId, byte payloadPeerId)
        {
            var message = new ReadyMessage
            {
                PeerId = payloadPeerId
            };

            var packet = new CommandLinkPacket
            {
                PeerId = packetPeerId
            };
            CommandLinkMessageSerializer.TrySerializeReady(message, ref packet.Payload);
            return packet;
        }

        private static CommandLinkPacket CreateDisconnectPacket(byte packetPeerId, byte payloadPeerId)
        {
            var message = new DisconnectNoticeMessage
            {
                PeerId = payloadPeerId
            };

            var packet = new CommandLinkPacket
            {
                PeerId = packetPeerId
            };
            CommandLinkMessageSerializer.TrySerializeDisconnect(message, ref packet.Payload);
            return packet;
        }

        private static CommandLinkPacket CreateTransportDisconnectPacket(byte peerId)
        {
            return new CommandLinkPacket
            {
                Kind = CommandLinkPacketKind.TransportDisconnect,
                PeerId = peerId
            };
        }

        private sealed class FakeRuntimeHooks : ICommandLinkRuntimeHooks
        {
            public bool SupportsTickCallbacks => true;

            public bool IsSimulationReady => true;

            public void SetGateCheck(Func<uint, bool> gateCheck)
            {
            }

            public void ClearGateCheck(Func<uint, bool> gateCheck)
            {
            }

            public void AddPreTick(Action<uint> callback)
            {
            }

            public void RemovePreTick(Action<uint> callback)
            {
            }

            public void AddPostTick(Action<uint> callback)
            {
            }

            public void RemovePostTick(Action<uint> callback)
            {
            }

            public bool TryApplyResolvedFrame(uint tick, in ResolvedInputFrame resolvedFrame)
            {
                return true;
            }

            public bool TryComputeSimulationChecksum(out uint checksum)
            {
                checksum = 0;
                return true;
            }
        }

        private sealed class FakeNetworkDriverFactory : INetworkDriverFactory
        {
            private readonly INetworkDriver _driver;

            public FakeNetworkDriverFactory(INetworkDriver driver)
            {
                _driver = driver;
            }

            public INetworkDriver Create(CommandLinkConfig config, LockstepSessionConfig sessionConfig, INetworkEndpointProvider endpointProvider)
            {
                _driver.Initialize(config, sessionConfig, endpointProvider);
                return _driver;
            }
        }

        private sealed class FakeNetworkDriver : INetworkDriver
        {
            private readonly Queue<CommandLinkPacket> _queuedPackets = new Queue<CommandLinkPacket>();

            public bool IsCreated { get; private set; } = true;

            public bool IsHostConnectionReady => true;

            public void Initialize(CommandLinkConfig config, LockstepSessionConfig sessionConfig, INetworkEndpointProvider endpointProvider)
            {
                IsCreated = true;
            }

            public void Poll()
            {
            }

            public void Send(byte peerId, in CommandLinkPacket packet)
            {
            }

            public bool TryDequeue(out CommandLinkPacket packet)
            {
                if (_queuedPackets.Count == 0)
                {
                    packet = default;
                    return false;
                }

                packet = _queuedPackets.Dequeue();
                return true;
            }

            public void Shutdown()
            {
                IsCreated = false;
            }

            public void Enqueue(in CommandLinkPacket packet)
            {
                _queuedPackets.Enqueue(packet);
            }
        }

        private sealed class FakeEndpointProvider : INetworkEndpointProvider
        {
            public bool TryGetListenEndpoint(out CommandLinkEndpoint endpoint)
            {
                endpoint = new CommandLinkEndpoint
                {
                    Address = "127.0.0.1",
                    Port = 7777
                };
                return true;
            }

            public bool TryGetRemoteEndpoint(out CommandLinkEndpoint endpoint)
            {
                endpoint = new CommandLinkEndpoint
                {
                    Address = "127.0.0.1",
                    Port = 7778
                };
                return true;
            }
        }

        private delegate bool SerializerAction(ref FixedList512Bytes<byte> payload);

        private delegate bool TruncatedDeserializer(FixedList512Bytes<byte> payload);
    }
}
