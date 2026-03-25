namespace CrossCut.CommandLink
{
    public enum LockstepSessionState : byte
    {
        None = 0,
        WaitingForPeers = 1,
        Starting = 2,
        Running = 3,
        Closing = 4,
        Closed = 5
    }
}
