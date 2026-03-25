using Unity.Entities;

namespace CrossCut.CommandLink
{
    public static class NetworkWorlds
    {
        public static World NetworkWorld { get; internal set; }

        public static bool IsReady => NetworkWorld != null && NetworkWorld.IsCreated;
    }
}
