using PluginSdk.Commands;
using VRage.Game.ModAPI;
using Voidwright.Core;

namespace Voidwright.ServerPlugin
{
    [CommandRoot("vw", "Driving Lab telemetry", "Player-consented read-only vehicle telemetry.")]
    public sealed class PassiveTelemetryCommands : CommandModule
    {
        private static DrivingLabPassiveTelemetryPlugin activePlugin;

        internal static void Attach(DrivingLabPassiveTelemetryPlugin plugin)
        {
            activePlugin = plugin;
        }

        internal static void Detach(DrivingLabPassiveTelemetryPlugin plugin)
        {
            if (activePlugin == plugin) activePlugin = null;
        }

        [Command("telemetry help", "Explains the read-only telemetry opt-in.",
            "Capture is limited to your exact occupied cockpit grid, up to 10 Hz, 15 minutes, and 64 MiB. " +
            "Only grid pose, speed, simulation time, and wheel actuator/brake state are recorded. " +
            "Nothing starts until you enter !vw telemetry consent. Use !vw telemetry stop to revoke.")]
        [Permission(MyPromoteLevel.None)]
        public string Help()
        {
            return "Read-only capture binds your authenticated player to your currently occupied cockpit grid. " +
                   "It records grid pose, speed, simulation time, and wheel actuator/brake state at up to 10 Hz, " +
                   "for no more than 15 minutes or 64 MiB. Keyboard, gamepad, and human intent are not collected. " +
                   "Type !vw telemetry consent to opt in or !vw telemetry stop to revoke.";
        }

        [Command("telemetry consent", "Start bounded telemetry for your occupied grid after explicit opt-in.",
            "Starts capture for your current occupied cockpit grid only. No command or actuator control is available.")]
        [Permission(MyPromoteLevel.None)]
        public string Consent()
        {
            return Execute(DrivingLabConsentWire.StartRequest);
        }

        [Command("telemetry stop", "Revoke your telemetry consent and stop your capture.",
            "Stops only the capture bound to your authenticated player identity.")]
        [Permission(MyPromoteLevel.None)]
        public string Stop()
        {
            return Execute(DrivingLabConsentWire.StopRequest);
        }

        [Command("telemetry diagnose", "Check player-to-cockpit binding without starting telemetry.",
            "Reports whether your authenticated identity matches an occupied cockpit and whether the read-only policy would allow capture. Does not start capture.")]
        [Permission(MyPromoteLevel.None)]
        public string Diagnose()
        {
            if (activePlugin == null)
                return "The read-only telemetry observer is not active.";
            return activePlugin.RunPlayerCommand(Context.Caller, DrivingLabPassiveTelemetryPlugin.DiagnoseRequest);
        }

        private string Execute(byte operation)
        {
            if (activePlugin == null)
                return "The read-only telemetry observer is not active.";
            return activePlugin.RunPlayerCommand(Context.Caller, operation);
        }
    }
}
