using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using Voidwright.Core;

namespace Voidwright.Game
{
    /// <summary>
    /// Run-bound ordinary-mod path for the disposable VRageCage depth-navigation command.
    /// It observes and scans before acting, gates every actuator write, and never uses GridWheels.
    /// </summary>
    public sealed class VoidwrightLabPreflight
    {
        private const string Action = "drive-to-coordinate-depth";
        private const double ScanRangeMeters = 8.0;
        private const double RequiredClearanceMeters = 3.4;
        private const double MaximumSpeedMps = 0.8;
        private const int ScanIntervalTicks = 10;
        private const int HeartbeatPublishIntervalTicks = ScanIntervalTicks;
        private const int ContextValidationIntervalTicks = 60;
        // Match the host's bounded handshake allowance. Sensor scan freshness
        // remains independently limited to one second and 20 simulation ticks.
        private static readonly TimeSpan HandshakeMaximumAge = TimeSpan.FromSeconds(1.25);
        private static readonly double[] BeamAngles = CreateBeamAngles();

        private string runId;
        private string fixtureId;
        private string operationId;
        private string contextSha256;
        private string challenge;
        private readonly string sessionId;
        private readonly string discoveryExporterRunId;
        private readonly string bootstrapNonce;
        private readonly RouteFollower follower = new RouteFollower();
        private readonly Dictionary<long, SavedWheelState> savedWheels = new Dictionary<long, SavedWheelState>();
        private VoidwrightModActivation context;
        private string activationText;
        private string heartbeatText;
        private bool activationLoaded;
        private bool stageTwoAcknowledged;
        private int activationWaitTicks;
        private long approvalWaitSinceTick = -1;
        private IMyShipController controller;
        private IMyCameraBlock camera;
        private List<IMyMotorSuspension> wheels = new List<IMyMotorSuspension>();
        private readonly double[] clearances = new double[VoidwrightPreflightHeartbeat.RayCount];
        private long scanTick = -1;
        private DateTime scanUtc = DateTime.MinValue;
        private int validRayCount;
        private bool handshakeValid;
        private string handshakeReason = "no live preflight heartbeat";
        private long scanGridId;
        private long scanCameraId;
        private double previousDetourHeading;
        private bool adapterOwnsActuators;
        private bool terminal;
        private long firstTick = -1;
        private long lastEvidenceTick = long.MinValue;
        private string lastReason;
        private string forcedTerminalStatus;

        private sealed class SavedWheelState
        {
            public IMyMotorSuspension Block;
            public float SteeringOverride;
            public float PropulsionOverride;
            public float ExpectedSteering;
            public float ExpectedPropulsion;
            public bool OwnsSteering;
            public bool OwnsPropulsion;
        }

        public VoidwrightLabPreflight(string discoveryExporterRunId) : this(discoveryExporterRunId, Guid.NewGuid().ToString("N"), string.Empty) { }

        public VoidwrightLabPreflight(string discoveryExporterRunId, string sessionId, string bootstrapNonce)
        {
            this.discoveryExporterRunId = discoveryExporterRunId ?? string.Empty;
            this.sessionId = sessionId ?? string.Empty;
            this.bootstrapNonce = bootstrapNonce ?? string.Empty;
            lastReason = "waiting for host-verified run activation in world storage";
        }

        public bool IsConfigured { get { return activationLoaded && context != null; } }
        public string SessionId { get { return sessionId; } }
        public string LivenessState
        {
            get
            {
                if (!terminal) return "running";
                if ((lastReason ?? string.Empty).StartsWith("ordinary adapter update failed closed", StringComparison.Ordinal) ||
                    (lastReason ?? string.Empty).StartsWith("preflight evidence write failed", StringComparison.Ordinal))
                    return "error";
                var status = MovementPreflight.ResolveTerminalStatus(lastReason, forcedTerminalStatus);
                if (status == "complete") return "complete";
                if (status == "timeout") return "timeout";
                if (status == "preflight-blocked" || status == "actuator-conflict" ||
                    status == "depth-no-clear-corridor" || status == "human-connected" ||
                    status == "invalid-guidance") return "preflight-blocked";
                return "error";
            }
        }
        public string LivenessReason { get { return terminal ? (lastReason ?? string.Empty) : string.Empty; } }

        public void Update(long simulationTick)
        {
            if (terminal) return;
            try { UpdateCore(simulationTick); }
            catch (Exception error)
            {
                lastReason = "ordinary adapter update failed closed: " + error.GetType().Name;
                try { StopActuators(); } catch { }
                terminal = true;
                try
                {
                    if (context != null)
                        WriteEvidence(simulationTick, BuildPreflight(DateTime.UtcNow, simulationTick)
                            .Invalidate(DateTime.UtcNow, lastReason));
                }
                catch { }
            }
        }

        private void UpdateCore(long simulationTick)
        {
            if (terminal) return;
            if (context != null && firstTick >= 0 && MovementPreflight.HasOperationTimedOut(
                    simulationTick, firstTick, context.TimeoutTicks))
            {
                FinishOperationTimeout(simulationTick);
                return;
            }
            if (MyAPIGateway.Utilities == null)
            {
                return;
            }
            if (!IsConfigured)
            {
                activationWaitTicks++;
                if (!TryLoadActivation(out lastReason))
                {
                    var activationWaitLimit = string.IsNullOrEmpty(bootstrapNonce) ? 300 : 3600;
                    if (activationWaitTicks >= activationWaitLimit)
                    {
                        terminal = true;
                        MyLog.Default.WriteLineAndConsole("Voidwright VRageCage preflight unavailable: " + lastReason);
                    }
                    return;
                }
                firstTick = simulationTick;
                MyLog.Default.WriteLineAndConsole("Voidwright VRageCage host activation accepted; waiting for matching live preflight approval.");
            }
            if (firstTick < 0) firstTick = simulationTick;
            if (context != null && MovementPreflight.HasOperationTimedOut(
                    simulationTick, firstTick, context.TimeoutTicks))
            {
                // The deadline covers preflight/approval waiting as well as
                // movement. A blocked command must not remain "progress"
                // forever just because the dispatch gate never opened.
                FinishOperationTimeout(simulationTick);
                return;
            }
            if (simulationTick % ContextValidationIntervalTicks == 0)
            {
                if (!LoadContext(out lastReason))
                {
                    WriteEvidence(simulationTick, context == null ? null : BuildPreflight(DateTime.UtcNow, simulationTick));
                    StopActuators();
                    terminal = true;
                    return;
                }
                if (!stageTwoAcknowledged && !string.IsNullOrEmpty(bootstrapNonce))
                {
                    VoidwrightModActivation accepted;
                    string observed;
                    if (!TryReadActivation(out observed, out accepted, out lastReason) ||
                        !WriteStageTwoModAcknowledgement(accepted, out lastReason))
                    {
                        StopActuators();
                        terminal = true;
                        return;
                    }
                    stageTwoAcknowledged = true;
                }
            }
            if (SimulationEvidenceCadence.IsPeriodicTick(simulationTick, ScanIntervalTicks))
            {
                ResolveEntities();
                ScanReadOnly(simulationTick);
            }
            // Keep the exact serialized heartbeat stable for one scan
            // interval so the host can acknowledge that particular tick. The
            // host's 1.25-second heartbeat window handles update skew, while
            // sensor freshness remains capped at one second / 20 ticks.
            if (SimulationEvidenceCadence.IsPeriodicTick(simulationTick, HeartbeatPublishIntervalTicks))
                PublishHeartbeat(simulationTick);
            ReadHostApproval();

            var now = DateTime.UtcNow;
            var preflight = BuildPreflight(now, simulationTick);
            var state = StateFingerprint();
            if (adapterOwnsActuators && preflight.PassingCount != preflight.TotalCount &&
                !IsOnlyAwaitingHostApproval(preflight))
            {
                lastReason = "a required preflight check became unknown or failed while the adapter owned the wheels";
                StopActuators();
                terminal = true;
                WriteEvidence(simulationTick, preflight.Invalidate(now, lastReason));
                return;
            }
            if (IsOnlyAwaitingHostApproval(preflight))
            {
                if (approvalWaitSinceTick < 0) approvalWaitSinceTick = simulationTick;
                if (simulationTick - approvalWaitSinceTick > 120)
                {
                    lastReason = "host preflight approval did not refresh within the bounded handshake window: " + handshakeReason;
                    if (adapterOwnsActuators) StopActuators();
                    terminal = true;
                    WriteEvidence(simulationTick, preflight.Invalidate(now, lastReason));
                }
                return;
            }
            if (MovementPreflight.MustStopAfterHostApproval(preflight, handshakeValid))
            {
                lastReason = "mod-side movement preflight denied dispatch after host approval";
                StopActuators();
                terminal = true;
                WriteEvidence(simulationTick, preflight.Invalidate(now, lastReason));
                return;
            }
            if (context.Action == "preflight-only")
            {
                var decision = PreflightOnlyPolicy.Decide(preflight, handshakeValid);
                if (decision == PreflightOnlyDecision.Failed)
                {
                    lastReason = "preflight-only found a failed or incomplete safety check; no actuator dispatch was attempted";
                    terminal = true;
                    WriteEvidence(simulationTick, preflight);
                }
                else if (decision == PreflightOnlyDecision.Complete)
                {
                    lastReason = "preflight-only complete; no actuator dispatch was attempted";
                    terminal = true;
                    WriteEvidence(simulationTick, preflight);
                }
                else if (SimulationEvidenceCadence.IsDue(simulationTick, lastEvidenceTick, 60))
                {
                    WriteEvidence(simulationTick, preflight);
                }
                return;
            }
            if (context.Action == "direct-maneuver")
            {
                approvalWaitSinceTick = -1;
                if (SimulationEvidenceCadence.IsDue(simulationTick, lastEvidenceTick, 10))
                    WriteEvidence(simulationTick, preflight);
                return;
            }
            approvalWaitSinceTick = -1;
            var stepAccepted = false;
            var dispatched = MovementCommandGate.TryDispatchSafely(preflight, now, runId, operationId,
                FixtureFingerprint(), state, TimeSpan.FromSeconds(1), delegate
                {
                    stepAccepted = simulationTick - firstTick < context.StartDelayTicks || ApplyOneBoundedStep(simulationTick);
                }, delegate
                {
                    StopActuators();
                    terminal = true;
                }, delegate(Exception error)
                {
                    lastReason = "ordinary adapter update threw; owned wheels were stopped: " + error.GetType().Name;
                });
            if (!dispatched || !stepAccepted)
            {
                if (terminal)
                {
                    WriteEvidence(simulationTick, preflight.Invalidate(now, lastReason ?? "adapter reached a terminal safety state"));
                    return;
                }
                if (preflight.PassingCount == preflight.TotalCount &&
                    !preflight.IsAuthorizing(now, runId, operationId, FixtureFingerprint(), state, TimeSpan.FromSeconds(1)))
                {
                    StopActuators();
                    terminal = true;
                    lastReason = "preflight identity, age, or state changed before dispatch";
                }
                if (SimulationEvidenceCadence.IsDue(simulationTick, lastEvidenceTick, 60))
                    WriteEvidence(simulationTick, preflight);
            }
            else
            {
                if (!terminal) lastReason = null;
                if (terminal || SimulationEvidenceCadence.IsDue(simulationTick, lastEvidenceTick, 60))
                    WriteEvidence(simulationTick, preflight);
            }
        }

        public void Stop()
        {
            StopActuators();
        }

        private bool LoadContext(out string reason)
        {
            VoidwrightModActivation loaded;
            DirectManeuverPlan ignoredPlan;
            string observed;
            if (!TryReadActivation(out observed, out loaded, out reason) ||
                !string.Equals(observed, activationText, StringComparison.Ordinal))
            { context = null; reason = "host activation disappeared or changed during the run"; return false; }
            context = loaded;
            if (context.ControllerId <= 0 || context.CameraId <= 0 ||
                context.MaximumSpeed <= 0 || (context.Action == "direct-maneuver"
                    ? context.MaximumSpeed > 2.0 : context.MaximumSpeed > MaximumSpeedMps) ||
                context.TimeoutTicks < 60 || context.TimeoutTicks > (context.Action == "direct-maneuver" ? 2700 : 7200) ||
                (context.Action == "direct-maneuver"
                    ? context.GridId <= 0 || !DirectManeuverPlan.TryParseCompactSegments(
                        context.DirectManeuverSegments, context.RunId, context.FixtureId,
                        context.OperationId, context.GridId, context.ControllerId,
                        context.AgentCoreId, out ignoredPlan)
                    : context.Action == "preflight-only"
                        ? context.GridId <= 0 || !string.IsNullOrEmpty(context.DirectManeuverSegments)
                    : context.ArrivalRadius <= 0 || context.ArrivalRadius > 5 || context.StartDelayTicks > 3600 || context.Reverse))
            {
                reason = "run-bound navigation command exceeds the Voidwright adapter limits";
                context = null;
                return false;
            }
            reason = string.Empty;
            return true;
        }

        private bool IsOnlyAwaitingHostApproval(MovementPreflight preflight)
        {
            return !handshakeValid && handshakeReason == "waiting for a fresh host approval matching this run heartbeat" && preflight != null &&
                preflight.HasOnlyUnknownChecks("voidwright_module_and_session_marker",
                    "controller_readiness", "movement_interface_readiness");
        }

        private bool TryLoadActivation(out string reason)
        {
            VoidwrightModActivation loaded;
            string observed;
            if (!TryReadActivation(out observed, out loaded, out reason)) return false;
            activationText = observed;
            context = loaded;
            runId = loaded.RunId;
            fixtureId = loaded.FixtureId;
            operationId = loaded.OperationId;
            contextSha256 = loaded.ContextSha256;
            challenge = loaded.Challenge;
            activationLoaded = true;
            reason = string.Empty;
            return true;
        }

        private bool WriteStageTwoModAcknowledgement(VoidwrightModActivation loaded, out string reason)
        {
            reason = "stage-two activation is not bound to this mod session";
            try
            {
                string text;
                using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(
                           "voidwright-activation-" + loaded.RunId + ".txt", typeof(MySessionComponentBase))) text = reader.ReadToEnd();
                VoidwrightStageTwoActivation activation;
                if (!VoidwrightStageTwoActivation.TryParse(text, DateTime.UtcNow, out activation) ||
                    activation.RunId != loaded.RunId || activation.FixtureId != loaded.FixtureId ||
                    activation.OperationId != loaded.OperationId || activation.BootstrapNonce != bootstrapNonce ||
                    activation.SessionId != sessionId || activation.ExporterRunId != discoveryExporterRunId ||
                    activation.ContextSha256 != loaded.ContextSha256 || activation.ControllerId != loaded.ControllerId ||
                    activation.CameraId != loaded.CameraId || activation.GridId != loaded.GridId ||
                    activation.CoreId != loaded.AgentCoreId || activation.Tick != loaded.DiscoveryTick ||
                    activation.Fingerprint != loaded.DiscoveryFingerprint || activation.Action != loaded.Action) return false;
                if (loaded.Action == "direct-maneuver")
                {
                    DirectManeuverPlan plan;
                    if (!DirectManeuverPlan.TryParseCompactSegments(loaded.DirectManeuverSegments,
                            loaded.RunId, loaded.FixtureId, loaded.OperationId, loaded.GridId,
                            loaded.ControllerId, loaded.AgentCoreId, out plan) ||
                        !DirectManeuverCommandAcceptancePolicy.IsZeroOnlyStationaryHold(plan,
                            loaded.RunId, loaded.FixtureId, loaded.OperationId, loaded.GridId,
                            loaded.ControllerId, loaded.AgentCoreId, loaded.DirectManeuverSegments,
                            loaded.TimeoutTicks)) return false;
                }
                else if (loaded.Action != "preflight-only" || !string.IsNullOrEmpty(loaded.DirectManeuverSegments))
                    return false;
                string discovery;
                using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(
                           AgentCoreSnapshotFileExporter.DiscoveryFileName, typeof(MySessionComponentBase))) discovery = reader.ReadToEnd();
                var liveFingerprint = Sha256Digest.ComputeHex(Encoding.UTF8.GetBytes(discovery));
                var runMatch = Regex.Match(discovery, "\\\"runId\\\":\\\"([0-9a-f]{32})\\\"");
                var tickMatch = Regex.Match(discovery, "\\\"capturedSimulationTick\\\":([0-9]+)");
                var capturedMatch = Regex.Match(discovery, "\\\"capturedAtUtc\\\":\\\"([^\\\"]+)\\\"");
                long liveTick;
                DateTime capturedUtc;
                if (!runMatch.Success || !tickMatch.Success || !long.TryParse(tickMatch.Groups[1].Value,
                        NumberStyles.None, CultureInfo.InvariantCulture, out liveTick) ||
                    !capturedMatch.Success || !DateTime.TryParse(capturedMatch.Groups[1].Value,
                        CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out capturedUtc) || !DirectManeuverCommandAcceptancePolicy.IsDiscoveryFresh(
                        capturedUtc, DateTime.UtcNow, TimeSpan.FromSeconds(15)) ||
                    !DirectManeuverCommandAcceptancePolicy.MatchesFreshDiscoveryAdvance(
                        runMatch.Groups[1].Value, liveTick, liveFingerprint, loaded.GridId,
                        loaded.ControllerId, loaded.AgentCoreId, activation.ExporterRunId,
                        activation.Tick, activation.Fingerprint, activation.GridId,
                        activation.ControllerId, activation.CoreId) ||
                    discovery.IndexOf("\\\"sessionId\\\":\\\"" + activation.SessionId + "\\\"", StringComparison.Ordinal) < 0 ||
                    discovery.IndexOf("\\\"labRunId\\\":\\\"" + activation.RunId + "\\\"", StringComparison.Ordinal) < 0 ||
                    discovery.IndexOf("\\\"fixtureId\\\":\\\"" + activation.FixtureId + "\\\"", StringComparison.Ordinal) < 0 ||
                    discovery.IndexOf("\\\"bootstrapNonce\\\":\\\"" + activation.BootstrapNonce + "\\\"", StringComparison.Ordinal) < 0 ||
                    discovery.IndexOf("\\\"coreEntityId\\\":" + activation.CoreId.ToString(CultureInfo.InvariantCulture) + ",", StringComparison.Ordinal) < 0 ||
                    discovery.IndexOf("\\\"anchorGridEntityId\\\":" + activation.GridId.ToString(CultureInfo.InvariantCulture) + ",", StringComparison.Ordinal) < 0)
                    return false;
                foreach (var idText in activation.SuspensionIds.Split(','))
                {
                    long wheelId;
                    if (!long.TryParse(idText, NumberStyles.None, CultureInfo.InvariantCulture, out wheelId) || wheelId <= 0 ||
                        Regex.Matches(discovery, "\\\"gridEntityId\\\":" + activation.GridId.ToString(CultureInfo.InvariantCulture) +
                            ",\\\"entityId\\\":" + wheelId.ToString(CultureInfo.InvariantCulture) +
                            ",\\\"definitionType\\\":\\\"[^\\\"]*\\\",\\\"subtypeId\\\":\\\"[^\\\"]*\\\",\\\"typedKind\\\":\\\"suspension\\\"").Count != 1)
                        return false;
                }
                var acknowledgement = new VoidwrightStageTwoAcknowledgement { Status = "mod-accepted",
                    RunId = activation.RunId, FixtureId = activation.FixtureId, OperationId = activation.OperationId,
                    BootstrapNonce = activation.BootstrapNonce, SessionId = activation.SessionId,
                    ExporterRunId = activation.ExporterRunId, Tick = activation.Tick,
                    Fingerprint = activation.Fingerprint, ContextSha256 = activation.ContextSha256,
                    UpdatedUtc = DateTime.UtcNow };
                var expected = acknowledgement.Format();
                var name = "voidwright-ack-" + activation.RunId + "-mod-accepted.txt";
                using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(name, typeof(MySessionComponentBase))) writer.Write(expected);
                using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(name, typeof(MySessionComponentBase)))
                    if (!string.Equals(expected, reader.ReadToEnd(), StringComparison.Ordinal))
                    { reason = "mod acceptance acknowledgment readback mismatch"; return false; }
                reason = string.Empty;
                return true;
            }
            catch (Exception error)
            {
                reason = "mod stage-two acknowledgment failed closed: " + error.GetType().Name;
                return false;
            }
        }

        private bool TryReadActivation(out string serialized, out VoidwrightModActivation loaded, out string reason)
        {
            serialized = string.Empty;
            loaded = null;
            reason = "host activation is not available in world storage";
            try
            {
                using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(
                           VoidwrightModActivation.FileName, typeof(MySessionComponentBase)))
                    serialized = reader.ReadToEnd();
                if (!VoidwrightModActivation.TryParse(serialized, DateTime.UtcNow, out loaded))
                { reason = "host activation is malformed, expired, or outside its bounded command envelope"; return false; }
                if (loaded.OperationId == null || loaded.ControllerId <= 0 || loaded.CameraId <= 0)
                { reason = "host activation omitted pinned vehicle identities"; return false; }
                reason = string.Empty;
                return true;
            }
            catch (Exception error)
            {
                reason = "world-storage activation read failed: " + error.GetType().Name;
                return false;
            }
        }

        private void ResolveEntities()
        {
            controller = null;
            camera = null;
            wheels.Clear();
            if (MyAPIGateway.Entities == null || context == null) return;
            IMyEntity entity;
            if (MyAPIGateway.Entities.TryGetEntityById(context.ControllerId, out entity))
                controller = entity as IMyShipController;
            if (MyAPIGateway.Entities.TryGetEntityById(context.CameraId, out entity))
                camera = entity as IMyCameraBlock;
            if (controller != null && controller.CubeGrid != null)
                foreach (var wheel in controller.CubeGrid.GetFatBlocks<IMyMotorSuspension>())
                    if (wheel != null && !wheel.Closed) wheels.Add(wheel);
        }

        private void ScanReadOnly(long simulationTick)
        {
            validRayCount = 0;
            scanTick = -1;
            scanUtc = DateTime.MinValue;
            scanGridId = 0;
            scanCameraId = 0;
            if (controller == null || camera == null || controller.CubeGrid == null || camera.CubeGrid == null ||
                controller.CubeGrid.EntityId != camera.CubeGrid.EntityId || MyAPIGateway.Physics == null) return;
            try
            {
                var up = controller.WorldMatrix.Up;
                var forward = camera.WorldMatrix.Forward;
                forward -= up * Vector3D.Dot(forward, up);
                if (forward.LengthSquared() < 0.0001) return;
                forward.Normalize();
                var right = Vector3D.Cross(forward, up);
                if (right.LengthSquared() < 0.0001) return;
                right.Normalize();
                var origin = camera.GetPosition() + forward * 1.5;
                for (var index = 0; index < BeamAngles.Length; index++)
                {
                    var direction = forward * Math.Cos(BeamAngles[index]) + right * Math.Sin(BeamAngles[index]);
                    var rayStart = origin;
                    var travelled = 0.0;
                    var clearance = ScanRangeMeters;
                    var resolved = false;
                    for (var attempt = 0; attempt < 4; attempt++)
                    {
                        var remaining = ScanRangeMeters - travelled;
                        if (remaining <= 0.05) { resolved = true; break; }
                        IHitInfo hit;
                        if (!MyAPIGateway.Physics.CastRay(rayStart, rayStart + direction * remaining, out hit) || hit == null)
                        {
                            clearance = ScanRangeMeters;
                            resolved = true;
                            break;
                        }
                        var distance = Vector3D.Distance(rayStart, hit.Position);
                        if (!Finite(distance) || distance < 0 || distance > remaining + 0.1) return;
                        if (hit.HitEntity != null && hit.HitEntity.EntityId == controller.CubeGrid.EntityId)
                        {
                            var advance = Math.Max(0.05, distance) + 0.05;
                            travelled += advance;
                            rayStart = hit.Position + direction * 0.05;
                            continue;
                        }
                        clearance = Math.Min(ScanRangeMeters, travelled + distance);
                        resolved = true;
                        break;
                    }
                    if (!resolved) clearance = Math.Min(ScanRangeMeters, travelled);
                    if (!Finite(clearance) || clearance < 0 || clearance > ScanRangeMeters) return;
                    clearances[index] = clearance;
                    validRayCount++;
                }
                scanTick = simulationTick;
                scanUtc = DateTime.UtcNow;
                scanGridId = controller.CubeGrid.EntityId;
                scanCameraId = camera.EntityId;
            }
            catch
            {
                validRayCount = 0;
                scanTick = -1;
            }
        }

        private void PublishHeartbeat(long simulationTick)
        {
            if (controller == null || camera == null || controller.CubeGrid == null) return;
            var heartbeat = new VoidwrightPreflightHeartbeat
            {
                RunId = runId, FixtureId = fixtureId, OperationId = operationId,
                ContextSha256 = contextSha256, Challenge = challenge, SessionId = sessionId,
                ControllerId = context.ControllerId, GridId = controller.CubeGrid.EntityId,
                AgentCoreId = (context.Action == "direct-maneuver" || context.Action == "preflight-only") &&
                    IsExpectedAgentCoreOnAnchorGrid(simulationTick)
                    ? context.AgentCoreId : 0,
                DiscoveryRunId = context.Action == "direct-maneuver" || context.Action == "preflight-only"
                    ? context.DiscoveryRunId : string.Empty,
                DiscoveryTick = context.Action == "direct-maneuver" || context.Action == "preflight-only"
                    ? context.DiscoveryTick : 0,
                DiscoveryFingerprint = context.Action == "direct-maneuver" || context.Action == "preflight-only"
                    ? context.DiscoveryFingerprint : string.Empty,
                CameraId = context.CameraId, SimulationTick = simulationTick,
                OperationCapabilityToken = context.Action == "direct-maneuver"
                    ? ReadOperationCapabilityToken() : string.Empty,
                PublishedUtc = DateTime.UtcNow, SensorValid = validRayCount == VoidwrightPreflightHeartbeat.RayCount,
                SensorScanTick = scanTick < 0 ? simulationTick : scanTick,
                SensorScanUtc = scanTick < 0 ? DateTime.UtcNow : scanUtc,
                SensorGridId = scanTick < 0 ? controller.CubeGrid.EntityId : scanGridId,
                SensorCameraId = scanTick < 0 ? context.CameraId : scanCameraId,
                ValidRayCount = validRayCount,
                WheelCount = wheels.Count,
                SteeringWheelCount = CountWheelChannel(true),
                PropulsionWheelCount = CountWheelChannel(false),
                ActuatorReady = wheels.Count > 0 && CountWheelChannel(true) > 0 && CountWheelChannel(false) > 0,
                Clearances = (double[])clearances.Clone()
            };
            try
            {
                var expectedReadback = heartbeat.Format();
                heartbeatText = expectedReadback;
                handshakeValid = false;
                handshakeReason = "waiting for host preflight approval";
                using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(
                           VoidwrightPreflightHeartbeat.FileName, typeof(MySessionComponentBase)))
                    writer.Write(expectedReadback);
                using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(
                           VoidwrightPreflightHeartbeat.FileName, typeof(MySessionComponentBase)))
                {
                    var serialized = reader.ReadToEnd();
                    VoidwrightPreflightHeartbeat observed;
                    handshakeValid = VoidwrightPreflightHandshake.TryAccept(expectedReadback, serialized,
                        DateTime.UtcNow, HandshakeMaximumAge, runId, fixtureId, operationId,
                        contextSha256, challenge, sessionId, context.ControllerId,
                        controller.CubeGrid.EntityId, context.CameraId, out observed);
                    handshakeReason = handshakeValid ? "fresh mod heartbeat read back; awaiting host validation" :
                        "world-storage heartbeat did not match the current run, challenge, session, grid, camera, or freshness window";
                }
            }
            catch (Exception error)
            {
                handshakeValid = false;
                handshakeReason = "heartbeat publish/readback failed: " + error.GetType().Name;
                lastReason = "run-bound heartbeat write failed: " + error.GetType().Name;
            }
        }

        private string ReadOperationCapabilityToken()
        {
            if (context == null || context.Action != "direct-maneuver" ||
                MyAPIGateway.Utilities == null || controller == null || controller.CubeGrid == null)
                return string.Empty;
            try
            {
                string serialized;
                using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(
                           VoidwrightOperationCapability.FileName, typeof(MySessionComponentBase)))
                    serialized = reader.ReadToEnd();
                VoidwrightOperationCapability capability;
                if (!VoidwrightOperationCapability.TryParse(serialized, DateTime.UtcNow, out capability) ||
                    !capability.Matches(DateTime.UtcNow, context.RunId, context.FixtureId,
                        context.OperationId, context.ContextSha256, context.Challenge, sessionId,
                        controller.CubeGrid.EntityId, context.ControllerId, context.AgentCoreId,
                        context.CameraId, context.DiscoveryRunId, context.DiscoveryTick,
                        context.DiscoveryFingerprint, capability.Token))
                    return string.Empty;
                return capability.Token;
            }
            catch { return string.Empty; }
        }

        private bool IsExpectedAgentCoreOnAnchorGrid(long simulationTick)
        {
            if (context == null || context.AgentCoreId <= 0 || context.GridId <= 0 ||
                controller == null || controller.CubeGrid == null ||
                controller.CubeGrid.EntityId != context.GridId ||
                !IsCurrentDiscoveryBinding() || MyAPIGateway.Entities == null)
                return false;
            VRage.ModAPI.IMyEntity entity;
            if (!MyAPIGateway.Entities.TryGetEntityById(context.AgentCoreId, out entity)) return false;
            var core = entity as IMyUpgradeModule;
            if (!NavigationControllerIdentity.IsNavigationController(core) || core.EntityId != context.AgentCoreId ||
                core.CubeGrid == null || core.CubeGrid.EntityId != context.GridId)
                return false;
            return AgentCoreBindingPolicy.IsValid(context.AgentCoreId, core.EntityId,
                context.GridId, core.CubeGrid.EntityId, simulationTick, simulationTick, 0, true);
        }

        private bool IsCurrentDiscoveryBinding()
        {
            if (context == null || MyAPIGateway.Utilities == null || context.DiscoveryRunId == null ||
                context.DiscoveryRunId.Length != 32 || context.DiscoveryFingerprint == null ||
                context.DiscoveryFingerprint.Length != 64 || context.DiscoveryTick < 0) return false;
            try
            {
                string json;
                using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(
                           AgentCoreSnapshotFileExporter.DiscoveryFileName, typeof(MySessionComponentBase)))
                    json = reader.ReadToEnd();
                if (string.IsNullOrEmpty(json) || Encoding.UTF8.GetByteCount(json) > AgentCoreSnapshotFileExporter.MaximumSnapshotBytes)
                    return false;
                var fingerprint = Sha256Digest.ComputeHex(Encoding.UTF8.GetBytes(json));
                var runMatch = Regex.Match(json, "\\\"runId\\\":\\\"([0-9a-f]{32})\\\"");
                if (!string.IsNullOrEmpty(bootstrapNonce) &&
                    (json.IndexOf("\"sessionId\":\"" + sessionId + "\"", StringComparison.Ordinal) < 0 ||
                     json.IndexOf("\"labRunId\":\"" + runId + "\"", StringComparison.Ordinal) < 0 ||
                     json.IndexOf("\"fixtureId\":\"" + fixtureId + "\"", StringComparison.Ordinal) < 0 ||
                     json.IndexOf("\"bootstrapNonce\":\"" + bootstrapNonce + "\"", StringComparison.Ordinal) < 0)) return false;
                var tickMatch = Regex.Match(json, "\\\"capturedSimulationTick\\\":([0-9]+)");
                var capturedMatch = Regex.Match(json, "\\\"capturedAtUtc\\\":\\\"([^\\\"]+)\\\"");
                long capturedTick;
                DateTime capturedUtc;
                if (!runMatch.Success || !tickMatch.Success || !capturedMatch.Success ||
                    runMatch.Groups[1].Value != context.DiscoveryRunId ||
                    !AgentCoreDiscoverySessionPolicy.IsCurrentSession(runMatch.Groups[1].Value, discoveryExporterRunId) ||
                    !long.TryParse(tickMatch.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out capturedTick) ||
                    capturedTick < context.DiscoveryTick ||
                    (capturedTick == context.DiscoveryTick && fingerprint != context.DiscoveryFingerprint) ||
                    !DateTime.TryParse(capturedMatch.Groups[1].Value, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out capturedUtc) ||
                    capturedUtc.Kind != DateTimeKind.Utc || DateTime.UtcNow < capturedUtc ||
                    DateTime.UtcNow - capturedUtc > TimeSpan.FromSeconds(15)) return false;
                return true;
            }
            catch { return false; }
        }

        private int CountWheelChannel(bool steering)
        {
            var count = 0;
            foreach (var wheel in wheels)
                if (wheel != null && !wheel.Closed && (steering ? wheel.Steering : wheel.Propulsion)) count++;
            return count;
        }

        private void ReadHostApproval()
        {
            if (string.IsNullOrEmpty(heartbeatText) || MyAPIGateway.Utilities == null) return;
            try
            {
                string heartbeatReadback;
                using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(
                           VoidwrightPreflightHeartbeat.FileName, typeof(MySessionComponentBase)))
                    heartbeatReadback = reader.ReadToEnd();
                if (!string.Equals(heartbeatText, heartbeatReadback, StringComparison.Ordinal))
                { handshakeValid = false; handshakeReason = "host observed a different mod heartbeat"; return; }
                VoidwrightPreflightHeartbeat heartbeat;
                if (!VoidwrightPreflightHeartbeat.TryParse(heartbeatReadback, out heartbeat) ||
                    !heartbeat.IsFreshAndBound(DateTime.UtcNow, HandshakeMaximumAge, runId, fixtureId,
                        operationId, contextSha256, challenge, sessionId, context.ControllerId,
                        controller == null || controller.CubeGrid == null ? 0 : controller.CubeGrid.EntityId,
                        context.CameraId) ||
                    DateTime.UtcNow - heartbeat.SensorScanUtc > TimeSpan.FromSeconds(1) ||
                    heartbeat.SimulationTick - heartbeat.SensorScanTick > ScanIntervalTicks * 2 ||
                    !heartbeat.ActuatorReady)
                { handshakeValid = false; handshakeReason = "mod heartbeat is stale or fails exact run, sensor, wheel, or entity binding"; return; }
                string approvalText;
                try
                {
                    using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(
                               VoidwrightPreflightApproval.FileName, typeof(MySessionComponentBase)))
                        approvalText = reader.ReadToEnd();
                }
                catch (Exception error)
                {
                    handshakeValid = false;
                    handshakeReason = "host approval file unavailable: " + error.GetType().Name;
                    return;
                }
                VoidwrightPreflightApproval approval;
                handshakeValid = VoidwrightPreflightApproval.TryAccept(approvalText, DateTime.UtcNow,
                    HandshakeMaximumAge, heartbeat, out approval);
                if (handshakeValid)
                    handshakeReason = "fresh host approval matches this run heartbeat and exact vehicle binding";
                else if (VoidwrightPreflightApproval.IsPendingForNewerHeartbeat(approvalText, DateTime.UtcNow,
                    HandshakeMaximumAge, heartbeat, ScanIntervalTicks))
                    handshakeReason = "waiting for a fresh host approval matching this run heartbeat";
                else
                    handshakeReason = "host approval rejected for current heartbeat; bytes=" + approvalText.Length +
                        ";header=" + (approvalText.IndexOf('\n') < 0 ? "missing" : approvalText.Substring(0, approvalText.IndexOf('\n')));
            }
            catch (Exception error)
            {
                handshakeValid = false;
                handshakeReason = "host preflight approval read failed: " + error.GetType().Name;
            }
        }

        private MovementPreflight BuildPreflight(DateTime now, long simulationTick)
        {
            var contextOkay = context != null && string.IsNullOrEmpty(lastReason);
            var entityOkay = controller != null && camera != null && controller.CubeGrid != null && camera.CubeGrid != null;
            var pinnedTarget = context != null &&
                (context.Action == "direct-maneuver" || context.Action == "preflight-only");
            var exactGridBinding = !pinnedTarget || VehicleControllerBindingPolicy.IsExact(
                controller != null, context.ControllerId, controller == null ? 0 : controller.EntityId,
                context.GridId, controller == null || controller.CubeGrid == null ? 0 : controller.CubeGrid.EntityId,
                camera != null, context.CameraId, camera == null ? 0 : camera.EntityId,
                camera == null || camera.CubeGrid == null ? 0 : camera.CubeGrid.EntityId);
            var agentCoreBound = context == null || context.Action != "direct-maneuver" || IsExpectedAgentCoreOnAnchorGrid(simulationTick);
            var sameGrid = entityOkay && exactGridBinding && agentCoreBound &&
                controller.CubeGrid.EntityId == camera.CubeGrid.EntityId;
            var wheelOkay = wheels.Count > 0;
            var steeringOkay = false;
            var propulsionOkay = false;
            foreach (var wheel in wheels)
            {
                steeringOkay |= wheel.Steering;
                propulsionOkay |= wheel.Propulsion;
            }
            var speed = controller == null ? double.NaN : controller.GetShipSpeed();
            var position = controller == null ? Vector3D.Zero : controller.GetPosition();
            var positionOkay = Finite(position.X) && Finite(position.Y) && Finite(position.Z) && Finite(speed);
            var sensorFresh = entityOkay && context != null &&
                SensorPreflightFreshness.IsFresh(now, simulationTick, scanTick, scanUtc,
                    validRayCount, VoidwrightPreflightHeartbeat.RayCount,
                    controller.CubeGrid.EntityId, camera.CubeGrid.EntityId, scanGridId,
                    camera.EntityId, context.CameraId, scanCameraId,
                    TimeSpan.FromSeconds(1), ScanIntervalTicks * 2);
            var operatorFree = controller != null && !controller.IsUnderControl;
            var localControllerReady = entityOkay && controller.IsFunctional &&
                controller.CubeGrid.Physics != null && operatorFree;
            var localControllerStatus = localControllerReady ? PreflightStatus.Pass :
                (controller == null ? PreflightStatus.Unknown : PreflightStatus.Fail);
            var statusController = MovementPreflight.RequireHostBrakeAttestation(localControllerStatus,
                handshakeValid);
            var directManeuver = context != null && context.Action == "direct-maneuver";
            var safety = contextOkay && (directManeuver
                ? context.MaximumSpeed <= 2.0 && context.TimeoutTicks <= 2700 && speed <= context.MaximumSpeed + 0.1
                : context.MaximumSpeed <= MaximumSpeedMps && context.TimeoutTicks <= 7200 &&
                  context.ArrivalRadius > 0 && speed <= context.MaximumSpeed + 0.1);
            var statusContext = contextOkay ? PreflightStatus.Pass : PreflightStatus.Unknown;
            var statusSession = MyAPIGateway.Session != null && (MyAPIGateway.Utilities.IsDedicated || MyAPIGateway.Session.IsServer)
                ? PreflightStatus.Pass : PreflightStatus.Unknown;
            var statusVehicle = entityOkay ? (sameGrid ? PreflightStatus.Pass : PreflightStatus.Fail) : PreflightStatus.Unknown;
            var statusSensors = !entityOkay ? PreflightStatus.Unknown :
                (sameGrid ? (sensorFresh ? PreflightStatus.Pass : PreflightStatus.Unknown) : PreflightStatus.Fail);
            var statusWheels = MovementPreflight.ResolveActuatorStatus(controller != null,
                wheels.Count, steeringOkay, propulsionOkay, WheelOverridesAreOursOrNeutral());
            if (directManeuver)
                statusWheels = controller == null || wheels.Count == 0 ? PreflightStatus.Unknown :
                    steeringOkay && propulsionOkay ? PreflightStatus.Pass : PreflightStatus.Fail;
            var wheelReason = !wheelOkay ? "No ordinary wheel channels are available." :
                !steeringOkay ? "No steerable wheel channel is available." :
                !propulsionOkay ? "No propulsive wheel channel is available." :
                directManeuver ? "Server plugin is the only participating actuator writer; this mod observes but never writes direct-maneuver wheel channels." :
                statusWheels == PreflightStatus.Pass
                    ? "Wheel actuator values are neutral or already owned by this adapter."
                    : "A steering or propulsion override is non-neutral and is not owned by this adapter; external state is preserved.";
            var statusSafety = MovementPreflight.ResolveSafetyStatus(contextOkay,
                controller != null, Finite(speed), safety, operatorFree);
            var safetyReason = directManeuver
                ? (!contextOkay ? "Direct maneuver run context is unavailable."
                    : !Finite(speed) ? "Direct maneuver current speed is unavailable."
                    : !operatorFree ? "Direct maneuver controller is under operator control."
                    : speed > context.MaximumSpeed + 0.1
                        ? "Direct maneuver speed exceeded its configured limit: current=" + speed.ToString("0.###", CultureInfo.InvariantCulture) +
                          " m/s allowed=" + (context.MaximumSpeed + 0.1).ToString("0.###", CultureInfo.InvariantCulture) + " m/s."
                        : context.MaximumSpeed > 2.0 || context.TimeoutTicks > 2700
                            ? "Direct maneuver configuration exceeds its speed or 2700-tick timeout bound."
                            : "Direct maneuver is passive in the mod; host executor retains independent speed, distance, freshness, and deadline guards.")
                : (safety && operatorFree ? "Speed, arrival radius, timeout, and operator-presence limits are within the ordinary adapter bounds." : "Run command, speed, or operator state violates the safety bounds.");
            var checks = new List<PreflightCheck>
            {
                Check("fixture_source_integrity", statusContext, contextOkay ? "Run artifacts and staged Mods tree match the launcher-bound context; fixture-sector digest is pinned to activation." : lastReason ?? "Run context is not available.", ContextEvidence(), now),
                Check("runtime_connectivity", statusSession, statusSession == PreflightStatus.Pass ? "Voidwright is updating in the authoritative server session." : "Authoritative dedicated/server session is unavailable.", "session=" + (MyAPIGateway.Session != null) + ";server=" + (MyAPIGateway.Session != null && MyAPIGateway.Session.IsServer), now),
                Check("vehicle_identity", statusVehicle, sameGrid ? "Exact controller and depth camera resolve on the explicitly bound target grid." : "Exact controller/camera identities are missing or split across grids.", "controller=" + (controller == null ? 0 : controller.EntityId) + ";grid=" + (controller == null || controller.CubeGrid == null ? 0 : controller.CubeGrid.EntityId) + ";gridName=" + (controller == null || controller.CubeGrid == null ? "unknown" : controller.CubeGrid.DisplayName) + ";camera=" + (camera == null ? 0 : camera.EntityId), now),
                Check("voidwright_module_and_session_marker", statusSession == PreflightStatus.Pass && contextOkay
                    ? (handshakeValid ? PreflightStatus.Pass : PreflightStatus.Unknown) : PreflightStatus.Unknown,
                    handshakeReason, "sessionId=" + sessionId + ";challenge=" + challenge + ";contextSha256=" + contextSha256, now),
                Check("controller_readiness", statusController, "Requires the exact functional remote control, physics grid, no player input, and fresh host approval of grid and per-wheel brake state.", "controllerFunctional=" + (controller != null && controller.IsFunctional) + ";operatorFree=" + operatorFree + ";brakeInputState=unavailable-via-mod-api;hostBrakeApproval=" + handshakeValid, now),
                Check("actuator_availability", statusWheels, wheelReason, WheelActuatorEvidence(steeringOkay, propulsionOkay), now),
                Check("sensor_validity_freshness", statusSensors, sensorFresh ? "All 19 obstacle rays are fresh and bound to the controller grid and pinned camera." : "Waiting for a fresh same-grid 19-ray scan.", "validRays=" + validRayCount + ";scanTick=" + scanTick + ";grid=" + scanGridId + ";camera=" + scanCameraId, now),
                Check("position_validity_freshness", positionOkay ? PreflightStatus.Pass : PreflightStatus.Unknown, positionOkay ? "Current controller position and speed are finite." : "Current position or speed is unavailable.", "position=" + Format(position) + ";speed=" + speed.ToString("0.###", CultureInfo.InvariantCulture), now),
                Check("safety_configuration", statusSafety, safetyReason, "maxSpeed=" + (context == null ? "unknown" : context.MaximumSpeed.ToString("0.###", CultureInfo.InvariantCulture)) + ";currentSpeed=" + speed.ToString("0.###", CultureInfo.InvariantCulture) + ";allowedSpeed=" + (context == null ? "unknown" : (context.MaximumSpeed + 0.1).ToString("0.###", CultureInfo.InvariantCulture)) + ";arrivalRadius=" + (context == null ? "unknown" : context.ArrivalRadius.ToString("0.###", CultureInfo.InvariantCulture)) + ";timeoutTicks=" + (context == null ? 0 : context.TimeoutTicks), now),
                Check("movement_interface_readiness", handshakeValid ? PreflightStatus.Pass : PreflightStatus.Unknown,
                    handshakeValid ? "Host preflight approved the ordinary Voidwright adapter for this exact live heartbeat." : "Waiting for the plugin host to validate this heartbeat; no new movement step is dispatched.",
                    "adapter=IMyMotorSuspension;pluginGridWheels=disabled;approval=" + handshakeValid, now)
            };
            return new MovementPreflight(runId, operationId, FixtureFingerprint(), StateFingerprint(), now, checks);
        }

        private bool ApplyOneBoundedStep(long simulationTick)
        {
            if (controller == null || camera == null || wheels.Count == 0 || terminal) return false;
            if (context != null && context.Action == "direct-maneuver")
            {
                lastReason = "direct maneuver actuator runner is unavailable until controlled brake ownership is implemented";
                StopActuators();
                terminal = true;
                return false;
            }
            if (simulationTick - firstTick >= context.TimeoutTicks)
            {
                StopActuators();
                terminal = true;
                lastReason = "run tick limit reached";
                return false;
            }
            if (controller.IsUnderControl)
            {
                StopActuators();
                terminal = true;
                lastReason = "operator took control; autonomous movement stopped";
                return false;
            }
            foreach (var wheel in wheels)
            {
                SavedWheelState saved;
                if (!savedWheels.TryGetValue(wheel.EntityId, out saved))
                {
                    saved = new SavedWheelState { Block = wheel, SteeringOverride = wheel.SteeringOverride, PropulsionOverride = wheel.PropulsionOverride };
                    if (!WheelActuatorStopPlan.CanAdopt(saved.SteeringOverride, saved.PropulsionOverride))
                    {
                        lastReason = "wheel already has an external steering or propulsion override";
                        terminal = true;
                        StopActuators();
                        return false;
                    }
                    saved.ExpectedSteering = saved.SteeringOverride;
                    saved.ExpectedPropulsion = saved.PropulsionOverride;
                    savedWheels.Add(wheel.EntityId, saved);
                }
            }
            var target = new Vector3D(context.TargetX, context.TargetY, context.TargetZ);
            var delta = target - controller.GetPosition();
            var matrix = controller.WorldMatrix;
            var right = Vector3D.Dot(delta, matrix.Right);
            var up = Vector3D.Dot(delta, matrix.Up);
            var forward = Vector3D.Dot(delta, matrix.Forward);
            var speed = controller.GetShipSpeed();
            var guidance = follower.FollowLocalTarget(MobilityClass.Land, right, up, forward,
                speed, context.MaximumSpeed, context.ArrivalRadius);
            if (guidance.Status == GuidanceStatus.Invalid)
            {
                lastReason = "route guidance rejected non-finite or invalid state";
                terminal = true;
                StopActuators();
                return false;
            }
            if (guidance.Status == GuidanceStatus.Arrived)
            {
                lastReason = "arrival radius reached";
                terminal = true;
                StopActuators();
                return true;
            }
            var bearing = Math.Atan2(right, forward);
            var decision = DepthAvoidancePolicy.SelectHeading(bearing, BeamAngles, clearances,
                ScanRangeMeters, RequiredClearanceMeters, previousDetourHeading);
            if (!DepthAvoidancePolicy.IsNavigationPermitted(decision.Status))
            {
                lastReason = "sensor has no safe corridor or destination is outside the observed fan: " + decision.Status;
                terminal = true;
                StopActuators();
                return false;
            }
            previousDetourHeading = decision.Status == DepthAvoidanceStatus.Avoid ? decision.HeadingRadians : 0;
            var steering = DepthAvoidancePolicy.SteeringCommand(decision.HeadingRadians, context.Reverse);
            var drive = DepthAvoidancePolicy.SignedDriveDuty(guidance.Forward, context.Reverse);
            if (decision.Status == DepthAvoidanceStatus.Avoid) drive = Math.Min(drive, 0.2);
            if (speed >= context.MaximumSpeed) drive = 0;
            foreach (var wheel in wheels)
            {
                SavedWheelState saved;
                if (savedWheels.TryGetValue(wheel.EntityId, out saved))
                {
                    var steeringOverride = wheel.Steering ? (float)steering : 0;
                    var propulsionOverride = wheel.Propulsion ? (float)drive : 0;
                    // Record each attempted value before its setter. If the setter applies
                    // and then throws, the exception stop can still recognize and clear it.
                    adapterOwnsActuators = true;
                    saved.OwnsSteering = true;
                    saved.ExpectedSteering = steeringOverride;
                    wheel.SteeringOverride = steeringOverride;
                    saved.OwnsPropulsion = true;
                    saved.ExpectedPropulsion = propulsionOverride;
                    wheel.PropulsionOverride = propulsionOverride;
                }
            }
            return true;
        }

        private void StopActuators()
        {
            if (!adapterOwnsActuators) return;
            var externalChange = false;
            var stopWriteFailed = false;
            foreach (var saved in savedWheels.Values)
            {
                var wheel = saved.Block;
                if (wheel == null) continue;
                WheelActuatorStopPlan plan;
                try
                {
                    plan = WheelActuatorStopPlan.Create(wheel.SteeringOverride, saved.ExpectedSteering,
                        wheel.PropulsionOverride, saved.ExpectedPropulsion,
                        saved.OwnsSteering, saved.OwnsPropulsion);
                }
                catch { externalChange = true; continue; }
                externalChange |= plan.HasExternalChanges;
                if (plan.ClearSteering)
                {
                    try
                    {
                        wheel.SteeringOverride = 0;
                        if (WheelActuatorStopPlan.IsNeutral(wheel.SteeringOverride)) saved.ExpectedSteering = 0;
                        else stopWriteFailed = true;
                    }
                    catch { stopWriteFailed = true; }
                }
                if (plan.ClearPropulsion)
                {
                    try
                    {
                        wheel.PropulsionOverride = 0;
                        if (WheelActuatorStopPlan.IsNeutral(wheel.PropulsionOverride)) saved.ExpectedPropulsion = 0;
                        else stopWriteFailed = true;
                    }
                    catch { stopWriteFailed = true; }
                }
            }
            // Keep the ownership record if a stop write failed so Unload/Stop can retry.
            adapterOwnsActuators = stopWriteFailed;
            if (externalChange)
                lastReason = (lastReason ?? "adapter stopped") + "; changed external wheel input was preserved";
            if (stopWriteFailed)
                lastReason = (lastReason ?? "adapter stop") + "; one or more owned wheel stop writes failed";
        }

        private void WriteEvidence(long simulationTick, MovementPreflight preflight)
        {
            if (context == null || preflight == null || MyAPIGateway.Utilities == null) return;
            try
            {
                using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(
                           "voidwright-preflight-evidence-" + runId + "-" + operationId + ".json",
                           typeof(MySessionComponentBase)))
                    writer.Write(preflight.ToJson());
                lastEvidenceTick = simulationTick;
            }
            catch (Exception error)
            {
                lastReason = "preflight evidence write failed: " + error.GetType().Name;
                try { WriteOperationEvidence(simulationTick, "type=depth-controller", preflight); } catch { }
                return;
            }
            try { WriteOperationEvidence(simulationTick, context.Action == "preflight-only"
                ? "type=preflight-only"
                : "type=depth-controller", preflight); }
            catch { /* Best-effort telemetry must not change the actuator decision. */ }
        }

        private void FinishOperationTimeout(long simulationTick)
        {
            forcedTerminalStatus = "timeout";
            lastReason = "run tick limit reached";
            try { StopActuators(); }
            catch (Exception error)
            {
                lastReason += "; actuator stop threw " + error.GetType().Name;
            }
            terminal = true;
            MovementPreflight preflight = null;
            if (context != null && MyAPIGateway.Utilities != null)
            {
                try
                {
                    var now = DateTime.UtcNow;
                    preflight = BuildPreflight(now, simulationTick).Invalidate(now, lastReason);
                    using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(
                               "voidwright-preflight-evidence-" + runId + "-" + operationId + ".json",
                               typeof(MySessionComponentBase)))
                        writer.Write(preflight.ToJson());
                    lastEvidenceTick = simulationTick;
                }
                catch { /* Timeout remains terminal even if its optional snapshot cannot be stored. */ }
            }
            try { WriteOperationEvidence(simulationTick, "type=depth-controller", preflight); }
            catch
            {
                // Preserve the terminal event for VRageCage even if a closed
                // controller makes the richer telemetry unavailable.
                try
                {
                    MyLog.Default.WriteLineAndConsole("Voidwright evidence: type=depth-controller operation=" + operationId +
                        " controllerId=" + (context == null ? 0 : context.ControllerId) +
                        " cameraId=" + (context == null ? 0 : context.CameraId) +
                        " status=timeout elapsedTicks=" + Math.Max(0, simulationTick - firstTick) +
                        " position=unknown distanceToTargetMeters=unknown speedMps=unknown" +
                        " linearVelocity=unavailable actuatorOwner=Voidwright.Game actuatorDispatch=none" +
                        " wheels= sensorValidRays=" + validRayCount + "/" + clearances.Length +
                        " collisionContact=unavailable-via-current-mod-api reason=run_tick_limit_reached");
                }
                catch { }
            }
        }

        private void WriteOperationEvidence(long simulationTick, string eventType, MovementPreflight preflight = null)
        {
            var status = "progress";
            if (terminal || forcedTerminalStatus != null)
                status = MovementPreflight.ResolveTerminalStatus(lastReason, forcedTerminalStatus);
            var position = controller == null ? "unknown" : Format(controller.GetPosition());
            var speed = controller == null ? "unknown" : controller.GetShipSpeed().ToString("0.###", CultureInfo.InvariantCulture);
            var linearVelocity = "unavailable";
            if (controller != null && controller.CubeGrid != null && controller.CubeGrid.Physics != null)
            {
                var velocity = new Vector3D(controller.CubeGrid.Physics.LinearVelocity);
                linearVelocity = Format(velocity) + ":" + velocity.Length().ToString("0.###", CultureInfo.InvariantCulture);
            }
            var distance = "unknown";
            if (controller != null && context != null)
            {
                var target = new Vector3D(context.TargetX, context.TargetY, context.TargetZ);
                distance = Vector3D.Distance(controller.GetPosition(), target).ToString("0.###", CultureInfo.InvariantCulture);
            }
            var wheelEvidence = new StringBuilder();
            foreach (var wheel in wheels)
            {
                if (wheelEvidence.Length > 0) wheelEvidence.Append(';');
                wheelEvidence.Append(wheel.EntityId.ToString(CultureInfo.InvariantCulture))
                    .Append(":steering=").Append(wheel.Steering ? "1" : "0")
                    .Append(":steerOverride=").Append(wheel.SteeringOverride.ToString("0.###", CultureInfo.InvariantCulture))
                    .Append(":steerAngle=").Append(wheel.SteerAngle.ToString("0.###", CultureInfo.InvariantCulture))
                    .Append(":propulsion=").Append(wheel.Propulsion ? "1" : "0")
                    .Append(":propulsionOverride=").Append(wheel.PropulsionOverride.ToString("0.###", CultureInfo.InvariantCulture))
                    .Append(":brakingEnabled=").Append(wheel.Brake ? "1" : "0");
            }
            var sensorEvidence = new StringBuilder();
            for (var index = 0; index < clearances.Length; index++)
            {
                if (index > 0) sensorEvidence.Append(',');
                sensorEvidence.Append(clearances[index].ToString("0.###", CultureInfo.InvariantCulture));
            }
            MyLog.Default.WriteLineAndConsole("Voidwright evidence: " + eventType + " operation=" + operationId +
                " controllerId=" + (controller == null ? context.ControllerId.ToString(CultureInfo.InvariantCulture) : controller.EntityId.ToString(CultureInfo.InvariantCulture)) +
                " cameraId=" + context.CameraId.ToString(CultureInfo.InvariantCulture) + " status=" + status +
                " elapsedTicks=" + Math.Max(0, simulationTick - firstTick) + " position=" + position +
                " distanceToTargetMeters=" + distance + " speedMps=" + speed + " linearVelocity=" + linearVelocity +
                " actuatorOwner=Voidwright.Game actuatorDispatch=" + (adapterOwnsActuators ? "mod-wheel-override-applied" : "none") +
                " wheels=" + wheelEvidence + " sensorValidRays=" + validRayCount + "/" + clearances.Length +
                " sensorClearanceMeters=" + sensorEvidence + " collisionContact=unavailable-via-current-mod-api" +
                (string.IsNullOrEmpty(lastReason) ? "" : " reason=" + lastReason.Replace(' ', '_')) +
                (preflight == null || string.IsNullOrEmpty(preflight.NonPassingSummary())
                    ? "" : " preflightIssues=" + preflight.NonPassingSummary()));
        }

        private string ContextEvidence()
            => context == null ? "unavailable" : "run=" + context.RunId + ";fixture=" + context.FixtureId + ";source=" + context.SourceTreeSha256 + ";context=" + contextSha256;

        private string FixtureFingerprint() => (context == null ? fixtureId : context.FixtureId) + ":" + contextSha256;

        private string StateFingerprint()
            => (controller == null ? "missing" : controller.EntityId.ToString(CultureInfo.InvariantCulture)) + ":" +
               (controller == null || controller.CubeGrid == null ? "missing" : controller.CubeGrid.EntityId.ToString(CultureInfo.InvariantCulture)) + ":" +
               (camera == null ? "missing" : camera.EntityId.ToString(CultureInfo.InvariantCulture)) + ":" + operationId;

        private static PreflightCheck Check(string name, PreflightStatus status, string reason, string evidence, DateTime now)
            => new PreflightCheck(name, status, reason ?? "", evidence ?? "", "VoidwrightLabPreflight", now);

        private static double[] CreateBeamAngles()
        {
            var result = new double[VoidwrightPreflightHeartbeat.RayCount];
            for (var index = 0; index < result.Length; index++) result[index] = (-90 + index * 10) * Math.PI / 180.0;
            return result;
        }

        private static string Format(Vector3D value)
            => value.X.ToString("0.###", CultureInfo.InvariantCulture) + "," +
               value.Y.ToString("0.###", CultureInfo.InvariantCulture) + "," +
               value.Z.ToString("0.###", CultureInfo.InvariantCulture);

        private static bool Hex32(string value)
        {
            if (value == null || value.Length != 32) return false;
            foreach (var character in value)
                if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f'))) return false;
            return true;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private bool WheelOverridesAreOursOrNeutral()
        {
            foreach (var wheel in wheels)
            {
                SavedWheelState saved;
                if (savedWheels.TryGetValue(wheel.EntityId, out saved))
                {
                    if (!WheelActuatorStopPlan.IsOwnedValue(wheel.SteeringOverride, saved.ExpectedSteering) ||
                        !WheelActuatorStopPlan.IsOwnedValue(wheel.PropulsionOverride, saved.ExpectedPropulsion)) return false;
                }
                else if (!WheelActuatorStopPlan.CanAdopt(wheel.SteeringOverride, wheel.PropulsionOverride))
                    return false;
            }
            return true;
        }

        private string WheelActuatorEvidence(bool steeringOkay, bool propulsionOkay)
        {
            var state = new StringBuilder();
            foreach (var wheel in wheels)
            {
                if (state.Length > 0) state.Append(';');
                state.Append(wheel.EntityId.ToString(CultureInfo.InvariantCulture))
                    .Append(":steer=").Append(wheel.SteeringOverride.ToString("0.###", CultureInfo.InvariantCulture))
                    .Append(":propulsion=").Append(wheel.PropulsionOverride.ToString("0.###", CultureInfo.InvariantCulture))
                    // In the installed game SDK, IMyMotorSuspension.Brake maps
                    // to MyMotorSuspension.BrakingEnabled. It is a wheel setting,
                    // not the current grid brake input; never use it as one.
                    .Append(":brakingEnabled=").Append(wheel.Brake ? "1" : "0")
                    .Append(":parkingEnabled=").Append(wheel.IsParkingEnabled ? "1" : "0");
            }
            return "wheels=" + wheels.Count + ";steering=" + steeringOkay + ";propulsion=" + propulsionOkay +
                ";gridHandBrake=unavailable-via-mod-api;gridBrake=unavailable-via-mod-api;wheelActiveBrake=unavailable-via-mod-api" +
                ";remoteAutoPilot=" + (controller == null ? "unknown" :
                    (controller as IMyRemoteControl == null ? "not-applicable" :
                        (controller as IMyRemoteControl).IsAutoPilotEnabled.ToString())) +
                ";operatorUnderControl=" + (controller != null && controller.IsUnderControl) +
                ";adapter=Voidwright.Game;wheelState=" + state;
        }

    }
}
