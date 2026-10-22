using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using PluginSdk.Commands;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Plugins;
using VRage.Utils;
using Voidwright.Core;

namespace Voidwright.ServerPlugin
{
    /// <summary>
    /// Separate read-only server plugin for player-consented Driving Lab capture.
    /// It exposes consent chat commands but contains no actuator or control-dispatch code.
    /// </summary>
    public sealed class DrivingLabPassiveTelemetryPlugin : IPlugin
    {
        internal const byte DiagnoseRequest = 3;
        private readonly DrivingLabPassiveTelemetryAdapter telemetry = new DrivingLabPassiveTelemetryAdapter();
        private bool enabled;
        private bool handlerRegistered;
        private string runId;

        public void Init(object gameInstance)
        {
            runId = Environment.GetEnvironmentVariable("VRAGECAGE_PASSIVE_TELEMETRY_RUN_ID");
            var contractSha256 = Environment.GetEnvironmentVariable("VRAGECAGE_PASSIVE_TELEMETRY_CONTRACT_SHA256");
            enabled = Environment.GetEnvironmentVariable("VRAGECAGE_PASSIVE_TELEMETRY_AUTHORITY") ==
                          "read_only_observation" && IsIdentity(runId) &&
                      IsSha256(contractSha256);
            if (!enabled)
                throw new InvalidOperationException("Passive telemetry plugin requires a verified read-only observer run.");
            PassiveTelemetryCommands.Attach(this);
            ServerCommands.Register(Assembly.GetExecutingAssembly(), typeof(PassiveTelemetryCommands));
            EnsureMessageHandler();
            MyLog.Default.WriteLineAndConsole(
                "[VRageCage][DRIVING-LAB][PASSIVE] read-only observer loaded; actuator dispatch unavailable.");
            MyLog.Default.WriteLineAndConsole(
                "[VRageCage][DRIVING-LAB][PASSIVE] player chat commands registered: !vw telemetry help|diagnose|consent|stop.");
        }

        public void Update()
        {
            if (!enabled) return;
            EnsureMessageHandler();
            telemetry.Update();
        }

        public void Dispose()
        {
            if (handlerRegistered && MyAPIGateway.Multiplayer != null)
                MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(
                    DrivingLabConsentWire.MessageId, OnConsentMessage);
            handlerRegistered = false;
            telemetry.StopAndExport();
            PassiveTelemetryCommands.Detach(this);
            enabled = false;
        }

        private void EnsureMessageHandler()
        {
            if (!enabled || handlerRegistered || MyAPIGateway.Multiplayer == null) return;
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(
                DrivingLabConsentWire.MessageId, OnConsentMessage);
            handlerRegistered = true;
        }

        private void OnConsentMessage(ushort messageId, byte[] payload, ulong senderSteamId, bool fromServer)
        {
            if (!enabled || messageId != DrivingLabConsentWire.MessageId || fromServer || payload == null ||
                MyAPIGateway.Players == null || MyAPIGateway.Multiplayer == null) return;
            byte operation;
            if (!DrivingLabConsentWire.TryReadRequest(payload, out operation))
            {
                Reply(senderSteamId, false, "Invalid request.");
                return;
            }
            IMyPlayer player;
            string error;
            if (!TryResolvePlayer(senderSteamId, 0, out player, out error))
            {
                Reply(senderSteamId, false, error);
                return;
            }
            if (operation == DrivingLabConsentWire.StopRequest)
            {
                var revoked = telemetry.Revoke(player.IdentityId);
                Reply(senderSteamId, revoked, revoked
                    ? "Capture revoked; bounded files are being exported."
                    : "No active capture is bound to your identity.");
                return;
            }

            long gridId;
            var sessionId = Guid.NewGuid().ToString("N").ToLowerInvariant();
            string startFailure;
            var started = TryStart(player, sessionId, out gridId, out startFailure);
            Reply(senderSteamId, started, started
                ? "Capture started for grid " + gridId.ToString(CultureInfo.InvariantCulture) +
                  ". Use !vw telemetry stop to revoke."
                : "Capture not started: " + startFailure + ". Run !vw telemetry diagnose again for a fresh read-only check.");
        }

        internal string RunPlayerCommand(CommandCaller caller, byte operation)
        {
            if (operation != DrivingLabConsentWire.StartRequest && operation != DrivingLabConsentWire.StopRequest &&
                operation != DiagnoseRequest)
                return "Unknown telemetry command. Use !vw telemetry help.";
            if (!enabled)
                return "The read-only telemetry observer is not active.";

            if (caller.IsConsole)
                return "This command is available to connected players only.";
            if (caller.SteamId == 0 || caller.IdentityId <= 0)
                return "The authenticated player could not be resolved.";

            IMyPlayer player;
            string error;
            if (!TryResolvePlayer(caller.SteamId, caller.IdentityId, out player, out error))
                return error;

            if (operation == DrivingLabConsentWire.StopRequest)
                return telemetry.Revoke(player.IdentityId)
                    ? "Capture revoked; bounded files are being exported."
                    : "No active capture is bound to your identity.";

            if (operation == DiagnoseRequest)
                return DiagnosePlayer(player);

            long gridId;
            var sessionId = Guid.NewGuid().ToString("N").ToLowerInvariant();
            string startFailure;
            return TryStart(player, sessionId, out gridId, out startFailure)
                ? "Capture started for your occupied grid " + gridId.ToString(CultureInfo.InvariantCulture) +
                  ". Use !vw telemetry stop to revoke."
                : "Capture not started: " + startFailure + ". Run !vw telemetry diagnose again for a fresh read-only check.";
        }

        private static bool TryResolvePlayer(ulong steamId, long expectedIdentityId, out IMyPlayer resolvedPlayer, out string error)
        {
            resolvedPlayer = null;
            error = "The authenticated player could not be resolved.";
            if (steamId == 0 || expectedIdentityId < 0 || MyAPIGateway.Players == null)
                return false;

            var identityId = MyAPIGateway.Players.TryGetIdentityId(steamId);
            if (identityId <= 0 || (expectedIdentityId > 0 && identityId != expectedIdentityId))
                return false;

            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players,
                player => player != null && !player.IsBot && player.IdentityId == identityId);
            if (players.Count != 1)
            {
                error = "A unique connected player identity is required.";
                return false;
            }

            resolvedPlayer = players[0];
            error = null;
            return true;
        }

        private bool TryStart(IMyPlayer player, string sessionId, out long gridId, out string failure)
        {
            gridId = 0;
            failure = "the read-only observer or game session is unavailable";
            if (!enabled || MyAPIGateway.Session == null)
                return false;
            string seatReport;
            if (!TryFindUniqueOccupiedCockpitGrid(player, out gridId, out seatReport))
            {
                failure = seatReport;
                return false;
            }
            var rejection = DrivingLabPassiveTelemetryRejection.InvalidIdentity;
            var started = telemetry.TryStart(player, sessionId, runId, gridId, DateTime.UtcNow,
                DateTime.UtcNow.AddMinutes(15), DrivingLabPassiveTelemetryPolicy.MaximumSampleRateHz,
                DrivingLabPassiveTelemetryPolicy.MaximumBufferedRecords,
                DrivingLabPassiveTelemetryPolicy.MaximumBufferedBytes, out rejection);
            if (!started) failure = "shared consent checks rejected the current state: " + rejection;
            return started;
        }

        private string DiagnosePlayer(IMyPlayer player)
        {
            if (telemetry.IsActive)
                return "A telemetry capture is already active; diagnose was read-only and started nothing.";
            if (MyAPIGateway.Session == null)
                return "Diagnosis failed: game session is unavailable.";

            long gridId;
            string seatReport;
            if (!TryFindUniqueOccupiedCockpitGrid(player, out gridId, out seatReport))
                return "Occupied-controller match failed: " + seatReport;

            var rejection = telemetry.Diagnose(player, Guid.NewGuid().ToString("N").ToLowerInvariant(),
                runId, gridId, DateTime.UtcNow);
            return rejection == DrivingLabPassiveTelemetryRejection.None
                ? "Occupied-controller match passed: " + seatReport + ". Policy checks pass; no telemetry was started."
                : "Occupied-controller match passed: " + seatReport + ". Policy rejected capture: " + rejection + ". No telemetry was started.";
        }

        private static bool TryFindUniqueOccupiedCockpitGrid(IMyPlayer player, out long gridEntityId)
        {
            string ignored;
            return TryFindUniqueOccupiedCockpitGrid(player, out gridEntityId, out ignored);
        }

        private static bool TryFindUniqueOccupiedCockpitGrid(IMyPlayer player, out long gridEntityId, out string report)
        {
            gridEntityId = 0;
            report = "no cockpit inventory";
            if (player == null || player.IsBot || player.IdentityId <= 0 || MyAPIGateway.Entities == null)
            {
                report = "player or entity inventory is unavailable";
                return false;
            }
            var entities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(entities, entity => entity is IMyCubeGrid);
            var controlledEntity = player.Controller == null || player.Controller.ControlledEntity == null
                ? null : player.Controller.ControlledEntity.Entity;
            var controlledController = controlledEntity as IMyShipController;
            var controlledCockpit = controlledEntity as IMyCockpit;
            var matches = new List<long>();
            var matchingTypes = new List<string>();
            var cockpitCount = 0;
            var emptyPilots = 0;
            var nonPlayerPilots = 0;
            var botPilots = 0;
            var missingControllerInfo = 0;
            var otherIdentityPilots = 0;
            var subtypeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var entity in entities)
            {
                var grid = entity as IMyCubeGrid;
                if (grid == null || grid.MarkedForClose) continue;
                foreach (var cockpit in grid.GetFatBlocks<IMyCockpit>())
                {
                    if (cockpit == null || cockpit.MarkedForClose) continue;
                    cockpitCount++;
                    var subtype = string.IsNullOrWhiteSpace(cockpit.BlockDefinition.SubtypeName)
                        ? "unknown" : cockpit.BlockDefinition.SubtypeName;
                    int subtypeCount;
                    subtypeCounts.TryGetValue(subtype, out subtypeCount);
                    subtypeCounts[subtype] = subtypeCount + 1;
                    var pilot = cockpit.Pilot;
                    if (pilot == null)
                    {
                        emptyPilots++;
                        continue;
                    }
                    if (!pilot.IsPlayer) { nonPlayerPilots++; continue; }
                    if (pilot.IsBot) { botPilots++; continue; }
                    if (pilot.ControllerInfo == null) { missingControllerInfo++; continue; }
                    if (pilot.ControllerInfo.ControllingIdentityId != player.IdentityId)
                        otherIdentityPilots++;
                }
                // Some game builds expose the authenticated player's occupied
                // controller while its Pilot field is null. Bind only the exact
                // controller object returned by that player's game controller.
                IMyTerminalBlock controlledBlock = (IMyTerminalBlock)controlledController ?? controlledCockpit;
                if (controlledBlock != null && !controlledBlock.MarkedForClose &&
                    controlledBlock.CubeGrid != null && controlledBlock.CubeGrid.EntityId == grid.EntityId &&
                    controlledBlock.EntityId != 0)
                {
                    if (!matches.Contains(grid.EntityId)) matches.Add(grid.EntityId);
                    var block = controlledController != null ? (IMyTerminalBlock)controlledController : controlledCockpit;
                    if (matchingTypes.Count < matches.Count)
                        matchingTypes.Add(block.BlockDefinition.SubtypeName ?? "unknown");
                }
            }
            var subtypeReport = string.Join(",", subtypeCounts.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Key + "=" + pair.Value.ToString(CultureInfo.InvariantCulture)));
            var pilotReport = "pilot states empty=" + emptyPilots.ToString(CultureInfo.InvariantCulture) +
                ",nonPlayer=" + nonPlayerPilots.ToString(CultureInfo.InvariantCulture) +
                ",bot=" + botPilots.ToString(CultureInfo.InvariantCulture) +
                ",controllerInfoMissing=" + missingControllerInfo.ToString(CultureInfo.InvariantCulture) +
                ",otherIdentity=" + otherIdentityPilots.ToString(CultureInfo.InvariantCulture);
            var controlTarget = DescribePlayerControlledEntity(player);
            if (matches.Count != 1 || matches[0] <= 0)
            {
                report = "found " + cockpitCount.ToString(CultureInfo.InvariantCulture) +
                    " cockpit(s), occupied matches=" + matches.Count.ToString(CultureInfo.InvariantCulture) +
                    "; " + pilotReport + "; subtypes=" + (subtypeReport.Length == 0 ? "none" : subtypeReport) +
                    "; player control target=" + controlTarget + ".";
                return false;
            }
            gridEntityId = matches[0];
            report = "one occupied cockpit matched (subtype=" + matchingTypes[0] +
                "); player control target=" + controlTarget;
            return true;
        }

        private static string DescribePlayerControlledEntity(IMyPlayer player)
        {
            if (player == null || player.Controller == null) return "controller-unavailable";
            var controlled = player.Controller.ControlledEntity;
            if (controlled == null || controlled.Entity == null) return "no-controlled-entity";
            var entity = controlled.Entity;
            var cockpit = entity as IMyCockpit;
            if (cockpit != null)
                return "cockpit/" + (cockpit.BlockDefinition.SubtypeName ?? "unknown");
            var shipController = entity as IMyShipController;
            if (shipController != null)
                return "ship-controller/" + (shipController.BlockDefinition.SubtypeName ?? "unknown");
            if (entity is IMyCharacter) return "character";
            if (entity is IMyCubeGrid) return "grid";
            return "other-entity";
        }

        private static void Reply(ulong recipient, bool accepted, string message)
        {
            if (MyAPIGateway.Multiplayer != null)
                MyAPIGateway.Multiplayer.SendMessageTo(DrivingLabConsentWire.MessageId,
                    DrivingLabConsentWire.CreateResult(accepted, message), recipient, true);
        }

        private static bool IsIdentity(string value)
        {
            if (value == null || value.Length != 32) return false;
            foreach (var character in value)
                if (!Uri.IsHexDigit(character)) return false;
            return true;
        }

        private static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (var character in value)
                if (!Uri.IsHexDigit(character)) return false;
            return true;
        }
    }
}
