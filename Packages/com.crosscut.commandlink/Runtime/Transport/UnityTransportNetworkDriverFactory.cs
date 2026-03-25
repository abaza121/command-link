namespace CrossCut.CommandLink
{
    public sealed class UnityTransportNetworkDriverFactory : INetworkDriverFactory
    {
        public INetworkDriver Create(CommandLinkConfig config, LockstepSessionConfig sessionConfig, INetworkEndpointProvider endpointProvider)
        {
            var driver = new UnityTransportNetworkDriver();
            driver.Initialize(config, sessionConfig, endpointProvider);
            return driver;
        }
    }
}
