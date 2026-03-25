using System;
using System.Collections.Generic;

namespace CrossCut.CommandLink.Samples.TwoPeerArena
{
    public sealed class TwoPeerArenaRuntimeHooks : ICommandLinkRuntimeHooks
    {
        private readonly List<Func<uint, bool>> _gateChecks = new List<Func<uint, bool>>(4);
        private readonly List<Action<uint>> _preTickCallbacks = new List<Action<uint>>(4);
        private readonly List<Action<uint>> _postTickCallbacks = new List<Action<uint>>(4);

        public TwoPeerArenaSimulation Simulation { get; } = new TwoPeerArenaSimulation();

        public bool SupportsTickCallbacks => true;
        public bool IsSimulationReady { get; private set; }

        public void SetSimulationReady(bool ready)
        {
            IsSimulationReady = ready;
        }

        public void ResetSimulation()
        {
            Simulation.Reset();
        }

        public bool CanAdvanceTick(uint tick)
        {
            for (int i = 0; i < _gateChecks.Count; i++)
            {
                if (!_gateChecks[i].Invoke(tick))
                {
                    return false;
                }
            }

            return true;
        }

        public void InvokePreTick(uint tick)
        {
            for (int i = 0; i < _preTickCallbacks.Count; i++)
            {
                _preTickCallbacks[i].Invoke(tick);
            }
        }

        public void InvokePostTick(uint tick)
        {
            for (int i = 0; i < _postTickCallbacks.Count; i++)
            {
                _postTickCallbacks[i].Invoke(tick);
            }
        }

        public void SetGateCheck(Func<uint, bool> gateCheck)
        {
            if (gateCheck == null || _gateChecks.Contains(gateCheck))
            {
                return;
            }

            _gateChecks.Add(gateCheck);
        }

        public void ClearGateCheck(Func<uint, bool> gateCheck)
        {
            if (gateCheck == null)
            {
                return;
            }

            _gateChecks.Remove(gateCheck);
        }

        public void AddPreTick(Action<uint> callback)
        {
            if (callback == null || _preTickCallbacks.Contains(callback))
            {
                return;
            }

            _preTickCallbacks.Add(callback);
        }

        public void RemovePreTick(Action<uint> callback)
        {
            if (callback == null)
            {
                return;
            }

            _preTickCallbacks.Remove(callback);
        }

        public void AddPostTick(Action<uint> callback)
        {
            if (callback == null || _postTickCallbacks.Contains(callback))
            {
                return;
            }

            _postTickCallbacks.Add(callback);
        }

        public void RemovePostTick(Action<uint> callback)
        {
            if (callback == null)
            {
                return;
            }

            _postTickCallbacks.Remove(callback);
        }

        public bool TryApplyResolvedFrame(uint tick, in ResolvedInputFrame resolvedFrame)
        {
            Simulation.StageResolvedFrame(tick, resolvedFrame);
            return true;
        }

        public bool TryComputeSimulationChecksum(out uint checksum)
        {
            checksum = Simulation.ComputeChecksum();
            return true;
        }
    }
}
