namespace CrossCut.CommandLink
{
    /// <summary>
    /// Resolves host/client socket endpoints independently of transport backend.
    /// </summary>
    public interface INetworkEndpointProvider
    {
        bool TryGetListenEndpoint(out CommandLinkEndpoint endpoint);
        bool TryGetRemoteEndpoint(out CommandLinkEndpoint endpoint);
    }
}
