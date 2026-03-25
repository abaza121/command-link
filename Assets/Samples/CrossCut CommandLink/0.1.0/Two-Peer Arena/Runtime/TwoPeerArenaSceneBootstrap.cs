using UnityEngine;

namespace CrossCut.CommandLink.Samples.TwoPeerArena
{
    public enum TwoPeerArenaSceneRole : byte
    {
        Launcher = 0,
        Arena = 1,
    }

    [DisallowMultipleComponent]
    public sealed class TwoPeerArenaSceneBootstrap : MonoBehaviour
    {
        [SerializeField] private TwoPeerArenaSceneRole sceneRole = TwoPeerArenaSceneRole.Launcher;
        [SerializeField] private string hostAddress = "127.0.0.1";
        [SerializeField] private string hostPort = "7777";

        private TwoPeerArenaSessionController _controller;
        private GameObject _arenaRoot;
        private Transform[] _tokenTransforms;
        private Material[] _tokenMaterials;
        private Material _tileEvenMaterial;
        private Material _tileOddMaterial;

        private void OnEnable()
        {
            _controller = TwoPeerArenaSessionController.EnsureInstance();
            string activeScenePath = gameObject.scene.path;

            if (sceneRole == TwoPeerArenaSceneRole.Launcher)
            {
                _controller.RegisterLauncherScene(activeScenePath);
                return;
            }

            _controller.RegisterArenaScene(activeScenePath);
            BuildArenaVisuals();

            if (!_controller.HasActiveSession)
            {
                _controller.StartOfflineIfIdle(activeScenePath);
            }
        }

        private void Update()
        {
            if (sceneRole != TwoPeerArenaSceneRole.Arena || _controller == null)
            {
                return;
            }

            HandleArenaInput();
            RefreshArenaVisuals();
        }

        private void OnGUI()
        {
            if (_controller == null)
            {
                return;
            }

            if (sceneRole == TwoPeerArenaSceneRole.Launcher)
            {
                RenderLauncherGui();
                return;
            }

            RenderArenaGui();
        }

        private void OnDestroy()
        {
            DestroyMaterial(ref _tileEvenMaterial);
            DestroyMaterial(ref _tileOddMaterial);

            if (_tokenMaterials == null)
            {
                return;
            }

            for (int i = 0; i < _tokenMaterials.Length; i++)
            {
                if (_tokenMaterials[i] != null)
                {
                    Destroy(_tokenMaterials[i]);
                }
            }
        }

        private void RenderLauncherGui()
        {
            var boxRect = new Rect(24f, 24f, 460f, 310f);
            GUI.Box(boxRect, "Two-Peer Arena Launcher");

            GUILayout.BeginArea(new Rect(boxRect.x + 16f, boxRect.y + 30f, boxRect.width - 32f, boxRect.height - 40f));
            GUILayout.Label("Minimal sample for CrossCut.CommandLink.");
            GUILayout.Label("Use Host in one Editor instance, then Client in a second instance.");
            GUILayout.Space(8f);

            GUILayout.Label("Host / Client Address");
            hostAddress = GUILayout.TextField(hostAddress);

            GUILayout.Label("Port");
            hostPort = GUILayout.TextField(hostPort);

            GUILayout.Space(12f);

            bool hostPressed = GUILayout.Button("Start Host", GUILayout.Height(32f));
            bool clientPressed = GUILayout.Button("Start Client", GUILayout.Height(32f));
            bool offlinePressed = GUILayout.Button("Start Offline", GUILayout.Height(32f));

            GUILayout.Space(12f);
            GUILayout.Label($"Status: {_controller.StatusMessage}");
            GUILayout.Label($"Current Session: {_controller.SessionState.SessionState}");
            GUILayout.EndArea();

            if (hostPressed)
            {
                _controller.StartHost(hostAddress, ParsePort(), BuildArenaScenePath());
            }

            if (clientPressed)
            {
                _controller.StartClient(hostAddress, ParsePort(), BuildArenaScenePath());
            }

            if (offlinePressed)
            {
                _controller.StartOffline(BuildArenaScenePath());
            }
        }

        private void RenderArenaGui()
        {
            var sessionState = _controller.SessionState;
            var boxRect = new Rect(16f, 16f, 430f, 250f);
            GUI.Box(boxRect, "Two-Peer Arena HUD");

            GUILayout.BeginArea(new Rect(boxRect.x + 14f, boxRect.y + 28f, boxRect.width - 28f, boxRect.height - 38f));
            GUILayout.Label($"Launch Mode: {_controller.LaunchMode}");
            GUILayout.Label($"Session: {sessionState.SessionState}");
            GUILayout.Label($"Tick: {_controller.Simulation.CurrentTick}");
            GUILayout.Label($"Buffered: {_controller.BufferedTickCountEstimate} tick(s), {_controller.BufferedTickBacklogSeconds:F3}s");
            GUILayout.Label($"Tick Mode: {_controller.TickResilienceSummary}");
            GUILayout.Label($"Local Peer: {sessionState.LocalPeerId}");
            GUILayout.Label($"Connected Peers: {FormatConnectedPeers()}");
            GUILayout.Label($"Last Submitted: {_controller.Simulation.LastSubmittedMoveSummary}");
            GUILayout.Label($"Last Resolved: {_controller.Simulation.LastResolvedMoveSummary}");
            GUILayout.Label($"Last Applied: {_controller.Simulation.LastAppliedMoveSummary}");
            GUILayout.Label($"Checksum: 0x{_controller.Simulation.LastChecksum:X8}");
            GUILayout.Space(8f);
            GUILayout.Label("Controls: Arrow Keys or WASD");
            GUILayout.Label(_controller.StatusMessage);
            GUILayout.EndArea();
        }

        private void HandleArenaInput()
        {
            if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow))
            {
                _controller.TryQueueLocalMove(0, 1);
            }
            else if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow))
            {
                _controller.TryQueueLocalMove(0, -1);
            }
            else if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow))
            {
                _controller.TryQueueLocalMove(-1, 0);
            }
            else if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow))
            {
                _controller.TryQueueLocalMove(1, 0);
            }
        }

        private void RefreshArenaVisuals()
        {
            if (_tokenTransforms == null || _tokenMaterials == null)
            {
                return;
            }

            byte localPeerId = _controller.SessionState.LocalPeerId;
            for (byte peerId = 0; peerId < _tokenTransforms.Length; peerId++)
            {
                if (!_controller.Simulation.TryGetTokenState(peerId, out var token))
                {
                    continue;
                }

                _tokenTransforms[peerId].position = CellToWorld(token.CellX, token.CellY, 0.55f + (peerId * 0.05f));
                bool isConnected = _controller.IsPeerConnected(peerId);
                Color peerColor = peerId == 0 ? new Color(0.19f, 0.56f, 0.93f) : new Color(0.94f, 0.55f, 0.21f);
                Color disconnectedColor = new Color(0.45f, 0.45f, 0.45f, 0.65f);
                Color highlightColor = Color.Lerp(peerColor, Color.white, 0.2f);

                _tokenMaterials[peerId].color = peerId == localPeerId && isConnected
                    ? highlightColor
                    : (isConnected ? peerColor : disconnectedColor);
            }
        }

        private void BuildArenaVisuals()
        {
            if (_arenaRoot != null)
            {
                return;
            }

            EnsureLighting();
            EnsureCamera();

            _arenaRoot = new GameObject("TwoPeerArenaVisuals");
            _arenaRoot.transform.SetParent(transform, false);

            _tileEvenMaterial = CreateMaterial(new Color(0.2f, 0.24f, 0.29f));
            _tileOddMaterial = CreateMaterial(new Color(0.15f, 0.19f, 0.23f));

            for (int y = 0; y < TwoPeerArenaSimulation.BoardHeight; y++)
            {
                for (int x = 0; x < TwoPeerArenaSimulation.BoardWidth; x++)
                {
                    var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    tile.name = $"Tile_{x}_{y}";
                    tile.transform.SetParent(_arenaRoot.transform, false);
                    tile.transform.position = CellToWorld(x, y, 0f);
                    tile.transform.localScale = new Vector3(0.94f, 0.08f, 0.94f);

                    var renderer = tile.GetComponent<Renderer>();
                    renderer.sharedMaterial = ((x + y) % 2 == 0) ? _tileEvenMaterial : _tileOddMaterial;
                }
            }

            _tokenTransforms = new Transform[2];
            _tokenMaterials = new Material[2];
            for (int i = 0; i < _tokenTransforms.Length; i++)
            {
                var token = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                token.name = $"PeerToken_{i}";
                token.transform.SetParent(_arenaRoot.transform, false);
                token.transform.localScale = new Vector3(0.55f, 0.65f, 0.55f);

                var renderer = token.GetComponent<Renderer>();
                _tokenMaterials[i] = CreateMaterial(i == 0 ? new Color(0.19f, 0.56f, 0.93f) : new Color(0.94f, 0.55f, 0.21f));
                renderer.sharedMaterial = _tokenMaterials[i];
                _tokenTransforms[i] = token.transform;
            }
        }

        private void EnsureLighting()
        {
            if (FindAnyObjectByType<Light>() != null)
            {
                return;
            }

            var lightRoot = new GameObject("TwoPeerArenaLight");
            lightRoot.transform.SetParent(transform, false);
            lightRoot.transform.rotation = Quaternion.Euler(55f, -30f, 0f);

            var light = lightRoot.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.color = new Color(1f, 0.96f, 0.9f);
        }

        private void EnsureCamera()
        {
            if (Camera.main != null)
            {
                return;
            }

            var cameraRoot = new GameObject("TwoPeerArenaCamera");
            cameraRoot.transform.SetParent(transform, false);
            cameraRoot.transform.position = new Vector3(0f, 8.75f, -4.8f);
            cameraRoot.transform.rotation = Quaternion.Euler(58f, 0f, 0f);

            var camera = cameraRoot.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.1f, 0.12f);
            camera.fieldOfView = 42f;
        }

        private Vector3 CellToWorld(int cellX, int cellY, float height)
        {
            float originX = -(TwoPeerArenaSimulation.BoardWidth - 1) * 0.5f;
            float originZ = -(TwoPeerArenaSimulation.BoardHeight - 1) * 0.5f;
            return new Vector3(originX + cellX, height, originZ + cellY);
        }

        private ushort ParsePort()
        {
            return ushort.TryParse(hostPort, out ushort parsedPort) ? parsedPort : (ushort)7777;
        }

        private string BuildArenaScenePath()
        {
            return _controller.ArenaScenePath;
        }

        private string FormatConnectedPeers()
        {
            var connectedPeers = _controller.SessionState.ConnectedPeerIds;
            if (connectedPeers.Length == 0)
            {
                return "none";
            }

            string[] parts = new string[connectedPeers.Length];
            for (int i = 0; i < connectedPeers.Length; i++)
            {
                parts[i] = connectedPeers[i].ToString();
            }

            return string.Join(", ", parts);
        }

        private static Material CreateMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            var material = new Material(shader);
            material.color = color;
            return material;
        }

        private static void DestroyMaterial(ref Material material)
        {
            if (material == null)
            {
                return;
            }

            Destroy(material);
            material = null;
        }
    }
}
