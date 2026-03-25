using Unity.Entities;
using UnityEngine;

namespace CrossCut.CommandLink
{
    [DefaultExecutionOrder(-90)]
    public sealed class NetworkBootstrapper : MonoBehaviour
    {
        private bool _attemptedCreation;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureBootstrapper()
        {
            var bootstrapperObject = new GameObject("[CommandLink] NetworkBootstrapper");
            DontDestroyOnLoad(bootstrapperObject);
            bootstrapperObject.AddComponent<NetworkBootstrapper>();
        }

        private void Update()
        {
            if (_attemptedCreation || NetworkWorlds.IsReady)
            {
                return;
            }

            if (!CommandLinkRuntimeRegistry.RuntimeHooks.IsSimulationReady)
            {
                return;
            }

            _attemptedCreation = true;
            CreateNetworkWorld();
        }

        private static void CreateNetworkWorld()
        {
            var networkWorld = new World("NetworkWorld", WorldFlags.GameServer);
            DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(
                networkWorld,
                typeof(NetworkPollSystem),
                typeof(NetworkInputSubmitSystem));
            ScriptBehaviourUpdateOrder.AppendWorldToCurrentPlayerLoop(networkWorld);

            NetworkWorlds.NetworkWorld = networkWorld;
            Debug.Log("[CommandLink] NetworkWorld created and appended to player loop.");
        }

        private void OnDestroy()
        {
            if (NetworkWorlds.NetworkWorld != null && NetworkWorlds.NetworkWorld.IsCreated)
            {
                NetworkWorlds.NetworkWorld.Dispose();
                NetworkWorlds.NetworkWorld = null;
            }
        }
    }
}
