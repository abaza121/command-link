using Unity.Collections;
using Unity.Entities;

namespace CrossCut.CommandLink
{
    [WorldSystemFilter(WorldSystemFilterFlags.Presentation)]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial struct NetworkPollSystem : ISystem
    {
        public void OnCreate(ref SystemState state) { }

        public void OnUpdate(ref SystemState state)
        {
            if (CommandLinkRuntimeRegistry.DriveFromMonoBehaviour)
            {
                return;
            }

            CommandLinkRuntimeRegistry.Engine?.Poll();
        }

        public void OnDestroy(ref SystemState state) { }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.Presentation)]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(NetworkPollSystem))]
    public partial struct NetworkInputSubmitSystem : ISystem
    {
        public void OnCreate(ref SystemState state) { }

        public void OnUpdate(ref SystemState state)
        {
            if (CommandLinkRuntimeRegistry.DriveFromMonoBehaviour)
            {
                return;
            }

            var engine = CommandLinkRuntimeRegistry.Engine;
            if (engine == null)
            {
                return;
            }

            if (engine.SessionState.SessionState != LockstepSessionState.Running)
            {
                return;
            }

            if (CommandLinkRunnerBridge.TryBuildPendingLocalInput(engine, out var localInput))
            {
                engine.SubmitLocalInputsUpTo(localInput);
            }
        }

        public void OnDestroy(ref SystemState state) { }
    }
}
