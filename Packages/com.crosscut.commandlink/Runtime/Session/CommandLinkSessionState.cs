using Unity.Collections;

namespace CrossCut.CommandLink
{
    /// <summary>
    /// Snapshot of current session-level state used by orchestration and diagnostics.
    /// </summary>
    public struct CommandLinkSessionState
    {
        public LockstepSessionState SessionState;
        public byte LocalPeerId;
        public byte HostPeerId;
        public LockstepSessionConfig SessionConfig;
        public FixedList32Bytes<byte> ConnectedPeerIds;
        public FixedList32Bytes<byte> ReadyPeerIds;
    }
}
