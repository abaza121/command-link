using CrossCut.CommandLink.Diagnostics;

namespace CrossCut.CommandLink
{
    /// <summary>
    /// Stores the active runtime bridge instances shared across the CommandLink package.
    /// </summary>
    public static class CommandLinkRuntimeRegistry
    {
        private static ICommandLinkRuntimeHooks _runtimeHooks = NullCommandLinkRuntimeHooks.Instance;

        /// <summary>
        /// Host-provided runtime hooks that integrate CommandLink with the active simulation runner.
        /// </summary>
        public static ICommandLinkRuntimeHooks RuntimeHooks
        {
            get => _runtimeHooks;
            set => _runtimeHooks = value ?? NullCommandLinkRuntimeHooks.Instance;
        }

        /// <summary>
        /// Active network engine instance when a CommandLink session is running.
        /// </summary>
        public static CommandLinkNetworkEngine Engine { get; internal set; }

        /// <summary>
        /// Indicates whether the bridge is being driven by a MonoBehaviour instead of ECS systems.
        /// </summary>
        public static bool DriveFromMonoBehaviour { get; set; } = true;

        /// <summary>
        /// Returns a fresh diagnostics snapshot for editor and runtime tooling.
        /// </summary>
        public static CommandLinkDiagnosticsSnapshot Diagnostics => CommandLinkDiagnosticsService.CopySnapshot();
    }
}
