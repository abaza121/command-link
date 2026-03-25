using System;

namespace CrossCut.CommandLink
{
    /// <summary>
    /// Host-provided integration hooks that connect CommandLink to a simulation runner and world.
    /// </summary>
    public interface ICommandLinkRuntimeHooks
    {
        bool SupportsTickCallbacks { get; }
        bool IsSimulationReady { get; }

        void SetGateCheck(Func<uint, bool> gateCheck);
        void ClearGateCheck(Func<uint, bool> gateCheck);
        void AddPreTick(Action<uint> callback);
        void RemovePreTick(Action<uint> callback);
        void AddPostTick(Action<uint> callback);
        void RemovePostTick(Action<uint> callback);

        bool TryApplyResolvedFrame(uint tick, in ResolvedInputFrame resolvedFrame);
        bool TryComputeSimulationChecksum(out uint checksum);
    }
}
