namespace CrossCut.CommandLink
{
    /// <summary>
    /// Runtime abstraction over the low-level transport driver.
    /// </summary>
    public interface INetworkDriver
    {
        bool IsCreated { get; }
        bool IsHostConnectionReady { get; }

        void Initialize(CommandLinkConfig config, LockstepSessionConfig sessionConfig, INetworkEndpointProvider endpointProvider);
        void Poll();
        void Send(byte peerId, in CommandLinkPacket packet);
        bool TryDequeue(out CommandLinkPacket packet);
        void Shutdown();
    }
}
