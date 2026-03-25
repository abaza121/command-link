namespace CrossCut.CommandLink
{
    /// <summary>
    /// Forwards resolved frames to the host-provided simulation integration hooks.
    /// </summary>
    public static class SimulationCommandInboxBridge
    {
        /// <summary>
        /// Applies the resolved command frame for one tick through the registered runtime hooks.
        /// </summary>
        public static void ApplyResolvedFrame(uint tick, in ResolvedInputFrame resolvedFrame)
        {
            CommandLinkRuntimeRegistry.RuntimeHooks.TryApplyResolvedFrame(tick, resolvedFrame);
        }
    }
}
