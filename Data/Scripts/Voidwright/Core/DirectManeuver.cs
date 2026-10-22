using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Voidwright.Core
{
    public static class DirectManeuverControllerTakeoverPolicy
    {
        public static bool IsStable(bool controllerResolved, bool controllerInfoResolved,
            bool targetIsMainCockpit, bool selectedMainMatchesTarget, bool underControl,
            long controllingIdentityId, bool pilotPresent, bool otherControllerUnknownOrActive,
            bool controlTransitionObserved = false)
        {
            return controllerResolved && controllerInfoResolved && targetIsMainCockpit &&
                selectedMainMatchesTarget && !underControl && controllingIdentityId == 0 &&
                !pilotPresent && !otherControllerUnknownOrActive && !controlTransitionObserved;
        }
    }

    public enum DirectManeuverKind
    {
        ForwardDistance,
        ReverseDistance,
        SteerToHeading,
        SteerAngle,
        Stop,
        StationaryHold
    }

    public enum DirectManeuverOutcome
    {
        NotStarted,
        Running,
        StopRequested,
        Complete,
        PreflightBlocked,
        SensorBlocked,
        ActuatorConflict,
        Cancelled,
        EmergencyStopped,
        TimedOut,
        Failed
    }

    public enum DirectManeuverAcceptance
    {
        Rejected,
        Accepted
    }

    /// <summary>One bounded, planner-independent vehicle control segment.</summary>
    public sealed class DirectManeuverSegment
    {
        public readonly DirectManeuverKind Kind;
        public readonly double Value;
        public readonly double MaximumSpeedMetersPerSecond;
        public readonly int TimeoutTicks;

        public DirectManeuverSegment(DirectManeuverKind kind, double value,
            double maximumSpeedMetersPerSecond, int timeoutTicks)
        {
            Kind = kind;
            Value = value;
            MaximumSpeedMetersPerSecond = maximumSpeedMetersPerSecond;
            TimeoutTicks = timeoutTicks;
        }
    }

    /// <summary>
    /// Exact identity and fresh authority/sensor/actuator evidence required at
    /// the direct-maneuver dispatch boundary. Path-availability flags describe
    /// capabilities only; they do not attest exclusive ownership or a physical stop.
    /// This value contains no game API.
    /// </summary>
    public sealed class DirectManeuverEvidence
    {
        public readonly string RunId, FixtureId, OperationId;
        public readonly long AgentCoreId, GridId, ControllerId, SensorGridId;
        public readonly double MinimumObstacleClearanceMeters;
        public readonly DateTime HostPreflightUtc, ModPreflightUtc, SensorScanUtc;
        public readonly bool HostPreflightPass, ModPreflightPass, SensorValid;
        public readonly bool ObstaclePathClear, ParticipatingWriterPathAvailable;
        public readonly bool BrakeOwnershipKnown, BrakeControlAvailable;
        public readonly bool ControlledStopPathAvailable;

        public DirectManeuverEvidence(string runId, string fixtureId, string operationId,
            long gridId, long controllerId, long sensorGridId, double minimumObstacleClearanceMeters,
            DateTime hostPreflightUtc, bool hostPreflightPass,
            DateTime modPreflightUtc, bool modPreflightPass,
            DateTime sensorScanUtc, bool sensorValid, bool obstaclePathClear,
            bool participatingWriterPathAvailable, bool brakeOwnershipKnown, bool brakeControlAvailable,
            bool controlledStopPathAvailable = false, long agentCoreId = 0)
        {
            RunId = runId ?? string.Empty;
            FixtureId = fixtureId ?? string.Empty;
            OperationId = operationId ?? string.Empty;
            AgentCoreId = agentCoreId;
            GridId = gridId;
            ControllerId = controllerId;
            SensorGridId = sensorGridId;
            MinimumObstacleClearanceMeters = minimumObstacleClearanceMeters;
            HostPreflightUtc = hostPreflightUtc.ToUniversalTime();
            HostPreflightPass = hostPreflightPass;
            ModPreflightUtc = modPreflightUtc.ToUniversalTime();
            ModPreflightPass = modPreflightPass;
            SensorScanUtc = sensorScanUtc.ToUniversalTime();
            SensorValid = sensorValid;
            ObstaclePathClear = obstaclePathClear;
            ParticipatingWriterPathAvailable = participatingWriterPathAvailable;
            BrakeOwnershipKnown = brakeOwnershipKnown;
            BrakeControlAvailable = brakeControlAvailable;
            ControlledStopPathAvailable = controlledStopPathAvailable;
        }
    }

    public sealed class DirectManeuverPlan
    {
        public const int MaximumSegments = 12;
        public const int MaximumTotalTicks = 7200;
        public const int MaximumStopConfirmationTicks = 120;
        public static readonly TimeSpan MaximumWallClockDuration = TimeSpan.FromSeconds(45);
        public const double MaximumSteeringWheelAngleDegrees = 40.0;
        public const double MaximumHeadingChangeDegrees = 90.0;
        public const double MinimumClearanceMeters = 3.40;
        public static readonly TimeSpan EvidenceMaximumAge = TimeSpan.FromSeconds(1);

        public readonly string RunId, FixtureId, OperationId;
        public readonly long AgentCoreId, GridId, ControllerId;
        private readonly List<DirectManeuverSegment> segments;

        public DirectManeuverPlan(string runId, string fixtureId, string operationId,
            long gridId, long controllerId, IList<DirectManeuverSegment> segments,
            long agentCoreId = 0)
        {
            RunId = runId ?? string.Empty;
            FixtureId = fixtureId ?? string.Empty;
            OperationId = operationId ?? string.Empty;
            AgentCoreId = agentCoreId;
            GridId = gridId;
            ControllerId = controllerId;
            this.segments = segments == null ? new List<DirectManeuverSegment>() : new List<DirectManeuverSegment>(segments);
        }

        public IList<DirectManeuverSegment> Segments => segments.AsReadOnly();

        /// <summary>True only when every segment can produce zero wheel inputs.</summary>
        public bool IsZeroActuationOnly
        {
            get
            {
                if (segments.Count == 0) return false;
                foreach (var segment in segments)
                {
                    if (segment == null) return false;
                    if (segment.Kind == DirectManeuverKind.Stop ||
                        segment.Kind == DirectManeuverKind.StationaryHold) continue;
                    if (segment.Kind == DirectManeuverKind.SteerAngle &&
                        segment.Value == 0 && segment.MaximumSpeedMetersPerSecond == 0) continue;
                    return false;
                }
                return true;
            }
        }

        public bool AllowsActuatorValues(double steering, double propulsion)
        {
            if (!IsZeroActuationOnly) return true;
            return Finite(steering) && Finite(propulsion) && steering == 0 && propulsion == 0;
        }

        public bool IsValid(out string reason)
        {
            if (!IsHexIdentity(RunId) || !IsHexIdentity(FixtureId) || !IsHexIdentity(OperationId) ||
                AgentCoreId <= 0 || GridId <= 0 || ControllerId <= 0)
            { reason = "invalid exact run, fixture, operation, Agent Core, grid, or controller identity"; return false; }
            if (segments.Count == 0 || segments.Count > MaximumSegments)
            { reason = "maneuver segment count is outside the bounded range"; return false; }

            var totalTicks = 0;
            var forwardDistance = 0.0;
            var reverseDistance = 0.0;
            foreach (var segment in segments)
            {
                if (segment == null || segment.TimeoutTicks < 1)
                { reason = "segment is missing or has no runtime bound"; return false; }
                totalTicks += segment.TimeoutTicks;
                if (totalTicks > MaximumTotalTicks)
                { reason = "maneuver exceeds the existing total runtime limit"; return false; }
                if (!ValidateSegment(segment, out reason)) return false;
                if (segment.Kind == DirectManeuverKind.ForwardDistance) forwardDistance += segment.Value;
                if (segment.Kind == DirectManeuverKind.ReverseDistance) reverseDistance += segment.Value;
            }
            if (forwardDistance > 25 || reverseDistance > 3)
            { reason = "composed distance exceeds the existing forward or reverse operation limit"; return false; }
            if (segments[segments.Count - 1].Kind != DirectManeuverKind.StationaryHold)
            { reason = "maneuver must end with a telemetry-confirmed stationary hold"; return false; }
            reason = "bounded direct maneuver accepted for preflight evaluation";
            return true;
        }

        public string ToCompactSegments()
        {
            var result = new StringBuilder();
            foreach (var segment in segments)
            {
                if (result.Length > 0) result.Append(';');
                result.Append(KindCode(segment.Kind)).Append(',')
                    .Append(segment.Value.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(segment.MaximumSpeedMetersPerSecond.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(segment.TimeoutTicks.ToString(CultureInfo.InvariantCulture));
            }
            return result.ToString();
        }

        public static bool TryParseCompactSegments(string value, string runId, string fixtureId,
            string operationId, long gridId, long controllerId, long agentCoreId,
            out DirectManeuverPlan plan)
        {
            plan = null;
            if (string.IsNullOrEmpty(value) || value.Length > 4096) return false;
            var parts = value.Split(';');
            if (parts.Length == 0 || parts.Length > MaximumSegments) return false;
            var parsed = new List<DirectManeuverSegment>();
            foreach (var part in parts)
            {
                var fields = part.Split(',');
                double magnitude, speed;
                int ticks;
                DirectManeuverKind kind;
                if (fields.Length != 4 || !TryKind(fields[0], out kind) ||
                    !double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out magnitude) ||
                    !double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out speed) ||
                    !int.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out ticks)) return false;
                parsed.Add(new DirectManeuverSegment(kind, magnitude, speed, ticks));
            }
            var candidate = new DirectManeuverPlan(runId, fixtureId, operationId, gridId, controllerId, parsed, agentCoreId);
            string validationReason;
            if (!candidate.IsValid(out validationReason)) return false;
            plan = candidate;
            return true;
        }

        public bool CanDispatch(DirectManeuverEvidence evidence, DateTime nowUtc, out string reason)
        {
            if (!IsValid(out reason)) return false;
            if (evidence == null)
            { reason = "preflight evidence is unavailable"; return false; }
            nowUtc = nowUtc.ToUniversalTime();
            if (evidence.RunId != RunId || evidence.FixtureId != FixtureId || evidence.OperationId != OperationId ||
                AgentCoreId <= 0 || evidence.AgentCoreId != AgentCoreId ||
                evidence.GridId != GridId || evidence.ControllerId <= 0 || evidence.ControllerId != ControllerId ||
                evidence.SensorGridId != GridId)
            { reason = "preflight evidence does not match the exact run, fixture, grid, controller, and sensor binding"; return false; }
            if (!FreshPass(evidence.HostPreflightUtc, evidence.HostPreflightPass, nowUtc) ||
                !FreshPass(evidence.ModPreflightUtc, evidence.ModPreflightPass, nowUtc))
            { reason = "both host and mod preflight must be fresh and pass"; return false; }
            if (!FreshPass(evidence.SensorScanUtc, evidence.SensorValid, nowUtc) || !evidence.ObstaclePathClear ||
                !Finite(evidence.MinimumObstacleClearanceMeters) ||
                evidence.MinimumObstacleClearanceMeters < MinimumClearanceMeters)
            { reason = "same-grid obstacle sensing is stale, invalid, or blocked"; return false; }
            if (!evidence.ParticipatingWriterPathAvailable || !evidence.BrakeOwnershipKnown || !evidence.BrakeControlAvailable ||
                !evidence.ControlledStopPathAvailable)
            { reason = "participating-writer path, brake state, or controlled-stop path is unavailable"; return false; }
            reason = "all exact, fresh movement gates pass";
            return true;
        }

        private static bool ValidateSegment(DirectManeuverSegment segment, out string reason)
        {
            var value = segment.Value;
            var speed = segment.MaximumSpeedMetersPerSecond;
            if (!Finite(value) || !Finite(speed) || speed < 0)
            { reason = "segment contains a non-finite value"; return false; }
            switch (segment.Kind)
            {
                case DirectManeuverKind.ForwardDistance:
                    if (value < 0.25 || value > 25 || speed < 0.1 || speed > 0.8 || segment.TimeoutTicks > 3600)
                    { reason = "forward segment exceeds existing distance, speed, or runtime limits"; return false; }
                    break;
                case DirectManeuverKind.ReverseDistance:
                    if (value < 0.1 || value > 3 || speed < 0.1 || speed > 0.5 || segment.TimeoutTicks > 1800)
                    { reason = "reverse segment exceeds existing distance, speed, or runtime limits"; return false; }
                    break;
                case DirectManeuverKind.SteerToHeading:
                    if (Math.Abs(value) > MaximumHeadingChangeDegrees || speed < 0.1 || speed > 0.25 || segment.TimeoutTicks > 1800)
                    { reason = "heading correction exceeds the bounded steering envelope"; return false; }
                    break;
                case DirectManeuverKind.SteerAngle:
                    if (Math.Abs(value) > MaximumSteeringWheelAngleDegrees || speed != 0 || segment.TimeoutTicks > 1800)
                    { reason = "steering angle exceeds the bounded wheel-angle envelope"; return false; }
                    break;
                case DirectManeuverKind.Stop:
                case DirectManeuverKind.StationaryHold:
                    if (value != 0 || speed != 0 || segment.TimeoutTicks > 1800)
                    { reason = "stop and stationary-hold segments accept no movement magnitude or speed"; return false; }
                    break;
                default:
                    reason = "unsupported direct maneuver segment";
                    return false;
            }
            reason = "valid";
            return true;
        }

        private static bool FreshPass(DateTime timestamp, bool passed, DateTime nowUtc)
            => passed && timestamp <= nowUtc && nowUtc - timestamp <= EvidenceMaximumAge;

        private static bool IsHexIdentity(string value)
        {
            if (value == null || value.Length != 32) return false;
            foreach (var character in value)
                if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f'))) return false;
            return true;
        }

        private static string KindCode(DirectManeuverKind kind)
        {
            switch (kind)
            {
                case DirectManeuverKind.ForwardDistance: return "F";
                case DirectManeuverKind.ReverseDistance: return "R";
                case DirectManeuverKind.SteerToHeading: return "H";
                case DirectManeuverKind.SteerAngle: return "A";
                case DirectManeuverKind.Stop: return "S";
                case DirectManeuverKind.StationaryHold: return "W";
                default: return "?";
            }
        }

        private static bool TryKind(string code, out DirectManeuverKind kind)
        {
            switch (code)
            {
                case "F": kind = DirectManeuverKind.ForwardDistance; return true;
                case "R": kind = DirectManeuverKind.ReverseDistance; return true;
                case "H": kind = DirectManeuverKind.SteerToHeading; return true;
                case "A": kind = DirectManeuverKind.SteerAngle; return true;
                case "S": kind = DirectManeuverKind.Stop; return true;
                case "W": kind = DirectManeuverKind.StationaryHold; return true;
                default: kind = DirectManeuverKind.Stop; return false;
            }
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    /// <summary>
    /// Separate commanded and observed telemetry. This record does not infer
    /// acceptance or completion from a command, wheel input, or heading alone.
    /// </summary>
    public sealed class DirectManeuverTelemetry
    {
        public readonly double CommandedSteeringSign, ActualSteeringSign, WheelResponse;
        public readonly double CommandedHeadingDegrees, ActualHeadingDegrees, DisplacementMeters, SpeedMetersPerSecond;
        public readonly bool SensorFresh, ObstacleClear;
        public readonly DirectManeuverOutcome Outcome;
        public readonly string OutcomeEvidence;

        public DirectManeuverTelemetry(double commandedSteeringSign, double actualSteeringSign,
            double wheelResponse, double commandedHeadingDegrees, double actualHeadingDegrees,
            double displacementMeters, double speedMetersPerSecond, bool sensorFresh,
            bool obstacleClear, DirectManeuverOutcome outcome, string outcomeEvidence)
        {
            CommandedSteeringSign = Sign(commandedSteeringSign);
            ActualSteeringSign = Sign(actualSteeringSign);
            WheelResponse = wheelResponse;
            CommandedHeadingDegrees = commandedHeadingDegrees;
            ActualHeadingDegrees = actualHeadingDegrees;
            DisplacementMeters = displacementMeters;
            SpeedMetersPerSecond = speedMetersPerSecond;
            SensorFresh = sensorFresh;
            ObstacleClear = obstacleClear;
            Outcome = outcome;
            OutcomeEvidence = outcomeEvidence ?? string.Empty;
        }

        public bool HasSteeringResponse => ActualSteeringSign == CommandedSteeringSign &&
            CommandedSteeringSign != 0 && Finite(WheelResponse) && Math.Abs(WheelResponse) > 0.0001;

        private static double Sign(double value) => !Finite(value) || Math.Abs(value) <= 0.0001 ? 0 : Math.Sign(value);
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    /// <summary>
    /// Tracks acceptance separately from execution and requires three distinct
    /// stationary telemetry samples before recording stop, cancellation, or
    /// maneuver completion as terminal.
    /// </summary>
    public sealed class DirectManeuverLifecycle
    {
        public const double StationarySpeedMetersPerSecond = 0.05;
        // Physics velocity can hover a few thousandths above the nominal rest
        // bound while position telemetry remains stable. Keep the raw speed
        // visible, allow only a narrow confirmation band, and bound total
        // position travel across the full confirmation window.
        public const double StationaryConfirmationSpeedMetersPerSecond = 0.06;
        public const double StationaryDisplacementMeters = 0.02;
        public const double StationaryConfirmationWindowDisplacementMeters = 0.02;
        public const int RequiredStationarySamples = 3;

        private long lastTick = long.MinValue;
        private int stationarySamples;
        private double stationaryWindowDisplacementMeters;
        private DirectManeuverOutcome requestedTerminal = DirectManeuverOutcome.NotStarted;

        public readonly DirectManeuverAcceptance Acceptance;
        public DirectManeuverOutcome Outcome { get; private set; }
        public bool StopDispatchRequired { get; private set; }
        public int StationarySampleCount => stationarySamples;

        public DirectManeuverLifecycle(bool accepted)
        {
            Acceptance = accepted ? DirectManeuverAcceptance.Accepted : DirectManeuverAcceptance.Rejected;
            Outcome = accepted ? DirectManeuverOutcome.NotStarted : DirectManeuverOutcome.PreflightBlocked;
        }

        public bool Start()
        {
            if (Acceptance != DirectManeuverAcceptance.Accepted || Outcome != DirectManeuverOutcome.NotStarted)
                return false;
            Outcome = DirectManeuverOutcome.Running;
            return true;
        }

        public bool RequestCancellation() => RequestStop(DirectManeuverOutcome.Cancelled);
        public bool RequestEmergencyStop() => RequestStop(DirectManeuverOutcome.EmergencyStopped);
        public bool RequestCompletionAfterHold() => RequestStop(DirectManeuverOutcome.Complete);

        public bool ObserveStationaryTelemetry(long simulationTick, double speedMetersPerSecond,
            double displacementSincePreviousSampleMeters)
        {
            if (!StopDispatchRequired || Outcome != DirectManeuverOutcome.Running &&
                Outcome != DirectManeuverOutcome.StopRequested) return false;
            if (simulationTick <= lastTick) return false;
            lastTick = simulationTick;
            if (!Finite(speedMetersPerSecond) || !Finite(displacementSincePreviousSampleMeters) ||
                Math.Abs(speedMetersPerSecond) > StationaryConfirmationSpeedMetersPerSecond ||
                Math.Abs(displacementSincePreviousSampleMeters) > StationaryDisplacementMeters)
            {
                stationarySamples = 0;
                stationaryWindowDisplacementMeters = 0;
                return false;
            }
            stationaryWindowDisplacementMeters += Math.Abs(displacementSincePreviousSampleMeters);
            if (stationaryWindowDisplacementMeters > StationaryConfirmationWindowDisplacementMeters)
            {
                stationarySamples = 0;
                stationaryWindowDisplacementMeters = 0;
                return false;
            }
            stationarySamples++;
            if (stationarySamples < RequiredStationarySamples) return false;
            Outcome = requestedTerminal;
            StopDispatchRequired = false;
            return true;
        }

        private bool RequestStop(DirectManeuverOutcome terminalOutcome)
        {
            if (Acceptance != DirectManeuverAcceptance.Accepted ||
                (Outcome != DirectManeuverOutcome.Running && Outcome != DirectManeuverOutcome.NotStarted))
                return false;
            requestedTerminal = terminalOutcome;
            Outcome = DirectManeuverOutcome.StopRequested;
            StopDispatchRequired = true;
            stationarySamples = 0;
            lastTick = long.MinValue;
            return true;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    /// <summary>
    /// Requires a fresh, stable stationary window before the actuator lease is
    /// acquired. The initial gate uses the strict rest-speed bound, with no
    /// confirmation-speed grace band.
    /// </summary>
    public sealed class DirectManeuverStationaryGate
    {
        private long lastTick = long.MinValue;
        private int stationarySamples;
        private double accumulatedDisplacement;
        private double previousX, previousY, previousZ;
        private bool hasPreviousPosition;

        public int StationarySampleCount => stationarySamples;

        public bool Observe(long simulationTick, double speedMetersPerSecond,
            double positionX, double positionY, double positionZ)
        {
            if (simulationTick <= lastTick) return stationarySamples >= DirectManeuverLifecycle.RequiredStationarySamples;
            lastTick = simulationTick;
            var displacement = hasPreviousPosition
                ? Distance(positionX - previousX, positionY - previousY, positionZ - previousZ) : 0;
            previousX = positionX;
            previousY = positionY;
            previousZ = positionZ;
            hasPreviousPosition = true;

            if (!Finite(speedMetersPerSecond) || !Finite(displacement) ||
                !Finite(positionX) || !Finite(positionY) || !Finite(positionZ) ||
                Math.Abs(speedMetersPerSecond) > DirectManeuverLifecycle.StationarySpeedMetersPerSecond ||
                displacement > DirectManeuverLifecycle.StationaryDisplacementMeters)
            {
                stationarySamples = 0;
                accumulatedDisplacement = 0;
                return false;
            }

            accumulatedDisplacement += displacement;
            if (accumulatedDisplacement > DirectManeuverLifecycle.StationaryConfirmationWindowDisplacementMeters)
            {
                stationarySamples = 0;
                accumulatedDisplacement = 0;
                return false;
            }
            stationarySamples++;
            return stationarySamples >= DirectManeuverLifecycle.RequiredStationarySamples;
        }

        private static double Distance(double x, double y, double z) => Math.Sqrt(x * x + y * y + z * z);
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
