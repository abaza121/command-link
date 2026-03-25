using System;

namespace CrossCut.CommandLink
{
    /// <summary>
    /// Host-authored deterministic session parameters shared with all peers.
    /// </summary>
    [Serializable]
    public struct LockstepSessionConfig
    {
        public uint MatchSeed;
        public ushort TickRate;
        public ushort InputDelayTicks;
        public ushort MaxPlayers;
        public ushort ChecksumIntervalTicks;

        public static LockstepSessionConfig Default => new LockstepSessionConfig
        {
            MatchSeed = 1u,
            TickRate = 20,
            InputDelayTicks = 4,
            MaxPlayers = 8,
            ChecksumIntervalTicks = 10
        };

        public bool IsValid()
        {
            return TickRate > 0
                && InputDelayTicks > 0
                && MaxPlayers > 0
                && ChecksumIntervalTicks > 0;
        }
    }
}
