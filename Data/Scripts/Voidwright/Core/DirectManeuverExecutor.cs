using System;
using System.Collections.Generic;

namespace Voidwright.Core
{
    public enum DirectManeuverDispatchKind
    {
        None,
        WheelInputs,
        ControlledStop
    }

    /// <summary>Fresh exact-run, exact-grid observation with independent host and mod clocks.</summary>
    public sealed class DirectManeuverExecutionSample
    {
        public readonly string RunId, FixtureId, OperationId;
        public readonly long GridId, ControllerId, SensorGridId, HostSampleSequence, ModSimulationTick;
        public readonly DateTime CapturedUtc, SensorScanUtc;
        public readonly double PositionX, PositionY, PositionZ, HeadingDegrees, SpeedMetersPerSecond;
        public readonly double LongitudinalSpeedMetersPerSecond;
        public readonly double PropulsionPolarity;
        public readonly double MinimumPropulsionWheelPower;
        public readonly double ForwardX, ForwardY, ForwardZ;
        public readonly double? ObservedSteeringAngleDegrees;
        public readonly bool SensorValid, ObstacleClear, BrakeOwnershipKnown, ControlledStopGuaranteed, ExternalBrakeActive;
        public readonly double MinimumClearanceMeters;
        public readonly IList<DirectManeuverWheelState> ActuatorStates;
        public readonly DirectManeuverBrakeState ControllerBrakeState;

        public DirectManeuverExecutionSample(string runId, string fixtureId, string operationId,
            long gridId, long controllerId, long sensorGridId, long hostSampleSequence,
            DateTime capturedUtc, DateTime sensorScanUtc, double positionX, double positionY,
            double positionZ, double headingDegrees, double speedMetersPerSecond,
            double? observedSteeringAngleDegrees, bool sensorValid, bool obstacleClear,
            double minimumClearanceMeters, bool brakeOwnershipKnown,
            bool controlledStopGuaranteed, bool externalBrakeActive,
            IList<DirectManeuverWheelState> actuatorStates = null,
            DirectManeuverBrakeState controllerBrakeState = null,
            double forwardX = 0, double forwardY = 0, double forwardZ = 0,
            double? longitudinalSpeedMetersPerSecond = null, double propulsionPolarity = 1,
            double minimumPropulsionWheelPower = 1, long modSimulationTick = -1)
        {
            RunId = runId ?? string.Empty;
            FixtureId = fixtureId ?? string.Empty;
            OperationId = operationId ?? string.Empty;
            GridId = gridId;
            ControllerId = controllerId;
            SensorGridId = sensorGridId;
            HostSampleSequence = hostSampleSequence;
            ModSimulationTick = modSimulationTick;
            CapturedUtc = capturedUtc.ToUniversalTime();
            SensorScanUtc = sensorScanUtc.ToUniversalTime();
            PositionX = positionX;
            PositionY = positionY;
            PositionZ = positionZ;
            HeadingDegrees = headingDegrees;
            ForwardX = forwardX;
            ForwardY = forwardY;
            ForwardZ = forwardZ;
            SpeedMetersPerSecond = speedMetersPerSecond;
            LongitudinalSpeedMetersPerSecond = longitudinalSpeedMetersPerSecond ?? speedMetersPerSecond;
            PropulsionPolarity = propulsionPolarity;
            MinimumPropulsionWheelPower = minimumPropulsionWheelPower;
            ObservedSteeringAngleDegrees = observedSteeringAngleDegrees;
            SensorValid = sensorValid;
            ObstacleClear = obstacleClear;
            MinimumClearanceMeters = minimumClearanceMeters;
            BrakeOwnershipKnown = brakeOwnershipKnown;
            ControlledStopGuaranteed = controlledStopGuaranteed;
            ExternalBrakeActive = externalBrakeActive;
            ActuatorStates = actuatorStates == null ? null : new List<DirectManeuverWheelState>(actuatorStates);
            ControllerBrakeState = controllerBrakeState;
        }
    }

    public sealed class DirectManeuverWheelCommand
    {
        public readonly DirectManeuverDispatchKind Kind;
        public readonly string RunId, FixtureId, OperationId, CommandId;
        public readonly long GridId, ControllerId;
        public readonly int SegmentIndex;
        public readonly double Steering, Propulsion;

        internal DirectManeuverWheelCommand(DirectManeuverDispatchKind kind, DirectManeuverPlan plan,
            string commandId, int segmentIndex, double steering, double propulsion)
        {
            Kind = kind;
            RunId = plan.RunId;
            FixtureId = plan.FixtureId;
            OperationId = plan.OperationId;
            CommandId = commandId;
            GridId = plan.GridId;
            ControllerId = plan.ControllerId;
            SegmentIndex = segmentIndex;
            Steering = steering;
            Propulsion = propulsion;
        }
    }

    public sealed class DirectManeuverStep
    {
        public readonly DirectManeuverOutcome Outcome;
        public readonly DirectManeuverDispatchKind Dispatch;
        public readonly DirectManeuverWheelCommand Command;
        public readonly int SegmentIndex, StationarySampleCount;
        public readonly bool? LastCommandAccepted;
        public readonly bool MovementObserved, CompletionConfirmed;
        public readonly DirectManeuverStopAcceptance StopAcceptance;
        public readonly DirectManeuverTerminalReceipt TerminalReceipt;
        public readonly string Reason;

        internal DirectManeuverStep(DirectManeuverOutcome outcome, DirectManeuverDispatchKind dispatch,
            DirectManeuverWheelCommand command, int segmentIndex, int stationarySampleCount,
            bool? lastCommandAccepted, bool movementObserved, bool completionConfirmed, string reason,
            DirectManeuverStopAcceptance stopAcceptance = null, DirectManeuverTerminalReceipt terminalReceipt = null)
        {
            Outcome = outcome;
            Dispatch = dispatch;
            Command = command;
            SegmentIndex = segmentIndex;
            StationarySampleCount = stationarySampleCount;
            LastCommandAccepted = lastCommandAccepted;
            MovementObserved = movementObserved;
            CompletionConfirmed = completionConfirmed;
            StopAcceptance = stopAcceptance;
            TerminalReceipt = terminalReceipt;
            Reason = reason ?? string.Empty;
        }
    }

    /// <summary>
    /// Bounded planner-independent command policy. It never calls game APIs; the
    /// host must separately accept each command. The policy refuses to begin or
    /// continue unless a fresh controlled-stop guarantee and exact bindings exist.
    /// </summary>
    public sealed class DirectManeuverExecutor
    {
        public const double MaximumReversePropulsionInput = 0.05;
        private readonly DirectManeuverPlan plan;
        private readonly DirectManeuverOperationLease lease;
        private readonly DirectManeuverOperationIdentity identity;
        private DirectManeuverOutcome outcome = DirectManeuverOutcome.NotStarted;
        private int segmentIndex;
        private long segmentStartHostSampleSequence;
        private long operationStartHostSampleSequence;
        private int stationarySamples;
        private double stationaryWindowDisplacement;
        private double segmentStartX, segmentStartY, segmentStartZ, segmentStartHeading;
        private double operationStartX, operationStartY, operationStartZ;
        private double segmentStartForwardX, segmentStartForwardY, segmentStartForwardZ;
        private double previousX, previousY, previousZ;
        private bool hasPreviousPosition;
        private bool awaitingAcceptance;
        private bool? lastCommandAccepted;
        private DirectManeuverWheelCommand pendingCommand;
        private int nextSegmentAfterStop = -1;
        private DirectManeuverOutcome requestedTerminal = DirectManeuverOutcome.NotStarted;
        private string reason = "not started";
        private long lastObservedHostSampleSequence;
        private long lastObservedModSimulationTick = -1;
        private DateTime operationStartedUtc;
        private DateTime lastProcessedCaptureUtc = DateTime.MinValue;
        private DateTime lastStationaryCaptureUtc = DateTime.MinValue;
        private bool lastNeutralizationConfirmed;
        private int commandSequence;
        private DateTime lastNowUtc;
        private DateTime stopStartedUtc;
        private DateTime segmentStartedUtc;
        private long stopStartedHostSampleSequence;
        private string stopRequestId;
        private DirectManeuverStopAcceptance stopAcceptance;
        private DirectManeuverTerminalReceipt terminalReceipt;

        public DirectManeuverExecutor(DirectManeuverPlan plan, DirectManeuverOperationLease lease)
        {
            this.plan = plan ?? throw new ArgumentNullException(nameof(plan));
            this.lease = lease;
            identity = new DirectManeuverOperationIdentity(plan.RunId, plan.FixtureId,
                plan.OperationId, plan.GridId, plan.ControllerId, plan.AgentCoreId);
        }

        public DirectManeuverOutcome Outcome => outcome;
        public int CurrentSegmentIndex => segmentIndex;
        public int SegmentIndex => segmentIndex;
        public bool CompletionConfirmed => outcome == DirectManeuverOutcome.Complete;

        public DirectManeuverStep Start(DirectManeuverEvidence evidence,
            DirectManeuverExecutionSample sample, DateTime nowUtc)
        {
            if (outcome != DirectManeuverOutcome.NotStarted)
                return StepResult(DirectManeuverDispatchKind.None, null, false, "executor already started");
            string denial;
            if (!plan.CanDispatch(evidence, nowUtc, out denial))
            {
                outcome = DirectManeuverOutcome.PreflightBlocked;
                reason = denial;
                return StepResult(DirectManeuverDispatchKind.None, null, false, reason);
            }
            if (!SampleMatches(sample, nowUtc, out denial))
            {
                outcome = DirectManeuverOutcome.PreflightBlocked;
                reason = denial;
                return StepResult(DirectManeuverDispatchKind.None, null, false, reason);
            }
            if (RequiresPropulsion(plan.Segments[0]) && sample.MinimumPropulsionWheelPower <= 0)
            {
                outcome = DirectManeuverOutcome.PreflightBlocked;
                reason = "propulsion wheel power is zero; no wheel input was dispatched";
                return StepResult(DirectManeuverDispatchKind.None, null, false, reason);
            }
            if (lease == null || !lease.Identity.Matches(identity))
            {
                outcome = DirectManeuverOutcome.PreflightBlocked;
                reason = "fresh exact operation actuator lease is required";
                return StepResult(DirectManeuverDispatchKind.None, null, false, reason);
            }
            var leaseCheck = lease.Heartbeat(identity, sample.ActuatorStates, sample.ControllerBrakeState, nowUtc);
            if (leaseCheck != DirectManeuverLeaseCheck.Valid)
            {
                outcome = DirectManeuverOutcome.PreflightBlocked;
                reason = "operation lease acquisition/heartbeat failed: " + leaseCheck;
                return StepResult(DirectManeuverDispatchKind.None, null, false, reason);
            }
            outcome = DirectManeuverOutcome.Running;
            segmentIndex = 0;
            operationStartHostSampleSequence = sample.HostSampleSequence;
            segmentStartHostSampleSequence = sample.HostSampleSequence;
            lastObservedHostSampleSequence = sample.HostSampleSequence;
            lastObservedModSimulationTick = sample.ModSimulationTick;
            lastNowUtc = nowUtc.ToUniversalTime();
            operationStartedUtc = lastNowUtc;
            lastProcessedCaptureUtc = sample.CapturedUtc;
            lastNeutralizationConfirmed = AreChannelsNeutral(sample.ActuatorStates) && AreBrakesActive(sample.ControllerBrakeState);
            operationStartX = sample.PositionX;
            operationStartY = sample.PositionY;
            operationStartZ = sample.PositionZ;
            SetSegmentOrigin(sample);
            reason = "preflight passed; awaiting explicit wheel command acceptance";
            return PrepareStep(evidence, sample, nowUtc);
        }

        public bool AcknowledgeCommand(DirectManeuverCommandAcceptance acceptance)
        {
            if (!awaitingAcceptance || pendingCommand == null) return false;
            if (acceptance == null || acceptance.Identity == null || !acceptance.Identity.Matches(identity) ||
                acceptance.CommandId != pendingCommand.CommandId || acceptance.AcknowledgedUtc < lastNowUtc ||
                acceptance.AcknowledgedUtc - lastNowUtc > DirectManeuverOperationLease.MaximumDuration ||
                lease == null || !lease.IsDispatchAuthorized(identity, acceptance.AcknowledgedUtc)) return false;
            var commandKind = pendingCommand.Kind;
            var accepted = acceptance.Accepted && lease.HasAcceptedValuesForCommand(identity,
                pendingCommand.Steering, pendingCommand.Propulsion, acceptance.AcknowledgedUtc,
                commandKind == DirectManeuverDispatchKind.ControlledStop,
                acceptance.PropulsionByWheel);
            awaitingAcceptance = false;
            lastCommandAccepted = accepted;
            if (!accepted)
            {
                pendingCommand = null;
                if (commandKind == DirectManeuverDispatchKind.ControlledStop)
                {
                    outcome = DirectManeuverOutcome.Failed;
                    requestedTerminal = DirectManeuverOutcome.NotStarted;
                    reason = "controlled-stop command was rejected or not every owned wheel channel read back the stop value";
                    terminalReceipt = CreateTerminalReceipt(DirectManeuverOutcome.Failed, false);
                }
                else
                {
                    RequestTerminal(DirectManeuverOutcome.ActuatorConflict);
                    reason = "wheel command was rejected or partially applied; requesting an owned stop without inferring movement";
                }
                return true;
            }
            reason = "wheel command accepted; physical response remains unobserved";
            lastNowUtc = acceptance.AcknowledgedUtc;
            return true;
        }

        public DirectManeuverLeaseWriteResult RecordAcceptedChannelWrite(string commandId,
            long wheelEntityId, DirectManeuverActuatorChannel channel, double observedBefore,
            double requestedValue, double observedAfter, DateTime writtenUtc,
            IDictionary<long, double> propulsionByWheel = null)
        {
            if (!awaitingAcceptance || pendingCommand == null || commandId != pendingCommand.CommandId ||
                lease == null || !identity.Matches(new DirectManeuverOperationIdentity(
                    pendingCommand.RunId, pendingCommand.FixtureId, pendingCommand.OperationId,
                    pendingCommand.GridId, pendingCommand.ControllerId, plan.AgentCoreId)))
                return DirectManeuverLeaseWriteResult.IdentityMismatch;
            if (!IsCommandValueExpected(channel, wheelEntityId, requestedValue,
                pendingCommand.Steering, pendingCommand.Propulsion,
                pendingCommand.Kind == DirectManeuverDispatchKind.ControlledStop, propulsionByWheel))
                return DirectManeuverLeaseWriteResult.InvalidValue;
            return lease.RecordAcceptedChannelWrite(identity, wheelEntityId, channel,
                observedBefore, requestedValue, observedAfter, writtenUtc);
        }

        /// <summary>Resolves the exact command value expected for an owned wheel channel.</summary>
        public static bool TryResolveCommandValue(DirectManeuverActuatorChannel channel, long wheelEntityId,
            double steeringValue, double propulsionValue, bool controlledStop,
            IDictionary<long, double> propulsionByWheel, out double expected)
        {
            expected = 0;
            if (channel == DirectManeuverActuatorChannel.SteeringOverride)
                expected = steeringValue;
            else if (channel == DirectManeuverActuatorChannel.PropulsionOverride)
            {
                if (propulsionByWheel == null || !propulsionByWheel.TryGetValue(wheelEntityId, out expected))
                    return false;
            }
            else expected = controlledStop ? 1.0 : 0.0;
            return Finite(expected) && Math.Abs(expected) <= 1;
        }

        public static bool IsCommandValueExpected(DirectManeuverActuatorChannel channel, long wheelEntityId,
            double requestedValue, double steeringValue, double propulsionValue, bool controlledStop,
            IDictionary<long, double> propulsionByWheel)
        {
            double expected;
            return Finite(requestedValue) && TryResolveCommandValue(channel, wheelEntityId,
                steeringValue, propulsionValue, controlledStop, propulsionByWheel, out expected) &&
                Math.Abs(requestedValue - expected) <= DirectManeuverOperationLease.NeutralTolerance;
        }

        public static bool TryGetImmediateSafetyStopReason(bool authorityFresh,
            DirectManeuverKind activeKind, double speedMetersPerSecond,
            double segmentMaximumSpeedMetersPerSecond, double excursionMeters,
            double hardExcursionLimitMeters, out string reason)
        {
            if (!authorityFresh)
            { reason = "run-bound host or mod heartbeat became stale"; return true; }
            if (!Finite(speedMetersPerSecond) || !Finite(segmentMaximumSpeedMetersPerSecond) ||
                !Finite(excursionMeters) || !Finite(hardExcursionLimitMeters) ||
                segmentMaximumSpeedMetersPerSecond < 0 || hardExcursionLimitMeters <= 0)
            { reason = "live speed or excursion telemetry became invalid"; return true; }
            if (excursionMeters >= hardExcursionLimitMeters)
            { reason = "pre-boundary hard stop reached"; return true; }
            if ((activeKind == DirectManeuverKind.ForwardDistance ||
                 activeKind == DirectManeuverKind.ReverseDistance) &&
                Math.Abs(speedMetersPerSecond) > segmentMaximumSpeedMetersPerSecond)
            { reason = "measured speed exceeded the active segment cap"; return true; }
            reason = "live maneuver remains within immediate safety bounds";
            return false;
        }

        public static bool TryGetImmediateSafetyStopReason(bool authorityFresh,
            DateTime operationStartedUtc, DateTime nowUtc, DateTime capturedUtc,
            DirectManeuverKind activeKind, double speedMetersPerSecond,
            double segmentMaximumSpeedMetersPerSecond, double excursionMeters,
            double hardExcursionLimitMeters, out string reason)
        {
            nowUtc = nowUtc.ToUniversalTime();
            capturedUtc = capturedUtc.ToUniversalTime();
            if (!authorityFresh)
            { reason = "run-bound host or mod heartbeat became stale"; return true; }
            if (capturedUtc > nowUtc || nowUtc - capturedUtc > DirectManeuverPlan.EvidenceMaximumAge)
            { reason = "host motion sample became stale"; return true; }
            if (operationStartedUtc == DateTime.MinValue || nowUtc < operationStartedUtc.ToUniversalTime() ||
                nowUtc - operationStartedUtc.ToUniversalTime() >= DirectManeuverPlan.MaximumWallClockDuration)
            { reason = "45-second wall-clock maneuver deadline reached"; return true; }
            return TryGetImmediateSafetyStopReason(true, activeKind, speedMetersPerSecond,
                segmentMaximumSpeedMetersPerSecond, excursionMeters, hardExcursionLimitMeters, out reason);
        }

        public DirectManeuverStopAcceptance RequestCancellation(DirectManeuverStopRequest request, DateTime nowUtc)
        {
            return AcceptStopRequest(request, DirectManeuverStopKind.Cancellation, DirectManeuverOutcome.Cancelled, nowUtc);
        }

        public DirectManeuverStopAcceptance RequestEmergencyStop(DirectManeuverStopRequest request, DateTime nowUtc)
        {
            return AcceptStopRequest(request, DirectManeuverStopKind.EmergencyStop, DirectManeuverOutcome.EmergencyStopped, nowUtc);
        }

        public DirectManeuverStep Step(DirectManeuverEvidence evidence,
            DirectManeuverExecutionSample sample, DateTime nowUtc)
        {
            if (outcome != DirectManeuverOutcome.Running && outcome != DirectManeuverOutcome.StopRequested)
                return StepResult(DirectManeuverDispatchKind.None, null, false, reason);
            if (!IdentityMatches(sample))
            {
                outcome = DirectManeuverOutcome.Failed;
                reason = "run, fixture, operation, grid, controller, or sensor identity changed; dispatch blocked";
                return StepResult(DirectManeuverDispatchKind.None, null, false, reason);
            }
            lastNowUtc = nowUtc.ToUniversalTime();
            if (lease == null || lease.Heartbeat(identity, sample.ActuatorStates,
                    sample.ControllerBrakeState, nowUtc) != DirectManeuverLeaseCheck.Valid)
            {
                outcome = DirectManeuverOutcome.Failed;
                reason = "operation actuator lease expired, changed identity, or detected external channel/brake state";
                terminalReceipt = CreateTerminalReceipt(DirectManeuverOutcome.Failed, false);
                return StepResult(DirectManeuverDispatchKind.None, null, false, reason);
            }
            if (!SampleMatches(sample, nowUtc, out var captureDenial))
            {
                if (CanSafelyStop(sample)) RequestTerminal(DirectManeuverOutcome.SensorBlocked);
                else
                {
                    outcome = DirectManeuverOutcome.Failed;
                    reason = captureDenial;
                    return StepResult(DirectManeuverDispatchKind.None, null, false, reason);
                }
            }
            var activeKindForSafety = plan.Segments[segmentIndex].Kind;
            var safetyExcursion = Distance(sample.PositionX - operationStartX, sample.PositionY - operationStartY,
                sample.PositionZ - operationStartZ);
            if (outcome == DirectManeuverOutcome.Running &&
                TryGetImmediateSafetyStopReason(true, operationStartedUtc, nowUtc, sample.CapturedUtc,
                    activeKindForSafety, sample.SpeedMetersPerSecond,
                    plan.Segments[segmentIndex].MaximumSpeedMetersPerSecond, safetyExcursion, 7.5,
                    out var immediateReason))
                RequestTerminal(DirectManeuverOutcome.EmergencyStopped);
            if (sample.CapturedUtc <= lastProcessedCaptureUtc &&
                requestedTerminal == DirectManeuverOutcome.NotStarted &&
                nowUtc.ToUniversalTime() - operationStartedUtc < DirectManeuverPlan.MaximumWallClockDuration)
                return StepResult(DirectManeuverDispatchKind.None, null, false,
                    "waiting for a distinct fresh host motion sample");
            if (sample.CapturedUtc > lastProcessedCaptureUtc) lastProcessedCaptureUtc = sample.CapturedUtc;
            lastObservedHostSampleSequence = sample.HostSampleSequence;
            lastObservedModSimulationTick = sample.ModSimulationTick;
            lastNeutralizationConfirmed = AreChannelsNeutral(sample.ActuatorStates) && AreBrakesActive(sample.ControllerBrakeState);
            if (outcome == DirectManeuverOutcome.Running && requestedTerminal == DirectManeuverOutcome.NotStarted &&
                (nowUtc.ToUniversalTime() - operationStartedUtc >= DirectManeuverPlan.MaximumWallClockDuration ||
                 nowUtc.ToUniversalTime() - segmentStartedUtc >= TimeSpan.FromSeconds(
                     Math.Max(0.001, plan.Segments[segmentIndex].TimeoutTicks / 60.0))))
                RequestTerminal(DirectManeuverOutcome.TimedOut);
            if (awaitingAcceptance && requestedTerminal == DirectManeuverOutcome.NotStarted)
                return StepResult(DirectManeuverDispatchKind.None, null, false,
                    "waiting for explicit acceptance of the previous wheel command within the operation tick bound");

            var sampleValid = SampleMatches(sample, nowUtc, out var sampleDenial);
            var evidenceValid = plan.CanDispatch(evidence, nowUtc, out var evidenceDenial);
            if (!sampleValid || !evidenceValid)
            {
                if (!CanSafelyStop(sample))
                {
                    outcome = DirectManeuverOutcome.Failed;
                    reason = "fresh sensor/preflight evidence failed (" +
                        (sampleValid ? evidenceDenial : sampleDenial) + ") and no operation-owned controlled stop is proven";
                    return StepResult(DirectManeuverDispatchKind.None, null, false, reason);
                }
                reason = sampleValid ? evidenceDenial : sampleDenial;
                RequestTerminal(DirectManeuverOutcome.SensorBlocked);
            }

            var activeSegment = plan.Segments[segmentIndex];
            if (outcome == DirectManeuverOutcome.Running && requestedTerminal == DirectManeuverOutcome.NotStarted &&
                RequiresPropulsion(activeSegment) && sample.MinimumPropulsionWheelPower <= 0)
            {
                RequestTerminal(DirectManeuverOutcome.ActuatorConflict);
                return StopAndConfirm(sample, HasMovement(sample), nowUtc);
            }

            var movementObserved = HasMovement(sample);
            if (outcome == DirectManeuverOutcome.StopRequested || requestedTerminal != DirectManeuverOutcome.NotStarted)
                return StopAndConfirm(sample, movementObserved, nowUtc);

            var segment = plan.Segments[segmentIndex];
            if (segment.Kind != DirectManeuverKind.Stop && segment.Kind != DirectManeuverKind.StationaryHold &&
                Math.Abs(sample.SpeedMetersPerSecond) > segment.MaximumSpeedMetersPerSecond)
            {
                RequestTerminal(DirectManeuverOutcome.EmergencyStopped);
                return StopAndConfirm(sample, movementObserved, nowUtc);
            }
            if (segment.Kind == DirectManeuverKind.Stop || segment.Kind == DirectManeuverKind.StationaryHold)
            {
                if (pendingCommand == null)
                {
                    nextSegmentAfterStop = segmentIndex + 1;
                    QueueStop();
                }
                return StopAndConfirm(sample, movementObserved, nowUtc);
            }

            if (SegmentReached(segment, sample))
            {
                if (pendingCommand != null && pendingCommand.Kind == DirectManeuverDispatchKind.ControlledStop &&
                    lastCommandAccepted == true)
                    return StopAndConfirm(sample, movementObserved, nowUtc);
                nextSegmentAfterStop = segmentIndex + 1;
                QueueStop();
                stationarySamples = 0;
                return StepResult(DirectManeuverDispatchKind.ControlledStop, pendingCommand,
                    movementObserved, "segment target observed; waiting for three distinct stationary samples");
            }

            var command = BuildCommand(segment, sample);
            pendingCommand = command;
            awaitingAcceptance = true;
            lastCommandAccepted = null;
            return StepResult(DirectManeuverDispatchKind.WheelInputs, command, movementObserved,
                "command proposed; acceptance and physical movement are separate observations");
        }

        private DirectManeuverStep StopAndConfirm(DirectManeuverExecutionSample sample,
            bool movementObserved, DateTime nowUtc)
        {
            if (nowUtc.ToUniversalTime() - stopStartedUtc >= TimeSpan.FromSeconds(5) &&
                requestedTerminal != DirectManeuverOutcome.NotStarted)
            {
                outcome = DirectManeuverOutcome.Failed;
                reason = "run-bound stop request timed out without accepted controlled stop and fresh stationary confirmation";
                terminalReceipt = CreateTerminalReceipt(DirectManeuverOutcome.Failed, false);
                return StepResult(DirectManeuverDispatchKind.None, null, movementObserved, reason);
            }
            if (pendingCommand == null || pendingCommand.Kind != DirectManeuverDispatchKind.ControlledStop)
                QueueStop();
            if (awaitingAcceptance)
                return StepResult(DirectManeuverDispatchKind.ControlledStop, pendingCommand, movementObserved,
                    "waiting for explicit controlled-stop command acceptance");
            if (lastCommandAccepted != true)
                return StepResult(DirectManeuverDispatchKind.ControlledStop, pendingCommand,
                    movementObserved, "controlled-stop command acceptance is required");

            if (!HasFreshMotionTelemetry(sample, nowUtc))
                return StepResult(DirectManeuverDispatchKind.None, null, movementObserved,
                    "controlled stop accepted; waiting for fresh position and speed telemetry");

            if (sample.CapturedUtc > lastStationaryCaptureUtc)
            {
                lastStationaryCaptureUtc = sample.CapturedUtc;
                var delta = hasPreviousPosition ? Distance(sample.PositionX - previousX,
                    sample.PositionY - previousY, sample.PositionZ - previousZ) : double.MaxValue;
                if (Math.Abs(sample.SpeedMetersPerSecond) <= DirectManeuverLifecycle.StationaryConfirmationSpeedMetersPerSecond &&
                    delta <= DirectManeuverLifecycle.StationaryDisplacementMeters)
                {
                    stationaryWindowDisplacement += delta;
                    if (stationaryWindowDisplacement <= DirectManeuverLifecycle.StationaryConfirmationWindowDisplacementMeters)
                        stationarySamples++;
                    else
                    {
                        stationarySamples = 0;
                        stationaryWindowDisplacement = 0;
                    }
                }
                else
                {
                    stationarySamples = 0;
                    stationaryWindowDisplacement = 0;
                }
            }
            previousX = sample.PositionX;
            previousY = sample.PositionY;
            previousZ = sample.PositionZ;
            hasPreviousPosition = true;
            if (stationarySamples < DirectManeuverLifecycle.RequiredStationarySamples)
                return StepResult(DirectManeuverDispatchKind.None, null, movementObserved,
                    "controlled stop accepted; stationary confirmation pending");

            if (requestedTerminal != DirectManeuverOutcome.NotStarted)
            {
                outcome = requestedTerminal;
                reason = "three distinct fresh stationary samples confirmed the requested terminal stop";
                terminalReceipt = CreateTerminalReceipt(requestedTerminal, true);
                return StepResult(DirectManeuverDispatchKind.None, null, movementObserved, reason);
            }
            if (nextSegmentAfterStop >= plan.Segments.Count)
            {
                outcome = DirectManeuverOutcome.Complete;
                reason = "final stationary hold confirmed by three distinct samples";
                terminalReceipt = CreateTerminalReceipt(DirectManeuverOutcome.Complete, true);
                return StepResult(DirectManeuverDispatchKind.None, null, movementObserved, reason);
            }
            segmentIndex = nextSegmentAfterStop;
            nextSegmentAfterStop = -1;
            segmentStartHostSampleSequence = sample.HostSampleSequence;
            segmentStartedUtc = sample.CapturedUtc;
            SetSegmentOrigin(sample);
            stationarySamples = 0;
            stationaryWindowDisplacement = 0;
            pendingCommand = null;
            lastCommandAccepted = null;
            return PrepareCurrentSegment(sample, movementObserved);
        }

        private DirectManeuverStep PrepareStep(DirectManeuverEvidence evidence,
            DirectManeuverExecutionSample sample, DateTime nowUtc)
        {
            if (!SampleMatches(sample, nowUtc, out var denial) || !plan.CanDispatch(evidence, nowUtc, out denial))
            {
                outcome = DirectManeuverOutcome.PreflightBlocked;
                reason = denial;
                return StepResult(DirectManeuverDispatchKind.None, null, false, reason);
            }
            return PrepareCurrentSegment(sample, false);
        }

        private DirectManeuverStep PrepareCurrentSegment(DirectManeuverExecutionSample sample, bool movementObserved)
        {
            var segment = plan.Segments[segmentIndex];
            if (RequiresPropulsion(segment) && sample.MinimumPropulsionWheelPower <= 0)
            {
                outcome = DirectManeuverOutcome.PreflightBlocked;
                reason = "propulsion wheel power is zero; no wheel input was dispatched";
                return StepResult(DirectManeuverDispatchKind.None, null, movementObserved, reason);
            }
            if (segment.Kind == DirectManeuverKind.Stop || segment.Kind == DirectManeuverKind.StationaryHold)
            {
                nextSegmentAfterStop = segmentIndex + 1;
                QueueStop();
                return StepResult(DirectManeuverDispatchKind.ControlledStop, pendingCommand,
                    movementObserved, "controlled stop proposed; awaiting explicit adapter acceptance");
            }
            pendingCommand = BuildCommand(segment, sample);
            awaitingAcceptance = true;
            lastCommandAccepted = null;
            return StepResult(DirectManeuverDispatchKind.WheelInputs, pendingCommand,
                movementObserved, "wheel command proposed; awaiting explicit adapter acceptance");
        }

        private DirectManeuverWheelCommand BuildCommand(DirectManeuverSegment segment,
            DirectManeuverExecutionSample sample)
        {
            double steering = 0;
            double propulsion = 0;
            if (segment.Kind == DirectManeuverKind.ForwardDistance || segment.Kind == DirectManeuverKind.ReverseDistance)
            {
                var forward = segment.Kind == DirectManeuverKind.ReverseDistance ? -1.0 : 1.0;
                var commandedDirectionSpeed = forward * sample.LongitudinalSpeedMetersPerSecond;
                var propulsionMagnitude = PropulsionForSpeed(segment.MaximumSpeedMetersPerSecond,
                    commandedDirectionSpeed);
                if (segment.Kind == DirectManeuverKind.ReverseDistance)
                    propulsionMagnitude = Math.Min(MaximumReversePropulsionInput, propulsionMagnitude);
                propulsion = sample.PropulsionPolarity * forward * propulsionMagnitude;
            }
            else if (segment.Kind == DirectManeuverKind.SteerToHeading)
            {
                var error = NormalizeDegrees(segmentStartHeading + segment.Value - sample.HeadingDegrees);
                steering = Clamp(error / DirectManeuverPlan.MaximumHeadingChangeDegrees);
                propulsion = sample.PropulsionPolarity * PropulsionForSpeed(
                    segment.MaximumSpeedMetersPerSecond,
                    sample.LongitudinalSpeedMetersPerSecond);
            }
            else if (segment.Kind == DirectManeuverKind.SteerAngle)
            {
                steering = Clamp(segment.Value / DirectManeuverPlan.MaximumSteeringWheelAngleDegrees);
            }
            return new DirectManeuverWheelCommand(DirectManeuverDispatchKind.WheelInputs,
                plan, NextCommandId(), segmentIndex, steering, propulsion);
        }

        /// <summary>
        /// Tapers only speed in the commanded direction. Wrong-way motion keeps
        /// the bounded input needed to recover instead of tapering propulsion
        /// away as the vehicle drifts toward the cap.
        /// </summary>
        public static double PropulsionForSpeed(double maximumSpeedMetersPerSecond,
            double speedMetersPerSecond)
        {
            if (!Finite(maximumSpeedMetersPerSecond) || maximumSpeedMetersPerSecond <= 0 ||
                !Finite(speedMetersPerSecond)) return 0;
            var speed = Math.Max(0, speedMetersPerSecond);
            var taperStart = maximumSpeedMetersPerSecond * 0.4;
            if (speed <= taperStart) return 0.1;
            var fractionRemaining = 1.0 - (speed - taperStart) /
                (maximumSpeedMetersPerSecond - taperStart);
            return Math.Max(0, Math.Min(0.1, 0.1 * fractionRemaining));
        }

        private bool SegmentReached(DirectManeuverSegment segment, DirectManeuverExecutionSample sample)
        {
            if (segment.Kind == DirectManeuverKind.ForwardDistance || segment.Kind == DirectManeuverKind.ReverseDistance)
            {
                var dx = sample.PositionX - segmentStartX;
                var dy = sample.PositionY - segmentStartY;
                var dz = sample.PositionZ - segmentStartZ;
                var projected = segmentStartForwardX * dx + segmentStartForwardY * dy + segmentStartForwardZ * dz;
                if (segmentStartForwardX == 0 && segmentStartForwardY == 0 && segmentStartForwardZ == 0)
                {
                    var radians = segmentStartHeading * Math.PI / 180.0;
                    projected = dx * Math.Sin(radians) + dz * Math.Cos(radians);
                }
                if (segment.Kind == DirectManeuverKind.ReverseDistance) projected = -projected;
                return projected >= segment.Value;
            }
            if (segment.Kind == DirectManeuverKind.SteerToHeading)
                return Math.Abs(NormalizeDegrees(segmentStartHeading + segment.Value - sample.HeadingDegrees)) <= 2.0 &&
                       sample.ObservedSteeringAngleDegrees.HasValue;
            if (segment.Kind == DirectManeuverKind.SteerAngle)
                return sample.ObservedSteeringAngleDegrees.HasValue &&
                       Math.Abs(sample.ObservedSteeringAngleDegrees.Value - segment.Value) <= 2.0;
            return false;
        }

        private static bool RequiresPropulsion(DirectManeuverSegment segment)
        {
            return segment != null && (segment.Kind == DirectManeuverKind.ForwardDistance ||
                segment.Kind == DirectManeuverKind.ReverseDistance ||
                segment.Kind == DirectManeuverKind.SteerToHeading);
        }

        private void QueueStop()
        {
            if (pendingCommand == null || pendingCommand.Kind != DirectManeuverDispatchKind.ControlledStop)
            {
                pendingCommand = new DirectManeuverWheelCommand(DirectManeuverDispatchKind.ControlledStop,
                    plan, NextCommandId(), segmentIndex, 0, 0);
                lastCommandAccepted = null;
            }
            awaitingAcceptance = true;
        }

        private bool RequestTerminal(DirectManeuverOutcome terminalOutcome)
        {
            if (outcome != DirectManeuverOutcome.Running) return false;
            requestedTerminal = terminalOutcome;
            outcome = DirectManeuverOutcome.StopRequested;
            stopStartedHostSampleSequence = lastObservedHostSampleSequence;
            stopStartedUtc = lastNowUtc;
            stopRequestId = "auto-" + terminalOutcome.ToString().ToLowerInvariant() + "-" + stopStartedHostSampleSequence.ToString(System.Globalization.CultureInfo.InvariantCulture);
            pendingCommand = null;
            awaitingAcceptance = false;
            lastCommandAccepted = null;
            stationarySamples = 0;
            stationaryWindowDisplacement = 0;
            nextSegmentAfterStop = -1;
            reason = "terminal request accepted; controlled stop must be accepted and telemetry confirmed";
            return true;
        }

        private DirectManeuverStopAcceptance AcceptStopRequest(DirectManeuverStopRequest request,
            DirectManeuverStopKind expectedKind, DirectManeuverOutcome terminalOutcome, DateTime nowUtc)
        {
            nowUtc = nowUtc.ToUniversalTime();
            var validRequest = request != null && request.Identity != null && request.Identity.Matches(identity) &&
                request.Kind == expectedKind && !string.IsNullOrWhiteSpace(request.RequestId) &&
                request.RequestId.Length <= 128 && request.RequestedUtc <= nowUtc &&
                nowUtc - request.RequestedUtc <= DirectManeuverPlan.EvidenceMaximumAge &&
                lease != null && lease.IsDispatchAuthorized(identity, nowUtc);
            var stopAlreadyPending = outcome == DirectManeuverOutcome.StopRequested &&
                requestedTerminal != DirectManeuverOutcome.NotStarted;
            var accepted = validRequest && (stopAlreadyPending || RequestTerminal(terminalOutcome));
            stopAcceptance = new DirectManeuverStopAcceptance(identity,
                request == null ? string.Empty : request.RequestId, accepted,
                accepted ? stopAlreadyPending ? "run-bound stop request joined the existing terminal stop" :
                    "run-bound stop request accepted; awaiting controlled-stop acceptance and stationary telemetry" :
                    "stop request rejected because request identity, freshness, lease, or executor state did not match",
                nowUtc);
            if (accepted)
            {
                // Bind the broker's exact request ID to the already-running
                // terminal stop without resetting its original stop deadline.
                stopRequestId = request.RequestId;
                if (!stopAlreadyPending)
                {
                    stopStartedUtc = nowUtc;
                    stopStartedHostSampleSequence = lastObservedHostSampleSequence;
                    stopStartedUtc = nowUtc;
                    lastNowUtc = nowUtc;
                }
            }
            return stopAcceptance;
        }

        private DirectManeuverTerminalReceipt CreateTerminalReceipt(DirectManeuverOutcome terminalOutcome,
            bool stationaryConfirmed)
        {
            return new DirectManeuverTerminalReceipt(identity, stopRequestId ?? plan.OperationId,
                terminalOutcome, lastObservedHostSampleSequence, lastObservedModSimulationTick, stationarySamples,
                stationaryConfirmed, lastNeutralizationConfirmed, !stationaryConfirmed, lastNowUtc);
        }

        private static bool AreChannelsNeutral(IList<DirectManeuverWheelState> states)
        {
            if (states == null || states.Count == 0) return false;
            foreach (var state in states)
                if (state == null || Math.Abs(state.SteeringOverride) > DirectManeuverOperationLease.NeutralTolerance ||
                    Math.Abs(state.PropulsionOverride) > DirectManeuverOperationLease.NeutralTolerance) return false;
            return true;
        }

        private static bool AreBrakesActive(DirectManeuverBrakeState state)
        { return state != null && state.GridBrakeKnown && state.HandBrakeKnown && state.GridBrakeActive && state.HandBrakeActive; }

        private string NextCommandId()
        {
            commandSequence++;
            return plan.OperationId + "-cmd-" + commandSequence.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private bool SampleMatches(DirectManeuverExecutionSample sample, DateTime nowUtc, out string denial)
        {
            if (!IdentityMatches(sample))
            { denial = "runtime sample does not match exact run, fixture, operation, grid, controller, and sensor bindings"; return false; }
            nowUtc = nowUtc.ToUniversalTime();
            if (sample.CapturedUtc > nowUtc || nowUtc - sample.CapturedUtc > DirectManeuverPlan.EvidenceMaximumAge ||
                sample.SensorScanUtc > nowUtc || nowUtc - sample.SensorScanUtc > DirectManeuverPlan.EvidenceMaximumAge ||
                !sample.SensorValid || !sample.ObstacleClear || !Finite(sample.MinimumClearanceMeters) ||
                sample.MinimumClearanceMeters < DirectManeuverPlan.MinimumClearanceMeters)
            { denial = "runtime sample or same-grid obstacle scan is stale, invalid, or blocked"; return false; }
            if (!sample.BrakeOwnershipKnown || !sample.ControlledStopGuaranteed || sample.ExternalBrakeActive)
            { denial = "safe stop is unavailable, external brake state is active, or brake ownership is unknown"; return false; }
            if (sample.HostSampleSequence < 0 || !Finite(sample.PositionX) || !Finite(sample.PositionY) ||
                !Finite(sample.PositionZ) || !Finite(sample.HeadingDegrees) || !Finite(sample.SpeedMetersPerSecond) ||
                !Finite(sample.LongitudinalSpeedMetersPerSecond) ||
                !Finite(sample.MinimumPropulsionWheelPower) || sample.MinimumPropulsionWheelPower < 0 ||
                (sample.PropulsionPolarity != 1 && sample.PropulsionPolarity != -1) ||
                (sample.ObservedSteeringAngleDegrees.HasValue && !Finite(sample.ObservedSteeringAngleDegrees.Value)))
            { denial = "runtime physics or wheel telemetry is invalid"; return false; }
            denial = "fresh exact runtime sample accepted";
            return true;
        }

        private static bool HasFreshMotionTelemetry(DirectManeuverExecutionSample sample, DateTime nowUtc)
        {
            nowUtc = nowUtc.ToUniversalTime();
            return sample != null && sample.CapturedUtc <= nowUtc &&
                nowUtc - sample.CapturedUtc <= DirectManeuverPlan.EvidenceMaximumAge &&
                sample.HostSampleSequence >= 0 && Finite(sample.PositionX) && Finite(sample.PositionY) &&
                Finite(sample.PositionZ) && Finite(sample.SpeedMetersPerSecond);
        }

        private bool IdentityMatches(DirectManeuverExecutionSample sample)
        {
            return sample != null && sample.RunId == plan.RunId && sample.FixtureId == plan.FixtureId &&
                sample.OperationId == plan.OperationId && sample.GridId == plan.GridId &&
                sample.ControllerId == plan.ControllerId && sample.SensorGridId == plan.GridId;
        }

        private bool CanSafelyStop(DirectManeuverExecutionSample sample) => IdentityMatches(sample) &&
            sample.BrakeOwnershipKnown && sample.ControlledStopGuaranteed && !sample.ExternalBrakeActive;

        private void SetSegmentOrigin(DirectManeuverExecutionSample sample)
        {
            segmentStartX = sample.PositionX;
            segmentStartY = sample.PositionY;
            segmentStartZ = sample.PositionZ;
            segmentStartHeading = sample.HeadingDegrees;
            segmentStartForwardX = sample.ForwardX;
            segmentStartForwardY = sample.ForwardY;
            segmentStartForwardZ = sample.ForwardZ;
            var forwardLength = Math.Sqrt(segmentStartForwardX * segmentStartForwardX +
                segmentStartForwardY * segmentStartForwardY + segmentStartForwardZ * segmentStartForwardZ);
            if (forwardLength > 0.0001)
            {
                segmentStartForwardX /= forwardLength;
                segmentStartForwardY /= forwardLength;
                segmentStartForwardZ /= forwardLength;
            }
            previousX = sample.PositionX;
            previousY = sample.PositionY;
            previousZ = sample.PositionZ;
            hasPreviousPosition = true;
            lastStationaryCaptureUtc = DateTime.MinValue;
            segmentStartedUtc = sample.CapturedUtc;
            stationarySamples = 0;
            stationaryWindowDisplacement = 0;
        }

        private bool HasMovement(DirectManeuverExecutionSample sample)
        {
            if (!hasPreviousPosition) return Math.Abs(sample.SpeedMetersPerSecond) > DirectManeuverLifecycle.StationarySpeedMetersPerSecond;
            var moved = Distance(sample.PositionX - previousX, sample.PositionY - previousY,
                sample.PositionZ - previousZ) > DirectManeuverLifecycle.StationaryDisplacementMeters;
            return moved || Math.Abs(sample.SpeedMetersPerSecond) > DirectManeuverLifecycle.StationarySpeedMetersPerSecond;
        }

        private DirectManeuverStep StepResult(DirectManeuverDispatchKind dispatch,
            DirectManeuverWheelCommand command, bool movementObserved, string resultReason)
        {
            return new DirectManeuverStep(outcome, dispatch, command, segmentIndex,
                stationarySamples, lastCommandAccepted, movementObserved, CompletionConfirmed, resultReason,
                stopAcceptance, terminalReceipt);
        }

        private static double NormalizeDegrees(double value)
        {
            while (value > 180) value -= 360;
            while (value < -180) value += 360;
            return value;
        }

        private static double Clamp(double value) => value < -1 ? -1 : value > 1 ? 1 : value;
        private static double Distance(double x, double y, double z) => Math.Sqrt(x * x + y * y + z * z);
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

}
