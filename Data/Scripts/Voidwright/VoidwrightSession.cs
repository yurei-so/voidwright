using Sandbox.ModAPI;
using System;
using System.Globalization;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Voidwright.Game;
using Voidwright.Core;
using VRage.Game.Components;
using VRage.Utils;

namespace Voidwright
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public sealed class VoidwrightSession : MySessionComponentBase
    {
        private VehicleProbe probe;
        private RoutePreviewManager previews;
        private NavigationControllerRegistry navigationControllers;
        private AgentCoreSnapshotFileExporter snapshotExporter;
        private BlockLifecycleDiagnosticExporter blockLifecycleDiagnostic;
        private VirtualCameraMarkerManager cameraMarkers;
        private VoidwrightLabPreflight labPreflight;
        private readonly Dictionary<long, string> observedSignatures = new Dictionary<long, string>();
        private readonly Dictionary<long, string> controllerSignatures = new Dictionary<long, string>();
        private int updateCountdown = 100;
        private long simulationTick;
        private string sessionLivenessSessionId;
        private string lastSessionLivenessState;
        private string lastSessionLivenessReason;
        private long lastSessionLivenessPublishTick = long.MinValue;
        private string lastSessionLivenessWriteError;
        private bool roleLogged;
        private bool telemetryConsentUiRegistered;
        private bool loadLifecycleCaptureAttempted;
        private bool preSaveLifecycleCaptureAttempted;
        private string bootstrapNonce;

        public override void LoadData()
        {
            probe = new VehicleProbe();
            previews = new RoutePreviewManager();
            navigationControllers = new NavigationControllerRegistry();
            sessionLivenessSessionId = Guid.NewGuid().ToString("N");
            var bootstrap = ReadLabBootstrap();
            bootstrapNonce = BootstrapValue(bootstrap, "bootstrapNonce");
            var labRunId = BootstrapValue(bootstrap, "runId");
            var fixtureId = BootstrapValue(bootstrap, "fixtureId");
            snapshotExporter = new AgentCoreSnapshotFileExporter(sessionLivenessSessionId, labRunId, fixtureId, bootstrapNonce);
            var diagnosticTarget = ReadBlockLifecycleTarget(fixtureId);
            if (diagnosticTarget != null)
                blockLifecycleDiagnostic = new BlockLifecycleDiagnosticExporter(diagnosticTarget,
                    snapshotExporter.RunId, sessionLivenessSessionId, labRunId, fixtureId, bootstrapNonce);
            cameraMarkers = new VirtualCameraMarkerManager();
#if VOIDWRIGHT_ENABLE_LAB_PREFLIGHT
            labPreflight = new VoidwrightLabPreflight(snapshotExporter.RunId, sessionLivenessSessionId, bootstrapNonce);
#else
            // Read-only observation builds cannot construct the component that
            // owns the ordinary wheel-actuation path. Enable it only in an
            // explicitly configured control-capable build.
            labPreflight = null;
#endif
            TryRegisterTelemetryConsentUi();
            if (MyAPIGateway.Utilities != null && !MyAPIGateway.Utilities.IsDedicated)
                MyAPIGateway.Utilities.ShowMessage("Voidwright", "observation loaded; place an Agent Core to enroll a grid");
            MyLog.Default.WriteLineAndConsole("Voidwright: observation loaded; Agent Core enrollment is read-only and control adapters are inactive.");
        }

        private static string ReadLabBootstrap()
        {
            const string fileName = "vragecage-lab-bootstrap-v1.json";
            if (MyAPIGateway.Utilities == null ||
                !MyAPIGateway.Utilities.FileExistsInWorldStorage(fileName, typeof(VoidwrightSession)))
                return string.Empty;
            try
            {
                using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(fileName, typeof(VoidwrightSession)))
                {
                    var buffer = new char[4097];
                    var count = reader.ReadBlock(buffer, 0, buffer.Length);
                    if (count < 0 || count > 4096) return string.Empty;
                    return new string(buffer, 0, count);
                }
            }
            catch (Exception error)
            {
                MyLog.Default.WriteLineAndConsole("Voidwright: lab bootstrap is unavailable: " + error.GetType().Name);
                return string.Empty;
            }
        }

        private static string BootstrapValue(string json, string name)
        {
            if (string.IsNullOrEmpty(json)) return string.Empty;
            var match = Regex.Match(json, "\\\"" + Regex.Escape(name) + "\\\"\\s*:\\s*\\\"([0-9a-f]{32})\\\"");
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        public override void UpdateBeforeSimulation()
        {
            simulationTick++;
            if (!loadLifecycleCaptureAttempted)
            {
                loadLifecycleCaptureAttempted = true;
                if (blockLifecycleDiagnostic != null)
                    blockLifecycleDiagnostic.Capture("after-entity-initialization", simulationTick);
            }
            TryRegisterTelemetryConsentUi();
            if (labPreflight != null) labPreflight.Update(simulationTick);
            PublishSessionLiveness(simulationTick);
            updateCountdown--;
            if (updateCountdown > 0) return;
            updateCountdown = 100;
            if (probe == null)
                return;

            var dedicated = MyAPIGateway.Utilities != null && MyAPIGateway.Utilities.IsDedicated;
            var multiplayerAvailable = MyAPIGateway.Multiplayer != null;
            var multiplayerServer = multiplayerAvailable && MyAPIGateway.Multiplayer.IsServer;
            if (!roleLogged)
            {
                roleLogged = true;
                MyLog.Default.WriteLineAndConsole(
                    "Voidwright probe role: dedicated=" + dedicated +
                    " multiplayerAvailable=" + multiplayerAvailable +
                    " multiplayerServer=" + multiplayerServer);
            }
            // Dedicated hosts may expose Multiplayer late or not at all under the
            // headless loader. Observation is read-only, so either trustworthy
            // server signal is sufficient; clients still remain excluded.
            if (!dedicated && !multiplayerServer)
            {
                return;
            }

            var current = new HashSet<long>();
            var snapshots = probe.ObserveOptedInVehicles();
            foreach (var snapshot in snapshots)
            {
                current.Add(snapshot.ControllerId);
                var signature = snapshot.Signature();
                string previous;
                if (observedSignatures.TryGetValue(snapshot.ControllerId, out previous) && previous == signature)
                    continue;
                observedSignatures[snapshot.ControllerId] = signature;
                MyLog.Default.WriteLineAndConsole("Voidwright probe: " + snapshot);
                MyLog.Default.WriteLineAndConsole("Voidwright evidence: type=vehicle " + snapshot);
                if (MyAPIGateway.Utilities != null && !MyAPIGateway.Utilities.IsDedicated)
                    MyAPIGateway.Utilities.ShowNotification("Voidwright observed " + snapshot.Mobility + " vehicle: " + snapshot.ControllerName, 4000, "White");
            }

            var removed = new List<long>();
            foreach (var controllerId in observedSignatures.Keys)
                if (!current.Contains(controllerId)) removed.Add(controllerId);
            foreach (var controllerId in removed)
            {
                observedSignatures.Remove(controllerId);
                MyLog.Default.WriteLineAndConsole("Voidwright probe: controller " + controllerId + " no longer opted in.");
            }
            if (previews != null) previews.Update(snapshots);
            ObserveNavigationControllers();
            if (cameraMarkers != null && snapshotExporter != null)
                cameraMarkers.Broadcast(snapshotExporter.RunId);
        }

        private void ObserveNavigationControllers()
        {
            if (navigationControllers == null) return;
            var active = new HashSet<long>();
            var snapshots = navigationControllers.Observe(simulationTick);
            if (snapshotExporter != null) snapshotExporter.Update(snapshots, simulationTick);
            foreach (var controller in snapshots)
            {
                active.Add(controller.EntityId);
                var signature = controller.Signature();
                string previous;
                if (controllerSignatures.TryGetValue(controller.EntityId, out previous) && previous == signature)
                    continue;
                controllerSignatures[controller.EntityId] = signature;
                MyLog.Default.WriteLineAndConsole("Voidwright Agent Core: name='" + controller.Name +
                                                  "' id=" + controller.EntityId + " grid=" + controller.GridId +
                                                  " armed=" + controller.Settings.Armed +
                                                  " mode=" + controller.Settings.MobilityMode +
                                                  " destination=" +
                                                  (string.IsNullOrWhiteSpace(controller.Settings.DestinationGps) ? "unset" : "set") +
                                                  " vanillaAi=" + controller.Settings.VanillaAi +
                                                  " providers=" + controller.IntentProviders +
                                                  " winner=" + controller.WinningIntent +
                                                  " agent=" + controller.Agent.AgentId +
                                                  " schema=" + controller.Agent.SchemaVersion +
                                                  " capturedTick=" + controller.Agent.CapturedSimulationTick +
                                                  " corePosition=" + FormatVector(controller.Agent.CorePosition) +
                                                  " coreRight=" + FormatVector(controller.Agent.CoreRight) +
                                                  " coreUp=" + FormatVector(controller.Agent.CoreUp) +
                                                  " coreForward=" + FormatVector(controller.Agent.CoreForward) +
                                                  " physicalGrids=" + controller.Agent.PhysicalGridCount +
                                                  " blockEntries=" + controller.Agent.BlocksObserved +
                                                  " coverage=" + (controller.Agent.IsComplete ? "complete" : "truncated") +
                                                  " authority=none");
                if (MyAPIGateway.Utilities != null && !MyAPIGateway.Utilities.IsDedicated)
                    MyAPIGateway.Utilities.ShowNotification(
                        "Voidwright Agent Core: " + controller.Settings.MobilityMode +
                        (controller.Settings.Armed ? " armed (no authority yet)" : " disarmed"),
                        4000, "White");
            }
            var removed = new List<long>();
            foreach (var id in controllerSignatures.Keys)
                if (!active.Contains(id)) removed.Add(id);
            foreach (var id in removed) controllerSignatures.Remove(id);
        }

        private static string FormatVector(VRageMath.Vector3D value)
            => value.X.ToString("0.###", CultureInfo.InvariantCulture) + "," +
               value.Y.ToString("0.###", CultureInfo.InvariantCulture) + "," +
               value.Z.ToString("0.###", CultureInfo.InvariantCulture);

        private void PublishSessionLiveness(long tick)
        {
            if (MyAPIGateway.Utilities == null || string.IsNullOrEmpty(sessionLivenessSessionId)) return;
            var state = labPreflight == null ? "inactive" : labPreflight.LivenessState;
            var reason = labPreflight == null ? "session preflight adapter unavailable" : labPreflight.LivenessReason;
            if (reason.Length > 512) reason = reason.Substring(0, 512);
            var stateChanged = state != lastSessionLivenessState || reason != lastSessionLivenessReason;
            if (!stateChanged && tick - lastSessionLivenessPublishTick < 10) return;
            var liveness = new VoidwrightSessionLiveness
            {
                SessionId = sessionLivenessSessionId,
                SimulationTick = tick,
                PublishedUtc = DateTime.UtcNow,
                PreflightState = state,
                TerminalReason = reason
            };
            try
            {
                using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(
                           VoidwrightSessionLiveness.FileName, typeof(MySessionComponentBase)))
                    writer.Write(liveness.Format());
                lastSessionLivenessPublishTick = tick;
                lastSessionLivenessState = state;
                lastSessionLivenessReason = reason;
                lastSessionLivenessWriteError = null;
            }
            catch (Exception error)
            {
                var failure = error.GetType().Name;
                if (failure != lastSessionLivenessWriteError)
                    MyLog.Default.WriteLineAndConsole("Voidwright session liveness publish failed: " + failure);
                lastSessionLivenessWriteError = failure;
            }
        }

        protected override void UnloadData()
        {
            UnregisterTelemetryConsentUi();
            if (labPreflight != null) labPreflight.Stop();
            labPreflight = null;
            observedSignatures.Clear();
            controllerSignatures.Clear();
            if (previews != null) previews.ClearAll();
            previews = null;
            if (navigationControllers != null) navigationControllers.Clear();
            navigationControllers = null;
            snapshotExporter = null;
            blockLifecycleDiagnostic = null;
            if (cameraMarkers != null) cameraMarkers.Clear();
            cameraMarkers = null;
            probe = null;
            MyLog.Default.WriteLineAndConsole("Voidwright: session unloaded.");
        }

        public override void SaveData()
        {
            if (preSaveLifecycleCaptureAttempted) return;
            preSaveLifecycleCaptureAttempted = true;
            if (blockLifecycleDiagnostic != null)
                blockLifecycleDiagnostic.Capture("before-first-world-save", simulationTick);
        }

        private static BlockLifecycleDiagnosticTarget ReadBlockLifecycleTarget(string fixtureId)
        {
            const string fileName = BlockLifecycleDiagnosticExporter.TargetFileName;
            if (MyAPIGateway.Utilities == null ||
                !MyAPIGateway.Utilities.FileExistsInWorldStorage(fileName, typeof(VoidwrightSession))) return null;
            try
            {
                string json;
                using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(fileName, typeof(VoidwrightSession)))
                {
                    var buffer = new char[4097];
                    var count = reader.ReadBlock(buffer, 0, buffer.Length);
                    if (count <= 0 || count > 4096) return null;
                    json = new string(buffer, 0, count);
                }
                var schema = DiagnosticString(json, "schema");
                var target = new BlockLifecycleDiagnosticTarget
                {
                    GridEntityId = DiagnosticLong(json, "targetGridEntityId"),
                    GridName = DiagnosticString(json, "targetGridName"),
                    ControllerEntityId = DiagnosticLong(json, "controllerEntityId"),
                    ControllerType = DiagnosticString(json, "controllerType"),
                    ControllerSubtype = DiagnosticString(json, "controllerSubtype"),
                    CoreEntityId = DiagnosticLong(json, "coreEntityId"),
                    CoreSubtype = DiagnosticString(json, "coreSubtype"),
                    CameraEntityId = DiagnosticLong(json, "cameraEntityId"),
                    CameraSubtype = DiagnosticString(json, "cameraSubtype"),
                    SourceTreeSha256 = DiagnosticString(json, "sourceTreeSha256"),
                    InstrumentationLedgerSha256 = DiagnosticString(json, "instrumentationLedgerSha256")
                };
                if (schema != "vragecage.block-lifecycle-target.v1" || target.GridEntityId != 105034224363529183L ||
                    target.GridName != "Blue Truck" || target.ControllerEntityId != 83468039688685570L ||
                    target.ControllerType != "MyObjectBuilder_Cockpit" || string.IsNullOrWhiteSpace(target.ControllerSubtype) ||
                    target.CoreEntityId <= 0 || target.CameraEntityId <= 0 || target.CoreEntityId == target.CameraEntityId ||
                    target.CoreSubtype != "VoidwrightNavigationControllerSmall" ||
                    target.CameraSubtype != BlockLifecycleDiagnosticTarget.ExpectedFixtureCameraSubtype ||
                    !HexIdentity(fixtureId, 32) || !HexIdentity(target.SourceTreeSha256, 64) ||
                    !HexIdentity(target.InstrumentationLedgerSha256, 64))
                    throw new InvalidOperationException("target manifest failed pinned target validation");
                return target;
            }
            catch (Exception error)
            {
                MyLog.Default.WriteLineAndConsole("Voidwright block lifecycle target unavailable: " + error.GetType().Name);
                return null;
            }
        }

        private static string DiagnosticString(string json, string key)
        {
            var match = Regex.Match(json ?? string.Empty, "\\\"" + Regex.Escape(key) + "\\\"\\s*:\\s*\\\"([^\\\"]{1,256})\\\"");
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        private static long DiagnosticLong(string json, string key)
        {
            var match = Regex.Match(json ?? string.Empty, "\\\"" + Regex.Escape(key) + "\\\"\\s*:\\s*\\\"?([0-9]{1,20})\\\"?");
            long value;
            return match.Success && long.TryParse(match.Groups[1].Value, NumberStyles.None,
                CultureInfo.InvariantCulture, out value) ? value : 0;
        }

        private static bool HexIdentity(string value, int length)
        {
            if (string.IsNullOrEmpty(value) || value.Length != length) return false;
            foreach (var character in value)
                if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f') ||
                      (character >= 'A' && character <= 'F'))) return false;
            return true;
        }

        private void TryRegisterTelemetryConsentUi()
        {
            if (telemetryConsentUiRegistered || MyAPIGateway.Utilities == null ||
                MyAPIGateway.Utilities.IsDedicated || MyAPIGateway.Multiplayer == null ||
                MyAPIGateway.Multiplayer.IsServer) return;
            MyAPIGateway.Utilities.MessageEnteredSender += OnTelemetryChatCommand;
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(
                DrivingLabConsentWire.MessageId, OnTelemetryServerResult);
            telemetryConsentUiRegistered = true;
        }

        private void UnregisterTelemetryConsentUi()
        {
            if (!telemetryConsentUiRegistered) return;
            if (MyAPIGateway.Utilities != null)
                MyAPIGateway.Utilities.MessageEnteredSender -= OnTelemetryChatCommand;
            if (MyAPIGateway.Multiplayer != null)
                MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(
                    DrivingLabConsentWire.MessageId, OnTelemetryServerResult);
            telemetryConsentUiRegistered = false;
        }

        private void OnTelemetryChatCommand(ulong sender, string messageText, ref bool sendToOthers)
        {
            if (string.IsNullOrWhiteSpace(messageText)) return;
            var parts = messageText.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !string.Equals(parts[0], "/vw", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(parts[1], "telemetry", StringComparison.OrdinalIgnoreCase)) return;

            sendToOthers = false;
            if (parts.Length == 3 && string.Equals(parts[2], "help", StringComparison.OrdinalIgnoreCase))
            {
                MyAPIGateway.Utilities.ShowMessage("Driving Lab telemetry",
                    "Read-only capture binds your authenticated player to your current cockpit grid. It records grid pose, speed, simulation time, and wheel actuator/brake state at up to 10 Hz for no more than 15 minutes or 64 MiB. Keyboard, gamepad, and human intent are not collected. Type /vw telemetry consent to opt in or /vw telemetry stop to revoke.");
                return;
            }

            byte[] request;
            if (parts.Length == 3 && string.Equals(parts[2], "consent", StringComparison.OrdinalIgnoreCase))
                request = DrivingLabConsentWire.CreateStartRequest();
            else if (parts.Length == 3 && string.Equals(parts[2], "stop", StringComparison.OrdinalIgnoreCase))
                request = DrivingLabConsentWire.CreateStopRequest();
            else
            {
                MyAPIGateway.Utilities.ShowMessage("Driving Lab telemetry",
                    "Use /vw telemetry help, /vw telemetry consent, or /vw telemetry stop.");
                return;
            }

            if (MyAPIGateway.Multiplayer == null ||
                !MyAPIGateway.Multiplayer.SendMessageToServer(DrivingLabConsentWire.MessageId, request, true))
                MyAPIGateway.Utilities.ShowMessage("Driving Lab telemetry", "The private opt-in request could not be sent.");
            else
                MyAPIGateway.Utilities.ShowMessage("Driving Lab telemetry", "Private request sent to the server.");
        }

        private void OnTelemetryServerResult(ushort messageId, byte[] message, ulong senderSteamId, bool fromServer)
        {
            if (messageId != DrivingLabConsentWire.MessageId || !fromServer) return;
            bool accepted;
            string text;
            if (!DrivingLabConsentWire.TryReadResult(message, out accepted, out text)) return;
            MyAPIGateway.Utilities.ShowMessage(accepted ? "Driving Lab telemetry" : "Driving Lab telemetry",
                (accepted ? "Accepted: " : "Not started: ") + text);
        }
    }
}
