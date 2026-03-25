using Unity.Collections;
using Unity.Entities;

namespace CrossCut.CommandLink
{
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation)]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial struct LocalLoopbackCommandSystem : ISystem
    {
        private uint _localTick;

        public void OnCreate(ref SystemState state)
        {
            _localTick = 0;
        }

        public void OnUpdate(ref SystemState state)
        {
            if (CommandLinkRuntimeRegistry.Engine != null)
            {
                return;
            }

            var payload = new FixedList128Bytes<byte>();
            DeterministicCommandPayload.BuildPayload(ref payload);

            var resolved = new ResolvedInputFrame
            {
                Tick = _localTick,
                PeerMask = 1,
                PackedPayload = new FixedList512Bytes<byte>()
            };

            resolved.PackedPayload.Add(0);
            resolved.PackedPayload.Add((byte)payload.Length);
            for (int i = 0; i < payload.Length; i++)
            {
                resolved.PackedPayload.Add(payload[i]);
            }

            SimulationCommandInboxBridge.ApplyResolvedFrame(_localTick, resolved);
            _localTick++;
        }

        public void OnDestroy(ref SystemState state)
        {
        }
    }
}
