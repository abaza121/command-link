using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace CrossCut.CommandLink.Diagnostics
{
    /// <summary>
    /// Renders a lightweight runtime UI Toolkit overlay for inspecting CommandLink build synchronization state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CommandLinkDiagnosticsOverlay : MonoBehaviour
    {
        [SerializeField] private float refreshIntervalSeconds = 0.25f;
        [SerializeField] private int maxVisibleTraceRows = 8;

        private UIDocument _document;
        private PanelSettings _panelSettings;
        private Label _sessionLabel;
        private Label _queueLabel;
        private Label _stallLabel;
        private VisualElement _frameStrip;
        private ScrollView _traceScrollView;
        private float _nextRefreshAt;

        /// <summary>
        /// Spawns the diagnostics overlay automatically in editor and development builds.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BootstrapOverlay()
        {
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
            return;
#endif

            if (FindAnyObjectByType<CommandLinkDiagnosticsOverlay>() != null)
            {
                return;
            }

            var root = new GameObject(nameof(CommandLinkDiagnosticsOverlay));
            DontDestroyOnLoad(root);
            root.AddComponent<CommandLinkDiagnosticsOverlay>();
        }

        /// <summary>
        /// Creates the runtime document and the overlay UI tree.
        /// </summary>
        private void Awake()
        {
            EnsureDocument();
            BuildUi();
            RefreshNow();
        }

        /// <summary>
        /// Refreshes the overlay at a fixed cadence while play mode is active.
        /// </summary>
        private void Update()
        {
            if (Time.unscaledTime < _nextRefreshAt)
            {
                return;
            }

            RefreshNow();
        }

        /// <summary>
        /// Cleans up the runtime-created panel settings object when the overlay is destroyed.
        /// </summary>
        private void OnDestroy()
        {
            if (_panelSettings != null)
            {
                Destroy(_panelSettings);
            }
        }

        private void EnsureDocument()
        {
            _document = GetComponent<UIDocument>();
            if (_document == null)
            {
                _document = gameObject.AddComponent<UIDocument>();
            }

            if (_document.panelSettings != null)
            {
                return;
            }

            var sharedPanelSettings = Resources.Load<PanelSettings>("CommandLinkDiagnosticsPanelSettings");
            if (sharedPanelSettings != null)
            {
                sharedPanelSettings.sortingOrder = short.MaxValue;
                _document.panelSettings = sharedPanelSettings;
                return;
            }

            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _panelSettings.sortingOrder = short.MaxValue;
            _document.panelSettings = _panelSettings;
        }

        private void BuildUi()
        {
            var root = _document.rootVisualElement;
            root.Clear();
            root.style.flexGrow = 1f;

            var container = new VisualElement();
            container.style.position = Position.Absolute;
            container.style.left = 16f;
            container.style.top = 16f;
            container.style.width = 620f;
            container.style.maxHeight = 420f;
            container.style.backgroundColor = new Color(0.08f, 0.09f, 0.11f, 0.92f);
            container.style.borderTopLeftRadius = 10f;
            container.style.borderTopRightRadius = 10f;
            container.style.borderBottomLeftRadius = 10f;
            container.style.borderBottomRightRadius = 10f;
            container.style.paddingLeft = 12f;
            container.style.paddingRight = 12f;
            container.style.paddingTop = 10f;
            container.style.paddingBottom = 10f;
            container.style.color = new Color(0.92f, 0.94f, 0.97f);
            root.Add(container);

            var title = new Label("CommandLink Build Sync Diagnostics");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 16;
            container.Add(title);

            _sessionLabel = new Label();
            _sessionLabel.style.whiteSpace = WhiteSpace.Normal;
            _sessionLabel.style.marginTop = 6f;
            container.Add(_sessionLabel);

            _queueLabel = new Label();
            _queueLabel.style.whiteSpace = WhiteSpace.Normal;
            _queueLabel.style.marginTop = 4f;
            container.Add(_queueLabel);

            _stallLabel = new Label();
            _stallLabel.style.whiteSpace = WhiteSpace.Normal;
            _stallLabel.style.marginTop = 4f;
            container.Add(_stallLabel);

            var heatmapHeader = new Label("Frame Window");
            heatmapHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            heatmapHeader.style.marginTop = 10f;
            container.Add(heatmapHeader);

            _frameStrip = new VisualElement();
            _frameStrip.style.flexDirection = FlexDirection.Row;
            _frameStrip.style.flexWrap = Wrap.Wrap;
            _frameStrip.style.marginTop = 4f;
            container.Add(_frameStrip);

            var traceHeader = new Label("Recent Build Traces");
            traceHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            traceHeader.style.marginTop = 10f;
            container.Add(traceHeader);

            _traceScrollView = new ScrollView(ScrollViewMode.Vertical);
            _traceScrollView.style.height = 190f;
            _traceScrollView.style.marginTop = 4f;
            container.Add(_traceScrollView);
        }

        private void RefreshNow()
        {
            if (_document == null || _sessionLabel == null || _queueLabel == null || _stallLabel == null || _frameStrip == null || _traceScrollView == null)
            {
                EnsureDocument();
                if (_document == null || _document.rootVisualElement == null)
                {
                    return;
                }

                BuildUi();
            }

            _nextRefreshAt = Time.unscaledTime + Mathf.Max(0.05f, refreshIntervalSeconds);
            Render(CommandLinkDiagnosticsService.CopySnapshot());
        }

        private void Render(CommandLinkDiagnosticsSnapshot snapshot)
        {
            _sessionLabel.text =
                $"session={snapshot.SessionState} currentTick={snapshot.CurrentTick} peers={snapshot.ConnectedPeers} local={snapshot.LocalPeerId} host={snapshot.HostPeerId}";

            _queueLabel.text =
                $"queuedLocal={snapshot.QueuedLocalCount} pendingTicks={snapshot.PendingTicksSummary} inFlight={(snapshot.HasInFlightFrame ? $"{snapshot.InFlightTick}:{snapshot.InFlightSequence}" : "none")} " +
                $"acks={snapshot.PendingAckPeers} resendBacklog={snapshot.ResendBacklogCount} pendingBuilds={snapshot.PendingIntentSummary.BuildIntentCount} firstBuildPos={snapshot.PendingIntentSummary.FirstBuildIntentPosition}";

            _stallLabel.text =
                $"stall={snapshot.InferredStallReason} currentInputs={snapshot.CurrentInputsPresent}/{snapshot.RequiredPeerCount} targetInputs={snapshot.TargetInputsPresent}/{snapshot.RequiredPeerCount} " +
                $"currentBuilds={snapshot.CurrentBuildCommands} targetBuilds={snapshot.TargetBuildCommands}";

            RenderFrameWindow(snapshot.FrameWindow);
            RenderTraceRows(snapshot.BuildTraces);
        }

        private void RenderFrameWindow(FrameWindowSnapshot frameWindow)
        {
            _frameStrip.Clear();
            if (frameWindow == null || frameWindow.Entries == null || frameWindow.Entries.Length == 0)
            {
                _frameStrip.Add(new Label("No frame samples yet."));
                return;
            }

            for (int i = 0; i < frameWindow.Entries.Length; i++)
            {
                var entry = frameWindow.Entries[i];
                var cell = new VisualElement();
                cell.style.width = 8f;
                cell.style.height = 18f;
                cell.style.marginRight = 1f;
                cell.style.marginBottom = 1f;
                cell.style.backgroundColor = ResolveFrameColor(entry);
                cell.tooltip =
                    $"tick={entry.Tick}\ninputs={entry.InputsPresent}/{entry.RequiredPeerCount}\npeerMask=0x{entry.PresentPeerMask:X}\n" +
                    $"queuedLocal={entry.QueuedLocalCount}\nresendBacklog={entry.ResendBacklogCount}\nmissingInputs={entry.MissingInputs}\n" +
                    $"buildCommands={entry.BuildCommandCount}\nstall={entry.StallReason}";
                _frameStrip.Add(cell);
            }
        }

        private void RenderTraceRows(BuildSyncTraceRecord[] traces)
        {
            _traceScrollView.Clear();
            if (traces == null || traces.Length == 0)
            {
                _traceScrollView.Add(new Label("No build traces recorded yet."));
                return;
            }

            int visibleCount = Mathf.Min(maxVisibleTraceRows, traces.Length);
            for (int i = 0; i < visibleCount; i++)
            {
                var trace = traces[i];
                var label = new Label(BuildTraceToText(trace));
                label.style.whiteSpace = WhiteSpace.Normal;
                label.style.marginBottom = 6f;
                label.style.color = ResolveTraceColor(trace);
                _traceScrollView.Add(label);
            }
        }

        private static string BuildTraceToText(BuildSyncTraceRecord trace)
        {
            var sb = new StringBuilder(256);
            sb.Append('#').Append(trace.TraceId)
                .Append(" p").Append(trace.PeerId)
                .Append(" type=").Append(trace.BuildingTypeId)
                .Append(" cell=(").Append(trace.TargetCellX).Append(',').Append(trace.TargetCellY).Append(')')
                .Append(" targetTick=").Append(trace.InputTargetTick)
                .Append(" seq=").Append(trace.Sequence)
                .Append(" stage=").Append(trace.Stage)
                .Append(" wait=").Append(string.IsNullOrWhiteSpace(trace.InferredWaitReason) ? "none" : trace.InferredWaitReason)
                .Append(" latency=").Append(trace.LatencyTicks);

            if (!string.IsNullOrWhiteSpace(trace.FailureReason))
            {
                sb.Append(" reason=").Append(trace.FailureReason);
            }

            sb.Append('\n').Append(trace.StageTimeline);
            return sb.ToString();
        }

        private static Color ResolveFrameColor(FrameWindowEntry entry)
        {
            if (entry.WaitingForAck)
            {
                return new Color(0.92f, 0.57f, 0.17f);
            }

            if (entry.WaitingForRemoteFrame)
            {
                return new Color(0.82f, 0.24f, 0.24f);
            }

            if (entry.BuildCommandCount > 0 || entry.HasPendingBuildIntent)
            {
                return new Color(0.18f, 0.57f, 0.93f);
            }

            return entry.InputsPresent >= entry.RequiredPeerCount
                ? new Color(0.22f, 0.72f, 0.38f)
                : new Color(0.26f, 0.29f, 0.34f);
        }

        private static Color ResolveTraceColor(BuildSyncTraceRecord trace)
        {
            if (trace.Stage == BuildSyncStage.RejectedInvalidPlacement || trace.Stage == BuildSyncStage.RejectedNotReady)
            {
                return new Color(0.98f, 0.55f, 0.55f);
            }

            if (!string.IsNullOrWhiteSpace(trace.InferredWaitReason) && trace.InferredWaitReason != "none")
            {
                return new Color(0.96f, 0.8f, 0.47f);
            }

            if (trace.Stage == BuildSyncStage.LinkFlushed || trace.Stage == BuildSyncStage.MirrorCreated)
            {
                return new Color(0.63f, 0.93f, 0.69f);
            }

            return new Color(0.92f, 0.94f, 0.97f);
        }
    }
}
