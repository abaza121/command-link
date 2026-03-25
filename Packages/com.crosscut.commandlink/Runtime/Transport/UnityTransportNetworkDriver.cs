using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Networking.Transport;
using UnityEngine;

namespace CrossCut.CommandLink
{
    /// <summary>
    /// Unity Transport-backed driver implementation for CommandLink.
    /// Host mode binds/listens and accepts multiple clients.
    /// Client mode connects to a single remote host endpoint.
    /// </summary>
    public sealed class UnityTransportNetworkDriver : INetworkDriver
    {
        private NetworkDriver _driver;
        private NetworkConnection _hostConnection;
        private NativeList<NetworkConnection> _clientConnections;
        private readonly Dictionary<byte, int> _connectionIndexByPeerId = new Dictionary<byte, int>();
        private byte _nextConnectionPeerId = 1;
        private readonly Queue<CommandLinkPacket> _incomingPackets = new Queue<CommandLinkPacket>();
        private bool _isHost;
        private bool _isCreated;
        private bool _isHostConnectionReady;

        public bool IsCreated => _isCreated;
        public bool IsHostConnectionReady => _isHost || _isHostConnectionReady;

        public void Initialize(CommandLinkConfig config, LockstepSessionConfig sessionConfig, INetworkEndpointProvider endpointProvider)
        {
            _isHost = config.IsHost;
            _driver = NetworkDriver.Create();
            _clientConnections = new NativeList<NetworkConnection>(config.MaxPeers, Allocator.Persistent);
            _connectionIndexByPeerId.Clear();
            _nextConnectionPeerId = 1;
            _isHostConnectionReady = _isHost;

            if (_isHost)
            {
                if (endpointProvider == null || !endpointProvider.TryGetListenEndpoint(out var listenEndpoint) || !TryBuildEndpoint(listenEndpoint, out var endpoint))
                {
                    throw new InvalidOperationException("[CommandLink] Host requires a valid listen endpoint for Unity Transport.");
                }

                if (_driver.Bind(endpoint) != 0)
                {
                    throw new InvalidOperationException($"[CommandLink] Failed to bind Unity Transport to {listenEndpoint}.");
                }

                _driver.Listen();
            }
            else
            {
                if (endpointProvider == null || !endpointProvider.TryGetRemoteEndpoint(out var remoteEndpoint) || !TryBuildEndpoint(remoteEndpoint, out var endpoint))
                {
                    throw new InvalidOperationException("[CommandLink] Client requires a valid remote endpoint for Unity Transport.");
                }

                _hostConnection = _driver.Connect(endpoint);
                Debug.Log($"[CommandLink] Connecting to host transport endpoint {remoteEndpoint}.");
            }

            _isCreated = true;
        }

        public void Poll()
        {
            if (!_isCreated)
            {
                return;
            }

            _driver.ScheduleUpdate().Complete();

            if (_isHost)
            {
                AcceptConnections();
                PollHostSideEvents();
                return;
            }

            PollClientSideEvents();
        }

        public void Send(byte peerId, in CommandLinkPacket packet)
        {
            if (!_isCreated)
            {
                return;
            }

            if (_isHost)
            {
                if (peerId == 0)
                {
                    BroadcastToClients(packet);
                    return;
                }

                if (_connectionIndexByPeerId.TryGetValue(peerId, out int connectionIndex)
                    && connectionIndex >= 0
                    && connectionIndex < _clientConnections.Length)
                {
                    var connection = _clientConnections[connectionIndex];
                    if (connection.IsCreated)
                    {
                        SendToConnection(connection, packet);
                    }
                }

                return;
            }

            if (_hostConnection.IsCreated)
            {
                SendToConnection(_hostConnection, packet);
            }
        }

        public bool TryDequeue(out CommandLinkPacket packet)
        {
            if (_incomingPackets.Count > 0)
            {
                packet = _incomingPackets.Dequeue();
                return true;
            }

            packet = default;
            return false;
        }

        public void Shutdown()
        {
            _isCreated = false;
            _isHostConnectionReady = false;

            if (_hostConnection.IsCreated)
            {
                _hostConnection.Disconnect(_driver);
                _hostConnection = default;
            }

            if (_clientConnections.IsCreated)
            {
                for (int i = 0; i < _clientConnections.Length; i++)
                {
                    if (_clientConnections[i].IsCreated)
                    {
                        _clientConnections[i].Disconnect(_driver);
                    }
                }

                _clientConnections.Dispose();
            }

            if (_driver.IsCreated)
            {
                _driver.Dispose();
            }

            _incomingPackets.Clear();
            _connectionIndexByPeerId.Clear();
            _nextConnectionPeerId = 1;
        }

        private void AcceptConnections()
        {
            NetworkConnection connection;
            while ((connection = _driver.Accept()) != default)
            {
                int connectionIndex = _clientConnections.Length;
                _clientConnections.Add(connection);
                byte connectionPeerId = _nextConnectionPeerId++;
                _connectionIndexByPeerId[connectionPeerId] = connectionIndex;
                Debug.Log($"[CommandLink] Accepted transport connection mapped to peer {connectionPeerId}.");
            }
        }

        private void PollHostSideEvents()
        {
            for (int i = 0; i < _clientConnections.Length; i++)
            {
                if (!_clientConnections[i].IsCreated)
                {
                    continue;
                }

                while (TryPopEvent(_clientConnections[i], out var eventType, out var reader))
                {
                    if (eventType == NetworkEvent.Type.Disconnect)
                    {
                        byte peerId = GetPeerIdByConnectionIndex(i);
                        RemoveConnectionPeerMapping(i);
                        _clientConnections[i] = default;
                        EnqueueTransportDisconnect(peerId);
                        Debug.LogWarning($"[CommandLink] Client peer {peerId} disconnected from host transport.");
                        break;
                    }

                    if (eventType == NetworkEvent.Type.Data)
                    {
                        EnqueuePacketFromReader(reader, GetPeerIdByConnectionIndex(i));
                    }
                }
            }
        }

        private void PollClientSideEvents()
        {
            if (!_hostConnection.IsCreated)
            {
                return;
            }

            while (TryPopEvent(_hostConnection, out var eventType, out var reader))
            {
                if (eventType == NetworkEvent.Type.Connect)
                {
                    if (!_isHostConnectionReady)
                    {
                        _isHostConnectionReady = true;
                        Debug.Log("[CommandLink] Connected to host transport endpoint.");
                    }

                    continue;
                }

                if (eventType == NetworkEvent.Type.Disconnect)
                {
                    _isHostConnectionReady = false;
                    EnqueueTransportDisconnect(0);
                    _hostConnection = default;
                    Debug.LogWarning("[CommandLink] Disconnected from host transport endpoint.");
                    break;
                }

                if (eventType == NetworkEvent.Type.Data)
                {
                    EnqueuePacketFromReader(reader, 0);
                }
            }
        }

        private bool TryPopEvent(NetworkConnection connection, out NetworkEvent.Type eventType, out DataStreamReader reader)
        {
            eventType = _driver.PopEventForConnection(connection, out reader);
            return eventType != NetworkEvent.Type.Empty;
        }

        private void EnqueuePacketFromReader(DataStreamReader reader, byte peerId)
        {
            var payload = new FixedList512Bytes<byte>();
            int payloadLength = Math.Min(reader.Length, payload.Capacity);

            for (int i = 0; i < payloadLength; i++)
            {
                payload.Add(reader.ReadByte());
            }

            _incomingPackets.Enqueue(new CommandLinkPacket
            {
                Kind = CommandLinkPacketKind.Data,
                PeerId = peerId,
                Payload = payload
            });
        }

        private void EnqueueTransportDisconnect(byte peerId)
        {
            _incomingPackets.Enqueue(new CommandLinkPacket
            {
                Kind = CommandLinkPacketKind.TransportDisconnect,
                PeerId = peerId
            });
        }

        private void BroadcastToClients(in CommandLinkPacket packet)
        {
            for (int i = 0; i < _clientConnections.Length; i++)
            {
                var connection = _clientConnections[i];
                if (!connection.IsCreated)
                {
                    continue;
                }

                SendToConnection(connection, packet);
            }
        }

        private byte GetPeerIdByConnectionIndex(int connectionIndex)
        {
            foreach (var kvp in _connectionIndexByPeerId)
            {
                if (kvp.Value == connectionIndex)
                {
                    return kvp.Key;
                }
            }

            return 0;
        }

        private void RemoveConnectionPeerMapping(int connectionIndex)
        {
            byte peerIdToRemove = 0;
            foreach (var kvp in _connectionIndexByPeerId)
            {
                if (kvp.Value == connectionIndex)
                {
                    peerIdToRemove = kvp.Key;
                    break;
                }
            }

            if (peerIdToRemove != 0)
            {
                _connectionIndexByPeerId.Remove(peerIdToRemove);
            }
        }

        private void SendToConnection(NetworkConnection connection, in CommandLinkPacket packet)
        {
            int beginSendResult = _driver.BeginSend(connection, out var writer);
            if (beginSendResult != 0)
            {
                Debug.LogWarning($"[CommandLink] BeginSend failed with code {beginSendResult}.");
                return;
            }

            for (int i = 0; i < packet.Payload.Length; i++)
            {
                writer.WriteByte(packet.Payload[i]);
            }

            _driver.EndSend(writer);
        }

        private static bool TryBuildEndpoint(CommandLinkEndpoint endpoint, out NetworkEndpoint networkEndpoint)
        {
            return NetworkEndpoint.TryParse(endpoint.Address, endpoint.Port, out networkEndpoint, NetworkFamily.Ipv4);
        }
    }
}
