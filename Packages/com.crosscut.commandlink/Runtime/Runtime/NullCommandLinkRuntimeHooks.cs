using System;

namespace CrossCut.CommandLink
{
    internal sealed class NullCommandLinkRuntimeHooks : ICommandLinkRuntimeHooks
    {
        public static readonly NullCommandLinkRuntimeHooks Instance = new NullCommandLinkRuntimeHooks();

        private NullCommandLinkRuntimeHooks() { }

        public bool SupportsTickCallbacks => false;
        public bool IsSimulationReady => false;

        public void SetGateCheck(Func<uint, bool> gateCheck) { }
        public void ClearGateCheck(Func<uint, bool> gateCheck) { }
        public void AddPreTick(Action<uint> callback) { }
        public void RemovePreTick(Action<uint> callback) { }
        public void AddPostTick(Action<uint> callback) { }
        public void RemovePostTick(Action<uint> callback) { }

        public bool TryApplyResolvedFrame(uint tick, in ResolvedInputFrame resolvedFrame) => false;
        public bool TryComputeSimulationChecksum(out uint checksum)
        {
            checksum = 0;
            return false;
        }
    }
}
