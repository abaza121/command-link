using System;

namespace CrossCut.CommandLink
{
    /// <summary>
    /// Runtime-level networking settings for CommandLink transport/session behavior.
    /// </summary>
    [Serializable]
    public struct CommandLinkConfig
    {
        public bool IsHost;
        public int MaxPeers;
        public float DisconnectTimeoutSeconds;
        public int MaxPayloadBytes;
        public int MaxResendAttempts;

        public static CommandLinkConfig Default => new CommandLinkConfig
        {
            IsHost = false,
            MaxPeers = 8,
            DisconnectTimeoutSeconds = 2f,
            MaxPayloadBytes = 512,
            MaxResendAttempts = 4
        };

        public bool IsValid()
        {
            return MaxPeers > 0
                && DisconnectTimeoutSeconds > 0f
                && MaxPayloadBytes > 0
                && MaxResendAttempts > 0;
        }
    }
}
