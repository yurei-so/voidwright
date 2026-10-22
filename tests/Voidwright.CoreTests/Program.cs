using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Voidwright.Core;
using Voidwright.ServerPlugin;

static class Program
{
    private static int passed;
    private static int total;
    private const long TestAgentCoreId = 987654321;

    private static DirectManeuverPlan Plan(string runId, string fixtureId, string operationId,
        long gridId, long controllerId, IList<DirectManeuverSegment> segments)
        => new DirectManeuverPlan(runId, fixtureId, operationId, gridId, controllerId, segments, TestAgentCoreId);

    private static DirectManeuverEvidence Evidence(string runId, string fixtureId, string operationId,
        long gridId, long controllerId, long sensorGridId, double clearance,
        DateTime hostUtc, bool hostPass, DateTime modUtc, bool modPass,
        DateTime scanUtc, bool sensorValid, bool obstacleClear, bool ownsActuators,
        bool brakeKnown, bool brakeAvailable, bool controlledStop = false)
        => new DirectManeuverEvidence(runId, fixtureId, operationId, gridId, controllerId,
            sensorGridId, clearance, hostUtc, hostPass, modUtc, modPass, scanUtc, sensorValid,
            obstacleClear, ownsActuators, brakeKnown, brakeAvailable, controlledStop, TestAgentCoreId);

    private static DirectManeuverOperationIdentity OperationIdentity(string runId, string fixtureId,
        string operationId, long gridId, long controllerId)
        => new DirectManeuverOperationIdentity(runId, fixtureId, operationId, gridId, controllerId, TestAgentCoreId);

    static void Main()
    {
        Run("land routes around vehicle-sized obstruction", LandRoutesAroundObstacle);
        Run("land rejects excessive step", LandRejectsStep);
        Run("air routes over wall in 3D", AirRoutesOverWall);
        Run("air respects vehicle clearance", AirRespectsClearance);
        Run("planner reports budget exhaustion", BudgetIsBounded);
        Run("planner preserves approach heading", PlannerPreservesApproachHeading);
        Run("invalid input fails closed", InvalidInputFailsClosed);
        Run("mobility classification is conservative", MobilityClassificationIsConservative);
        Run("land guidance points toward target", LandGuidancePointsTowardTarget);
        Run("land guidance turns before accelerating", LandGuidanceTurnsBeforeAccelerating);
        Run("air guidance preserves three-dimensional intent", AirGuidanceIsThreeDimensional);
        Run("land arrival ignores unreachable vertical offset", LandArrivalIgnoresVerticalOffset);
        Run("land approach ignores unreachable vertical offset", LandApproachIgnoresVerticalOffset);
        Run("air arrival remains three-dimensional", AirArrivalRemainsThreeDimensional);
        Run("guidance rejects non-finite input", GuidanceRejectsNonFiniteInput);
        Run("guidance fuzz remains bounded and deterministic", GuidanceFuzzIsBoundedAndDeterministic);
        Run("depth steering preserves clear target heading", DepthSteeringPreservesClearTarget);
        Run("depth steering retains a safe detour through intermittent clear scans", DepthSteeringRetainsDetourThroughIntermittentClearScans);
        Run("startup settling trusts fresh measured displacement with a reported-speed bound", DepthStartupSettlingUsesFreshPositionTelemetry);
        Run("depth steering chooses a clear corridor", DepthSteeringAvoidsBlockedCenter);
        Run("depth navigation pins the exact resolved controller and same-grid camera", DepthGridBindingPinsExactGrid);
        Run("cockpit ship controller is accepted only on the exact activated grid and camera", CockpitControllerBindingIsExact);
        Run("preflight-only routes to host review without trusting motion-owner environment", PreflightOnlyRoutingDoesNotDependOnOwnerEnvironment);
        Run("depth steering commits to the corridor with more clearance", DepthSteeringPrefersClearerDetour);
        Run("depth steering picks the observed side away from a blocked target", DepthSteeringPicksOpenOppositeSide);
        Run("depth steering stops when all corridors are blocked", DepthSteeringStopsWhenBlocked);
        Run("depth steering stops when target leaves camera fan", DepthSteeringStopsOutOfView);
        Run("depth steering fails closed on malformed scan", DepthSteeringRejectsInvalidScan);
        Run("depth propulsion preserves saved power within a safe cap", DepthPowerIsBounded);
        Run("depth drive duty stays inside its bounded pulse envelope", DepthDriveDutyIsBounded);
        Run("depth steering uses available low-speed wheel angle", DepthSteeringUsesAvailableWheelAngle);
        Run("depth arrival shrinks planar radius for vertical offset", DepthArrivalShrinksPlanarRadius);
        Run("depth arrival requires stable neutral live evidence", DepthArrivalRequiresStableNeutralEvidence);
        Run("depth arrival settles on distinct host samples after the mod terminal heartbeat", DepthArrivalCountsDistinctHostSamples);
        Run("depth arrival rejects a point outside the world-space radius", DepthArrivalRequiresWorldRadius);
        Run("depth arrival rejects impossible or invalid vertical offsets", DepthArrivalFailsClosed);
        Run("agent wireframe includes top, side, and front profiles with selected colors", WireframeProvidesThreeProfilesAndColors);
        Run("agent wireframe escapes metadata and rejects unsafe selectors", WireframeValidatesAndEscapesInput);
        Run("agent wireframe caps block geometry and reports truncation", WireframeCapsGeometry);
        Run("movement preflight authorizes only fresh all-pass evidence bound to one run", MovementPreflightRequiresAllPasses);
        Run("preflight-only retries unknown startup evidence and completes only after host approval", PreflightOnlyWaitsForFinalEvidence);
        Run("preflight-only terminal completion is reported as success", PreflightOnlyTerminalStatusIsComplete);
        Run("preflight safety remains unknown until controller and speed are observable", PreflightSafetyWaitsForControllerEvidence);
        Run("preflight entity retry uses production cadence and waits on unknown startup evidence", PreflightStartupRetryUsesProductionCadence);
        Run("simulated shared storage heartbeat and exact approval use production codecs", PreflightHandshakeRoundTripsSimulatedWorldStorage);
        Run("actuator and brake readiness distinguish unknown from externally active state", ActuatorAndBrakeReadinessClassifyOwnership);
        Run("operation deadline terminates while actuator dispatch remains gated", MovementOperationDeadlineIsBounded);
        Run("timeout remains the terminal receipt outcome when stopping reports an error", TimeoutReceiptStatusSurvivesStopError);
        Run("movement preflight fails closed for missing, unknown, failed, or stale evidence", MovementPreflightFailsClosed);
        Run("movement preflight invalidates on run, fixture, and state changes", MovementPreflightInvalidatesChangedContext);
        Run("failed movement preflight does not dispatch a movement command", FailedPreflightDoesNotDispatch);
        Run("movement callback exceptions stop owned actuators and fail closed", MovementCallbackExceptionStopsAndFailsClosed);
        Run("wheel stop preserves externally changed actuators", WheelStopPreservesExternalChanges);
        Run("missing ordinary actuator availability prevents all dispatch", ActuatorFailureDoesNotDispatch);
        Run("movement preflight emits machine-readable per-check evidence", MovementPreflightEmitsEvidence);
        Run("host preflight snapshots remain immutable while latest view advances", MovementPreflightSnapshotsAreImmutable);
        Run("simulation evidence cadence handles first write and tick boundaries", SimulationEvidenceCadenceHandlesBoundaries);
        Run("VRageCage context binds run fixture command and immutable source hashes", PreflightContextBindsRunAndSources);
        Run("Voidwright heartbeat rejects stale and cross-grid sensor evidence", PreflightHeartbeatBindsFreshSensor);
        Run("mod session liveness advances after finite preflight denial", SessionLivenessAdvancesAfterTerminalPreflight);
        Run("world-storage activation is bounded and run-command bound", ModActivationIsBoundedAndStrict);
        Run("host approval acknowledges only the exact live mod heartbeat", HostApprovalBindsHeartbeat);
        Run("movement sensor gate binds fresh scans to the current grid and pinned camera", SensorPreflightBindsCurrentGridAndCamera);
        Run("direct maneuvers validate bounded planner-independent segments and fresh exact gates", DirectManeuversAreBoundedAndFailClosed);
        Run("host accepts only exact zero-only stationary hold plans with fresh matching discovery", DirectManeuverCommandAcceptanceIsNarrow);
        Run("direct maneuver telemetry keeps command response and terminal outcome separate", DirectManeuverTelemetrySeparatesCommandAndResponse);
        Run("wheel propulsion polarity follows controller/grid alignment and inversion", WheelPropulsionPolarityUsesMountedFrame);
        Run("direct steering override compensates installed suspension inversion", WheelSteeringOverrideCompensatesInvertSteer);
        Run("direct wheel overrides preserve native per-wheel propulsion direction", DirectWheelOverrideSignsMatchNativeDrive);
        Run("direct command acceptance matches all mapped wheel values and rejects conflicts", DirectWheelCommandAcceptanceMatchesMappedValues);
        Run("direct maneuver enforces speed, authority, and excursion stops on every host sample", DirectManeuverImmediateSafetyStop);
        Run("C8 speed denial retains its failed safety check and measured bound", C8PreflightDenialRetainsReason);
        Run("direct maneuver host sample sequence advances independently of frozen mod tick", DirectManeuverSeparatesHostAndModTicks);
        Run("direct maneuver cancellation and completion wait for distinct stationary samples", DirectManeuverLifecycleRequiresStationaryHold);
        Run("direct maneuver lease waits for three fresh samples inside strict startup rest bounds", DirectManeuverStartupStationaryGateIsStrict);
        Run("direct maneuver executor bounds segments, stop confirmation, cancellation, and owned cleanup", DirectManeuverExecutorIsBoundedAndFailClosed);
        Run("direct maneuver throttle tapers near speed cap and stop confirmation rejects cumulative drift", DirectManeuverFeedbackAndStationaryWindowAreBounded);
        Run("operation lease rejects stale, competing, wrong-vehicle/controller, takeover, partial, and external writes", DirectManeuverOperationLeaseIsFailClosed);
        Run("offline broker rejects unknown writers and generic fallback and binds stop failure", DirectManeuverBrokerContractIsFailClosed);
        Run("direct maneuver broker requires audited cooperative writers and operation-scoped stop", DirectManeuverBrokerRequiresCompleteCapabilities);
        Run("cached LAND actuator session rejects grid, controller, operation, inventory, and expiry changes", DirectActuatorSessionBindingIsExactAndExpiring);
        Run("stop-only capability survives movement expiry and funnels emergency writes", DirectManeuverStopCapabilityFencesExpiredMovement);
        Run("run-bound world-storage capability grants exact direct operation token and revokes on stop", DirectManeuverCapabilityArtifactRoundTripsExactLease);
        Run("direct maneuver controller lease fails closed on takeover or unknown main cockpit", DirectManeuverControllerTakeoverIsFailClosed);
        Run("local test control binds explicit authorization, fresh exact-grid wheels, bounded commands, and stop paths", LocalVehicleTestControlIsFailClosed);
        Run("local test adaptive obstacle scan stops on hits and fails closed on incomplete evidence", LocalVehicleTestObstacleStopIsFailClosed);
        Run("local test baseline requires exact q identity, wheel subgrids, camera clearance, physics, and stable samples", LocalVehicleTestBaselineIsFailClosed);
        Run("local test wire codec stays compatible with the game's C# 6 mod compiler", LocalVehicleTestWireUsesCSharp6Syntax);
        Run("local steering proof carries forward only after exact live neutral stop-ready checks", LocalVehicleTestDirectionProofCarryIsFailClosed);
        Run("local test displacement baseline is run-scoped and fail-closed", LocalVehicleTestDisplacementIsFailClosed);
        Run("stage-two handshake rejects run replay, session mismatch, duplicate wheels, and expiry", DiscoveryHandshakeWireRejectsReplayAndSessionMismatch);
        Run("direct maneuver Agent Core binding rejects missing, wrong, moved, stale, or incomplete discovery", DirectManeuverAgentCoreBindingIsFailClosed);
        Run("Agent Core discovery rejects prior-session export replay", AgentCoreDiscoveryRejectsPriorSessionReplay);
        Run("preflight-only activation binds exact grid and cannot carry maneuver segments", PreflightOnlyActivationBindsExactGrid);
        Run("block lifecycle diagnostic enforces schema, run binding, target, and observed identities", BlockLifecycleDiagnosticBindingIsFailClosed);
        Console.WriteLine($"Voidwright core: {passed}/{total} checks passed.");
    }

    private static void AgentCoreDiscoveryRejectsPriorSessionReplay()
    {
        var currentSession = new string('a', 32);
        var previousSession = new string('b', 32);
        Check(AgentCoreDiscoverySessionPolicy.IsCurrentSession(currentSession, currentSession),
            "current-session discovery export was rejected");
        Check(!AgentCoreDiscoverySessionPolicy.IsCurrentSession(previousSession, currentSession),
            "fresh-looking prior-session discovery export was accepted");
        Check(!AgentCoreDiscoverySessionPolicy.IsCurrentSession("not-a-run-id", currentSession),
            "malformed discovery run ID was accepted");
    }

    private static void WheelPropulsionPolarityUsesMountedFrame()
    {
        Check(WheelPropulsionMapping.ResolvePolarity(-1, false) == -1 &&
              WheelPropulsionMapping.ResolvePolarity(1, false) == 1 &&
              WheelPropulsionMapping.ResolvePolarity(-1, true) == 1 &&
              WheelPropulsionMapping.ResolvePolarity(1, true) == -1,
            "controller/grid direction and wheel inversion combine into the propulsion sign");
        Check(WheelPropulsionMapping.ResolvePolarity(0, false) == 0 &&
              WheelPropulsionMapping.ResolvePolarity(double.NaN, false) == 0,
            "ambiguous or invalid frame alignment fails closed");
    }

    private static void WheelSteeringOverrideCompensatesInvertSteer()
    {
        Check(WheelSteeringMapping.ResolveDirectOverride(0.15, true) == 0.15 &&
              WheelSteeringMapping.ResolveDirectOverride(0.15, false) == -0.15 &&
              WheelSteeringMapping.ResolveDirectOverride(-0.15, true) == -0.15 &&
              WheelSteeringMapping.ResolveDirectOverride(-0.15, false) == 0.15,
            "direct steering override did not match MyMotorSuspension.Steer inversion behavior");
        Check(WheelSteeringMapping.ResolveDirectOverride(double.NaN, true) == 0 &&
              WheelSteeringMapping.ResolveDirectOverride(double.PositiveInfinity, false) == 0,
            "non-finite direct steering input was not neutralized");
    }

    private static void DirectWheelOverrideSignsMatchNativeDrive()
    {
        var wheels = new[]
        {
            new { Id = 104326076815143335L, RevolveInvert = true },
            new { Id = 137181966790677954L, RevolveInvert = false },
            new { Id = 102601932682680356L, RevolveInvert = false },
            new { Id = 141177257262231226L, RevolveInvert = true }
        };
        foreach (var input in new[] { -0.05, 0.05 })
        foreach (var wheel in wheels)
        foreach (var invertPropulsion in new[] { false, true })
        {
            var nativeInputForward = input > 0;
            var nativeAccelerateForward = nativeInputForward != invertPropulsion;
            var expectedWheelUp = wheel.RevolveInvert == nativeAccelerateForward;
            var direct = WheelPropulsionMapping.ResolveDirectOverride(input,
                wheel.RevolveInvert, invertPropulsion);
            var overrideAccelerateForward = (direct > 0) != invertPropulsion;
            Check(direct != 0 && (overrideAccelerateForward == expectedWheelUp),
                "wheel " + wheel.Id + " direct sign matches the native " +
                (input > 0 ? "forward" : "reverse") + " impulse for inversion=" + invertPropulsion);
        }
        var forwardSigns = new[] { 1.0, -1.0, -1.0, 1.0 };
        var reverseSigns = new[] { -1.0, 1.0, 1.0, -1.0 };
        for (var index = 0; index < wheels.Length; index++)
        {
            var forward = WheelPropulsionMapping.ResolveDirectOverride(0.05,
                wheels[index].RevolveInvert, false);
            var reverse = WheelPropulsionMapping.ResolveDirectOverride(-0.05,
                wheels[index].RevolveInvert, false);
            Check(Math.Sign(forward) == forwardSigns[index] && Math.Sign(reverse) == reverseSigns[index],
                "wheel " + wheels[index].Id + " has the audited alternating forward/reverse override signs");
        }
        Check(WheelPropulsionMapping.ResolveDirectOverride(0, true, false) == 0 &&
              WheelPropulsionMapping.ResolveDirectOverride(double.NaN, false, false) == 0,
            "zero or invalid controller input does not create a wheel override");
    }

    private static void DirectWheelCommandAcceptanceMatchesMappedValues()
    {
        var wheels = new[]
        {
            new { Id = 104326076815143335L, RevolveInvert = true, InvertPropulsion = false },
            new { Id = 137181966790677954L, RevolveInvert = false, InvertPropulsion = false },
            new { Id = 102601932682680356L, RevolveInvert = false, InvertPropulsion = false },
            new { Id = 141177257262231226L, RevolveInvert = true, InvertPropulsion = false }
        };
        foreach (var input in new[] { -0.1, 0.1 })
        {
            var mapped = new Dictionary<long, double>();
            foreach (var wheel in wheels)
                mapped.Add(wheel.Id, WheelPropulsionMapping.ResolveDirectOverride(input,
                    wheel.RevolveInvert, wheel.InvertPropulsion));
            foreach (var wheel in wheels)
            {
                var expected = mapped[wheel.Id];
                Check(DirectManeuverExecutor.IsCommandValueExpected(
                          DirectManeuverActuatorChannel.PropulsionOverride, wheel.Id, expected,
                          0, input, false, mapped),
                    "wheel " + wheel.Id + " accepted its individually mapped " + input + " propulsion value");
                Check(!DirectManeuverExecutor.IsCommandValueExpected(
                          DirectManeuverActuatorChannel.PropulsionOverride, wheel.Id, -expected,
                          0, input, false, mapped),
                    "wheel " + wheel.Id + " rejected a conflicting uniform-sign expectation");
            }
        }
        var incomplete = new Dictionary<long, double> { [wheels[0].Id] = 0.1 };
        Check(!DirectManeuverExecutor.IsCommandValueExpected(
                  DirectManeuverActuatorChannel.PropulsionOverride, wheels[1].Id, 0.1,
                  0, 0.1, false, incomplete),
            "a missing wheel mapping was not rejected");
        Check(DirectManeuverExecutor.IsCommandValueExpected(
                  DirectManeuverActuatorChannel.PropulsionOverride, wheels[1].Id, 0,
                  0, 0.1, true, new Dictionary<long, double> { [wheels[1].Id] = 0 }),
            "controlled stop did not accept the mapped neutral wheel value");
    }

    private static void DirectManeuverImmediateSafetyStop()
    {
        Check(DirectManeuverExecutor.TryGetImmediateSafetyStopReason(false,
                  DirectManeuverKind.ForwardDistance, 0.1, 0.2, 0, 7.5, out _),
            "stale run-bound authority did not request an immediate stop");
        Check(DirectManeuverExecutor.TryGetImmediateSafetyStopReason(true,
                  DirectManeuverKind.ForwardDistance, 0.201, 0.2, 0, 7.5, out _),
            "forward overspeed did not request a stop on the current host sample");
        Check(DirectManeuverExecutor.TryGetImmediateSafetyStopReason(true,
                  DirectManeuverKind.ReverseDistance, -0.201, 0.2, 0, 7.5, out _),
            "reverse overspeed did not request a stop on the current host sample");
        Check(DirectManeuverExecutor.TryGetImmediateSafetyStopReason(true,
                  DirectManeuverKind.ForwardDistance, 0.2, 0.2, 7.5, 7.5, out _),
            "7.5-meter pre-boundary hard stop was not enforced");
        Check(!DirectManeuverExecutor.TryGetImmediateSafetyStopReason(true,
                   DirectManeuverKind.ForwardDistance, 0.2, 0.2, 7.49, 7.5, out _) &&
              !DirectManeuverExecutor.TryGetImmediateSafetyStopReason(true,
                   DirectManeuverKind.StationaryHold, 0.2, 0, 0, 7.5, out _),
            "safe movement below the cap or a stationary-hold settling sample was misclassified");
        Check(DirectManeuverExecutor.TryGetImmediateSafetyStopReason(true,
                  DirectManeuverKind.ForwardDistance, double.NaN, 0.2, 0, 7.5, out _),
            "invalid speed telemetry did not fail closed");
        var started = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        Check(DirectManeuverExecutor.TryGetImmediateSafetyStopReason(true, started,
                  started.AddSeconds(45), started.AddSeconds(45), DirectManeuverKind.ForwardDistance,
                  0, 0.2, 0, 7.5, out _),
            "frozen simulation time bypassed the wall-clock deadline");
        Check(DirectManeuverExecutor.TryGetImmediateSafetyStopReason(true, started,
                  started.AddMilliseconds(1100), started, DirectManeuverKind.ForwardDistance,
                  0, 0.2, 0, 7.5, out _),
            "stale host capture was accepted while simulation time was frozen");
        Check(DirectManeuverExecutor.TryGetImmediateSafetyStopReason(true, started,
                  started.AddMilliseconds(100), started.AddMilliseconds(100), DirectManeuverKind.ForwardDistance,
                  0, 0.2, 7.5, 7.5, out _),
            "host-time excursion guard missed the hard distance boundary");
    }

    private static void C8PreflightDenialRetainsReason()
    {
        var now = DateTime.UtcNow;
        var names = new[] { "fixture_source_integrity", "runtime_connectivity", "vehicle_identity",
            "voidwright_module_and_session_marker", "controller_readiness", "actuator_availability",
            "sensor_validity_freshness", "position_validity_freshness", "safety_configuration",
            "movement_interface_readiness" };
        var checks = new List<PreflightCheck>();
        foreach (var name in names)
            checks.Add(new PreflightCheck(name,
                name == "safety_configuration" ? PreflightStatus.Fail : PreflightStatus.Pass,
                name == "safety_configuration"
                    ? "Direct maneuver speed or timeout bound failed; measured speed exceeded allowed speed."
                    : "ok",
                name == "safety_configuration"
                    ? "maxSpeed=0.2;currentSpeed=0.359;allowedSpeed=0.3;timeoutTicks=1440"
                    : "ok", "VoidwrightLabPreflight", now));
        var preflight = new MovementPreflight(PreflightRunId, PreflightOperationId,
            PreflightFixture, PreflightState, now, checks);
        var summary = preflight.NonPassingSummary();
        Check(MovementPreflight.MustStopAfterHostApproval(preflight, true),
            "C8-style host-approved failed speed check did not become a terminal denial");
        Check(summary.Contains("safety_configuration:FAIL") && summary.Contains("currentSpeed_0.359") &&
              summary.Contains("allowedSpeed_0.3"),
            "terminal diagnostic lost the failed C8 check or measured speed bound");
    }

    private static void DirectManeuverSeparatesHostAndModTicks()
    {
        var captured = new DateTime(2026, 10, 19, 21, 58, 30, DateTimeKind.Utc);
        DirectManeuverExecutionSample Sample(long hostSequence, long modTick, DateTime at) =>
            new DirectManeuverExecutionSample(PreflightRunId, PreflightFixture, PreflightOperationId,
                456, 123, 456, hostSequence, at, at, 0, 0, 0, 90, 0, null,
                true, true, 4, true, true, false, null, null,
                1, 0, 0, 0, 1, 1, modTick);
        var first = Sample(61, 60, captured);
        var second = Sample(62, 60, captured.AddMilliseconds(16));
        var heartbeatLost = Sample(63, -1, captured.AddMilliseconds(32));
        Check(second.HostSampleSequence > first.HostSampleSequence &&
              second.ModSimulationTick == first.ModSimulationTick,
            "repeated mod heartbeat tick froze the host's independent sample sequence");
        Check(heartbeatLost.HostSampleSequence > second.HostSampleSequence &&
              heartbeatLost.ModSimulationTick == -1 && heartbeatLost.CapturedUtc > second.CapturedUtc,
            "host telemetry could not continue with an unavailable mod heartbeat");
        Check(DirectManeuverExecutor.TryGetImmediateSafetyStopReason(false,
                  DirectManeuverKind.ForwardDistance, 0.25, 0.2, 0, 7.5, out _),
            "host speed safety stopped running when mod heartbeat authority was lost");
    }

    private static void DirectManeuverBrokerRequiresCompleteCapabilities()
    {
        Check(DirectManeuverBrokerDispatchGate.CanDispatch(true, true, true, true, true, true, true, true),
            "all shared broker capabilities authorize the policy gate");
        Check(!DirectManeuverBrokerDispatchGate.CanDispatch(false, true, true, true, true, true, true) &&
              !DirectManeuverBrokerDispatchGate.CanDispatch(true, false, true, true, true, true, true) &&
              !DirectManeuverBrokerDispatchGate.CanDispatch(true, true, false, true, true, true, true) &&
              !DirectManeuverBrokerDispatchGate.CanDispatch(true, true, true, false, true, true, true) &&
              !DirectManeuverBrokerDispatchGate.CanDispatch(true, true, true, true, false, true, true) &&
              !DirectManeuverBrokerDispatchGate.CanDispatch(true, true, true, true, true, false, true) &&
              !DirectManeuverBrokerDispatchGate.CanDispatch(true, true, true, true, true, true, false, true) &&
              !DirectManeuverBrokerDispatchGate.CanDispatch(true, true, true, true, true, true, true, false),
            "each missing lease, attestation, operator, writer audit, readback, stop, or stable-priority capability denies dispatch");
    }

    private static void DirectActuatorSessionBindingIsExactAndExpiring()
    {
        var now = new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);
        var expiry = now.AddSeconds(45);
        Check(DirectManeuverActuatorSessionBindingPolicy.IsValid(now, expiry,
                  456, 123, PreflightOperationId, 456, 123, PreflightOperationId, 4, 4, 4),
            "fresh cache for the exact operation, controller, grid, and wheel inventory was rejected");
        Check(!DirectManeuverActuatorSessionBindingPolicy.IsValid(now, expiry,
                  456, 123, PreflightOperationId, 789, 123, PreflightOperationId, 4, 4, 4) &&
              !DirectManeuverActuatorSessionBindingPolicy.IsValid(now, expiry,
                  456, 123, PreflightOperationId, 456, 124, PreflightOperationId, 4, 4, 4) &&
              !DirectManeuverActuatorSessionBindingPolicy.IsValid(now, expiry,
                  456, 123, PreflightOperationId, 456, 123, "22222222222222222222222222222222", 4, 4, 4),
            "cache remained usable after its grid, controller, or operation binding changed");
        Check(!DirectManeuverActuatorSessionBindingPolicy.IsValid(now, expiry,
                  456, 123, PreflightOperationId, 456, 123, PreflightOperationId, 4, 3, 4) &&
              !DirectManeuverActuatorSessionBindingPolicy.IsValid(now, expiry,
                  456, 123, PreflightOperationId, 456, 123, PreflightOperationId, 4, 4, 3),
            "cache remained usable after wheel inventory changed or became incomplete");
        Check(!DirectManeuverActuatorSessionBindingPolicy.IsValid(expiry, expiry,
                  456, 123, PreflightOperationId, 456, 123, PreflightOperationId, 4, 4, 4),
            "expired actuator references remained valid at their expiry boundary");
    }

    private static void DirectManeuverAgentCoreBindingIsFailClosed()
    {
        Check(AgentCoreBindingPolicy.IsValid(TestAgentCoreId, TestAgentCoreId, 456, 456, 100, 100, 0, true),
            "fresh exact Core on the anchor grid was rejected");
        Check(!AgentCoreBindingPolicy.IsValid(0, TestAgentCoreId, 456, 456, 100, 100, 0, true) &&
              !AgentCoreBindingPolicy.IsValid(TestAgentCoreId, TestAgentCoreId + 1, 456, 456, 100, 100, 0, true),
            "missing or wrong Agent Core identity was accepted");
        Check(!AgentCoreBindingPolicy.IsValid(TestAgentCoreId, TestAgentCoreId, 456, 789, 100, 100, 0, true),
            "Agent Core moved to a different anchor grid without revoking the binding");
        Check(!AgentCoreBindingPolicy.IsValid(TestAgentCoreId, TestAgentCoreId, 456, 456, 98, 100, 1, true) &&
              !AgentCoreBindingPolicy.IsValid(TestAgentCoreId, TestAgentCoreId, 456, 456, 100, 100, 0, false),
            "stale or incomplete Core discovery was accepted");
        Check(!DirectManeuverBrokerDispatchGate.CanDispatch(true, true, true, false, true, true, true, true) &&
              !DirectManeuverBrokerDispatchGate.CanDispatch(true, true, true, true, true, true, true, false),
            "operator takeover or controller-priority change did not revoke dispatch");
        Check(!DirectManeuverBrokerDispatchGate.CanDispatch(false, true, true, true, true, true, true, true),
            "WorldStorage transport availability was treated as independently verified lease authority");
    }

    private static void DirectManeuversAreBoundedAndFailClosed()
    {
        var now = DateTime.UtcNow;
        var plan = Plan(PreflightRunId, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            PreflightOperationId, 456, 123, new[]
            {
                new DirectManeuverSegment(DirectManeuverKind.SteerAngle, 35, 0, 60),
                new DirectManeuverSegment(DirectManeuverKind.ForwardDistance, 1.5, 0.25, 300),
                new DirectManeuverSegment(DirectManeuverKind.Stop, 0, 0, 120),
                new DirectManeuverSegment(DirectManeuverKind.SteerAngle, -35, 0, 60),
                new DirectManeuverSegment(DirectManeuverKind.ReverseDistance, 1, 0.25, 300),
                new DirectManeuverSegment(DirectManeuverKind.StationaryHold, 0, 0, 120),
                new DirectManeuverSegment(DirectManeuverKind.ForwardDistance, 1.5, 0.25, 300),
                new DirectManeuverSegment(DirectManeuverKind.Stop, 0, 0, 120),
                new DirectManeuverSegment(DirectManeuverKind.StationaryHold, 0, 0, 120)
            });
        Check(plan.IsValid(out _), "composable three-point-turn segments did not validate");
        var zeroOnlyPlan = Plan(PreflightRunId, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            PreflightOperationId, 456, 123, new[]
            {
                new DirectManeuverSegment(DirectManeuverKind.StationaryHold, 0, 0, 60)
            });
        Check(zeroOnlyPlan.IsValid(out _) && zeroOnlyPlan.IsZeroActuationOnly &&
              zeroOnlyPlan.ToCompactSegments() == "W,0,0,60" &&
              zeroOnlyPlan.AllowsActuatorValues(0, 0) &&
              !zeroOnlyPlan.AllowsActuatorValues(0.000001, 0) &&
              !zeroOnlyPlan.AllowsActuatorValues(0, -0.000001) &&
              !zeroOnlyPlan.AllowsActuatorValues(double.NaN, 0) &&
              !plan.IsZeroActuationOnly,
            "zero-only stationary hold accepted a nonzero or nonfinite actuator value");
        Check(DirectManeuverPlan.TryParseCompactSegments(plan.ToCompactSegments(), PreflightRunId,
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", PreflightOperationId, 456, 123, TestAgentCoreId, out var reparsed) &&
              reparsed.Segments.Count == plan.Segments.Count,
            "serialized direct segments did not round-trip through the strict bounded parser");
        Check(!DirectManeuverPlan.TryParseCompactSegments(plan.ToCompactSegments() + "|bad", PreflightRunId,
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", PreflightOperationId, 456, 123, TestAgentCoreId, out _),
            "compact direct-segment parser accepted malformed trailing data");

        var evidence = Evidence(PreflightRunId, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            PreflightOperationId, 456, 123, 456, 3.4, now, true, now, true, now, true, true,
            true, true, true, true);
        Check(plan.CanDispatch(evidence, now.AddMilliseconds(10), out _), "fresh exact host/mod/sensor and ownership evidence did not authorize dispatch");
        var noParticipatingWriterPath = Evidence(PreflightRunId, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            PreflightOperationId, 456, 123, 456, 5, now, true, now, true, now, true, true,
            false, true, true, true);
        Check(!plan.CanDispatch(noParticipatingWriterPath, now, out _),
            "unavailable participating-writer path authorized movement");
        var missingCoreEvidence = new DirectManeuverEvidence(PreflightRunId, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            PreflightOperationId, 456, 123, 456, 4.0, now, true, now, true, now, true, true,
            true, true, true, true, 0);
        var wrongCoreEvidence = new DirectManeuverEvidence(PreflightRunId, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            PreflightOperationId, 456, 123, 456, 4.0, now, true, now, true, now, true, true,
            true, true, true, true, TestAgentCoreId + 1);
        Check(!plan.CanDispatch(missingCoreEvidence, now, out _) &&
              !plan.CanDispatch(wrongCoreEvidence, now, out _) &&
              !new DirectManeuverPlan(PreflightRunId, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", PreflightOperationId,
                  456, 123, plan.Segments).CanDispatch(evidence, now, out _),
            "missing or mismatched plan/evidence Agent Core ID authorized dispatch");
        Check(!plan.CanDispatch(evidence, now.AddSeconds(2), out _), "stale preflight evidence authorized dispatch");
        var blockedBrake = Evidence(PreflightRunId, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            PreflightOperationId, 456, 123, 456, 3.4, now, true, now, true, now, true, true,
            true, false, false);
        Check(!plan.CanDispatch(blockedBrake, now, out _), "unknown brake ownership authorized dispatch");
        var noSafeStop = Evidence(PreflightRunId, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            PreflightOperationId, 456, 123, 456, 3.4, now, true, now, true, now, true, true,
            true, true, true, false);
        Check(!plan.CanDispatch(noSafeStop, now, out _), "available brake setter without a guaranteed controlled stop authorized dispatch");
        var mismatchedSensor = Evidence(PreflightRunId, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            PreflightOperationId, 456, 123, 457, 3.4, now, true, now, true, now, true, true,
            true, true, true);
        Check(!plan.CanDispatch(mismatchedSensor, now, out _), "cross-grid sensor evidence authorized dispatch");
        var blocked = Plan(PreflightRunId, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            PreflightOperationId, 456, 123, new[] {
                new DirectManeuverSegment(DirectManeuverKind.ReverseDistance, 3.01, 0.25, 300) });
        Check(!blocked.IsValid(out _), "reverse distance exceeded the existing three-meter limit");
        var tinyReverse = Plan(PreflightRunId, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            PreflightOperationId, 456, 123, new[] {
                new DirectManeuverSegment(DirectManeuverKind.ReverseDistance, 0.1, 0.1, 20),
                new DirectManeuverSegment(DirectManeuverKind.StationaryHold, 0, 0, 20) });
        Check(tinyReverse.IsValid(out _), "bounded 0.1-meter reverse direction probe was rejected");
        var fastReverse = Plan(PreflightRunId, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            PreflightOperationId, 456, 123, new[] {
                new DirectManeuverSegment(DirectManeuverKind.ReverseDistance, 1, 0.51, 300) });
        Check(!fastReverse.IsValid(out _), "reverse speed exceeded the existing 0.5 m/s limit");
        var blockedSensor = Evidence(PreflightRunId, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            PreflightOperationId, 456, 123, 456, 2.0, now, true, now, true, now, true, true,
            true, true, true);
        Check(!plan.CanDispatch(blockedSensor, now, out _), "obstacle clearance below the existing threshold authorized dispatch");
    }

    private static void DirectManeuverCommandAcceptanceIsNarrow()
    {
        var run = PreflightRunId;
        var fixture = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var operation = PreflightOperationId;
        const long grid = 456, controller = 123, core = 789;
        const string compact = "W,0,0,60";
        Check(DirectManeuverPlan.TryParseCompactSegments(compact, run, fixture, operation,
                  grid, controller, core, out var accepted) &&
              DirectManeuverCommandAcceptancePolicy.IsZeroOnlyStationaryHold(
                  accepted, run, fixture, operation, grid, controller, core, compact, 180),
            "exact one-segment stationary hold with its stop budget was rejected");

        Check(!DirectManeuverPlan.TryParseCompactSegments("W,0,0,60|bad", run, fixture,
                  operation, grid, controller, core, out _) &&
              DirectManeuverPlan.TryParseCompactSegments(compact, run, fixture, operation,
                  grid, controller, core, out var wrongGridPlan) &&
              !DirectManeuverCommandAcceptancePolicy.IsZeroOnlyStationaryHold(
                  wrongGridPlan, run, fixture, operation, grid + 1, controller, core, compact, 180) &&
              !DirectManeuverCommandAcceptancePolicy.IsZeroOnlyStationaryHold(
                  accepted, run + "1", fixture, operation, grid, controller, core, compact, 180) &&
              !DirectManeuverCommandAcceptancePolicy.IsZeroOnlyStationaryHold(
                  accepted, run, fixture + "1", operation, grid, controller, core, compact, 180) &&
              !DirectManeuverCommandAcceptancePolicy.IsZeroOnlyStationaryHold(
                  accepted, run, fixture, operation + "1", grid, controller, core, compact, 180) &&
              !DirectManeuverCommandAcceptancePolicy.IsZeroOnlyStationaryHold(
                  accepted, run, fixture, operation, grid, controller, core, compact, 179),
            "malformed, wrong-grid, or unbudgeted hold command was accepted");

        Check(DirectManeuverPlan.TryParseCompactSegments("F,0.25,0.1,60;W,0,0,60", run,
                  fixture, operation, grid, controller, core, out var nonzero) &&
              !DirectManeuverCommandAcceptancePolicy.IsZeroOnlyStationaryHold(
                  nonzero, run, fixture, operation, grid, controller, core,
                  "F,0.25,0.1,60;W,0,0,60", 240) &&
              !DirectManeuverPlan.TryParseCompactSegments("W,0.001,0,60", run, fixture,
                  operation, grid, controller, core, out _),
            "nonzero actuation was accepted as a zero-only hold");

        var now = DateTime.UtcNow;
        Check(DirectManeuverCommandAcceptancePolicy.IsDiscoveryFresh(now.AddSeconds(-14), now,
                  TimeSpan.FromSeconds(15)) &&
              !DirectManeuverCommandAcceptancePolicy.IsDiscoveryFresh(now.AddSeconds(-16), now,
                  TimeSpan.FromSeconds(15)) &&
              !DirectManeuverCommandAcceptancePolicy.IsDiscoveryFresh(now.AddSeconds(1), now,
                  TimeSpan.FromSeconds(15)),
            "stale or future discovery snapshot passed freshness policy");
        Check(DirectManeuverCommandAcceptancePolicy.MatchesDiscoveryIdentity(run, 44,
                  grid, controller, core, run, 44, grid, controller, core) &&
              !DirectManeuverCommandAcceptancePolicy.MatchesDiscoveryIdentity(run, 44,
                  grid + 1, controller, core, run, 44, grid, controller, core) &&
              !DirectManeuverCommandAcceptancePolicy.MatchesDiscoveryIdentity(run, 44,
                  grid, controller + 1, core, run, 44, grid, controller, core) &&
              !DirectManeuverCommandAcceptancePolicy.MatchesDiscoveryIdentity(run, 44,
                  grid, controller, core + 1, run, 44, grid, controller, core) &&
              !DirectManeuverCommandAcceptancePolicy.MatchesDiscoveryIdentity(run, 45,
                  grid, controller, core, run, 44, grid, controller, core) &&
              !DirectManeuverCommandAcceptancePolicy.MatchesDiscoveryIdentity(run + "1", 44,
                  grid, controller, core, run, 44, grid, controller, core),
            "cross-run, stale-tick, wrong-grid, controller, or Agent Core discovery was accepted");
        Check(DirectManeuverCommandAcceptancePolicy.MatchesFreshDiscoveryAdvance(
                  run, 45, "b".PadLeft(64, '2'), grid, controller, core,
                  run, 44, "a".PadLeft(64, '1'), grid, controller, core) &&
              !DirectManeuverCommandAcceptancePolicy.MatchesFreshDiscoveryAdvance(
                  run, 43, "b".PadLeft(64, '2'), grid, controller, core,
                  run, 44, "a".PadLeft(64, '1'), grid, controller, core) &&
              !DirectManeuverCommandAcceptancePolicy.MatchesFreshDiscoveryAdvance(
                  run, 45, "b".PadLeft(64, '2'), grid + 1, controller, core,
                  run, 44, "a".PadLeft(64, '1'), grid, controller, core) &&
              !DirectManeuverCommandAcceptancePolicy.MatchesFreshDiscoveryAdvance(
                  run, 44, "b".PadLeft(64, '2'), grid, controller, core,
                  run, 44, "a".PadLeft(64, '1'), grid, controller, core),
            "fresh tick advancement did not preserve exact run, tick monotonicity, fingerprint, and entity identity rules");
    }

    private static void DirectManeuverTelemetrySeparatesCommandAndResponse()
    {
        var accepted = new DirectManeuverTelemetry(-1, 0, 0, -25, -2, 0, 0, true, true,
            DirectManeuverOutcome.Running, "command accepted; wheel response not yet observed");
        Check(accepted.CommandedSteeringSign == -1 && accepted.ActualSteeringSign == 0,
            "commanded steering sign was conflated with observed steering response");
        Check(!accepted.HasSteeringResponse && accepted.Outcome == DirectManeuverOutcome.Running,
            "command acceptance was reported as steering response or terminal completion");
        var responded = new DirectManeuverTelemetry(-1, -1, -0.2, -25, -21, 1.2, 0.2, true, true,
            DirectManeuverOutcome.Complete, "stationary hold telemetry confirmed completion");
        Check(responded.HasSteeringResponse && responded.Outcome == DirectManeuverOutcome.Complete,
            "actual wheel response and terminal completion were not independently recorded");
    }

    private static void DirectManeuverLifecycleRequiresStationaryHold()
    {
        var lifecycle = new DirectManeuverLifecycle(true);
        Check(lifecycle.Acceptance == DirectManeuverAcceptance.Accepted &&
              lifecycle.Outcome == DirectManeuverOutcome.NotStarted,
            "acceptance was conflated with execution or completion");
        Check(lifecycle.Start(), "accepted maneuver did not enter the running state");
        Check(lifecycle.RequestCancellation() && lifecycle.StopDispatchRequired &&
              lifecycle.Outcome == DirectManeuverOutcome.StopRequested,
            "cancellation did not request an owned stop before terminal status");
        Check(!lifecycle.ObserveStationaryTelemetry(20, 0, 0), "one stationary sample was reported terminal");
        Check(!lifecycle.ObserveStationaryTelemetry(20, 0, 0), "duplicate tick counted as independent stationary evidence");
        Check(!lifecycle.ObserveStationaryTelemetry(21, 0.1, 0), "moving telemetry counted as stationary evidence");
        Check(!lifecycle.ObserveStationaryTelemetry(22, 0, 0), "stationary evidence resumed without a complete fresh hold");
        Check(!lifecycle.ObserveStationaryTelemetry(23, 0, 0), "two samples were reported terminal");
        Check(lifecycle.ObserveStationaryTelemetry(24, 0, 0) && !lifecycle.StopDispatchRequired &&
              lifecycle.Outcome == DirectManeuverOutcome.Cancelled,
            "cancellation did not require three distinct stationary samples");

        var emergency = new DirectManeuverLifecycle(true);
        Check(emergency.Start() && emergency.RequestEmergencyStop(), "emergency stop request was not accepted");
        emergency.ObserveStationaryTelemetry(1, 0, 0);
        emergency.ObserveStationaryTelemetry(2, 0, 0);
        Check(emergency.ObserveStationaryTelemetry(3, 0, 0) &&
              emergency.Outcome == DirectManeuverOutcome.EmergencyStopped,
            "emergency-stop terminal status was not telemetry-confirmed");
    }

    private static void DirectManeuverStartupStationaryGateIsStrict()
    {
        var gate = new DirectManeuverStationaryGate();
        Check(!gate.Observe(10, 0.053, 0, 0, 0) && gate.StationarySampleCount == 0,
            "startup stationary gate accepted a speed above the strict rest bound");
        Check(!gate.Observe(20, 0.049, 0, 0, 0) && gate.StationarySampleCount == 1,
            "first in-bound startup sample was not counted");
        Check(!gate.Observe(20, 0.049, 0, 0, 0) && gate.StationarySampleCount == 1,
            "duplicate startup telemetry tick was counted twice");
        Check(!gate.Observe(30, 0.049, 0.005, 0, 0) && gate.StationarySampleCount == 2 &&
              gate.Observe(40, 0.049, 0.010, 0, 0),
            "startup gate did not require three distinct stationary samples within the displacement bound");

        var moving = new DirectManeuverStationaryGate();
        moving.Observe(10, 0.049, 0, 0, 0);
        Check(!moving.Observe(20, 0.049, 0.03, 0, 0) && moving.StationarySampleCount == 0,
            "startup stationary gate accepted displacement beyond the existing bound");
    }

    private static void DirectManeuverExecutorIsBoundedAndFailClosed()
    {
        var now = DateTime.UtcNow;
        const string fixture = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const long grid = 456;
        const long controller = 123;
        var tickTime = now;
        var currentSteering = 0.0;
        var currentPropulsion = 0.0;
        var currentBrake = false;
        DirectManeuverEvidence Evidence(DateTime captured, bool safeStop = true) =>
            Program.Evidence(PreflightRunId, fixture, PreflightOperationId,
                grid, controller, grid, 4.0, captured, true, captured, true, captured, true, true,
                true, true, true, safeStop);
        DirectManeuverExecutionSample Sample(DateTime captured, long tick, double x = 0,
            double heading = 90, double speed = 0, double? steering = null,
            long sampleGrid = grid, bool sensorValid = true, DateTime? scanAt = null,
            bool externalBrake = false, double y = 0, double forwardX = 1,
            double forwardY = 0, double forwardZ = 0,
            double? longitudinalSpeed = null, double propulsionPolarity = 1,
            double minimumPropulsionWheelPower = 1) => new DirectManeuverExecutionSample(
                PreflightRunId, fixture, PreflightOperationId, sampleGrid, controller, grid,
                tick, captured, scanAt ?? captured, x, y, 0, heading, speed, steering,
                sensorValid, true, 4.0, true, true, externalBrake,
                WheelStates(captured, currentSteering, currentPropulsion, externalBrake || currentBrake),
                new DirectManeuverBrakeState(true, externalBrake || currentBrake, true,
                    externalBrake || currentBrake, captured), forwardX, forwardY, forwardZ,
                longitudinalSpeed, propulsionPolarity, minimumPropulsionWheelPower);
        IList<DirectManeuverWheelState> WheelStates(DateTime captured, double steering = 0,
            double propulsion = 0, bool brakeActive = false) =>
            new[] { new DirectManeuverWheelState(9001, steering, propulsion, true, brakeActive, captured) };
        DirectManeuverExecutor Executor(DirectManeuverPlan plan, DateTime at)
        {
            currentSteering = 0;
            currentPropulsion = 0;
            currentBrake = false;
            if (!DirectManeuverOperationLease.TryAcquire(plan, Evidence(at), WheelStates(at),
                    new DirectManeuverBrakeState(true, false, true, false, at),
                    new DirectManeuverOperationLeaseAuthority(), at,
                    TimeSpan.FromSeconds(2), out var lease, out var leaseReason))
                throw new InvalidOperationException("test lease setup failed: " + leaseReason);
            return new DirectManeuverExecutor(plan, lease);
        }
        bool Ack(DirectManeuverExecutor executor, DirectManeuverStep commandStep,
            bool accepted = true, string runId = null)
        {
            var commandIdentity = OperationIdentity(
                runId ?? PreflightRunId, fixture, PreflightOperationId, grid, controller);
            if (accepted && runId == null)
            {
                var steering = commandStep.Command.Steering;
                var propulsion = commandStep.Command.Propulsion;
                var setBrake = commandStep.Command.Kind == DirectManeuverDispatchKind.ControlledStop;
                if (currentBrake != setBrake)
                {
                    var target = setBrake ? 1.0 : 0.0;
                    var observed = currentBrake ? 1.0 : 0.0;
                    if (executor.RecordAcceptedChannelWrite(commandStep.Command.CommandId, 0,
                            DirectManeuverActuatorChannel.GridBrake, observed, target, target, tickTime) != DirectManeuverLeaseWriteResult.Accepted ||
                        executor.RecordAcceptedChannelWrite(commandStep.Command.CommandId, 0,
                            DirectManeuverActuatorChannel.HandBrake, observed, target, target, tickTime) != DirectManeuverLeaseWriteResult.Accepted)
                        return false;
                    currentBrake = setBrake;
                }
                if (executor.RecordAcceptedChannelWrite(commandStep.Command.CommandId, 9001,
                        DirectManeuverActuatorChannel.SteeringOverride, currentSteering,
                        steering, steering, tickTime) != DirectManeuverLeaseWriteResult.Accepted ||
                    executor.RecordAcceptedChannelWrite(commandStep.Command.CommandId, 9001,
                        DirectManeuverActuatorChannel.PropulsionOverride, currentPropulsion,
                        propulsion, propulsion, tickTime,
                        new Dictionary<long, double> { [9001] = propulsion }) != DirectManeuverLeaseWriteResult.Accepted)
                    return false;
                currentSteering = steering;
                currentPropulsion = propulsion;
            }
            return executor.AcknowledgeCommand(new DirectManeuverCommandAcceptance(
                commandIdentity, commandStep.Command.CommandId, accepted, tickTime));
        }

        var forwardPlan = Plan(PreflightRunId, fixture, PreflightOperationId,
            grid, controller, new[] {
                new DirectManeuverSegment(DirectManeuverKind.ForwardDistance, 0.25, 0.25, 20),
                new DirectManeuverSegment(DirectManeuverKind.StationaryHold, 0, 0, 20)
            });
        var tinyReversePlan = Plan(PreflightRunId, fixture, PreflightOperationId,
            grid, controller, new[] {
                new DirectManeuverSegment(DirectManeuverKind.ReverseDistance, 0.1, 0.1, 600),
                new DirectManeuverSegment(DirectManeuverKind.StationaryHold, 0, 0, 20)
            });
        var unpoweredReverse = Executor(tinyReversePlan, now);
        var unpoweredStep = unpoweredReverse.Start(Evidence(now),
            Sample(now, 1, minimumPropulsionWheelPower: 0), now);
        Check(unpoweredStep.Dispatch == DirectManeuverDispatchKind.None &&
              unpoweredStep.Outcome == DirectManeuverOutcome.PreflightBlocked,
            "reverse maneuver dispatched with zero saved propulsion-wheel power");
        var poweredReverse = Executor(tinyReversePlan, now);
        tickTime = now;
        var reverseStep = poweredReverse.Start(Evidence(now),
            Sample(now, 1, minimumPropulsionWheelPower: 0.15), now);
        Check(reverseStep.Command != null && reverseStep.Command.Propulsion < 0 &&
              Math.Abs(reverseStep.Command.Propulsion) <= DirectManeuverExecutor.MaximumReversePropulsionInput,
            "powered tiny reverse did not produce the bounded reverse command");
        Check(Ack(poweredReverse, reverseStep), "bounded tiny reverse command was not accepted");
        tickTime = now.AddMilliseconds(50);
        reverseStep = poweredReverse.Step(Evidence(tickTime), Sample(tickTime, 2,
            x: -0.0158, speed: 0.048, longitudinalSpeed: -0.048,
            minimumPropulsionWheelPower: 0.15), tickTime);
        Check(!reverseStep.MovementObserved && poweredReverse.Outcome == DirectManeuverOutcome.Running,
            "15.8 mm of excursion below the executor threshold was promoted to movement confirmation");

        var lateralMotion = Executor(forwardPlan, now);
        var lateralStep = lateralMotion.Start(Evidence(now),
            Sample(now, 1, speed: 0.20, longitudinalSpeed: 0), now);
        Check(lateralStep.Command != null && Math.Abs(lateralStep.Command.Propulsion - 0.1) < 0.000001,
            "off-axis speed prematurely tapered forward propulsion");
        var forwardMotion = Executor(forwardPlan, now);
        var forwardStep = forwardMotion.Start(Evidence(now),
            Sample(now, 1, speed: 0.20, longitudinalSpeed: 0.20,
                propulsionPolarity: -1), now);
        Check(forwardStep.Command != null && forwardStep.Command.Propulsion < 0 &&
              forwardStep.Command.Propulsion > -0.1,
            "controller-forward speed or the reversed grid-frame propulsion sign was ignored");
        var reverseFeedbackPlan = Plan(PreflightRunId, fixture, PreflightOperationId,
            grid, controller, new[] {
                new DirectManeuverSegment(DirectManeuverKind.ReverseDistance, 0.25, 0.25, 20),
                new DirectManeuverSegment(DirectManeuverKind.StationaryHold, 0, 0, 20)
            });
        var reverseFeedback = Executor(reverseFeedbackPlan, now);
        var reverseFeedbackStep = reverseFeedback.Start(Evidence(now),
            Sample(now, 1, speed: 0.20, longitudinalSpeed: 0.20,
                propulsionPolarity: -1), now);
        Check(reverseFeedbackStep.Command != null && reverseFeedbackStep.Command.Propulsion > 0 &&
              Math.Abs(reverseFeedbackStep.Command.Propulsion - DirectManeuverExecutor.MaximumReversePropulsionInput) < 0.000001,
            "reverse propulsion tapered away while motion was still forward");
        var forward = Executor(forwardPlan, now);
        tickTime = now;
        var step = forward.Start(Evidence(tickTime), Sample(tickTime, 1), tickTime);
        Check(step.Dispatch == DirectManeuverDispatchKind.WheelInputs && step.Command.Propulsion > 0 &&
              step.LastCommandAccepted == null && !step.MovementObserved && !step.CompletionConfirmed,
            "forward command was not separated from acceptance, observed movement, and completion");
        Check(!Ack(forward, step, runId: "different-run"),
            "command acceptance from a different run was accepted");
        Check(Ack(forward, step), "forward wheel command acceptance was not recorded");
        tickTime = now.AddMilliseconds(50);
        step = forward.Step(Evidence(tickTime), Sample(tickTime, 2, x: 0.3, speed: 0.1), tickTime);
        Check(step.Dispatch == DirectManeuverDispatchKind.ControlledStop && step.MovementObserved &&
              !step.CompletionConfirmed,
            "forward distance completion did not request a stop after observed movement");
        Check(Ack(forward, step), "controlled stop acceptance was not recorded");
        for (var tick = 3; tick <= 6; tick++)
        {
            tickTime = now.AddMilliseconds(tick * 50);
            step = forward.Step(Evidence(tickTime), Sample(tickTime, tick, x: 0.3), tickTime);
        }
        Check(step.Dispatch == DirectManeuverDispatchKind.ControlledStop && forward.SegmentIndex == 1,
            "forward segment did not require three distinct stationary samples before hold");
        Ack(forward, step);
        for (var tick = 7; tick <= 9; tick++)
        {
            tickTime = now.AddMilliseconds(tick * 50);
            step = forward.Step(Evidence(tickTime), Sample(tickTime, tick, x: 0.3), tickTime);
        }
        Check(step.CompletionConfirmed && step.Outcome == DirectManeuverOutcome.Complete,
            "final stationary hold did not complete after three distinct samples");

        var rotatedWorldPlan = Plan(PreflightRunId, fixture, PreflightOperationId,
            grid, controller, new[] {
                new DirectManeuverSegment(DirectManeuverKind.ForwardDistance, 0.25, 0.25, 20),
                new DirectManeuverSegment(DirectManeuverKind.StationaryHold, 0, 0, 20)
            });
        var rotatedWorld = Executor(rotatedWorldPlan, now);
        tickTime = now;
        step = rotatedWorld.Start(Evidence(tickTime), Sample(tickTime, 1,
            forwardX: 0, forwardY: 1, forwardZ: 0), tickTime);
        Ack(rotatedWorld, step);
        tickTime = now.AddMilliseconds(50);
        step = rotatedWorld.Step(Evidence(tickTime), Sample(tickTime, 2,
            y: 0.3, forwardX: 0, forwardY: 1, forwardZ: 0, speed: 0.1), tickTime);
        Check(step.Dispatch == DirectManeuverDispatchKind.ControlledStop,
            "distance completion ignored forward displacement on a rotated-world vertical axis");

        var reversePlan = Plan(PreflightRunId, fixture, PreflightOperationId,
            grid, controller, new[] {
                new DirectManeuverSegment(DirectManeuverKind.ReverseDistance, 0.25, 0.25, 20),
                new DirectManeuverSegment(DirectManeuverKind.StationaryHold, 0, 0, 20)
            });
        var reverse = Executor(reversePlan, now);
        tickTime = now;
        step = reverse.Start(Evidence(tickTime), Sample(tickTime, 1), tickTime);
        Check(Math.Abs(step.Command.Propulsion + DirectManeuverExecutor.MaximumReversePropulsionInput) < 0.000001,
            "reverse segment did not use the bounded low-power probe input");
        Ack(reverse, step);
        tickTime = now.AddMilliseconds(50);
        step = reverse.Step(Evidence(tickTime), Sample(tickTime, 2, x: -0.3, speed: 0.1), tickTime);
        Check(step.Dispatch == DirectManeuverDispatchKind.ControlledStop,
            "reverse progress was not projected in the commanded direction");

        var wrongWayReversePlan = Plan(PreflightRunId, fixture, PreflightOperationId,
            grid, controller, new[] {
                new DirectManeuverSegment(DirectManeuverKind.ReverseDistance, 0.25, 0.25, 20),
                new DirectManeuverSegment(DirectManeuverKind.StationaryHold, 0, 0, 20)
            });
        var wrongWayReverse = Executor(wrongWayReversePlan, now);
        tickTime = now;
        step = wrongWayReverse.Start(Evidence(tickTime), Sample(tickTime, 1), tickTime);
        Ack(wrongWayReverse, step);
        tickTime = now.AddMilliseconds(50);
        step = wrongWayReverse.Step(Evidence(tickTime), Sample(tickTime, 2,
            speed: 0.15, longitudinalSpeed: 0.15), tickTime);
        Check(step.Dispatch == DirectManeuverDispatchKind.WheelInputs &&
              Math.Abs(Math.Abs(step.Command.Propulsion) - DirectManeuverExecutor.MaximumReversePropulsionInput) < 0.000001,
            "reverse propulsion tapered away while longitudinal motion was still forward");

        var steerPlan = Plan(PreflightRunId, fixture, PreflightOperationId,
            grid, controller, new[] {
                new DirectManeuverSegment(DirectManeuverKind.SteerAngle, 20, 0, 20),
                new DirectManeuverSegment(DirectManeuverKind.StationaryHold, 0, 0, 20)
            });
        var steer = Executor(steerPlan, now);
        tickTime = now;
        step = steer.Start(Evidence(tickTime), Sample(tickTime, 1), tickTime);
        Check(step.Command.Steering > 0 && step.Command.Steering <= 1 && step.Command.Propulsion == 0,
            "steering-angle segment exceeded its normalized steering-only envelope");
        Ack(steer, step);
        tickTime = now.AddMilliseconds(50);
        step = steer.Step(Evidence(tickTime), Sample(tickTime, 2, steering: 20), tickTime);
        Check(step.Dispatch == DirectManeuverDispatchKind.ControlledStop,
            "steering angle completed without a matching observed wheel angle");

        var threePointPlan = Plan(PreflightRunId, fixture, PreflightOperationId,
            grid, controller, new[] {
                new DirectManeuverSegment(DirectManeuverKind.ForwardDistance, 0.25, 0.25, 20),
                new DirectManeuverSegment(DirectManeuverKind.ReverseDistance, 0.25, 0.25, 20),
                new DirectManeuverSegment(DirectManeuverKind.ForwardDistance, 0.25, 0.25, 20),
                new DirectManeuverSegment(DirectManeuverKind.StationaryHold, 0, 0, 20)
            });
        var threePoint = Executor(threePointPlan, now);
        tickTime = now;
        step = threePoint.Start(Evidence(tickTime), Sample(tickTime, 1), tickTime);
        Ack(threePoint, step);
        var position = 0.0;
        for (var segment = 0; segment < 3; segment++)
        {
            var kind = threePointPlan.Segments[segment].Kind;
            position += kind == DirectManeuverKind.ReverseDistance ? -0.3 : 0.3;
            tickTime = tickTime.AddMilliseconds(50);
            step = threePoint.Step(Evidence(tickTime), Sample(tickTime, segment * 10 + 2,
                x: position, speed: 0.1), tickTime);
            Check(step.Dispatch == DirectManeuverDispatchKind.ControlledStop,
                "three-point turn segment did not stop at its bounded distance");
            Ack(threePoint, step);
            for (var stationary = 0; stationary < 4; stationary++)
            {
                tickTime = tickTime.AddMilliseconds(50);
                step = threePoint.Step(Evidence(tickTime), Sample(tickTime,
                    segment * 10 + 3 + stationary, x: position), tickTime);
            }
            if (segment < 2)
            {
                Check(step.Dispatch == DirectManeuverDispatchKind.WheelInputs &&
                      threePoint.SegmentIndex == segment + 1,
                    "three-point turn advanced without a stop-confirmed next segment");
                Ack(threePoint, step);
            }
        }
        Check(threePoint.SegmentIndex == 3,
            "bounded forward/reverse/forward sequence did not reach its final stationary hold");

        var cancellable = Executor(forwardPlan, now);
        tickTime = now;
        step = cancellable.Start(Evidence(tickTime), Sample(tickTime, 1), tickTime);
        Ack(cancellable, step);
        var identity = OperationIdentity(PreflightRunId, fixture,
            PreflightOperationId, grid, controller);
        var wrongStop = cancellable.RequestCancellation(new DirectManeuverStopRequest(
            OperationIdentity("different-run", fixture, PreflightOperationId, grid, controller),
            "wrong-run-cancel", DirectManeuverStopKind.Cancellation, tickTime), tickTime);
        Check(!wrongStop.Accepted && cancellable.Outcome == DirectManeuverOutcome.Running,
            "cancel request for a different run changed this operation");
        var cancelAcceptance = cancellable.RequestCancellation(new DirectManeuverStopRequest(
            identity, "cancel-request-01", DirectManeuverStopKind.Cancellation, tickTime), tickTime);
        Check(cancelAcceptance.Accepted && cancelAcceptance.Identity.Matches(identity) &&
              cancelAcceptance.RequestId == "cancel-request-01",
            "run-bound cancellation request was rejected or returned a mismatched acceptance");
        tickTime = now.AddMilliseconds(50);
        step = cancellable.Step(Evidence(tickTime), Sample(tickTime, 2), tickTime);
        Check(step.Dispatch == DirectManeuverDispatchKind.ControlledStop && !step.CompletionConfirmed,
            "cancellation was reported before controlled-stop acceptance");
        Ack(cancellable, step);
        for (var tick = 3; tick <= 5; tick++)
        {
            tickTime = now.AddMilliseconds(tick * 50);
            step = cancellable.Step(Evidence(tickTime), Sample(tickTime, tick), tickTime);
        }
        Check(step.Outcome == DirectManeuverOutcome.Cancelled && step.StationarySampleCount == 3 &&
              !step.CompletionConfirmed && step.TerminalReceipt != null &&
              step.TerminalReceipt.StationaryConfirmed && step.TerminalReceipt.Identity.Matches(identity) &&
              step.TerminalReceipt.RequestId == "cancel-request-01",
            "cancellation did not wait for stop acceptance and three stationary samples");

        var emergency = Executor(forwardPlan, now);
        tickTime = now;
        step = emergency.Start(Evidence(now), Sample(now, 1), now);
        Ack(emergency, step);
        var emergencyAcceptance = emergency.RequestEmergencyStop(new DirectManeuverStopRequest(
            identity, "emergency-request-01", DirectManeuverStopKind.EmergencyStop, now), now);
        Check(emergencyAcceptance.Accepted && emergencyAcceptance.Identity.Matches(identity),
            "run-bound emergency-stop request was rejected");
        tickTime = now.AddMilliseconds(50);
        step = emergency.Step(Evidence(tickTime), Sample(tickTime, 2), tickTime);
        Check(step.Dispatch == DirectManeuverDispatchKind.ControlledStop,
            "emergency stop did not request the controlled-stop command");
        Ack(emergency, step);
        for (var tick = 3; tick <= 5; tick++)
        {
            tickTime = now.AddMilliseconds(tick * 50);
            step = emergency.Step(Evidence(tickTime), Sample(tickTime, tick), tickTime);
        }
        Check(step.Outcome == DirectManeuverOutcome.EmergencyStopped && step.StationarySampleCount == 3 &&
              step.TerminalReceipt != null && step.TerminalReceipt.StationaryConfirmed &&
              step.TerminalReceipt.RequestId == "emergency-request-01",
            "emergency-stop completion was not held for fresh stationary telemetry");

        var noisyStop = Executor(forwardPlan, now);
        tickTime = now;
        step = noisyStop.Start(Evidence(tickTime), Sample(tickTime, 1), tickTime);
        Ack(noisyStop, step);
        var noisyStopAcceptance = noisyStop.RequestCancellation(new DirectManeuverStopRequest(
            identity, "noise-stop-01", DirectManeuverStopKind.Cancellation, tickTime), tickTime);
        Check(noisyStopAcceptance.Accepted, "stationary-noise stop request was rejected");
        tickTime = now.AddMilliseconds(50);
        step = noisyStop.Step(Evidence(tickTime), Sample(tickTime, 2, x: 0.001, speed: 0.054), tickTime);
        Check(step.Dispatch == DirectManeuverDispatchKind.ControlledStop && Ack(noisyStop, step),
            "stationary-noise stop command was not explicitly accepted");
        var noisySpeeds = new[] { 0.052, 0.055, 0.053 };
        for (var i = 0; i < noisySpeeds.Length; i++)
        {
            tickTime = now.AddMilliseconds((i + 3) * 50);
            step = noisyStop.Step(Evidence(tickTime),
                Sample(tickTime, i + 3, x: 0.002 + i * 0.001, speed: noisySpeeds[i]), tickTime);
        }
        Check(step.Outcome == DirectManeuverOutcome.Cancelled && step.TerminalReceipt != null &&
              step.TerminalReceipt.StationaryConfirmed && step.StationarySampleCount == 3,
            "executor did not issue a verified stationary receipt for low-speed jitter with bounded total drift");

        var explicitStopPlan = Plan(PreflightRunId, fixture, PreflightOperationId,
            grid, controller, new[] {
                new DirectManeuverSegment(DirectManeuverKind.Stop, 0, 0, 20),
                new DirectManeuverSegment(DirectManeuverKind.StationaryHold, 0, 0, 20)
            });
        var explicitStop = Executor(explicitStopPlan, now);
        step = explicitStop.Start(Evidence(now), Sample(now, 1, speed: 0.1), now);
        Check(step.Dispatch == DirectManeuverDispatchKind.ControlledStop && step.Command.Propulsion == 0,
            "explicit stop segment did not dispatch zero wheel input through the controlled-stop gate");

        var timeoutPlan = Plan(PreflightRunId, fixture, PreflightOperationId,
            grid, controller, new[] {
                new DirectManeuverSegment(DirectManeuverKind.ForwardDistance, 1, 0.25, 2),
                new DirectManeuverSegment(DirectManeuverKind.StationaryHold, 0, 0, 20)
            });
        var timeout = Executor(timeoutPlan, now);
        tickTime = now;
        step = timeout.Start(Evidence(now), Sample(now, 1), now);
        Ack(timeout, step);
        tickTime = now.AddMilliseconds(50);
        step = timeout.Step(Evidence(tickTime), Sample(tickTime, 3), tickTime);
        Check(step.Dispatch == DirectManeuverDispatchKind.ControlledStop,
            "segment timeout did not request a bounded stop");
        Ack(timeout, step);
        for (var tick = 4; tick <= 6; tick++)
        {
            tickTime = now.AddMilliseconds(tick * 50);
            step = timeout.Step(Evidence(tickTime), Sample(tickTime, tick), tickTime);
        }
        Check(step.Outcome == DirectManeuverOutcome.TimedOut && step.StationarySampleCount == 3 &&
              !step.CompletionConfirmed && step.TerminalReceipt != null &&
              step.TerminalReceipt.StationaryConfirmed,
            "timeout became terminal before stop confirmation or lost its terminal status");

        var frozenTick = Executor(forwardPlan, now);
        tickTime = now;
        step = frozenTick.Start(Evidence(now), Sample(now, 20), now);
        Ack(frozenTick, step);
        tickTime = now.AddMilliseconds(50);
        step = frozenTick.Step(Evidence(tickTime), Sample(tickTime, 20, speed: 0.251, x: 0.1), tickTime);
        Check(step.Dispatch == DirectManeuverDispatchKind.ControlledStop &&
              step.Outcome == DirectManeuverOutcome.StopRequested,
            "a repeated simulation tick with a new host sample bypassed the strict speed guard");
        Check(Ack(frozenTick, step), "frozen-tick safety stop was not acknowledged");
        for (var i = 1; i <= 52; i++)
        {
            tickTime = now.AddMilliseconds(50 + i * 100);
            step = frozenTick.Step(Evidence(tickTime), Sample(tickTime, 20, x: 0.1, speed: 0.251), tickTime);
        }
        Check(step.TerminalReceipt != null && !step.TerminalReceipt.StationaryConfirmed &&
              step.TerminalReceipt.StationarySamples == 0 && step.TerminalReceipt.ServerTerminationRequired &&
              step.TerminalReceipt.NeutralizationConfirmed &&
              step.TerminalReceipt.FinalHostSampleSequence == 20 &&
              step.TerminalReceipt.FinalModSimulationTick == -1,
            "stop timeout without stationary evidence did not require explicit server termination");

        var deadlineJoin = Executor(timeoutPlan, now);
        tickTime = now;
        step = deadlineJoin.Start(Evidence(now), Sample(now, 1), now);
        Ack(deadlineJoin, step);
        tickTime = now.AddMilliseconds(50);
        step = deadlineJoin.Step(Evidence(tickTime), Sample(tickTime, 3), tickTime);
        Check(step.Outcome == DirectManeuverOutcome.StopRequested &&
              step.Dispatch == DirectManeuverDispatchKind.ControlledStop,
            "segment deadline did not start the executor's controlled stop");
        var deadlineIdentity = OperationIdentity(PreflightRunId, fixture,
            PreflightOperationId, grid, controller);
        var externalDeadline = new DirectManeuverStopRequest(deadlineIdentity,
            "deadline-join-external-01", DirectManeuverStopKind.EmergencyStop, tickTime);
        var joined = deadlineJoin.RequestEmergencyStop(externalDeadline, tickTime);
        Check(joined.Accepted && joined.RequestId == externalDeadline.RequestId,
            "operation-scoped external deadline could not join an executor stop already in progress");
        Check(Ack(deadlineJoin, step), "joined deadline stop was not acknowledged");
        for (var tick = 4; tick <= 6; tick++)
        {
            tickTime = now.AddMilliseconds(tick * 50);
            step = deadlineJoin.Step(Evidence(tickTime), Sample(tickTime, tick), tickTime);
        }
        Check(step.Outcome == DirectManeuverOutcome.TimedOut && step.TerminalReceipt != null &&
              step.TerminalReceipt.StationaryConfirmed &&
              step.TerminalReceipt.RequestId == externalDeadline.RequestId,
            "deadline join lost the original timeout outcome, exact external request ID, or stationary confirmation");

        var unacknowledged = Executor(timeoutPlan, now);
        unacknowledged.Start(Evidence(now), Sample(now, 10), now);
        tickTime = now.AddMilliseconds(50);
        step = unacknowledged.Step(Evidence(tickTime), Sample(tickTime, 12), tickTime);
        Check(step.Dispatch == DirectManeuverDispatchKind.ControlledStop &&
              step.Outcome == DirectManeuverOutcome.StopRequested,
            "missing command acceptance escaped the segment tick deadline without requesting stop");
        Check(Ack(unacknowledged, step), "timeout stop acceptance was not recorded");

        var partiallyApplied = Executor(timeoutPlan, now);
        tickTime = now;
        step = partiallyApplied.Start(Evidence(now), Sample(now, 1), now);
        Check(partiallyApplied.RecordAcceptedChannelWrite(step.Command.CommandId, 9001,
                  DirectManeuverActuatorChannel.SteeringOverride, 0, step.Command.Steering,
                  step.Command.Steering, tickTime) == DirectManeuverLeaseWriteResult.Accepted &&
              partiallyApplied.AcknowledgeCommand(new DirectManeuverCommandAcceptance(identity,
                  step.Command.CommandId, true, tickTime)) &&
              partiallyApplied.Outcome == DirectManeuverOutcome.StopRequested,
            "executor accepted a command whose propulsion channel had no verified per-channel write receipt");
        tickTime = now.AddMilliseconds(50);
        step = partiallyApplied.Step(Evidence(tickTime), Sample(tickTime, 2), tickTime);
        Check(step.Dispatch == DirectManeuverDispatchKind.ControlledStop,
            "partially applied command did not move into stop handling");

        var cancelTimeout = Executor(forwardPlan, now);
        tickTime = now;
        step = cancelTimeout.Start(Evidence(now), Sample(now, 1), now);
        Ack(cancelTimeout, step);
        var cancelTimedAcceptance = cancelTimeout.RequestCancellation(new DirectManeuverStopRequest(
            identity, "cancel-timeout-01", DirectManeuverStopKind.Cancellation, tickTime), tickTime);
        Check(cancelTimedAcceptance.Accepted, "cancellation-timeout request was not accepted");
        for (var tick = 2; tick <= DirectManeuverPlan.MaximumStopConfirmationTicks + 1; tick++)
        {
            tickTime = now.AddMilliseconds(tick * 50);
            step = cancelTimeout.Step(Evidence(tickTime), Sample(tickTime, tick), tickTime);
        }
        Check(step.Outcome == DirectManeuverOutcome.Failed && step.TerminalReceipt != null &&
              !step.TerminalReceipt.StationaryConfirmed && step.TerminalReceipt.RequestId == "cancel-timeout-01",
            "cancellation timeout produced no run-bound unconfirmed terminal receipt");

        var blocked = new DirectManeuverExecutor(forwardPlan, null);
        step = blocked.Start(Evidence(now, safeStop: false), Sample(now, 1), now);
        Check(step.Outcome == DirectManeuverOutcome.PreflightBlocked &&
              step.Dispatch == DirectManeuverDispatchKind.None,
            "missing safe controlled-stop proof authorized wheel dispatch");
        var staleSensor = Executor(forwardPlan, now);
        tickTime = now;
        step = staleSensor.Start(Evidence(now), Sample(now, 1), now);
        Ack(staleSensor, step);
        tickTime = now.AddMilliseconds(50);
        step = staleSensor.Step(Evidence(tickTime), Sample(tickTime, 2, sensorValid: false), tickTime);
        Check(step.Dispatch == DirectManeuverDispatchKind.ControlledStop,
            "stale/invalid sensor evidence did not fail into the owned controlled-stop path");
        var wrongIdentity = Executor(forwardPlan, now);
        step = wrongIdentity.Start(Evidence(now), Sample(now, 1), now);
        Ack(wrongIdentity, step);
        tickTime = now.AddMilliseconds(50);
        step = wrongIdentity.Step(Evidence(tickTime), Sample(tickTime, 2, sampleGrid: grid + 1), tickTime);
        Check(step.Dispatch == DirectManeuverDispatchKind.None && step.Outcome == DirectManeuverOutcome.Failed,
            "grid identity change continued dispatch to a different entity");

    }

    private static void DirectManeuverFeedbackAndStationaryWindowAreBounded()
    {
        Check(Math.Abs(DirectManeuverExecutor.PropulsionForSpeed(0.25, 0) - 0.1) < 0.000001 &&
              Math.Abs(DirectManeuverExecutor.PropulsionForSpeed(0.25, 0.05) - 0.1) < 0.000001 &&
              DirectManeuverExecutor.PropulsionForSpeed(0.25, 0.125) > 0 &&
              DirectManeuverExecutor.PropulsionForSpeed(0.25, 0.125) < 0.1 &&
              DirectManeuverExecutor.PropulsionForSpeed(0.25, 0.20) <
                  DirectManeuverExecutor.PropulsionForSpeed(0.25, 0.125) &&
              DirectManeuverExecutor.PropulsionForSpeed(0.25, -0.125) == 0.1 &&
              DirectManeuverExecutor.PropulsionForSpeed(0.25, -0.20) == 0.1 &&
              DirectManeuverExecutor.PropulsionForSpeed(0.25, 0.25) == 0 &&
              DirectManeuverExecutor.PropulsionForSpeed(0.25, 0.304) == 0 &&
              DirectManeuverExecutor.PropulsionForSpeed(0.25, double.NaN) == 0,
            "propulsion input did not taper only as speed toward the commanded direction approaches its cap");

        var lifecycle = new DirectManeuverLifecycle(true);
        Check(lifecycle.Start() && lifecycle.RequestCancellation(),
            "stationary-window lifecycle setup failed");
        Check(!lifecycle.ObserveStationaryTelemetry(10, 0.054, 0.001) &&
              !lifecycle.ObserveStationaryTelemetry(20, 0.052, 0.001) &&
              lifecycle.ObserveStationaryTelemetry(30, 0.055, 0.001) &&
              lifecycle.Outcome == DirectManeuverOutcome.Cancelled,
            "bounded velocity jitter with stable cumulative position did not confirm a stationary hold");

        var drifting = new DirectManeuverLifecycle(true);
        Check(drifting.Start() && drifting.RequestCancellation(),
            "drift-window lifecycle setup failed");
        Check(!drifting.ObserveStationaryTelemetry(10, 0.054, 0.009) &&
              !drifting.ObserveStationaryTelemetry(20, 0.052, 0.009) &&
              !drifting.ObserveStationaryTelemetry(30, 0.055, 0.009) &&
              drifting.Outcome == DirectManeuverOutcome.StopRequested,
            "cumulative position drift was hidden by individually small sample deltas");
    }

    private static void DirectManeuverOperationLeaseIsFailClosed()
    {
        var now = DateTime.UtcNow;
        var run = PreflightRunId;
        const string fixture = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const long grid = 456, controller = 123, wheel = 9001;
        var identity = OperationIdentity(run, fixture, PreflightOperationId, grid, controller);
        var plan = Plan(run, fixture, PreflightOperationId, grid, controller,
            new[] {
                new DirectManeuverSegment(DirectManeuverKind.ForwardDistance, 0.25, 0.25, 20),
                new DirectManeuverSegment(DirectManeuverKind.StationaryHold, 0, 0, 20)
            });
        var evidence = Evidence(run, fixture, PreflightOperationId, grid, controller, grid,
            4.0, now, true, now, true, now, true, true, true, true, true, true);
        DirectManeuverBrakeState Brakes(DateTime at) => new DirectManeuverBrakeState(true, false, true, false, at);
        IList<DirectManeuverWheelState> State(double steering, double propulsion, DateTime at, bool brake = false) =>
            new[] { new DirectManeuverWheelState(wheel, steering, propulsion, true, brake, at) };
        DirectManeuverOperationLease Acquire(DateTime at) =>
            DirectManeuverOperationLease.TryAcquire(plan, evidence, State(0, 0, at), Brakes(at),
                new DirectManeuverOperationLeaseAuthority(), at, TimeSpan.FromSeconds(1), out var lease, out _) ? lease : null;

        var sharedAuthority = new DirectManeuverOperationLeaseAuthority();
        Check(DirectManeuverOperationLease.TryAcquire(plan, evidence, State(0, 0, now), Brakes(now),
                  sharedAuthority, now, TimeSpan.FromSeconds(1), out var firstGridLease, out _) && firstGridLease != null &&
              !DirectManeuverOperationLease.TryAcquire(plan, evidence, State(0, 0, now), Brakes(now),
                  sharedAuthority, now, TimeSpan.FromSeconds(1), out _, out _),
            "two competing operations acquired the same grid lease from one authority");

        var competitor = Acquire(now);
        Check(competitor != null && competitor.RecordAcceptedChannelWrite(identity, wheel,
                  DirectManeuverActuatorChannel.SteeringOverride, 0, 0.4, 0.4, now) ==
              DirectManeuverLeaseWriteResult.Accepted &&
              competitor.RecordAcceptedChannelWrite(identity, wheel,
                  DirectManeuverActuatorChannel.PropulsionOverride, 0, 0.3, 0.3, now) ==
              DirectManeuverLeaseWriteResult.Accepted,
            "per-channel accepted writes did not establish lease ownership");
        Check(competitor.Heartbeat(identity, State(0.8, 0.3, now.AddMilliseconds(10)), Brakes(now.AddMilliseconds(10)), now.AddMilliseconds(10)) ==
              DirectManeuverLeaseCheck.ExternalChange && !competitor.IsActive,
            "competing writer change did not revoke dispatch authority");
        Check(competitor.TryCreateCleanup(identity, State(0.8, 0.3, now.AddMilliseconds(10)), now.AddMilliseconds(10), out var competitorCleanup) &&
              competitorCleanup.Count == 1 &&
              competitorCleanup[0].Channel == DirectManeuverActuatorChannel.PropulsionOverride &&
              competitorCleanup[0].ExpectedOwnedValue == 0.3 && competitorCleanup[0].OriginalValue == 0,
            "cleanup did not preserve competing steering while releasing the still-owned propulsion value");

        var stale = Acquire(now);
        Check(stale != null && !stale.IsDispatchAuthorized(identity, now.AddSeconds(2)) &&
              stale.RecordAcceptedChannelWrite(identity, wheel, DirectManeuverActuatorChannel.SteeringOverride,
                  0, 0.2, 0.2, now.AddSeconds(2)) == DirectManeuverLeaseWriteResult.LeaseExpired,
            "expired operation lease still authorized a channel write");

        var wrongIdentity = Acquire(now);
        var wrong = OperationIdentity(run, fixture, "ffffffffffffffffffffffffffffffff", grid, controller);
        Check(wrongIdentity.RecordAcceptedChannelWrite(wrong, wheel,
                  DirectManeuverActuatorChannel.SteeringOverride, 0, 0.2, 0.2, now) ==
              DirectManeuverLeaseWriteResult.IdentityMismatch &&
              wrongIdentity.Heartbeat(wrong, State(0, 0, now), Brakes(now), now) == DirectManeuverLeaseCheck.IdentityMismatch,
            "lease accepted a write or heartbeat for another operation identity");
        var wrongCoreIdentity = new DirectManeuverOperationIdentity(run, fixture,
            PreflightOperationId, grid, controller, TestAgentCoreId + 1);
        Check(wrongIdentity.Heartbeat(wrongCoreIdentity, State(0, 0, now), Brakes(now), now) ==
              DirectManeuverLeaseCheck.IdentityMismatch,
            "lease accepted a heartbeat bound to a different Agent Core");
        var wrongGridIdentity = OperationIdentity(run, fixture, PreflightOperationId, grid + 1, controller);
        var wrongControllerIdentity = OperationIdentity(run, fixture, PreflightOperationId, grid, controller + 1);
        Check(wrongIdentity.RecordAcceptedChannelWrite(wrongGridIdentity, wheel,
                  DirectManeuverActuatorChannel.SteeringOverride, 0, 0.2, 0.2, now) ==
              DirectManeuverLeaseWriteResult.IdentityMismatch &&
              wrongIdentity.RecordAcceptedChannelWrite(wrongControllerIdentity, wheel,
                  DirectManeuverActuatorChannel.SteeringOverride, 0, 0.2, 0.2, now) ==
              DirectManeuverLeaseWriteResult.IdentityMismatch &&
              wrongIdentity.Heartbeat(wrongGridIdentity, State(0, 0, now), Brakes(now), now) ==
              DirectManeuverLeaseCheck.IdentityMismatch &&
              wrongIdentity.Heartbeat(wrongControllerIdentity, State(0, 0, now), Brakes(now), now) ==
              DirectManeuverLeaseCheck.IdentityMismatch,
            "lease accepted a different vehicle grid or controller identity");

        var takeoverAuthority = new DirectManeuverOperationLeaseAuthority();
        var shortLeaseDuration = TimeSpan.FromMilliseconds(100);
        Check(DirectManeuverOperationLease.TryAcquire(plan, evidence, State(0, 0, now), Brakes(now),
                  takeoverAuthority, now, shortLeaseDuration, out var expiredLease, out _) &&
              expiredLease != null,
            "takeover test could not establish the first grid claim");
        var takeoverTime = now.AddMilliseconds(101);
        const string takeoverOperationId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        var takeoverPlan = Plan(run, fixture, takeoverOperationId, grid, controller,
            new[] { new DirectManeuverSegment(DirectManeuverKind.StationaryHold, 0, 0, 20) });
        var takeoverEvidence = Evidence(run, fixture, takeoverOperationId, grid, controller, grid,
            4.0, takeoverTime, true, takeoverTime, true, takeoverTime, true, true, true, true, true, true);
        Check(DirectManeuverOperationLease.TryAcquire(takeoverPlan, takeoverEvidence,
                  State(0, 0, takeoverTime), Brakes(takeoverTime), takeoverAuthority,
                  takeoverTime, TimeSpan.FromSeconds(1), out var takeoverLease, out _) &&
              takeoverLease != null && !expiredLease.IsDispatchAuthorized(identity, takeoverTime),
            "expired grid lease was not revoked before a new operation took ownership");
        Check(expiredLease.TryCreateCleanup(identity, State(0, 0, takeoverTime), takeoverTime, out _) &&
              takeoverLease.IsDispatchAuthorized(takeoverLease.Identity, takeoverTime.AddMilliseconds(1)),
            "stale lease cleanup revoked or disabled the newer operation's authority token");
        var competingPlan = Plan(run, fixture, "cccccccccccccccccccccccccccccccc", grid, controller,
            new[] { new DirectManeuverSegment(DirectManeuverKind.StationaryHold, 0, 0, 20) });
        var competingEvidence = Evidence(run, fixture, competingPlan.OperationId, grid, controller, grid,
            4.0, takeoverTime, true, takeoverTime, true, takeoverTime, true, true, true, true, true, true);
        Check(!DirectManeuverOperationLease.TryAcquire(competingPlan, competingEvidence,
                  State(0, 0, takeoverTime), Brakes(takeoverTime), takeoverAuthority,
                  takeoverTime, TimeSpan.FromSeconds(1), out _, out _) &&
              takeoverLease.IsDispatchAuthorized(takeoverLease.Identity, takeoverTime.AddMilliseconds(2)),
            "a concurrent takeover acquired the grid while the current token remained valid");

        var partial = Acquire(now);
        Check(partial.RecordAcceptedChannelWrite(identity, wheel, DirectManeuverActuatorChannel.SteeringOverride,
                  0, 0.5, 0.5, now) == DirectManeuverLeaseWriteResult.Accepted &&
              partial.RecordAcceptedChannelWrite(identity, wheel, DirectManeuverActuatorChannel.PropulsionOverride,
                  0, 0.4, 0.1, now) == DirectManeuverLeaseWriteResult.ExternalChange && partial.SafetyLost,
            "partial write/readback mismatch did not fail closed");
        Check(partial.TryCreateCleanup(identity, State(0.5, 0.1, now), now, out var partialCleanup) &&
              partialCleanup.Count == 1 && partialCleanup[0].Channel == DirectManeuverActuatorChannel.SteeringOverride,
            "partial-write cleanup touched an unaccepted propulsion value");

        var changed = Acquire(now);
        Check(changed.RecordAcceptedChannelWrite(identity, wheel, DirectManeuverActuatorChannel.SteeringOverride,
                  0, 0.6, 0.6, now) == DirectManeuverLeaseWriteResult.Accepted &&
              changed.TryCreateCleanup(identity, State(-0.2, 0, now), now, out var changedCleanup) && changedCleanup.Count == 0,
            "cleanup overwrote a value changed externally after the operation write");

        var badBrake = Acquire(now);
        Check(badBrake.Heartbeat(identity, State(0, 0, now.AddMilliseconds(1), brake: true), Brakes(now.AddMilliseconds(1)), now.AddMilliseconds(1)) ==
              DirectManeuverLeaseCheck.ExternalChange,
            "active wheel brake change did not invalidate the operation lease");
        Check(!DirectManeuverOperationLease.TryAcquire(plan, evidence, State(0, 0, now),
                  new DirectManeuverBrakeState(false, false, true, false, now),
                  new DirectManeuverOperationLeaseAuthority(), now,
                  TimeSpan.FromSeconds(1), out _, out _),
            "lease acquisition accepted unknown controller/grid brake state");
    }

    private static void DirectManeuverBrokerContractIsFailClosed()
    {
        var now = DateTime.UtcNow;
        var run = PreflightRunId;
        const string fixture = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var operationId = PreflightOperationId;
        const long grid = 456, controller = 123, wheel = 9001;
        var identity = OperationIdentity(run, fixture, operationId, grid, controller);
        var plan = Plan(run, fixture, operationId, grid, controller, new[] {
            new DirectManeuverSegment(DirectManeuverKind.ForwardDistance, 0.25, 0.25, 20),
            new DirectManeuverSegment(DirectManeuverKind.StationaryHold, 0, 0, 20) });
        var evidence = Evidence(run, fixture, operationId, grid, controller, grid,
            4.0, now, true, now, true, now, true, true, true, true, true, true);
        IList<DirectManeuverWheelState> states = new[] { new DirectManeuverWheelState(wheel, 0, 0, true, false, now) };
        var brakes = new DirectManeuverBrakeState(true, false, true, false, now);
        var unknown = new DirectManeuverActuatorBroker(new[] { "host", "mod" }, DirectManeuverExternalWriterState.Unknown);
        Check(!unknown.TryAcquire(plan, evidence, states, brakes, now, TimeSpan.FromSeconds(1), true, out _) &&
              !unknown.TryAcquire(plan, evidence, states, brakes, now, TimeSpan.FromSeconds(1), false, out _),
            "broker acquired while external-writer state or runtime capability was unavailable");
        Check(DirectManeuverBrokerRoutePolicy.Resolve("direct-maneuver", false, true) == DirectManeuverBrokerRoute.DirectBroker &&
              DirectManeuverBrokerRoutePolicy.Resolve("direct-maneuver", false, false) == DirectManeuverBrokerRoute.Reject &&
              DirectManeuverBrokerRoutePolicy.Resolve("direct-maneuver", true, false) != DirectManeuverBrokerRoute.LegacyCommand &&
              DirectManeuverBrokerRoutePolicy.Resolve("drive-distance", true, true) == DirectManeuverBrokerRoute.Reject,
            "direct maneuver could reach generic drive fallback or another command entered during a lease");
        var broker = new DirectManeuverActuatorBroker(new[] { "host", "mod" },
            DirectManeuverExternalWriterState.LocalParticipatingWriterAuditPassed);
        Check(!broker.SamplingRequired &&
              !DirectManeuverHostSamplingPolicy.ShouldContinue(true, false, false, false) &&
              DirectManeuverHostSamplingPolicy.ShouldContinue(true, false, true, false) &&
              DirectManeuverHostSamplingPolicy.ShouldContinue(true, false, false, true) &&
              DirectManeuverHostSamplingPolicy.ShouldContinue(true, true, false, false),
            "terminal sampling policy did not distinguish a read-only preflight from an active command, lease, or unresolved stop");
        Check(broker.TryAcquire(plan, evidence, states, brakes, now, TimeSpan.FromSeconds(1), true, out _),
            "offline test contract did not acquire with explicit scoped writer attestation");
        Check(broker.SamplingRequired,
            "active actuator lease did not keep host sampling required");
        var staleRunIdentity = OperationIdentity(
            "ffffffffffffffffffffffffffffffff", fixture, operationId, grid, controller);
        Check(!broker.TryRunParticipatingTransaction("host", staleRunIdentity, now, () => true),
            "stale run identity reached a participating writer transaction");
        var inside = 0;
        var maximumInside = 0;
        Action<string> transaction = writer => broker.TryRunParticipatingTransaction(writer, identity, now, () => {
            var concurrent = System.Threading.Interlocked.Increment(ref inside);
            int prior;
            do { prior = maximumInside; if (prior >= concurrent) break; }
            while (System.Threading.Interlocked.CompareExchange(ref maximumInside, concurrent, prior) != prior);
            System.Threading.Thread.Sleep(20);
            System.Threading.Interlocked.Decrement(ref inside);
            return true;
        });
        System.Threading.Tasks.Parallel.Invoke(() => transaction("host"), () => transaction("mod"));
        Check(maximumInside == 1, "participating broker transactions overlapped instead of serializing");
        Check(broker.TryWrite("host", identity, wheel, DirectManeuverActuatorChannel.SteeringOverride,
                  0, 0.2, 0.2, now) == DirectManeuverLeaseWriteResult.Accepted &&
              broker.TryWrite("mod", identity, wheel, DirectManeuverActuatorChannel.SteeringOverride,
                  0.2, 0.3, 0.3, now) == DirectManeuverLeaseWriteResult.Accepted &&
              broker.TryWrite("unregistered", identity, wheel, DirectManeuverActuatorChannel.PropulsionOverride,
                  0, 0.2, 0.2, now) != DirectManeuverLeaseWriteResult.Accepted,
            "participating writers did not share one serialized ownership record");
        Check(!broker.TryRunParticipatingTransaction("unregistered", identity, now, () => true),
            "unregistered writer entered the participating transaction lock");
        var request = new DirectManeuverStopRequest(identity, "stop-timeout-01",
            DirectManeuverStopKind.EmergencyStop, now);
        var timeout = broker.CompleteStop(request, true, false, states, brakes, now.AddSeconds(2));
        Check(!timeout.Succeeded && timeout.Identity.Matches(identity) &&
              timeout.RequestId == request.RequestId && timeout.Reason.Contains("timed out"),
            "stop timeout did not produce an identity-bound failed receipt");
        Check(broker.TryWrite("host", identity, wheel, DirectManeuverActuatorChannel.SteeringOverride,
                  0.3, 0.4, 0.4, now.AddSeconds(3)) != DirectManeuverLeaseWriteResult.Accepted,
            "expired or stop-pending lease permitted a non-neutral participating writer call");

        var pendingBroker = new DirectManeuverActuatorBroker(new[] { "host", "mod" },
            DirectManeuverExternalWriterState.LocalParticipatingWriterAuditPassed);
        Check(pendingBroker.TryAcquire(plan, evidence, states, brakes, now, TimeSpan.FromSeconds(1), true, out _) &&
              pendingBroker.RequestStop(new DirectManeuverStopRequest(identity, "stop-pending-01",
                  DirectManeuverStopKind.EmergencyStop, now)),
            "operation-scoped stop request was not registered with its exact operation identity");
        Check(!pendingBroker.TryRunParticipatingTransaction("host", identity, now, () => true) &&
              pendingBroker.TryRunParticipatingTransaction("host", identity, now, () => true, controlledStop: true) &&
              pendingBroker.TryWrite("host", identity, wheel, DirectManeuverActuatorChannel.PropulsionOverride,
                  0, 0.2, 0.2, now) == DirectManeuverLeaseWriteResult.IdentityMismatch,
            "pending stop did not block new movement while allowing only the controlled-stop transaction");
        Check(!pendingBroker.TryRunParticipatingTransaction("host", identity, now,
                  () => false, controlledStop: true) && pendingBroker.SamplingRequired &&
              pendingBroker.StopPending &&
              !pendingBroker.TryRunParticipatingTransaction("host", identity, now, () => true),
            "failed or delayed stop acknowledgement released sampling or reopened propulsion");
        Check(!pendingBroker.CompleteStop(
                  new DirectManeuverStopRequest(identity, "stop-pending-01", DirectManeuverStopKind.EmergencyStop, now),
                  false, true, states, brakes, now.AddMilliseconds(90)).Succeeded && pendingBroker.SamplingRequired,
            "stationary telemetry alone released a lease without neutral readback and active brakes");
        var pendingTimeout = pendingBroker.CompleteStop(
            new DirectManeuverStopRequest(identity, "stop-pending-01", DirectManeuverStopKind.EmergencyStop, now),
            true, false, states, brakes, now.AddMilliseconds(100));
        Check(!pendingTimeout.Succeeded && pendingBroker.LeaseActive(now.AddMilliseconds(120)) &&
              pendingBroker.SamplingRequired && pendingBroker.StopPending &&
              !pendingBroker.TryRunParticipatingTransaction("host", identity, now.AddMilliseconds(120), () => true),
            "timed-out stop did not retain the active ownership claim while preventing new movement");

        var stopBroker = new DirectManeuverActuatorBroker(new[] { "host", "mod" },
            DirectManeuverExternalWriterState.LocalParticipatingWriterAuditPassed);
        Check(stopBroker.TryAcquire(plan, evidence, states, brakes, now, TimeSpan.FromSeconds(1), true, out _),
            "stop-release contract could not acquire a fresh operation");
        var mismatchedStop = new DirectManeuverStopRequest(staleRunIdentity, "stop-wrong-run",
            DirectManeuverStopKind.Cancellation, now);
        IList<DirectManeuverWheelState> stoppedStates = new[] { new DirectManeuverWheelState(wheel, 0, 0, true, true, now.AddMilliseconds(50)) };
        var stoppedBrakes = new DirectManeuverBrakeState(true, true, true, true, now.AddMilliseconds(50));
        var mismatchReceipt = stopBroker.CompleteStop(mismatchedStop, false, true, stoppedStates, stoppedBrakes, now.AddMilliseconds(50));
        Check(!mismatchReceipt.Succeeded && mismatchReceipt.Identity.Matches(staleRunIdentity) &&
              stopBroker.LeaseActive(now.AddMilliseconds(60)),
            "mismatched stop receipt released the active operation");
        Check(stopBroker.TryWrite("host", identity, 0, DirectManeuverActuatorChannel.GridBrake,
                  0, 1, 1, now.AddMilliseconds(70)) == DirectManeuverLeaseWriteResult.Accepted &&
              stopBroker.TryWrite("host", identity, 0, DirectManeuverActuatorChannel.HandBrake,
                  0, 1, 1, now.AddMilliseconds(80)) == DirectManeuverLeaseWriteResult.Accepted,
            "broker did not record controlled brake writes for stop confirmation");
        var successfulStop = new DirectManeuverStopRequest(identity, "stop-confirmed-01",
            DirectManeuverStopKind.Cancellation, now.AddMilliseconds(70));
        Check(stopBroker.RequestStop(successfulStop), "confirmed stop request was not bound before brake writes");
        stoppedStates = new[] { new DirectManeuverWheelState(wheel, 0, 0, true, true, now.AddMilliseconds(100)) };
        stoppedBrakes = new DirectManeuverBrakeState(true, true, true, true, now.AddMilliseconds(100));
        var missingStationary = stopBroker.CompleteStop(successfulStop, false, false,
            stoppedStates, stoppedBrakes, now.AddMilliseconds(90));
        var nonNeutralReadback = stopBroker.CompleteStop(successfulStop, false, true,
            new[] { new DirectManeuverWheelState(wheel, 0.2, 0, true, true, now.AddMilliseconds(95)) },
            stoppedBrakes, now.AddMilliseconds(95));
        Check(!missingStationary.Succeeded && !nonNeutralReadback.Succeeded &&
              stopBroker.SamplingRequired && stopBroker.StopPending,
            "stop receipt released ownership without stationary evidence and neutral channel readback");
        var successReceipt = stopBroker.CompleteStop(successfulStop, false, true, stoppedStates, stoppedBrakes, now.AddMilliseconds(100));
        Check(successReceipt.Succeeded && successReceipt.Identity.Matches(identity) &&
              successReceipt.RequestId == successfulStop.RequestId &&
              successReceipt.Reason == "stop confirmed" &&
              !stopBroker.LeaseActive(now.AddMilliseconds(110)) && !stopBroker.SamplingRequired,
            "confirmed operation-scoped stop did not release its lease with a verifiable receipt");
    }

    private static void DirectManeuverStopCapabilityFencesExpiredMovement()
    {
        var now = DateTime.UtcNow;
        var run = PreflightRunId;
        const string fixture = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var operation = PreflightOperationId;
        const long grid = 456, controller = 123, wheel = 9001;
        var identity = OperationIdentity(run, fixture, operation, grid, controller);
        var plan = Plan(run, fixture, operation, grid, controller, new[] {
            new DirectManeuverSegment(DirectManeuverKind.ForwardDistance, 0.25, 0.25, 20),
            new DirectManeuverSegment(DirectManeuverKind.StationaryHold, 0, 0, 20) });
        var evidence = Evidence(run, fixture, operation, grid, controller, grid,
            4.0, now, true, now, true, now, true, true, true, true, true, true);
        var broker = new DirectManeuverActuatorBroker(new[] { "vragecage-server-plugin", "voidwright-mod" },
            DirectManeuverExternalWriterState.LocalParticipatingWriterAuditPassed);
        var original = new[] { new DirectManeuverWheelState(wheel, 0, 0, true, false, now) };
        var initialBrakes = new DirectManeuverBrakeState(true, false, true, false, now);
        Check(broker.TryAcquire(plan, evidence, original, initialBrakes, now,
                  TimeSpan.FromSeconds(1), true, out _),
            "could not acquire movement lease for stop-only capability test");
        Check(broker.TryWrite("vragecage-server-plugin", identity, wheel,
                  DirectManeuverActuatorChannel.PropulsionOverride, 0, 0.25, 0.25, now) ==
              DirectManeuverLeaseWriteResult.Accepted,
            "movement write did not establish the owned pre-stop value");

        var expiredAt = now.AddSeconds(1.1);
        Check(!broker.LeaseActive(expiredAt), "movement authorization did not expire at its deadline");
        var wrongRun = OperationIdentity("ffffffffffffffffffffffffffffffff", fixture, operation, grid, controller);
        var wrongGrid = OperationIdentity(run, fixture, operation, grid + 1, controller);
        var wrongController = OperationIdentity(run, fixture, operation, grid, controller + 1);
        var wrongOperation = OperationIdentity(run, fixture, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", grid, controller);
        foreach (var wrong in new[] { wrongRun, wrongGrid, wrongController, wrongOperation })
            Check(!broker.RequestStop(new DirectManeuverStopRequest(wrong, "wrong-stop", DirectManeuverStopKind.EmergencyStop, expiredAt), out _),
                "stop-only capability was minted for a mismatched operation identity");

        var request = new DirectManeuverStopRequest(identity, "expired-lease-stop",
            DirectManeuverStopKind.EmergencyStop, expiredAt);
        Check(broker.RequestStop(request, out var capability) && capability != null &&
              capability.Identity.Matches(identity) && capability.RequestId == request.RequestId,
            "exact operation did not receive a broker-minted stop-only capability after movement expiry");

        var propulsion = 0.25;
        var steering = 0.0;
        var gridBrake = 0.0;
        var handBrake = 0.0;
        var setterCalls = 0;
        Check(broker.TryWriteStopChannel("unregistered", capability, wheel,
                  DirectManeuverActuatorChannel.PropulsionOverride, 0,
                  () => propulsion, value => { setterCalls++; propulsion = value; }, expiredAt) != DirectManeuverLeaseWriteResult.Accepted &&
              setterCalls == 0,
            "unregistered writer reached the emergency-stop setter funnel");
        Check(broker.TryWriteStopChannel("vragecage-server-plugin", capability, wheel,
                  DirectManeuverActuatorChannel.PropulsionOverride, 0.2,
                  () => propulsion, value => { setterCalls++; propulsion = value; }, expiredAt) == DirectManeuverLeaseWriteResult.InvalidValue &&
              setterCalls == 0,
            "stop-only capability accepted non-neutral propulsion");
        Check(broker.TryWriteStopChannel("vragecage-server-plugin", capability, wheel,
                  DirectManeuverActuatorChannel.SteeringOverride, 0,
                  () => steering, _ => throw new InvalidOperationException("setter failed"), expiredAt) ==
              DirectManeuverLeaseWriteResult.WriteNotApplied && setterCalls == 0,
            "setter exception was reported as a successful stop write");
        Check(broker.TryWriteStopChannel("vragecage-server-plugin", capability, wheel,
                  DirectManeuverActuatorChannel.PropulsionOverride, 0,
                  () => propulsion, _ => { }, expiredAt) == DirectManeuverLeaseWriteResult.WriteNotApplied,
            "failed readback was accepted as a neutralized propulsion channel");

        Check(broker.TryWriteStopChannel("vragecage-server-plugin", capability, wheel,
                  DirectManeuverActuatorChannel.PropulsionOverride, 0,
                  () => propulsion, value => { setterCalls++; propulsion = value; }, expiredAt) == DirectManeuverLeaseWriteResult.Accepted &&
              broker.TryWriteStopChannel("vragecage-server-plugin", capability, wheel,
                  DirectManeuverActuatorChannel.SteeringOverride, 0,
                  () => steering, value => { setterCalls++; steering = value; }, expiredAt) == DirectManeuverLeaseWriteResult.Accepted &&
              broker.TryWriteStopChannel("vragecage-server-plugin", capability, 0,
                  DirectManeuverActuatorChannel.GridBrake, 1,
                  () => gridBrake, value => { setterCalls++; gridBrake = value; }, expiredAt) == DirectManeuverLeaseWriteResult.Accepted &&
              broker.TryWriteStopChannel("vragecage-server-plugin", capability, 0,
                  DirectManeuverActuatorChannel.HandBrake, 1,
                  () => handBrake, value => { setterCalls++; handBrake = value; }, expiredAt) == DirectManeuverLeaseWriteResult.Accepted &&
              setterCalls == 4 && propulsion == 0 && steering == 0 && gridBrake == 1 && handBrake == 1,
            "emergency neutralization/braking did not pass through the capability-checked readback funnel");

        var stoppedStates = new[] { new DirectManeuverWheelState(wheel, steering, propulsion,
            true, true, expiredAt.AddMilliseconds(10)) };
        var stoppedBrakes = new DirectManeuverBrakeState(true, true, true, true, expiredAt.AddMilliseconds(10));
        var missingStationary = broker.CompleteStop(request, false, false, stoppedStates,
            stoppedBrakes, expiredAt.AddMilliseconds(10));
        Check(!missingStationary.Succeeded && broker.StopPending,
            "neutral readback and active brakes released stop capability without stationary proof");
        var receipt = broker.CompleteStop(request, false, true, stoppedStates,
            stoppedBrakes, expiredAt.AddMilliseconds(20));
        Check(receipt.Succeeded && !broker.StopPending && !broker.SamplingRequired,
            "expired movement lease was not released after capability-bound neutralization and stationary proof");
        Check(broker.TryWriteStopChannel("vragecage-server-plugin", capability, wheel,
                  DirectManeuverActuatorChannel.PropulsionOverride, 0,
                  () => propulsion, value => propulsion = value, expiredAt.AddMilliseconds(31)) !=
              DirectManeuverLeaseWriteResult.Accepted,
            "revoked stop capability remained usable after lease release and takeover");

        var takeoverBroker = new DirectManeuverActuatorBroker(new[] { "vragecage-server-plugin" },
            DirectManeuverExternalWriterState.LocalParticipatingWriterAuditPassed);
        Check(takeoverBroker.TryAcquire(plan, evidence, original, initialBrakes, now,
                  TimeSpan.FromSeconds(1), true, out _) && !takeoverBroker.LeaseActive(expiredAt),
            "takeover fixture did not establish an expired first operation");
        var takeoverOperation = "cccccccccccccccccccccccccccccccc";
        var takeoverPlan = Plan(run, fixture, takeoverOperation, grid, controller, new[] {
            new DirectManeuverSegment(DirectManeuverKind.ForwardDistance, 0.25, 0.25, 20),
            new DirectManeuverSegment(DirectManeuverKind.StationaryHold, 0, 0, 20) });
        var takeoverEvidence = Evidence(run, fixture, takeoverOperation, grid, controller, grid,
            4.0, expiredAt, true, expiredAt, true, expiredAt, true, true, true, true, true, true);
        var takeoverStates = new[] { new DirectManeuverWheelState(wheel, 0, 0, true, false, expiredAt) };
        var takeoverBrakes = new DirectManeuverBrakeState(true, false, true, false, expiredAt);
        Check(takeoverBroker.TryAcquire(takeoverPlan, takeoverEvidence, takeoverStates, takeoverBrakes,
                  expiredAt, TimeSpan.FromSeconds(1), true, out _),
            "new operation could not acquire the grid after the movement lease expired");
        Check(!takeoverBroker.RequestStop(request, out _),
            "stale stop request survived takeover by a new operation on the same grid");
    }

    private static void DirectManeuverCapabilityArtifactRoundTripsExactLease()
    {
        var now = DateTime.UtcNow;
        var run = PreflightRunId;
        var fixture = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var operation = PreflightOperationId;
        const long grid = 456, controller = 123, wheel = 9001, camera = 333;
        const long discoveryTick = 12;
        var contextSha = new string('a', 64);
        var challenge = new string('b', 32);
        var session = new string('c', 32);
        var fingerprint = new string('d', 64);
        var identity = OperationIdentity(run, fixture, operation, grid, controller);
        var plan = Plan(run, fixture, operation, grid, controller, new[] {
            new DirectManeuverSegment(DirectManeuverKind.ForwardDistance, 0.25, 0.25, 20),
            new DirectManeuverSegment(DirectManeuverKind.StationaryHold, 0, 0, 20) });
        var evidence = Evidence(run, fixture, operation, grid, controller, grid,
            4.0, now, true, now, true, now, true, true, true, true, true, true);
        var state = new[] { new DirectManeuverWheelState(wheel, 0, 0, true, false, now) };
        var brakes = new DirectManeuverBrakeState(true, false, true, false, now);
        var broker = new DirectManeuverActuatorBroker(new[] { "vragecage-server-plugin" },
            DirectManeuverExternalWriterState.LocalParticipatingWriterAuditPassed);
        Check(broker.TryAcquire(plan, evidence, state, brakes, now, TimeSpan.FromSeconds(1), true, out _),
            "host did not create the exact operation lease");
        var hostToken = broker.GetMovementCapabilityToken(identity, now);
        Check(VoidwrightOperationCapability.IsToken(hostToken) &&
              broker.TryRunCapabilityTransaction("vragecage-server-plugin", identity, hostToken,
                  now, () => true),
            "host-minted capability did not authorize its exact operation");
        Check(!broker.TryRunCapabilityTransaction("vragecage-server-plugin", identity,
                  new string('f', 32), now, () => true) &&
              !broker.TryRunCapabilityTransaction("wrong-writer", identity, hostToken, now, () => true),
            "wrong capability or writer crossed the host dispatch gate");
        Check(!broker.TryAcquire(plan, evidence, state, brakes, now,
                  TimeSpan.FromSeconds(1), true, out _),
            "a competing operation acquired the grid while the capability lease was live");

        var artifact = new VoidwrightOperationCapability
        {
            RunId = run, FixtureId = fixture, OperationId = operation,
            ContextSha256 = contextSha, Challenge = challenge, SessionId = session,
            GridId = grid, ControllerId = controller, AgentCoreId = TestAgentCoreId,
            CameraId = camera, DiscoveryRunId = run, DiscoveryTick = discoveryTick,
            DiscoveryFingerprint = fingerprint, Token = hostToken,
            IssuedUtc = now, ExpiresUtc = now.Add(VoidwrightOperationCapability.MaximumLifetime)
        };
        var serializedCapability = artifact.Format();
        Check(VoidwrightOperationCapability.TryParse(serializedCapability, now, out var parsedCapability) &&
              parsedCapability.Matches(now, run, fixture, operation, contextSha, challenge, session,
                  grid, controller, TestAgentCoreId, camera, run, discoveryTick, fingerprint, hostToken) &&
              !parsedCapability.Matches(now, run, fixture, operation, contextSha, challenge,
                  new string('e', 32), grid, controller, TestAgentCoreId, camera, run, discoveryTick,
                  fingerprint, hostToken) &&
              !parsedCapability.Matches(now, run, fixture, operation, contextSha, challenge, session,
                  grid + 1, controller, TestAgentCoreId, camera, run, discoveryTick, fingerprint, hostToken),
            "world-storage capability did not enforce its complete session and entity binding");
        Check(!VoidwrightOperationCapability.TryParse(serializedCapability,
                  now.Add(VoidwrightOperationCapability.MaximumLifetime), out _) &&
              !VoidwrightOperationCapability.TryParse(serializedCapability.Replace(hostToken, new string('z', 32)),
                  now, out _),
            "expired or malformed world-storage capability was accepted");

        var heartbeat = new VoidwrightPreflightHeartbeat
        {
            RunId = run, FixtureId = fixture, OperationId = operation,
            ContextSha256 = contextSha, Challenge = challenge,
            SessionId = session, ControllerId = controller, GridId = grid,
            AgentCoreId = TestAgentCoreId, DiscoveryRunId = run, DiscoveryTick = 12,
            DiscoveryFingerprint = fingerprint, CameraId = camera, SimulationTick = 15,
            PublishedUtc = now, SensorValid = true, SensorScanTick = 15, SensorScanUtc = now,
            SensorGridId = grid, SensorCameraId = 333, ValidRayCount = VoidwrightPreflightHeartbeat.RayCount,
            WheelCount = 4, SteeringWheelCount = 2, PropulsionWheelCount = 4, ActuatorReady = true,
            OperationCapabilityToken = parsedCapability.Token,
            Clearances = Enumerable.Repeat(8.0, VoidwrightPreflightHeartbeat.RayCount).ToArray()
        };
        var wire = heartbeat.Format();
        Check(wire.StartsWith("VWPF5\n", StringComparison.Ordinal) &&
              VoidwrightPreflightHeartbeat.TryParse(wire, out var parsed) &&
              parsed.OperationCapabilityToken == hostToken &&
              parsed.IsFreshAndBound(now, TimeSpan.FromSeconds(1), run, fixture, operation,
                  heartbeat.ContextSha256, heartbeat.Challenge, heartbeat.SessionId,
                  controller, grid, heartbeat.CameraId, TestAgentCoreId, run,
                  heartbeat.DiscoveryTick, heartbeat.DiscoveryFingerprint) &&
              !parsed.IsFreshAndBound(now, TimeSpan.FromSeconds(1), "ffffffffffffffffffffffffffffffff",
                  fixture, operation, heartbeat.ContextSha256, heartbeat.Challenge, heartbeat.SessionId,
                  controller, grid, heartbeat.CameraId, TestAgentCoreId, run,
                  heartbeat.DiscoveryTick, heartbeat.DiscoveryFingerprint) &&
              !parsed.IsFreshAndBound(now, TimeSpan.FromSeconds(1), run, fixture, operation,
                  heartbeat.ContextSha256, heartbeat.Challenge, heartbeat.SessionId,
                  controller + 1, grid, heartbeat.CameraId, TestAgentCoreId, run,
                  heartbeat.DiscoveryTick, heartbeat.DiscoveryFingerprint),
            "heartbeat did not round-trip an exact run-bound operation capability");

        var wrongGrid = OperationIdentity(run, fixture, operation, grid + 1, controller);
        var wrongController = OperationIdentity(run, fixture, operation, grid, controller + 1);
        var wrongOperation = OperationIdentity(run, fixture, "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee", grid, controller);
        Check(broker.GetMovementCapabilityToken(wrongGrid, now) == string.Empty &&
              broker.GetMovementCapabilityToken(wrongController, now) == string.Empty &&
              broker.GetMovementCapabilityToken(wrongOperation, now) == string.Empty,
            "capability token was returned for a wrong grid, controller, or operation");

        var expiryBroker = new DirectManeuverActuatorBroker(new[] { "vragecage-server-plugin" },
            DirectManeuverExternalWriterState.LocalParticipatingWriterAuditPassed);
        Check(expiryBroker.TryAcquire(plan, evidence, state, brakes, now, TimeSpan.FromSeconds(1), true, out _),
            "expiry fixture could not acquire its first operation");
        var expiringToken = expiryBroker.GetMovementCapabilityToken(identity, now);
        Check(VoidwrightOperationCapability.IsToken(expiringToken) &&
              expiryBroker.GetMovementCapabilityToken(identity, now.AddMilliseconds(1100)) == string.Empty &&
              !expiryBroker.TryRunCapabilityTransaction("vragecage-server-plugin", identity,
                  expiringToken, now.AddMilliseconds(1100), () => true),
            "expired capability remained available to the mod or host writer");
        var takeoverTime = now.AddMilliseconds(1100);
        var takeoverOperation = "dddddddddddddddddddddddddddddddd";
        var takeoverPlan = Plan(run, fixture, takeoverOperation, grid, controller, new[] {
            new DirectManeuverSegment(DirectManeuverKind.ForwardDistance, 0.25, 0.25, 20),
            new DirectManeuverSegment(DirectManeuverKind.StationaryHold, 0, 0, 20) });
        var takeoverEvidence = Evidence(run, fixture, takeoverOperation, grid, controller, grid,
            4.0, takeoverTime, true, takeoverTime, true, takeoverTime, true, true, true, true, true, true);
        var takeoverState = new[] { new DirectManeuverWheelState(wheel, 0, 0, true, false, takeoverTime) };
        var takeoverBrakes = new DirectManeuverBrakeState(true, false, true, false, takeoverTime);
        var takeoverIdentity = OperationIdentity(run, fixture, takeoverOperation, grid, controller);
        Check(expiryBroker.TryAcquire(takeoverPlan, takeoverEvidence, takeoverState, takeoverBrakes,
                  takeoverTime, TimeSpan.FromSeconds(1), true, out _) &&
              !expiryBroker.TryRunCapabilityTransaction("vragecage-server-plugin", identity,
                  expiringToken, takeoverTime, () => true) &&
              VoidwrightOperationCapability.IsToken(
                  expiryBroker.GetMovementCapabilityToken(takeoverIdentity, takeoverTime)),
            "takeover failed to revoke the previous operation capability");

        Check(broker.RequestStop(new DirectManeuverStopRequest(identity, "bridge-revoke",
                  DirectManeuverStopKind.EmergencyStop, now), out var stopCapability) &&
              stopCapability != null && broker.GetMovementCapabilityToken(identity, now) == string.Empty &&
              !broker.TryRunCapabilityTransaction("vragecage-server-plugin", identity, hostToken,
                  now, () => true),
            "stop request did not revoke movement token while preserving the stop-only path");
    }

    private static void DirectManeuverControllerTakeoverIsFailClosed()
    {
        bool Ready(bool resolved = true, bool info = true, bool main = true, bool selected = true,
            bool controlled = false, long identity = 0, bool pilot = false, bool other = false,
            bool transition = false) =>
            DirectManeuverControllerTakeoverPolicy.IsStable(resolved, info, main, selected,
                controlled, identity, pilot, other, transition);

        Check(Ready(), "known free main cockpit was not recognized as stable");
        Check(!Ready(resolved: false) && !Ready(info: false) && !Ready(main: false) &&
              !Ready(selected: false) && !Ready(other: true),
            "unknown or changed main-cockpit selection did not fail closed");
        Check(!Ready(controlled: true) && !Ready(identity: 44) && !Ready(pilot: true),
            "player acquisition of the main cockpit did not revoke the stable controller state");
        Check(!Ready(transition: true),
            "a latched ControlAcquired/ControlReleased event did not revoke the operation");
        Check(Ready(controlled: true, identity: 44) == false && Ready(controlled: false, identity: 0),
            "controller release did not restore the observable free state");
    }

    private static void MovementOperationDeadlineIsBounded()
    {
        Check(!MovementPreflight.HasOperationTimedOut(99, 100, 60), "a pre-start tick timed out the operation");
        Check(!MovementPreflight.HasOperationTimedOut(159, 100, 60), "operation timed out before its limit");
        Check(MovementPreflight.HasOperationTimedOut(160, 100, 60), "operation was not terminal at its tick limit");
        Check(MovementPreflight.HasOperationTimedOut(161, 100, 60), "deadline stopped applying after the limit");
        Check(!MovementPreflight.HasOperationTimedOut(160, 100, 0), "zero timeout was treated as a deadline");
        Check(!MovementPreflight.HasOperationTimedOut(160, 100, -1), "negative timeout was treated as a deadline");
        Check(!MovementPreflight.HasOperationTimedOut(160, -1, 60), "unset start tick was treated as an active operation");
    }

    private static void TimeoutReceiptStatusSurvivesStopError()
    {
        Check(MovementPreflight.ResolveTerminalStatus("run tick limit reached; actuator stop threw InvalidOperationException", "timeout") == "timeout",
            "stop failure replaced the terminal timeout outcome");
        Check(MovementPreflight.ResolveTerminalStatus("run tick limit reached", null) == "timeout",
            "ordinary timeout did not produce a timeout receipt");
        Check(MovementPreflight.ResolveTerminalStatus("mod-side movement preflight denied dispatch after host approval", null) == "preflight-blocked",
            "failed preflight did not produce an explicit terminal status");
        Check(MovementPreflight.ResolveTerminalStatus("arrival radius reached", null) == "complete",
            "arrival status mapping changed");
        Check(MovementPreflight.ResolveTerminalStatus("operator took control; autonomous movement stopped", null) == "human-connected",
            "operator interruption status mapping changed");
    }

    private static void SimulationEvidenceCadenceHandlesBoundaries()
    {
        Check(SimulationEvidenceCadence.IsDue(1, long.MinValue, 60),
            "first evidence write was suppressed by sentinel arithmetic");
        Check(!SimulationEvidenceCadence.IsDue(59, 0, 60), "evidence wrote before its interval");
        Check(SimulationEvidenceCadence.IsDue(60, 0, 60), "evidence did not write at its interval");
        Check(!SimulationEvidenceCadence.IsDue(59, 60, 60), "a backwards simulation tick was accepted");
        Check(!SimulationEvidenceCadence.IsDue(60, 0, 0), "zero interval was accepted");
        Check(!SimulationEvidenceCadence.IsDue(60, 0, -1), "negative interval was accepted");
    }

    private static readonly string PreflightRunId = "0123456789abcdef0123456789abcdef";
    private static readonly string PreflightOperationId = "abcdef0123456789abcdef0123456789";
    private static readonly string PreflightFixture = "fixture-sha256-a";
    private static readonly string PreflightState = "controller:123:grid:456";

    private static MovementPreflight CreatePreflight(DateTime now, PreflightStatus? overrideStatus = null,
        bool includeChecks = true)
    {
        var checks = new List<PreflightCheck>();
        if (includeChecks)
            foreach (var name in new[] { "fixture_source_integrity", "runtime_connectivity", "vehicle_identity",
                         "voidwright_module_and_session_marker", "controller_readiness", "actuator_availability",
                         "sensor_validity_freshness", "position_validity_freshness", "safety_configuration",
                         "movement_interface_readiness" })
                checks.Add(new PreflightCheck(name, overrideStatus ?? PreflightStatus.Pass,
                    "verified", "fixture evidence", "test signal", now));
        return new MovementPreflight(PreflightRunId, PreflightOperationId, PreflightFixture,
            PreflightState, now, checks);
    }

    private static MovementPreflight CreatePreflightWithUnknownChecks(DateTime now,
        string failedCheckName, params string[] unknownCheckNames)
    {
        var checks = new List<PreflightCheck>();
        foreach (var name in new[] { "fixture_source_integrity", "runtime_connectivity", "vehicle_identity",
                     "voidwright_module_and_session_marker", "controller_readiness", "actuator_availability",
                     "sensor_validity_freshness", "position_validity_freshness", "safety_configuration",
                     "movement_interface_readiness" })
        {
            var status = name == failedCheckName ? PreflightStatus.Fail :
                (Array.IndexOf(unknownCheckNames, name) >= 0 ? PreflightStatus.Unknown : PreflightStatus.Pass);
            checks.Add(new PreflightCheck(name, status, "verified", "fixture evidence", "test signal", now));
        }
        return new MovementPreflight(PreflightRunId, PreflightOperationId, PreflightFixture,
            PreflightState, now, checks);
    }

    private static bool CanDispatch(MovementPreflight preflight, DateTime now, string runId = null,
        string operationId = null, string fixture = null, string state = null, Action movement = null)
    {
        return MovementCommandGate.TryDispatch(preflight, now,
            runId ?? PreflightRunId, operationId ?? PreflightOperationId,
            fixture ?? PreflightFixture, state ?? PreflightState,
            TimeSpan.FromSeconds(1), movement ?? delegate { });
    }

    private static void MovementPreflightRequiresAllPasses()
    {
        var now = DateTime.UtcNow;
        var preflight = CreatePreflight(now);
        var dispatched = 0;
        Check(preflight.PassingCount == 10, "all-pass candidate did not count ten verified checks");
        Check(CanDispatch(preflight, now.AddMilliseconds(1), movement: () => dispatched++),
            "fresh, run-bound all-pass evidence did not authorize the movement boundary");
        Check(dispatched == 1, "authorized movement callback did not run exactly once");
    }

    private static void PreflightOnlyWaitsForFinalEvidence()
    {
        var now = DateTime.UtcNow;
        Check(PreflightOnlyPolicy.Decide(CreatePreflight(now, PreflightStatus.Unknown), false) == PreflightOnlyDecision.Wait,
            "preflight-only should keep collecting while startup evidence is unknown");
        Check(PreflightOnlyPolicy.Decide(CreatePreflight(now), false) == PreflightOnlyDecision.Wait,
            "all mod checks should still wait for the run-bound host approval");
        Check(PreflightOnlyPolicy.Decide(CreatePreflight(now), true) == PreflightOnlyDecision.Complete,
            "preflight-only should complete after all checks and host approval pass");
        var awaitingHost = CreatePreflightWithUnknownChecks(now, null,
            "voidwright_module_and_session_marker", "controller_readiness", "movement_interface_readiness");
        Check(awaitingHost.HasOnlyUnknownChecks("voidwright_module_and_session_marker",
                "controller_readiness", "movement_interface_readiness"),
            "fresh heartbeat awaiting host brake approval did not match the exact three-check wait state");
        Check(!CreatePreflightWithUnknownChecks(now, null,
                "voidwright_module_and_session_marker", "movement_interface_readiness")
                .HasOnlyUnknownChecks("voidwright_module_and_session_marker",
                    "controller_readiness", "movement_interface_readiness"),
            "an unrelated unresolved controller check was treated as host-approval-only");
        Check(!CreatePreflightWithUnknownChecks(now, "actuator_availability",
                "voidwright_module_and_session_marker", "controller_readiness", "movement_interface_readiness")
                .HasOnlyUnknownChecks("voidwright_module_and_session_marker",
                    "controller_readiness", "movement_interface_readiness"),
            "a failed actuator check was hidden by the host-approval wait state");
        Check(PreflightOnlyPolicy.Decide(CreatePreflight(now, PreflightStatus.Fail), false) == PreflightOnlyDecision.Failed,
            "a definite failed safety check should terminate preflight-only without waiting");
        Check(PreflightOnlyPolicy.Decide(CreatePreflight(now).Invalidate(now.AddMilliseconds(1), "fixture changed"), true) ==
              PreflightOnlyDecision.Failed, "invalidated preflight must never report completion");
    }

    private static void PreflightOnlyTerminalStatusIsComplete()
    {
        Check(MovementPreflight.ResolveTerminalStatus(
                "preflight-only complete; no actuator dispatch was attempted", null) == "complete",
            "a successful no-dispatch preflight was mislabeled as preflight-blocked");
        Check(MovementPreflight.ResolveTerminalStatus(
                "preflight-only found a failed or incomplete safety check; no actuator dispatch was attempted", null) == "preflight-blocked",
            "a failed no-dispatch preflight was mislabeled as successful");
    }

    private static void PreflightSafetyWaitsForControllerEvidence()
    {
        Check(MovementPreflight.ResolveSafetyStatus(true, false, false, false, false) == PreflightStatus.Unknown,
            "missing controller and speed evidence was treated as a definite safety failure");
        Check(MovementPreflight.ResolveSafetyStatus(true, true, false, false, false) == PreflightStatus.Unknown,
            "unavailable speed evidence was treated as a definite safety failure");
        Check(MovementPreflight.ResolveSafetyStatus(true, true, true, false, true) == PreflightStatus.Fail,
            "known unsafe command configuration did not fail");
        Check(MovementPreflight.ResolveSafetyStatus(true, true, true, true, false) == PreflightStatus.Fail,
            "known operator control did not fail");
        Check(MovementPreflight.ResolveSafetyStatus(true, true, true, true, true) == PreflightStatus.Pass,
            "fully observed safe state did not pass");
    }

    private static void PreflightStartupRetryUsesProductionCadence()
    {
        // Simulation boundary: entity availability is represented by a boolean because the
        // game entity registry cannot run in this offline test. Cadence and terminal policy
        // are the production helpers called by VoidwrightLabPreflight.UpdateCore.
        var lookupAttempts = 0;
        var controllerAvailable = false;
        for (var tick = 0; tick <= 10; tick++)
        {
            if (!SimulationEvidenceCadence.IsPeriodicTick(tick, 10)) continue;
            lookupAttempts++;
            if (tick == 10) controllerAvailable = true;
            var safety = MovementPreflight.ResolveSafetyStatus(true, controllerAvailable,
                controllerAvailable, true, controllerAvailable);
            if (tick == 0)
            {
                Check(safety == PreflightStatus.Unknown, "unresolved first lookup became a definite safety result");
                Check(PreflightOnlyPolicy.Decide(CreatePreflight(DateTime.UtcNow, PreflightStatus.Unknown), false) ==
                      PreflightOnlyDecision.Wait, "unknown first sample terminalized preflight-only");
            }
            else
            {
                Check(safety == PreflightStatus.Pass, "resolved controller did not produce observed safety evidence");
                Check(PreflightOnlyPolicy.Decide(CreatePreflight(DateTime.UtcNow), false) ==
                      PreflightOnlyDecision.Wait, "all-pass sample completed before host approval");
                Check(PreflightOnlyPolicy.Decide(CreatePreflight(DateTime.UtcNow), true) ==
                      PreflightOnlyDecision.Complete, "fresh host approval did not complete read-only preflight");
            }
        }
        Check(lookupAttempts == 2, "production 10-tick retry cadence did not attempt at tick 0 and tick 10");
        Check(!SimulationEvidenceCadence.IsPeriodicTick(-1, 10) &&
              !SimulationEvidenceCadence.IsPeriodicTick(10, 0), "invalid retry cadence was accepted");
    }

    private static void PreflightHandshakeRoundTripsSimulatedWorldStorage()
    {
        var now = new DateTime(2026, 10, 19, 16, 18, 10, DateTimeKind.Utc);
        var heartbeat = new VoidwrightPreflightHeartbeat
        {
            RunId = PreflightRunId, FixtureId = "11111111111111111111111111111111",
            OperationId = PreflightOperationId, ContextSha256 = new string('a', 64),
            Challenge = "33333333333333333333333333333333",
            SessionId = "44444444444444444444444444444444", ControllerId = 12345,
            GridId = 456, CameraId = 67890, SimulationTick = 100, PublishedUtc = now,
            SensorValid = true, SensorScanTick = 100, SensorScanUtc = now,
            SensorGridId = 456, SensorCameraId = 67890,
            ValidRayCount = VoidwrightPreflightHeartbeat.RayCount, WheelCount = 4,
            SteeringWheelCount = 2, PropulsionWheelCount = 4, ActuatorReady = true,
            Clearances = Enumerable.Repeat(8.0, VoidwrightPreflightHeartbeat.RayCount).ToArray()
        };

        // Simulation boundary: these strings stand in for the shared game WorldStorage
        // stream. Parsing, freshness, identity, echo, and approval checks are production code.
        var simulatedModWorldStorage = heartbeat.Format();
        bool HostAccept(string serialized, DateTime observedAt, long resolvedControllerGrid,
            long resolvedCameraGrid, string expectedRun = null)
        {
            VoidwrightPreflightHeartbeat observed;
            if (resolvedControllerGrid <= 0 || resolvedControllerGrid != resolvedCameraGrid ||
                !VoidwrightPreflightHeartbeat.TryParse(serialized, out observed)) return false;
            return observed.IsFreshAndBound(observedAt, TimeSpan.FromSeconds(1),
                expectedRun ?? heartbeat.RunId, heartbeat.FixtureId, heartbeat.OperationId,
                heartbeat.ContextSha256, heartbeat.Challenge, heartbeat.SessionId,
                heartbeat.ControllerId, resolvedControllerGrid, heartbeat.CameraId);
        }

        VoidwrightPreflightHeartbeat hostObserved;
        Check(HostAccept(simulatedModWorldStorage, now, 456, 456),
            "host rejected the exact fresh same-grid mod heartbeat");
        Check(VoidwrightPreflightHandshake.TryAccept(simulatedModWorldStorage, simulatedModWorldStorage,
                now, TimeSpan.FromSeconds(1), heartbeat.RunId, heartbeat.FixtureId,
                heartbeat.OperationId, heartbeat.ContextSha256, heartbeat.Challenge,
                heartbeat.SessionId, heartbeat.ControllerId, heartbeat.GridId, heartbeat.CameraId,
                out hostObserved), "mod rejected byte-exact heartbeat readback");

        var approval = new VoidwrightPreflightApproval
        {
            RunId = hostObserved.RunId, FixtureId = hostObserved.FixtureId,
            OperationId = hostObserved.OperationId, ContextSha256 = hostObserved.ContextSha256,
            Challenge = hostObserved.Challenge, SessionId = hostObserved.SessionId,
            ControllerId = hostObserved.ControllerId, GridId = hostObserved.GridId,
            CameraId = hostObserved.CameraId, SimulationTick = hostObserved.SimulationTick,
            SensorScanTick = hostObserved.SensorScanTick, PublishedUtc = now.AddMilliseconds(100)
        };
        var simulatedHostWorldStorage = approval.Format();
        Check(VoidwrightPreflightApproval.TryAccept(simulatedHostWorldStorage, now.AddMilliseconds(200),
                TimeSpan.FromSeconds(1), hostObserved, out _),
            "mod rejected the exact fresh host acknowledgement");
        Check(!HostAccept("malformed", now, 456, 456), "host accepted malformed heartbeat bytes");
        Check(!HostAccept(simulatedModWorldStorage, now, 456, 456,
                "66666666666666666666666666666666"), "host accepted a heartbeat from the wrong run");
        Check(!HostAccept(simulatedModWorldStorage, now, 456, 457),
            "host accepted controller and camera resolved on different grids");
        Check(!HostAccept(simulatedModWorldStorage, now.AddSeconds(2), 456, 456),
            "host accepted a stale heartbeat");

        var wrongGridHeartbeat = new VoidwrightPreflightHeartbeat
        {
            RunId = heartbeat.RunId, FixtureId = heartbeat.FixtureId, OperationId = heartbeat.OperationId,
            ContextSha256 = heartbeat.ContextSha256, Challenge = heartbeat.Challenge,
            SessionId = heartbeat.SessionId, ControllerId = heartbeat.ControllerId, GridId = 457,
            CameraId = heartbeat.CameraId, SimulationTick = heartbeat.SimulationTick,
            PublishedUtc = heartbeat.PublishedUtc, SensorValid = heartbeat.SensorValid,
            SensorScanTick = heartbeat.SensorScanTick, SensorScanUtc = heartbeat.SensorScanUtc,
            SensorGridId = heartbeat.SensorGridId, SensorCameraId = heartbeat.SensorCameraId,
            ValidRayCount = heartbeat.ValidRayCount, WheelCount = heartbeat.WheelCount,
            SteeringWheelCount = heartbeat.SteeringWheelCount, PropulsionWheelCount = heartbeat.PropulsionWheelCount,
            ActuatorReady = heartbeat.ActuatorReady, Clearances = (double[])heartbeat.Clearances.Clone()
        };
        Check(!HostAccept(wrongGridHeartbeat.Format(), now, 456, 456),
            "host accepted heartbeat grid identity that differed from its resolved entities");
    }

    private static void ActuatorAndBrakeReadinessClassifyOwnership()
    {
        Check(MovementPreflight.ResolveActuatorStatus(false, 0, false, false, false) == PreflightStatus.Unknown,
            "unresolved actuator discovery was treated as a definite failure");
        Check(MovementPreflight.ResolveActuatorStatus(true, 0, false, false, false) == PreflightStatus.Unknown,
            "empty startup wheel discovery was treated as a definite failure");
        Check(MovementPreflight.ResolveActuatorStatus(true, 4, false, true, true) == PreflightStatus.Fail,
            "missing steering channel passed actuator readiness");
        Check(MovementPreflight.ResolveActuatorStatus(true, 4, true, true, false) == PreflightStatus.Fail,
            "externally changed steering/propulsion override was treated as owned");
        Check(MovementPreflight.ResolveActuatorStatus(true, 4, true, true, true) == PreflightStatus.Pass,
            "available channels with neutral or adapter-owned overrides did not pass");

        Check(MovementPreflight.ResolveBrakeStatus(false, false, false, false, false) == PreflightStatus.Unknown,
            "unavailable brake telemetry was treated as brake-clear");
        Check(MovementPreflight.ResolveBrakeStatus(true, false, false, false, false) == PreflightStatus.Unknown,
            "missing per-wheel brake telemetry was treated as brake-clear");
        Check(MovementPreflight.ResolveBrakeStatus(true, true, false, true, false) == PreflightStatus.Fail,
            "active grid brake did not fail readiness");
        Check(MovementPreflight.ResolveBrakeStatus(true, false, true, true, false) == PreflightStatus.Fail,
            "active grid handbrake did not fail readiness");
        Check(MovementPreflight.ResolveBrakeStatus(true, false, false, true, true) == PreflightStatus.Fail,
            "active per-wheel brake/handbrake did not fail readiness");
        Check(MovementPreflight.ResolveBrakeStatus(true, false, false, true, false) == PreflightStatus.Pass,
            "observed-neutral grid and wheel brake state did not pass");
        Check(MovementPreflight.RequireHostBrakeAttestation(PreflightStatus.Pass, false) == PreflightStatus.Unknown,
            "mod treated unobservable grid/wheel brake inputs as locally owned or clear");
        Check(MovementPreflight.RequireHostBrakeAttestation(PreflightStatus.Pass, true) == PreflightStatus.Pass,
            "fresh host brake-state approval did not satisfy the mod's controller gate");
        Check(MovementPreflight.RequireHostBrakeAttestation(PreflightStatus.Fail, true) == PreflightStatus.Fail,
            "host approval overrode a locally observed controller failure");
    }

    private static void MovementPreflightFailsClosed()
    {
        var now = DateTime.UtcNow;
        Check(!CanDispatch(CreatePreflight(now, includeChecks: false), now), "missing checks authorized movement");
        var incompleteSet = new List<PreflightCheck>();
        foreach (var name in new[] { "fixture_source_integrity", "runtime_connectivity", "vehicle_identity",
                     "voidwright_module_and_session_marker", "controller_readiness", "actuator_availability",
                     "sensor_validity_freshness", "position_validity_freshness", "safety_configuration", "unexpected_check" })
            incompleteSet.Add(new PreflightCheck(name, PreflightStatus.Pass, "ok", "evidence", "source", now));
        Check(!CanDispatch(new MovementPreflight(PreflightRunId, PreflightOperationId, PreflightFixture,
            PreflightState, now, incompleteSet), now), "ten arbitrary checks replaced the required contract");
        Check(!CanDispatch(CreatePreflight(now, PreflightStatus.Unknown), now), "unknown check authorized movement");
        Check(!CanDispatch(CreatePreflight(now, PreflightStatus.Fail), now), "failed check authorized movement");
        Check(!CanDispatch(CreatePreflight(now), now.AddSeconds(2)), "stale preflight authorized movement");
        Check(!MovementPreflight.MustStopAfterHostApproval(CreatePreflight(now), true),
            "a current all-pass preflight was treated as a host-approved denial");
        Check(!MovementPreflight.MustStopAfterHostApproval(CreatePreflight(now, PreflightStatus.Fail), false),
            "a missing host approval was treated as a terminal mod-side denial");
        var oneFailedCheck = new List<PreflightCheck>();
        foreach (var name in new[] { "fixture_source_integrity", "runtime_connectivity", "vehicle_identity",
                     "voidwright_module_and_session_marker", "controller_readiness", "actuator_availability",
                     "sensor_validity_freshness", "position_validity_freshness", "safety_configuration",
                     "movement_interface_readiness" })
            oneFailedCheck.Add(new PreflightCheck(name,
                name == "actuator_availability" ? PreflightStatus.Fail : PreflightStatus.Pass,
                "fixture", "fixture", "test", now));
        Check(MovementPreflight.MustStopAfterHostApproval(new MovementPreflight(PreflightRunId,
                PreflightOperationId, PreflightFixture, PreflightState, now, oneFailedCheck), true),
            "a fresh host approval with one failed mod-side check did not terminate safely");
        Check(MovementPreflight.MustStopAfterHostApproval(CreatePreflight(now, includeChecks: false), true),
            "a fresh host approval with an incomplete mod-side contract did not terminate safely");
        var staleChecks = new List<PreflightCheck>();
        var names = new[] { "fixture_source_integrity", "runtime_connectivity", "vehicle_identity",
            "voidwright_module_and_session_marker", "controller_readiness", "actuator_availability",
            "sensor_validity_freshness", "position_validity_freshness", "safety_configuration",
            "movement_interface_readiness" };
        for (var index = 0; index < names.Length; index++)
            staleChecks.Add(new PreflightCheck(names[index], PreflightStatus.Pass, "ok", "evidence", "source",
                index == 4 ? now.AddSeconds(-2) : now));
        var staleOne = new MovementPreflight(PreflightRunId, PreflightOperationId, PreflightFixture,
            PreflightState, now, staleChecks);
        Check(!CanDispatch(staleOne, now), "one stale check did not invalidate the whole preflight");
        var invalidated = CreatePreflight(now).Invalidate(now.AddMilliseconds(1), "fixture changed");
        Check(!CanDispatch(invalidated, now.AddMilliseconds(2)), "explicitly invalidated success remained authorizing");
        using (var document = JsonDocument.Parse(invalidated.ToJson()))
            Check(document.RootElement.GetProperty("summary").GetProperty("success").GetBoolean() == false,
                "invalidated preflight remained reported as successful");
    }

    private static void MovementPreflightInvalidatesChangedContext()
    {
        var now = DateTime.UtcNow;
        var preflight = CreatePreflight(now);
        Check(!CanDispatch(preflight, now, runId: "11111111111111111111111111111111"), "new run reused old preflight");
        Check(!CanDispatch(preflight, now, fixture: "changed-fixture"), "fixture change reused old preflight");
        Check(!CanDispatch(preflight, now, state: "changed-controller-state"), "state change reused old preflight");
        Check(!CanDispatch(preflight, now, operationId: "22222222222222222222222222222222"), "new movement operation reused old preflight");
    }

    private static void FailedPreflightDoesNotDispatch()
    {
        var now = DateTime.UtcNow;
        var movementCalls = 0;
        var dispatched = CanDispatch(CreatePreflight(now, PreflightStatus.Unknown), now,
            movement: () => movementCalls++);
        Check(!dispatched && movementCalls == 0, "blocked preflight invoked the movement command");
    }

    private static void MovementCallbackExceptionStopsAndFailsClosed()
    {
        var now = DateTime.UtcNow;
        var movementCalls = 0;
        var stopCalls = 0;
        var failureType = string.Empty;
        var dispatched = MovementCommandGate.TryDispatchSafely(CreatePreflight(now), now,
            PreflightRunId, PreflightOperationId, PreflightFixture, PreflightState,
            TimeSpan.FromSeconds(1),
            () => { movementCalls++; throw new InvalidOperationException("synthetic actuator failure"); },
            () => stopCalls++, error => failureType = error.GetType().Name);
        Check(!dispatched, "throwing movement callback reported successful dispatch");
        Check(movementCalls == 1 && stopCalls == 1, "movement exception did not attempt emergency stop exactly once");
        Check(failureType == nameof(InvalidOperationException), "movement exception type was not reported");

        movementCalls = 0;
        stopCalls = 0;
        var blocked = MovementCommandGate.TryDispatchSafely(
            CreatePreflight(now, PreflightStatus.Unknown), now, PreflightRunId, PreflightOperationId,
            PreflightFixture, PreflightState, TimeSpan.FromSeconds(1), () => movementCalls++,
            () => stopCalls++, error => { });
        Check(!blocked && movementCalls == 0 && stopCalls == 0,
            "unknown preflight invoked movement or emergency-stop callback before adapter ownership");
    }

    private static void WheelStopPreservesExternalChanges()
    {
        var owned = WheelActuatorStopPlan.Create(0.2f, 0.2f, 0.4f, 0.4f);
        Check(owned.ClearSteering && owned.ClearPropulsion && !owned.HasExternalChanges,
            "unchanged adapter-owned wheel state was not eligible for a safety stop");

        var externalSteering = WheelActuatorStopPlan.Create(-0.6f, 0.2f, 0.4f, 0.4f);
        Check(!externalSteering.ClearSteering && externalSteering.ClearPropulsion &&
              externalSteering.HasExternalChanges, "external steering change was not isolated from owned channels");

        var externalPropulsion = WheelActuatorStopPlan.Create(0.2f, 0.2f, -0.3f, 0.4f);
        Check(externalPropulsion.ClearSteering && !externalPropulsion.ClearPropulsion,
            "external propulsion change was overwritten instead of preserved");

        var partialWriteApplied = WheelActuatorStopPlan.Create(0.2f, 0.2f, 0.4f, 0.4f);
        Check(partialWriteApplied.ClearSteering && partialWriteApplied.ClearPropulsion,
            "a setter that applied before throwing could not be recognized for cleanup");
        var setterDidNotApply = WheelActuatorStopPlan.Create(0, 0.2f, 0.4f, 0.4f);
        Check(!setterDidNotApply.ClearSteering && setterDidNotApply.ClearPropulsion,
            "a failed setter caused an unchanged external baseline to be overwritten");

        var nonFinite = WheelActuatorStopPlan.Create(float.NaN, 0.2f, 0.4f, 0.4f);
        Check(!nonFinite.ClearSteering && nonFinite.ClearPropulsion,
            "non-finite external actuator state was treated as owned");
        var neverOwned = WheelActuatorStopPlan.Create(0.5f, 0.5f, -0.4f, -0.4f,
            ownsSteering: false, ownsPropulsion: false);
        Check(!neverOwned.ClearSteering && !neverOwned.ClearPropulsion &&
              !neverOwned.HasExternalChanges,
            "stop plan attempted to change properties the adapter never owned");
        var partiallyOwned = WheelActuatorStopPlan.Create(0.2f, 0.2f, -0.4f, -0.4f,
            ownsSteering: true, ownsPropulsion: false);
        Check(partiallyOwned.ClearSteering && !partiallyOwned.ClearPropulsion &&
              !partiallyOwned.HasExternalChanges,
            "stop plan changed unowned propulsion while clearing owned steering");
        Check(!WheelActuatorStopPlan.IsNeutral(float.NaN) && !WheelActuatorStopPlan.IsNeutral(float.PositiveInfinity),
            "non-finite unowned actuator inputs were treated as neutral");
        Check(WheelActuatorStopPlan.CanAdopt(0, 0),
            "neutral unowned wheel state could not be adopted");
        Check(!WheelActuatorStopPlan.CanAdopt(float.NaN, 0),
            "a non-finite unowned override was accepted for autonomous control");
    }

    private static void MovementPreflightEmitsEvidence()
    {
        var now = DateTime.UtcNow;
        var json = CreatePreflight(now, PreflightStatus.Unknown).ToJson();
        using (var document = JsonDocument.Parse(json))
        {
            var root = document.RootElement;
            Check(root.GetProperty("schema").GetString() == "vragecage.movement-preflight.v1", "wrong preflight schema");
            Check(root.GetProperty("run_id").GetString() == PreflightRunId, "run identity is not bound into the result");
            Check(root.GetProperty("operation_id").GetString() == PreflightOperationId, "operation identity is not bound into the result");
            var checks = root.GetProperty("checks");
            Check(checks.GetArrayLength() == 10, "result does not include all ten checks");
            foreach (var check in checks.EnumerateArray())
            {
                Check(check.TryGetProperty("status", out _) && check.TryGetProperty("reason", out _) &&
                    check.TryGetProperty("evidence", out _) && check.TryGetProperty("source", out _) &&
                    check.TryGetProperty("timestamp_utc", out _), "check record is missing required provenance");
            }
            Check(root.GetProperty("summary").GetProperty("success").GetBoolean() == false,
                "unknown checks were reported as successful");
            Check(checks[5].GetProperty("status").GetString() == "UNKNOWN",
                "unknown actuator evidence was not preserved as UNKNOWN");
        }

        var nowFailed = DateTime.UtcNow;
        var failedChecks = new List<PreflightCheck>();
        foreach (var name in new[] { "fixture_source_integrity", "runtime_connectivity", "vehicle_identity",
                     "voidwright_module_and_session_marker", "controller_readiness", "actuator_availability",
                     "sensor_validity_freshness", "position_validity_freshness", "safety_configuration",
                     "movement_interface_readiness" })
            failedChecks.Add(new PreflightCheck(name,
                name == "actuator_availability" ? PreflightStatus.Fail : PreflightStatus.Pass,
                "synthetic result", "test", "test", nowFailed));
        using (var failedDocument = JsonDocument.Parse(new MovementPreflight(PreflightRunId,
            PreflightOperationId, PreflightFixture, PreflightState, nowFailed, failedChecks).ToJson()))
        {
            var failedRoot = failedDocument.RootElement;
            Check(!failedRoot.GetProperty("summary").GetProperty("success").GetBoolean(),
                "failed evidence was reported as success");
            Check(failedRoot.GetProperty("checks")[5].GetProperty("status").GetString() == "FAIL",
                "failed actuator evidence was not preserved as FAIL");
        }
    }

    private static void MovementPreflightSnapshotsAreImmutable()
    {
        var root = Path.Combine(Path.GetTempPath(), "voidwright-preflight-snapshots-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var created = new DateTime(2026, 10, 19, 17, 1, 36, DateTimeKind.Utc);
            MovementPreflightSnapshotStore.Write(root, PreflightRunId, PreflightOperationId, created, "first-pass");
            MovementPreflightSnapshotStore.Write(root, PreflightRunId, PreflightOperationId, created.AddSeconds(1), "later-stale");
            var snapshots = Directory.GetFiles(Path.Combine(root, "movement-preflight-snapshots"), "*.json").OrderBy(path => path, StringComparer.Ordinal).ToArray();
            var latest = Path.Combine(root, "movement-preflight-" + PreflightRunId + "-" + PreflightOperationId + ".json");
            Check(snapshots.Length == 2, "host preflight snapshots were not archived immutably");
            Check(File.ReadAllText(snapshots[0]) == "first-pass" && File.ReadAllText(snapshots[1]) == "later-stale",
                "an immutable host preflight snapshot was overwritten");
            Check(File.ReadAllText(latest) == "later-stale", "latest host preflight view did not advance");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void PreflightContextBindsRunAndSources()
    {
        var root = Path.Combine(Path.GetTempPath(), "vw-preflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var run = "0123456789abcdef0123456789abcdef";
            var fixture = "11111111111111111111111111111111";
            var operation = "22222222222222222222222222222222";
            var instance = Path.Combine(root, "instance");
            var runDirectory = Path.Combine(instance, "Quarantine", run);
            var modsPath = Path.Combine(instance, "Mods");
            var fixtureWorld = Path.Combine(instance, "World");
            Directory.CreateDirectory(runDirectory);
            Directory.CreateDirectory(Path.Combine(modsPath, "Voidwright", "Data", "Scripts"));
            Directory.CreateDirectory(fixtureWorld);
            var modAssembly = Path.Combine(modsPath, "Voidwright", "Data", "Scripts", "Voidwright.dll");
            File.WriteAllText(modAssembly, "test staged mod", Encoding.UTF8);
            var fixtureSectorPath = Path.Combine(fixtureWorld, "SANDBOX_0_0_0_.sbs");
            File.WriteAllText(fixtureSectorPath, "fixture sector", Encoding.UTF8);
            var artifactPaths = new Dictionary<string, string>();
            foreach (var name in new[] { "contract", "import", "active", "receipt", "command", "ledger" })
            {
                var path = Path.Combine(instance, name + ".json");
                File.WriteAllText(path, "artifact-" + name, Encoding.UTF8);
                artifactPaths[name] = path;
            }
            var fields = new Dictionary<string, string>
            {
                ["schema"] = "vragecage.preflight-context.v1", ["run_id"] = run,
                ["fixture_id"] = fixture, ["instance"] = "test-v4", ["operation_id"] = operation,
                ["action"] = "drive-to-coordinate-depth", ["controller_id"] = "12345",
                ["depth_camera_id"] = "67890", ["grid_id"] = "0", ["agent_core_id"] = "0", ["maneuver_segments"] = "",
                ["discovery_run_id"] = "", ["discovery_tick"] = "0", ["discovery_fingerprint"] = "",
                ["target_x"] = "1", ["target_y"] = "2",
                ["target_z"] = "3", ["max_speed"] = "0.5", ["timeout_ticks"] = "900",
                ["arrival_radius"] = "2", ["start_delay_ticks"] = "0", ["reverse"] = "false",
                ["source_tree_sha256"] = new string('a', 64),
                ["fixture_sector_path"] = fixtureSectorPath, ["fixture_sector_sha256"] = FileSha(fixtureSectorPath),
                ["mods_path"] = modsPath, ["mods_tree_sha256"] = TreeSha(modsPath),
                ["contract_path"] = artifactPaths["contract"], ["contract_sha256"] = FileSha(artifactPaths["contract"]),
                ["import_receipt_path"] = artifactPaths["import"], ["import_receipt_sha256"] = FileSha(artifactPaths["import"]),
                ["active_run_path"] = artifactPaths["active"], ["active_run_sha256"] = FileSha(artifactPaths["active"]),
                ["run_receipt_path"] = artifactPaths["receipt"], ["run_receipt_sha256"] = FileSha(artifactPaths["receipt"]),
                ["command_path"] = artifactPaths["command"], ["command_sha256"] = FileSha(artifactPaths["command"]),
                ["edit_ledger_path"] = artifactPaths["ledger"], ["edit_ledger_sha256"] = FileSha(artifactPaths["ledger"]),
                ["created_at_utc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
            };
            var contextPath = Path.Combine(runDirectory, "preflight-context-v1.txt");
            File.WriteAllText(contextPath, string.Join("", fields.Select(pair => pair.Key + "=" + pair.Value + "\n")), new UTF8Encoding(false));
            var digest = FileSha(contextPath);
            PreflightRunContext context;
            string reason;
            Check(PreflightRunContext.TryLoad(contextPath, digest, run, fixture, operation,
                "drive-to-coordinate-depth", out context, out reason), "valid context was rejected: " + reason);
            Check(context != null && context.ControllerId == 12345 && context.DepthCameraId == 67890 && context.MaximumSpeed == 0.5,
                "verified context values were not retained");
            Check(context.ModsTreeSha256 == fields["mods_tree_sha256"] && context.FixtureSectorSha256 == fields["fixture_sector_sha256"],
                "fixture or staged mod digest was not retained");
            Check(!PreflightRunContext.TryLoad(contextPath, digest, "33333333333333333333333333333333", fixture,
                operation, "drive-to-coordinate-depth", out context, out reason), "context was reusable under a different run");
            Check(!PreflightRunContext.TryLoad(contextPath, digest, run, "44444444444444444444444444444444",
                operation, "drive-to-coordinate-depth", out context, out reason), "context was reusable under a different fixture");
            Check(!PreflightRunContext.TryLoad(contextPath, digest, run, fixture,
                "55555555555555555555555555555555", "drive-to-coordinate-depth", out context, out reason),
                "context was reusable under a different operation");
            Check(!PreflightRunContext.TryLoad(contextPath, digest, run, fixture, operation,
                "drive-to-coordinate", out context, out reason), "depth context was reusable for another action");
            foreach (var name in new[] { "contract", "import", "active", "receipt", "command", "ledger" })
            {
                var original = File.ReadAllBytes(artifactPaths[name]);
                File.AppendAllText(artifactPaths[name], "tampered", Encoding.UTF8);
                Check(!PreflightRunContext.TryLoad(contextPath, digest, run, fixture, operation,
                    "drive-to-coordinate-depth", out context, out reason),
                    "changed " + name + " artifact retained authority");
                File.WriteAllBytes(artifactPaths[name], original);
            }
            File.AppendAllText(modAssembly, "changed", Encoding.UTF8);
            Check(!PreflightRunContext.TryLoad(contextPath, digest, run, fixture, operation,
                "drive-to-coordinate-depth", out context, out reason), "changed staged Voidwright source retained authority");
            File.WriteAllText(modAssembly, "test staged mod", Encoding.UTF8);
            fields["mods_tree_sha256"] = TreeSha(modsPath);
            Check(!PreflightRunContext.TryLoad(contextPath, new string('b', 64), run, fixture, operation,
                "drive-to-coordinate-depth", out context, out reason), "wrong context digest was accepted");

            fields["action"] = "direct-maneuver";
            fields["grid_id"] = "456";
            fields["agent_core_id"] = TestAgentCoreId.ToString(CultureInfo.InvariantCulture);
            fields["discovery_run_id"] = new string('d', 32);
            fields["discovery_tick"] = "900";
            fields["discovery_fingerprint"] = new string('e', 64);
            fields["maneuver_segments"] = "F,1,0.25,240;W,0,0,120";
            fields["max_speed"] = "0.25";
            fields["timeout_ticks"] = "360";
            fields["arrival_radius"] = "0.5";
            File.WriteAllText(contextPath,
                string.Join("", fields.Select(pair => pair.Key + "=" + pair.Value + "\n")), new UTF8Encoding(false));
            digest = FileSha(contextPath);
            Check(!PreflightRunContext.TryLoad(contextPath, digest, run, fixture, operation,
                "direct-maneuver", out context, out reason),
                "direct-maneuver context omitted the controlled-stop confirmation budget");
            fields["timeout_ticks"] = "480";
            File.WriteAllText(contextPath,
                string.Join("", fields.Select(pair => pair.Key + "=" + pair.Value + "\n")), new UTF8Encoding(false));
            digest = FileSha(contextPath);
            Check(PreflightRunContext.TryLoad(contextPath, digest, run, fixture, operation,
                "direct-maneuver", out context, out reason),
                "direct-maneuver context rejected segment time plus stop-confirmation budget: " + reason);
            Check(context.AgentCoreId == TestAgentCoreId && context.DirectManeuverPlan.AgentCoreId == TestAgentCoreId &&
                  context.DiscoveryRunId == new string('d', 32) && context.DiscoveryTick == 900 &&
                  context.DiscoveryFingerprint == new string('e', 64),
                "verified run context did not preserve the Agent Core discovery binding into its plan");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static void PreflightHeartbeatBindsFreshSensor()
    {
        var now = DateTime.UtcNow;
        var heartbeat = new VoidwrightPreflightHeartbeat
        {
            RunId = "0123456789abcdef0123456789abcdef",
            FixtureId = "11111111111111111111111111111111",
            OperationId = "22222222222222222222222222222222",
            ContextSha256 = new string('a', 64),
            Challenge = "33333333333333333333333333333333",
            SessionId = "44444444444444444444444444444444",
            ControllerId = 12345, GridId = 456, CameraId = 67890,
            SimulationTick = 100, PublishedUtc = now,
            SensorValid = true, SensorScanTick = 99, SensorScanUtc = now,
            SensorGridId = 456, SensorCameraId = 67890, ValidRayCount = VoidwrightPreflightHeartbeat.RayCount,
            WheelCount = 6, SteeringWheelCount = 2, PropulsionWheelCount = 4, ActuatorReady = true,
            Clearances = Enumerable.Repeat(8.0, VoidwrightPreflightHeartbeat.RayCount).ToArray()
        };
        VoidwrightPreflightHeartbeat parsed;
        var serializedHeartbeat = heartbeat.Format();
        Check(VoidwrightPreflightHandshake.IsValidChallenge(heartbeat.Challenge),
            "valid lowercase launcher challenge was rejected");
        Check(!VoidwrightPreflightHandshake.IsValidChallenge(null) &&
              !VoidwrightPreflightHandshake.IsValidChallenge(string.Empty) &&
              !VoidwrightPreflightHandshake.IsValidChallenge("g" + new string('0', 31)) &&
              !VoidwrightPreflightHandshake.IsValidChallenge(new string('0', 31)),
            "absent or malformed launcher challenge was accepted");
        Check(heartbeat.MatchesSerializedReadback(serializedHeartbeat), "exact sensor heartbeat readback was rejected");
        Check(VoidwrightPreflightHeartbeat.TryParse(serializedHeartbeat, out parsed), "valid heartbeat failed to parse");
        bool Accept(string readback, string expectedRun = null, DateTime? acceptedAt = null,
            string expectedReadback = null)
        {
            VoidwrightPreflightHeartbeat accepted;
            return VoidwrightPreflightHandshake.TryAccept(expectedReadback ?? serializedHeartbeat, readback,
                acceptedAt ?? now, TimeSpan.FromSeconds(1), expectedRun ?? heartbeat.RunId,
                heartbeat.FixtureId, heartbeat.OperationId, heartbeat.ContextSha256,
                heartbeat.Challenge, heartbeat.SessionId, heartbeat.ControllerId,
                heartbeat.GridId, heartbeat.CameraId, out accepted);
        }
        Check(Accept(serializedHeartbeat), "production handshake acceptance rejected the exact fresh marker");
        Check(!Accept(null) && !Accept(string.Empty), "missing heartbeat marker passed the readback handshake");
        Check(!Accept("not-a-VWPF1-marker"), "malformed heartbeat marker passed the readback handshake");
        Check(!VoidwrightPreflightHeartbeat.TryParse("not-a-VWPF1-marker", out parsed),
            "malformed heartbeat marker parsed as evidence");
        Check(!VoidwrightPreflightHeartbeat.TryParse(null, out parsed),
            "absent heartbeat marker parsed as evidence");
        Check(!Accept(serializedHeartbeat.Replace(heartbeat.Challenge, "not-a-valid-challenge")),
            "malformed challenge marker passed the readback handshake");
        VoidwrightPreflightHeartbeat nextHeartbeat;
        Check(VoidwrightPreflightHeartbeat.TryParse(serializedHeartbeat, out nextHeartbeat),
            "valid heartbeat could not seed the replay test");
        nextHeartbeat.SimulationTick++;
        nextHeartbeat.PublishedUtc = now.AddMilliseconds(10);
        nextHeartbeat.SensorScanTick = nextHeartbeat.SimulationTick;
        nextHeartbeat.SensorScanUtc = nextHeartbeat.PublishedUtc;
        Check(!Accept(serializedHeartbeat, acceptedAt: now.AddMilliseconds(20),
                expectedReadback: nextHeartbeat.Format()),
            "previously read heartbeat was replayed instead of the current response");
        Check(!Accept(serializedHeartbeat, "66666666666666666666666666666666"),
            "replaying the same challenge and marker under another run was accepted");
        Check(!Accept(serializedHeartbeat, acceptedAt: now.AddSeconds(2)),
            "an expired readback passed the production handshake acceptance rule");
        var alteredSensorReadback = serializedHeartbeat.Replace("|8\n", "|7\n");
        Check(VoidwrightPreflightHeartbeat.TryParse(alteredSensorReadback, out parsed) &&
              !heartbeat.MatchesSerializedReadback(alteredSensorReadback),
            "well-formed but altered clearance telemetry passed readback equality");
        Check(!Accept(alteredSensorReadback), "altered but well-formed marker passed the production readback rule");
        Check(VoidwrightPreflightHeartbeat.TryParse(serializedHeartbeat, out parsed), "valid heartbeat failed to reparse");
        Check(parsed.IsFreshAndBound(now.AddMilliseconds(20), TimeSpan.FromSeconds(1), heartbeat.RunId,
            heartbeat.FixtureId, heartbeat.OperationId, heartbeat.ContextSha256, heartbeat.Challenge, heartbeat.SessionId,
            heartbeat.ControllerId, heartbeat.GridId, heartbeat.CameraId), "fresh same-grid handshake was rejected");
        Check(!parsed.IsFreshAndBound(now.AddSeconds(2), TimeSpan.FromSeconds(1), heartbeat.RunId,
            heartbeat.FixtureId, heartbeat.OperationId, heartbeat.ContextSha256, heartbeat.Challenge, heartbeat.SessionId,
            heartbeat.ControllerId, heartbeat.GridId, heartbeat.CameraId), "stale handshake authorized movement");
        Check(!parsed.IsFreshAndBound(now, TimeSpan.FromSeconds(1), "66666666666666666666666666666666",
            heartbeat.FixtureId, heartbeat.OperationId, heartbeat.ContextSha256, heartbeat.Challenge, heartbeat.SessionId,
            heartbeat.ControllerId, heartbeat.GridId, heartbeat.CameraId), "heartbeat from a different run authorized movement");
        Check(!parsed.IsFreshAndBound(now, TimeSpan.FromSeconds(1), heartbeat.RunId,
            "77777777777777777777777777777777", heartbeat.OperationId, heartbeat.ContextSha256, heartbeat.Challenge, heartbeat.SessionId,
            heartbeat.ControllerId, heartbeat.GridId, heartbeat.CameraId), "heartbeat from a different fixture authorized movement");
        Check(!parsed.IsFreshAndBound(now, TimeSpan.FromSeconds(1), heartbeat.RunId,
            heartbeat.FixtureId, "88888888888888888888888888888888", heartbeat.ContextSha256, heartbeat.Challenge, heartbeat.SessionId,
            heartbeat.ControllerId, heartbeat.GridId, heartbeat.CameraId), "heartbeat from a different operation authorized movement");
        Check(!parsed.IsFreshAndBound(now, TimeSpan.FromSeconds(1), heartbeat.RunId,
            heartbeat.FixtureId, heartbeat.OperationId, heartbeat.ContextSha256, heartbeat.Challenge, heartbeat.SessionId,
            heartbeat.ControllerId, heartbeat.GridId + 1, heartbeat.CameraId), "cross-grid telemetry authorized movement");
        Check(!parsed.IsFreshAndBound(now, TimeSpan.FromSeconds(1), heartbeat.RunId,
            heartbeat.FixtureId, heartbeat.OperationId, new string('b', 64), heartbeat.Challenge, heartbeat.SessionId,
            heartbeat.ControllerId, heartbeat.GridId, heartbeat.CameraId), "wrong context hash authorized movement");
        Check(!parsed.IsFreshAndBound(now, TimeSpan.FromSeconds(1), heartbeat.RunId,
            heartbeat.FixtureId, heartbeat.OperationId, heartbeat.ContextSha256, new string('5', 32), heartbeat.SessionId,
            heartbeat.ControllerId, heartbeat.GridId, heartbeat.CameraId), "wrong challenge authorized movement");
        Check(!parsed.IsFreshAndBound(now, TimeSpan.FromSeconds(1), heartbeat.RunId,
            heartbeat.FixtureId, heartbeat.OperationId, heartbeat.ContextSha256, heartbeat.Challenge, heartbeat.SessionId,
            heartbeat.ControllerId, heartbeat.GridId, heartbeat.CameraId + 1), "wrong camera binding authorized movement");
        Check(!parsed.IsFreshAndBound(now, TimeSpan.FromSeconds(1), heartbeat.RunId,
            heartbeat.FixtureId, heartbeat.OperationId, heartbeat.ContextSha256, heartbeat.Challenge,
            "55555555555555555555555555555555", heartbeat.ControllerId, heartbeat.GridId, heartbeat.CameraId),
            "previous-session handshake was reused after a mod restart");
        heartbeat.SensorGridId++;
        Check(VoidwrightPreflightHeartbeat.TryParse(heartbeat.Format(), out parsed) &&
              !parsed.IsFreshAndBound(now, TimeSpan.FromSeconds(1), heartbeat.RunId,
                  heartbeat.FixtureId, heartbeat.OperationId, heartbeat.ContextSha256, heartbeat.Challenge, heartbeat.SessionId,
                  heartbeat.ControllerId, heartbeat.GridId, heartbeat.CameraId), "sensor scan from another grid was accepted");
    }

    private static void SessionLivenessAdvancesAfterTerminalPreflight()
    {
        var now = new DateTime(2026, 10, 19, 21, 58, 30, DateTimeKind.Utc);
        const string sessionId = "44444444444444444444444444444444";
        var terminal = new VoidwrightSessionLiveness
        {
            SessionId = sessionId,
            SimulationTick = 62,
            PublishedUtc = now,
            PreflightState = "preflight-blocked",
            TerminalReason = "mod-side movement preflight denied dispatch after host approval"
        };
        VoidwrightSessionLiveness parsedTerminal;
        Check(VoidwrightSessionLiveness.TryParse(terminal.Format(), out parsedTerminal) &&
              parsedTerminal.PreflightState == "preflight-blocked" &&
              parsedTerminal.TerminalReason == terminal.TerminalReason &&
              parsedTerminal.IsFresh(now, TimeSpan.FromSeconds(1.25), sessionId),
            "terminal preflight result was not preserved in the independent session-liveness record");
        var laterSessionBeat = new VoidwrightSessionLiveness
        {
            SessionId = sessionId,
            SimulationTick = 72,
            PublishedUtc = now.AddMilliseconds(167),
            PreflightState = "preflight-blocked",
            TerminalReason = terminal.TerminalReason
        };
        VoidwrightSessionLiveness parsedLater;
        Check(VoidwrightSessionLiveness.TryParse(laterSessionBeat.Format(), out parsedLater) &&
              parsedLater.SimulationTick > parsedTerminal.SimulationTick &&
              parsedLater.PreflightState == parsedTerminal.PreflightState,
            "session callback liveness stopped advancing when preflight reached its terminal state");
        Check(!parsedLater.IsFresh(now.AddSeconds(2), TimeSpan.FromSeconds(1.25), sessionId) &&
              !parsedLater.IsFresh(now, TimeSpan.FromSeconds(1.25), "55555555555555555555555555555555"),
            "stale or cross-session liveness was accepted as proof of a live callback");
    }

    private static void ModActivationIsBoundedAndStrict()
    {
        var now = DateTime.UtcNow;
        var activation = new VoidwrightModActivation
        {
            RunId = "0123456789abcdef0123456789abcdef",
            FixtureId = "11111111111111111111111111111111",
            OperationId = "22222222222222222222222222222222",
            ContextSha256 = new string('a', 64), Challenge = "33333333333333333333333333333333",
            ControllerId = 12345, CameraId = 67890, TargetX = 10, TargetY = 2, TargetZ = -4,
            MaximumSpeed = 0.5, TimeoutTicks = 900, ArrivalRadius = 2, StartDelayTicks = 30,
            Reverse = false, SourceTreeSha256 = new string('b', 64),
            IssuedUtc = now, ExpiresUtc = now.AddMinutes(2)
        };
        var serialized = activation.Format();
        VoidwrightModActivation parsed;
        Check(VoidwrightModActivation.TryParse(serialized, now.AddSeconds(1), out parsed),
            "fresh bounded host activation did not parse");
        Check(parsed != null && parsed.RunId == activation.RunId && parsed.FixtureId == activation.FixtureId &&
              parsed.OperationId == activation.OperationId && parsed.ContextSha256 == activation.ContextSha256 &&
              parsed.ControllerId == activation.ControllerId && parsed.CameraId == activation.CameraId &&
              parsed.TargetX == activation.TargetX && parsed.MaximumSpeed == activation.MaximumSpeed,
            "activation lost a pinned run, vehicle, or command field");
        Check(!VoidwrightModActivation.TryParse(serialized, now.AddMinutes(3), out parsed),
            "expired activation remained usable");
        Check(!VoidwrightModActivation.TryParse(serialized.Replace("|12345|", "|0|"), now, out parsed),
            "activation with an unbound controller was accepted");
        Check(!VoidwrightModActivation.TryParse(serialized.Replace("|0.5|", "|4.0|"), now, out parsed),
            "activation above the low-speed lab bound was accepted");
        Check(!VoidwrightModActivation.TryParse(serialized.Replace("|22222222222222222222222222222222|", "|../../escape|"), now, out parsed),
            "activation with an unsafe identity field was accepted");
        Check(!VoidwrightModActivation.TryParse(serialized, now.AddMinutes(-1), out parsed),
            "future-issued activation was accepted");

        var directActivation = new VoidwrightModActivation
        {
            RunId = activation.RunId, FixtureId = activation.FixtureId, OperationId = activation.OperationId,
            ContextSha256 = activation.ContextSha256, Challenge = activation.Challenge,
            ControllerId = activation.ControllerId, CameraId = activation.CameraId, GridId = 456,
            AgentCoreId = TestAgentCoreId, DiscoveryRunId = new string('d', 32), DiscoveryTick = 900,
            DiscoveryFingerprint = new string('e', 64), Action = "direct-maneuver",
            DirectManeuverSegments = "F,0.25,0.1,60;W,0,0,60",
            TargetX = 0, TargetY = 0, TargetZ = 0, MaximumSpeed = 0.1,
            TimeoutTicks = 180, ArrivalRadius = 0.5, StartDelayTicks = 0, Reverse = false,
            SourceTreeSha256 = activation.SourceTreeSha256, IssuedUtc = now, ExpiresUtc = now.AddMinutes(2)
        };
        var directText = directActivation.Format();
        Check(directText.StartsWith("VWPA4\n", StringComparison.Ordinal) &&
              VoidwrightModActivation.TryParse(directText, now.AddSeconds(1), out parsed) &&
              parsed.AgentCoreId == TestAgentCoreId && parsed.GridId == 456 &&
              parsed.DiscoveryRunId == new string('d', 32) && parsed.DiscoveryTick == 900 &&
              parsed.DiscoveryFingerprint == new string('e', 64),
            "direct activation did not round-trip its exact discovery-bound Core and anchor grid");
        directActivation.AgentCoreId = 0;
        var missingCoreRejected = false;
        try { directActivation.Format(); }
        catch (ArgumentException) { missingCoreRejected = true; }
        Check(missingCoreRejected, "direct activation serialized without an Agent Core identity");
    }

    private static void PreflightOnlyActivationBindsExactGrid()
    {
        var now = DateTime.UtcNow;
        var activation = new VoidwrightModActivation
        {
            RunId = PreflightRunId, FixtureId = "11111111111111111111111111111111",
            OperationId = PreflightOperationId, ContextSha256 = new string('a', 64),
            Challenge = "33333333333333333333333333333333", ControllerId = 12345,
            CameraId = 67890, GridId = 456, Action = "preflight-only", DirectManeuverSegments = "",
            AgentCoreId = TestAgentCoreId, DiscoveryRunId = new string('d', 32), DiscoveryTick = 900,
            DiscoveryFingerprint = new string('e', 64),
            TargetX = 0, TargetY = 0, TargetZ = 0, MaximumSpeed = 0.8, TimeoutTicks = 300,
            ArrivalRadius = 0.5, StartDelayTicks = 0, Reverse = false,
            SourceTreeSha256 = new string('b', 64), IssuedUtc = now, ExpiresUtc = now.AddMinutes(2)
        };
        var serialized = activation.Format();
        Check(serialized.StartsWith("VWPA4\n", StringComparison.Ordinal),
            "preflight-only activation did not use the discovery-bound exact-grid binding");
        Check(VoidwrightModActivation.TryParse(serialized, now.AddSeconds(1), out var parsed) &&
              parsed.Action == "preflight-only" && parsed.GridId == 456 && parsed.ControllerId == 12345 &&
              parsed.CameraId == 67890 && parsed.DirectManeuverSegments == string.Empty,
            "preflight-only activation did not round-trip its exact identities");
        Check(!VoidwrightModActivation.TryParse(serialized.Replace("|preflight-only|456|", "|preflight-only|0|"), now, out _),
            "preflight-only activation accepted an unbound grid");
    }

    private static void DiscoveryHandshakeWireRejectsReplayAndSessionMismatch()
    {
        var now = DateTime.UtcNow;
        var activation = new VoidwrightStageTwoActivation { RunId = new string('a', 32), FixtureId = new string('b', 32),
            OperationId = new string('c', 32), BootstrapNonce = new string('d', 32),
            SessionId = new string('e', 32), ExporterRunId = new string('f', 32), Tick = 40,
            Fingerprint = new string('1', 64), CoreId = 1001, GridId = 2002, ControllerId = 3003,
            CameraId = 4004, SuspensionIds = "5005,6006", ContextPath = "/tmp/run/preflight-context-v1.txt",
            ContextSha256 = new string('2', 64), Challenge = new string('3', 32),
            IssuedUtc = now.AddSeconds(-1), ExpiresUtc = now.AddSeconds(30) };
        var wire = activation.Format();
        Check(VoidwrightStageTwoActivation.TryParse(wire, now, out var parsed), "valid stage-two activation was rejected");
        Check(parsed.OperationId == activation.OperationId, "stage-two activation identity changed on parse");
        activation.Action = "direct-maneuver";
        var directWire = activation.Format();
        Check(VoidwrightStageTwoActivation.TryParse(directWire, now, out var directParsed) &&
              directParsed.Action == "direct-maneuver" && directParsed.SessionId == activation.SessionId,
            "same-session direct-maneuver activation did not round-trip");
        Check(!VoidwrightStageTwoActivation.TryParse(directWire.Replace("|direct-maneuver\n", "|arbitrary\n"), now, out _),
            "stage-two activation accepted an unsupported action");
        activation.Action = "preflight-only";
        wire = activation.Format();
        Check(!VoidwrightStageTwoActivation.ValidIds("5005,5005"), "duplicate wheel identities were accepted");
        var receipt = new VoidwrightDiscoveryReceipt { Status = "host-ready", RunId = activation.RunId,
            FixtureId = activation.FixtureId, BootstrapNonce = activation.BootstrapNonce, SessionId = activation.SessionId,
            ExporterRunId = activation.ExporterRunId, Tick = activation.Tick, CapturedUtc = now,
            Fingerprint = activation.Fingerprint, ObservedGridCount = 1 };
        Check(activation.Matches(receipt, now, "host-ready"), "matching host receipt was rejected");
        receipt.Tick = activation.Tick + 300;
        receipt.Fingerprint = new string('4', 64);
        Check(activation.MatchesCurrentDiscovery(receipt, now, "host-ready") &&
              !activation.Matches(receipt, now, "host-ready"),
            "fresh current receipt with an advanced tick and updated snapshot fingerprint was rejected or treated as the original snapshot");
        receipt.Tick = activation.Tick - 1;
        Check(!activation.MatchesCurrentDiscovery(receipt, now, "host-ready"),
            "a discovery receipt older than the activation snapshot was accepted");
        receipt.Tick = activation.Tick + 300;
        receipt.SessionId = new string('9', 32);
        Check(!activation.MatchesCurrentDiscovery(receipt, now, "host-ready"),
            "a tick-advanced receipt from another game session was accepted");
        receipt.SessionId = activation.SessionId;
        receipt.CapturedUtc = now.AddSeconds(-16);
        Check(!activation.MatchesCurrentDiscovery(receipt, now, "host-ready"),
            "a stale tick-advanced receipt was accepted");
        receipt.CapturedUtc = now;
        receipt.Tick = activation.Tick;
        receipt.Fingerprint = activation.Fingerprint;
        var replayWire = wire.Replace(activation.RunId, new string('9', 32));
        Check(VoidwrightStageTwoActivation.TryParse(replayWire, now, out var replay) &&
              !replay.Matches(receipt, now, "host-ready"), "activation replay under another run was accepted");
        receipt.SessionId = new string('9', 32);
        Check(!activation.Matches(receipt, now, "host-ready"), "cross-session host receipt was accepted");
        Check(!VoidwrightStageTwoActivation.TryParse(wire, now.AddMinutes(2), out _), "expired activation was accepted");
    }

    private static void HostApprovalBindsHeartbeat()
    {
        var now = DateTime.UtcNow;
        var heartbeat = new VoidwrightPreflightHeartbeat
        {
            RunId = "0123456789abcdef0123456789abcdef",
            FixtureId = "11111111111111111111111111111111",
            OperationId = "22222222222222222222222222222222",
            ContextSha256 = new string('a', 64), Challenge = "33333333333333333333333333333333",
            SessionId = "44444444444444444444444444444444", ControllerId = 12345, GridId = 456,
            AgentCoreId = TestAgentCoreId, CameraId = 67890, SimulationTick = 100, PublishedUtc = now,
            DiscoveryRunId = new string('d', 32), DiscoveryTick = 900,
            DiscoveryFingerprint = new string('e', 64),
            SensorValid = true, SensorScanTick = 99, SensorScanUtc = now, SensorGridId = 456,
            SensorCameraId = 67890, ValidRayCount = VoidwrightPreflightHeartbeat.RayCount,
            WheelCount = 6, SteeringWheelCount = 2, PropulsionWheelCount = 4, ActuatorReady = true,
            Clearances = Enumerable.Repeat(8.0, VoidwrightPreflightHeartbeat.RayCount).ToArray()
        };
        var approval = new VoidwrightPreflightApproval
        {
            RunId = heartbeat.RunId, FixtureId = heartbeat.FixtureId, OperationId = heartbeat.OperationId,
            ContextSha256 = heartbeat.ContextSha256, Challenge = heartbeat.Challenge,
            SessionId = heartbeat.SessionId, ControllerId = heartbeat.ControllerId, GridId = heartbeat.GridId,
            CameraId = heartbeat.CameraId, SimulationTick = heartbeat.SimulationTick,
            SensorScanTick = heartbeat.SensorScanTick, AgentCoreId = heartbeat.AgentCoreId,
            DiscoveryRunId = heartbeat.DiscoveryRunId, DiscoveryTick = heartbeat.DiscoveryTick,
            DiscoveryFingerprint = heartbeat.DiscoveryFingerprint,
            PublishedUtc = now.AddMilliseconds(100)
        };
        var heartbeatWire = heartbeat.Format();
        Check(heartbeatWire.StartsWith("VWPF4\n", StringComparison.Ordinal) &&
              VoidwrightPreflightHeartbeat.TryParse(heartbeatWire, out var parsedHeartbeat) &&
              parsedHeartbeat.DiscoveryRunId == heartbeat.DiscoveryRunId &&
              parsedHeartbeat.DiscoveryTick == heartbeat.DiscoveryTick &&
              parsedHeartbeat.DiscoveryFingerprint == heartbeat.DiscoveryFingerprint,
            "heartbeat did not round-trip the exact discovery run, tick, and fingerprint");
        Check(heartbeat.IsFreshAndBound(now.AddMilliseconds(10), TimeSpan.FromSeconds(1), heartbeat.RunId,
                heartbeat.FixtureId, heartbeat.OperationId, heartbeat.ContextSha256, heartbeat.Challenge,
                heartbeat.SessionId, heartbeat.ControllerId, heartbeat.GridId, heartbeat.CameraId,
                heartbeat.AgentCoreId, heartbeat.DiscoveryRunId, heartbeat.DiscoveryTick, heartbeat.DiscoveryFingerprint) &&
              !heartbeat.IsFreshAndBound(now.AddMilliseconds(10), TimeSpan.FromSeconds(1), heartbeat.RunId,
                heartbeat.FixtureId, heartbeat.OperationId, heartbeat.ContextSha256, heartbeat.Challenge,
                heartbeat.SessionId, heartbeat.ControllerId, heartbeat.GridId, heartbeat.CameraId,
                heartbeat.AgentCoreId, heartbeat.DiscoveryRunId, heartbeat.DiscoveryTick + 1, heartbeat.DiscoveryFingerprint),
            "heartbeat freshness accepted a different discovery tick");
        var serialized = approval.Format();
        VoidwrightPreflightApproval parsed;
        Check(VoidwrightPreflightApproval.TryAccept(serialized, now.AddMilliseconds(200),
            TimeSpan.FromSeconds(1), heartbeat, out parsed), "fresh host acknowledgement rejected exact mod heartbeat");
        Check(!VoidwrightPreflightApproval.TryAccept(serialized, now.AddSeconds(2),
            TimeSpan.FromSeconds(1), heartbeat, out parsed), "stale host acknowledgement remained valid");
        Check(!VoidwrightPreflightApproval.TryAccept(serialized.Replace("|100|99|", "|101|99|"),
            now.AddMilliseconds(200), TimeSpan.FromSeconds(1), heartbeat, out parsed),
            "approval for a different mod heartbeat tick was accepted");
        approval.AgentCoreId++;
        Check(!VoidwrightPreflightApproval.TryAccept(approval.Format(),
            now.AddMilliseconds(200), TimeSpan.FromSeconds(1), heartbeat, out parsed),
            "approval for a different Agent Core was accepted");
        approval.AgentCoreId--;
        approval.DiscoveryFingerprint = new string('f', 64);
        Check(!VoidwrightPreflightApproval.TryAccept(approval.Format(), now.AddMilliseconds(200),
            TimeSpan.FromSeconds(1), heartbeat, out parsed),
            "approval for a different Agent Core discovery fingerprint was accepted");
        approval.DiscoveryFingerprint = heartbeat.DiscoveryFingerprint;
        var priorHeartbeatApproval = new VoidwrightPreflightApproval
        {
            RunId = heartbeat.RunId, FixtureId = heartbeat.FixtureId, OperationId = heartbeat.OperationId,
            ContextSha256 = heartbeat.ContextSha256, Challenge = heartbeat.Challenge,
            SessionId = heartbeat.SessionId, ControllerId = heartbeat.ControllerId, GridId = heartbeat.GridId,
            CameraId = heartbeat.CameraId, SimulationTick = 90, SensorScanTick = 89,
            AgentCoreId = heartbeat.AgentCoreId,
            DiscoveryRunId = heartbeat.DiscoveryRunId, DiscoveryTick = heartbeat.DiscoveryTick,
            DiscoveryFingerprint = heartbeat.DiscoveryFingerprint,
            PublishedUtc = now.AddMilliseconds(100)
        }.Format();
        heartbeat.SimulationTick = 100;
        heartbeat.SensorScanTick = 99;
        heartbeat.PublishedUtc = now.AddMilliseconds(200);
        Check(!VoidwrightPreflightApproval.TryAccept(priorHeartbeatApproval, now.AddMilliseconds(300),
            TimeSpan.FromSeconds(1), heartbeat, out parsed),
            "approval for a prior heartbeat authorized the current heartbeat");
        Check(VoidwrightPreflightApproval.IsPendingForNewerHeartbeat(priorHeartbeatApproval,
            now.AddMilliseconds(300), TimeSpan.FromSeconds(1), heartbeat, 10),
            "fresh same-run approval for the immediately prior heartbeat was not classified as pending");
        Check(!VoidwrightPreflightApproval.IsPendingForNewerHeartbeat(
            priorHeartbeatApproval.Replace(heartbeat.RunId, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"),
            now.AddMilliseconds(300), TimeSpan.FromSeconds(1), heartbeat, 10),
            "prior approval from another run was classified as pending");
        Check(!VoidwrightPreflightApproval.IsPendingForNewerHeartbeat(priorHeartbeatApproval,
            now.AddMilliseconds(300), TimeSpan.FromSeconds(1), heartbeat, 9),
            "approval older than the bounded heartbeat lag was classified as pending");
        heartbeat.ActuatorReady = false;
        Check(!VoidwrightPreflightApproval.TryAccept(serialized, now.AddMilliseconds(200),
            TimeSpan.FromSeconds(1), heartbeat, out parsed), "approval passed without actuator readiness");
    }

    private static void SensorPreflightBindsCurrentGridAndCamera()
    {
        var now = DateTime.UtcNow;
        bool Fresh(long tick = 100, long scanTick = 99, DateTime? scanUtc = null,
            int validRays = 19, long controllerGrid = 456, long cameraGrid = 456,
            long scanGrid = 456, long currentCamera = 67890, long pinnedCamera = 67890,
            long scanCamera = 67890, TimeSpan? maximumAge = null, long maximumTickAge = 20) =>
            SensorPreflightFreshness.IsFresh(now, tick, scanTick, scanUtc ?? now,
                validRays, 19, controllerGrid, cameraGrid, scanGrid,
                currentCamera, pinnedCamera, scanCamera,
                maximumAge ?? TimeSpan.FromSeconds(1), maximumTickAge);

        Check(Fresh(), "fresh scan from the current pinned same-grid camera was rejected");
        Check(!Fresh(controllerGrid: 456, cameraGrid: 456, scanGrid: 789),
            "scan from a previously attached grid authorized movement");
        Check(!Fresh(controllerGrid: 456, cameraGrid: 789, scanGrid: 456),
            "camera on another current grid authorized movement");
        Check(!Fresh(scanCamera: 67891), "scan from a different camera authorized movement");
        Check(!Fresh(currentCamera: 67891), "replacement camera bypassed the run-pinned camera identity");
        Check(!Fresh(validRays: 18), "partial scan authorized movement");
        Check(!Fresh(scanTick: 79), "scan older than the configured simulation-tick window authorized movement");
        Check(!Fresh(scanTick: 101), "future scan tick authorized movement");
        Check(!Fresh(scanUtc: now.AddMilliseconds(-1001)), "wall-clock stale scan authorized movement");
        Check(!Fresh(scanUtc: now.AddMilliseconds(1)), "future-dated sensor evidence authorized movement");
        Check(!Fresh(controllerGrid: 0), "unknown controller grid authorized movement");
    }

    private static void ActuatorFailureDoesNotDispatch()
    {
        var now = DateTime.UtcNow;
        foreach (var failedName in new[] { "actuator_availability", "movement_interface_readiness" })
        {
            var checks = new List<PreflightCheck>();
            foreach (var name in new[] { "fixture_source_integrity", "runtime_connectivity", "vehicle_identity",
                         "voidwright_module_and_session_marker", "controller_readiness", "actuator_availability",
                         "sensor_validity_freshness", "position_validity_freshness", "safety_configuration",
                         "movement_interface_readiness" })
                checks.Add(new PreflightCheck(name, name == failedName ? PreflightStatus.Fail : PreflightStatus.Pass,
                    "fixture", "fixture", "test", now));
            var preflight = new MovementPreflight(PreflightRunId, PreflightOperationId, PreflightFixture,
                PreflightState, now, checks);
            var calls = 0;
            Check(!CanDispatch(preflight, now, movement: () => calls++), failedName + " failure authorized dispatch");
            Check(calls == 0, failedName + " failure invoked the actuator callback");
        }
    }

    private static string FileSha(string path)
    {
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static string TreeSha(string root)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using (var sha = SHA256.Create())
        {
            var separator = new byte[] { 0 };
            var buffer = new byte[65536];
            foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal))
            {
                var relative = file.Substring(fullRoot.Length).Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
                var name = Encoding.UTF8.GetBytes(relative);
                sha.TransformBlock(name, 0, name.Length, name, 0);
                sha.TransformBlock(separator, 0, 1, separator, 0);
                using (var stream = File.OpenRead(file))
                {
                    int count;
                    while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
                        sha.TransformBlock(buffer, 0, count, buffer, 0);
                }
                sha.TransformBlock(separator, 0, 1, separator, 0);
            }
            sha.TransformFinalBlock(new byte[0], 0, 0);
            return BitConverter.ToString(sha.Hash).Replace("-", string.Empty).ToLowerInvariant();
        }
    }

    private static void DepthStartupSettlingUsesFreshPositionTelemetry()
    {
        Check(DepthStartupSettlePolicy.IsStable(0.053, true, 0.002),
            "fresh near-stationary displacement was rejected due to small reported-speed disagreement");
        Check(!DepthStartupSettlePolicy.IsStable(0.081, true, 0.002),
            "fresh displacement accepted reported speed above its safety bound");
        Check(!DepthStartupSettlePolicy.IsStable(0.053, true, 0.051),
            "fresh displacement accepted motion above the settlement threshold");
        Check(!DepthStartupSettlePolicy.IsStable(0.053, false, 0.002),
            "stale position telemetry was used instead of reported speed");
        Check(DepthStartupSettlePolicy.IsStable(0.049, false, double.NaN),
            "reported-speed fallback rejected a safe stationary reading");
    }

    private static void LandRoutesAroundObstacle()
    {
        var heights = new int[9, 7];
        var blocked = new bool[9, 7];
        for (var z = 1; z < 6; z++) blocked[4, z] = true;
        var vehicle = new VehicleProfile(MobilityClass.Land, 0, 0, 0, 1, 1, 0.25);
        var route = new RoutePlanner().FindRoute(
            new LandTraversalModel(heights, blocked, vehicle),
            new GridPoint(1, 0, 3), new GridPoint(7, 0, 3), 500);
        Check(route.Status == RouteStatus.Found, "route was not found");
        Check(route.Points.All(point => !blocked[point.X, point.Z]), "route crosses obstruction");
    }

    private static void LandRejectsStep()
    {
        var heights = new int[3, 1];
        heights[1, 0] = 2;
        var vehicle = new VehicleProfile(MobilityClass.Land, 0, 0, 0, 1, 1, 0);
        var route = new RoutePlanner().FindRoute(
            new LandTraversalModel(heights, new bool[3, 1], vehicle),
            new GridPoint(0, 0, 0), new GridPoint(2, 0, 0), 20);
        Check(route.Status == RouteStatus.NoPath, "vehicle climbed an excessive step");
    }

    private static void AirRoutesOverWall()
    {
        var blocked = new bool[7, 5, 3];
        for (var y = 0; y < 3; y++) for (var z = 0; z < 3; z++) blocked[3, y, z] = true;
        var vehicle = new VehicleProfile(MobilityClass.Air, 0, 0, 0, 0, 0, 0.1);
        var route = new RoutePlanner().FindRoute(
            new AirTraversalModel(blocked, vehicle),
            new GridPoint(1, 1, 1), new GridPoint(5, 1, 1), 1000);
        Check(route.Status == RouteStatus.Found, "3D route was not found");
        Check(route.Points.Any(point => point.Y >= 3), "route did not use vertical clearance");
    }

    private static void AirRespectsClearance()
    {
        var blocked = new bool[7, 5, 5];
        for (var y = 0; y < 5; y++)
        {
            blocked[3, y, 0] = true;
            blocked[3, y, 2] = true;
            blocked[3, y, 3] = true;
            blocked[3, y, 4] = true;
        }
        var vehicle = new VehicleProfile(MobilityClass.Air, 0, 0, 1, 0, 0, 0);
        var route = new RoutePlanner().FindRoute(
            new AirTraversalModel(blocked, vehicle),
            new GridPoint(1, 2, 2), new GridPoint(5, 2, 2), 1000);
        Check(route.Status == RouteStatus.NoPath, "oversized vehicle used a one-cell gap");
    }

    private static void BudgetIsBounded()
    {
        var model = new AirTraversalModel(new bool[20, 20, 20],
            new VehicleProfile(MobilityClass.Air, 0, 0, 0, 0, 0, 0));
        var route = new RoutePlanner().FindRoute(model,
            new GridPoint(0, 0, 0), new GridPoint(19, 19, 19), 1);
        Check(route.Status == RouteStatus.SearchBudgetExceeded, "budget was not enforced");
        Check(route.ExpandedNodes == 1, "expanded node count is inaccurate");
    }

    private static void PlannerPreservesApproachHeading()
    {
        var route = new RoutePlanner().FindRoute(
            new HeadingTrapModel(),
            new GridPoint(0, 0, 0), new GridPoint(2, 0, 0), 20);
        Check(route.Status == RouteStatus.Found, "heading-aware route was not found");
        Check(Math.Abs(route.Cost - 3) < 0.000001, "planner discarded the better arrival heading");
        Check(route.Points.Count == 4 && route.Points[1] == new GridPoint(0, 0, 1),
            "planner selected the cheap arrival with the expensive exit");
    }

    private static void InvalidInputFailsClosed()
    {
        var planner = new RoutePlanner();
        var point = new GridPoint(0, 0, 0);
        Check(planner.FindRoute(null!, point, point, 10).Status == RouteStatus.InvalidEndpoint,
            "null model did not fail closed");
        var model = new AirTraversalModel(new bool[1, 1, 1],
            new VehicleProfile(MobilityClass.Air, -1, -1, -1, -1, -1, -1));
        Check(planner.FindRoute(model, point, point, 0).Status == RouteStatus.SearchBudgetExceeded,
            "invalid budget did not fail closed");
    }

    private static void MobilityClassificationIsConservative()
    {
        Check(MobilityClassifier.Classify(0, 0) == MobilityClassification.Unknown, "empty grid was classified");
        Check(MobilityClassifier.Classify(1, 0) == MobilityClassification.Unknown, "single wheel was called LAND");
        Check(MobilityClassifier.Classify(4, 0) == MobilityClassification.Land, "rover was not LAND");
        Check(MobilityClassifier.Classify(0, 6) == MobilityClassification.Air, "thruster craft was not AIR");
        Check(MobilityClassifier.Classify(4, 6) == MobilityClassification.Hybrid, "hybrid evidence was flattened");
    }

    private static void LandGuidancePointsTowardTarget()
    {
        var command = new RouteFollower().FollowLocalTarget(
            MobilityClass.Land, 2, 0, 10, 0, 20, 1);
        Check(command.Status == GuidanceStatus.Tracking, "land target was not tracked");
        Check(command.Forward > 0, "land guidance did not request forward motion");
        Check(command.Yaw > 0, "right-hand target did not request a right turn");
        Check(command.Right == 0 && command.Up == 0, "land guidance leaked lateral thrust");
    }

    private static void LandGuidanceTurnsBeforeAccelerating()
    {
        var command = new RouteFollower().FollowLocalTarget(
            MobilityClass.Land, 10, 0, 0, 0, 20, 1);
        Check(command.Yaw == 1, "side target did not request maximum bounded steering");
        Check(command.Forward == 0, "land guidance accelerated while perpendicular to target");
    }

    private static void AirGuidanceIsThreeDimensional()
    {
        var command = new RouteFollower().FollowLocalTarget(
            MobilityClass.Air, 4, 3, 12, 0, 20, 1);
        Check(command.Status == GuidanceStatus.Tracking, "air target was not tracked");
        Check(command.Forward > 0 && command.Right > 0 && command.Up > 0,
            "air guidance lost an axis");
        Check(Math.Abs(command.Forward) <= 1 && Math.Abs(command.Right) <= 1 &&
              Math.Abs(command.Up) <= 1 && Math.Abs(command.Yaw) <= 1,
            "air guidance escaped normalized bounds");
    }

    private static void LandArrivalIgnoresVerticalOffset()
    {
        var command = new RouteFollower().FollowLocalTarget(
            MobilityClass.Land, 0.2, 8, 0.2, 3, 20, 1);
        Check(command.Status == GuidanceStatus.Arrived, "arrival was not recognized");
        Check(command.Forward == 0 && command.Yaw == 0, "arrival command was not neutral");
    }

    private static void LandApproachIgnoresVerticalOffset()
    {
        var follower = new RouteFollower();
        var elevated = follower.FollowLocalTarget(
            MobilityClass.Land, 3, 8, 7, 0, 20, 1);
        var level = follower.FollowLocalTarget(
            MobilityClass.Land, 3, 0, 7, 0, 20, 1);
        Check(elevated.Status == GuidanceStatus.Tracking && level.Status == GuidanceStatus.Tracking,
            "ground target was not tracked");
        Check(Math.Abs(elevated.Forward - level.Forward) < 0.000001 &&
              Math.Abs(elevated.Yaw - level.Yaw) < 0.000001,
            "unreachable vertical offset changed land guidance");
    }

    private static void AirArrivalRemainsThreeDimensional()
    {
        var command = new RouteFollower().FollowLocalTarget(
            MobilityClass.Air, 0.2, 8, 0.2, 3, 20, 1);
        Check(command.Status == GuidanceStatus.Tracking,
            "aircraft incorrectly ignored vertical distance at arrival");
        Check(command.Up > 0, "aircraft lost vertical guidance");
    }

    private static void GuidanceRejectsNonFiniteInput()
    {
        var command = new RouteFollower().FollowLocalTarget(
            MobilityClass.Air, double.NaN, 0, 10, 0, 20, 1);
        Check(command.Status == GuidanceStatus.Invalid, "non-finite target was accepted");
        Check(command.Forward == 0 && command.Right == 0 && command.Up == 0 && command.Yaw == 0,
            "invalid guidance was not neutral");
    }

    private static void GuidanceFuzzIsBoundedAndDeterministic()
    {
        var random = new Random(8826);
        var follower = new RouteFollower();
        for (var index = 0; index < 10000; index++)
        {
            var mobility = index % 2 == 0 ? MobilityClass.Land : MobilityClass.Air;
            var right = (random.NextDouble() * 2000) - 1000;
            var up = (random.NextDouble() * 2000) - 1000;
            var forward = (random.NextDouble() * 2000) - 1000;
            var speed = (random.NextDouble() * 200) - 50;
            var first = follower.FollowLocalTarget(mobility, right, up, forward, speed, 100, 0.5);
            var second = follower.FollowLocalTarget(mobility, right, up, forward, speed, 100, 0.5);
            Check(first.Status == second.Status && first.Forward == second.Forward &&
                  first.Right == second.Right && first.Up == second.Up && first.Yaw == second.Yaw,
                "guidance changed for identical input");
            Check(Math.Abs(first.Forward) <= 1 && Math.Abs(first.Right) <= 1 &&
                  Math.Abs(first.Up) <= 1 && Math.Abs(first.Yaw) <= 1,
                "fuzz guidance escaped normalized bounds");
        }
    }

    private static void DepthSteeringPreservesClearTarget()
    {
        var angles = DepthFanAngles();
        var distances = new double[angles.Length];
        for (var index = 0; index < distances.Length; index++) distances[index] = 8;
        var targetHeading = 0.13;
        var decision = DepthAvoidancePolicy.SelectHeading(targetHeading, angles, distances,
            8, 3.5, 0);
        Check(decision.Status == DepthAvoidanceStatus.Clear, "open corridor was not classified clear");
        Check(Math.Abs(decision.HeadingRadians - targetHeading) < 0.000001,
            "clear corridor changed target heading");
    }

    private static void LocalVehicleTestObstacleStopIsFailClosed()
    {
        var clear = Enumerable.Repeat(4.0, 9).ToArray();
        var decision = LocalVehicleTestObstacleStopPolicy.Evaluate(clear, 4.0, 4.0, true);
        Check(decision.Status == LocalVehicleTestObstacleStatus.Clear &&
              LocalVehicleTestObstacleStopPolicy.MayContinue(decision),
            "complete clear fan did not permit the bounded test to continue");

        var wall = (double[])clear.Clone();
        wall[4] = 1.75;
        decision = LocalVehicleTestObstacleStopPolicy.Evaluate(wall, 4.0, 4.0, true);
        Check(decision.Status == LocalVehicleTestObstacleStatus.Obstacle &&
              !LocalVehicleTestObstacleStopPolicy.MayContinue(decision) &&
              Math.Abs(decision.NearestClearanceMeters - 1.75) < 0.000001,
            "near wall in the center fan did not require an automatic stop");

        var sideWall = (double[])clear.Clone();
        sideWall[0] = 0.8;
        decision = LocalVehicleTestObstacleStopPolicy.Evaluate(sideWall, 4.0, 4.0, true);
        Check(decision.Status == LocalVehicleTestObstacleStatus.Obstacle,
            "obstacle in the swept side lane did not require a stop");

        decision = LocalVehicleTestObstacleStopPolicy.Evaluate(clear, 4.0, 4.0, false);
        Check(decision.Status == LocalVehicleTestObstacleStatus.Unknown &&
              !LocalVehicleTestObstacleStopPolicy.MayContinue(decision),
            "incomplete ray fan was treated as clear");
        decision = LocalVehicleTestObstacleStopPolicy.Evaluate(new[] { 4.0, double.NaN, 4.0 }, 4.0, 4.0, true);
        Check(decision.Status == LocalVehicleTestObstacleStatus.InvalidScan &&
              !LocalVehicleTestObstacleStopPolicy.MayContinue(decision),
            "invalid ray result was treated as clear");
    }

    private static void DepthSteeringRetainsDetourThroughIntermittentClearScans()
    {
        var angles = DepthFanAngles();
        var blocked = Enumerable.Repeat(8.0, angles.Length).ToArray();
        blocked[8] = blocked[9] = blocked[10] = 1.5;
        var decision = DepthAvoidancePolicy.SelectHeading(0, angles, blocked, 8, 3.4, 0);
        Check(decision.Status == DepthAvoidanceStatus.Avoid && Math.Abs(decision.HeadingRadians) > 0.08,
            "initial blocked scan did not start a side detour");

        var intermittentOpening = Enumerable.Repeat(8.0, angles.Length).ToArray();
        intermittentOpening[9] = intermittentOpening[10] = intermittentOpening[11] = 4.765;
        var retainedHeading = decision.HeadingRadians;
        decision = DepthAvoidancePolicy.SelectHeading(10 * Math.PI / 180.0, angles,
            intermittentOpening, 8, 3.4, retainedHeading, 0);
        Check(decision.Status == DepthAvoidanceStatus.Avoid &&
              Math.Abs(decision.HeadingRadians - retainedHeading) < 0.000001 &&
              decision.ConsecutiveClearFrames == 1,
            "one marginal clear scan released the detour");

        decision = DepthAvoidancePolicy.SelectHeading(10 * Math.PI / 180.0, angles,
            blocked, 8, 3.4, decision.HeadingRadians, decision.ConsecutiveClearFrames);
        Check(decision.Status == DepthAvoidanceStatus.Avoid && decision.ConsecutiveClearFrames == 0,
            "a reblocked direct corridor did not reset clear-frame hysteresis");
        retainedHeading = decision.HeadingRadians;

        for (var frame = 1; frame < 3; frame++)
        {
            decision = DepthAvoidancePolicy.SelectHeading(10 * Math.PI / 180.0, angles,
                intermittentOpening, 8, 3.4, retainedHeading, decision.ConsecutiveClearFrames);
            Check(decision.Status == DepthAvoidanceStatus.Avoid,
                "detour was released before three consecutive clear scans");
            Check(Math.Abs(decision.HeadingRadians - retainedHeading) < 0.000001,
                "detour heading changed while its corridor remained safe");
        }

        decision = DepthAvoidancePolicy.SelectHeading(10 * Math.PI / 180.0, angles,
            intermittentOpening, 8, 3.4, retainedHeading, decision.ConsecutiveClearFrames);
        Check(decision.Status == DepthAvoidanceStatus.Clear &&
              Math.Abs(decision.HeadingRadians - 10 * Math.PI / 180.0) < 0.000001,
            "three consecutive clear scans did not release the detour");
    }

    private static void DepthSteeringAvoidsBlockedCenter()
    {
        var angles = DepthFanAngles();
        var distances = new double[angles.Length];
        for (var index = 0; index < distances.Length; index++) distances[index] = 8;
        distances[8] = distances[9] = distances[10] = 1.5;
        var decision = DepthAvoidancePolicy.SelectHeading(0, angles, distances, 8, 3.5, 0);
        Check(decision.Status == DepthAvoidanceStatus.Avoid, "blocked center did not select a detour");
        Check(Math.Abs(decision.HeadingRadians) >= Math.PI / 180 * 10,
            "detour still points into the blocked center");
        var corridorIndex = Array.IndexOf(angles, decision.HeadingRadians);
        Check(corridorIndex > 0 && corridorIndex < angles.Length - 1 &&
              distances[corridorIndex - 1] >= 3.5 && distances[corridorIndex] >= 3.5 &&
              distances[corridorIndex + 1] >= 3.5,
            "selected detour lacked three-beam clearance");
    }

    private static void DepthGridBindingPinsExactGrid()
    {
        Check(DepthGridBindingPolicy.PinExpectedGrid(0, true, true, 73396920176713659, true) == 73396920176713659,
            "depth mode did not pin the resolved controller grid after confirming its camera is on that grid");
        Check(DepthGridBindingPolicy.PinExpectedGrid(73396920176713659, true, true, 999, true) == 73396920176713659,
            "depth mode rebound an already pinned operation to a different grid");
        Check(DepthGridBindingPolicy.PinExpectedGrid(0, true, true, 73396920176713659, false) == 0,
            "depth mode pinned a grid before confirming the camera shares it");
        Check(DepthGridBindingPolicy.PinExpectedGrid(0, true, false, 0, false) == 0,
            "depth mode pinned an unresolved controller");
        Check(DepthGridBindingPolicy.PinExpectedGrid(0, false, true, 73396920176713659, true) == 0,
            "ordinary navigation changed its existing grid binding semantics");
    }

    private static void CockpitControllerBindingIsExact()
    {
        const long grid = 105034224363529183;
        const long controller = 83468039688685570;
        const long camera = 73900203231573625;
        Check(VehicleControllerBindingPolicy.IsExact(true, controller, controller,
                grid, grid, true, camera, camera, grid),
            "the common ship-controller binding rejected the exact discovered cockpit, grid, or camera");
        Check(!VehicleControllerBindingPolicy.IsExact(false, controller, controller,
                grid, grid, true, camera, camera, grid),
            "an entity that did not resolve as a ship controller was accepted");
        Check(!VehicleControllerBindingPolicy.IsExact(true, controller, controller,
                grid, grid + 1, true, camera, camera, grid + 1),
            "a controller and camera on another grid were accepted");
        Check(!VehicleControllerBindingPolicy.IsExact(true, controller, controller + 1,
                grid, grid, true, camera, camera, grid),
            "a different controller ID was accepted");
    }

    private static void PreflightOnlyRoutingDoesNotDependOnOwnerEnvironment()
    {
        Check(ModOwnedCommandRoutingPolicy.ShouldHostReview("preflight-only", null),
            "preflight-only was not routed to the host review loop when owner metadata was absent");
        Check(ModOwnedCommandRoutingPolicy.ShouldHostReview("preflight-only", "voidwright-plugin"),
            "preflight-only was incorrectly rejected because of an unrelated motion-owner value");
        Check(ModOwnedCommandRoutingPolicy.ShouldHostReview("drive-to-coordinate-depth", "voidwright-mod"),
            "mod-owned depth movement was not routed to the host review loop");
        Check(!ModOwnedCommandRoutingPolicy.ShouldHostReview("drive-to-coordinate-depth", "voidwright-plugin"),
            "depth movement bypassed its explicit mod-owner requirement");
        Check(!ModOwnedCommandRoutingPolicy.ShouldHostReview("unknown-command", "voidwright-mod"),
            "unknown movement command was accepted by the host review route");
    }

    private static void DepthSteeringStopsWhenBlocked()
    {
        var angles = DepthFanAngles();
        var distances = new double[angles.Length];
        for (var index = 0; index < distances.Length; index++) distances[index] = 1;
        var decision = DepthAvoidancePolicy.SelectHeading(0, angles, distances, 8, 3.5, 0);
        Check(decision.Status == DepthAvoidanceStatus.NoClearCorridor,
            "fully blocked fan did not request a safe stop");
    }

    private static void DepthSteeringPrefersClearerDetour()
    {
        var angles = DepthFanAngles();
        var distances = new double[angles.Length];
        for (var index = 0; index < distances.Length; index++) distances[index] = 8;
        for (var index = 4; index <= 10; index++) distances[index] = 1.5;
        distances[11] = 3.8;
        distances[12] = 3.8;
        var decision = DepthAvoidancePolicy.SelectHeading(0, angles, distances, 8, 3.5, 0);
        Check(decision.Status == DepthAvoidanceStatus.Avoid,
            "the one-sided opening was not selected");
        Check(Math.Abs(decision.HeadingRadians - angles[14]) < 0.000001,
            "the controller did not commit toward the corridor with more clearance");
    }

    private static void DepthSteeringPicksOpenOppositeSide()
    {
        var angles = DepthFanAngles();
        var distances = new[]
        {
            8.0, 8.0, 8.0, 2.046, 2.106, 2.239, 2.471, 2.853, 3.501, 3.732,
            3.494, 3.381, 3.374, 3.473, 3.693, 4.076, 8.0, 8.0, 8.0
        };
        var targetBearing = -37 * Math.PI / 180.0;
        var decision = DepthAvoidancePolicy.SelectHeading(
            targetBearing, angles, distances, 8, 3.40, 0);
        Check(decision.Status == DepthAvoidanceStatus.Avoid,
            "the measured right-side corridor was rejected");
        Check(Math.Abs(decision.HeadingRadians - 80 * Math.PI / 180.0) < 0.000001,
            "the selected heading did not turn away from the blocked target sector");

        var wallApproachFan = new[]
        {
            8.0, 8.0, 8.0, 4.211, 3.695, 3.384, 3.211, 3.146, 3.178, 3.312,
            3.572, 4.007, 4.728, 5.986, 8.0, 8.0, 8.0, 8.0, 8.0
        };
        decision = DepthAvoidancePolicy.SelectHeading(-0.174, angles, wallApproachFan,
            8, 3.40, 0);
        Check(decision.Status == DepthAvoidanceStatus.Avoid &&
              Math.Abs(decision.HeadingRadians - 60 * Math.PI / 180.0) < 0.000001,
            "wider fan did not choose the expected open corridor: " + decision.Status +
            " heading=" + decision.HeadingRadians.ToString("F3"));

        distances[13] = 3.39;
        distances[14] = 3.39;
        distances[15] = 3.39;
        distances[16] = 3.39;
        decision = DepthAvoidancePolicy.SelectHeading(
            targetBearing, angles, distances, 8, 3.40, 0);
        Check(decision.Status == DepthAvoidanceStatus.NoClearCorridor,
            "a corridor below the measured clearance threshold was accepted");
    }

    private static void DepthSteeringStopsOutOfView()
    {
        var angles = DepthFanAngles();
        var distances = new double[angles.Length];
        for (var index = 0; index < distances.Length; index++) distances[index] = 8;
        var decision = DepthAvoidancePolicy.SelectHeading(1.7, angles, distances, 8, 3.5, 0);
        Check(decision.Status == DepthAvoidanceStatus.TargetOutOfView,
            "target outside the front-camera fan was not rejected");
    }

    private static void DepthSteeringRejectsInvalidScan()
    {
        var angles = DepthFanAngles();
        var distances = new double[angles.Length];
        for (var index = 0; index < distances.Length; index++) distances[index] = 8;
        distances[3] = double.NaN;
        var decision = DepthAvoidancePolicy.SelectHeading(0, angles, distances, 8, 3.5, 0);
        Check(decision.Status == DepthAvoidanceStatus.InvalidScan,
            "non-finite depth reading was accepted");
    }

    private static void DepthPowerIsBounded()
    {
        Check(Math.Abs(DepthAvoidancePolicy.LimitWheelPower(0.1f) - 0.1f) < 0.000001f,
            "depth mode raised an already-conservative saved wheel power");
        Check(Math.Abs(DepthAvoidancePolicy.LimitWheelPower(1.0f) - 0.15f) < 0.000001f,
            "depth mode did not cap high saved wheel power");
        Check(DepthAvoidancePolicy.LimitWheelPower(float.NaN) == 0 &&
              DepthAvoidancePolicy.LimitWheelPower(-0.1f) == 0,
            "invalid saved wheel power did not fail closed");
    }

    private static void DepthDriveDutyIsBounded()
    {
        Check(Math.Abs(DepthAvoidancePolicy.LimitDriveDuty(0.1) - 0.1) < 0.000001,
            "depth mode did not preserve a lower requested pulse duty");
        Check(Math.Abs(DepthAvoidancePolicy.LimitDriveDuty(1.0) - 0.4) < 0.000001,
            "depth mode exceeded or missed its 40% pulse-duty cap");
        Check(DepthAvoidancePolicy.LimitDriveDuty(double.NaN) == 0 &&
              DepthAvoidancePolicy.LimitDriveDuty(double.PositiveInfinity) == 0 &&
              DepthAvoidancePolicy.LimitDriveDuty(-0.1) == 0,
            "invalid requested pulse duty did not fail closed");
        Check(Math.Abs(DepthAvoidancePolicy.SignedDriveDuty(0.2, false) - 0.2) < 0.000001 &&
              Math.Abs(DepthAvoidancePolicy.SignedDriveDuty(0.2, true) + 0.2) < 0.000001,
            "forward and reverse must write opposite propulsion signs at the same capped duty");
        Check(Math.Abs(DepthAvoidancePolicy.SignedDriveDuty(1.0, true) + 0.4) < 0.000001 &&
              DepthAvoidancePolicy.SignedDriveDuty(double.NaN, true) == 0,
            "reverse sign must preserve the duty cap and fail closed for invalid input");
    }

    private static void DepthSteeringUsesAvailableWheelAngle()
    {
        Check(Math.Abs(DepthAvoidancePolicy.SteeringCommand(Math.PI / 12, false) - 0.5) < 0.000001,
            "15-degree forward heading should turn the wheel toward the target");
        Check(DepthAvoidancePolicy.SteeringCommand(50 * Math.PI / 180.0, false) == 1,
            "large forward depth heading should request the configured maximum steering angle");
        Check(Math.Abs(DepthAvoidancePolicy.SteeringCommand(0.2, true) +
                       DepthAvoidancePolicy.SteeringCommand(0.2, false)) < 0.000001,
            "reverse travel should invert the forward wheel-steering mapping");
        Check(DepthAvoidancePolicy.SteeringCommand(double.NaN, false) == 0,
            "non-finite depth heading should not actuate steering");
    }

    private static void DepthArrivalShrinksPlanarRadius()
    {
        Check(DepthArrivalPolicy.TryGetPlanarRadius(0.5, 0.15, out var planarRadius),
            "valid world-space radius was rejected");
        Check(Math.Abs(planarRadius - Math.Sqrt(0.2275)) < 0.000001,
            "planar arrival radius did not account for vertical error");
    }

    private static void DepthArrivalRequiresStableNeutralEvidence()
    {
        Check(DepthArrivalPolicy.IsStableArrival(0.49, 0.05, 0.5, 0.1, true, true),
            "a neutral, fresh vehicle inside the world-space radius should qualify as stably arrived");
        Check(!DepthArrivalPolicy.IsStableArrival(0.51, 0.05, 0.5, 0.1, true, true),
            "arrival outside the world-space radius was accepted");
        Check(!DepthArrivalPolicy.IsStableArrival(0.1, 0.11, 0.5, 0.1, true, true),
            "moving too quickly through the radius was accepted as stable arrival");
        Check(!DepthArrivalPolicy.IsStableArrival(0.1, 0.05, 0.5, 0.1, false, true),
            "non-neutral wheel overrides were accepted as stable arrival");
        Check(!DepthArrivalPolicy.IsStableArrival(0.1, 0.05, 0.5, 0.1, true, false),
            "stale or unbound heartbeat was accepted as stable arrival");
        Check(!DepthArrivalPolicy.IsStableArrival(double.NaN, 0, 0.5, 0.1, true, true),
            "non-finite position distance was accepted as stable arrival");
    }

    private static void DepthArrivalCountsDistinctHostSamples()
    {
        Check(DepthArrivalPolicy.IsNewHostSample(11, 10),
            "a later host update was not accepted as a distinct arrival sample");
        Check(!DepthArrivalPolicy.IsNewHostSample(10, 10),
            "a repeated host update was counted twice for stable arrival");
        Check(!DepthArrivalPolicy.IsNewHostSample(9, 10),
            "an older host update was accepted as a stable arrival sample");
        Check(!DepthArrivalPolicy.IsNewHostSample(-1, -2),
            "an invalid host update tick was accepted");
    }

    private static void DepthArrivalRequiresWorldRadius()
    {
        Check(!DepthArrivalPolicy.IsWithinWorldRadius(0.499, 0.153, 0.5),
            "planar arrival was mistaken for world-space arrival");
        Check(DepthArrivalPolicy.IsWithinWorldRadius(0.47, 0.153, 0.5),
            "point inside the world-space sphere was rejected");
    }

    private static void DepthArrivalFailsClosed()
    {
        Check(!DepthArrivalPolicy.TryGetPlanarRadius(0.5, 0.6, out _),
            "unreachable vertical offset was treated as a valid ground-plane radius");
        Check(!DepthArrivalPolicy.TryGetPlanarRadius(0.5, double.NaN, out _),
            "non-finite vertical offset was accepted");
        Check(!DepthArrivalPolicy.IsWithinWorldRadius(0, 0, double.NaN),
            "non-finite arrival radius was accepted");
    }

    private static void WireframeProvidesThreeProfilesAndColors()
    {
        var model = new AgentWireframeModel { AgentId = "voidwright-core:42", CapturedSimulationTick = 1234 };
        model.Blocks.Add(WireframeBox(1, 10, "Armor", -1, 0, -1));
        model.Blocks.Add(WireframeBox(2, 20, "Camera", 1, 2, 3, 0.5, 1.0, 1.5));
        var selection = new AgentWireframeSelection
        {
            GridEntityId = 2,
            SubtypeId = "Camera",
            Color = "#ff33aa"
        };

        var result = AgentWireframeRenderer.Render(model, new[] { selection });
        XDocument.Parse(result.Svg);
        Check(result.RenderedBlocks == 2 && result.SelectedBlocks == 1 && !result.Truncated,
            "wireframe result counts or coverage were wrong");
        Check(result.Svg.Contains("data-view=\"top\"") && result.Svg.Contains("data-view=\"side\"") &&
              result.Svg.Contains("data-view=\"front\""), "one or more orthographic profiles are missing");
        Check(result.Svg.Split(new[] { "fill=\"#FF33AA\"" }, StringSplitOptions.None).Length - 1 == 3,
            "selected block color was not applied once in each profile");
        Check(result.Svg.Contains("stroke-width=\"1.8\""), "selected geometry was not visually emphasized");
        var top = SelectedPath(result.Svg, "top", "#FF33AA");
        var side = SelectedPath(result.Svg, "side", "#FF33AA");
        var front = SelectedPath(result.Svg, "front", "#FF33AA");
        Check(top != side && side != front && top != front,
            "the three panels did not project the asymmetric block along distinct axis pairs");
        Check(result.Svg == AgentWireframeRenderer.Render(model, new[] { selection }).Svg,
            "identical snapshot query was not deterministic");

        model.SourceSnapshotComplete = false;
        var incomplete = AgentWireframeRenderer.Render(model, new[] { selection });
        Check(incomplete.Truncated && incomplete.Svg.Contains("TRUNCATED"),
            "an incomplete source snapshot was presented as a complete wireframe");
    }

    private static void WireframeValidatesAndEscapesInput()
    {
        var model = new AgentWireframeModel { AgentId = "<agent & body>" };
        model.Blocks.Add(WireframeBox(1, 10, "Wheel", 0, 0, 0));
        var result = AgentWireframeRenderer.Render(model, new AgentWireframeSelection[0]);
        Check(result.Svg.Contains("&lt;agent &amp; body&gt;") && !result.Svg.Contains("<agent & body>"),
            "agent metadata was not XML escaped");

        var rejectedColor = false;
        try
        {
            AgentWireframeRenderer.Render(model, new[]
            {
                new AgentWireframeSelection { SubtypeId = "Wheel", Color = "red\" onload=\"bad" }
            });
        }
        catch (ArgumentException) { rejectedColor = true; }
        Check(rejectedColor, "unsafe color value was accepted");

        var rejectedWildcard = false;
        try
        {
            AgentWireframeRenderer.Render(model, new[]
            {
                new AgentWireframeSelection { Color = "#00FF00" }
            });
        }
        catch (ArgumentException) { rejectedWildcard = true; }
        Check(rejectedWildcard, "selector without a block identity was accepted");

        var rejectedMode = false;
        try
        {
            AgentWireframeRenderer.Render(model, new AgentWireframeSelection[0],
                (AgentWireframeVisualizationMode)99);
        }
        catch (ArgumentException exception)
        {
            rejectedMode = exception.GetType() == typeof(ArgumentException);
        }
        Check(rejectedMode, "unsupported visualization mode did not use the game-compatible argument error");
    }

    private static void WireframeCapsGeometry()
    {
        var model = new AgentWireframeModel { AgentId = "cap-test" };
        for (var index = 0; index <= AgentWireframeRenderer.MaximumBlocks; index++)
            model.Blocks.Add(WireframeBox(1, index + 1, "Armor", index, 0, 0));
        var result = AgentWireframeRenderer.Render(model, new AgentWireframeSelection[0]);
        Check(result.Truncated, "over-limit geometry was not marked truncated");
        Check(result.RenderedBlocks == AgentWireframeRenderer.MaximumBlocks,
            "wireframe renderer did not enforce its block cap");
        Check(result.Svg.Length <= AgentWireframeRenderer.MaximumSvgCharacters,
            "wireframe exceeded its hard output character bound");
    }

    private static AgentWireframeBlock WireframeBox(long gridId, long blockId, string subtype, double x, double y, double z)
        => WireframeBox(gridId, blockId, subtype, x, y, z, 0.5, 0.5, 0.5);

    private static AgentWireframeBlock WireframeBox(
        long gridId, long blockId, string subtype, double x, double y, double z,
        double halfX, double halfY, double halfZ)
    {
        var block = new AgentWireframeBlock
        {
            GridEntityId = gridId,
            BlockEntityId = blockId,
            DefinitionType = "CubeBlock",
            SubtypeId = subtype
        };
        for (var xSign = -1; xSign <= 1; xSign += 2)
        for (var ySign = -1; ySign <= 1; ySign += 2)
        for (var zSign = -1; zSign <= 1; zSign += 2)
            block.Corners.Add(new AgentWireframePoint(x + xSign * halfX, y + ySign * halfY, z + zSign * halfZ));
        return block;
    }

    private static string SelectedPath(string svg, string view, string color)
    {
        var viewStart = svg.IndexOf("data-view=\"" + view + "\"", StringComparison.Ordinal);
        var viewEnd = svg.IndexOf("</g>", viewStart, StringComparison.Ordinal);
        var colorIndex = svg.IndexOf("fill=\"" + color + "\"", viewStart, StringComparison.Ordinal);
        if (viewStart < 0 || viewEnd < viewStart || colorIndex < viewStart || colorIndex > viewEnd) return "";
        var dataStart = svg.LastIndexOf("d=\"", colorIndex, StringComparison.Ordinal) + 3;
        var dataEnd = svg.IndexOf('"', dataStart);
        return dataStart < 3 || dataEnd <= dataStart ? "" : svg.Substring(dataStart, dataEnd - dataStart);
    }

    private static double[] DepthFanAngles()
    {
        var angles = new double[19];
        for (var index = 0; index < angles.Length; index++)
            angles[index] = (-90 + index * 10) * Math.PI / 180.0;
        return angles;
    }

    private static void Run(string name, Action test)
    {
        total++;
        test();
        passed++;
        Console.WriteLine("PASS " + name);
    }

    private static void LocalVehicleTestControlIsFailClosed()
    {
        var now = new DateTime(2026, 10, 21, 20, 0, 0, DateTimeKind.Utc);
        const string instance = "test-suite-world-v2-r3-voidwright-unified-20261013-c";
        const string run = "11111111111111111111111111111111";
        const string fixture = "22222222222222222222222222222222";
        const long grid = 106313676705820376;
        const long controller = 113605230354192484;
        const long camera = 505;
        var authorization = new LocalVehicleTestAuthorization(instance, run, fixture,
            "explicit-local-test-run:20261021", grid, controller, camera, now.AddMinutes(5));
        var wheelIds = new List<long> { 101, 102, 103, 104 };
        var wheelGridIds = new List<long> { grid, grid, grid, grid };
        var readiness = new LocalVehicleTestReadiness(instance, run, fixture, grid, controller,
            wheelIds, wheelGridIds, now, true, true, true, true, true, true,
            groundContactVerified: true, gravityVerified: true,
            fullAssemblyClearanceVerified: true);
        var brakeOnlyReadiness = new LocalVehicleTestReadiness(instance, run, fixture, grid, controller,
            wheelIds, wheelGridIds, now, true, false, true, true, true, true,
            linearSpeedMetersPerSecond: 0, angularSpeedRadiansPerSecond: 0,
            stationarySteeringOnly: true, groundContactVerified: true, gravityVerified: true,
            brakeOnly: true);
        LocalVehicleTestCommand Command(LocalVehicleTestCommandKind kind, double value, double speed,
            int expiry = 2, double steering = 0)
            => new LocalVehicleTestCommand("cmd-" + kind, instance, run, fixture, grid, controller,
                kind, value, speed, now, now.AddSeconds(expiry), steering);
        string reason;
        foreach (var command in new[]
                 {
                     Command(LocalVehicleTestCommandKind.Steering, 0.15, 0),
                     Command(LocalVehicleTestCommandKind.Propulsion, 0.02, 0.15),
                     Command(LocalVehicleTestCommandKind.Drive, 0.02, 0.15, 2, -0.12),
                     Command(LocalVehicleTestCommandKind.Brake, 1, 0),
                     Command(LocalVehicleTestCommandKind.Neutral, 0, 0)
                 })
            Check(LocalVehicleTestControlPolicy.CanDispatch(authorization, readiness, command,
                    instance, run, fixture, grid, controller, camera, now.AddMilliseconds(100), out reason),
                "valid local command was rejected: " + reason);

        Check(LocalVehicleTestControlPolicy.CanDispatch(authorization, brakeOnlyReadiness,
                  Command(LocalVehicleTestCommandKind.Brake, 1, 0), instance, run, fixture,
                  grid, controller, camera, now.AddMilliseconds(100), out reason),
            "stationary brake-only mode rejected the explicit active-brake command: " + reason);
        foreach (var command in new[]
                 {
                     Command(LocalVehicleTestCommandKind.Steering, 0.01, 0),
                     Command(LocalVehicleTestCommandKind.Propulsion, 0.02, 0.15),
                     Command(LocalVehicleTestCommandKind.Drive, 0.02, 0.15, 2, 0),
                     Command(LocalVehicleTestCommandKind.Neutral, 0, 0)
                 })
            Check(!LocalVehicleTestControlPolicy.CanDispatch(authorization, brakeOnlyReadiness, command,
                    instance, run, fixture, grid, controller, camera, now.AddMilliseconds(100), out reason),
                "stationary brake-only mode admitted a non-brake command: " + command.Kind);

        Check(!LocalVehicleTestControlPolicy.RequiresSteeringDirectionConfirmation(
                  Command(LocalVehicleTestCommandKind.Propulsion, 0.02, 0.15)) &&
              !LocalVehicleTestControlPolicy.RequiresSteeringDirectionConfirmation(
                  Command(LocalVehicleTestCommandKind.Drive, 0.02, 0.15, 2, 0)) &&
              LocalVehicleTestControlPolicy.RequiresSteeringDirectionConfirmation(
                  Command(LocalVehicleTestCommandKind.Drive, 0.02, 0.15, 2, 0.01)) &&
              !LocalVehicleTestControlPolicy.RequiresSteeringDirectionConfirmation(
                  Command(LocalVehicleTestCommandKind.Steering, 0.1, 0)),
            "steering direction confirmation was not limited to combined movement that requests a turn");

        var wrongGrid = Command(LocalVehicleTestCommandKind.Steering, 0.1, 0);
        var wrongGridAuthorization = new LocalVehicleTestAuthorization(instance, run, fixture,
            "explicit-local-test-run:20261021", grid + 1, controller, camera, now.AddMinutes(5));
        Check(!LocalVehicleTestControlPolicy.CanDispatch(wrongGridAuthorization, readiness, wrongGrid,
                instance, run, fixture, grid, controller, camera, now, out reason),
            "local test grant accepted a different grid");
        Check(!LocalVehicleTestControlPolicy.CanDispatch(authorization, readiness,
                Command(LocalVehicleTestCommandKind.Steering, 0.1, 0),
                instance, "33333333333333333333333333333333", fixture, grid, controller, camera, now, out reason),
            "local test policy accepted a different run");
        Check(!LocalVehicleTestControlPolicy.CanDispatch(authorization, readiness,
                Command(LocalVehicleTestCommandKind.Steering, 0.1, 0),
                instance, run, "44444444444444444444444444444444", grid, controller, camera, now, out reason),
            "local test policy accepted a different fixture");
        Check(!LocalVehicleTestControlPolicy.CanDispatch(authorization, readiness,
                Command(LocalVehicleTestCommandKind.Steering, 0.1, 0),
                instance, run, fixture, grid, controller + 1, camera, now, out reason),
            "local test policy accepted a different controller");
        var wrongCameraAuthorization = new LocalVehicleTestAuthorization(instance, run, fixture,
            "explicit-local-test-run:20261021", grid, controller, camera + 1, now.AddMinutes(5));
        Check(!LocalVehicleTestControlPolicy.CanDispatch(wrongCameraAuthorization, readiness, wrongGrid,
                instance, run, fixture, grid, controller, camera, now, out reason),
            "local test policy accepted an authorization for a different camera");
        var expiredAuthorization = new LocalVehicleTestAuthorization(instance, run, fixture,
            "explicit-local-test-run:20261021", grid, controller, camera, now);
        Check(!LocalVehicleTestControlPolicy.CanDispatch(expiredAuthorization, readiness,
                Command(LocalVehicleTestCommandKind.Brake, 1, 0),
                instance, run, fixture, grid, controller, camera, now, out reason),
            "local test policy accepted an expired run grant");
        Check(!LocalVehicleTestControlPolicy.CanDispatch(authorization, readiness,
                Command(LocalVehicleTestCommandKind.Neutral, 0, 0, 0),
                instance, run, fixture, grid, controller, camera, now, out reason),
            "local test policy accepted a command whose timeout had already elapsed");
        Check(reason == "local test command expired before dispatch or during execution",
            "expired local test command did not report its distinct terminal reason");
        Check(!LocalVehicleTestControlPolicy.CanDispatch(authorization, readiness,
                Command(LocalVehicleTestCommandKind.Propulsion, 0.04, 0.5, 20),
                instance, run, fixture, grid, controller, camera, now, out reason),
            "local test policy accepted an excessive propulsion value, speed, or expiry");
        Check(!LocalVehicleTestControlPolicy.CanDispatch(authorization,
                new LocalVehicleTestReadiness(instance, run, fixture, grid, controller, wheelIds,
                    new List<long> { grid, grid, grid, grid + 1 }, now, true, true, true, true, true, true),
                Command(LocalVehicleTestCommandKind.Steering, 0.1, 0),
                instance, run, fixture, grid, controller, camera, now, out reason),
            "local test policy accepted a wheel outside the exact enrolled grid");
        Check(!LocalVehicleTestControlPolicy.CanDispatch(authorization,
                new LocalVehicleTestReadiness(instance, run, fixture, grid, controller, wheelIds,
                    wheelGridIds, now.AddSeconds(-2), true, true, true, true, true, true),
                Command(LocalVehicleTestCommandKind.Steering, 0.1, 0),
                instance, run, fixture, grid, controller, camera, now, out reason),
            "local test policy accepted stale motion telemetry");
        Check(!LocalVehicleTestControlPolicy.CanDispatch(authorization,
                new LocalVehicleTestReadiness(instance, run, fixture, grid, controller, wheelIds,
                    wheelGridIds, now, true, true, false, true, true, true),
                Command(LocalVehicleTestCommandKind.Propulsion, 0.02, 0.15),
                instance, run, fixture, grid, controller, camera, now, out reason),
            "local test policy accepted propulsion without the independent stop path");
        Check(!LocalVehicleTestControlPolicy.CanDispatch(authorization,
                new LocalVehicleTestReadiness(instance, run, fixture, grid, controller, wheelIds,
                    wheelGridIds, now, true, true, true, true, true, true, 0, 0, true, true, true),
                Command(LocalVehicleTestCommandKind.Drive, 0.02, 0.15, 2, 0.1),
                instance, run, fixture, grid, controller, camera, now, out reason),
            "combined local drive was allowed in stationary-steering-only mode");
        Check(!LocalVehicleTestControlPolicy.CanDispatch(authorization,
                new LocalVehicleTestReadiness(instance, run, fixture, grid, controller, wheelIds,
                    wheelGridIds, now, true, true, true, true, true, true,
                    groundContactVerified: true, gravityVerified: true),
                Command(LocalVehicleTestCommandKind.Propulsion, 0.02, 0.15),
                instance, run, fixture, grid, controller, camera, now, out reason) &&
              reason.Contains("clearance", StringComparison.Ordinal),
            "propulsion was allowed based only on a center-camera ray without full-assembly clearance");
        Check(!LocalVehicleTestControlPolicy.CanDispatch(authorization,
                new LocalVehicleTestReadiness(instance, run, fixture, grid, controller, wheelIds,
                    wheelGridIds, now, true, true, true, true, true, true),
                Command(LocalVehicleTestCommandKind.Propulsion, 0.01, 0.1),
                instance, run, fixture, grid, controller, camera, now, out reason) &&
              reason.Contains("ground", StringComparison.Ordinal),
            "local test dispatched without fresh ground-contact and gravity evidence");

        LocalVehicleTestAuthorization decodedAuthorization;
        Check(LocalVehicleTestWire.TryParseAuthorization(LocalVehicleTestWire.FormatAuthorization(authorization),
                out decodedAuthorization) && decodedAuthorization.GridId == grid &&
              decodedAuthorization.CameraId == camera &&
              decodedAuthorization.AuthorizationId == authorization.AuthorizationId,
            "local test authorization mailbox did not preserve exact identity");
        Check(LocalVehicleTestWire.FormatAuthorization(authorization).StartsWith("VWLA2|", StringComparison.Ordinal) &&
              !LocalVehicleTestWire.TryParseAuthorization("VWLA1|" + instance + "|" + run + "|" + fixture +
                  "|old-grant|" + grid + "|" + controller + "|" + now.AddMinutes(5).Ticks + "\n", out decodedAuthorization),
            "camera-unbound legacy authorizations were not rejected");
        LocalVehicleTestCommand decodedCommand;
        var propulsion = Command(LocalVehicleTestCommandKind.Propulsion, 0.02, 0.15);
        Check(LocalVehicleTestWire.TryParseCommand(LocalVehicleTestWire.FormatCommand(propulsion), out decodedCommand) &&
              decodedCommand.GridId == grid && decodedCommand.Value == propulsion.Value &&
              decodedCommand.Kind == LocalVehicleTestCommandKind.Propulsion,
            "local test command mailbox did not preserve exact command values");
        var drive = Command(LocalVehicleTestCommandKind.Drive, 0.02, 0.15, 2, -0.12);
        Check(LocalVehicleTestWire.FormatCommand(drive).StartsWith("VWLC2|", StringComparison.Ordinal) &&
              LocalVehicleTestWire.TryParseCommand(LocalVehicleTestWire.FormatCommand(drive), out decodedCommand) &&
              decodedCommand.Kind == LocalVehicleTestCommandKind.Drive && decodedCommand.Value == 0.02 &&
              decodedCommand.SteeringValue == -0.12 && decodedCommand.MaximumSpeedMetersPerSecond == 0.15,
            "combined local steering/propulsion values did not round-trip through the v2 mailbox");
        Check(!LocalVehicleTestWire.TryParseCommand("VWLC1|bad|" + instance + "|" + run + "|" + fixture +
                "|" + grid + "|" + controller + "|propulsion|NaN|0.1|1|2\n", out decodedCommand),
            "local test command mailbox accepted non-finite actuator values");
        var expectedStop = new LocalVehicleTestReceipt("stop-request", "stopped", "readback_verified",
            now, 0, 0, 3, true, true, true);
        Check(LocalVehicleTestWire.TryParseReceipt(LocalVehicleTestWire.FormatReceipt(expectedStop), out var decodedReceipt) &&
              decodedReceipt.CommandId == expectedStop.CommandId && decodedReceipt.ActuatorsNeutralized &&
              decodedReceipt.BrakesRequested && decodedReceipt.PhysicalStopObserved &&
              decodedReceipt.ConsecutiveStationarySamples == 3,
            "local test stop receipt did not preserve acknowledged neutralization and stationary samples");
        Check(!LocalVehicleTestWire.TryParseReceipt("VWLR1|stop-request|stopped|reason|0|NaN|0|3|1|1|1\n", out decodedReceipt),
            "local test receipt accepted non-finite motion telemetry");
    }

    private static void LocalVehicleTestBaselineIsFailClosed()
    {
        var wheels = new List<long>(LocalVehicleTestBindingPolicy.WheelIds);
        var grids = new List<long>(LocalVehicleTestBindingPolicy.WheelGridIds);
        var exact = LocalVehicleTestBindingPolicy.IsExactRun(LocalVehicleTestBindingPolicy.FreshInstance,
            "0123456789abcdef0123456789abcdef", "0123456789abcdef0123456789abcdef",
            LocalVehicleTestBindingPolicy.GridId, LocalVehicleTestBindingPolicy.ControllerId,
            LocalVehicleTestBindingPolicy.FreshInstanceCameraId);
        Check(exact && LocalVehicleTestBindingPolicy.HasExactWheelMap(wheels, grids),
            "fresh candidate baseline rejected exact pinned run and suspension-to-subgrid pairs");
        Check(LocalVehicleTestBindingPolicy.HasExactModAllowlist(
                  new List<ulong>(LocalVehicleTestBindingPolicy.FixtureModIds)) &&
              !LocalVehicleTestBindingPolicy.HasExactModAllowlist(new List<ulong> { 999999999UL }),
            "fixture writer audit accepted an unexpected world mod");
        Check(!LocalVehicleTestBindingPolicy.IsExactRun("land-local-test-codex-truck-20261022-e",
                "0123456789abcdef0123456789abcdef", "0123456789abcdef0123456789abcdef",
                LocalVehicleTestBindingPolicy.GridId, LocalVehicleTestBindingPolicy.ControllerId,
                LocalVehicleTestBindingPolicy.FreshInstanceCameraId),
            "baseline accepted superseded E");
        Check(!LocalVehicleTestBindingPolicy.IsExactRun(LocalVehicleTestBindingPolicy.FreshInstance,
                "0123456789abcdef0123456789abcdef", "0123456789abcdef0123456789abcdef",
                LocalVehicleTestBindingPolicy.GridId, LocalVehicleTestBindingPolicy.ControllerId,
                LocalVehicleTestBindingPolicy.FreshInstanceCameraId + 1),
            "baseline accepted a camera that was not bound to the fresh exact fixture");
        Check(LocalVehicleTestBindingPolicy.CameraIdFor("some-other-instance") == 0L &&
              !LocalVehicleTestBindingPolicy.IsExactRun("some-other-instance",
                  "0123456789abcdef0123456789abcdef", "0123456789abcdef0123456789abcdef",
                  LocalVehicleTestBindingPolicy.GridId, LocalVehicleTestBindingPolicy.ControllerId,
                  LocalVehicleTestBindingPolicy.FreshInstanceCameraId),
            "another instance inherited the Hydro Scout camera binding");
        var wrongGrids = new List<long>(grids);
        wrongGrids[0]++;
        Check(!LocalVehicleTestBindingPolicy.HasExactWheelMap(wheels, wrongGrids),
            "baseline accepted a wrong suspension top-grid pair");
        Check(LocalVehicleTestBindingPolicy.CanAcceptBaseline(true, true, true, true, true,
                true, true, true, true, 20, 2.0),
            "baseline rejected its complete minimum live evidence");
        Check(!LocalVehicleTestBindingPolicy.CanAcceptBaseline(true, true, true, false, false,
                true, true, true, true, 20, 2.0),
            "baseline accepted a missing camera");
        Check(!LocalVehicleTestBindingPolicy.CanAcceptBaseline(true, true, true, true, true,
                false, true, true, true, 20, 2.0),
            "baseline accepted a blocked camera path");
        Check(!LocalVehicleTestBindingPolicy.CanAcceptBaseline(true, true, true, true, true,
                true, true, true, true, 19, 2.0) &&
              !LocalVehicleTestBindingPolicy.CanAcceptBaseline(true, true, true, true, true,
                true, true, true, true, 20, 1.99),
            "baseline accepted too few samples or too little stable time");
        Check(!LocalVehicleTestBindingPolicy.CanAcceptBaseline(true, true, false, true, true,
                true, true, true, true, 20, 2.0) &&
              !LocalVehicleTestBindingPolicy.CanAcceptBaseline(true, false, true, true, true,
                true, true, true, true, 20, 2.0),
            "baseline accepted missing chassis or wheel-grid physics");
        Check(!LocalVehicleTestBindingPolicy.CanAcceptBaseline(true, true, true, true, true,
                true, true, false, true, 20, 2.0) &&
              !LocalVehicleTestBindingPolicy.CanAcceptBaseline(true, true, true, true, true,
                true, true, true, false, 20, 2.0),
            "baseline accepted missing ground support or measurable gravity");
        Check(LocalVehicleTestBindingPolicy.ShouldSample(6) &&
              !LocalVehicleTestBindingPolicy.ShouldSample(7) &&
              !LocalVehicleTestBindingPolicy.ShouldSample(1806),
            "baseline sampling cadence or bounded duration changed");
        Check(LocalVehicleTestBindingPolicy.IsForwardOnly(LocalVehicleTestCommandKind.Drive, 0.03) &&
              !LocalVehicleTestBindingPolicy.IsForwardOnly(LocalVehicleTestCommandKind.Drive, -0.01) &&
              LocalVehicleTestBindingPolicy.IsForwardOnly(LocalVehicleTestCommandKind.Steering, 0),
            "front-camera clearance authorized reverse propulsion or rejected steering calibration");
        Check(LocalVehicleTestBindingPolicy.AreSubgridsStationary(new[] { 0.01, 0.02 },
                  new[] { 0.01, 0.02 }, new[] { 0.01, 0.05 }) &&
              !LocalVehicleTestBindingPolicy.AreSubgridsStationary(new[] { 0.01, 0.021 },
                  new[] { 0.01, 0.01 }, new[] { 0.01, 0.01 }),
            "mechanical subgrid speed or drift thresholds were not enforced fail-closed");
        Check(!LocalVehicleTestBindingPolicy.AreSubgridsStationary(new[] { 0.01 },
                  new[] { 0.01, 0.01 }, new[] { 0.01 }) &&
              !LocalVehicleTestBindingPolicy.AreSubgridsStationary(new[] { 0.01, 0.01 },
                  new[] { 0.01, 0.01 }, new[] { 0.01, 0.051 }),
            "subgrid evidence accepted incomplete rotor coverage or excessive drift");
        Check(LocalVehicleTestBindingPolicy.IsDistinctStopSample(100, 101) &&
              !LocalVehicleTestBindingPolicy.IsDistinctStopSample(101, 101) &&
              LocalVehicleTestBindingPolicy.CanConfirmStop(true, true, true, true, 3) &&
              !LocalVehicleTestBindingPolicy.CanConfirmStop(true, true, true, false, 3) &&
              !LocalVehicleTestBindingPolicy.CanConfirmStop(true, false, true, true, 3),
            "stop confirmation accepted duplicate samples, moving subgrids, or missing readbacks");
        var rotorWindow = new Dictionary<long, double[]>();
        Check(LocalVehicleTestBindingPolicy.ObserveRotorPosition(rotorWindow, 99,
                  new[] { 0.0, 0.0, 0.0 }, 0.05, true, out var rotorDrift) && rotorDrift == 0 &&
              !LocalVehicleTestBindingPolicy.ObserveRotorPosition(rotorWindow, 99,
                  new[] { 0.06, 0.0, 0.0 }, 0.05, true, out rotorDrift) &&
              rotorWindow.Count == 0 &&
              LocalVehicleTestBindingPolicy.ObserveRotorPosition(rotorWindow, 99,
                  new[] { 0.06, 0.0, 0.0 }, 0.05, true, out rotorDrift) && rotorDrift == 0 &&
              LocalVehicleTestBindingPolicy.ObserveRotorPosition(rotorWindow, 99,
                  new[] { 0.08, 0.0, 0.0 }, 0.05, true, out rotorDrift) &&
              Math.Abs(rotorDrift - 0.02) < 1e-9,
            "baseline rotor-position window did not re-anchor after early settling drift");
    }

    private static void LocalVehicleTestWireUsesCSharp6Syntax()
    {
        var source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../../Data/Scripts/Voidwright/Core/LocalVehicleTestControl.cs"));
        var text = File.ReadAllText(source);
        Check(!text.Contains("out var ", StringComparison.Ordinal) &&
              !text.Contains(" is LocalVehicleTest", StringComparison.Ordinal),
            "the game mod script contains syntax newer than the C# 6 compiler supports");
    }

    private static void LocalVehicleTestDirectionProofCarryIsFailClosed()
    {
        Check(LocalVehicleTestDirectionProofPolicy.CanCarryForward(true, true, true, true, true, true, 0.02, 0.02),
            "exact prior proof and safe live state did not carry forward");
        Check(!LocalVehicleTestDirectionProofPolicy.CanCarryForward(false, true, true, true, true, true, 0, 0),
            "missing prior receipt was accepted");
        Check(!LocalVehicleTestDirectionProofPolicy.CanCarryForward(true, false, true, true, true, true, 0, 0),
            "changed live wheel map was accepted");
        Check(!LocalVehicleTestDirectionProofPolicy.CanCarryForward(true, true, false, true, true, true, 0, 0),
            "unknown steering orientation was accepted");
        Check(!LocalVehicleTestDirectionProofPolicy.CanCarryForward(true, true, true, false, true, true, 0, 0),
            "missing verified stop path was accepted");
        Check(!LocalVehicleTestDirectionProofPolicy.CanCarryForward(true, true, true, true, false, true, 0, 0),
            "another movement writer was accepted");
        Check(!LocalVehicleTestDirectionProofPolicy.CanCarryForward(true, true, true, true, true, false, 0, 0),
            "non-neutral actuators were accepted");
        Check(!LocalVehicleTestDirectionProofPolicy.CanCarryForward(true, true, true, true, true, true, 0.020001, 0),
            "linear speed above the existing stationary bound was accepted");
        Check(!LocalVehicleTestDirectionProofPolicy.CanCarryForward(true, true, true, true, true, true, 0, 0.020001),
            "angular speed above the existing stationary bound was accepted");
        Check(!LocalVehicleTestDirectionProofPolicy.CanCarryForward(true, true, true, true, true, true, double.NaN, 0),
            "non-finite speed was accepted");
    }

    private static void LocalVehicleTestDisplacementIsFailClosed()
    {
        Check(LocalVehicleTestDisplacementPolicy.CanInitialize(false, true, true, true, 0, 0),
            "neutral stop-ready run did not initialize displacement baseline");
        Check(!LocalVehicleTestDisplacementPolicy.CanInitialize(true, true, true, true, 0, 0),
            "run displacement baseline was reinitialized after it was already set");
        Check(!LocalVehicleTestDisplacementPolicy.CanInitialize(false, false, true, true, 0, 0),
            "moving actuators initialized a displacement baseline");
        Check(!LocalVehicleTestDisplacementPolicy.CanInitialize(false, true, false, true, 0, 0),
            "missing stop path initialized a displacement baseline");
        Check(!LocalVehicleTestDisplacementPolicy.CanInitialize(false, true, true, false, 0, 0),
            "un-audited movement writer initialized a displacement baseline");
        Check(!LocalVehicleTestDisplacementPolicy.CanInitialize(false, true, true, true, double.NaN, 0),
            "non-finite speed initialized a displacement baseline");

        Check(LocalVehicleTestDisplacementPolicy.TryMeasure(1, 2, 3, 4, 6, 3, out var distance) &&
              Math.Abs(distance - 5) < 1e-12,
            "run displacement was not measured from the retained baseline");
        Check(!LocalVehicleTestDisplacementPolicy.TryMeasure(1, 2, 3, double.PositiveInfinity, 6, 3, out _),
            "non-finite position produced a valid displacement");
        Check(!LocalVehicleTestDisplacementPolicy.Exceeds(24.999, 25),
            "a displacement below its configured limit was rejected");
        Check(LocalVehicleTestDisplacementPolicy.Exceeds(25, 25),
            "a displacement exactly at its configured limit was accepted");
        Check(LocalVehicleTestDisplacementPolicy.Exceeds(25.001, 25),
            "a displacement above its configured limit was accepted");
        Check(LocalVehicleTestDisplacementPolicy.Exceeds(double.NaN, 25),
            "a non-finite displacement was accepted");
        Check(LocalVehicleTestDisplacementPolicy.Exceeds(0, double.NaN),
            "a non-finite displacement limit was accepted");
    }

    private static void BlockLifecycleDiagnosticBindingIsFailClosed()
    {
        const string run = "11111111111111111111111111111111";
        const string session = "22222222222222222222222222222222";
        const string labRun = "33333333333333333333333333333333";
        const string fixture = "44444444444444444444444444444444";
        const string nonce = "55555555555555555555555555555555";
        var target = new BlockLifecycleDiagnosticTarget
        {
            GridEntityId = 105034224363529183,
            GridName = "Blue Truck",
            ControllerEntityId = 83468039688685570,
            ControllerType = "MyObjectBuilder_Cockpit",
            ControllerSubtype = "SmallBlockCapCockpit",
            CoreEntityId = 138593789302115959,
            CoreSubtype = "VoidwrightNavigationControllerSmall",
            CameraEntityId = 81617799316996022,
            CameraSubtype = BlockLifecycleDiagnosticTarget.ExpectedFixtureCameraSubtype,
            SourceTreeSha256 = new string('a', 64),
            InstrumentationLedgerSha256 = new string('b', 64)
        };
        var snapshot = new BlockLifecycleDiagnosticSnapshot
        {
            Phase = "after-entity-initialization",
            RunId = run,
            SessionId = session,
            LabRunId = labRun,
            FixtureId = fixture,
            BootstrapNonce = nonce,
            SourceTreeSha256 = target.SourceTreeSha256,
            InstrumentationLedgerSha256 = target.InstrumentationLedgerSha256,
            CapturedUtc = DateTime.SpecifyKind(new DateTime(2026, 10, 20, 18, 0, 0), DateTimeKind.Utc),
            TargetGridEntityId = target.GridEntityId,
            TargetGridName = target.GridName,
            TargetGridFound = true,
            BlockCount = 4
        };
        snapshot.Blocks.Add(new BlockLifecycleObservedBlock
        {
            EntityId = target.ControllerEntityId,
            HasEntityId = true,
            DefinitionType = target.ControllerType,
            SubtypeId = target.ControllerSubtype
        });
        snapshot.Blocks.Add(new BlockLifecycleObservedBlock
        {
            EntityId = target.CoreEntityId,
            HasEntityId = true,
            DefinitionType = "MyObjectBuilder_UpgradeModule",
            SubtypeId = target.CoreSubtype
        });
        snapshot.Blocks.Add(new BlockLifecycleObservedBlock
        {
            EntityId = target.CameraEntityId,
            HasEntityId = true,
            DefinitionType = "MyObjectBuilder_CameraBlock",
            SubtypeId = target.CameraSubtype
        });
        snapshot.Blocks.Add(new BlockLifecycleObservedBlock
        {
            EntityId = 0,
            HasEntityId = false,
            DefinitionType = "MyObjectBuilder_CubeBlock",
            SubtypeId = "LargeBlockArmorBlock"
        });

        Check(BlockLifecycleDiagnosticBindingPolicy.IsBound(snapshot, target, run, session, labRun, fixture, nonce),
            "valid load snapshot failed schema and authority binding");
        Check(BlockLifecycleDiagnosticBindingPolicy.ContainsExpectedBlocks(snapshot, target),
            "valid snapshot did not contain the exact Core, controller, and camera identities");
        Check(target.CameraSubtype == "SmallCameraBlock",
            "lifecycle fixture camera binding is not pinned to the base-game camera subtype");
        snapshot.Blocks[2].SubtypeId = "SmallCameraTopMounted";
        Check(!BlockLifecycleDiagnosticBindingPolicy.ContainsExpectedBlocks(snapshot, target),
            "legacy DLC-tagged camera subtype was accepted for the lifecycle fixture");
        snapshot.Blocks[2].SubtypeId = target.CameraSubtype;
        using (var document = JsonDocument.Parse(snapshot.ToJson()))
        {
            var root = document.RootElement;
            Check(root.GetProperty("schema").GetString() == BlockLifecycleDiagnosticSnapshot.CurrentSchema &&
                  root.GetProperty("schemaVersion").GetInt32() == 1,
                "diagnostic JSON schema header was not serialized");
            Check(root.GetProperty("targetGridEntityId").GetInt64() == target.GridEntityId &&
                  root.GetProperty("fixtureId").GetString() == fixture &&
                  root.GetProperty("blocks").GetArrayLength() == snapshot.BlockCount,
                "diagnostic JSON lost target binding or the complete block list");
            Check(root.GetProperty("blocks")[3].GetProperty("entityId").ValueKind == JsonValueKind.Null,
                "blocks without a runtime entity id were not represented as null");
        }

        var presave = new BlockLifecycleDiagnosticSnapshot
        {
            Schema = snapshot.Schema,
            SchemaVersion = snapshot.SchemaVersion,
            Phase = "before-first-world-save",
            RunId = run,
            SessionId = session,
            LabRunId = labRun,
            FixtureId = fixture,
            BootstrapNonce = nonce,
            SourceTreeSha256 = target.SourceTreeSha256,
            InstrumentationLedgerSha256 = target.InstrumentationLedgerSha256,
            TargetGridEntityId = target.GridEntityId,
            TargetGridName = target.GridName,
            TargetGridFound = true,
            BlockCount = snapshot.BlockCount
        };
        presave.Blocks.AddRange(snapshot.Blocks);
        Check(BlockLifecycleDiagnosticBindingPolicy.IsBound(presave, target, run, session, labRun, fixture, nonce),
            "pre-save snapshot phase was rejected");

        var wrongFixture = new BlockLifecycleDiagnosticSnapshot
        {
            Schema = snapshot.Schema,
            SchemaVersion = snapshot.SchemaVersion,
            Phase = snapshot.Phase,
            RunId = run,
            SessionId = session,
            LabRunId = labRun,
            FixtureId = new string('6', 32),
            BootstrapNonce = nonce,
            SourceTreeSha256 = target.SourceTreeSha256,
            InstrumentationLedgerSha256 = target.InstrumentationLedgerSha256,
            TargetGridEntityId = target.GridEntityId,
            TargetGridName = target.GridName,
            TargetGridFound = true,
            BlockCount = snapshot.BlockCount
        };
        wrongFixture.Blocks.AddRange(snapshot.Blocks);
        Check(!BlockLifecycleDiagnosticBindingPolicy.IsBound(wrongFixture, target, run, session, labRun, fixture, nonce),
            "wrong fixture identity was accepted");

        var wrongSchema = new BlockLifecycleDiagnosticSnapshot
        {
            Schema = "unknown",
            Phase = snapshot.Phase,
            RunId = run,
            SessionId = session,
            LabRunId = labRun,
            FixtureId = fixture,
            BootstrapNonce = nonce,
            SourceTreeSha256 = target.SourceTreeSha256,
            InstrumentationLedgerSha256 = target.InstrumentationLedgerSha256,
            TargetGridEntityId = target.GridEntityId,
            TargetGridName = target.GridName,
            TargetGridFound = true,
            BlockCount = snapshot.BlockCount
        };
        wrongSchema.Blocks.AddRange(snapshot.Blocks);
        Check(!BlockLifecycleDiagnosticBindingPolicy.IsBound(wrongSchema, target, run, session, labRun, fixture, nonce),
            "unknown diagnostic schema was accepted");

        snapshot.Blocks.RemoveAt(2);
        snapshot.BlockCount--;
        Check(BlockLifecycleDiagnosticBindingPolicy.IsBound(snapshot, target, run, session, labRun, fixture, nonce),
            "load snapshot with missing instrumented block was discarded instead of reported");
        Check(!BlockLifecycleDiagnosticBindingPolicy.ContainsExpectedBlocks(snapshot, target),
            "missing camera was reported present");
        snapshot.Blocks.Add(snapshot.Blocks[0]);
        snapshot.BlockCount++;
        Check(!BlockLifecycleDiagnosticBindingPolicy.IsBound(snapshot, target, run, session, labRun, fixture, nonce),
            "duplicate runtime block entity id was accepted");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class HeadingTrapModel : ITraversalModel
    {
        private static readonly GridPoint Start = new GridPoint(0, 0, 0);
        private static readonly GridPoint Junction = new GridPoint(1, 0, 0);
        private static readonly GridPoint Turn = new GridPoint(0, 0, 1);
        private static readonly GridPoint Goal = new GridPoint(2, 0, 0);

        public bool CanOccupy(GridPoint point)
            => point == Start || point == Junction || point == Turn || point == Goal;

        public System.Collections.Generic.IEnumerable<TraversalEdge> GetNeighbors(
            GridPoint point, GridPoint? previous)
        {
            if (point == Start)
            {
                yield return new TraversalEdge(Junction, 1);
                yield return new TraversalEdge(Turn, 1);
            }
            else if (point == Turn)
            {
                yield return new TraversalEdge(Junction, 1);
            }
            else if (point == Junction)
            {
                yield return new TraversalEdge(Goal, previous.HasValue && previous.Value == Turn ? 1 : 100);
            }
        }

        public double EstimateRemainingCost(GridPoint point, GridPoint goal) => 0;
    }
}
