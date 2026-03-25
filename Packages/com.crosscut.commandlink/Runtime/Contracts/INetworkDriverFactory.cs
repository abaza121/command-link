namespace CrossCut.CommandLink
{
    /// <summary>
    /// Factory abstraction to create concrete transport drivers (Unity Transport first, Relay later).
    /// </summary>
    public interface INetworkDriverFactory
    {
        INetworkDriver Create(CommandLinkConfig config, LockstepSessionConfig sessionConfig, INetworkEndpointProvider endpointProvider);
    }
}
