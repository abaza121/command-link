namespace CrossCut.CommandLink
{
    public enum CommandLinkMessageType : byte
    {
        JoinRequest = 1,
        JoinAccept = 2,
        SessionStart = 3,
        InputFrame = 4,
        InputAck = 5,
        Checksum = 6,
        DisconnectNotice = 7,
        Ready = 8
    }
}
