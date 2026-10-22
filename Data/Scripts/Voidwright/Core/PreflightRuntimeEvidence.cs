using System;
using System.Globalization;
using System.Text;

namespace Voidwright.Core
{
    /// <summary>
    /// Host-verified, bounded activation payload delivered to the game mod over
    /// Space Engineers WorldStorage. It contains no host paths or credentials.
    /// </summary>
    public sealed class VoidwrightModActivation
    {
        public const string FileName = "voidwright-lab-activation-v1.txt";
        private const string Header = "VWPA1";
        private const string DirectHeader = "VWPA2";
        private const string CoreBoundDirectHeader = "VWPA3";
        private const string DiscoveryBoundHeader = "VWPA4";
        public string RunId, FixtureId, OperationId, ContextSha256, Challenge, SourceTreeSha256;
        public string Action, DirectManeuverSegments;
        public long ControllerId, CameraId;
        public long GridId;
        public long AgentCoreId;
        public string DiscoveryRunId, DiscoveryFingerprint;
        public long DiscoveryTick;
        public double TargetX, TargetY, TargetZ, MaximumSpeed, ArrivalRadius;
        public int TimeoutTicks, StartDelayTicks;
        public bool Reverse;
        public DateTime IssuedUtc, ExpiresUtc;

        public string Format()
        {
            var directManeuver = Action == "direct-maneuver";
            var extendedBinding = directManeuver || Action == "preflight-only";
            if (!Identity(RunId, 32) || !Identity(FixtureId, 32) || !Identity(OperationId, 32) ||
                !Identity(ContextSha256, 64) || !Identity(Challenge, 32) || !Identity(SourceTreeSha256, 64) ||
                ControllerId <= 0 || CameraId <= 0 || !Finite(TargetX) || !Finite(TargetY) || !Finite(TargetZ) ||
                !Finite(MaximumSpeed) || MaximumSpeed <= 0 || MaximumSpeed > 0.8 ||
                !Finite(ArrivalRadius) || ArrivalRadius <= 0 || ArrivalRadius > 5 ||
                TimeoutTicks < 60 || TimeoutTicks > 7200 || StartDelayTicks < 0 || StartDelayTicks > 3600 ||
                ExpiresUtc <= IssuedUtc || ExpiresUtc - IssuedUtc > TimeSpan.FromMinutes(5))
                throw new ArgumentException("Invalid Voidwright mod activation.");
            DirectManeuverPlan ignoredPlan;
            if (extendedBinding && (GridId <= 0 || AgentCoreId <= 0 ||
                !Identity(DiscoveryRunId, 32) || DiscoveryTick < 0 || !Identity(DiscoveryFingerprint, 64) ||
                (directManeuver && !DirectManeuverPlan.TryParseCompactSegments(DirectManeuverSegments, RunId, FixtureId,
                    OperationId, GridId, ControllerId, AgentCoreId, out ignoredPlan)) ||
                (Action == "preflight-only" && !string.IsNullOrEmpty(DirectManeuverSegments))))
                throw new ArgumentException("Invalid direct maneuver activation binding or segment plan.");
            var values = new[] { RunId, FixtureId, OperationId, ContextSha256,
                Challenge, ControllerId.ToString(CultureInfo.InvariantCulture), CameraId.ToString(CultureInfo.InvariantCulture),
                TargetX.ToString("R", CultureInfo.InvariantCulture), TargetY.ToString("R", CultureInfo.InvariantCulture),
                TargetZ.ToString("R", CultureInfo.InvariantCulture), MaximumSpeed.ToString("R", CultureInfo.InvariantCulture),
                TimeoutTicks.ToString(CultureInfo.InvariantCulture), ArrivalRadius.ToString("R", CultureInfo.InvariantCulture),
                StartDelayTicks.ToString(CultureInfo.InvariantCulture), Reverse ? "1" : "0", SourceTreeSha256,
                IssuedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                ExpiresUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) };
            return (extendedBinding
                ? DiscoveryBoundHeader + "\n" + string.Join("|", values) + "|" + Action + "|" +
                  GridId.ToString(CultureInfo.InvariantCulture) + "|" +
                  AgentCoreId.ToString(CultureInfo.InvariantCulture) + "|" + DiscoveryRunId + "|" +
                  DiscoveryTick.ToString(CultureInfo.InvariantCulture) + "|" + DiscoveryFingerprint + "|" + DirectManeuverSegments
                : Header + "\n" + string.Join("|", values)) + "\n";
        }

        public static bool TryParse(string text, DateTime nowUtc, out VoidwrightModActivation activation)
        {
            activation = null;
            if (string.IsNullOrEmpty(text) || text.Length > 8192 || !text.EndsWith("\n", StringComparison.Ordinal)) return false;
            var lines = text.Split('\n');
            if (lines.Length != 3 || (lines[0] != Header && lines[0] != DirectHeader && lines[0] != CoreBoundDirectHeader &&
                lines[0] != DiscoveryBoundHeader) || lines[2].Length != 0) return false;
            var coreBoundDirect = lines[0] == CoreBoundDirectHeader;
            var discoveryBound = lines[0] == DiscoveryBoundHeader;
            var extendedBinding = lines[0] == DirectHeader || coreBoundDirect || discoveryBound;
            var f = lines[1].Split('|');
            long controller, camera;
            int timeout, delay;
            double x, y, z, speed, radius;
            DateTime issued, expires;
            if (f.Length != (discoveryBound ? 25 : coreBoundDirect ? 22 : extendedBinding ? 21 : 18) ||
                !Identity(f[0], 32) || !Identity(f[1], 32) || !Identity(f[2], 32) ||
                !Identity(f[3], 64) || !Identity(f[4], 32) || !Identity(f[15], 64) ||
                !long.TryParse(f[5], NumberStyles.None, CultureInfo.InvariantCulture, out controller) || controller <= 0 ||
                !long.TryParse(f[6], NumberStyles.None, CultureInfo.InvariantCulture, out camera) || camera <= 0 ||
                !double.TryParse(f[7], NumberStyles.Float, CultureInfo.InvariantCulture, out x) || !Finite(x) ||
                !double.TryParse(f[8], NumberStyles.Float, CultureInfo.InvariantCulture, out y) || !Finite(y) ||
                !double.TryParse(f[9], NumberStyles.Float, CultureInfo.InvariantCulture, out z) || !Finite(z) ||
                !double.TryParse(f[10], NumberStyles.Float, CultureInfo.InvariantCulture, out speed) || !Finite(speed) || speed <= 0 || speed > 0.8 ||
                !int.TryParse(f[11], NumberStyles.None, CultureInfo.InvariantCulture, out timeout) || timeout < 60 || timeout > 7200 ||
                !double.TryParse(f[12], NumberStyles.Float, CultureInfo.InvariantCulture, out radius) || !Finite(radius) || radius <= 0 || radius > 5 ||
                !int.TryParse(f[13], NumberStyles.None, CultureInfo.InvariantCulture, out delay) || delay < 0 || delay > 3600 ||
                (f[14] != "0" && f[14] != "1") ||
                !DateTime.TryParseExact(f[16], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out issued) || issued.Kind != DateTimeKind.Utc ||
                !DateTime.TryParseExact(f[17], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out expires) || expires.Kind != DateTimeKind.Utc ||
                expires <= issued || expires - issued > TimeSpan.FromMinutes(5) || nowUtc.ToUniversalTime() < issued || nowUtc.ToUniversalTime() >= expires)
                return false;
            long gridId = 0;
            long agentCoreId = 0;
            long discoveryTick = 0;
            string discoveryRunId = string.Empty, discoveryFingerprint = string.Empty;
            var action = "drive-to-coordinate-depth";
            var segments = string.Empty;
            if (extendedBinding && f[18] != "direct-maneuver" && f[18] != "preflight-only") return false;
            var directManeuver = extendedBinding && f[18] == "direct-maneuver";
            var preflightOnly = extendedBinding && f[18] == "preflight-only";
            if (directManeuver || preflightOnly)
            {
                DirectManeuverPlan ignoredPlan;
                var segmentIndex = discoveryBound ? 24 : coreBoundDirect ? 21 : 20;
                if (!long.TryParse(f[19], NumberStyles.None, CultureInfo.InvariantCulture, out gridId) || gridId <= 0 ||
                    (discoveryBound && (!long.TryParse(f[20], NumberStyles.None, CultureInfo.InvariantCulture, out agentCoreId) || agentCoreId <= 0 ||
                     !Identity(f[21], 32) || !long.TryParse(f[22], NumberStyles.None, CultureInfo.InvariantCulture, out discoveryTick) || discoveryTick < 0 ||
                     !Identity(f[23], 64))) ||
                    (directManeuver && (!discoveryBound && !coreBoundDirect ||
                     !discoveryBound && (!long.TryParse(f[20], NumberStyles.None, CultureInfo.InvariantCulture, out agentCoreId) || agentCoreId <= 0) ||
                     !DirectManeuverPlan.TryParseCompactSegments(f[segmentIndex], f[0], f[1], f[2], gridId, controller, agentCoreId, out ignoredPlan))) ||
                    (preflightOnly && (discoveryBound ? !string.IsNullOrEmpty(f[24]) : !string.IsNullOrEmpty(f[20])))) return false;
                if (discoveryBound)
                { discoveryRunId = f[21]; discoveryFingerprint = f[23]; }
            }
            else if (extendedBinding) return false;
            if (directManeuver || preflightOnly) { action = f[18]; segments = f[discoveryBound ? 24 : coreBoundDirect ? 21 : 20]; }
            activation = new VoidwrightModActivation { RunId = f[0], FixtureId = f[1], OperationId = f[2],
                ContextSha256 = f[3], Challenge = f[4], ControllerId = controller, CameraId = camera,
                TargetX = x, TargetY = y, TargetZ = z, MaximumSpeed = speed, TimeoutTicks = timeout,
                ArrivalRadius = radius, StartDelayTicks = delay, Reverse = f[14] == "1", SourceTreeSha256 = f[15],
                IssuedUtc = issued, ExpiresUtc = expires, Action = action, GridId = gridId,
                AgentCoreId = agentCoreId, DiscoveryRunId = discoveryRunId,
                DiscoveryTick = discoveryTick, DiscoveryFingerprint = discoveryFingerprint,
                DirectManeuverSegments = segments };
            return true;
        }

        private static bool Identity(string value, int length)
        {
            if (value == null || value.Length != length) return false;
            foreach (var c in value) if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            return true;
        }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }

    /// <summary>Host acknowledgement of a fresh, mod-published run-bound heartbeat.</summary>
    public sealed class VoidwrightPreflightApproval
    {
        public const string FileName = "voidwright-preflight-approval-v1.txt";
        private const string Header = "VWPAK2";
        private const string DiscoveryHeader = "VWPAK3";
        public string RunId, FixtureId, OperationId, ContextSha256, Challenge, SessionId;
        public string DiscoveryRunId, DiscoveryFingerprint;
        public long ControllerId, GridId, CameraId, AgentCoreId, SimulationTick, SensorScanTick;
        public long DiscoveryTick;
        public DateTime PublishedUtc;
        public string Format()
        {
            var discoveryBound = !string.IsNullOrEmpty(DiscoveryRunId);
            if (discoveryBound && (!Identity(DiscoveryRunId, 32) || DiscoveryTick < 0 ||
                !Identity(DiscoveryFingerprint, 64)))
                throw new ArgumentException("Invalid discovery binding in Voidwright approval.");
            var values = new[] { RunId, FixtureId, OperationId, ContextSha256, Challenge,
                SessionId, ControllerId.ToString(CultureInfo.InvariantCulture), GridId.ToString(CultureInfo.InvariantCulture),
                CameraId.ToString(CultureInfo.InvariantCulture), SimulationTick.ToString(CultureInfo.InvariantCulture),
                SensorScanTick.ToString(CultureInfo.InvariantCulture), PublishedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                AgentCoreId.ToString(CultureInfo.InvariantCulture) };
            if (discoveryBound)
                return DiscoveryHeader + "\n" + string.Join("|", values) + "|" + DiscoveryRunId + "|" +
                    DiscoveryTick.ToString(CultureInfo.InvariantCulture) + "|" + DiscoveryFingerprint + "\n";
            return Header + "\n" + string.Join("|", values) + "\n";
        }
        public static bool TryAccept(string text, DateTime nowUtc, TimeSpan maximumAge,
            VoidwrightPreflightHeartbeat heartbeat, out VoidwrightPreflightApproval approval)
        {
            approval = null;
            if (heartbeat == null || string.IsNullOrEmpty(text) || text.Length > 1024 ||
                !text.EndsWith("\n", StringComparison.Ordinal)) return false;
            var lines = text.Split('\n');
            if (lines.Length != 3 || (lines[0] != Header && lines[0] != DiscoveryHeader) || lines[2].Length != 0) return false;
            var discoveryBound = lines[0] == DiscoveryHeader;
            var f = lines[1].Split('|');
            long controller, grid, camera, tick, scanTick, agentCoreId, discoveryTick = 0;
            DateTime published;
            if (f.Length != (discoveryBound ? 16 : 13) ||
                discoveryBound != !string.IsNullOrEmpty(heartbeat.DiscoveryRunId) ||
                f[0] != heartbeat.RunId || f[1] != heartbeat.FixtureId ||
                f[2] != heartbeat.OperationId || f[3] != heartbeat.ContextSha256 || f[4] != heartbeat.Challenge ||
                f[5] != heartbeat.SessionId || !long.TryParse(f[6], NumberStyles.None, CultureInfo.InvariantCulture, out controller) ||
                !long.TryParse(f[7], NumberStyles.None, CultureInfo.InvariantCulture, out grid) ||
                !long.TryParse(f[8], NumberStyles.None, CultureInfo.InvariantCulture, out camera) ||
                !long.TryParse(f[9], NumberStyles.None, CultureInfo.InvariantCulture, out tick) ||
                !long.TryParse(f[10], NumberStyles.None, CultureInfo.InvariantCulture, out scanTick) ||
                !DateTime.TryParseExact(f[11], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out published) ||
                !long.TryParse(f[12], NumberStyles.None, CultureInfo.InvariantCulture, out agentCoreId) || agentCoreId < 0 ||
                (discoveryBound && (!Identity(f[13], 32) ||
                 !long.TryParse(f[14], NumberStyles.None, CultureInfo.InvariantCulture, out discoveryTick) || discoveryTick < 0 ||
                 !Identity(f[15], 64))) ||
                published.Kind != DateTimeKind.Utc || controller != heartbeat.ControllerId || grid != heartbeat.GridId ||
                camera != heartbeat.CameraId || tick != heartbeat.SimulationTick || scanTick != heartbeat.SensorScanTick ||
                agentCoreId != heartbeat.AgentCoreId ||
                (discoveryBound && (f[13] != heartbeat.DiscoveryRunId || discoveryTick != heartbeat.DiscoveryTick ||
                 f[15] != heartbeat.DiscoveryFingerprint)) ||
                published < heartbeat.PublishedUtc.ToUniversalTime() ||
                nowUtc.ToUniversalTime() < published || nowUtc.ToUniversalTime() - published > maximumAge ||
                !heartbeat.SensorValid || !heartbeat.ActuatorReady || heartbeat.ValidRayCount != VoidwrightPreflightHeartbeat.RayCount)
                return false;
            approval = new VoidwrightPreflightApproval { RunId = f[0], FixtureId = f[1], OperationId = f[2],
                ContextSha256 = f[3], Challenge = f[4], SessionId = f[5], ControllerId = controller,
                GridId = grid, CameraId = camera, SimulationTick = tick, SensorScanTick = scanTick,
                AgentCoreId = agentCoreId, PublishedUtc = published,
                DiscoveryRunId = discoveryBound ? f[13] : string.Empty,
                DiscoveryTick = discoveryTick, DiscoveryFingerprint = discoveryBound ? f[15] : string.Empty };
            return true;
        }

        /// <summary>
        /// Identifies a fresh acknowledgement for the immediately preceding heartbeat of
        /// the same run and exact vehicle binding. This is only a handshake-pending state;
        /// callers must never use it to authorize actuator dispatch.
        /// </summary>
        public static bool IsPendingForNewerHeartbeat(string text, DateTime nowUtc, TimeSpan maximumAge,
            VoidwrightPreflightHeartbeat heartbeat, long maximumTickLag)
        {
            if (heartbeat == null || string.IsNullOrEmpty(text) || text.Length > 1024 || maximumTickLag < 1 ||
                !text.EndsWith("\n", StringComparison.Ordinal)) return false;
            var lines = text.Split('\n');
            if (lines.Length != 3 || (lines[0] != Header && lines[0] != DiscoveryHeader) || lines[2].Length != 0) return false;
            var discoveryBound = lines[0] == DiscoveryHeader;
            var f = lines[1].Split('|');
            long controller, grid, camera, tick, scanTick, agentCoreId, discoveryTick = 0;
            DateTime published;
            if (f.Length != (discoveryBound ? 16 : 13) ||
                discoveryBound != !string.IsNullOrEmpty(heartbeat.DiscoveryRunId) ||
                f[0] != heartbeat.RunId || f[1] != heartbeat.FixtureId ||
                f[2] != heartbeat.OperationId || f[3] != heartbeat.ContextSha256 || f[4] != heartbeat.Challenge ||
                f[5] != heartbeat.SessionId || !long.TryParse(f[6], NumberStyles.None, CultureInfo.InvariantCulture, out controller) ||
                !long.TryParse(f[7], NumberStyles.None, CultureInfo.InvariantCulture, out grid) ||
                !long.TryParse(f[8], NumberStyles.None, CultureInfo.InvariantCulture, out camera) ||
                !long.TryParse(f[9], NumberStyles.None, CultureInfo.InvariantCulture, out tick) ||
                !long.TryParse(f[10], NumberStyles.None, CultureInfo.InvariantCulture, out scanTick) ||
                !DateTime.TryParseExact(f[11], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out published) ||
                !long.TryParse(f[12], NumberStyles.None, CultureInfo.InvariantCulture, out agentCoreId) ||
                agentCoreId != heartbeat.AgentCoreId ||
                (discoveryBound && (!Identity(f[13], 32) || f[13] != heartbeat.DiscoveryRunId ||
                 !long.TryParse(f[14], NumberStyles.None, CultureInfo.InvariantCulture, out discoveryTick) ||
                 discoveryTick != heartbeat.DiscoveryTick || f[15] != heartbeat.DiscoveryFingerprint)) ||
                published.Kind != DateTimeKind.Utc || controller != heartbeat.ControllerId || grid != heartbeat.GridId ||
                camera != heartbeat.CameraId || tick >= heartbeat.SimulationTick ||
                heartbeat.SimulationTick - tick > maximumTickLag || scanTick > tick ||
                nowUtc.ToUniversalTime() < published || nowUtc.ToUniversalTime() - published > maximumAge ||
                !heartbeat.SensorValid || !heartbeat.ActuatorReady ||
                heartbeat.ValidRayCount != VoidwrightPreflightHeartbeat.RayCount)
                return false;
            return true;
        }

        private static bool Identity(string value, int length)
        {
            if (value == null || value.Length != length) return false;
            foreach (var c in value) if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            return true;
        }
    }

    /// <summary>Run-bound live Voidwright session and sensor receipt shared by the mod and preflight reader.</summary>
    public sealed class VoidwrightPreflightHeartbeat
    {
        public const string FileName = "voidwright-preflight-heartbeat-v1.txt";
        public const int RayCount = 19;
        private const string Header = "VWPF3";
        private const string DiscoveryHeader = "VWPF4";
        private const string CapabilityHeader = "VWPF5";

        public string RunId;
        public string FixtureId;
        public string OperationId;
        public string ContextSha256;
        public string Challenge;
        public string SessionId;
        public long ControllerId;
        public long GridId;
        public long AgentCoreId;
        public string DiscoveryRunId, DiscoveryFingerprint;
        public long DiscoveryTick;
        public long CameraId;
        public long SimulationTick;
        public DateTime PublishedUtc;
        public bool SensorValid;
        public long SensorScanTick;
        public DateTime SensorScanUtc;
        public long SensorGridId;
        public long SensorCameraId;
        public int ValidRayCount;
        public int WheelCount;
        public int SteeringWheelCount;
        public int PropulsionWheelCount;
        public bool ActuatorReady;
        public string OperationCapabilityToken;
        public double[] Clearances = new double[RayCount];

        public string Format()
        {
            var discoveryBound = !string.IsNullOrEmpty(DiscoveryRunId);
            var capabilityBound = !string.IsNullOrEmpty(OperationCapabilityToken);
            if (!Hex32(RunId) || !Hex32(FixtureId) || !Hex32(OperationId) || !Hex64(ContextSha256) ||
                !Hex32(Challenge) || !Hex32(SessionId) || ControllerId <= 0 || GridId <= 0 ||
                CameraId <= 0 || AgentCoreId < 0 || SimulationTick < 0 || SensorScanTick < 0 || SensorGridId <= 0 ||
                SensorCameraId <= 0 || ValidRayCount < 0 || ValidRayCount > RayCount ||
                (SensorValid && ValidRayCount != RayCount) || WheelCount < 0 || WheelCount > 256 ||
                SteeringWheelCount < 0 || SteeringWheelCount > WheelCount || PropulsionWheelCount < 0 ||
                PropulsionWheelCount > WheelCount || (ActuatorReady && (WheelCount == 0 || SteeringWheelCount == 0 || PropulsionWheelCount == 0)) ||
                Clearances == null || Clearances.Length != RayCount ||
                (discoveryBound && (!Hex32(DiscoveryRunId) || DiscoveryTick < 0 || !Hex64(DiscoveryFingerprint))) ||
                (capabilityBound && (!discoveryBound || !VoidwrightOperationCapability.IsToken(OperationCapabilityToken))) ||
                (!discoveryBound && AgentCoreId > 0))
                throw new ArgumentException("Invalid Voidwright preflight heartbeat identity.");
            var text = new StringBuilder(capabilityBound ? CapabilityHeader : discoveryBound ? DiscoveryHeader : Header);
            text.Append('\n').Append(RunId).Append('|').Append(FixtureId).Append('|').Append(OperationId)
                .Append('|').Append(ContextSha256).Append('|').Append(Challenge).Append('|').Append(SessionId)
                .Append('|').Append(ControllerId.ToString(CultureInfo.InvariantCulture))
                .Append('|').Append(GridId.ToString(CultureInfo.InvariantCulture))
                .Append('|').Append(AgentCoreId.ToString(CultureInfo.InvariantCulture))
                .Append('|').Append(CameraId.ToString(CultureInfo.InvariantCulture))
                .Append('|').Append(SimulationTick.ToString(CultureInfo.InvariantCulture))
                .Append('|').Append(PublishedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
                .Append('|').Append(SensorValid ? "1" : "0")
                .Append('|').Append(SensorScanTick.ToString(CultureInfo.InvariantCulture))
                .Append('|').Append(SensorScanUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
                .Append('|').Append(SensorGridId.ToString(CultureInfo.InvariantCulture))
                .Append('|').Append(SensorCameraId.ToString(CultureInfo.InvariantCulture))
                .Append('|').Append(ValidRayCount.ToString(CultureInfo.InvariantCulture))
                .Append('|').Append(WheelCount.ToString(CultureInfo.InvariantCulture))
                .Append('|').Append(SteeringWheelCount.ToString(CultureInfo.InvariantCulture))
                .Append('|').Append(PropulsionWheelCount.ToString(CultureInfo.InvariantCulture))
                .Append('|').Append(ActuatorReady ? "1" : "0");
            if (discoveryBound)
                text.Append('|').Append(DiscoveryRunId)
                    .Append('|').Append(DiscoveryTick.ToString(CultureInfo.InvariantCulture))
                    .Append('|').Append(DiscoveryFingerprint);
            if (capabilityBound) text.Append('|').Append(OperationCapabilityToken);
            foreach (var clearance in Clearances)
            {
                if (!Finite(clearance) || clearance < 0 || clearance > 1000)
                    throw new ArgumentException("Invalid preflight sensor clearance.");
                text.Append('|').Append(clearance.ToString("R", CultureInfo.InvariantCulture));
            }
            return text.Append('\n').ToString();
        }

        public bool MatchesSerializedReadback(string text)
        {
            try { return string.Equals(Format(), text, StringComparison.Ordinal); }
            catch { return false; }
        }

        public static bool TryParse(string text, out VoidwrightPreflightHeartbeat heartbeat)
        {
            heartbeat = null;
            if (string.IsNullOrEmpty(text) || text.Length > 8192 || !text.EndsWith("\n", StringComparison.Ordinal)) return false;
            var lines = text.Split('\n');
            if (lines.Length != 3 || (lines[0] != Header && lines[0] != DiscoveryHeader && lines[0] != CapabilityHeader) || lines[2] != string.Empty) return false;
            var capabilityBound = lines[0] == CapabilityHeader;
            var discoveryBound = lines[0] == DiscoveryHeader || capabilityBound;
            var fields = lines[1].Split('|');
            if (fields.Length != (discoveryBound ? (capabilityBound ? 26 : 25) + RayCount : 22 + RayCount)) return false;
            long controllerId, gridId, agentCoreId, cameraId, simulationTick, scanTick, sensorGridId, sensorCameraId;
            long discoveryTick = 0;
            int validRays, wheelCount, steeringWheels, propulsionWheels;
            bool sensorValid, actuatorReady;
            DateTime published, scanUtc;
            if (!Hex32(fields[0]) || !Hex32(fields[1]) || !Hex32(fields[2]) || !Hex64(fields[3]) ||
                !Hex32(fields[4]) || !Hex32(fields[5]) ||
                !long.TryParse(fields[6], NumberStyles.None, CultureInfo.InvariantCulture, out controllerId) || controllerId <= 0 ||
                !long.TryParse(fields[7], NumberStyles.None, CultureInfo.InvariantCulture, out gridId) || gridId <= 0 ||
                !long.TryParse(fields[8], NumberStyles.None, CultureInfo.InvariantCulture, out agentCoreId) || agentCoreId < 0 ||
                !long.TryParse(fields[9], NumberStyles.None, CultureInfo.InvariantCulture, out cameraId) || cameraId <= 0 ||
                !long.TryParse(fields[10], NumberStyles.None, CultureInfo.InvariantCulture, out simulationTick) || simulationTick < 0 ||
                !Utc(fields[11], out published) ||
                !(fields[12] == "0" || fields[12] == "1") ||
                !long.TryParse(fields[13], NumberStyles.None, CultureInfo.InvariantCulture, out scanTick) || scanTick < 0 ||
                !Utc(fields[14], out scanUtc) ||
                !long.TryParse(fields[15], NumberStyles.None, CultureInfo.InvariantCulture, out sensorGridId) || sensorGridId <= 0 ||
                !long.TryParse(fields[16], NumberStyles.None, CultureInfo.InvariantCulture, out sensorCameraId) || sensorCameraId <= 0 ||
                !int.TryParse(fields[17], NumberStyles.None, CultureInfo.InvariantCulture, out validRays) || validRays < 0 || validRays > RayCount ||
                !int.TryParse(fields[18], NumberStyles.None, CultureInfo.InvariantCulture, out wheelCount) || wheelCount < 0 || wheelCount > 256 ||
                !int.TryParse(fields[19], NumberStyles.None, CultureInfo.InvariantCulture, out steeringWheels) || steeringWheels < 0 || steeringWheels > wheelCount ||
                !int.TryParse(fields[20], NumberStyles.None, CultureInfo.InvariantCulture, out propulsionWheels) || propulsionWheels < 0 || propulsionWheels > wheelCount ||
                (fields[21] != "0" && fields[21] != "1") ||
                (discoveryBound && (!Hex32(fields[22]) ||
                 !long.TryParse(fields[23], NumberStyles.None, CultureInfo.InvariantCulture, out discoveryTick) || discoveryTick < 0 ||
                 !Hex64(fields[24]))) || (!discoveryBound && agentCoreId > 0))
                return false;
            if (capabilityBound && !VoidwrightOperationCapability.IsToken(fields[25])) return false;
            var clearances = new double[RayCount];
            for (var index = 0; index < RayCount; index++)
                if (!double.TryParse(fields[index + (capabilityBound ? 26 : discoveryBound ? 25 : 22)], NumberStyles.Float, CultureInfo.InvariantCulture, out clearances[index]) ||
                    !Finite(clearances[index]) || clearances[index] < 0 || clearances[index] > 1000) return false;
            sensorValid = fields[12] == "1";
            actuatorReady = fields[21] == "1";
            if (actuatorReady && (wheelCount == 0 || steeringWheels == 0 || propulsionWheels == 0)) return false;
            heartbeat = new VoidwrightPreflightHeartbeat
            {
                RunId = fields[0], FixtureId = fields[1], OperationId = fields[2], ContextSha256 = fields[3],
                Challenge = fields[4], SessionId = fields[5], ControllerId = controllerId, GridId = gridId,
                AgentCoreId = agentCoreId, CameraId = cameraId, SimulationTick = simulationTick, PublishedUtc = published,
                DiscoveryRunId = discoveryBound ? fields[22] : string.Empty, DiscoveryTick = discoveryTick,
                DiscoveryFingerprint = discoveryBound ? fields[24] : string.Empty,
                OperationCapabilityToken = capabilityBound ? fields[25] : string.Empty,
                SensorValid = sensorValid, SensorScanTick = scanTick, SensorScanUtc = scanUtc,
                SensorGridId = sensorGridId, SensorCameraId = sensorCameraId, ValidRayCount = validRays,
                WheelCount = wheelCount, SteeringWheelCount = steeringWheels, PropulsionWheelCount = propulsionWheels,
                ActuatorReady = actuatorReady,
                Clearances = clearances
            };
            return true;
        }

        public bool IsFreshAndBound(DateTime nowUtc, TimeSpan maximumAge, string runId,
            string fixtureId, string operationId, string contextSha256, string challenge, string sessionId,
            long controllerId, long gridId, long cameraId, long expectedAgentCoreId = -1,
            string expectedDiscoveryRunId = null, long expectedDiscoveryTick = -1,
            string expectedDiscoveryFingerprint = null)
        {
            var now = nowUtc.ToUniversalTime();
            var heartbeatAge = now - PublishedUtc.ToUniversalTime();
            var scanAge = now - SensorScanUtc.ToUniversalTime();
            if (maximumAge < TimeSpan.Zero || heartbeatAge < TimeSpan.Zero || heartbeatAge > maximumAge ||
                scanAge < TimeSpan.Zero || scanAge > maximumAge || !SensorValid || ValidRayCount != RayCount ||
                RunId != runId || FixtureId != fixtureId || OperationId != operationId ||
                ContextSha256 != contextSha256 || Challenge != challenge || SessionId != sessionId || ControllerId != controllerId ||
                GridId != gridId || CameraId != cameraId || SensorGridId != gridId || SensorCameraId != cameraId ||
                (expectedAgentCoreId >= 0 && AgentCoreId != expectedAgentCoreId) ||
                (!string.IsNullOrEmpty(expectedDiscoveryRunId) && (DiscoveryRunId != expectedDiscoveryRunId ||
                 DiscoveryTick != expectedDiscoveryTick || DiscoveryFingerprint != expectedDiscoveryFingerprint)) ||
                SensorScanTick > SimulationTick || SimulationTick - SensorScanTick > 120)
                return false;
            return true;
        }

        private static bool Utc(string value, out DateTime parsed)
        {
            return DateTime.TryParseExact(value, "O", CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out parsed) && parsed.Kind == DateTimeKind.Utc;
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static bool Hex32(string value) { return Hex(value, 32); }
        private static bool Hex64(string value) { return Hex(value, 64); }
        private static bool Hex(string value, int length)
        {
            if (value == null || value.Length != length) return false;
            foreach (var character in value)
                if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f'))) return false;
            return true;
        }
    }

    /// <summary>
    /// Independent, session-owned liveness record. Unlike the finite preflight
    /// heartbeat, this record continues after preflight reaches a terminal
    /// result so hosts can distinguish a stopped publisher from a stopped mod
    /// session callback.
    /// </summary>
    public sealed class VoidwrightSessionLiveness
    {
        public const string FileName = "voidwright-session-liveness-v1.txt";
        private const string Header = "VWLS1";
        private static readonly string[] AllowedStates =
            { "running", "complete", "timeout", "preflight-blocked", "error", "inactive" };

        public string SessionId;
        public long SimulationTick;
        public DateTime PublishedUtc;
        public string PreflightState;
        public string TerminalReason;

        public string Format()
        {
            if (!Hex32(SessionId) || SimulationTick < 0 || !Allowed(PreflightState) ||
                TerminalReason == null || TerminalReason.Length > 512)
                throw new ArgumentException("Invalid Voidwright session liveness record.");
            return Header + "\n" + SessionId + "|" +
                SimulationTick.ToString(CultureInfo.InvariantCulture) + "|" +
                PublishedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) + "|" +
                PreflightState + "|" + Convert.ToBase64String(Encoding.UTF8.GetBytes(TerminalReason)) + "\n";
        }

        public static bool TryParse(string text, out VoidwrightSessionLiveness liveness)
        {
            liveness = null;
            if (string.IsNullOrEmpty(text) || text.Length > 2048 ||
                !text.EndsWith("\n", StringComparison.Ordinal)) return false;
            var lines = text.Split('\n');
            if (lines.Length != 3 || lines[0] != Header || lines[2] != string.Empty) return false;
            var fields = lines[1].Split('|');
            long tick;
            DateTime published;
            if (fields.Length != 5 || !Hex32(fields[0]) ||
                !long.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out tick) || tick < 0 ||
                !DateTime.TryParseExact(fields[2], "O", CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out published) || published.Kind != DateTimeKind.Utc ||
                !Allowed(fields[3])) return false;
            try
            {
                var reason = Encoding.UTF8.GetString(Convert.FromBase64String(fields[4]));
                if (reason.Length > 512) return false;
                liveness = new VoidwrightSessionLiveness
                {
                    SessionId = fields[0], SimulationTick = tick, PublishedUtc = published,
                    PreflightState = fields[3], TerminalReason = reason
                };
                return true;
            }
            catch { return false; }
        }

        public bool IsFresh(DateTime nowUtc, TimeSpan maximumAge, string expectedSessionId = null)
        {
            var now = nowUtc.ToUniversalTime();
            var age = now - PublishedUtc.ToUniversalTime();
            return maximumAge >= TimeSpan.Zero && age >= TimeSpan.Zero && age <= maximumAge &&
                (string.IsNullOrEmpty(expectedSessionId) || SessionId == expectedSessionId);
        }

        private static bool Allowed(string state)
        {
            foreach (var allowed in AllowedStates)
                if (string.Equals(state, allowed, StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool Hex32(string value)
        {
            if (value == null || value.Length != 32) return false;
            foreach (var c in value)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            return true;
        }
    }

    /// <summary>
    /// Pure acceptance rule used by the in-game world-storage readback. The
    /// serialized bytes must match what this mod instance just wrote, then the
    /// parsed marker must be fresh and bound to the launcher-selected run and
    /// exact controller/camera identity.
    /// </summary>
    public static class VoidwrightPreflightHandshake
    {
        public static bool IsValidChallenge(string challenge)
        {
            if (challenge == null || challenge.Length != 32) return false;
            foreach (var character in challenge)
                if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f'))) return false;
            return true;
        }

        public static bool TryAccept(string expectedReadback, string observedReadback,
            DateTime nowUtc, TimeSpan maximumAge, string runId, string fixtureId,
            string operationId, string contextSha256, string challenge, string sessionId,
            long controllerId, long gridId, long cameraId,
            out VoidwrightPreflightHeartbeat heartbeat)
        {
            heartbeat = null;
            if (string.IsNullOrEmpty(expectedReadback) ||
                !string.Equals(expectedReadback, observedReadback, StringComparison.Ordinal) ||
                !VoidwrightPreflightHeartbeat.TryParse(observedReadback, out heartbeat) ||
                !heartbeat.IsFreshAndBound(nowUtc, maximumAge, runId, fixtureId, operationId,
                    contextSha256, challenge, sessionId, controllerId, gridId, cameraId))
            {
                heartbeat = null;
                return false;
            }
            return true;
        }
    }
}
