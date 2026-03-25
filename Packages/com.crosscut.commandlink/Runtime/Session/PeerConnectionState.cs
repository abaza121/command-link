namespace CrossCut.CommandLink
{
    public enum PeerConnectionState : byte
    {
        Disconnected = 0,
        Connecting = 1,
        Connected = 2,
        Ready = 3,
        TimedOut = 4
    }
}
