namespace CrossCut.CommandLink
{
    public sealed class StaticEndpointProvider : INetworkEndpointProvider
    {
        private readonly CommandLinkEndpoint _listenEndpoint;
        private readonly CommandLinkEndpoint _remoteEndpoint;

        public StaticEndpointProvider(CommandLinkEndpoint listenEndpoint, CommandLinkEndpoint remoteEndpoint)
        {
            _listenEndpoint = listenEndpoint;
            _remoteEndpoint = remoteEndpoint;
        }

        public bool TryGetListenEndpoint(out CommandLinkEndpoint endpoint)
        {
            endpoint = _listenEndpoint;
            return endpoint.IsValid();
        }

        public bool TryGetRemoteEndpoint(out CommandLinkEndpoint endpoint)
        {
            endpoint = _remoteEndpoint;
            return endpoint.IsValid();
        }
    }
}
