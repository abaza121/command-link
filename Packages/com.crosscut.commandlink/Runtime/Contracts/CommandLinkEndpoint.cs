namespace CrossCut.CommandLink
{
    /// <summary>
    /// Abstract endpoint description used by transport providers.
    /// </summary>
    public struct CommandLinkEndpoint
    {
        public string Address;
        public ushort Port;

        public bool IsValid()
        {
            return !string.IsNullOrWhiteSpace(Address) && Port > 0;
        }

        public override string ToString()
        {
            return $"{Address}:{Port}";
        }
    }
}
