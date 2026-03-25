using System.IO;
using System.Text;
using CrossCut.CommandLink;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CrossCut.CommandLink.Diagnostics
{
    /// <summary>
    /// Editor-facing UI Toolkit window for inspecting live CommandLink diagnostics and exporting snapshots.
    /// </summary>
    public sealed class CommandLinkDiagnosticsWindow : EditorWindow
    {
        private Label _sessionLabel;
        private Label _queueLabel;
        private Label _stallLabel;
        private VisualElement _frameStrip;
        private ScrollView _traceScrollView;
        private double _nextRefreshAt;

        /// <summary>
        /// Opens the diagnostics window from the Unity editor menu.
        /// </summary>
        [MenuItem("Window/CrossCut/CommandLink Diagnostics")]
        public static void Open()
        {
            var window = GetWindow<CommandLinkDiagnosticsWindow>();
            window.titleContent = new GUIContent("CommandLink Diagnostics");
            window.minSize = new Vector2(760f, 520f);
        }

        /// <summary>
        /// Builds the UI and starts the periodic refresh loop when the window is created.
        /// </summary>
        private void OnEnable()
        {
            BuildUi();
            EditorApplication.update += OnEditorUpdate;
            RefreshNow();
        }

        /// <summary>
        /// Stops the periodic refresh loop when the window is closed.
        /// </summary>
        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        private void BuildUi()
        {
            var root = rootVisualElement;
            root.Clear();
            root.style.paddingLeft = 12f;
            root.style.paddingRight = 12f;
            root.style.paddingTop = 12f;
            root.style.paddingBottom = 12f;

            var toolbar = new VisualElement();
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.marginBottom = 10f;
            root.Add(toolbar);

            var refreshButton = new Button(RefreshNow) { text = "Refresh" };
            toolbar.Add(refreshButton);

            var exportButton = new Button(ExportSnapshot) { text = "Export JSON" };
            exportButton.style.marginLeft = 8f;
            toolbar.Add(exportButton);

            _sessionLabel = new Label();
            _sessionLabel.style.whiteSpace = WhiteSpace.Normal;
            root.Add(_sessionLabel);

            _queueLabel = new Label();
            _queueLabel.style.whiteSpace = WhiteSpace.Normal;
            _queueLabel.style.marginTop = 4f;
            root.Add(_queueLabel);

            _stallLabel = new Label();
            _stallLabel.style.whiteSpace = WhiteSpace.Normal;
            _stallLabel.style.marginTop = 4f;
            root.Add(_stallLabel);

            var frameHeader = new Label("Frame Window");
            frameHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            frameHeader.style.marginTop = 12f;
            root.Add(frameHeader);

            _frameStrip = new VisualElement();
            _frameStrip.style.flexDirection = FlexDirection.Row;
            _frameStrip.style.flexWrap = Wrap.Wrap;
            _frameStrip.style.marginTop = 4f;
            root.Add(_frameStrip);

            var traceHeader = new Label("Recent Build Traces");
            traceHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            traceHeader.style.marginTop = 12f;
            root.Add(traceHeader);

            _traceScrollView = new ScrollView(ScrollViewMode.Vertical);
            _traceScrollView.style.flexGrow = 1f;
            _traceScrollView.style.marginTop = 4f;
            root.Add(_traceScrollView);
        }

        private void OnEditorUpdate()
        {
            if (EditorApplication.timeSinceStartup < _nextRefreshAt)
            {
                return;
            }

            RefreshNow();
        }

        private void RefreshNow()
        {
            _nextRefreshAt = EditorApplication.timeSinceStartup + 0.25d;
            Render(CommandLinkRuntimeRegistry.Diagnostics);
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

            for (int i = 0; i < traces.Length; i++)
            {
                var trace = traces[i];
                var label = new Label(BuildTraceToText(trace));
                label.style.whiteSpace = WhiteSpace.Normal;
                label.style.marginBottom = 8f;
                label.style.color = ResolveTraceColor(trace);
                _traceScrollView.Add(label);
            }
        }

        private void ExportSnapshot()
        {
            string path = EditorUtility.SaveFilePanel("Export CommandLink Diagnostics", Application.dataPath, "commandlink-diagnostics", "json");
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            File.WriteAllText(path, JsonUtility.ToJson(CommandLinkRuntimeRegistry.Diagnostics, true), Encoding.UTF8);
            AssetDatabase.Refresh();
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
