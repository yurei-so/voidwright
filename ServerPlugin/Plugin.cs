using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Security.Cryptography;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.Components;
using VRage.Plugins;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using Voidwright.Core;

namespace Voidwright.ServerPlugin
{
    /// <summary>
    /// Privileged actuator host for disposable VRageCage fixtures. It accepts
    /// only a bounded one-shot command from an activated destructive-lab run and
    /// neutralizes/restores temporary wheel settings on every terminal path.
    /// </summary>
    public sealed class Plugin : IPlugin
    {
        private const double DirectManeuverHardExcursionLimitMeters = 7.5;
        // At the direct-maneuver cap of 0.25 m/s, this bounds travel during
        // host/mod handshake skew to 0.3125 m before the next gated update.
        private static readonly TimeSpan DirectModHeartbeatMaximumAge = TimeSpan.FromSeconds(1.25);
        private static readonly TimeSpan DirectCapabilityArtifactRefreshInterval = TimeSpan.FromSeconds(1);
        private string lastDirectHandshakeDiagnostic;
        private bool directCapabilityStorageReady;
        private string lastDirectCapabilityArtifactToken;
        private DateTime lastDirectCapabilityArtifactIssuedUtc = DateTime.MinValue;
        private bool enabled;
        private long controllerId;
        private long expectedGridId;
        private long expectedAgentCoreId;
        private string expectedDiscoveryRunId, expectedDiscoveryFingerprint;
        private long expectedDiscoveryTick;
        private string operationId;
        private double distanceMeters;
        private double maxSpeed;
        private double navigationOffsetLeft;
        private long navigationDepthCameraId;
        private IMyCameraBlock navigationDepthCamera;
        private readonly double[] navigationDepthClearances = new double[19];
        private bool navigationDepthScanReady;
        private int navigationDepthScanTick;
        private int navigationDepthSelfHits;
        private int navigationDepthAwayTicks;
        private int navigationDepthStableTicks;
        private int navigationDepthNoMotionTicks;
        private int navigationDepthDirectClearFrames;
        private bool navigationDepthStartupSettled;
        private double navigationDepthChosenHeading;
        private double navigationDepthMinimumClearance;
        private double navigationDepthDirectClearance;
        private string navigationDepthDecision = "not-started";
        private int navigationMotionSampleTick = -1;
        private int navigationMotionTelemetryIntervalTicks = 10;
        private long navigationMotionSampleSequence;
        private double navigationMotionSampleTimeSeconds;
        private Vector3D navigationMotionSampleGridOrigin;
        private Vector3D navigationMotionSampleCenterOfMass;
        private Vector3D navigationMotionSampleWheelReference;
        private bool navigationMotionSampleReady;
        private Vector3D navigationMotionPreviousLinearVelocity;
        private double navigationMotionPreviousVelocityTimeSeconds;
        private bool navigationMotionVelocityIntegratorReady;
        private Vector3D navigationMotionIntegratedLinearVelocity;
        private double navigationMotionIntegratedSeconds;
        private Vector3D navigationDepthVelocityAnchorPosition;
        private double navigationDepthVelocityAnchorTimeSeconds;
        private int navigationDepthVelocityAnchorTick = -1;
        private bool navigationDepthVelocityAnchorReady;
        private Vector3D navigationDepthPositionVelocity;
        private bool navigationDepthPositionVelocityReady;
        private Vector3D navigationPathLastCenterOfMass;
        private bool navigationPathReady;
        private double navigationPathLengthMeters;
        private double navigationClosestApproachMeters = double.MaxValue;
        private Vector3D navigationPathStartCenterOfMass;
        private int timeoutTicks;
        private int elapsedTicks;
        private int navigationStartDelayTicks;
        private int navigationStartDelayRemaining;
        private bool driveReverse;
        private MyShipController controller;
        private Vector3D startPosition;
        private bool started;
        private bool terminal;
        private bool preflightSuccessLogged;
        private bool modActivationWritten;
        private string modActivationText;
        private string modSessionId;
        private VoidwrightPreflightHeartbeat modHeartbeat;
        private bool modHeartbeatFresh;
        private VoidwrightSessionLiveness modSessionLiveness;
        private bool modSessionLivenessFresh;
        private string modSessionLivenessFailure = "not-read";
        private long directManeuverHostSampleSequence;
        private string modHandshakeReason = "waiting for Voidwright mod heartbeat";
        private bool discoveryBootstrapReady;
        private DateTime lastDiscoveryBootstrapRefreshUtc = DateTime.MinValue;
        private bool modActivationFailureLogged;
        private DateTime lastStageTwoActivationAttemptUtc = DateTime.MinValue;
        private DateTime lastModActivationAttemptUtc = DateTime.MinValue;
        private DateTime lastModApprovalWriteAttemptUtc = DateTime.MinValue;
        private DateTime lastModApprovalRevokeAttemptUtc = DateTime.MinValue;
        private string lastModApprovalHeartbeatKey;
        private bool modApprovalStateKnown;
        private bool modApprovalFilePresent;
        private long modPreflightUpdateTicks;
        private int modPreflightElapsedTicks;
        private long lastPersistedModHeartbeatTick = long.MinValue;
        private bool lastPersistedModPreflightPass;
        private bool modPreflightPersisted;
        private bool preflightWheelPowerLogged;
        private long lastModArrivalHostSampleTick = long.MinValue;
        private int modArrivalStableHeartbeatCount;
        private Vector3D startForward;
        private string commandKind;
        private long recorderId;
        private IMyPathRecorderComponent recorderComponent;
        private MyAiBlockComponent recordedRouteAiComponent;
        private MyAutopilotComponent recordedRouteAutopilot;
        private Sandbox.ModAPI.Ingame.IMyFlightMovementBlock recordedRouteMovement;
        private Sandbox.ModAPI.IMyProgrammableBlock recordedRouteProgrammable;
        private bool recordedRouteAiWasActivated;
        private bool recordedRouteAiActivatedByTrial;
        private bool recordedRouteAutopilotEnabledByTrial;
        private bool recordedRouteProgrammableStartedByTrial;
        private List<Sandbox.ModAPI.Ingame.IMyAutopilotWaypoint> routeWaypoints;
        private int routeWaypointIndex;
        private long cameraId;
        private int frameWidth;
        private int frameHeight;
        private double frameRange;
        private double frameFov;
        private string quarantine;
        private IMyCameraBlock sensorCamera;
        private long statorId;
        private long statorCameraId;
        private string statorExpectedName;
        private double statorDesiredAngleDegrees;
        private double statorMaxRpm;
        private IMyMotorStator targetStator;
        private int statorStableTicks;
        private ushort[] sensorDepths;
        private long[] sensorHitEntityIds;
        private short[] sensorSurfaceNormals;
        private readonly Dictionary<long, int> sensorEntityHitCounts = new Dictionary<long, int>();
        private readonly Dictionary<long, string> sensorEntityDisplayNames = new Dictionary<long, string>();
        private readonly Dictionary<long, string> sensorEntityRuntimeTypes = new Dictionary<long, string>();
        private MatrixD sensorMatrix;
        private Vector3D sensorOrigin;
        private Vector3D sensorNaturalGravity;
        private Vector3D sensorNaturalUp;
        private float sensorNaturalGravityInterference;
        private long sensorGridId;
        private int sensorIndex;
        private int sensorHits;
        private int sensorKnownEntityHits;
        private int sensorSelfGridHits;
        private int sensorValidNormalHits;
        private int sensorInvalidNormalHits;
        private Vector3D navigationTarget;
        private Vector3D navigationUp;
        private string navigationZone;
        private double arrivalRadius;
        private double initialTargetDistance;
        private double bestTargetDistance = double.MaxValue;
        private bool navigationReversing;
        private int lastProgressTick;
        private double lastSteeringCommand;
        private double lastSteeringPolarity;
        private double lastNavigationHeadingError;
        private double lastDriveInput;
        private double lastDriveDuty;
        private double lastControllerGridForwardAlignment;
        private double lastForwardWheelInput;
        private readonly Dictionary<long, float> originalWheelSpeedLimits = new Dictionary<long, float>();
        private readonly Dictionary<long, float> originalWheelPower = new Dictionary<long, float>();
        private readonly Dictionary<long, float> originalWheelSteerLimits = new Dictionary<long, float>();
        private readonly Dictionary<long, bool> originalWheelSteering = new Dictionary<long, bool>();
        private bool? originalHandBrake;
        private DirectManeuverPlan directManeuverPlan;
        private DirectManeuverExecutor directManeuverExecutor;
        private DirectManeuverActuatorBroker directManeuverBroker;
        private DirectManeuverStopCapability directManeuverStopCapability;
        private DirectManeuverOperationIdentity directManeuverIdentity;
        private DirectManeuverStep pendingDirectManeuverStep;
        private DirectManeuverStationaryGate directPreflightStationaryGate;
        private Vector3D directManeuverStartPosition;
        private DateTime directManeuverStartedUtc = DateTime.MinValue;
        private bool directManeuverStartPositionReady;
        private bool directBrakeActive;
        private bool modPreflightPass;
        private DateTime modPreflightCapturedUtc;
        private string directWriterAuditEvidence = "not-audited";
        private bool directWriterAuditAttempted;
        private bool directWriterAuditPassed;
        private List<Sandbox.Game.Entities.Cube.MyMotorSuspension> directCachedActuators;
        private long directCachedActuatorGridId, directCachedActuatorControllerId;
        private string directCachedActuatorOperationId;
        private DateTime directCachedActuatorExpiresUtc;
        private int directStopRequestSequence;
        private bool directEmergencyStopPending;
        private bool directManeuverStopUnresolved;
        private bool directEmergencyStopWriteAccepted;
        private bool directEmergencyStopTimeoutLogged;
        private bool directEmergencyStopCompletionFailureLogged;
        private string directStopRequestId;
        private DateTime directEmergencyStopStartedUtc;
        private string directEmergencyStopReason;
        private int directEmergencyStopSamples;
        private DateTime directEmergencyStopLastSampleUtc = DateTime.MinValue;
        private Vector3D directEmergencyStopLastPosition;
        private bool directEmergencyStopPositionReady;
        private double directEmergencyStopWindowDisplacement;
        private static readonly double[] NavigationDepthBeamAngles = new double[]
        {
            -90 * Math.PI / 180.0, -80 * Math.PI / 180.0, -70 * Math.PI / 180.0,
            -60 * Math.PI / 180.0, -50 * Math.PI / 180.0, -40 * Math.PI / 180.0,
            -30 * Math.PI / 180.0, -20 * Math.PI / 180.0, -10 * Math.PI / 180.0,
            0, 10 * Math.PI / 180.0, 20 * Math.PI / 180.0, 30 * Math.PI / 180.0,
            40 * Math.PI / 180.0, 50 * Math.PI / 180.0, 60 * Math.PI / 180.0,
            70 * Math.PI / 180.0, 80 * Math.PI / 180.0, 90 * Math.PI / 180.0
        };
        private const double NavigationDepthRangeMeters = 8.0;
        private const double NavigationDepthRequiredClearanceMeters = 3.40;
        private const int NavigationDepthScanIntervalTicks = 6;
        private const int NavigationDepthStableStartupTicks = 12;
        private const int NavigationDepthStartupTimeoutTicks = 120;
        private const int NavigationDepthNoMotionTimeoutTicks = 180;
        private const int CockpitPollIntervalTicks = 30;
        private int cockpitPollTicks;
        private bool cockpitBaselineReady;
        private bool cockpitObserverErrorLogged;
        private bool passiveTelemetryHandlerRegistered;
        private bool localVehicleTestMode;
        private bool localVehicleTestReadOnlyBaselineMode;
        private bool localVehicleTestBrakeOnly;
        private int localVehicleTestReadOnlyBaselineSamples;
        private long localVehicleTestReadOnlySampleSequence;
        private long localVehicleTestReadOnlyBaselineStartTimestamp;
        private long localVehicleTestReadOnlyLastSampleTimestamp;
        private Vector3D localVehicleTestReadOnlyReferencePosition;
        private bool localVehicleTestReadOnlyReferencePositionReady;
        private bool localVehicleTestReadOnlyBaselineReported;
        private LocalVehicleTestAuthorization localVehicleTestAuthorization;
        private LocalVehicleTestCommand localVehicleTestActiveCommand;
        private MyShipController localVehicleTestController;
        private List<Sandbox.Game.Entities.Cube.MyMotorSuspension> localVehicleTestWheels;
        private DateTime localVehicleTestLastSampleUtc = DateTime.MinValue;
        private int localVehicleTestStationarySamples;
        private DateTime localVehicleTestLastStopSampleUtc = DateTime.MinValue;
        private string localVehicleTestLastCommandId = string.Empty;
        private string localVehicleTestLastCommandWire = string.Empty;
        private const string LocalVehicleTestInstance = "land-local-test-v4r1-blue-20261022-a";
        private bool localVehicleTestStationarySteeringOnly;
        private bool localVehicleTestDirectionProofEnabled;
        private const double LocalVehicleTestMaximumRunDisplacementMeters = 25.0;
        private Vector3D localVehicleTestRunStartPosition;
        private bool localVehicleTestRunStartPositionReady;
        private Vector3D localVehicleTestStopStartPosition;
        private bool localVehicleTestStopStartPositionReady;
        private string localVehicleTestLastTerminalCommandId = string.Empty;
        private string localVehicleTestLastStopConfirmationCommandId = string.Empty;
        private readonly Dictionary<long, CockpitOccupant> cockpitOccupants = new Dictionary<long, CockpitOccupant>();
        private readonly DrivingLabPassiveTelemetryAdapter passiveTelemetry = new DrivingLabPassiveTelemetryAdapter();
        private readonly Dictionary<long, DirectControllerEventBinding> directControllerEventBindings =
            new Dictionary<long, DirectControllerEventBinding>();
        private long directControllerEventGridId;
        private int directControllerTransitionObserved;
        private DateTime directControllerReconcileAfterUtc = DateTime.MinValue;
        private bool directOtherControllerUnknownOrActive;
        private bool directControllerInventoryComplete;
        private bool localVehicleTestStopPathVerified;
        private bool localVehicleTestSteeringDirectionConfirmed;
        private bool localVehicleTestPreflightLogged;
        private double localVehicleTestSteeringAngleRadians;
        private int localVehicleTestUpdateTicks;
        private int localVehicleTestBaselineSamples;
        private long localVehicleTestBaselineStartTimestamp;
        private bool localVehicleTestBaselinePassed;
        private long localVehicleTestCameraId;
        private Vector3D localVehicleTestBaselinePosition;
        private bool localVehicleTestBaselinePositionReady;
        private readonly Dictionary<long, double[]> localVehicleTestBaselineRotorPositions = new Dictionary<long, double[]>();
        private readonly Dictionary<long, double[]> localVehicleTestStopRotorPositions = new Dictionary<long, double[]>();
        private long localVehicleTestLastStopSampleTimestamp;
        private volatile bool pluginDisposing;

        private sealed class DirectControllerEventBinding
        {
            public MyControllerInfo Info;
            public Action<MyEntityController> Acquired;
            public Action<MyEntityController> Released;
        }

        private sealed class CockpitOccupant
        {
            public long PilotEntityId;
            public string PilotName;
            public long GridId;
            public string GridName;
            public string CockpitName;
        }

        public void Init(object gameInstance)
        {
            enabled = ValidateLabEnvironment();
            if (!enabled)
                throw new InvalidOperationException(
                    "Voidwright Server Plugin requires an activated VRageCage destructive-lab run.");
            directCapabilityStorageReady = MyAPIGateway.Utilities != null;
            MyLog.Default.WriteLineAndConsole(
                "Voidwright server plugin: destructive-lab authority verified; actuator inactive.");
            MyLog.Default.WriteLineAndConsole(
                "Voidwright direct capability transport: run-bound WorldStorage; ready=" + directCapabilityStorageReady);
            localVehicleTestMode = string.Equals(
                Environment.GetEnvironmentVariable("VRAGECAGE_LOCAL_VEHICLE_TEST"), "1", StringComparison.Ordinal) &&
                string.Equals(Environment.GetEnvironmentVariable("VRAGECAGE_LOCAL_VEHICLE_TEST_INSTANCE"),
                    LocalVehicleTestInstance, StringComparison.Ordinal);
            localVehicleTestReadOnlyBaselineMode = localVehicleTestMode && string.Equals(
                Environment.GetEnvironmentVariable("VRAGECAGE_LOCAL_VEHICLE_TEST_READ_ONLY_BASELINE"), "true",
                StringComparison.OrdinalIgnoreCase);
            if (!localVehicleTestReadOnlyBaselineMode)
            {
                ReadCommand();
                EnsurePassiveTelemetryMessageHandler();
            }
            localVehicleTestStationarySteeringOnly = localVehicleTestMode && string.Equals(
                Environment.GetEnvironmentVariable("VRAGECAGE_LOCAL_VEHICLE_TEST_STEERING_ONLY"), "true",
                StringComparison.OrdinalIgnoreCase);
            localVehicleTestBrakeOnly = localVehicleTestMode && string.Equals(
                Environment.GetEnvironmentVariable("VRAGECAGE_LOCAL_VEHICLE_TEST_BRAKE_ONLY"), "true",
                StringComparison.OrdinalIgnoreCase);
            localVehicleTestDirectionProofEnabled = localVehicleTestMode && string.Equals(
                Environment.GetEnvironmentVariable("VRAGECAGE_LOCAL_VEHICLE_TEST_DIRECTION_CONFIRMED"), "true",
                StringComparison.OrdinalIgnoreCase) &&
                IsSha256Hex(Environment.GetEnvironmentVariable("VRAGECAGE_LOCAL_VEHICLE_TEST_DIRECTION_PROOF_LOG_SHA256")) &&
                IsSha256Hex(Environment.GetEnvironmentVariable("VRAGECAGE_LOCAL_VEHICLE_TEST_DIRECTION_PROOF_RECEIPT_SHA256")) &&
                IsSha256Hex(Environment.GetEnvironmentVariable("VRAGECAGE_LOCAL_VEHICLE_TEST_DIRECTION_PROOF_PLUGIN_TREE_SHA256"));
            if (localVehicleTestMode)
                MyLog.Default.WriteLineAndConsole("Voidwright local vehicle test lane enabled for the exact disposable instance; mode=" +
                    (localVehicleTestReadOnlyBaselineMode ? "read-only-baseline actuator-dispatch-disabled" :
                        localVehicleTestBrakeOnly ? "stationary-brake-only non-brake-commands-disabled" :
                        localVehicleTestStationarySteeringOnly ? "stationary-steering-only propulsion-hard-disabled" : "isolated-clear") +
                    "; steeringDirectionProof=" + localVehicleTestDirectionProofEnabled +
                    "; MCP and production dispatch gates are unchanged.");
        }

        /// <summary>
        /// Trusted local UI/service entry point. Calling this method represents
        /// the player's explicit opt-in; callers must pass the authenticated
        /// IMyPlayer object from the current server roster, never a model-supplied ID.
        /// This method configures observation only and has no control dispatch.
        /// </summary>
        public bool BeginDrivingLabPassiveCaptureFromTrustedOptIn(
            IMyPlayer authenticatedPlayer,
            string sessionId,
            string runId,
            DateTime consentExpiresAtUtc,
            int maximumSampleRateHz,
            int maximumBufferedRecords,
            int maximumBufferedBytes,
            out long exactGridEntityId)
        {
            exactGridEntityId = 0;
            if (!enabled || terminal || MySession.Static == null) return false;
            var activeRunId = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_RUN_ID") ?? string.Empty;
            if (!IsHexIdentity(sessionId) || !IsHexIdentity(runId) ||
                !string.Equals(runId, activeRunId, StringComparison.Ordinal) ||
                !string.Equals(sessionId, modSessionId, StringComparison.Ordinal))
                return false;
            if (!TryFindUniqueOccupiedCockpitGrid(authenticatedPlayer, out exactGridEntityId)) return false;
            var now = DateTime.UtcNow;
            return passiveTelemetry.TryStart(authenticatedPlayer, sessionId, runId,
                exactGridEntityId, now, consentExpiresAtUtc, maximumSampleRateHz,
                maximumBufferedRecords, maximumBufferedBytes);
        }

        /// <summary>Revoke the active capture only for the bound authenticated player.</summary>
        public bool RevokeDrivingLabPassiveCapture(IMyPlayer authenticatedPlayer)
        {
            return authenticatedPlayer != null &&
                   passiveTelemetry.Revoke(authenticatedPlayer.IdentityId);
        }

        public void Update()
        {
            if (!enabled) return;
            if (localVehicleTestReadOnlyBaselineMode)
            {
                if (terminal) return;
                UpdateLocalVehicleTestReadOnlyBaseline();
                return;
            }
            if (localVehicleTestMode && !terminal)
            {
                UpdateLocalVehicleTest(DateTime.UtcNow);
                return;
            }
            if (terminal)
            {
                if (DirectManeuverHostSamplingPolicy.ShouldContinue(true,
                    directManeuverExecutor != null && directManeuverExecutor.Outcome == DirectManeuverOutcome.Running,
                    directManeuverBroker != null && directManeuverBroker.SamplingRequired,
                    directManeuverStopUnresolved))
                {
                    UpdateDirectManeuverStopResolution();
                    return;
                }
                passiveTelemetry.StopAndExport();
                return;
            }
            if (MySession.Static == null)
            {
                if (IsMovementCommand() && !IsModOwnedDepthCommand()) BlockMovementForPreflight(BuildMovementPreflight());
                return;
            }
            EnsurePassiveTelemetryMessageHandler();
            ObserveCockpitOccupancy();
            passiveTelemetry.Update();
            if (commandKind == "sensor-frame")
            {
                CaptureSensorFrame();
                return;
            }
            if (commandKind == "discovery-only")
            {
                UpdateDiscoveryBootstrap();
                return;
            }
            if (!IsMovementCommand()) return;

            if (commandKind == "direct-maneuver")
            {
                UpdateDirectManeuver();
                return;
            }

            if (IsModOwnedDepthCommand())
            {
                modPreflightElapsedTicks++;
                if (timeoutTicks <= 0 || MovementPreflight.HasOperationTimedOut(
                        modPreflightElapsedTicks, 0, timeoutTicks))
                {
                    FinishModOwnedPreflightTimeout();
                    return;
                }
                if (!EnsureModActivation()) return;
                ReadModHeartbeat();
                var modPreflight = BuildMovementPreflight();
                var hostPass = modPreflight.IsAuthorizing(DateTime.UtcNow,
                    Environment.GetEnvironmentVariable("VRAGECAGE_LAB_RUN_ID"), operationId,
                    CurrentFixtureFingerprint(), CurrentMovementStateFingerprint(), TimeSpan.FromSeconds(1));
                modPreflightUpdateTicks++;
                var heartbeatTick = modHeartbeat == null ? -1 : modHeartbeat.SimulationTick;
                var persistNow = !modPreflightPersisted || heartbeatTick != lastPersistedModHeartbeatTick ||
                    hostPass != lastPersistedModPreflightPass || (!hostPass && modPreflightUpdateTicks % 30 == 0);
                if (persistNow && !PersistMovementPreflight(modPreflight))
                {
                    modHandshakeReason = "host preflight evidence could not be persisted";
                    RevokeModApproval();
                    return;
                }
                if (persistNow)
                {
                    modPreflightPersisted = true;
                    lastPersistedModHeartbeatTick = heartbeatTick;
                    lastPersistedModPreflightPass = hostPass;
                }
                if (hostPass && modHeartbeatFresh)
                {
                    hostPass = WriteModApproval();
                    if (!hostPass) PersistMovementPreflight(BuildMovementPreflight());
                }
                else hostPass = false;
                if (commandKind == "preflight-only" && !preflightWheelPowerLogged)
                {
                    LogPreflightWheelPowerSnapshot();
                    preflightWheelPowerLogged = true;
                }
                if (!hostPass) RevokeModApproval();
                if (hostPass)
                {
                    if (!preflightSuccessLogged)
                    {
                        LogMovementPreflight(modPreflight);
                        preflightSuccessLogged = true;
                    }
                    if (commandKind == "preflight-only")
                    {
                        terminal = true;
                        MyLog.Default.WriteLineAndConsole("Voidwright evidence: type=preflight-only operation=" + operationId +
                            " controllerId=" + controllerId + " cameraId=" + navigationDepthCameraId +
                            " status=complete elapsedTicks=" + modPreflightElapsedTicks +
                            " actuatorDispatch=none reason=host_approved_run_bound_preflight");
                    }
                    if (commandKind != "preflight-only" && CompleteModOwnedDepthArrivalIfStable()) return;
                }
                return; // The ordinary mod adapter owns locomotion; this plugin remains a passive reviewer.
            }

            var preflight = BuildMovementPreflight();
            if (!PersistMovementPreflight(preflight))
            {
                BlockMovementForPreflight(preflight.Invalidate(DateTime.UtcNow,
                    "The machine-readable preflight result could not be persisted."));
                return;
            }
            var runId = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_RUN_ID");
            var fixtureFingerprint = CurrentFixtureFingerprint();
            var stateFingerprint = CurrentMovementStateFingerprint();
            var dispatched = MovementCommandGate.TryDispatch(preflight, DateTime.UtcNow,
                runId, operationId, fixtureFingerprint, stateFingerprint,
                TimeSpan.FromSeconds(1), delegate { UpdateMovementCommandCore(); });
            if (!dispatched)
                BlockMovementForPreflight(preflight.Invalidate(DateTime.UtcNow,
                    "Run identity, operation identity, fixture fingerprint, state fingerprint, check freshness, or required PASS evidence changed before dispatch."));
            else if (!preflightSuccessLogged)
            {
                LogMovementPreflight(preflight);
                preflightSuccessLogged = true;
            }
        }

        private bool IsModOwnedDepthCommand()
        {
            return ModOwnedCommandRoutingPolicy.ShouldHostReview(commandKind,
                Environment.GetEnvironmentVariable("VRAGECAGE_LAB_MOTION_OWNER"));
        }

        private void UpdateDirectManeuver()
        {
            if (directManeuverStopUnresolved)
            {
                UpdateDirectManeuverStopResolution();
                return;
            }
            if (Environment.GetEnvironmentVariable("VRAGECAGE_LAB_MOTION_OWNER") != "voidwright-plugin" ||
                directManeuverPlan == null)
            {
                FinishDirectManeuver("preflight-blocked", "server-plugin actuator ownership or parsed maneuver plan is missing", false);
                return;
            }
            modPreflightElapsedTicks++;
            if (!EnsureModActivation())
            {
                if (directManeuverExecutor != null) EmergencyStopDirectManeuver("host activation became invalid");
                else if (modPreflightElapsedTicks >= timeoutTicks)
                    FinishDirectManeuver("preflight-blocked", modHandshakeReason, false);
                return;
            }
            // Magnetar runs MySession.Static.Update (including the mod heartbeat
            // publisher) before IPlugin.Update. This is the current session
            // sample; any token artifact written below is for the mod's next
            // session update. Dispatch re-reads both artifacts before every setter.
            ReadModHeartbeat();
            var now = DateTime.UtcNow;
            ReadModSessionLiveness(now);
            ReadModPreflightAttestation(now);
            var preflight = BuildMovementPreflight();
            // The host snapshot timestamps its checks while it is built. Refresh
            // the operation clock afterwards so the same evidence is never
            // judged against the earlier time captured before that snapshot.
            now = DateTime.UtcNow;
            var runId = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_RUN_ID") ?? string.Empty;
            var fixtureFingerprint = CurrentFixtureFingerprint();
            var stateFingerprint = CurrentMovementStateFingerprint();
            // BuildMovementPreflight stamps its checks after the caller's earlier
            // heartbeat read. Authorize against a fresh clock read so the gate
            // does not reject the snapshot simply because its timestamps are a
            // few microseconds newer than the pre-build time.
            var preliminaryPass = preflight.IsAuthorizing(DateTime.UtcNow, runId, operationId,
                fixtureFingerprint, stateFingerprint, TimeSpan.FromSeconds(1));
            var heartbeatTick = modHeartbeat == null ? -1 : modHeartbeat.SimulationTick;
            var persistNow = !modPreflightPersisted || heartbeatTick != lastPersistedModHeartbeatTick ||
                preliminaryPass != lastPersistedModPreflightPass;
            var persisted = !persistNow || PersistMovementPreflight(preflight);
            if (persistNow && persisted)
            {
                modPreflightPersisted = true;
                lastPersistedModHeartbeatTick = heartbeatTick;
                lastPersistedModPreflightPass = preliminaryPass;
            }
            var hostPass = persisted && preflight.IsAuthorizing(DateTime.UtcNow, runId, operationId,
                fixtureFingerprint, stateFingerprint, TimeSpan.FromSeconds(1));
            var approvalResult = "not-attempted";
            if (hostPass && modHeartbeatFresh)
            {
                if (!WriteModApproval())
                {
                    approvalResult = "write-failed";
                    hostPass = false;
                    RevokeModApproval();
                }
                else approvalResult = "written";
            }
            else
            {
                RevokeModApproval();
                hostPass = false;
            }
            var handshakeDiagnostic = "tick=" + modPreflightElapsedTicks + ";heartbeatTick=" + heartbeatTick +
                ";preliminaryPass=" + preliminaryPass + ";persisted=" + persisted + ";hostPass=" + hostPass +
                ";modHeartbeatFresh=" + modHeartbeatFresh + ";modPreflightPass=" + modPreflightPass +
                ";approval=" + approvalResult + ";reason=" + modHandshakeReason;
            if (!string.Equals(lastDirectHandshakeDiagnostic, handshakeDiagnostic, StringComparison.Ordinal))
            {
                MyLog.Default.WriteLineAndConsole("[VRageCage][PREFLIGHT] direct-handshake " + handshakeDiagnostic);
                lastDirectHandshakeDiagnostic = handshakeDiagnostic;
            }

            MyEntity entity;
            if (!MyEntities.TryGetEntityById(controllerId, out entity) || (controller = entity as MyShipController) == null ||
                controller.CubeGrid == null || controller.CubeGrid.EntityId != expectedGridId ||
                controller.CubeGrid.Physics == null || controller.GridWheels == null)
            {
                if (directManeuverExecutor != null) EmergencyStopDirectManeuver("exact controller or vehicle grid disappeared");
                else if (modPreflightElapsedTicks >= timeoutTicks)
                    FinishDirectManeuver("preflight-blocked", "exact controller or vehicle grid did not become available", false);
                return;
            }

            var sample = BuildDirectManeuverSample(now);
            if (directEmergencyStopPending)
            {
                MonitorDirectEmergencyStop(sample, now);
                return;
            }
            if (!EnsureDirectActuatorSession(controller, now, out var actuatorSessionReason))
            {
                if (directManeuverExecutor != null)
                    EmergencyStopDirectManeuver("cached actuator session invalid: " + actuatorSessionReason);
                else if (modPreflightElapsedTicks >= timeoutTicks)
                    FinishDirectManeuver("preflight-blocked", "exact-grid LAND actuator cache failed: " + actuatorSessionReason, false);
                return;
            }
            if ((!sample.SensorValid || !sample.ObstacleClear || !sample.BrakeOwnershipKnown ||
                sample.ExternalBrakeActive) && directManeuverExecutor == null)
            {
                if (modPreflightElapsedTicks >= timeoutTicks)
                    FinishDirectManeuver("preflight-blocked", "sensor, obstacle clearance, or initial brake state is not safe", false);
                return;
            }

            if (directManeuverExecutor == null)
            {
                if (!hostPass || !modPreflightPass || !modHeartbeatFresh)
                {
                    if (modPreflightElapsedTicks >= timeoutTicks)
                        FinishDirectManeuver("preflight-blocked", "fresh all-pass host and mod preflights did not arrive before the bounded deadline", false);
                    return;
                }
                if (directPreflightStationaryGate == null || !directPreflightStationaryGate.Observe(
                        sample.HostSampleSequence, sample.SpeedMetersPerSecond,
                        sample.PositionX, sample.PositionY, sample.PositionZ))
                {
                    if (modPreflightElapsedTicks >= timeoutTicks - DirectManeuverPlan.MaximumStopConfirmationTicks)
                        FinishDirectManeuver("preflight-blocked", "vehicle did not provide three distinct stationary samples within the existing rest-speed and displacement bounds", false);
                    return;
                }
                var evidence = BuildDirectManeuverEvidence(preflight, hostPass, modPreflightPass, sample);
                var actuatorStates = sample.ActuatorStates;
                directCapabilityStorageReady = MyAPIGateway.Utilities != null;
                var operatorControl = controller as Sandbox.ModAPI.Ingame.IMyShipController;
                if (!directWriterAuditAttempted)
                {
                    directWriterAuditPassed = AuditDirectManeuverWriters(controller, out directWriterAuditEvidence);
                    directWriterAuditAttempted = true;
                }
                var participatingWritersClear = directWriterAuditPassed;
                var broker = new DirectManeuverActuatorBroker(new[] { "vragecage-server-plugin" },
                    participatingWritersClear
                        ? DirectManeuverExternalWriterState.LocalParticipatingWriterAuditPassed
                        : DirectManeuverExternalWriterState.Unknown);
                var takeoverStable = DirectManeuverControllerStateReady(controller, out var takeoverEvidence);
                var brokerCapabilitiesReady = DirectManeuverBrokerDispatchGate.CanDispatch(
                    // WorldStorage availability provides transport, not an independent
                    // authority/lease arbiter. Keep movement dispatch fail-closed until one exists.
                    independentlyVerifiedLeaseAuthority: false,
                    freshHostAttestation: hostPass,
                    freshModAttestation: modPreflightPass && modHeartbeatFresh,
                    operatorFree: operatorControl != null && !operatorControl.IsUnderControl,
                    participatingWritersAudited: participatingWritersClear,
                    completeChannelReadback: modHeartbeat != null && sample.ActuatorStates != null &&
                        sample.ActuatorStates.Count == modHeartbeat.WheelCount,
                    operationScopedStop: sample.ControllerBrakeState != null && sample.ControllerBrakeState.GridBrakeKnown &&
                        sample.ControllerBrakeState.HandBrakeKnown && controller.GridWheels != null,
                    controllerPriorityStable: takeoverStable);
                if (!brokerCapabilitiesReady)
                {
                    FinishDirectManeuver("preflight-blocked",
                        "cooperative broker capability gate denied the exact run-bound operation; controller=" + takeoverEvidence, false);
                    return;
                }
                if (!broker.TryAcquire(directManeuverPlan, evidence, actuatorStates,
                        sample.ControllerBrakeState, now, DirectManeuverOperationLease.MaximumDuration,
                        runtimeCapabilityReady: brokerCapabilitiesReady, out var acquireReason))
                {
                    FinishDirectManeuver("preflight-blocked", "operation lease acquisition failed: " + acquireReason, false);
                    return;
                }
                directManeuverBroker = broker;
                directManeuverIdentity = new DirectManeuverOperationIdentity(
                    directManeuverPlan.RunId, directManeuverPlan.FixtureId, directManeuverPlan.OperationId,
                    directManeuverPlan.GridId, directManeuverPlan.ControllerId, directManeuverPlan.AgentCoreId);
                if (!RefreshDirectCapabilityArtifact(now))
                {
                    EmergencyStopDirectManeuver("host capability artifact could not be written and read back");
                    return;
                }
                directManeuverStartPosition = new Vector3D(sample.PositionX, sample.PositionY, sample.PositionZ);
                directManeuverStartedUtc = now.ToUniversalTime();
                directManeuverStartPositionReady = true;
                directBrakeActive = false;
                directManeuverExecutor = new DirectManeuverExecutor(directManeuverPlan,
                    directManeuverBroker.ActiveLease(now));
                var initial = directManeuverExecutor.Start(evidence, sample, now);
                pendingDirectManeuverStep = initial;
                LogMovementPreflight(preflight);
                return;
            }

            var currentCapabilityToken = directManeuverBroker == null || directManeuverIdentity == null
                ? string.Empty : directManeuverBroker.GetMovementCapabilityToken(directManeuverIdentity, now);
            if (directManeuverBroker != null && directManeuverIdentity != null &&
                !directManeuverBroker.StopPending && !RefreshDirectCapabilityArtifact(now))
            {
                EmergencyStopDirectManeuver("run-bound host capability artifact became unavailable");
                return;
            }
            if (pendingDirectManeuverStep != null)
            {
                if (!hostPass || !modPreflightPass || !modHeartbeatFresh)
                {
                    EmergencyStopDirectManeuver("host/mod attestation became stale before capability handoff");
                    return;
                }
                if (string.IsNullOrEmpty(currentCapabilityToken) || modHeartbeat == null ||
                    !string.Equals(modHeartbeat.OperationCapabilityToken, currentCapabilityToken, StringComparison.Ordinal))
                {
                    if (modPreflightElapsedTicks >= timeoutTicks - DirectManeuverPlan.MaximumStopConfirmationTicks)
                        EmergencyStopDirectManeuver("mod capability echo did not arrive before the bounded start deadline");
                    return;
                }
                var displacementBeforeFirstWrite = Vector3D.Distance(directManeuverStartPosition,
                    new Vector3D(sample.PositionX, sample.PositionY, sample.PositionZ));
                if (!sample.SensorValid || !sample.ObstacleClear || !sample.BrakeOwnershipKnown ||
                    sample.ExternalBrakeActive ||
                    Math.Abs(sample.SpeedMetersPerSecond) > DirectManeuverLifecycle.StationaryConfirmationSpeedMetersPerSecond ||
                    displacementBeforeFirstWrite > DirectManeuverLifecycle.StationaryDisplacementMeters)
                {
                    EmergencyStopDirectManeuver("start-state changed while awaiting the mod capability echo");
                    return;
                }
                if (!DirectManeuverControllerStateReady(controller, out var pendingControllerState))
                {
                    EmergencyStopDirectManeuver("controller takeover state is unknown before first write: " + pendingControllerState);
                    return;
                }
                var pending = pendingDirectManeuverStep;
                pendingDirectManeuverStep = null;
                DispatchDirectManeuverStep(pending, now);
                return;
            }
            if (string.IsNullOrEmpty(currentCapabilityToken) || modHeartbeat == null ||
                !string.Equals(modHeartbeat.OperationCapabilityToken, currentCapabilityToken, StringComparison.Ordinal))
            {
                EmergencyStopDirectManeuver("mod capability echo became stale or was revoked");
                return;
            }
            if (!DirectManeuverControllerStateReady(controller, out var activeControllerState))
            {
                EmergencyStopDirectManeuver("controller takeover state became unknown or changed: " + activeControllerState);
                return;
            }

            if (hostPass && modPreflightPass && modHeartbeatFresh &&
                modPreflightElapsedTicks >= timeoutTicks - DirectManeuverPlan.MaximumStopConfirmationTicks)
                RequestDirectEmergencyStop("operation deadline reached; ending maneuver inside the 45-second envelope");
            var offset = Vector3D.Distance(directManeuverStartPosition,
                new Vector3D(sample.PositionX, sample.PositionY, sample.PositionZ));
            var segmentIndex = directManeuverExecutor.CurrentSegmentIndex;
            if (segmentIndex < 0 || segmentIndex >= directManeuverPlan.Segments.Count)
            {
                RequestDirectEmergencyStop("active maneuver segment identity became invalid");
                return;
            }
            var activeSegment = directManeuverPlan.Segments[segmentIndex];
            if (DirectManeuverExecutor.TryGetImmediateSafetyStopReason(
                    hostPass && modPreflightPass && modHeartbeatFresh,
                    directManeuverStartedUtc, now, sample.CapturedUtc,
                    activeSegment.Kind, sample.SpeedMetersPerSecond,
                    activeSegment.MaximumSpeedMetersPerSecond, offset,
                    DirectManeuverHardExcursionLimitMeters, out var safetyStopReason))
            {
                RequestDirectEmergencyStop(safetyStopReason +
                    (offset >= DirectManeuverHardExcursionLimitMeters
                        ? " at 7.5 meters before the eight-meter operation envelope" : string.Empty));
                return;
            }

            var liveEvidence = BuildDirectManeuverEvidence(preflight, hostPass, modPreflightPass, sample);
            if (!hostPass || !modPreflightPass || !modHeartbeatFresh)
                liveEvidence = BuildDirectManeuverEvidence(preflight, false, false, sample);
            var step = directManeuverExecutor.Step(liveEvidence, sample, now);
            if (step.Command != null) DispatchDirectManeuverStep(step, now);
            if (sample.HostSampleSequence % 10 == 0)
                LogDirectManeuverSample(sample, step);
            if (directManeuverExecutor.Outcome == DirectManeuverOutcome.Complete ||
                directManeuverExecutor.Outcome == DirectManeuverOutcome.Cancelled ||
                directManeuverExecutor.Outcome == DirectManeuverOutcome.EmergencyStopped ||
                directManeuverExecutor.Outcome == DirectManeuverOutcome.TimedOut)
                CompleteDirectManeuverStop(sample, directManeuverExecutor.Outcome, now);
            else if (directManeuverExecutor.Outcome == DirectManeuverOutcome.Failed ||
                directManeuverExecutor.Outcome == DirectManeuverOutcome.ActuatorConflict ||
                directManeuverExecutor.Outcome == DirectManeuverOutcome.SensorBlocked ||
                directManeuverExecutor.Outcome == DirectManeuverOutcome.PreflightBlocked)
                EmergencyStopDirectManeuver("executor outcome=" + directManeuverExecutor.Outcome + "; " + step.Reason);
        }

        private DirectManeuverExecutionSample BuildDirectManeuverSample(DateTime nowUtc)
        {
            var states = new List<DirectManeuverWheelState>();
            var minimumClearance = double.MaxValue;
            var obstacleClear = modHeartbeatFresh && modHeartbeat != null &&
                modHeartbeat.SensorValid && modHeartbeat.ValidRayCount == VoidwrightPreflightHeartbeat.RayCount;
            if (modHeartbeat != null && modHeartbeat.Clearances != null)
                foreach (var clearance in modHeartbeat.Clearances)
                {
                    minimumClearance = Math.Min(minimumClearance, clearance);
                    obstacleClear &= Finite(clearance) && clearance >= DirectManeuverPlan.MinimumClearanceMeters;
                }
            var wheels = controller == null || controller.GridWheels == null ? null : controller.GridWheels.Wheels;
            var minimumPropulsionWheelPower = double.MaxValue;
            if (wheels != null)
                foreach (var wheel in wheels)
                {
                    if (wheel == null) { obstacleClear = false; continue; }
                    if (wheel.Propulsion)
                        minimumPropulsionWheelPower = Math.Min(minimumPropulsionWheelPower, wheel.Power);
                    bool revolveInvert;
                    var driveStateKnown = DirectManeuverWheelRevolveInvert(controller, wheel, out revolveInvert);
                    var wheelHandBrake = wheel.Handbrake;
                    var wheelBrake = wheel.Brake;
                    var brakingEnabled = wheel.BrakingEnabled;
                    var shouldBrake = brakingEnabled && (wheelBrake ||
                        (wheelHandBrake && wheel.IsParkingEnabled));
                    states.Add(new DirectManeuverWheelState(wheel.EntityId,
                        wheel.SteeringOverride, wheel.PropulsionOverride, true,
                        wheelBrake || wheelHandBrake, nowUtc, driveStateKnown, revolveInvert,
                        wheel.InvertPropulsion, wheel.InvertSteer, wheel.Power, brakingEnabled,
                        wheelBrake, wheelHandBrake, shouldBrake));
                }
            var physics = controller == null || controller.CubeGrid == null ? null : controller.CubeGrid.Physics;
            var position = physics == null ? Vector3D.Zero : physics.CenterOfMassWorld;
            var velocity = physics == null ? Vector3D.Zero : new Vector3D(physics.LinearVelocity);
            var forward = controller == null || controller.CubeGrid == null
                ? Vector3D.Zero : controller.WorldMatrix.Forward;
            var up = controller == null ? Vector3D.Zero : new Vector3D(controller.GetTotalGravity());
            if (up.LengthSquared() > 0.0001)
            {
                up.Normalize();
                up = -up;
            }
            else if (controller != null && controller.CubeGrid != null)
            {
                up = new Vector3D(controller.CubeGrid.WorldMatrix.Up);
                if (up.LengthSquared() > 0.0001) up.Normalize();
            }
            forward -= up * Vector3D.Dot(forward, up);
            if (forward.LengthSquared() > 0.0001) forward.Normalize();
            var reference = Vector3D.Right - up * Vector3D.Dot(Vector3D.Right, up);
            if (reference.LengthSquared() < 0.0001)
                reference = Vector3D.Forward - up * Vector3D.Dot(Vector3D.Forward, up);
            if (reference.LengthSquared() > 0.0001) reference.Normalize();
            var right = Vector3D.Cross(up, reference);
            if (right.LengthSquared() > 0.0001) right.Normalize();
            var heading = Math.Atan2(Vector3D.Dot(forward, right),
                Vector3D.Dot(forward, reference)) * 180.0 / Math.PI;
            var averageSteeringAngle = 0.0;
            var steeringCount = 0;
            if (wheels != null)
                foreach (var wheel in wheels)
                    if (wheel != null && wheel.Steering)
                    { averageSteeringAngle += wheel.SteerAngle; steeringCount++; }
            double? observedSteeringAngle = steeringCount == 0
                ? (double?)null : averageSteeringAngle / steeringCount;
            var brakes = controller == null || controller.GridWheels == null ? null :
                new DirectManeuverBrakeState(true, controller.GridWheels.Brake, true,
                    controller.GridWheels.HandBrake, nowUtc);
            var freshSensorTick = modHeartbeat == null ? -1 : modHeartbeat.SensorScanTick;
            var freshSensorTime = modHeartbeat == null ? DateTime.MinValue : modHeartbeat.SensorScanUtc;
            return new DirectManeuverExecutionSample(
                Environment.GetEnvironmentVariable("VRAGECAGE_LAB_RUN_ID"),
                Environment.GetEnvironmentVariable("VRAGECAGE_LAB_FIXTURE_ID"), operationId,
                controller == null || controller.CubeGrid == null ? 0 : controller.CubeGrid.EntityId,
                controller == null ? 0 : controller.EntityId,
                modHeartbeat == null ? 0 : modHeartbeat.SensorGridId,
                ++directManeuverHostSampleSequence,
                nowUtc, freshSensorTime, position.X, position.Y, position.Z, heading,
                velocity.Length(), observedSteeringAngle,
                modHeartbeatFresh && modHeartbeat != null && modHeartbeat.SensorValid,
                obstacleClear, minimumClearance == double.MaxValue ? 0 : minimumClearance,
                brakes != null && brakes.GridBrakeKnown && brakes.HandBrakeKnown,
                controller != null && controller.GridWheels != null,
                brakes != null && (brakes.GridBrakeActive || brakes.HandBrakeActive) && !directBrakeActive,
                states, brakes, forward.X, forward.Y, forward.Z,
                Vector3D.Dot(velocity, forward), DirectManeuverPropulsionPolarity(),
                minimumPropulsionWheelPower == double.MaxValue ? 0 : minimumPropulsionWheelPower,
                modHeartbeat == null ? -1 : modHeartbeat.SimulationTick);
        }

        private double DirectManeuverPropulsionPolarity()
        {
            if (controller == null || controller.CubeGrid == null || controller.GridWheels == null)
                return 0;

            var controllerForward = controller.WorldMatrix.Forward;
            var gridForward = controller.CubeGrid.WorldMatrix.Forward;
            if (controllerForward.LengthSquared() < 0.0001 || gridForward.LengthSquared() < 0.0001)
                return 0;
            controllerForward.Normalize();
            gridForward.Normalize();
            var alignment = Vector3D.Dot(controllerForward, gridForward);
            return WheelPropulsionMapping.ResolvePolarity(alignment, false);
        }

        // Mirrors MyMotorSuspension.InitControl from the installed game build.
        // Direct overrides bypass RevolveInvert in Accelerate, so this is
        // calculated from the live controller and suspension transforms.
        private static bool DirectManeuverWheelRevolveInvert(MyShipController shipController,
            Sandbox.Game.Entities.Cube.MyMotorSuspension wheel, out bool revolveInvert)
        {
            revolveInvert = false;
            if (shipController == null || wheel == null || wheel.PositionComp == null) return false;
            var controllerToWheel = shipController.WorldMatrix * wheel.PositionComp.WorldMatrixNormalizedInv;
            Vector3 reference;
            var relativeForward = Base6Directions.GetClosestDirection(controllerToWheel.Forward);
            if (relativeForward == Base6Directions.Direction.Up ||
                relativeForward == Base6Directions.Direction.Down)
            {
                reference = shipController.WorldMatrix.Forward;
            }
            else
            {
                var relativeUp = Base6Directions.GetClosestDirection(controllerToWheel.Up);
                if (relativeUp == Base6Directions.Direction.Up ||
                    relativeUp == Base6Directions.Direction.Down)
                    reference = shipController.WorldMatrix.Up;
                else
                    reference = shipController.WorldMatrix.Right;
            }
            revolveInvert = (wheel.WorldMatrix.Up - reference).Length() > 0.1;
            return true;
        }

        private DirectManeuverEvidence BuildDirectManeuverEvidence(MovementPreflight preflight,
            bool hostPass, bool modPass, DirectManeuverExecutionSample sample)
        {
            return new DirectManeuverEvidence(
                Environment.GetEnvironmentVariable("VRAGECAGE_LAB_RUN_ID"),
                Environment.GetEnvironmentVariable("VRAGECAGE_LAB_FIXTURE_ID"), operationId,
                expectedGridId, controllerId, sample.SensorGridId,
                sample.MinimumClearanceMeters, preflight == null ? DateTime.MinValue : preflight.CreatedAtUtc,
                hostPass, modPreflightCapturedUtc, modPass, modHeartbeat == null ? DateTime.MinValue : modHeartbeat.SensorScanUtc,
                sample.SensorValid, sample.ObstacleClear,
                false, sample.BrakeOwnershipKnown,
                controller != null && controller.GridWheels != null,
                sample.ControllerBrakeState != null &&
                    sample.ControllerBrakeState.GridBrakeKnown && sample.ControllerBrakeState.HandBrakeKnown &&
                    controller != null && controller.GridWheels != null,
                modHeartbeat == null ? 0 : modHeartbeat.AgentCoreId);
        }

        private bool RefreshDirectCapabilityArtifact(DateTime nowUtc)
        {
            if (!directCapabilityStorageReady || MyAPIGateway.Utilities == null ||
                directManeuverBroker == null || directManeuverIdentity == null ||
                string.IsNullOrEmpty(modSessionId)) return false;
            var token = directManeuverBroker.GetMovementCapabilityToken(directManeuverIdentity, nowUtc);
            if (!VoidwrightOperationCapability.IsToken(token))
            {
                RevokeDirectCapabilityArtifact();
                return false;
            }
            if (string.Equals(lastDirectCapabilityArtifactToken, token, StringComparison.Ordinal) &&
                nowUtc.ToUniversalTime() - lastDirectCapabilityArtifactIssuedUtc < DirectCapabilityArtifactRefreshInterval)
                return true;
            VoidwrightModActivation activation;
            if (!VoidwrightModActivation.TryParse(modActivationText, nowUtc, out activation) ||
                activation.Action != "direct-maneuver" || activation.OperationId != operationId ||
                activation.ContextSha256 != Environment.GetEnvironmentVariable("VRAGECAGE_LAB_PREFLIGHT_CONTEXT_SHA256") ||
                activation.Challenge != Environment.GetEnvironmentVariable("VRAGECAGE_LAB_PREFLIGHT_CHALLENGE"))
                return false;
            var issued = nowUtc.ToUniversalTime();
            var artifact = new VoidwrightOperationCapability
            {
                RunId = activation.RunId, FixtureId = activation.FixtureId,
                OperationId = activation.OperationId, ContextSha256 = activation.ContextSha256,
                Challenge = activation.Challenge, SessionId = modSessionId,
                GridId = expectedGridId, ControllerId = controllerId,
                AgentCoreId = expectedAgentCoreId, CameraId = navigationDepthCameraId,
                DiscoveryRunId = expectedDiscoveryRunId, DiscoveryTick = expectedDiscoveryTick,
                DiscoveryFingerprint = expectedDiscoveryFingerprint, Token = token,
                IssuedUtc = issued, ExpiresUtc = issued + VoidwrightOperationCapability.MaximumLifetime
            };
            var serialized = artifact.Format();
            try
            {
                using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(
                           VoidwrightOperationCapability.FileName, typeof(MySessionComponentBase)))
                    writer.Write(serialized);
                using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(
                           VoidwrightOperationCapability.FileName, typeof(MySessionComponentBase)))
                    if (!string.Equals(serialized, reader.ReadToEnd(), StringComparison.Ordinal)) return false;
                lastDirectCapabilityArtifactToken = token;
                lastDirectCapabilityArtifactIssuedUtc = issued;
                return true;
            }
            catch (Exception error)
            {
                MyLog.Default.WriteLineAndConsole("Voidwright evidence: type=direct-maneuver-capability status=write-failed reason=" +
                    error.GetType().Name);
                return false;
            }
        }

        private void RevokeDirectCapabilityArtifact()
        {
            lastDirectCapabilityArtifactToken = null;
            lastDirectCapabilityArtifactIssuedUtc = DateTime.MinValue;
            if (MyAPIGateway.Utilities == null) return;
            try
            {
                using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(
                           VoidwrightOperationCapability.FileName, typeof(MySessionComponentBase)))
                    writer.Write(string.Empty);
            }
            catch { }
        }

        private bool IsCurrentDirectCapabilityEcho(DateTime nowUtc, string expectedToken)
        {
            ReadModHeartbeat();
            var heartbeat = modHeartbeat;
            if (!modHeartbeatFresh || heartbeat == null || directManeuverBroker == null ||
                directManeuverIdentity == null || !VoidwrightOperationCapability.IsToken(expectedToken) ||
                !directManeuverBroker.IsMovementCapabilityCurrent("vragecage-server-plugin",
                    directManeuverIdentity, expectedToken, nowUtc) ||
                !string.Equals(heartbeat.OperationCapabilityToken, expectedToken, StringComparison.Ordinal))
                return false;
            VoidwrightModActivation activation;
            if (!VoidwrightModActivation.TryParse(modActivationText, nowUtc, out activation) ||
                !heartbeat.IsFreshAndBound(nowUtc, DirectModHeartbeatMaximumAge,
                    activation.RunId, activation.FixtureId, activation.OperationId,
                    activation.ContextSha256, activation.Challenge, modSessionId,
                    controllerId, expectedGridId, navigationDepthCameraId,
                    expectedAgentCoreId, expectedDiscoveryRunId, expectedDiscoveryTick,
                    expectedDiscoveryFingerprint) ||
                nowUtc.ToUniversalTime() - heartbeat.SensorScanUtc.ToUniversalTime() > TimeSpan.FromSeconds(1) ||
                heartbeat.SimulationTick - heartbeat.SensorScanTick > 20)
                return false;
            try
            {
                string serialized;
                using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(
                           VoidwrightOperationCapability.FileName, typeof(MySessionComponentBase)))
                    serialized = reader.ReadToEnd();
                VoidwrightOperationCapability artifact;
                return VoidwrightOperationCapability.TryParse(serialized, nowUtc, out artifact) &&
                    artifact.Matches(nowUtc, activation.RunId, activation.FixtureId,
                        activation.OperationId, activation.ContextSha256, activation.Challenge,
                        modSessionId, expectedGridId, controllerId, expectedAgentCoreId,
                        navigationDepthCameraId, expectedDiscoveryRunId, expectedDiscoveryTick,
                        expectedDiscoveryFingerprint, expectedToken);
            }
            catch { return false; }
        }

        private void DispatchDirectManeuverStep(DirectManeuverStep step, DateTime nowUtc)
        {
            if (step == null || step.Command == null || directManeuverExecutor == null ||
                directManeuverBroker == null || directManeuverIdentity == null) return;
            var command = step.Command;
            var controlledStop = command.Kind == DirectManeuverDispatchKind.ControlledStop;
            var zeroActuationOnly = directManeuverPlan != null && directManeuverPlan.IsZeroActuationOnly;
            if (zeroActuationOnly && (!controlledStop ||
                !directManeuverPlan.AllowsActuatorValues(command.Steering, command.Propulsion)))
            {
                EmergencyStopDirectManeuver("zero-only maneuver produced a nonzero wheel command");
                return;
            }
            var propulsionByWheel = new Dictionary<long, double>();
            var capabilityToken = modHeartbeat == null ? string.Empty : modHeartbeat.OperationCapabilityToken;
            var accepted = directManeuverBroker.TryRunCapabilityTransaction(
                "vragecage-server-plugin", directManeuverIdentity, capabilityToken, nowUtc, delegate
                {
                    if (controller == null || controller.MarkedForClose || controller.GridWheels == null ||
                        controller.CubeGrid == null || controller.CubeGrid.EntityId != command.GridId ||
                        controller.EntityId != command.ControllerId || command.OperationId != operationId ||
                        !IsDirectActuatorSessionValid(controller, nowUtc) ||
                        !IsCurrentDirectCapabilityEcho(DateTime.UtcNow, capabilityToken) ||
                        !DirectManeuverControllerStateReady(controller, out _))
                        return false;
                    var wheelList = directCachedActuators;
                    if (wheelList == null || wheelList.Count == 0) return false;
                    if (!controlledStop && directBrakeActive)
                    {
                        if (!RecordDirectBrakeWrite(command.CommandId, DirectManeuverActuatorChannel.HandBrake, false, capabilityToken) ||
                            !RecordDirectBrakeWrite(command.CommandId, DirectManeuverActuatorChannel.GridBrake, false, capabilityToken))
                            return false;
                        directBrakeActive = false;
                    }
                    foreach (var wheel in wheelList)
                    {
                        if (wheel == null || wheel.Closed) return false;
                        if (!directManeuverBroker.IsMovementCapabilityCurrent("vragecage-server-plugin",
                                directManeuverIdentity, capabilityToken, DateTime.UtcNow) ||
                            !IsCurrentDirectCapabilityEcho(DateTime.UtcNow, capabilityToken) ||
                            !DirectManeuverControllerStateReady(controller, out _)) return false;
                        var steeringBefore = (double)wheel.SteeringOverride;
                        var propulsionBefore = (double)wheel.PropulsionOverride;
                        var steering = controlledStop ? 0.0 : command.Steering;
                        var revolveInvert = false;
                        if (!controlledStop && wheel.Propulsion &&
                            !DirectManeuverWheelRevolveInvert(controller, wheel, out revolveInvert))
                            return false;
                        if (controlledStop || !wheel.Propulsion) revolveInvert = false;
                        var propulsion = controlledStop || !wheel.Propulsion ? 0.0 :
                            WheelPropulsionMapping.ResolveDirectOverride(command.Propulsion,
                                revolveInvert, wheel.InvertPropulsion);
                        if (zeroActuationOnly &&
                            !directManeuverPlan.AllowsActuatorValues(steering, propulsion)) return false;
                        propulsionByWheel[wheel.EntityId] = propulsion;
                        if (!directManeuverBroker.IsMovementCapabilityCurrent("vragecage-server-plugin",
                                directManeuverIdentity, capabilityToken, DateTime.UtcNow) ||
                            !IsCurrentDirectCapabilityEcho(DateTime.UtcNow, capabilityToken) ||
                            !DirectManeuverControllerStateReady(controller, out _)) return false;
                        wheel.PropulsionOverride = (float)propulsion;
                        var propulsionAfter = (double)wheel.PropulsionOverride;
                        var propulsionWrite = directManeuverExecutor.RecordAcceptedChannelWrite(command.CommandId,
                            wheel.EntityId, DirectManeuverActuatorChannel.PropulsionOverride,
                            propulsionBefore, propulsion, propulsionAfter, DateTime.UtcNow, propulsionByWheel);
                        LogDirectManeuverChannelWrite(command.CommandId, wheel.EntityId,
                            "propulsion_override", propulsionBefore, propulsion, propulsionAfter, propulsionWrite);
                        if (propulsionWrite != DirectManeuverLeaseWriteResult.Accepted)
                            return false;
                        if (!directManeuverBroker.IsMovementCapabilityCurrent("vragecage-server-plugin",
                                directManeuverIdentity, capabilityToken, DateTime.UtcNow) ||
                            !IsCurrentDirectCapabilityEcho(DateTime.UtcNow, capabilityToken) ||
                            !DirectManeuverControllerStateReady(controller, out _)) return false;
                        wheel.SteeringOverride = (float)steering;
                        var steeringAfter = (double)wheel.SteeringOverride;
                        var steeringWrite = directManeuverExecutor.RecordAcceptedChannelWrite(command.CommandId,
                            wheel.EntityId, DirectManeuverActuatorChannel.SteeringOverride,
                            steeringBefore, steering, steeringAfter, DateTime.UtcNow);
                        LogDirectManeuverChannelWrite(command.CommandId, wheel.EntityId,
                            "steering_override", steeringBefore, steering, steeringAfter, steeringWrite);
                        if (steeringWrite != DirectManeuverLeaseWriteResult.Accepted)
                            return false;
                        if (Math.Abs(propulsionAfter - propulsion) > DirectManeuverOperationLease.NeutralTolerance ||
                            Math.Abs(steeringAfter - steering) > DirectManeuverOperationLease.NeutralTolerance)
                            return false;
                    }
                    if (controlledStop)
                    {
                        if (!RecordDirectBrakeWrite(command.CommandId, DirectManeuverActuatorChannel.GridBrake, true, capabilityToken) ||
                            !RecordDirectBrakeWrite(command.CommandId, DirectManeuverActuatorChannel.HandBrake, true, capabilityToken))
                            return false;
                        directBrakeActive = true;
                    }
                    return true;
                });
            var acknowledged = directManeuverExecutor.AcknowledgeCommand(new DirectManeuverCommandAcceptance(
                directManeuverIdentity, command.CommandId, accepted, DateTime.UtcNow,
                propulsionByWheel));
            if (!accepted || !acknowledged)
            {
                MyLog.Default.WriteLineAndConsole("Voidwright evidence: type=direct-maneuver operation=" +
                    operationId + " status=actuator-conflict commandId=" + command.CommandId +
                    " accepted=false actuatorDispatch=partial_or_rejected externalCAS=unavailable");
                EmergencyStopDirectManeuver("participating broker write or complete readback failed");
            }
        }

        private bool EnsureDirectActuatorSession(MyShipController target, DateTime nowUtc, out string reason)
        {
            reason = "unknown";
            if (directCachedActuators != null)
            {
                var valid = IsDirectActuatorSessionValid(target, nowUtc);
                reason = valid ? "cached exact-grid wheel references remain valid" : "cached wheel reference, identity, or expiry changed";
                return valid;
            }
            if (target == null || target.MarkedForClose || target.EntityId != controllerId ||
                target.CubeGrid == null || target.CubeGrid.EntityId != expectedGridId || target.GridWheels == null ||
                target.GridWheels.Wheels == null || target.GridWheels.Wheels.Count == 0)
            { reason = "exact enrolled grid or wheel inventory is unavailable"; return false; }
            var captured = new List<Sandbox.Game.Entities.Cube.MyMotorSuspension>(target.GridWheels.Wheels.Count);
            var identities = new HashSet<long>();
            foreach (var wheel in target.GridWheels.Wheels)
            {
                if (wheel == null || wheel.Closed || wheel.CubeGrid == null ||
                    wheel.CubeGrid.EntityId != expectedGridId || !identities.Add(wheel.EntityId))
                { reason = "wheel is closed, duplicated, or outside the exact enrolled grid"; return false; }
                captured.Add(wheel);
            }
            if (captured.Count != target.GridWheels.WheelCount)
            { reason = "wheel inventory count changed during enrollment"; return false; }
            directCachedActuators = captured;
            directCachedActuatorGridId = expectedGridId;
            directCachedActuatorControllerId = controllerId;
            directCachedActuatorOperationId = operationId;
            directCachedActuatorExpiresUtc = nowUtc.ToUniversalTime() + DirectManeuverPlan.MaximumWallClockDuration;
            reason = "exact-grid wheel references cached";
            return true;
        }

        private bool IsDirectActuatorSessionValid(MyShipController target, DateTime nowUtc)
        {
            if (target == null || directCachedActuators == null || target.MarkedForClose ||
                !DirectManeuverActuatorSessionBindingPolicy.IsValid(nowUtc, directCachedActuatorExpiresUtc,
                    expectedGridId, controllerId, operationId, directCachedActuatorGridId,
                    directCachedActuatorControllerId, directCachedActuatorOperationId,
                    directCachedActuators.Count, target.GridWheels?.Wheels?.Count ?? -1,
                    target.GridWheels?.WheelCount ?? -1) ||
                target.EntityId != directCachedActuatorControllerId || target.CubeGrid == null ||
                target.CubeGrid.EntityId != directCachedActuatorGridId || target.GridWheels == null ||
                target.GridWheels.Wheels == null)
                return false;
            foreach (var wheel in directCachedActuators)
                if (wheel == null || wheel.Closed || wheel.CubeGrid == null ||
                    wheel.CubeGrid.EntityId != directCachedActuatorGridId) return false;
            return true;
        }

        private bool RecordDirectBrakeWrite(string commandId, DirectManeuverActuatorChannel channel, bool requested,
            string capabilityToken)
        {
            if (controller == null || controller.GridWheels == null ||
                !directManeuverBroker.IsMovementCapabilityCurrent("vragecage-server-plugin",
                    directManeuverIdentity, capabilityToken, DateTime.UtcNow) ||
                !IsCurrentDirectCapabilityEcho(DateTime.UtcNow, capabilityToken) ||
                !DirectManeuverControllerStateReady(controller, out _)) return false;
            var before = channel == DirectManeuverActuatorChannel.GridBrake
                ? controller.GridWheels.Brake : controller.GridWheels.HandBrake;
            if (channel == DirectManeuverActuatorChannel.GridBrake) controller.GridWheels.Brake = requested;
            else controller.GridWheels.HandBrake = requested;
            var after = channel == DirectManeuverActuatorChannel.GridBrake
                ? controller.GridWheels.Brake : controller.GridWheels.HandBrake;
            var value = requested ? 1.0 : 0.0;
            var result = directManeuverExecutor.RecordAcceptedChannelWrite(commandId, 0, channel,
                before ? 1 : 0, value, after ? 1 : 0, DateTime.UtcNow);
            LogDirectManeuverChannelWrite(commandId, 0,
                channel == DirectManeuverActuatorChannel.GridBrake ? "grid_brake" : "hand_brake",
                before ? 1 : 0, value, after ? 1 : 0, result);
            return result == DirectManeuverLeaseWriteResult.Accepted && after == requested;
        }

        private void LogDirectManeuverChannelWrite(string commandId, long wheelId, string channel,
            double before, double requested, double after, DirectManeuverLeaseWriteResult result)
        {
            MyLog.Default.WriteLineAndConsole("Voidwright evidence: type=direct-maneuver-channel operation=" +
                operationId + " commandId=" + commandId + " wheelId=" + wheelId + " channel=" + channel +
                " before=" + F(before) + " requested=" + F(requested) + " after=" + F(after) +
                " accepted=" + (result == DirectManeuverLeaseWriteResult.Accepted) +
                " result=" + result + " externalCAS=unavailable");
        }

        private void LogDirectManeuverSample(DirectManeuverExecutionSample sample, DirectManeuverStep step)
        {
            var position = new Vector3D(sample.PositionX, sample.PositionY, sample.PositionZ);
            var displacement = directManeuverStartPositionReady
                ? Vector3D.Distance(position, directManeuverStartPosition) : 0;
            var hostNowUtc = DateTime.UtcNow;
            var hostTelemetryState = sample.CapturedUtc > hostNowUtc ? "clock-skew" :
                hostNowUtc - sample.CapturedUtc > DirectManeuverPlan.EvidenceMaximumAge ? "stale" :
                !Finite(sample.PositionX) || !Finite(sample.PositionY) || !Finite(sample.PositionZ) ||
                !Finite(sample.SpeedMetersPerSecond) ? "invalid" : "fresh";
            MyLog.Default.WriteLineAndConsole("Voidwright evidence: type=direct-maneuver operation=" +
                operationId + " runId=" + sample.RunId + " fixtureId=" + sample.FixtureId +
                " controllerId=" + sample.ControllerId + " gridId=" + sample.GridId +
                " hostSampleSequence=" + sample.HostSampleSequence +
                " hostCapturedUtc=" + sample.CapturedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) +
                " hostTelemetry=" + hostTelemetryState +
                " modSimulationTick=" + sample.ModSimulationTick +
                " modHeartbeatFresh=" + modHeartbeatFresh +
                " modSessionLiveness=" + (modSessionLivenessFresh && modSessionLiveness != null
                    ? modSessionLiveness.PreflightState : "stale-or-unavailable") +
                " modSessionTick=" + (modSessionLivenessFresh && modSessionLiveness != null
                    ? modSessionLiveness.SimulationTick : -1) +
                " modSessionLivenessFailure=" + (modSessionLivenessFresh ? "none" : modSessionLivenessFailure.Replace(' ', '_')) +
                " modTerminalReason=" + (modSessionLivenessFresh && modSessionLiveness != null &&
                    !string.IsNullOrEmpty(modSessionLiveness.TerminalReason)
                    ? modSessionLiveness.TerminalReason.Replace(' ', '_') : "none") +
                " segment=" + (step == null ? -1 : step.SegmentIndex) +
                " status=" + (directManeuverExecutor == null ? "preflight" : directManeuverExecutor.Outcome.ToString().ToLowerInvariant()) +
                " position=" + V(position) + " excursionMeters=" + F(displacement) +
                " forward=" + V(new Vector3D(sample.ForwardX, sample.ForwardY, sample.ForwardZ)) +
                " headingDegrees=" + F(sample.HeadingDegrees) + " steeringAngleDegrees=" +
                (sample.ObservedSteeringAngleDegrees.HasValue ? F(sample.ObservedSteeringAngleDegrees.Value) : "unknown") +
                " speedMps=" + F(sample.SpeedMetersPerSecond) +
                " longitudinalSpeedMps=" + F(sample.LongitudinalSpeedMetersPerSecond) +
                " propulsionPolarity=" + F(sample.PropulsionPolarity) +
                " minimumPropulsionWheelPower=" + F(sample.MinimumPropulsionWheelPower) +
                " obstacleClear=" + sample.ObstacleClear +
                " minimumClearanceMeters=" + F(sample.MinimumClearanceMeters) +
                " brake=" + (sample.ControllerBrakeState != null && sample.ControllerBrakeState.GridBrakeActive) +
                " handBrake=" + (sample.ControllerBrakeState != null && sample.ControllerBrakeState.HandBrakeActive) +
                " wheelCount=" + (sample.ActuatorStates == null ? 0 : sample.ActuatorStates.Count) +
                " movementObserved=" + (step != null && step.MovementObserved) +
                " commandAccepted=" + (step == null || !step.LastCommandAccepted.HasValue ? "pending" : step.LastCommandAccepted.Value.ToString()) +
                " commandKind=" + (step == null || step.Command == null ? "none" : step.Command.Kind.ToString()) +
                " commandSteering=" + (step == null || step.Command == null ? "none" : F(step.Command.Steering)) +
                " commandPropulsion=" + (step == null || step.Command == null ? "none" : F(step.Command.Propulsion)) +
                " wheelDriveStates=" + DirectManeuverWheelDriveStateSummary(sample.ActuatorStates) +
                " stepReason=" + (step == null ? "none" : step.Reason.Replace(' ', '_')) +
                " writerAudit=" + directWriterAuditEvidence + " externalCAS=unavailable");
        }

        private static string DirectManeuverWheelDriveStateSummary(
            IList<DirectManeuverWheelState> states)
        {
            if (states == null) return "unknown";
            var result = new StringBuilder();
            foreach (var state in states)
            {
                if (state == null) continue;
                if (result.Length > 0) result.Append(';');
                result.Append(state.WheelEntityId)
                    .Append(":polarityKnown=").Append(state.DriveStateKnown)
                    .Append(":revolveInvert=").Append(state.RevolveInvert)
                    .Append(":invertProp=").Append(state.InvertPropulsion)
                    .Append(":invertSteer=").Append(state.InvertSteer)
                    .Append(":power=").Append(F(state.Power))
                    .Append(":brakingEnabled=").Append(state.BrakingEnabled)
                    .Append(":brake=").Append(state.WheelBrakeActive)
                    .Append(":handBrake=").Append(state.WheelHandBrakeActive)
                    .Append(":shouldBrake=").Append(state.ShouldBrake)
                    .Append(":driveOverride=").Append(F(state.PropulsionOverride));
            }
            return result.ToString();
        }

        private void LogPreflightWheelPowerSnapshot()
        {
            MyEntity entity;
            entity = null;
            var found = controllerId > 0 && MyEntities.TryGetEntityById(controllerId, out entity);
            var observedController = found ? entity as MyShipController : null;
            if (observedController == null || observedController.CubeGrid == null ||
                observedController.CubeGrid.EntityId != expectedGridId || observedController.GridWheels == null ||
                observedController.GridWheels.Wheels == null)
            {
                MyLog.Default.WriteLineAndConsole("Voidwright evidence: type=preflight-wheel-power operation=" +
                    operationId + " status=unknown reason=exact_controller_grid_or_wheels_unavailable");
                return;
            }
            var values = new StringBuilder();
            var propulsionCount = 0;
            var minimumPropulsionPower = double.MaxValue;
            foreach (var wheel in observedController.GridWheels.Wheels)
            {
                if (wheel == null) continue;
                if (values.Length > 0) values.Append(';');
                values.Append(wheel.EntityId).Append(":prop=").Append(wheel.Propulsion)
                    .Append(":power=").Append(F(wheel.Power))
                    .Append(":speedLimitKmh=").Append(F(wheel.SpeedLimit));
                if (wheel.Propulsion)
                {
                    propulsionCount++;
                    minimumPropulsionPower = Math.Min(minimumPropulsionPower, wheel.Power);
                }
            }
            MyLog.Default.WriteLineAndConsole("Voidwright evidence: type=preflight-wheel-power operation=" +
                operationId + " controllerId=" + controllerId + " gridId=" + expectedGridId +
                " propulsionWheelCount=" + propulsionCount + " minimumPropulsionWheelPower=" +
                (minimumPropulsionPower == double.MaxValue ? "unknown" : F(minimumPropulsionPower)) +
                " wheels=" + values.ToString() + " actuatorDispatch=none");
        }

        private void RequestDirectEmergencyStop(string reason)
        {
            RevokeDirectCapabilityArtifact();
            if (directManeuverExecutor == null || directManeuverIdentity == null)
            {
                EmergencyStopDirectManeuver(reason);
                return;
            }
            var requestId = operationId + "-stop-" + (++directStopRequestSequence).ToString(CultureInfo.InvariantCulture);
            var request = new DirectManeuverStopRequest(directManeuverIdentity, requestId,
                DirectManeuverStopKind.EmergencyStop, DateTime.UtcNow);
            var accepted = directManeuverExecutor.RequestEmergencyStop(request, DateTime.UtcNow);
            if (!accepted.Accepted)
            {
                EmergencyStopDirectManeuver(reason + "; executor rejected emergency-stop request");
                return;
            }
            if (directManeuverBroker != null && !directManeuverBroker.RequestStop(request,
                    out directManeuverStopCapability))
            {
                EmergencyStopDirectManeuver(reason + "; broker rejected stop request identity");
                return;
            }
            directStopRequestId = requestId;
            RevokeModApproval();
            MyLog.Default.WriteLineAndConsole("Voidwright evidence: type=direct-maneuver-stop operation=" +
                operationId + " requestId=" + requestId + " accepted=true reason=" +
                reason.Replace(' ', '_') + " externalCAS=unavailable");
            // Do not wait for another fresh mod heartbeat or executor dispatch
            // cycle before removing propulsion authority and requesting brakes.
            EmergencyStopDirectManeuver(reason);
        }

        private void CompleteDirectManeuverStop(DirectManeuverExecutionSample sample,
            DirectManeuverOutcome outcome, DateTime nowUtc)
        {
            var terminalReceipt = directManeuverExecutor == null ? null :
                directManeuverExecutor.Step(null, sample, nowUtc).TerminalReceipt;
            // A completed executor exposes its terminal receipt on the completing step;
            // reconstruct the request binding if the outcome is the normal final hold.
            var requestId = terminalReceipt == null ? operationId : terminalReceipt.RequestId;
            directStopRequestId = requestId;
            var request = new DirectManeuverStopRequest(directManeuverIdentity, requestId,
                outcome == DirectManeuverOutcome.Cancelled ? DirectManeuverStopKind.Cancellation :
                    DirectManeuverStopKind.EmergencyStop, nowUtc);
            var receipt = directManeuverBroker == null
                ? null
                : directManeuverBroker.CompleteStop(request,
                    outcome == DirectManeuverOutcome.TimedOut &&
                        !(terminalReceipt != null && terminalReceipt.StationaryConfirmed),
                    terminalReceipt != null && terminalReceipt.StationaryConfirmed,
                    sample.ActuatorStates, sample.ControllerBrakeState, nowUtc);
            var succeeded = receipt != null && receipt.Succeeded;
            MyLog.Default.WriteLineAndConsole("Voidwright evidence: type=direct-maneuver-terminal operation=" +
                operationId + " runId=" + directManeuverPlan.RunId + " fixtureId=" + directManeuverPlan.FixtureId +
                " gridId=" + directManeuverPlan.GridId + " controllerId=" + directManeuverPlan.ControllerId +
                " requestId=" + requestId + " outcome=" + outcome.ToString().ToLowerInvariant() +
                " stationaryConfirmed=" + (terminalReceipt != null && terminalReceipt.StationaryConfirmed) +
                " stationarySamples=" + (terminalReceipt == null ? 0 : terminalReceipt.StationarySamples) +
                " neutralizationConfirmed=" + (terminalReceipt != null && terminalReceipt.NeutralizationConfirmed) +
                " serverTerminationRequired=" + (terminalReceipt == null || terminalReceipt.ServerTerminationRequired) +
                " finalHostSampleSequence=" + sample.HostSampleSequence + " modSimulationTick=" + sample.ModSimulationTick + " wheelChannelsNeutral=" +
                DirectManeuverChannelsNeutral(sample.ActuatorStates) + " brake=" +
                sample.ControllerBrakeState.GridBrakeActive + " handBrake=" + sample.ControllerBrakeState.HandBrakeActive +
                " receipt=" + (succeeded ? "verified" : "failed") +
                " receiptReason=" + (receipt == null ? "broker-unavailable" : receipt.Reason.Replace(' ', '_')) +
                " externalCAS=unavailable");
            if (succeeded)
            {
                terminal = true;
                directManeuverStopUnresolved = false;
                RevokeDirectCapabilityArtifact();
                RevokeModApproval();
            }
            else
            {
                directManeuverStopUnresolved = true;
                directEmergencyStopReason = directEmergencyStopReason ?? "broker stop receipt unresolved";
                EmergencyStopDirectManeuver("broker stop receipt unresolved: " +
                    (receipt == null ? "broker-unavailable" : receipt.Reason));
                RevokeModApproval();
            }
        }

        private static bool DirectManeuverChannelsNeutral(IList<DirectManeuverWheelState> states)
        {
            if (states == null || states.Count == 0) return false;
            foreach (var state in states)
                if (state == null || Math.Abs(state.SteeringOverride) > DirectManeuverOperationLease.NeutralTolerance ||
                    Math.Abs(state.PropulsionOverride) > DirectManeuverOperationLease.NeutralTolerance) return false;
            return true;
        }

        private static bool DirectManeuverNeutralizationConfirmed(IList<DirectManeuverWheelState> states,
            DirectManeuverBrakeState brakes)
        {
            return DirectManeuverChannelsNeutral(states) && brakes != null && brakes.GridBrakeKnown &&
                brakes.HandBrakeKnown && brakes.GridBrakeActive && brakes.HandBrakeActive;
        }

        private void EmergencyStopDirectManeuver(string reason)
        {
            directManeuverStopUnresolved = true;
            RevokeDirectCapabilityArtifact();
            RevokeModApproval();
            if (directManeuverBroker != null && directManeuverIdentity != null &&
                !directManeuverBroker.StopPending)
            {
                directStopRequestId = operationId + "-stop-" +
                    (++directStopRequestSequence).ToString(CultureInfo.InvariantCulture);
                if (!directManeuverBroker.RequestStop(new DirectManeuverStopRequest(
                    directManeuverIdentity, directStopRequestId,
                    DirectManeuverStopKind.EmergencyStop, DateTime.UtcNow),
                    out directManeuverStopCapability))
                    directManeuverStopCapability = null;
            }
            var writeSucceeded = false;
            try
            {
                MyEntity entity;
                if (controller == null || controller.MarkedForClose ||
                    !MyEntities.TryGetEntityById(controllerId, out entity) ||
                    (controller = entity as MyShipController) == null ||
                    controller.CubeGrid == null || controller.CubeGrid.EntityId != expectedGridId ||
                    controller.GridWheels == null)
                    throw new InvalidOperationException("exact controller/grid is unavailable");
                var allNeutral = true;
                var wheelList = controller.GridWheels.Wheels;
                if (wheelList == null || wheelList.Count == 0)
                    throw new InvalidOperationException("exact enrolled grid has no cached wheel actuators");
                foreach (var wheel in wheelList)
                {
                    if (wheel == null || wheel.Closed) { allNeutral = false; continue; }
                    // Emergency neutralization is bound to the exact resolved grid and
                    // must remain available if normal lease/capability admission failed.
                    wheel.PropulsionOverride = 0;
                    wheel.SteeringOverride = 0;
                    allNeutral &= Math.Abs(wheel.PropulsionOverride) <= DirectManeuverOperationLease.NeutralTolerance &&
                        Math.Abs(wheel.SteeringOverride) <= DirectManeuverOperationLease.NeutralTolerance;
                }
                controller.GridWheels.Brake = true;
                controller.GridWheels.HandBrake = true;
                directBrakeActive = controller.GridWheels.Brake && controller.GridWheels.HandBrake;
                writeSucceeded = allNeutral && directBrakeActive;
            }
            catch { writeSucceeded = false; }
            if (writeSucceeded)
            {
                if (!directEmergencyStopWriteAccepted)
                {
                    directEmergencyStopStartedUtc = DateTime.UtcNow;
                    directEmergencyStopTimeoutLogged = false;
                    directEmergencyStopCompletionFailureLogged = false;
                }
                directEmergencyStopPending = true;
                directEmergencyStopWriteAccepted = true;
                directEmergencyStopReason = reason ?? "emergency stop";
                directEmergencyStopSamples = 0;
                directEmergencyStopLastSampleUtc = DateTime.MinValue;
                directEmergencyStopPositionReady = false;
                directEmergencyStopWindowDisplacement = 0;
                MyLog.Default.WriteLineAndConsole("Voidwright evidence: type=direct-maneuver-stop operation=" +
                    operationId + " accepted=true actuatorDispatch=zero_overrides_and_brakes reason=" +
                    (reason ?? "emergency stop").Replace(' ', '_') + " externalCAS=unavailable");
            }
            else
            {
                directEmergencyStopPending = false;
                directEmergencyStopWriteAccepted = false;
                directEmergencyStopReason = reason ?? "emergency stop";
                MyLog.Default.WriteLineAndConsole("Voidwright evidence: type=direct-maneuver-stop operation=" +
                    operationId + " accepted=false stopWrite=failed unresolved=true reason=" +
                    (reason ?? "emergency stop").Replace(' ', '_'));
            }
        }

        private void UpdateDirectManeuverStopResolution()
        {
            MyEntity entity;
            if (controller == null || controller.MarkedForClose ||
                !MyEntities.TryGetEntityById(controllerId, out entity) ||
                (controller = entity as MyShipController) == null || controller.CubeGrid == null ||
                controller.CubeGrid.EntityId != expectedGridId || controller.CubeGrid.Physics == null ||
                controller.GridWheels == null)
                return;

            var sample = BuildDirectManeuverSample(DateTime.UtcNow);
            if (!directEmergencyStopWriteAccepted)
                EmergencyStopDirectManeuver(directEmergencyStopReason ?? "retry unresolved emergency stop");
            if (!directEmergencyStopWriteAccepted)
            {
                if (sample.HostSampleSequence % 10 == 0)
                    MyLog.Default.WriteLineAndConsole("Voidwright evidence: type=direct-maneuver-stop-sample operation=" +
                        operationId + " hostSampleSequence=" + sample.HostSampleSequence +
                        " hostCapturedUtc=" + sample.CapturedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) +
                        " speedMps=" + F(sample.SpeedMetersPerSecond) +
                        " stopWriteAccepted=false unresolved=true serverTerminationRequired=true");
                return;
            }
            if (sample.HostSampleSequence % 10 == 0)
                MyLog.Default.WriteLineAndConsole("Voidwright evidence: type=direct-maneuver-stop-sample operation=" +
                    operationId + " hostSampleSequence=" + sample.HostSampleSequence +
                    " hostCapturedUtc=" + sample.CapturedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) +
                    " speedMps=" + F(sample.SpeedMetersPerSecond) +
                    " wheelChannelsNeutral=" + DirectManeuverChannelsNeutral(sample.ActuatorStates) +
                    " brakesActive=" + (sample.ControllerBrakeState != null &&
                        sample.ControllerBrakeState.GridBrakeActive && sample.ControllerBrakeState.HandBrakeActive) +
                    " modHeartbeatFresh=" + modHeartbeatFresh + " unresolved=true");
            MonitorDirectEmergencyStop(sample, sample.CapturedUtc);
        }

        private void MonitorDirectEmergencyStop(DirectManeuverExecutionSample sample, DateTime nowUtc)
        {
            if (sample.CapturedUtc > directEmergencyStopLastSampleUtc)
            {
                var position = new Vector3D(sample.PositionX, sample.PositionY, sample.PositionZ);
                var displacement = directEmergencyStopPositionReady
                    ? Vector3D.Distance(position, directEmergencyStopLastPosition) : double.MaxValue;
                if (Math.Abs(sample.SpeedMetersPerSecond) <= DirectManeuverLifecycle.StationaryConfirmationSpeedMetersPerSecond &&
                    displacement <= DirectManeuverLifecycle.StationaryDisplacementMeters)
                {
                    directEmergencyStopWindowDisplacement += displacement;
                    if (directEmergencyStopWindowDisplacement <= DirectManeuverLifecycle.StationaryConfirmationWindowDisplacementMeters)
                        directEmergencyStopSamples++;
                    else
                    {
                        directEmergencyStopSamples = 0;
                        directEmergencyStopWindowDisplacement = 0;
                    }
                }
                else
                {
                    directEmergencyStopSamples = 0;
                    directEmergencyStopWindowDisplacement = 0;
                }
                directEmergencyStopPositionReady = true;
                directEmergencyStopLastPosition = position;
                directEmergencyStopLastSampleUtc = sample.CapturedUtc;
            }
            if (directEmergencyStopSamples >= DirectManeuverLifecycle.RequiredStationarySamples)
            {
                var requestId = directStopRequestId ?? operationId + "-fallback-stop";
                var stopRequest = new DirectManeuverStopRequest(directManeuverIdentity,
                    requestId, DirectManeuverStopKind.EmergencyStop, nowUtc);
                var stopReceipt = directManeuverBroker == null ? null :
                    directManeuverBroker.CompleteStop(stopRequest, false, true,
                        sample.ActuatorStates, sample.ControllerBrakeState, nowUtc);
                var leaseResolved = directManeuverBroker == null ||
                    (stopReceipt != null && stopReceipt.Succeeded);
                if (leaseResolved || !directEmergencyStopCompletionFailureLogged)
                    MyLog.Default.WriteLineAndConsole("Voidwright evidence: type=direct-maneuver-terminal operation=" +
                    operationId + " runId=" + (Environment.GetEnvironmentVariable("VRAGECAGE_LAB_RUN_ID") ?? string.Empty) +
                    " fixtureId=" + (Environment.GetEnvironmentVariable("VRAGECAGE_LAB_FIXTURE_ID") ?? string.Empty) +
                    " gridId=" + expectedGridId + " controllerId=" + controllerId +
                    " requestId=" + operationId + "-fallback-stop outcome=emergency-stopped" +
                    " stationaryConfirmed=true stationarySamples=" + directEmergencyStopSamples +
                    " finalHostSampleSequence=" + sample.HostSampleSequence + " modSimulationTick=" + sample.ModSimulationTick + " wheelChannelsNeutral=" +
                    DirectManeuverChannelsNeutral(sample.ActuatorStates) + " brake=" +
                    sample.ControllerBrakeState.GridBrakeActive + " handBrake=" +
                    sample.ControllerBrakeState.HandBrakeActive + " neutralizationConfirmed=" +
                    DirectManeuverNeutralizationConfirmed(sample.ActuatorStates, sample.ControllerBrakeState) +
                    " serverTerminationRequired=" + (!leaseResolved) + " receipt=" +
                    (leaseResolved ? "verified" : "failed") + " fallback=true reason=" +
                    directEmergencyStopReason.Replace(' ', '_') + " externalCAS=unavailable");
                if (leaseResolved)
                {
                    directEmergencyStopPending = false;
                    directManeuverStopUnresolved = false;
                    terminal = true;
                    RevokeModApproval();
                }
                else directEmergencyStopCompletionFailureLogged = true;
                return;
            }
            if (!directEmergencyStopTimeoutLogged && nowUtc - directEmergencyStopStartedUtc >= TimeSpan.FromSeconds(2))
            {
                MyLog.Default.WriteLineAndConsole("Voidwright evidence: type=direct-maneuver-terminal operation=" +
                    operationId + " requestId=" + operationId + "-fallback-stop outcome=failed" +
                    " stationaryConfirmed=false stationarySamples=" + directEmergencyStopSamples +
                    " finalHostSampleSequence=" + sample.HostSampleSequence + " modSimulationTick=" + sample.ModSimulationTick + " wheelChannelsNeutral=" +
                    DirectManeuverChannelsNeutral(sample.ActuatorStates) + " neutralizationConfirmed=" +
                    DirectManeuverNeutralizationConfirmed(sample.ActuatorStates, sample.ControllerBrakeState) +
                    " receipt=unresolved fallback=true reason=" + directEmergencyStopReason.Replace(' ', '_') +
                    " serverTerminationRequired=true");
                directEmergencyStopTimeoutLogged = true;
            }
        }

        private void FinishDirectManeuver(string status, string reason, bool actuatorDispatch)
        {
            if (directManeuverExecutor != null)
            {
                EmergencyStopDirectManeuver(reason);
                return;
            }
            terminal = true;
            RevokeDirectCapabilityArtifact();
            RevokeModApproval();
            MyLog.Default.WriteLineAndConsole("Voidwright evidence: type=direct-maneuver-terminal operation=" +
                operationId + " runId=" + (Environment.GetEnvironmentVariable("VRAGECAGE_LAB_RUN_ID") ?? string.Empty) +
                " fixtureId=" + (Environment.GetEnvironmentVariable("VRAGECAGE_LAB_FIXTURE_ID") ?? string.Empty) +
                " gridId=" + expectedGridId + " controllerId=" + controllerId + " status=" + status +
                " elapsedTicks=" + modPreflightElapsedTicks + " timeoutTicks=" + timeoutTicks +
                " actuatorDispatch=" + (actuatorDispatch ? "attempted" : "none") +
                " reason=" + (reason ?? "unspecified").Replace(' ', '_'));
        }

        // Passive exact-fixture baseline. Keep this path structurally separate from the
        // authorization/mailbox/actuator command loop below: it observes state
        // and emits evidence, but never writes wheel or brake properties.
        private void UpdateLocalVehicleTestReadOnlyBaseline()
        {
            localVehicleTestUpdateTicks++;
            var runId = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_RUN_ID") ?? string.Empty;
            var fixtureId = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_FIXTURE_ID") ?? string.Empty;
            var gridId = LocalVehicleTestExpectedId("VRAGECAGE_LOCAL_VEHICLE_TEST_GRID_ID");
            var controllerId = LocalVehicleTestExpectedId("VRAGECAGE_LOCAL_VEHICLE_TEST_CONTROLLER_ID");
            var cameraId = LocalVehicleTestExpectedId("VRAGECAGE_LOCAL_VEHICLE_TEST_CAMERA_ID");
            if (!string.Equals(LocalVehicleTestInstance, Voidwright.Core.LocalVehicleTestBindingPolicy.FreshInstance,
                    StringComparison.Ordinal) ||
                gridId != Voidwright.Core.LocalVehicleTestBindingPolicy.GridId ||
                controllerId != Voidwright.Core.LocalVehicleTestBindingPolicy.ControllerId ||
                cameraId != Voidwright.Core.LocalVehicleTestBindingPolicy.CameraIdFor(LocalVehicleTestInstance) ||
                !IsHexIdentity(runId) || !IsHexIdentity(fixtureId) ||
                MySession.Static == null || MyAPIGateway.Physics == null)
            {
                LocalVehicleTestReadOnlyBaselineLog("blocked", "exact_binding_or_physics_unavailable", runId, fixtureId,
                    gridId, controllerId, cameraId, "unavailable", "unavailable", "unavailable");
                return;
            }

            MyEntity gridEntity;
            MyEntity controllerEntity;
            MyEntity cameraEntity;
            if (!MyEntities.TryGetEntityById(gridId, out gridEntity) || gridEntity == null || gridEntity.Closed ||
                !MyEntities.TryGetEntityById(controllerId, out controllerEntity) || controllerEntity == null || controllerEntity.Closed ||
                !MyEntities.TryGetEntityById(cameraId, out cameraEntity) || cameraEntity == null || cameraEntity.Closed)
            {
                localVehicleTestReadOnlyBaselineSamples = 0;
                LocalVehicleTestReadOnlyBaselineLog("blocked", "exact_entities_not_loaded", runId, fixtureId,
                    gridId, controllerId, cameraId, "unavailable", "unavailable", "unavailable");
                return;
            }

            var controller = controllerEntity as MyShipController;
            var camera = cameraEntity as IMyCameraBlock;
            if (controller == null || controller.CubeGrid == null || controller.CubeGrid.EntityId != gridId ||
                controller.GridWheels == null || controller.GridWheels.Wheels == null || camera == null ||
                camera.CubeGrid == null || camera.CubeGrid.EntityId != gridId)
            {
                localVehicleTestReadOnlyBaselineSamples = 0;
                LocalVehicleTestReadOnlyBaselineLog("blocked", "controller_camera_or_grid_type_mismatch", runId, fixtureId,
                    gridId, controllerId, cameraId, "unavailable", "unavailable", "unavailable");
                return;
            }

            var wheels = new List<Sandbox.Game.Entities.Cube.MyMotorSuspension>();
            var wheelIds = new List<long>();
            var wheelGridIds = new List<long>();
            foreach (var expectedWheelId in Voidwright.Core.LocalVehicleTestBindingPolicy.WheelIds)
            {
                MyEntity wheelEntity;
                if (!MyEntities.TryGetEntityById(expectedWheelId, out wheelEntity) || wheelEntity == null || wheelEntity.Closed)
                {
                    localVehicleTestReadOnlyBaselineSamples = 0;
                    LocalVehicleTestReadOnlyBaselineLog("blocked", "exact_wheel_not_loaded", runId, fixtureId,
                        gridId, controllerId, cameraId, "unavailable", "unavailable", "unavailable");
                    return;
                }
                var wheel = wheelEntity as Sandbox.Game.Entities.Cube.MyMotorSuspension;
                if (wheel == null || wheel.CubeGrid == null || wheel.CubeGrid.EntityId != gridId ||
                    wheel.RotorGrid == null || wheel.RotorGrid.Physics == null)
                {
                    localVehicleTestReadOnlyBaselineSamples = 0;
                    LocalVehicleTestReadOnlyBaselineLog("blocked", "wheel_or_rotor_physics_mismatch", runId, fixtureId,
                        gridId, controllerId, cameraId, "unavailable", "unavailable", "unavailable");
                    return;
                }
                wheels.Add(wheel);
                wheelIds.Add(wheel.EntityId);
                wheelGridIds.Add(wheel.RotorGrid.EntityId);
            }

            if (!Voidwright.Core.LocalVehicleTestBindingPolicy.HasExactWheelMap(wheelIds, wheelGridIds) ||
                wheels.Count != controller.GridWheels.Wheels.Count)
            {
                localVehicleTestReadOnlyBaselineSamples = 0;
                LocalVehicleTestReadOnlyBaselineLog("blocked", "runtime_wheel_map_mismatch", runId, fixtureId,
                    gridId, controllerId, cameraId, "unavailable", "unavailable", "unavailable");
                return;
            }

            localVehicleTestController = controller;
            localVehicleTestWheels = wheels;
            localVehicleTestCameraId = cameraId;
            var writerAuditPassed = AuditLocalVehicleTestAssemblyWriters(controller, wheels, out var writerEvidence);
            var physics = controller.CubeGrid.Physics;
            if (physics == null)
            {
                localVehicleTestReadOnlyBaselineSamples = 0;
                LocalVehicleTestReadOnlyBaselineLog("blocked", "chassis_physics_unavailable", runId, fixtureId,
                    gridId, controllerId, cameraId, "unavailable", writerEvidence, "unavailable");
                return;
            }

            var position = new Vector3D(controller.CubeGrid.WorldMatrix.Translation);
            var linearSpeed = new Vector3D(physics.LinearVelocity).Length();
            var angularSpeed = new Vector3D(physics.AngularVelocity).Length();
            var groundContactVerified = LocalVehicleTestGroundSupport(wheels,
                new Vector3D(controller.GetTotalGravity()), out var groundEvidence);
            var actuatorReadback = LocalVehicleTestActuatorsNeutral(wheels);
            var sampleTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
            var distinct = Voidwright.Core.LocalVehicleTestBindingPolicy.IsDistinctStopSample(
                localVehicleTestReadOnlyLastSampleTimestamp, sampleTimestamp);
            var sampleDue = Voidwright.Core.LocalVehicleTestBindingPolicy.ShouldSample(localVehicleTestUpdateTicks);
            if (distinct && sampleDue)
            {
                localVehicleTestReadOnlyLastSampleTimestamp = sampleTimestamp;
                localVehicleTestReadOnlySampleSequence++;
            }
            var subgridEvidence = "unavailable";
            var stationary = distinct && sampleDue && actuatorReadback && Finite(linearSpeed) &&
                Finite(angularSpeed) && linearSpeed <= 0.02 && angularSpeed <= 0.02 &&
                groundContactVerified &&
                LocalVehicleTestSubgridsStationary(wheels, localVehicleTestBaselineRotorPositions,
                    true, false, out subgridEvidence);
            if (!localVehicleTestReadOnlyReferencePositionReady)
            {
                localVehicleTestReadOnlyReferencePosition = position;
                localVehicleTestReadOnlyReferencePositionReady = true;
            }
            var drift = Vector3D.Distance(localVehicleTestReadOnlyReferencePosition, position);
            stationary &= Finite(drift) && drift <= 0.05;
            if (distinct && sampleDue)
            {
                if (stationary)
                {
                    if (localVehicleTestReadOnlyBaselineStartTimestamp == 0)
                        localVehicleTestReadOnlyBaselineStartTimestamp = sampleTimestamp;
                    localVehicleTestReadOnlyBaselineSamples++;
                }
                else
                {
                    localVehicleTestReadOnlyBaselineSamples = 0;
                    localVehicleTestReadOnlyBaselineStartTimestamp = 0;
                    localVehicleTestReadOnlyReferencePosition = position;
                    localVehicleTestReadOnlyReferencePositionReady = true;
                }
            }
            var elapsed = localVehicleTestReadOnlyBaselineStartTimestamp == 0 ? 0 :
                (sampleTimestamp - localVehicleTestReadOnlyBaselineStartTimestamp) /
                (double)System.Diagnostics.Stopwatch.Frequency;

            var alignment = double.NaN;
            try
            {
                var cameraForward = Vector3D.Normalize(camera.WorldMatrix.Forward);
                var controllerForward = Vector3D.Normalize(controller.WorldMatrix.Forward);
                alignment = Vector3D.Dot(cameraForward, controllerForward);
            }
            catch { }
            var clearanceEvidence = "not_assessed_full_assembly";
            var controllerEvidence = "controllerBound=true;autopilot=not_applicable_to_ship_controller;forward=" +
                LocalVehicleTestVector(controller.WorldMatrix.Forward) +
                ";cameraForwardAlignment=" + alignment.ToString("R", CultureInfo.InvariantCulture);
            if (distinct && sampleDue)
            {
                var linearVelocity = new Vector3D(physics.LinearVelocity);
                var angularVelocity = new Vector3D(physics.AngularVelocity);
                var forwardVector = new Vector3D(controller.CubeGrid.WorldMatrix.Forward);
                var rightVector = new Vector3D(controller.CubeGrid.WorldMatrix.Right);
                var upVector = new Vector3D(controller.CubeGrid.WorldMatrix.Up);
                var forward = forwardVector.LengthSquared() > 1e-12 ? Vector3D.Normalize(forwardVector) : Vector3D.Zero;
                var right = rightVector.LengthSquared() > 1e-12 ? Vector3D.Normalize(rightVector) : Vector3D.Zero;
                var up = upVector.LengthSquared() > 1e-12 ? Vector3D.Normalize(upVector) : Vector3D.Zero;
                var gravity = new Vector3D(controller.GetTotalGravity());
                var gravityUp = gravity.LengthSquared() > 1e-12 ? Vector3D.Normalize(-gravity) : Vector3D.Zero;
                var utc = DateTime.UtcNow;
                MyLog.Default.WriteLineAndConsole("Voidwright local test sample: run=" + runId +
                    " fixture=" + fixtureId + " instance=" + LocalVehicleTestInstance +
                    " sequence=" + localVehicleTestReadOnlySampleSequence +
                    " pluginTick=" + localVehicleTestUpdateTicks +
                    " simulationTick=unavailable utc=" + utc.ToString("o", CultureInfo.InvariantCulture) +
                    " monotonicTicks=" + sampleTimestamp + " grid=" + gridId + " controller=" + controllerId +
                    " position=" + LocalVehicleTestVector(position) +
                    " orientationForward=" + LocalVehicleTestVector(controller.CubeGrid.WorldMatrix.Forward) +
                    " orientationUp=" + LocalVehicleTestVector(controller.CubeGrid.WorldMatrix.Up) +
                    " linearVelocity=" + LocalVehicleTestVector(linearVelocity) +
                    " angularVelocity=" + LocalVehicleTestVector(angularVelocity) +
                    " forwardVelocity=" + Vector3D.Dot(linearVelocity, forward).ToString("R", CultureInfo.InvariantCulture) +
                    " rightVelocity=" + Vector3D.Dot(linearVelocity, right).ToString("R", CultureInfo.InvariantCulture) +
                    " upVelocity=" + Vector3D.Dot(linearVelocity, up).ToString("R", CultureInfo.InvariantCulture) +
                    " gravity=" + LocalVehicleTestVector(gravity) +
                    " gravityUp=" + LocalVehicleTestVector(gravityUp) +
                    " actuatorReadbacks=" + LocalVehicleTestWheelSteeringEvidence().Replace(' ', '_') +
                    " controllerBrake=" + controller.GridWheels.Brake +
                    " controllerHandbrake=" + controller.GridWheels.HandBrake +
                    " writerAudit=" + writerEvidence.Replace(' ', '_') +
                    " groundContact=" + groundContactVerified + ":" + groundEvidence.Replace(' ', '_') +
                    " clearance=not_assessed_full_assembly");
            }
            var baselineComplete = localVehicleTestReadOnlyBaselineSamples >= 20 && elapsed >= 2.0 &&
                writerAuditPassed && actuatorReadback && groundContactVerified;
            var state = baselineComplete ? "baseline_observed" : "collecting";
            if (!baselineComplete) localVehicleTestReadOnlyBaselineReported = false;
            if (localVehicleTestUpdateTicks % 30 == 0 &&
                (!baselineComplete || !localVehicleTestReadOnlyBaselineReported))
            {
                LocalVehicleTestReadOnlyBaselineLog(state, "passive_observation_only", runId, fixtureId,
                    gridId, controllerId, cameraId, writerEvidence, controllerEvidence,
                    "samples=" + localVehicleTestReadOnlyBaselineSamples + ";linearSpeed=" +
                    linearSpeed.ToString("R", CultureInfo.InvariantCulture) + ";angularSpeed=" +
                    angularSpeed.ToString("R", CultureInfo.InvariantCulture) + ";elapsedSeconds=" +
                    elapsed.ToString("R", CultureInfo.InvariantCulture) + ";drift=" +
                    drift.ToString("R", CultureInfo.InvariantCulture) + ";actuatorsNeutral=" + actuatorReadback +
                    ";controllerBrake=" + controller.GridWheels.Brake + ";controllerHandbrake=" +
                    controller.GridWheels.HandBrake + ";wheelBrakeState=" +
                    DescribeWheelBrakeState(controller).Replace(' ', '_') +
                    ";subgrids=" + subgridEvidence.Replace(' ', '_') +
                    ";groundContact=" + groundContactVerified + ":" + groundEvidence.Replace(' ', '_') +
                    ";clearance=" + clearanceEvidence +
                    ";wheelReadbacks=" + LocalVehicleTestWheelSteeringEvidence().Replace(' ', '_'));
                if (baselineComplete) localVehicleTestReadOnlyBaselineReported = true;
            }
        }

        private void LocalVehicleTestReadOnlyBaselineLog(string state, string reason, string runId,
            string fixtureId, long gridId, long controllerId, long cameraId, string writerEvidence,
            string controllerEvidence, string observationEvidence)
        {
            if (localVehicleTestUpdateTicks % 30 != 0) return;
            MyLog.Default.WriteLineAndConsole("Voidwright local test read-only baseline: run=" + runId +
                " fixture=" + fixtureId + " instance=" + LocalVehicleTestInstance + " state=" + state +
                " reason=" + reason.Replace(' ', '_') + " grid=" + gridId + " controller=" + controllerId +
                " camera=" + cameraId + " authAccess=none mailboxAccess=none actuatorWrites=none" +
                " writerAudit=" + (writerEvidence ?? "unavailable").Replace(' ', '_') +
                " controllerState=" + (controllerEvidence ?? "unavailable").Replace(' ', '_') +
                " observations=" + (observationEvidence ?? "unavailable").Replace(' ', '_'));
        }

        private void UpdateLocalVehicleTest(DateTime nowUtc)
        {
            var runId = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_RUN_ID") ?? string.Empty;
            var fixtureId = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_FIXTURE_ID") ?? string.Empty;
            var expectedGridId = LocalVehicleTestExpectedId("VRAGECAGE_LOCAL_VEHICLE_TEST_GRID_ID");
            var expectedControllerId = LocalVehicleTestExpectedId("VRAGECAGE_LOCAL_VEHICLE_TEST_CONTROLLER_ID");
            var expectedCameraId = LocalVehicleTestExpectedId("VRAGECAGE_LOCAL_VEHICLE_TEST_CAMERA_ID");
            var boundedMode = !localVehicleTestStationarySteeringOnly;
            localVehicleTestUpdateTicks++;
            if ((!boundedMode && !localVehicleTestStationarySteeringOnly) || expectedGridId <= 0 ||
                expectedControllerId <= 0 || expectedCameraId <= 0 ||
                !LocalVehicleTestMailbox.TryReadAuthorization(out var authorization) ||
                !LocalVehicleTestControlPolicy.IsAuthorized(authorization, LocalVehicleTestInstance, runId,
                    fixtureId, expectedGridId, expectedControllerId, expectedCameraId, nowUtc))
            {
                LocalVehicleTestStop(nowUtc, "authorization_or_isolation_certificate_missing", "revoked");
                return;
            }
            if (localVehicleTestAuthorization == null ||
                localVehicleTestAuthorization.AuthorizationId != authorization.AuthorizationId ||
                localVehicleTestAuthorization.RunId != authorization.RunId ||
                localVehicleTestAuthorization.GridId != authorization.GridId ||
                localVehicleTestAuthorization.ControllerId != authorization.ControllerId ||
                localVehicleTestAuthorization.CameraId != authorization.CameraId)
            {
                LocalVehicleTestStop(nowUtc, "authorization_changed", "revoked");
                localVehicleTestAuthorization = authorization;
                localVehicleTestController = null;
                localVehicleTestWheels = null;
                localVehicleTestStopPathVerified = false;
                localVehicleTestSteeringDirectionConfirmed = false;
                localVehicleTestSteeringAngleRadians = 0;
                localVehicleTestActiveCommand = null;
                localVehicleTestRunStartPositionReady = false;
                localVehicleTestBaselineSamples = 0;
                localVehicleTestBaselineStartTimestamp = 0;
                localVehicleTestBaselinePassed = false;
                localVehicleTestBaselinePositionReady = false;
                localVehicleTestBaselineRotorPositions.Clear();
                localVehicleTestStopRotorPositions.Clear();
                localVehicleTestLastStopSampleTimestamp = 0;
                localVehicleTestCameraId = authorization.CameraId;
                localVehicleTestStopStartPositionReady = false;
                localVehicleTestLastStopSampleUtc = DateTime.MinValue;
                localVehicleTestLastTerminalCommandId = string.Empty;
                localVehicleTestLastStopConfirmationCommandId = string.Empty;
                localVehicleTestPreflightLogged = false;
                MyLog.Default.WriteLineAndConsole("Voidwright local test authorization accepted: run=" + runId +
                    " fixture=" + fixtureId + " grid=" + authorization.GridId +
                    " controller=" + authorization.ControllerId + " camera=" + authorization.CameraId +
                    " expiresUtc=" + authorization.ExpiresUtc.ToString("o", CultureInfo.InvariantCulture));
            }

            MyEntity entity;
            if (MySession.Static == null ||
                !MyEntities.TryGetEntityById(authorization.ControllerId, out entity) ||
                (localVehicleTestController = entity as MyShipController) == null ||
                localVehicleTestController.MarkedForClose || localVehicleTestController.CubeGrid == null ||
                localVehicleTestController.CubeGrid.EntityId != authorization.GridId ||
                localVehicleTestController.CubeGrid.Physics == null || localVehicleTestController.GridWheels == null ||
                localVehicleTestController.GridWheels.Wheels == null || localVehicleTestController.GridWheels.Wheels.Count == 0)
            {
                LocalVehicleTestStop(nowUtc, "exact_controller_or_physics_unavailable", "blocked");
                return;
            }

            var observedWheels = new List<Sandbox.Game.Entities.Cube.MyMotorSuspension>();
            var wheelIds = new List<long>();
            var wheelGridIds = new List<long>();
            var wheelSeen = new HashSet<long>();
            var steeringWheelCount = 0;
            var inventoryComplete = true;
            foreach (var wheel in localVehicleTestController.GridWheels.Wheels)
            {
                if (wheel == null || wheel.Closed || wheel.CubeGrid == null ||
                    wheel.CubeGrid.EntityId != authorization.GridId || !wheelSeen.Add(wheel.EntityId))
                {
                    inventoryComplete = false;
                    break;
                }
                var rotorGrid = wheel.RotorGrid;
                if (rotorGrid == null || rotorGrid.Physics == null)
                {
                    inventoryComplete = false;
                    break;
                }
                observedWheels.Add(wheel);
                wheelIds.Add(wheel.EntityId);
                wheelGridIds.Add(rotorGrid.EntityId);
                if (wheel.Steering) steeringWheelCount++;
            }
            inventoryComplete &= observedWheels.Count == localVehicleTestController.GridWheels.WheelCount &&
                observedWheels.Count == localVehicleTestController.GridWheels.Wheels.Count;
            if (!inventoryComplete)
            {
                LocalVehicleTestStop(nowUtc, "wheel_inventory_incomplete_or_outside_exact_grid", "blocked");
                return;
            }
            localVehicleTestWheels = observedWheels;
            if (!LocalVehicleTestBindingPolicy.HasExactWheelMap(wheelIds, wheelGridIds))
            {
                LocalVehicleTestStop(nowUtc, "runtime_wheel_or_rotor_grid_pairs_differ_from_pinned_fixture", "blocked");
                return;
            }

            var physics = localVehicleTestController.CubeGrid.Physics;
            var linearVelocity = new Vector3D(physics.LinearVelocity);
            var angularVelocity = new Vector3D(physics.AngularVelocity);
            var linearSpeed = linearVelocity.Length();
            var angularSpeed = angularVelocity.Length();
            var forward = localVehicleTestController.WorldMatrix.Forward;
            var gridForward = localVehicleTestController.CubeGrid.WorldMatrix.Forward;
            var steeringOrientationKnown = steeringWheelCount > 0 &&
                Finite(forward.X) && Finite(forward.Y) && Finite(forward.Z) &&
                Finite(gridForward.X) && Finite(gridForward.Y) && Finite(gridForward.Z) &&
                forward.LengthSquared() > 0.9 && gridForward.LengthSquared() > 0.9 &&
                Math.Abs(Vector3D.Dot(Vector3D.Normalize(forward), Vector3D.Normalize(gridForward))) >= 0.5;
            var writerAuditPassed = AuditLocalVehicleTestAssemblyWriters(localVehicleTestController,
                observedWheels, out var writerEvidence);

            var maximumRunDisplacement = LocalVehicleTestMaximumRunDisplacement();
            if (!Finite(maximumRunDisplacement) || maximumRunDisplacement <= 0 ||
                maximumRunDisplacement > LocalVehicleTestMaximumRunDisplacementMeters)
            {
                LocalVehicleTestStop(nowUtc, "run_displacement_bound_missing_or_invalid", "blocked");
                return;
            }
            var runPosition = new Vector3D(localVehicleTestController.CubeGrid.WorldMatrix.Translation);
            var cameraReady = LocalVehicleTestCameraPathClear(out var cameraEvidence);
            if (!cameraReady && localVehicleTestActiveCommand != null &&
                (localVehicleTestActiveCommand.Kind == LocalVehicleTestCommandKind.Propulsion ||
                 localVehicleTestActiveCommand.Kind == LocalVehicleTestCommandKind.Drive))
            {
                var obstacleDetected = cameraEvidence.IndexOf("status=obstacle", StringComparison.Ordinal) >= 0;
                LocalVehicleTestStop(nowUtc,
                    obstacleDetected ? "adaptive_forward_fan_obstacle_auto_stop" :
                        "adaptive_forward_fan_unknown_auto_stop",
                    "stopped", localVehicleTestActiveCommand.CommandId, linearSpeed, angularSpeed);
                MyLog.Default.WriteLineAndConsole("Voidwright local test auto-stop: run=" + runId +
                    " fixture=" + fixtureId + " grid=" + authorization.GridId +
                    " reason=" + (obstacleDetected ? "obstacle" : "unknown") +
                    " speed=" + linearSpeed.ToString("R", CultureInfo.InvariantCulture) +
                    " scan=" + cameraEvidence.Replace(' ', '_'));
                return;
            }
            var totalGravity = new Vector3D(localVehicleTestController.GetTotalGravity());
            var gravityVerified = Finite(totalGravity.Length()) && totalGravity.Length() >= 0.1;
            var groundContactVerified = LocalVehicleTestGroundSupport(observedWheels, totalGravity,
                out var groundEvidence);
            // This game build exposes the parking-brake state while reporting
            // the service-brake state as false. Accept a consistent engaged
            // brake/handbrake readback across the controller and all wheels.
            var brakesFullyApplied = (localVehicleTestController.GridWheels.Brake ||
                localVehicleTestController.GridWheels.HandBrake) &&
                LocalVehicleTestWheelBrakesActive(observedWheels);
            var brakesFullyReleased = !localVehicleTestController.GridWheels.Brake &&
                !localVehicleTestController.GridWheels.HandBrake;
            var controllerBrakeStateSafe = localVehicleTestBrakeOnly
                ? brakesFullyApplied
                : brakesFullyApplied || brakesFullyReleased;
            var controlsSafe = LocalVehicleTestActuatorsNeutral(observedWheels) && controllerBrakeStateSafe;
            if (!localVehicleTestBaselinePassed &&
                LocalVehicleTestBindingPolicy.ShouldSample(localVehicleTestUpdateTicks))
            {
                var sampleNow = System.Diagnostics.Stopwatch.GetTimestamp();
                var allWheelPhysics = true;
                foreach (var wheel in observedWheels)
                    allWheelPhysics &= wheel.RotorGrid != null && wheel.RotorGrid.Physics != null;
                var subgridsStationary = LocalVehicleTestSubgridsStationary(observedWheels,
                    localVehicleTestBaselineRotorPositions, true, true, out var subgridMotionEvidence);
                var exactIdentity = LocalVehicleTestBindingPolicy.IsExactRun(LocalVehicleTestInstance,
                    runId, authorization.RunId, authorization.GridId, authorization.ControllerId,
                    authorization.CameraId);
                var stationary = controlsSafe && linearSpeed <= 0.02 && angularSpeed <= 0.02 &&
                    subgridsStationary && groundContactVerified && gravityVerified;
                if (!localVehicleTestBaselinePositionReady)
                {
                    localVehicleTestBaselinePosition = runPosition;
                    localVehicleTestBaselinePositionReady = true;
                }
                var positionStable = Vector3D.Distance(localVehicleTestBaselinePosition, runPosition) <= 0.05;
                if (!exactIdentity || !allWheelPhysics || !cameraReady || !stationary || !positionStable)
                {
                    localVehicleTestBaselineSamples = 0;
                    localVehicleTestBaselineStartTimestamp = sampleNow;
                    localVehicleTestBaselinePosition = runPosition;
                    localVehicleTestBaselineRotorPositions.Clear();
                }
                else
                {
                    if (localVehicleTestBaselineStartTimestamp == 0) localVehicleTestBaselineStartTimestamp = sampleNow;
                    if (localVehicleTestBaselineSamples < 1800) localVehicleTestBaselineSamples++;
                }
                var elapsed = localVehicleTestBaselineStartTimestamp == 0 ? 0 :
                    (sampleNow - localVehicleTestBaselineStartTimestamp) /
                    (double)System.Diagnostics.Stopwatch.Frequency;
                localVehicleTestBaselinePassed = LocalVehicleTestBindingPolicy.CanAcceptBaseline(
                    exactIdentity && LocalVehicleTestBindingPolicy.HasExactWheelMap(wheelIds, wheelGridIds),
                    physics != null, allWheelPhysics, localVehicleTestCameraId > 0 && cameraReady,
                    cameraReady, cameraReady, stationary && positionStable,
                    groundContactVerified, gravityVerified,
                    localVehicleTestBaselineSamples, elapsed);
                MyLog.Default.WriteLineAndConsole("Voidwright local baseline sample: run=" + runId +
                    " fixture=" + fixtureId + " tick=" + localVehicleTestUpdateTicks +
                    " stopwatch=" + sampleNow + " sampleCount=" + localVehicleTestBaselineSamples +
                    " elapsedSeconds=" + elapsed.ToString("R", CultureInfo.InvariantCulture) +
                    " chassisGrid=" + authorization.GridId + " position=" + LocalVehicleTestVector(runPosition) +
                    " chassisForward=" + LocalVehicleTestVector(gridForward) +
                    " linearVelocity=" + LocalVehicleTestVector(linearVelocity) +
                    " angularVelocity=" + LocalVehicleTestVector(angularVelocity) +
                    " wheelPairs=" + string.Join(",", wheelIds) + "->" + string.Join(",", wheelGridIds) +
                    " actuatorState=" + LocalVehicleTestActuatorStateEvidence(observedWheels) +
                    " subgridPhysics=" + LocalVehicleTestSubgridPhysicsEvidence(observedWheels) +
                    " subgridMotion=" + subgridMotionEvidence.Replace(' ', '_') +
                    " groundSupport=" + groundContactVerified + ":" + groundEvidence.Replace(' ', '_') +
                    " controlsSafe=" + controlsSafe + " cameraId=" + localVehicleTestCameraId +
                    " cameraEvidence=" + cameraEvidence.Replace(' ', '_') +
                    " passed=" + localVehicleTestBaselinePassed);
            }
            if (!localVehicleTestBaselinePassed)
            {
                if (localVehicleTestUpdateTicks >= 1800)
                    LocalVehicleTestStop(nowUtc, "read_only_live_baseline_failed_or_timed_out", "blocked");
                return;
            }
            if (!localVehicleTestStopPathVerified)
                localVehicleTestStopPathVerified = LocalVehicleTestApplyStopAndVerify();
            if (LocalVehicleTestDisplacementPolicy.CanInitialize(localVehicleTestRunStartPositionReady,
                    LocalVehicleTestActuatorsNeutral(observedWheels), localVehicleTestStopPathVerified,
                    writerAuditPassed, linearSpeed, angularSpeed))
            {
                localVehicleTestRunStartPosition = runPosition;
                localVehicleTestRunStartPositionReady = true;
                MyLog.Default.WriteLineAndConsole("Voidwright local test baseline: run=" + runId +
                    " fixture=" + fixtureId + " grid=" + authorization.GridId +
                    " controller=" + authorization.ControllerId +
                    " position=" + LocalVehicleTestVector(runPosition) +
                    " maximumRunDisplacementMeters=" + maximumRunDisplacement.ToString("R", CultureInfo.InvariantCulture) +
                    " inventoryComplete=" + inventoryComplete +
                    " wheelReadbacks=" + LocalVehicleTestWheelSteeringEvidence() +
                    " stopPathVerified=" + localVehicleTestStopPathVerified +
                    " writerAuditPassed=" + writerAuditPassed +
                    " writerAudit=" + writerEvidence.Replace(' ', '_') +
                    " linearSpeed=" + linearSpeed.ToString("R", CultureInfo.InvariantCulture) +
                    " angularSpeed=" + angularSpeed.ToString("R", CultureInfo.InvariantCulture) +
                    " steeringOrientationKnown=" + steeringOrientationKnown);
            }
            if (localVehicleTestRunStartPositionReady)
            {
                if (!LocalVehicleTestDisplacementPolicy.TryMeasure(
                        localVehicleTestRunStartPosition.X, localVehicleTestRunStartPosition.Y,
                        localVehicleTestRunStartPosition.Z, runPosition.X, runPosition.Y, runPosition.Z,
                        out var runDisplacement) ||
                    LocalVehicleTestDisplacementPolicy.Exceeds(runDisplacement, maximumRunDisplacement))
                {
                    LocalVehicleTestStop(nowUtc, "maximum_local_test_displacement_reached", "stopped",
                        localVehicleTestActiveCommand == null ? null : localVehicleTestActiveCommand.CommandId,
                        linearSpeed, angularSpeed);
                    return;
                }
            }

            // Carry a prior steering result only when the worker validated its
            // immutable run receipt and this live vehicle still matches that
            // exact wheel map in a stationary, neutral, stop-ready state.
            if (localVehicleTestDirectionProofEnabled)
            {
                var proofWheelMapMatches = LocalVehicleTestDirectionProofMatchesWheels(observedWheels);
                if (!proofWheelMapMatches)
                    localVehicleTestSteeringDirectionConfirmed = false;
                else if (!localVehicleTestSteeringDirectionConfirmed &&
                         LocalVehicleTestDirectionProofPolicy.CanCarryForward(
                             localVehicleTestDirectionProofEnabled, proofWheelMapMatches,
                             steeringOrientationKnown, localVehicleTestStopPathVerified,
                             writerAuditPassed, LocalVehicleTestActuatorsNeutral(observedWheels),
                             linearSpeed, angularSpeed))
                    localVehicleTestSteeringDirectionConfirmed = true;
            }
            // Without a carried proof, a fresh same-run stationary steering
            // readback may establish direction. Keep that observation for the
            // exact authorization instead of erasing it on the next heartbeat.

            var readiness = new LocalVehicleTestReadiness(LocalVehicleTestInstance, runId, fixtureId,
                authorization.GridId, authorization.ControllerId, wheelIds, wheelGridIds, nowUtc,
                inventoryComplete && localVehicleTestBaselinePassed, boundedMode && cameraReady, localVehicleTestStopPathVerified,
                localVehicleTestStopPathVerified, steeringOrientationKnown, writerAuditPassed,
                linearSpeed, angularSpeed, localVehicleTestStationarySteeringOnly,
                groundContactVerified, gravityVerified,
                fullAssemblyClearanceVerified: boundedMode && cameraReady,
                brakeOnly: localVehicleTestBrakeOnly);

            if (!localVehicleTestPreflightLogged && localVehicleTestRunStartPositionReady &&
                inventoryComplete && boundedMode && cameraReady && localVehicleTestStopPathVerified &&
                steeringOrientationKnown && writerAuditPassed && groundContactVerified && gravityVerified &&
                LocalVehicleTestActuatorsNeutral(observedWheels) &&
                linearSpeed <= 0.02 && angularSpeed <= 0.02)
            {
                localVehicleTestPreflightLogged = true;
                MyLog.Default.WriteLineAndConsole("Voidwright local test preflight passed: run=" + runId +
                    " fixture=" + fixtureId + " grid=" + authorization.GridId +
                    " controller=" + authorization.ControllerId +
                    " wheelIds=" + string.Join(",", wheelIds) +
                    " wheelGridIds=" + string.Join(",", wheelGridIds) +
                    " stopPathVerified=true writerAuditPassed=true steeringOrientationKnown=true" +
                    " inventoryComplete=true isolatedClear=true actuatorsNeutral=true" +
                    " linearSpeed=" + linearSpeed.ToString("R", CultureInfo.InvariantCulture) +
                    " angularSpeed=" + angularSpeed.ToString("R", CultureInfo.InvariantCulture) +
                    " maximumRunDisplacementMeters=" + maximumRunDisplacement.ToString("R", CultureInfo.InvariantCulture) +
                    " baselinePosition=" + LocalVehicleTestVector(runPosition));
            }

            if (!LocalVehicleTestMailbox.TryReadCommand(out var command) || command == null)
            {
                LocalVehicleTestStop(nowUtc, "no_current_command", "idle");
                if (localVehicleTestUpdateTicks % 60 == 0)
                    MyLog.Default.WriteLineAndConsole("Voidwright local test idle: grid=" + authorization.GridId +
                        " controller=" + authorization.ControllerId +
                        " position=" + LocalVehicleTestVector(localVehicleTestController.CubeGrid.WorldMatrix.Translation) +
                        " linearSpeed=" + linearSpeed.ToString("R", CultureInfo.InvariantCulture) +
                        " angularSpeed=" + angularSpeed.ToString("R", CultureInfo.InvariantCulture) +
                        " steeringOrientationKnown=" + steeringOrientationKnown +
                        " wheelReadbacks=" + LocalVehicleTestWheelSteeringEvidence() +
                        " writerAudit=" + writerEvidence.Replace(' ', '_') +
                        " steeringDirectionConfirmed=" + localVehicleTestSteeringDirectionConfirmed +
                        " stationarySteeringOnly=" + localVehicleTestStationarySteeringOnly +
                        " isolatedClearLive=" + cameraReady + " baselinePassed=" + localVehicleTestBaselinePassed +
                        " propulsionHardDisabled=" + localVehicleTestStationarySteeringOnly +
                        " maximumRunDisplacementMeters=" + LocalVehicleTestMaximumRunDisplacement().ToString("R", CultureInfo.InvariantCulture) +
                        " runDisplacementMeters=" + LocalVehicleTestRunDisplacement(
                            new Vector3D(localVehicleTestController.CubeGrid.WorldMatrix.Translation)));
                return;
            }
            var isNewCommand = command.CommandId != localVehicleTestLastCommandId;
            if (isNewCommand)
            {
                localVehicleTestLastCommandId = command.CommandId;
                localVehicleTestLastCommandWire = LocalVehicleTestWire.FormatCommand(command);
                localVehicleTestStationarySamples = 0;
            }
            else if (!string.Equals(LocalVehicleTestWire.FormatCommand(command),
                         localVehicleTestLastCommandWire, StringComparison.Ordinal))
            {
                LocalVehicleTestStop(nowUtc, "command_id_replayed_with_changed_content", "blocked",
                    command.CommandId, linearSpeed, angularSpeed);
                return;
            }

            // Preserve a sign measurement from the previous steering update even when
            // that update's small chassis response trips the stationary speed guard.
            // The speed check below remains unchanged and still brakes before any
            // subsequent steering or propulsion dispatch.
            var requestedSteering = command.Kind == LocalVehicleTestCommandKind.Steering
                ? command.Value
                : command.Kind == LocalVehicleTestCommandKind.Drive ? command.SteeringValue : 0;
            if (!isNewCommand && (command.Kind == LocalVehicleTestCommandKind.Steering ||
                                  command.Kind == LocalVehicleTestCommandKind.Drive) &&
                Math.Abs(requestedSteering) > 0.001)
            {
                var priorMeasuredAngle = LocalVehicleTestAverageSteeringAngle();
                if (Math.Abs(priorMeasuredAngle) > 0.005)
                {
                    if (Math.Sign(priorMeasuredAngle) == Math.Sign(requestedSteering))
                        localVehicleTestSteeringDirectionConfirmed = true;
                    else
                    {
                        LocalVehicleTestStop(nowUtc, "measured_steering_angle_has_opposite_sign", "blocked",
                            command.CommandId, linearSpeed, angularSpeed);
                        return;
                    }
                }
            }

            if (!LocalVehicleTestControlPolicy.CanDispatch(authorization, readiness, command,
                    LocalVehicleTestInstance, runId, fixtureId, authorization.GridId,
                    authorization.ControllerId, authorization.CameraId,
                    LocalVehicleTestBindingPolicy.WheelIds,
                    LocalVehicleTestBindingPolicy.WheelGridIds, nowUtc, out var denial))
            {
                var failedState = command.ExpiresUtc <= nowUtc ? "expired" : "blocked";
                LocalVehicleTestStop(nowUtc, denial, failedState, command.CommandId, linearSpeed, angularSpeed);
                return;
            }
            if (!LocalVehicleTestBindingPolicy.IsForwardOnly(command.Kind, command.Value))
            {
                LocalVehicleTestStop(nowUtc, "front_camera_clearance_cannot_authorize_reverse", "blocked",
                    command.CommandId, linearSpeed, angularSpeed);
                return;
            }
            if (localVehicleTestStationarySteeringOnly &&
                (command.Kind == LocalVehicleTestCommandKind.Propulsion || command.Kind == LocalVehicleTestCommandKind.Drive))
            {
                LocalVehicleTestStop(nowUtc, "propulsion_hard_disabled_in_stationary_steering_only_mode", "blocked",
                    command.CommandId, linearSpeed, angularSpeed);
                return;
            }
            if ((command.Kind == LocalVehicleTestCommandKind.Steering &&
                 (linearSpeed > 0.03 || angularSpeed > 0.03)) ||
                ((command.Kind == LocalVehicleTestCommandKind.Propulsion || command.Kind == LocalVehicleTestCommandKind.Drive) &&
                 (linearSpeed > command.MaximumSpeedMetersPerSecond + LocalVehicleTestControlPolicy.MaximumSpeedOvershootToleranceMetersPerSecond || angularSpeed > 0.1)))
            {
                LocalVehicleTestStop(nowUtc, "live_linear_or_angular_speed_limit_exceeded", "stopped",
                    command.CommandId, linearSpeed, angularSpeed);
                return;
            }
            if (LocalVehicleTestControlPolicy.RequiresSteeringDirectionConfirmation(command) &&
                !localVehicleTestSteeringDirectionConfirmed)
            {
                LocalVehicleTestStop(nowUtc, "stationary_steering_direction_not_yet_confirmed", "blocked",
                    command.CommandId, linearSpeed, angularSpeed);
                return;
            }

            if (!LocalVehicleTestApply(command))
            {
                LocalVehicleTestStop(nowUtc, "actuator_write_or_readback_failed", "stopped",
                    command.CommandId, linearSpeed, angularSpeed);
                return;
            }
            localVehicleTestStopRotorPositions.Clear();
            localVehicleTestLastStopSampleTimestamp = 0;
            localVehicleTestActiveCommand = command;
            localVehicleTestSteeringAngleRadians = LocalVehicleTestAverageSteeringAngle();
            if ((command.Kind == LocalVehicleTestCommandKind.Steering || command.Kind == LocalVehicleTestCommandKind.Drive) &&
                Math.Abs(requestedSteering) > 0.001)
            {
                if (Math.Abs(localVehicleTestSteeringAngleRadians) > 0.005 &&
                    Math.Sign(localVehicleTestSteeringAngleRadians) == Math.Sign(requestedSteering))
                    localVehicleTestSteeringDirectionConfirmed = true;
                else if (Math.Abs(localVehicleTestSteeringAngleRadians) > 0.005)
                {
                    LocalVehicleTestStop(nowUtc, "measured_steering_angle_has_opposite_sign", "blocked",
                        command.CommandId, linearSpeed, angularSpeed);
                    return;
                }
            }
            if (isNewCommand && (command.Kind == LocalVehicleTestCommandKind.Steering ||
                                 command.Kind == LocalVehicleTestCommandKind.Drive))
                MyLog.Default.WriteLineAndConsole("Voidwright local test steering: run=" + runId +
                    " fixture=" + fixtureId + " grid=" + authorization.GridId + " controller=" + authorization.ControllerId +
                    " command=" + command.CommandId + " requestedSteering=" + requestedSteering.ToString("R", CultureInfo.InvariantCulture) +
                    " requestedPropulsion=" + (command.Kind == LocalVehicleTestCommandKind.Drive
                        ? command.Value.ToString("R", CultureInfo.InvariantCulture) : "0") +
                    " measuredSteeringAngle=" + LocalVehicleTestAverageSteeringAngle().ToString("R", CultureInfo.InvariantCulture) +
                    " wheelReadbacks=" + LocalVehicleTestWheelSteeringEvidence() +
                    " linearSpeed=" + linearSpeed.ToString("R", CultureInfo.InvariantCulture) +
                    " angularSpeed=" + angularSpeed.ToString("R", CultureInfo.InvariantCulture) +
                    " " + LocalVehicleTestGridPhysicsEvidence() +
                    " position=" + LocalVehicleTestVector(localVehicleTestController.CubeGrid.WorldMatrix.Translation) +
                    " writerAudit=" + writerEvidence.Replace(' ', '_') +
                    " steeringDirectionConfirmed=" + localVehicleTestSteeringDirectionConfirmed +
                    " maximumRunDisplacementMeters=" + maximumRunDisplacement.ToString("R", CultureInfo.InvariantCulture) +
                    " propulsionHardDisabled=" + localVehicleTestStationarySteeringOnly);
            if (localVehicleTestActiveCommand != null &&
                (isNewCommand || localVehicleTestUpdateTicks % 6 == 0))
                MyLog.Default.WriteLineAndConsole("Voidwright local test sample: run=" + runId +
                    " fixture=" + fixtureId + " grid=" + authorization.GridId +
                    " controller=" + authorization.ControllerId + " command=" + command.CommandId +
                    " kind=" + command.Kind.ToString().ToLowerInvariant() +
                    " requestedValue=" + command.Value.ToString("R", CultureInfo.InvariantCulture) +
                    " requestedSteering=" + requestedSteering.ToString("R", CultureInfo.InvariantCulture) +
                    " requestedSpeedLimit=" + command.MaximumSpeedMetersPerSecond.ToString("R", CultureInfo.InvariantCulture) +
                    " " + LocalVehicleTestMotionEvidence(runPosition, forward, linearVelocity, angularVelocity,
                        linearSpeed, angularSpeed) +
                    " " + LocalVehicleTestGridPhysicsEvidence() +
                    " runDisplacementMeters=" + LocalVehicleTestRunDisplacement(runPosition) +
                    " brake=" + localVehicleTestController.GridWheels.Brake +
                    " handbrake=" + localVehicleTestController.GridWheels.HandBrake +
                    " wheelReadbacks=" + LocalVehicleTestWheelSteeringEvidence());
            if (linearSpeed <= 0.02 && angularSpeed <= 0.02) localVehicleTestStationarySamples++;
            else localVehicleTestStationarySamples = 0;
            if (localVehicleTestUpdateTicks % 6 == 0 || command.Kind == LocalVehicleTestCommandKind.Brake)
                LocalVehicleTestMailbox.WriteReceipt(new LocalVehicleTestReceipt(command.CommandId, "active",
                    "readback_verified", nowUtc, linearSpeed, angularSpeed, localVehicleTestStationarySamples,
                    command.Kind != LocalVehicleTestCommandKind.Propulsion && command.Kind != LocalVehicleTestCommandKind.Drive, true,
                    localVehicleTestStationarySamples >= DirectManeuverLifecycle.RequiredStationarySamples,
                    localVehicleTestSteeringAngleRadians, localVehicleTestSteeringDirectionConfirmed));
        }

        private bool LocalVehicleTestCameraPathClear(out string evidence)
        {
            evidence = "unavailable";
            if (localVehicleTestCameraId <= 0 || localVehicleTestController == null ||
                localVehicleTestController.CubeGrid == null || MyAPIGateway.Physics == null)
                return false;
            MyEntity entity;
            if (!MyEntities.TryGetEntityById(localVehicleTestCameraId, out entity) || entity == null || entity.Closed)
                return false;
            var camera = entity as IMyCameraBlock;
            if (camera == null || camera.CubeGrid == null ||
                camera.CubeGrid.EntityId != localVehicleTestController.CubeGrid.EntityId)
            { evidence = "camera_identity_or_grid_mismatch"; return false; }
            try
            {
                // Resolve the exact run-bound camera and cast a fresh engine fan on
                // every update; no config flag or cached scan can establish clearance.
                var matrix = camera.WorldMatrix;
                var cameraForward = matrix.Forward;
                var targetForward = localVehicleTestController.WorldMatrix.Forward;
                if (!Finite(cameraForward.X) || !Finite(cameraForward.Y) || !Finite(cameraForward.Z) ||
                    !Finite(targetForward.X) || !Finite(targetForward.Y) || !Finite(targetForward.Z) ||
                    cameraForward.LengthSquared() < 0.9 || targetForward.LengthSquared() < 0.9)
                { evidence = "camera_or_controller_forward_unavailable"; return false; }
                cameraForward.Normalize();
                targetForward.Normalize();
                var alignment = Vector3D.Dot(cameraForward, targetForward);
                if (!Finite(alignment) || alignment < 0.98)
                {
                    evidence = "camera_not_aligned_with_controller_travel_forward camera=" +
                        localVehicleTestCameraId + " targetGrid=" + localVehicleTestController.CubeGrid.EntityId +
                        " tick=" + localVehicleTestUpdateTicks + " alignment=" +
                        alignment.ToString("R", CultureInfo.InvariantCulture);
                    return false;
                }
                var direction = targetForward;
                var up = matrix.Up;
                var right = Vector3D.Cross(direction, up);
                if (!Finite(right.X) || !Finite(right.Y) || !Finite(right.Z) ||
                    right.LengthSquared() < 0.9 || up.LengthSquared() < 0.9)
                { evidence = "status=unknown camera_fan_frame_invalid"; return false; }
                right.Normalize();
                up.Normalize();
                var halfWidth = 0.75;
                if (localVehicleTestWheels != null)
                    foreach (var wheel in localVehicleTestWheels)
                    {
                        if (wheel == null || wheel.RotorGrid == null)
                        { evidence = "status=unknown wheel_geometry_missing"; return false; }
                        var wheelPosition = new Vector3D(wheel.RotorGrid.WorldMatrix.Translation);
                        var lateral = Math.Abs(Vector3D.Dot(wheelPosition -
                            localVehicleTestController.CubeGrid.WorldMatrix.Translation, right));
                        if (!Finite(lateral))
                        { evidence = "status=unknown wheel_geometry_invalid"; return false; }
                        halfWidth = Math.Max(halfWidth, lateral + 0.25);
                    }
                halfWidth = Math.Min(2.5, halfWidth);
                const double range = 4.0;
                var lateralOffsets = new[] { -halfWidth, 0.0, halfWidth };
                var verticalOffsets = new[] { -0.35, 0.0, 0.35 };
                var clearances = new double[lateralOffsets.Length * verticalOffsets.Length];
                var hitEntityIds = new long[clearances.Length];
                var ray = 0;
                for (var lateralIndex = 0; lateralIndex < lateralOffsets.Length; lateralIndex++)
                for (var verticalIndex = 0; verticalIndex < verticalOffsets.Length; verticalIndex++)
                {
                    var rayStart = matrix.Translation + direction * 1.5 +
                        right * lateralOffsets[lateralIndex] + up * verticalOffsets[verticalIndex];
                    var travelled = 0.0;
                    var resolved = false;
                    for (var attempt = 0; attempt < 5; attempt++)
                    {
                        var remaining = range - travelled;
                        if (remaining <= 0.05)
                        { clearances[ray] = range; resolved = true; break; }
                        IHitInfo hit;
                        if (!MyAPIGateway.Physics.CastRay(rayStart, rayStart + direction * remaining, out hit) ||
                            hit == null)
                        { clearances[ray] = range; resolved = true; break; }
                        var distance = Vector3D.Distance(rayStart, hit.Position);
                        if (!Finite(distance) || distance < 0 || distance > remaining + 0.1)
                        { evidence = "status=unknown invalid_ray_result"; return false; }
                        var hitGridId = hit.HitEntity == null ? 0 : hit.HitEntity.EntityId;
                        if (!LocalVehicleTestIsSelfHit(hitGridId))
                        {
                            clearances[ray] = Math.Min(range, travelled + distance);
                            hitEntityIds[ray] = hitGridId;
                            resolved = true;
                            break;
                        }
                        travelled += distance + 0.05;
                        rayStart = hit.Position + direction * 0.05;
                    }
                    if (!resolved)
                    { evidence = "status=unknown self_hit_limit ray=" + ray; return false; }
                    ray++;
                }
                var decision = LocalVehicleTestObstacleStopPolicy.Evaluate(clearances, range, range, true);
                var clearanceValues = new List<string>();
                foreach (var clearance in clearances)
                    clearanceValues.Add(clearance.ToString("R", CultureInfo.InvariantCulture));
                var clearanceEvidence = string.Join(",", clearanceValues.ToArray());
                var hitIdValues = new List<string>();
                foreach (var hitId in hitEntityIds)
                    hitIdValues.Add(hitId.ToString(CultureInfo.InvariantCulture));
                var hitIdEvidence = string.Join(",", hitIdValues.ToArray());
                evidence = "status=" + decision.Status.ToString().ToLowerInvariant() +
                    " camera=" + localVehicleTestCameraId + " targetGrid=" +
                    localVehicleTestController.CubeGrid.EntityId + " tick=" + localVehicleTestUpdateTicks +
                    " alignment=" + alignment.ToString("R", CultureInfo.InvariantCulture) +
                    " position=" + LocalVehicleTestVector(matrix.Translation) + " forward=" +
                    LocalVehicleTestVector(direction) + " width_m=" + halfWidth.ToString("R", CultureInfo.InvariantCulture) +
                    " stop_clearance_m=" + range.ToString("R", CultureInfo.InvariantCulture) +
                    " nearest_m=" + decision.NearestClearanceMeters.ToString("R", CultureInfo.InvariantCulture) +
                    " rays=" + clearanceEvidence + " hit_ids=" + hitIdEvidence;
                return LocalVehicleTestObstacleStopPolicy.MayContinue(decision);
            }
            catch (Exception error)
            {
                evidence = "raycast_error=" + error.GetType().Name;
                return false;
            }
        }

        private bool LocalVehicleTestIsSelfHit(long entityId)
        {
            if (entityId <= 0) return false;
            if (localVehicleTestController != null && localVehicleTestController.CubeGrid != null &&
                localVehicleTestController.CubeGrid.EntityId == entityId) return true;
            if (localVehicleTestWheels != null)
                foreach (var wheel in localVehicleTestWheels)
                    if (wheel != null && wheel.RotorGrid != null && wheel.RotorGrid.EntityId == entityId)
                        return true;
            return false;
        }

        private static string LocalVehicleTestSubgridPhysicsEvidence(
            IList<Sandbox.Game.Entities.Cube.MyMotorSuspension> wheels)
        {
            if (wheels == null) return "unavailable";
            var values = new List<string>();
            foreach (var wheel in wheels)
            {
                var grid = wheel == null ? null : wheel.RotorGrid;
                if (grid == null || grid.Physics == null)
                { values.Add("unknown"); continue; }
                var physics = grid.Physics;
                values.Add(grid.EntityId.ToString(CultureInfo.InvariantCulture) +
                    ":position=" + LocalVehicleTestVector(grid.WorldMatrix.Translation) +
                    ":forward=" + LocalVehicleTestVector(grid.WorldMatrix.Forward) +
                    ":linear=" + LocalVehicleTestVector(new Vector3D(physics.LinearVelocity)) +
                    ":angular=" + LocalVehicleTestVector(new Vector3D(physics.AngularVelocity)));
            }
            return string.Join(";", values.ToArray());
        }

        private static bool LocalVehicleTestSubgridsStationary(
            IList<Sandbox.Game.Entities.Cube.MyMotorSuspension> wheels,
            IDictionary<long, double[]> referencePositions, bool establishMissing,
            bool resetWindowOnExcess,
            out string evidence)
        {
            evidence = "unavailable";
            if (wheels == null || referencePositions == null || wheels.Count != 4) return false;
            var seen = new HashSet<long>();
            var values = new List<string>();
            var linearSpeeds = new List<double>();
            var angularSpeeds = new List<double>();
            var drifts = new List<double>();
            var passed = true;
            foreach (var wheel in wheels)
            {
                var grid = wheel == null ? null : wheel.RotorGrid;
                if (grid == null || grid.Physics == null || !seen.Add(grid.EntityId)) return false;
                var position = new Vector3D(grid.WorldMatrix.Translation);
                var linear = new Vector3D(grid.Physics.LinearVelocity).Length();
                var angular = new Vector3D(grid.Physics.AngularVelocity).Length();
                var current = new[] { position.X, position.Y, position.Z };
                if (!establishMissing && !referencePositions.ContainsKey(grid.EntityId)) return false;
                var positionReady = LocalVehicleTestBindingPolicy.ObserveRotorPosition(
                    referencePositions, grid.EntityId, current, 0.05, resetWindowOnExcess, out var drift);
                linearSpeeds.Add(linear);
                angularSpeeds.Add(angular);
                drifts.Add(drift);
                var valid = positionReady && Finite(linear) && Finite(angular) && Finite(drift) &&
                    linear <= 0.02 && angular <= 0.02 && drift <= 0.05;
                passed &= valid;
                values.Add(grid.EntityId.ToString(CultureInfo.InvariantCulture) + ":linear=" +
                    linear.ToString("R", CultureInfo.InvariantCulture) + ":angular=" +
                    angular.ToString("R", CultureInfo.InvariantCulture) + ":drift=" +
                    drift.ToString("R", CultureInfo.InvariantCulture) + ":pass=" + valid);
            }
            evidence = string.Join(";", values.ToArray());
            return passed && seen.Count == 4 &&
                LocalVehicleTestBindingPolicy.AreSubgridsStationary(linearSpeeds, angularSpeeds, drifts);
        }

        // A fresh no-command baseline must establish real static-surface contact
        // at every wheel. In the isolated planet-only candidate, static contact
        // is terrain; stale contact and normals that disagree with gravity fail closed.
        private static bool LocalVehicleTestGroundSupport(
            IList<Sandbox.Game.Entities.Cube.MyMotorSuspension> wheels, Vector3D gravity,
            out string evidence)
        {
            if (wheels == null || wheels.Count != 4 || !Finite(gravity.X) || !Finite(gravity.Y) ||
                !Finite(gravity.Z) || gravity.Length() < 0.1)
            { evidence = "gravity_or_wheel_inventory_unavailable"; return false; }
            var gravityUp = Vector3D.Normalize(-gravity);
            var details = new List<string>();
            var allSupported = true;
            foreach (var suspension in wheels)
            {
                var wheel = suspension == null ? null : suspension.TopBlock as Sandbox.Game.Entities.Blocks.MyWheel;
                var frames = wheel == null ? ulong.MaxValue : wheel.FramesSinceLastContact;
                var normal = wheel == null ? Vector3D.Zero : new Vector3D(wheel.LastUsedGroundNormal);
                var normalAlignment = normal.LengthSquared() > 1e-12 && Finite(normal.X) &&
                    Finite(normal.Y) && Finite(normal.Z)
                    ? Vector3D.Dot(Vector3D.Normalize(normal), gravityUp) : -1;
                var contact = wheel != null && wheel.IsConsideredInContactWithStaticSurface;
                // LastUsedGroundNormal is reversed by this game build. Contact
                // is the authoritative support bit; alignment magnitude checks
                // that its reported normal is gravity-parallel regardless of
                // sign convention. Magnetar's contact-age counter advances
                // while its static-contact bit remains asserted on this
                // fixture, so preserve the counter as evidence but do not
                // reject that persistent contact state solely by its age.
                var supported = contact && Math.Abs(normalAlignment) >= 0.85;
                allSupported &= supported;
                details.Add((suspension == null ? 0 : suspension.EntityId).ToString(CultureInfo.InvariantCulture) +
                    ":contact=" + contact + ":framesSinceContact=" + frames.ToString(CultureInfo.InvariantCulture) +
                    ":normalGravityDot=" + normalAlignment.ToString("R", CultureInfo.InvariantCulture));
            }
            evidence = "gravityMagnitude=" + gravity.Length().ToString("R", CultureInfo.InvariantCulture) +
                ";gravityUp=" + LocalVehicleTestVector(gravityUp) + ";wheels=" + string.Join(";", details.ToArray());
            return allSupported;
        }

        private static long LocalVehicleTestExpectedId(string environmentName)
        {
            if (!long.TryParse(Environment.GetEnvironmentVariable(environmentName), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var id) || id <= 0) return 0;
            return id;
        }

        private bool LocalVehicleTestApplyStopAndVerify()
        {
            if (localVehicleTestController == null || localVehicleTestController.GridWheels == null ||
                localVehicleTestController.GridWheels.Wheels == null ||
                localVehicleTestController.GridWheels.Wheels.Count == 0) return false;
            try
            {
                var neutralized = true;
                foreach (var wheel in localVehicleTestController.GridWheels.Wheels)
                {
                    if (wheel == null || wheel.Closed || wheel.CubeGrid == null ||
                        wheel.CubeGrid.EntityId != localVehicleTestController.CubeGrid.EntityId)
                    { neutralized = false; continue; }
                    wheel.PropulsionOverride = 0;
                    wheel.SteeringOverride = 0;
                    if (Math.Abs(wheel.PropulsionOverride) > 0.001 || Math.Abs(wheel.SteeringOverride) > 0.001)
                        neutralized = false;
                }
                localVehicleTestController.GridWheels.Brake = true;
                localVehicleTestController.GridWheels.HandBrake = true;
                return neutralized && (localVehicleTestController.GridWheels.Brake ||
                    localVehicleTestController.GridWheels.HandBrake) &&
                    LocalVehicleTestWheelBrakesActive(localVehicleTestController.GridWheels.Wheels);
            }
            catch { return false; }
        }

        private bool LocalVehicleTestApply(LocalVehicleTestCommand command)
        {
            if (localVehicleTestController == null || localVehicleTestController.GridWheels == null ||
                localVehicleTestWheels == null || localVehicleTestWheels.Count == 0) return false;
            var propulsion = command.Kind == LocalVehicleTestCommandKind.Propulsion ||
                command.Kind == LocalVehicleTestCommandKind.Drive;
            var steeringValue = command.Kind == LocalVehicleTestCommandKind.Steering
                ? command.Value
                : command.Kind == LocalVehicleTestCommandKind.Drive ? command.SteeringValue : 0;
            try
            {
                if (propulsion)
                {
                    localVehicleTestController.GridWheels.Brake = false;
                    localVehicleTestController.GridWheels.HandBrake = false;
                    if (localVehicleTestController.GridWheels.Brake || localVehicleTestController.GridWheels.HandBrake) return false;
                }
                foreach (var wheel in localVehicleTestWheels)
                {
                    if (wheel == null || wheel.Closed || wheel.CubeGrid == null ||
                        wheel.CubeGrid.EntityId != command.GridId) return false;
                    var steering = Math.Abs(steeringValue) > 0
                        ? WheelSteeringMapping.ResolveDirectOverride(steeringValue, wheel.InvertSteer) : 0;
                    var wheelPropulsion = 0.0;
                    if (propulsion && wheel.Propulsion)
                    {
                        if (!DirectManeuverWheelRevolveInvert(localVehicleTestController, wheel, out var revolveInvert)) return false;
                        wheelPropulsion = WheelPropulsionMapping.ResolveDirectOverride(command.Value,
                            revolveInvert, wheel.InvertPropulsion);
                    }
                    wheel.PropulsionOverride = (float)wheelPropulsion;
                    wheel.SteeringOverride = (float)steering;
                    if (Math.Abs(wheel.PropulsionOverride - wheelPropulsion) > 0.001 ||
                        Math.Abs(wheel.SteeringOverride - steering) > 0.001) return false;
                }
                if (!propulsion)
                {
                    localVehicleTestController.GridWheels.Brake = true;
                    localVehicleTestController.GridWheels.HandBrake = true;
                    if (!localVehicleTestController.GridWheels.Brake ||
                        !localVehicleTestController.GridWheels.HandBrake ||
                        !LocalVehicleTestWheelBrakesActive(localVehicleTestWheels)) return false;
                }
                return !propulsion || (!localVehicleTestController.GridWheels.Brake &&
                    !localVehicleTestController.GridWheels.HandBrake);
            }
            catch { return false; }
        }

        private void LocalVehicleTestStop(DateTime nowUtc, string reason, string state,
            string commandId = null, double linearSpeed = 0, double angularSpeed = 0)
        {
            if (!localVehicleTestStopStartPositionReady && localVehicleTestActiveCommand != null &&
                (localVehicleTestActiveCommand.Kind == LocalVehicleTestCommandKind.Propulsion ||
                 localVehicleTestActiveCommand.Kind == LocalVehicleTestCommandKind.Drive) &&
                localVehicleTestController != null && localVehicleTestController.CubeGrid != null)
            {
                localVehicleTestStopStartPosition = new Vector3D(localVehicleTestController.CubeGrid.WorldMatrix.Translation);
                localVehicleTestStopStartPositionReady = true;
            }
            var neutralized = LocalVehicleTestApplyStopAndVerify();
            localVehicleTestActiveCommand = null;
            var subgridsStationary = LocalVehicleTestSubgridsStationary(localVehicleTestWheels,
                localVehicleTestStopRotorPositions, true, false, out var subgridStopEvidence);
            var sampleTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
            var distinctSample = LocalVehicleTestBindingPolicy.IsDistinctStopSample(
                localVehicleTestLastStopSampleTimestamp, sampleTimestamp);
            var stationary = distinctSample && Finite(linearSpeed) && Finite(angularSpeed) &&
                linearSpeed <= 0.02 && angularSpeed <= 0.02 && subgridsStationary;
            if (distinctSample)
            {
                localVehicleTestLastStopSampleTimestamp = sampleTimestamp;
                localVehicleTestLastStopSampleUtc = nowUtc;
                localVehicleTestStationarySamples = stationary ? localVehicleTestStationarySamples + 1 : 0;
            }
            var neutralReadbacks = LocalVehicleTestActuatorsNeutral(localVehicleTestWheels);
            var brakeReadbacks = localVehicleTestController != null &&
                localVehicleTestController.GridWheels != null &&
                (localVehicleTestController.GridWheels.Brake || localVehicleTestController.GridWheels.HandBrake) &&
                LocalVehicleTestWheelBrakesActive(localVehicleTestWheels);
            var stopConfirmed = LocalVehicleTestBindingPolicy.CanConfirmStop(neutralReadbacks,
                brakeReadbacks, Finite(linearSpeed) && Finite(angularSpeed) &&
                    linearSpeed <= 0.02 && angularSpeed <= 0.02, subgridsStationary,
                localVehicleTestStationarySamples, DirectManeuverLifecycle.RequiredStationarySamples);
            var terminalEvent = commandId != null && commandId != localVehicleTestLastTerminalCommandId;
            var confirmationEvent = stopConfirmed && commandId != null &&
                commandId != localVehicleTestLastStopConfirmationCommandId;
            var revokedHeartbeat = state == "revoked" && commandId == null && localVehicleTestUpdateTicks % 600 == 0;
            if (terminalEvent || confirmationEvent || revokedHeartbeat)
            {
                var position = localVehicleTestController == null || localVehicleTestController.CubeGrid == null
                    ? "unavailable" : LocalVehicleTestVector(localVehicleTestController.CubeGrid.WorldMatrix.Translation);
                var currentPosition = localVehicleTestController == null || localVehicleTestController.CubeGrid == null
                    ? new Vector3D(double.NaN, double.NaN, double.NaN)
                    : new Vector3D(localVehicleTestController.CubeGrid.WorldMatrix.Translation);
                var displacement = LocalVehicleTestRunDisplacement(currentPosition);
                var stoppingDistance = localVehicleTestStopStartPositionReady
                    ? Vector3D.Distance(localVehicleTestStopStartPosition, currentPosition).ToString("R", CultureInfo.InvariantCulture)
                    : "unavailable";
                var brake = localVehicleTestController != null && localVehicleTestController.GridWheels != null &&
                    localVehicleTestController.GridWheels.Brake;
                var handbrake = localVehicleTestController != null && localVehicleTestController.GridWheels != null &&
                    localVehicleTestController.GridWheels.HandBrake;
                MyLog.Default.WriteLineAndConsole("Voidwright local test terminal: run=" +
                    (Environment.GetEnvironmentVariable("VRAGECAGE_LAB_RUN_ID") ?? string.Empty) +
                    " fixture=" + (Environment.GetEnvironmentVariable("VRAGECAGE_LAB_FIXTURE_ID") ?? string.Empty) +
                    " grid=" + (localVehicleTestController == null || localVehicleTestController.CubeGrid == null ? 0 : localVehicleTestController.CubeGrid.EntityId) +
                    " controller=" + (localVehicleTestController == null ? 0 : localVehicleTestController.EntityId) +
                    " command=" + (commandId ?? localVehicleTestLastCommandId) + " state=" + state +
                    " reason=" + reason.Replace(' ', '_') + " position=" + position + " displacementMeters=" + displacement +
                    " stoppingDistanceMeters=" + stoppingDistance +
                    " " + LocalVehicleTestOrientationEvidence(currentPosition) +
                    " linearSpeed=" + linearSpeed.ToString("R", CultureInfo.InvariantCulture) +
                    " angularSpeed=" + angularSpeed.ToString("R", CultureInfo.InvariantCulture) +
                    " " + LocalVehicleTestGridPhysicsEvidence() +
                    " subgridStop=" + subgridStopEvidence.Replace(' ', '_') +
                    " wheelReadbacks=" + LocalVehicleTestWheelSteeringEvidence() +
                    " neutral=" + neutralized + " brake=" + brake + " handbrake=" + handbrake +
                    " stationarySamples=" + localVehicleTestStationarySamples +
                    " sampleTimestamp=" + sampleTimestamp.ToString(CultureInfo.InvariantCulture) +
                    " physicalStopObserved=" + stopConfirmed);
                if (terminalEvent) localVehicleTestLastTerminalCommandId = commandId;
                if (confirmationEvent) localVehicleTestLastStopConfirmationCommandId = commandId;
                if (confirmationEvent) localVehicleTestStopStartPositionReady = false;
            }
            if ((localVehicleTestUpdateTicks % 6 == 0 && state != "revoked") || terminalEvent ||
                confirmationEvent || revokedHeartbeat)
                LocalVehicleTestMailbox.WriteReceipt(new LocalVehicleTestReceipt(
                    string.IsNullOrWhiteSpace(commandId) ? (localVehicleTestLastCommandId.Length == 0 ? "idle" : localVehicleTestLastCommandId) : commandId,
                    state, reason, nowUtc, linearSpeed, angularSpeed, localVehicleTestStationarySamples,
                    neutralized, neutralized, stopConfirmed,
                    LocalVehicleTestAverageSteeringAngle(), localVehicleTestSteeringDirectionConfirmed));
        }

        private double LocalVehicleTestAverageSteeringAngle()
        {
            if (localVehicleTestWheels == null || localVehicleTestWheels.Count == 0) return 0;
            var sum = 0.0;
            var count = 0;
            foreach (var wheel in localVehicleTestWheels)
                if (wheel != null && !wheel.Closed && wheel.Steering)
                { sum += wheel.SteerAngle; count++; }
            return count == 0 ? 0 : sum / count;
        }

        private static double LocalVehicleTestMaximumRunDisplacement()
        {
            var text = Environment.GetEnvironmentVariable("VRAGECAGE_LOCAL_VEHICLE_TEST_MAX_DISPLACEMENT_METERS");
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
                   Finite(value) && value > 0 && value <= LocalVehicleTestMaximumRunDisplacementMeters
                ? value : double.NaN;
        }

        private string LocalVehicleTestRunDisplacement(Vector3D position)
        {
            if (!localVehicleTestRunStartPositionReady ||
                !LocalVehicleTestDisplacementPolicy.TryMeasure(
                    localVehicleTestRunStartPosition.X, localVehicleTestRunStartPosition.Y,
                    localVehicleTestRunStartPosition.Z, position.X, position.Y, position.Z,
                    out var displacement)) return "unavailable";
            return displacement.ToString("R", CultureInfo.InvariantCulture);
        }

        private string LocalVehicleTestOrientationEvidence(Vector3D position)
        {
            var forward = localVehicleTestController == null
                ? Vector3D.Zero : localVehicleTestController.WorldMatrix.Forward;
            return Finite(position.X) && Finite(position.Y) && Finite(position.Z) &&
                   Finite(forward.X) && Finite(forward.Y) && Finite(forward.Z) && forward.LengthSquared() > 0.9
                ? "headingForward=" + LocalVehicleTestVector(Vector3D.Normalize(forward))
                : "headingForward=unavailable";
        }

        private string LocalVehicleTestGridPhysicsEvidence()
        {
            if (localVehicleTestController == null || localVehicleTestController.CubeGrid == null ||
                localVehicleTestController.CubeGrid.Physics == null) return "gridPhysics=unavailable";
            return "gridIsStatic=" + localVehicleTestController.CubeGrid.IsStatic +
                " physicsIsStatic=" + localVehicleTestController.CubeGrid.Physics.IsStatic;
        }

        private static string LocalVehicleTestMotionEvidence(Vector3D position, Vector3D heading,
            Vector3D linearVelocity, Vector3D angularVelocity, double linearSpeed, double angularSpeed)
        {
            return "position=" + LocalVehicleTestVector(position) +
                " headingForward=" + (Finite(heading.X) && Finite(heading.Y) && Finite(heading.Z) && heading.LengthSquared() > 0.9
                    ? LocalVehicleTestVector(Vector3D.Normalize(heading)) : "unavailable") +
                " linearVelocity=" + LocalVehicleTestVector(linearVelocity) +
                " angularVelocity=" + LocalVehicleTestVector(angularVelocity) +
                " linearSpeed=" + linearSpeed.ToString("R", CultureInfo.InvariantCulture) +
                " angularSpeed=" + angularSpeed.ToString("R", CultureInfo.InvariantCulture);
        }

        private string LocalVehicleTestWheelSteeringEvidence()
        {
            if (localVehicleTestWheels == null || localVehicleTestWheels.Count == 0) return "unavailable";
            var values = new List<string>();
            foreach (var wheel in localVehicleTestWheels)
                if (wheel != null && !wheel.Closed)
                {
                    var rotor = wheel.Rotor;
                    var rotorGrid = wheel.RotorGrid;
                    var rotorReady = rotor != null && rotorGrid != null && rotorGrid.Physics != null;
                    var rotorAngularVelocity = 0.0;
                    if (rotorReady)
                    {
                        try { rotorAngularVelocity = wheel.RotorAngularVelocity.Length(); }
                        catch { rotorReady = false; }
                    }
                    values.Add(wheel.EntityId.ToString(CultureInfo.InvariantCulture) + ":working=" + wheel.IsWorking +
                        ",functional=" + wheel.IsFunctional +
                        ",power=" + wheel.Power.ToString("R", CultureInfo.InvariantCulture) +
                        ",speedLimitKmh=" + wheel.SpeedLimit.ToString("R", CultureInfo.InvariantCulture) +
                        ",rotorPresent=" + (rotor != null) +
                        ",rotorWorking=" + (rotor != null && rotor.IsWorking) +
                        ",rotorGrid=" + (rotorGrid == null ? 0 : rotorGrid.EntityId).ToString(CultureInfo.InvariantCulture) +
                        ",rotorPhysics=" + (rotorGrid != null && rotorGrid.Physics != null) +
                        ",rotorAngularVelocity=" + rotorAngularVelocity.ToString("R", CultureInfo.InvariantCulture) +
                        ",brake=" + wheel.Brake + ",handbrake=" + wheel.Handbrake +
                        ",steering=" + wheel.Steering +
                        ",invertSteer=" + wheel.InvertSteer +
                        ",propulsionEnabled=" + wheel.Propulsion +
                        ",invertPropulsion=" + wheel.InvertPropulsion +
                        ",override=" + wheel.SteeringOverride.ToString("R", CultureInfo.InvariantCulture) +
                        ",angle=" + wheel.SteerAngle.ToString("R", CultureInfo.InvariantCulture) +
                        ",propulsion=" + wheel.PropulsionOverride.ToString("R", CultureInfo.InvariantCulture));
                }
            return string.Join(";", values.ToArray());
        }

        private static bool LocalVehicleTestDirectionProofMatchesWheels(
            IList<Sandbox.Game.Entities.Cube.MyMotorSuspension> wheels)
        {
            if (wheels == null || wheels.Count != 4) return false;
            var wheelIds = new List<long>();
            var wheelGridIds = new List<long>();
            foreach (var wheel in wheels)
            {
                if (wheel == null || wheel.Closed || wheel.RotorGrid == null || wheel.InvertSteer) return false;
                wheelIds.Add(wheel.EntityId);
                wheelGridIds.Add(wheel.RotorGrid.EntityId);
            }
            return LocalVehicleTestBindingPolicy.HasExactWheelMap(wheelIds, wheelGridIds);
        }

        private static bool LocalVehicleTestActuatorsNeutral(
            IList<Sandbox.Game.Entities.Cube.MyMotorSuspension> wheels)
        {
            if (wheels == null || wheels.Count != 4) return false;
            foreach (var wheel in wheels)
                if (wheel == null || wheel.Closed || Math.Abs(wheel.PropulsionOverride) > 0.000001 ||
                    Math.Abs(wheel.SteeringOverride) > 0.000001)
                    return false;
            return true;
        }

        private string LocalVehicleTestActuatorStateEvidence(
            IList<Sandbox.Game.Entities.Cube.MyMotorSuspension> wheels)
        {
            if (wheels == null) return "unavailable";
            var values = new List<string>();
            foreach (var wheel in wheels)
                values.Add((wheel == null ? 0 : wheel.EntityId).ToString(CultureInfo.InvariantCulture) +
                    ":prop=" + (wheel == null ? "unavailable" : wheel.PropulsionOverride.ToString("R", CultureInfo.InvariantCulture)) +
                    ":steer=" + (wheel == null ? "unavailable" : wheel.SteeringOverride.ToString("R", CultureInfo.InvariantCulture)) +
                    ":brake=" + (wheel != null && wheel.Brake) +
                    ":handbrake=" + (wheel != null && wheel.Handbrake));
            return "controllerBrake=" + (localVehicleTestController != null && localVehicleTestController.GridWheels != null &&
                    localVehicleTestController.GridWheels.Brake) +
                ":controllerHandbrake=" + (localVehicleTestController != null && localVehicleTestController.GridWheels != null &&
                    localVehicleTestController.GridWheels.HandBrake) + ";wheels=" + string.Join(";", values.ToArray());
        }

        private static bool LocalVehicleTestWheelBrakesActive(
            IEnumerable<Sandbox.Game.Entities.Cube.MyMotorSuspension> wheels)
        {
            if (wheels == null) return false;
            var count = 0;
            foreach (var wheel in wheels)
            {
                count++;
                if (wheel == null || wheel.Closed || (!wheel.Brake && !wheel.Handbrake))
                    return false;
            }
            return count == 4;
        }

        private static bool IsSha256Hex(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (var character in value)
                if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f')))
                    return false;
            return true;
        }

        private static string LocalVehicleTestVector(Vector3D value)
            => value.X.ToString("R", CultureInfo.InvariantCulture) + "," +
               value.Y.ToString("R", CultureInfo.InvariantCulture) + "," +
               value.Z.ToString("R", CultureInfo.InvariantCulture);

        private void FinishModOwnedPreflightTimeout()
        {
            if (terminal) return;
            terminal = true;
            modHandshakeReason = "host and mod preflight did not pass before the configured command deadline";
            RevokeModApproval();

            try
            {
                var blocked = BuildMovementPreflight().Invalidate(DateTime.UtcNow, modHandshakeReason);
                if (!PersistMovementPreflight(blocked))
                    MyLog.Default.WriteLineAndConsole("[VRageCage][PREFLIGHT] UNKNOWN terminal preflight result could not be persisted.");
                LogMovementPreflight(blocked);
            }
            catch (Exception error)
            {
                MyLog.Default.WriteLineAndConsole("[VRageCage][PREFLIGHT] UNKNOWN terminal preflight snapshot failed: " + error.GetType().Name);
            }

            var eventType = commandKind == "preflight-only" ? "preflight-only" : "depth-controller";
            MyLog.Default.WriteLineAndConsole("Voidwright evidence: type=" + eventType + " operation=" + operationId +
                " controllerId=" + controllerId + " cameraId=" + navigationDepthCameraId +
                " status=timeout elapsedTicks=" + modPreflightElapsedTicks +
                " timeoutTicks=" + timeoutTicks + " actuatorDispatch=none reason=preflight_deadline_expired");
        }

        private bool CompleteModOwnedDepthArrivalIfStable()
        {
            var physics = controller == null || controller.CubeGrid == null
                ? null : controller.CubeGrid.Physics;
            if (!modActivationWritten || !modHeartbeatFresh || modHeartbeat == null ||
                controller == null || controller.GridWheels == null || physics == null)
            {
                modArrivalStableHeartbeatCount = 0;
                lastModArrivalHostSampleTick = long.MinValue;
                return false;
            }

            var position = controller.PositionComp.GetPosition();
            var distance = Vector3D.Distance(position, navigationTarget);
            var speed = new Vector3D(physics.LinearVelocity).Length();
            var wheelsNeutral = true;
            foreach (var wheel in controller.GridWheels.Wheels)
            {
                if (wheel == null || wheel.Closed ||
                    !Finite(wheel.SteeringOverride) || !Finite(wheel.PropulsionOverride) ||
                    Math.Abs(wheel.SteeringOverride) > 0.001 ||
                    Math.Abs(wheel.PropulsionOverride) > 0.001)
                {
                    wheelsNeutral = false;
                    break;
                }
            }
            var stable = DepthArrivalPolicy.IsStableArrival(distance, speed, arrivalRadius,
                maxSpeed, wheelsNeutral, modHeartbeat.SensorValid && modHeartbeat.ActuatorReady);
            if (!stable)
            {
                modArrivalStableHeartbeatCount = 0;
                lastModArrivalHostSampleTick = long.MinValue;
                return false;
            }
            // A mod-owned arrival is terminal on the mod side, so its last
            // valid heartbeat may remain unchanged while the host observes
            // the vehicle settling. Count distinct host updates, not mod
            // heartbeat ticks, to avoid waiting for heartbeats the completed
            // adapter correctly stops publishing.
            if (!DepthArrivalPolicy.IsNewHostSample(modPreflightElapsedTicks, lastModArrivalHostSampleTick))
                return false;
            lastModArrivalHostSampleTick = modPreflightElapsedTicks;
            modArrivalStableHeartbeatCount++;
            if (modArrivalStableHeartbeatCount < 3) return false;

            terminal = true;
            modHandshakeReason = "host confirmed three fresh bound arrival samples with neutral wheel overrides";
            RevokeModApproval();
            MyLog.Default.WriteLineAndConsole("Voidwright evidence: type=depth-controller operation=" + operationId +
                " controllerId=" + controllerId + " cameraId=" + navigationDepthCameraId +
                " status=complete elapsedTicks=" + modPreflightElapsedTicks +
                " position=" + V(position) + " distanceToTargetMeters=" + F(distance) +
                " speedMps=" + F(speed) + " stableHostSamples=" + modArrivalStableHeartbeatCount +
                " actuatorDispatch=none wheelsNeutral=true" +
                " sensorValidRays=" + modHeartbeat.ValidRayCount + "/" + VoidwrightPreflightHeartbeat.RayCount +
                " reason=stable_arrival_confirmed_by_host");
            return true;
        }

        private void UpdateDiscoveryBootstrap()
        {
            if (MyAPIGateway.Utilities == null || MyAPIGateway.Entities == null) return;
            var nowUtc = DateTime.UtcNow;
            if (discoveryBootstrapReady)
            {
                TryAcceptStageTwoActivation();
                if (nowUtc - lastDiscoveryBootstrapRefreshUtc < TimeSpan.FromSeconds(1)) return;
                discoveryBootstrapReady = false;
            }
            // Discovery snapshots can be large and are read, hashed, and parsed
            // on the game update thread. Bound retries while bootstrap is waiting.
            if (nowUtc - lastDiscoveryBootstrapRefreshUtc < TimeSpan.FromSeconds(1)) return;
            lastDiscoveryBootstrapRefreshUtc = nowUtc;
            try
            {
                var runId = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_RUN_ID") ?? string.Empty;
                var fixtureId = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_FIXTURE_ID") ?? string.Empty;
                var nonce = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_DISCOVERY_NONCE") ?? string.Empty;
                var baseline = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_DISCOVERY_BASELINE_RUN_ID") ?? string.Empty;
                if (!VoidwrightDiscoveryReceipt.Hex(runId, 32) || !VoidwrightDiscoveryReceipt.Hex(fixtureId, 32) ||
                    !VoidwrightDiscoveryReceipt.Hex(nonce, 32) || !VoidwrightDiscoveryReceipt.Hex(baseline, 32)) return;
                string json;
                using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(
                           "agent-core-discovery-v1.json", typeof(MySessionComponentBase))) json = reader.ReadToEnd();
                if (string.IsNullOrEmpty(json) || Encoding.UTF8.GetByteCount(json) > 16 * 1024 * 1024 ||
                    json.IndexOf("\"labRunId\":\"" + runId + "\"", StringComparison.Ordinal) < 0 ||
                    json.IndexOf("\"fixtureId\":\"" + fixtureId + "\"", StringComparison.Ordinal) < 0 ||
                    json.IndexOf("\"bootstrapNonce\":\"" + nonce + "\"", StringComparison.Ordinal) < 0 ||
                    json.IndexOf("\"agentsTruncated\":false", StringComparison.Ordinal) < 0 ||
                    json.IndexOf("\"physicalGridInventoryComplete\":true", StringComparison.Ordinal) < 0 ||
                    json.IndexOf("\"typedBlockInventoryComplete\":true", StringComparison.Ordinal) < 0 ||
                    json.IndexOf("\"mechanicalConnectionInventoryComplete\":true", StringComparison.Ordinal) < 0) return;
                var runMatch = System.Text.RegularExpressions.Regex.Match(json, "\\\"runId\\\":\\\"([0-9a-f]{32})\\\"");
                var sessionMatch = System.Text.RegularExpressions.Regex.Match(json, "\\\"sessionId\\\":\\\"([0-9a-f]{32})\\\"");
                var tickMatch = System.Text.RegularExpressions.Regex.Match(json, "\\\"capturedSimulationTick\\\":([0-9]+)");
                var capturedMatch = System.Text.RegularExpressions.Regex.Match(json, "\\\"capturedAtUtc\\\":\\\"([^\\\"]+)\\\"");
                long tick;
                DateTime captured;
                if (!runMatch.Success || runMatch.Groups[1].Value == baseline || !sessionMatch.Success ||
                    !tickMatch.Success || !long.TryParse(tickMatch.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out tick) || tick < 0 ||
                    !capturedMatch.Success || !DateTime.TryParse(capturedMatch.Groups[1].Value, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out captured) ||
                    DateTime.UtcNow < captured || DateTime.UtcNow - captured > TimeSpan.FromSeconds(15)) return;
                var bytes = Encoding.UTF8.GetBytes(json);
                var fingerprint = Sha256Bytes(bytes);
                var modReceiptText = string.Empty;
                using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(
                           "voidwright-discovery-" + runId + ".receipt", typeof(MySessionComponentBase)))
                    modReceiptText = reader.ReadToEnd();
                VoidwrightDiscoveryReceipt modReceipt;
                if (!VoidwrightDiscoveryReceipt.TryParse(modReceiptText, out modReceipt) ||
                    !modReceipt.IsFreshAndBound(DateTime.UtcNow, TimeSpan.FromSeconds(15), "mod-ready", runId,
                        fixtureId, nonce, sessionMatch.Groups[1].Value, runMatch.Groups[1].Value, tick, fingerprint)) return;
                if (modReceipt.ObservedGridCount <= 0) return;
                var hostReceipt = new VoidwrightDiscoveryReceipt { Status = "host-ready", RunId = runId,
                    FixtureId = fixtureId, BootstrapNonce = nonce, SessionId = modReceipt.SessionId,
                    ExporterRunId = modReceipt.ExporterRunId, Tick = tick, CapturedUtc = DateTime.UtcNow,
                    Fingerprint = fingerprint, ObservedGridCount = modReceipt.ObservedGridCount };
                var text = hostReceipt.Format();
                var filename = "voidwright-host-ready-" + runId + ".receipt";
                using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(filename, typeof(MySessionComponentBase))) writer.Write(text);
                using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(filename, typeof(MySessionComponentBase)))
                    if (!string.Equals(text, reader.ReadToEnd(), StringComparison.Ordinal)) return;
                discoveryBootstrapReady = true;
                MyLog.Default.WriteLineAndConsole("[VRageCage][DISCOVERY] Same-session discovery bootstrap accepted for run=" + runId + " exporter=" + modReceipt.ExporterRunId + " tick=" + tick.ToString(CultureInfo.InvariantCulture) + "; no command or actuator dispatch is enabled.");
            }
            catch (Exception error)
            {
                if (!modActivationFailureLogged)
                {
                    modActivationFailureLogged = true;
                    MyLog.Default.WriteLineAndConsole("[VRageCage][DISCOVERY] Bootstrap waiting or rejected: " + error.GetType().Name);
                }
            }
        }

        private void TryAcceptStageTwoActivation()
        {
            var nowUtc = DateTime.UtcNow;
            // This method runs from IPlugin.Update; avoid per-tick file reads and hashing.
            if (nowUtc - lastStageTwoActivationAttemptUtc < TimeSpan.FromSeconds(1)) return;
            lastStageTwoActivationAttemptUtc = nowUtc;
            try
            {
                var runId = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_RUN_ID") ?? string.Empty;
                var fixtureId = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_FIXTURE_ID") ?? string.Empty;
                var nonce = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_DISCOVERY_NONCE") ?? string.Empty;
                var filename = "voidwright-activation-" + runId + ".txt";
                string text;
                using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(filename, typeof(MySessionComponentBase))) text = reader.ReadToEnd();
                VoidwrightStageTwoActivation activation;
                if (!VoidwrightStageTwoActivation.TryParse(text, DateTime.UtcNow, out activation) ||
                    activation.RunId != runId || activation.FixtureId != fixtureId || activation.BootstrapNonce != nonce) return;
                VoidwrightDiscoveryReceipt hostReceipt;
                using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(
                           "voidwright-host-ready-" + runId + ".receipt", typeof(MySessionComponentBase))) text = reader.ReadToEnd();
                if (!VoidwrightDiscoveryReceipt.TryParse(text, out hostReceipt) ||
                    !activation.MatchesCurrentDiscovery(hostReceipt, DateTime.UtcNow, "host-ready")) return;
                PreflightRunContext context;
                string reason;
                if (!PreflightRunContext.TryLoad(activation.ContextPath, activation.ContextSha256, runId,
                        fixtureId, activation.OperationId, activation.Action, out context, out reason) ||
                    context.ControllerId != activation.ControllerId || context.GridId != activation.GridId ||
                    context.DepthCameraId != activation.CameraId || context.AgentCoreId != activation.CoreId ||
                    context.DiscoveryRunId != activation.ExporterRunId || context.DiscoveryTick != activation.Tick ||
                    context.DiscoveryFingerprint != activation.Fingerprint ||
                    !HostDiscoveryMatches(context, activation.SessionId, activation.BootstrapNonce,
                        activation.SuspensionIds)) return;
                DirectManeuverPlan zeroHoldPlan;
                if (activation.Action == "direct-maneuver" &&
                    (!DirectManeuverPlan.TryParseCompactSegments(context.DirectManeuverSegments,
                        context.RunId, context.FixtureId, context.OperationId, context.GridId,
                        context.ControllerId, context.AgentCoreId, out zeroHoldPlan) ||
                     !DirectManeuverCommandAcceptancePolicy.IsZeroOnlyStationaryHold(zeroHoldPlan,
                        context.RunId, context.FixtureId, context.OperationId, context.GridId,
                        context.ControllerId, context.AgentCoreId, context.DirectManeuverSegments,
                        context.TimeoutTicks))) return;
                Environment.SetEnvironmentVariable("VRAGECAGE_LAB_COMMAND", activation.Action);
                Environment.SetEnvironmentVariable("VRAGECAGE_LAB_SESSION_ID", activation.SessionId);
                Environment.SetEnvironmentVariable("VRAGECAGE_LAB_DISCOVERY_NONCE", activation.BootstrapNonce);
                Environment.SetEnvironmentVariable("VRAGECAGE_LAB_OPERATION_ID", activation.OperationId);
                Environment.SetEnvironmentVariable("VRAGECAGE_LAB_PREFLIGHT_CONTEXT", activation.ContextPath);
                Environment.SetEnvironmentVariable("VRAGECAGE_LAB_PREFLIGHT_CONTEXT_SHA256", activation.ContextSha256);
                Environment.SetEnvironmentVariable("VRAGECAGE_LAB_PREFLIGHT_CHALLENGE", activation.Challenge);
                Environment.SetEnvironmentVariable("VRAGECAGE_LAB_CONTROLLER_ID", activation.ControllerId.ToString(CultureInfo.InvariantCulture));
                Environment.SetEnvironmentVariable("VRAGECAGE_LAB_GRID_ID", activation.GridId.ToString(CultureInfo.InvariantCulture));
                Environment.SetEnvironmentVariable("VRAGECAGE_LAB_DEPTH_CAMERA_ID", activation.CameraId.ToString(CultureInfo.InvariantCulture));
                Environment.SetEnvironmentVariable("VRAGECAGE_LAB_AGENT_CORE_ID", activation.CoreId.ToString(CultureInfo.InvariantCulture));
                Environment.SetEnvironmentVariable("VRAGECAGE_LAB_AGENT_DISCOVERY_RUN_ID", activation.ExporterRunId);
                Environment.SetEnvironmentVariable("VRAGECAGE_LAB_AGENT_DISCOVERY_TICK", activation.Tick.ToString(CultureInfo.InvariantCulture));
                Environment.SetEnvironmentVariable("VRAGECAGE_LAB_AGENT_DISCOVERY_FINGERPRINT", activation.Fingerprint);
                Environment.SetEnvironmentVariable("VRAGECAGE_LAB_MAX_SPEED", context.MaximumSpeed.ToString("R", CultureInfo.InvariantCulture));
                Environment.SetEnvironmentVariable("VRAGECAGE_LAB_TIMEOUT_TICKS", context.TimeoutTicks.ToString(CultureInfo.InvariantCulture));
                Environment.SetEnvironmentVariable("VRAGECAGE_LAB_ARRIVAL_RADIUS", context.ArrivalRadius.ToString("R", CultureInfo.InvariantCulture));
                Environment.SetEnvironmentVariable("VRAGECAGE_LAB_START_DELAY_TICKS", context.StartDelayTicks.ToString(CultureInfo.InvariantCulture));
                Environment.SetEnvironmentVariable("VRAGECAGE_LAB_REVERSE", context.Reverse ? "true" : "false");
                Environment.SetEnvironmentVariable("VRAGECAGE_LAB_MANEUVER_SEGMENTS", context.DirectManeuverSegments ?? string.Empty);
                if (activation.Action == "direct-maneuver")
                    Environment.SetEnvironmentVariable("VRAGECAGE_LAB_MOTION_OWNER", "voidwright-plugin");
                ReadCommand();
                if (commandKind != activation.Action || !EnsureModActivation(context, true)) return;
                WriteStageTwoAcknowledgement(activation, "host-accepted");
                MyLog.Default.WriteLineAndConsole("[VRageCage][DISCOVERY] Stage-two " + activation.Action + " activation accepted.");
            }
            catch (Exception error)
            {
                if (!modActivationFailureLogged)
                {
                    modActivationFailureLogged = true;
                    MyLog.Default.WriteLineAndConsole("[VRageCage][DISCOVERY] Stage-two activation rejected or still pending: " + error.GetType().Name);
                }
            }
        }

        private void WriteStageTwoAcknowledgement(VoidwrightStageTwoActivation activation, string status)
        {
            var acknowledgement = new VoidwrightStageTwoAcknowledgement { Status = status,
                RunId = activation.RunId, FixtureId = activation.FixtureId, OperationId = activation.OperationId,
                BootstrapNonce = activation.BootstrapNonce, SessionId = activation.SessionId,
                ExporterRunId = activation.ExporterRunId, Tick = activation.Tick,
                Fingerprint = activation.Fingerprint, ContextSha256 = activation.ContextSha256,
                UpdatedUtc = DateTime.UtcNow };
            var expected = acknowledgement.Format();
            var name = "voidwright-ack-" + activation.RunId + "-" + status + ".txt";
            using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(name, typeof(MySessionComponentBase))) writer.Write(expected);
            using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(name, typeof(MySessionComponentBase)))
                if (!string.Equals(expected, reader.ReadToEnd(), StringComparison.Ordinal)) throw new InvalidOperationException("activation acknowledgment readback mismatch");
        }

        private bool EnsureModActivation()
        {
            return EnsureModActivation(null, false);
        }

        private bool EnsureModActivation(PreflightRunContext prevalidatedContext, bool discoveryWasValidated)
        {
            if (modActivationWritten) return true;
            var attemptUtc = DateTime.UtcNow;
            if (prevalidatedContext == null &&
                attemptUtc - lastModActivationAttemptUtc < TimeSpan.FromSeconds(1)) return false;
            lastModActivationAttemptUtc = attemptUtc;
            try
            {
                var runId = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_RUN_ID") ?? string.Empty;
                var fixtureId = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_FIXTURE_ID") ?? string.Empty;
                var challenge = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_PREFLIGHT_CHALLENGE") ?? string.Empty;
                var contextPath = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_PREFLIGHT_CONTEXT") ?? string.Empty;
                var contextSha = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_PREFLIGHT_CONTEXT_SHA256") ?? string.Empty;
                PreflightRunContext context = prevalidatedContext;
                string reason = null;
                if (!VoidwrightPreflightHandshake.IsValidChallenge(challenge))
                {
                    modHandshakeReason = "host rejected activation: launcher challenge is malformed";
                    LogModActivationFailure(modHandshakeReason);
                    return false;
                }
                var expectedAction = commandKind == "direct-maneuver" || commandKind == "preflight-only"
                    ? commandKind : "drive-to-coordinate-depth";
                if (context == null && !PreflightRunContext.TryLoad(contextPath, contextSha, runId, fixtureId, operationId,
                        expectedAction, out context, out reason))
                {
                    modHandshakeReason = "host rejected activation: " + reason;
                    LogModActivationFailure(modHandshakeReason);
                    return false;
                }
                var direct = expectedAction == "direct-maneuver";
                var preflightOnly = expectedAction == "preflight-only";
                if (context.ControllerId != controllerId || context.DepthCameraId != navigationDepthCameraId ||
                    context.MaximumSpeed != maxSpeed || context.TimeoutTicks != timeoutTicks ||
                    (!direct && (context.TargetX != navigationTarget.X || context.TargetY != navigationTarget.Y ||
                    context.TargetZ != navigationTarget.Z || context.ArrivalRadius != arrivalRadius ||
                    context.StartDelayTicks != navigationStartDelayTicks || context.Reverse != driveReverse)) ||
                    ((direct || preflightOnly) && (context.GridId != expectedGridId || context.DepthCameraId != navigationDepthCameraId ||
                    context.AgentCoreId != expectedAgentCoreId ||
                    context.DiscoveryRunId != expectedDiscoveryRunId || context.DiscoveryTick != expectedDiscoveryTick ||
                    context.DiscoveryFingerprint != expectedDiscoveryFingerprint ||
                    (direct && context.DirectManeuverPlan == null) ||
                    context.DirectManeuverSegments != (Environment.GetEnvironmentVariable("VRAGECAGE_LAB_MANEUVER_SEGMENTS") ?? string.Empty))) )
                {
                    modHandshakeReason = "host rejected activation: launcher command differs from the verified immutable context";
                    LogModActivationFailure(modHandshakeReason);
                    return false;
                }
                if ((direct || preflightOnly) && !discoveryWasValidated && !HostDiscoveryMatches(context))
                {
                modHandshakeReason = "host rejected activation: exact Agent Core discovery is stale, cross-session, or no longer matches the pinned fixture entities";
                    LogModActivationFailure(modHandshakeReason);
                    return false;
                }
                var now = DateTime.UtcNow;
                var activation = new VoidwrightModActivation
                {
                    RunId = context.RunId, FixtureId = context.FixtureId, OperationId = context.OperationId,
                    ContextSha256 = context.ContextSha256, Challenge = challenge,
                    ControllerId = context.ControllerId, CameraId = context.DepthCameraId,
                    TargetX = context.TargetX, TargetY = context.TargetY, TargetZ = context.TargetZ,
                    MaximumSpeed = context.MaximumSpeed, TimeoutTicks = context.TimeoutTicks,
                    ArrivalRadius = context.ArrivalRadius, StartDelayTicks = context.StartDelayTicks,
                    Reverse = context.Reverse, SourceTreeSha256 = context.SourceTreeSha256,
                    Action = context.Action, GridId = context.GridId,
                    AgentCoreId = context.AgentCoreId,
                    DiscoveryRunId = context.DiscoveryRunId, DiscoveryTick = context.DiscoveryTick,
                    DiscoveryFingerprint = context.DiscoveryFingerprint,
                    DirectManeuverSegments = context.DirectManeuverSegments,
                    IssuedUtc = now, ExpiresUtc = now.AddMinutes(5)
                };
                modActivationText = activation.Format();
                using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(
                           VoidwrightModActivation.FileName, typeof(MySessionComponentBase)))
                    writer.Write(modActivationText);
                using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(
                           VoidwrightModActivation.FileName, typeof(MySessionComponentBase)))
                    if (!string.Equals(modActivationText, reader.ReadToEnd(), StringComparison.Ordinal))
                    {
                        modHandshakeReason = "host activation readback differed from its verified command";
                        LogModActivationFailure(modHandshakeReason);
                        return false;
                    }
                modActivationWritten = true;
                modHandshakeReason = "host-verified activation written to isolated world storage";
                MyLog.Default.WriteLineAndConsole("[VRageCage][PREFLIGHT] Host activation written for exact run, fixture, operation, controller, camera, and command bindings.");
                return true;
            }
            catch (Exception error)
            {
                modHandshakeReason = "host activation failed closed: " + error.GetType().Name + ": " + error.Message;
                LogModActivationFailure(modHandshakeReason);
                return false;
            }
        }

        private void LogModActivationFailure(string reason)
        {
            if (modActivationFailureLogged) return;
            MyLog.Default.WriteLineAndConsole("[VRageCage][PREFLIGHT] FAIL mod activation: " + reason);
            modActivationFailureLogged = true;
        }

        private bool HostDiscoveryMatches(PreflightRunContext context)
        {
            return HostDiscoveryMatches(context,
                Environment.GetEnvironmentVariable("VRAGECAGE_LAB_SESSION_ID"),
                Environment.GetEnvironmentVariable("VRAGECAGE_LAB_DISCOVERY_NONCE"));
        }

        private bool HostDiscoveryMatches(PreflightRunContext context, string expectedSessionId,
            string expectedBootstrapNonce)
        {
            return HostDiscoveryMatches(context, expectedSessionId, expectedBootstrapNonce, null);
        }

        private bool HostDiscoveryMatches(PreflightRunContext context, string expectedSessionId,
            string expectedBootstrapNonce, string expectedSuspensionIds)
        {
            if (context == null || MyAPIGateway.Utilities == null || MyAPIGateway.Entities == null ||
                context.GridId <= 0 || context.AgentCoreId <= 0 || context.ControllerId <= 0 || context.DepthCameraId <= 0 ||
                !IsHexIdentity(context.DiscoveryRunId) || context.DiscoveryTick < 0 ||
                !IsHexDigest(context.DiscoveryFingerprint)) return false;
            try
            {
                string json;
                using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(
                           "agent-core-discovery-v1.json", typeof(MySessionComponentBase)))
                    json = reader.ReadToEnd();
                if (string.IsNullOrEmpty(json) || Encoding.UTF8.GetByteCount(json) > 16 * 1024 * 1024) return false;
                var liveFingerprint = Sha256Bytes(Encoding.UTF8.GetBytes(json));
                if (json.IndexOf("\"agentsTruncated\":false", StringComparison.Ordinal) < 0 ||
                    json.IndexOf("\"physicalGridInventoryComplete\":true", StringComparison.Ordinal) < 0 ||
                    json.IndexOf("\"typedBlockInventoryComplete\":true", StringComparison.Ordinal) < 0 ||
                    json.IndexOf("\"mechanicalConnectionInventoryComplete\":true", StringComparison.Ordinal) < 0 ||
                    json.IndexOf("\"coreEntityId\":" + context.AgentCoreId.ToString(CultureInfo.InvariantCulture) + ",", StringComparison.Ordinal) < 0 ||
                    json.IndexOf("\"anchorGridEntityId\":" + context.GridId.ToString(CultureInfo.InvariantCulture) + ",", StringComparison.Ordinal) < 0 ||
                    json.IndexOf("\"entityId\":" + context.GridId.ToString(CultureInfo.InvariantCulture) + ",\"name\":", StringComparison.Ordinal) < 0)
                    return false;
                if (!string.IsNullOrEmpty(expectedSessionId) &&
                    json.IndexOf("\"sessionId\":\"" + expectedSessionId + "\"", StringComparison.Ordinal) < 0) return false;
                if (!string.IsNullOrEmpty(expectedBootstrapNonce) &&
                    (json.IndexOf("\"labRunId\":\"" + context.RunId + "\"", StringComparison.Ordinal) < 0 ||
                     json.IndexOf("\"fixtureId\":\"" + context.FixtureId + "\"", StringComparison.Ordinal) < 0 ||
                     json.IndexOf("\"bootstrapNonce\":\"" + expectedBootstrapNonce + "\"", StringComparison.Ordinal) < 0)) return false;
                var runMatch = System.Text.RegularExpressions.Regex.Match(json, "\\\"runId\\\":\\\"([0-9a-f]{32})\\\"");
                var tickMatch = System.Text.RegularExpressions.Regex.Match(json, "\\\"capturedSimulationTick\\\":([0-9]+)");
                var capturedMatch = System.Text.RegularExpressions.Regex.Match(json, "\\\"capturedAtUtc\\\":\\\"([^\\\"]+)\\\"");
                long tick;
                DateTime captured;
                if (!runMatch.Success ||
                    !tickMatch.Success || !long.TryParse(tickMatch.Groups[1].Value, NumberStyles.None,
                        CultureInfo.InvariantCulture, out tick) ||
                    !capturedMatch.Success || !DateTime.TryParse(capturedMatch.Groups[1].Value,
                        CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out captured) || !DirectManeuverCommandAcceptancePolicy.IsDiscoveryFresh(
                            captured, DateTime.UtcNow, TimeSpan.FromSeconds(15)))
                    return false;
                MyEntity gridEntity, controllerEntity, cameraEntity, coreEntity;
                if (!MyEntities.TryGetEntityById(context.GridId, out gridEntity) ||
                    !MyEntities.TryGetEntityById(context.ControllerId, out controllerEntity) ||
                    !MyEntities.TryGetEntityById(context.DepthCameraId, out cameraEntity) ||
                    !MyEntities.TryGetEntityById(context.AgentCoreId, out coreEntity)) return false;
                var grid = gridEntity as MyCubeGrid;
                var controller = controllerEntity as MyShipController;
                var camera = cameraEntity as IMyCameraBlock;
                var core = coreEntity as IMyUpgradeModule;
                if (grid == null || controller == null || camera == null || core == null ||
                    grid.EntityId != context.GridId ||
                    controller.CubeGrid != grid || camera.CubeGrid == null || camera.CubeGrid.EntityId != grid.EntityId ||
                    core.CubeGrid == null || core.CubeGrid.EntityId != grid.EntityId ||
                    (core.BlockDefinition.SubtypeName != "VoidwrightNavigationControllerSmall" &&
                     core.BlockDefinition.SubtypeName != "VoidwrightNavigationControllerLarge")) return false;
                return DirectManeuverCommandAcceptancePolicy.MatchesFreshDiscoveryAdvance(
                    runMatch.Groups[1].Value, tick, liveFingerprint, grid.EntityId, controller.EntityId, core.EntityId,
                    context.DiscoveryRunId, context.DiscoveryTick, context.DiscoveryFingerprint,
                    context.GridId, context.ControllerId, context.AgentCoreId) &&
                    (expectedSuspensionIds == null ||
                     HostDiscoverySuspensionsMatch(json, context.GridId, expectedSuspensionIds));
            }
            catch { return false; }
        }

        private static bool HostDiscoverySuspensionsMatch(string json, long gridId, string suspensionIds)
        {
            if (string.IsNullOrEmpty(json) || gridId <= 0 || string.IsNullOrEmpty(suspensionIds)) return false;
            var expected = new HashSet<long>();
            foreach (var value in suspensionIds.Split(','))
            {
                long id;
                if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out id) ||
                    id <= 0 || !expected.Add(id)) return false;
            }
            var pattern = "\\\"gridEntityId\\\":" + gridId.ToString(CultureInfo.InvariantCulture) +
                ",\\\"entityId\\\":([0-9]+),\\\"definitionType\\\":\\\"[^\\\"]*\\\",\\\"subtypeId\\\":\\\"[^\\\"]*\\\",\\\"typedKind\\\":\\\"suspension\\\"";
            var actual = new HashSet<long>();
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(json, pattern))
            {
                long id;
                if (!long.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out id) ||
                    id <= 0 || !actual.Add(id)) return false;
            }
            return expected.Count > 0 && expected.SetEquals(actual);
        }

        private void ReadModHeartbeat()
        {
            modHeartbeatFresh = false;
            modHeartbeat = null;
            try
            {
                VoidwrightModActivation activation;
                if (!VoidwrightModActivation.TryParse(modActivationText, DateTime.UtcNow, out activation) ||
                    activation.RunId != Environment.GetEnvironmentVariable("VRAGECAGE_LAB_RUN_ID") ||
                    activation.FixtureId != Environment.GetEnvironmentVariable("VRAGECAGE_LAB_FIXTURE_ID") ||
                    activation.OperationId != operationId || activation.ControllerId != controllerId ||
                    activation.CameraId != navigationDepthCameraId ||
                    ((commandKind == "direct-maneuver" || commandKind == "preflight-only") &&
                    (activation.Action != commandKind || activation.GridId != expectedGridId ||
                     activation.AgentCoreId != expectedAgentCoreId || activation.DiscoveryRunId != expectedDiscoveryRunId ||
                     activation.DiscoveryTick != expectedDiscoveryTick || activation.DiscoveryFingerprint != expectedDiscoveryFingerprint)) ||
                    (commandKind != "direct-maneuver" && commandKind != "preflight-only" && activation.Action != "drive-to-coordinate-depth"))
                { modHandshakeReason = "host activation expired or no longer matches this command"; return; }
                string serialized;
                using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(
                           VoidwrightPreflightHeartbeat.FileName, typeof(MySessionComponentBase)))
                    serialized = reader.ReadToEnd();
                VoidwrightPreflightHeartbeat heartbeat;
                if (!VoidwrightPreflightHeartbeat.TryParse(serialized, out heartbeat))
                { modHandshakeReason = "mod heartbeat is malformed"; return; }
                MyEntity controllerEntity;
                MyEntity cameraEntity;
                var foundController = MyEntities.TryGetEntityById(controllerId, out controllerEntity);
                var foundCamera = MyEntities.TryGetEntityById(navigationDepthCameraId, out cameraEntity);
                var observedController = foundController ? controllerEntity as MyShipController : null;
                var observedCamera = foundCamera ? cameraEntity as IMyCameraBlock : null;
                if (observedController == null || observedController.CubeGrid == null ||
                    observedCamera == null || observedCamera.CubeGrid == null ||
                    observedController.CubeGrid.EntityId != observedCamera.CubeGrid.EntityId ||
                    (modSessionId != null && modSessionId != heartbeat.SessionId) ||
                    !heartbeat.IsFreshAndBound(DateTime.UtcNow, DirectModHeartbeatMaximumAge,
                        Environment.GetEnvironmentVariable("VRAGECAGE_LAB_RUN_ID"),
                        Environment.GetEnvironmentVariable("VRAGECAGE_LAB_FIXTURE_ID"), operationId,
                        Environment.GetEnvironmentVariable("VRAGECAGE_LAB_PREFLIGHT_CONTEXT_SHA256"),
                        Environment.GetEnvironmentVariable("VRAGECAGE_LAB_PREFLIGHT_CHALLENGE"),
                        heartbeat.SessionId, controllerId, observedController.CubeGrid.EntityId,
                        navigationDepthCameraId,
                        expectedAgentCoreId, expectedDiscoveryRunId, expectedDiscoveryTick, expectedDiscoveryFingerprint) ||
                    DateTime.UtcNow - heartbeat.SensorScanUtc > TimeSpan.FromSeconds(1) ||
                    heartbeat.SimulationTick - heartbeat.SensorScanTick > 20 ||
                    !heartbeat.ActuatorReady || !heartbeat.SensorValid ||
                    heartbeat.WheelCount == 0 || heartbeat.SteeringWheelCount == 0 || heartbeat.PropulsionWheelCount == 0)
                { modHandshakeReason = "mod heartbeat failed freshness, sensor, wheel, session, or exact entity binding"; return; }
                modSessionId = heartbeat.SessionId;
                modHeartbeat = heartbeat;
                modHeartbeatFresh = true;
                modHandshakeReason = commandKind == "direct-maneuver" && directManeuverBroker != null &&
                    directManeuverIdentity != null && !string.Equals(heartbeat.OperationCapabilityToken,
                        directManeuverBroker.GetMovementCapabilityToken(directManeuverIdentity, DateTime.UtcNow),
                        StringComparison.Ordinal)
                    ? "fresh run-bound mod heartbeat is waiting for the current host capability echo"
                    : "fresh mod heartbeat matched host-verified run and exact vehicle binding";
            }
            catch (Exception error)
            {
                modHandshakeReason = "host heartbeat validation failed: " + error.GetType().Name;
            }
        }

        private bool DirectManeuverControllerStateReady(MyShipController target, out string evidence)
        {
            try { return DirectManeuverControllerStateReadyCore(target, out evidence); }
            catch (Exception error)
            {
                evidence = "controller state read failed: " + error.GetType().Name;
                return false;
            }
        }

        private bool DirectManeuverControllerStateReadyCore(MyShipController target, out string evidence)
        {
            evidence = "unknown";
            if (target == null || target.MarkedForClose || target.CubeGrid == null)
            { evidence = "controller or grid unavailable"; return false; }
            if (!EnsureDirectControllerEventBindings(target.CubeGrid))
            { evidence = "controller acquisition/release event binding unavailable"; return false; }
            var ingame = target as Sandbox.ModAPI.Ingame.IMyShipController;
            var infoKnown = target.ControllerInfo != null;
            var controllingIdentityId = infoKnown ? target.ControllerInfo.ControllingIdentityId : -1;
            var underControl = ingame == null || ingame.IsUnderControl;
            var stable = infoKnown && !underControl && controllingIdentityId == 0 && target.Pilot == null &&
                directControllerInventoryComplete && !directOtherControllerUnknownOrActive &&
                System.Threading.Volatile.Read(ref directControllerTransitionObserved) == 0;
            evidence = "targetController=" + target.EntityId.ToString(CultureInfo.InvariantCulture) +
                ";underControl=" +
                (ingame == null ? "unknown" : ingame.IsUnderControl.ToString()) +
                ";controllingIdentity=" + controllingIdentityId.ToString(CultureInfo.InvariantCulture) +
                ";pilot=" + (target.Pilot == null ? "empty" : "occupied") +
                ";otherControllerUnknownOrActive=" + directOtherControllerUnknownOrActive +
                ";controllerInventoryComplete=" + directControllerInventoryComplete +
                ";reconcileIntervalSeconds=1" +
                ";controlTransitionObserved=" + (System.Threading.Volatile.Read(ref directControllerTransitionObserved) != 0);
            return stable;
        }

        private bool EnsureDirectControllerEventBindings(MyCubeGrid grid)
        {
            if (grid == null) return false;
            if (directControllerEventGridId != 0 && directControllerEventGridId != grid.EntityId)
                ClearDirectControllerEventBindings();
            directControllerEventGridId = grid.EntityId;
            var now = DateTime.UtcNow;
            if (directControllerInventoryComplete && now < directControllerReconcileAfterUtc)
                return directControllerEventBindings.ContainsKey(controllerId);
            var seen = new HashSet<long>();
            var otherControllerUnknownOrActive = false;
            foreach (var slim in grid.GetBlocks())
            {
                var block = slim == null ? null : slim.FatBlock as MyShipController;
                if (block == null || block.MarkedForClose) continue;
                if (block.EntityId != controllerId)
                {
                    var otherApi = block as Sandbox.ModAPI.Ingame.IMyShipController;
                    if (block.ControllerInfo == null || otherApi == null || otherApi.IsUnderControl ||
                        block.ControllerInfo.ControllingIdentityId != 0 || block.Pilot != null)
                        otherControllerUnknownOrActive = true;
                }
                if (block.ControllerInfo == null) continue;
                seen.Add(block.EntityId);
                if (directControllerEventBindings.ContainsKey(block.EntityId)) continue;
                var binding = new DirectControllerEventBinding { Info = block.ControllerInfo };
                binding.Acquired = _ => System.Threading.Interlocked.Exchange(ref directControllerTransitionObserved, 1);
                binding.Released = _ => System.Threading.Interlocked.Exchange(ref directControllerTransitionObserved, 1);
                try
                {
                    binding.Info.ControlAcquired += binding.Acquired;
                    binding.Info.ControlReleased += binding.Released;
                    directControllerEventBindings.Add(block.EntityId, binding);
                }
                catch { return false; }
            }
            var removed = new List<long>();
            foreach (var id in directControllerEventBindings.Keys)
                if (!seen.Contains(id)) removed.Add(id);
            foreach (var id in removed)
            {
                var binding = directControllerEventBindings[id];
                try
                {
                    binding.Info.ControlAcquired -= binding.Acquired;
                    binding.Info.ControlReleased -= binding.Released;
                }
                catch { }
                directControllerEventBindings.Remove(id);
                if (directManeuverExecutor != null)
                    System.Threading.Interlocked.Exchange(ref directControllerTransitionObserved, 1);
            }
            directOtherControllerUnknownOrActive = otherControllerUnknownOrActive;
            directControllerInventoryComplete = true;
            directControllerReconcileAfterUtc = now.AddSeconds(1);
            return seen.Contains(controllerId);
        }

        private void ClearDirectControllerEventBindings()
        {
            foreach (var binding in directControllerEventBindings.Values)
            {
                try
                {
                    binding.Info.ControlAcquired -= binding.Acquired;
                    binding.Info.ControlReleased -= binding.Released;
                }
                catch { }
            }
            directControllerEventBindings.Clear();
            directControllerEventGridId = 0;
            directControllerReconcileAfterUtc = DateTime.MinValue;
            directControllerInventoryComplete = false;
            directOtherControllerUnknownOrActive = true;
        }

        private void ReadModSessionLiveness(DateTime nowUtc)
        {
            modSessionLivenessFresh = false;
            modSessionLiveness = null;
            modSessionLivenessFailure = "unavailable";
            try
            {
                string serialized;
                using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(
                           VoidwrightSessionLiveness.FileName, typeof(MySessionComponentBase)))
                    serialized = reader.ReadToEnd();
                VoidwrightSessionLiveness liveness;
                if (!VoidwrightSessionLiveness.TryParse(serialized, out liveness))
                { modSessionLivenessFailure = "malformed"; return; }
                if (!liveness.IsFresh(nowUtc, DirectModHeartbeatMaximumAge, modSessionId))
                { modSessionLivenessFailure = "stale-or-session-mismatch"; return; }
                modSessionLiveness = liveness;
                modSessionLivenessFresh = true;
                if (modHeartbeatFresh) modSessionLivenessFailure = "none";
                else modSessionLivenessFailure = liveness.PreflightState == "running"
                    ? "preflight-heartbeat-not-fresh"
                    : "preflight-terminal-" + liveness.PreflightState;
            }
            catch (Exception error)
            {
                modSessionLivenessFailure = "read-failed-" + error.GetType().Name;
            }
        }

        private bool ReadModPreflightAttestation(DateTime nowUtc)
        {
            modPreflightPass = false;
            modPreflightCapturedUtc = DateTime.MinValue;
            try
            {
                var fileName = "voidwright-preflight-evidence-" +
                    (Environment.GetEnvironmentVariable("VRAGECAGE_LAB_RUN_ID") ?? string.Empty) + "-" + operationId + ".json";
                string json;
                using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(fileName, typeof(MySessionComponentBase)))
                    json = reader.ReadToEnd();
                if (string.IsNullOrEmpty(json) || json.Length > 32768 ||
                    !json.Contains("\"schema\":\"vragecage.movement-preflight.v1\"") ||
                    !json.Contains("\"run_id\":\"" + (Environment.GetEnvironmentVariable("VRAGECAGE_LAB_RUN_ID") ?? string.Empty) + "\"") ||
                    !json.Contains("\"operation_id\":\"" + operationId + "\"") ||
                    !json.Contains("\"invalidated\":false") ||
                    !json.Contains("\"summary\":{\"passing\":10,\"total\":10,\"success\":true}")) return false;
                var createdMatch = System.Text.RegularExpressions.Regex.Match(json,
                    "\\\"created_at_utc\\\":\\\"([0-9T:.Z-]+)\\\"");
                if (!createdMatch.Success || !DateTime.TryParseExact(createdMatch.Groups[1].Value,
                    "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out modPreflightCapturedUtc) ||
                    modPreflightCapturedUtc > nowUtc || nowUtc - modPreflightCapturedUtc > DirectModHeartbeatMaximumAge) return false;
                var names = new HashSet<string>(StringComparer.Ordinal);
                var checks = System.Text.RegularExpressions.Regex.Matches(json,
                    "\\{\\\"name\\\":\\\"([a-z_]+)\\\",\\\"status\\\":\\\"(PASS|FAIL|UNKNOWN)\\\"[^}]*\\\"timestamp_utc\\\":\\\"([0-9T:.Z-]+)\\\"\\}");
                if (checks.Count != MovementPreflight.ExpectedCheckCount) return false;
                foreach (System.Text.RegularExpressions.Match match in checks)
                {
                    if (match.Groups[2].Value != "PASS" || !names.Add(match.Groups[1].Value)) return false;
                    DateTime captured;
                    if (!DateTime.TryParseExact(match.Groups[3].Value, "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
                        CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out captured) ||
                        captured > nowUtc || nowUtc - captured > DirectModHeartbeatMaximumAge) return false;
                }
                modPreflightPass = true;
            }
            catch { modPreflightPass = false; }
            return modPreflightPass;
        }

        private bool WriteModApproval()
        {
            if (!modHeartbeatFresh || modHeartbeat == null) return false;
            var heartbeatKey = string.Join("\n", new[]
            {
                modHeartbeat.RunId, modHeartbeat.FixtureId, modHeartbeat.OperationId,
                modHeartbeat.ContextSha256, modHeartbeat.Challenge, modHeartbeat.SessionId,
                modHeartbeat.ControllerId.ToString(CultureInfo.InvariantCulture),
                modHeartbeat.GridId.ToString(CultureInfo.InvariantCulture),
                modHeartbeat.CameraId.ToString(CultureInfo.InvariantCulture),
                modHeartbeat.SimulationTick.ToString(CultureInfo.InvariantCulture),
                modHeartbeat.SensorScanTick.ToString(CultureInfo.InvariantCulture),
                modHeartbeat.AgentCoreId.ToString(CultureInfo.InvariantCulture),
                modHeartbeat.DiscoveryRunId,
                modHeartbeat.DiscoveryTick.ToString(CultureInfo.InvariantCulture),
                modHeartbeat.DiscoveryFingerprint
            });
            if (modApprovalStateKnown && modApprovalFilePresent &&
                lastModApprovalHeartbeatKey == heartbeatKey) return true;
            var nowUtc = DateTime.UtcNow;
            if (nowUtc - lastModApprovalWriteAttemptUtc < TimeSpan.FromMilliseconds(250)) return false;
            lastModApprovalWriteAttemptUtc = nowUtc;
            try
            {
                var approval = new VoidwrightPreflightApproval
                {
                    RunId = modHeartbeat.RunId, FixtureId = modHeartbeat.FixtureId, OperationId = modHeartbeat.OperationId,
                    ContextSha256 = modHeartbeat.ContextSha256, Challenge = modHeartbeat.Challenge,
                    SessionId = modHeartbeat.SessionId, ControllerId = modHeartbeat.ControllerId,
                    GridId = modHeartbeat.GridId, CameraId = modHeartbeat.CameraId,
                    SimulationTick = modHeartbeat.SimulationTick, SensorScanTick = modHeartbeat.SensorScanTick,
                    AgentCoreId = modHeartbeat.AgentCoreId, DiscoveryRunId = modHeartbeat.DiscoveryRunId,
                    DiscoveryTick = modHeartbeat.DiscoveryTick, DiscoveryFingerprint = modHeartbeat.DiscoveryFingerprint,
                    PublishedUtc = nowUtc
                };
                var serialized = approval.Format();
                using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(
                           VoidwrightPreflightApproval.FileName, typeof(MySessionComponentBase)))
                    writer.Write(serialized);
                using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(
                           VoidwrightPreflightApproval.FileName, typeof(MySessionComponentBase)))
                    if (!string.Equals(serialized, reader.ReadToEnd(), StringComparison.Ordinal))
                        throw new InvalidOperationException("approval readback mismatch");
                modHandshakeReason = "all host preflight checks passed; exact heartbeat was acknowledged";
                modApprovalStateKnown = true;
                modApprovalFilePresent = true;
                lastModApprovalHeartbeatKey = heartbeatKey;
                lastModApprovalRevokeAttemptUtc = DateTime.MinValue;
                return true;
            }
            catch (Exception error)
            {
                modHeartbeatFresh = false;
                modHandshakeReason = "host approval failed closed: " + error.GetType().Name;
                RevokeModApproval();
                return false;
            }
        }

        private void RevokeModApproval()
        {
            if (modApprovalStateKnown && !modApprovalFilePresent) return;
            var nowUtc = DateTime.UtcNow;
            if (nowUtc - lastModApprovalRevokeAttemptUtc < TimeSpan.FromMilliseconds(250)) return;
            lastModApprovalRevokeAttemptUtc = nowUtc;
            try
            {
                using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(
                           VoidwrightPreflightApproval.FileName, typeof(MySessionComponentBase)))
                    writer.Write(string.Empty);
                modApprovalStateKnown = true;
                modApprovalFilePresent = false;
                lastModApprovalHeartbeatKey = null;
            }
            catch { /* The mod-side acknowledgement expires within one second. */ }
        }

        private void BlockMovementForPreflight(MovementPreflight preflight)
        {
            var persisted = preflight != null && PersistMovementPreflight(preflight);
            if (!persisted)
                MyLog.Default.WriteLineAndConsole(
                    "[VRageCage][PREFLIGHT] UNKNOWN movement blocked; machine-readable result could not be persisted.");
            if (preflight != null) LogMovementPreflight(preflight);
            // The depth-navigation command is owned by the ordinary Voidwright mod adapter.
            // The legacy plugin still records its explicit denial, but must not clear or touch
            // IMyMotorSuspension state that the authorized adapter may own.
            if (commandKind == "drive-to-coordinate-depth" &&
                Environment.GetEnvironmentVariable("VRAGECAGE_LAB_MOTION_OWNER") == "voidwright-mod")
            {
                MyLog.Default.WriteLineAndConsole(
                    "[VRageCage][PREFLIGHT] Magnetar actuator path denied without wheel writes; ordinary Voidwright adapter owns this run.");
                terminal = true;
                LogMovementPreflightTerminalEvent();
                return;
            }
            if (commandKind == "direct-maneuver")
            {
                // No actuator cleanup here: without a proven owner token, a
                // neutralizing write could overwrite another writer's value.
                terminal = true;
                LogMovementPreflightTerminalEvent();
                return;
            }
            StopRecordedRouteSystems();
            StopStatorMotion();
            Neutralize();
            terminal = true;
            LogMovementPreflightTerminalEvent();
        }

        private void LogMovementPreflightTerminalEvent()
        {
            if (string.IsNullOrEmpty(operationId)) return;
            var modOwnedDepth = commandKind == "drive-to-coordinate-depth" &&
                Environment.GetEnvironmentVariable("VRAGECAGE_LAB_MOTION_OWNER") == "voidwright-mod";
            var eventType = commandKind == "drive-to-coordinate-depth" ? "depth-controller" : "server-control";
            var ticks = modOwnedDepth ? modPreflightElapsedTicks : elapsedTicks;
            MyLog.Default.WriteLineAndConsole("Voidwright evidence: type=" + eventType + " operation=" + operationId +
                " controllerId=" + controllerId +
                (commandKind == "drive-to-coordinate-depth" ? " cameraId=" + navigationDepthCameraId : string.Empty) +
                " status=preflight-blocked elapsedTicks=" + ticks + " timeoutTicks=" + timeoutTicks +
                " actuatorDispatch=none reason=preflight_gate_denied");
        }

        private void UpdateMovementCommandCore()
        {
            if (commandKind == "rotate-stator")
            {
                UpdateStatorRotation();
                return;
            }
            if (commandKind == "play-recorded-route")
            {
                UpdateRecordedRoute();
                return;
            }
            if (commandKind == "drive-recorded-route")
            {
                UpdateRouteFollowing();
                return;
            }
            if (controllerId == 0) return;
            elapsedTicks++;
            if (!started && !TryAcquireController())
            {
                if (elapsedTicks >= timeoutTicks) Finish("controller-unavailable", 0);
                return;
            }
            if (controller == null || controller.MarkedForClose || controller.GridWheels == null)
            {
                Finish("controller-lost", Displacement());
                return;
            }

            if (IsCoordinateNavigation())
            {
                NavigateToTarget();
                return;
            }

            var displacement = Displacement();
            var forwardDisplacement = ForwardDisplacement();
            var travelDisplacement = driveReverse ? -forwardDisplacement : forwardDisplacement;
            var velocity = controller.CubeGrid.Physics == null
                ? Vector3D.Zero : new Vector3D(controller.CubeGrid.Physics.LinearVelocity);
            var speed = velocity.Length();
            var forwardSpeed = Vector3D.Dot(velocity, controller.WorldMatrix.Forward);
            var commandedSpeed = driveReverse ? -forwardSpeed : forwardSpeed;
            if (travelDisplacement <= -Math.Max(0.5, maxSpeed * 2.0))
            {
                Finish(driveReverse ? "wrong-direction" : "reverse-drift", displacement);
                return;
            }
            if (travelDisplacement >= distanceMeters)
            {
                controller.GridWheels.AngularVelocity = Vector3.Zero;
                controller.GridWheels.Brake = true;
                lastSteeringCommand = 0;
                if (speed <= 0.05)
                {
                    var overshootLimit = distanceMeters + Math.Max(0.5, maxSpeed * 2.0);
                    Finish(travelDisplacement <= overshootLimit ? "complete" : "overshoot", displacement);
                    return;
                }
                if (elapsedTicks >= timeoutTicks)
                {
                    Finish("settle-timeout", displacement);
                    return;
                }
                if (elapsedTicks % 60 == 0)
                    Evidence("settling", displacement, speed);
                return;
            }
            if (elapsedTicks >= timeoutTicks)
            {
                Finish("timeout", displacement);
                return;
            }

            var speedMargin = Math.Max(0.05, maxSpeed * 0.15);
            controller.GridWheels.Brake = speed > maxSpeed + speedMargin;
            lastForwardWheelInput = ForwardWheelInput();
            var remainingDistance = Math.Max(0, distanceMeters - travelDisplacement);
            var targetSpeed = Math.Min(maxSpeed, Math.Sqrt(0.5 * remainingDistance));
            var speedError = targetSpeed - commandedSpeed;
            var pulseDuty = Clamp(speedError * 2.0);
            if (controller.GridWheels.Brake || pulseDuty < 0.025) pulseDuty = 0;
            var pulseTicks = (int)Math.Round(pulseDuty * 10.0);
            var wheelDirection = driveReverse ? -lastForwardWheelInput : lastForwardWheelInput;
            var throttle = pulseTicks > 0 && elapsedTicks % 10 < pulseTicks
                ? (float)wheelDirection : 0f;
            var up = controller.GetTotalGravity();
            if (up.LengthSquared() > 0.0001)
            {
                up.Normalize();
                up = -up;
            }
            else
            {
                up = controller.CubeGrid.WorldMatrix.Up;
            }
            var currentForward = controller.WorldMatrix.Forward;
            currentForward -= up * Vector3D.Dot(currentForward, up);
            if (currentForward.LengthSquared() > 0.0001) currentForward.Normalize();
            var right = Vector3D.Cross(currentForward, up);
            if (right.LengthSquared() > 0.0001) right.Normalize();
            var headingError = Math.Atan2(Vector3D.Dot(startForward, right),
                Vector3D.Dot(startForward, currentForward));
            var steering = driveReverse || Math.Abs(headingError) < 0.08
                ? 0 : Clamp(headingError / (Math.PI / 2));
            lastSteeringCommand = steering;
            lastSteeringPolarity = 1;
            controller.GridWheels.AngularVelocity = new Vector3((float)steering, 0, throttle);
            if (elapsedTicks % 60 == 0)
                Evidence("running", displacement, speed);
        }

        private bool IsMovementCommand()
        {
            return commandKind == "direct-maneuver" || commandKind == "preflight-only" || commandKind == "rotate-stator" || commandKind == "play-recorded-route" ||
                commandKind == "drive-recorded-route" || commandKind == "drive-distance" ||
                IsCoordinateNavigation();
        }

        private static bool AuditDirectManeuverWriters(MyShipController target, out string evidence)
        {
            var programmableBlocks = 0;
            var runningProgrammableBlocks = 0;
            var movementBlocks = 0;
            var activeAutopilots = 0;
            var activeAiBehaviors = 0;
            var controlledSeats = 0;
            if (target == null || target.CubeGrid == null)
            {
                evidence = "grid=unavailable";
                return false;
            }
            foreach (var slim in target.CubeGrid.GetBlocks())
            {
                var block = slim == null ? null : slim.FatBlock;
                if (block == null) continue;
                var programmable = block as Sandbox.ModAPI.Ingame.IMyProgrammableBlock;
                if (programmable != null)
                {
                    programmableBlocks++;
                    if (programmable.IsRunning) runningProgrammableBlocks++;
                }
                var movement = block as Sandbox.ModAPI.Ingame.IMyFlightMovementBlock;
                if (movement != null)
                {
                    movementBlocks++;
                    if (movement.IsAutoPilotEnabled) activeAutopilots++;
                }
                var shipController = block as Sandbox.ModAPI.Ingame.IMyShipController;
                if (shipController != null && shipController.IsUnderControl) controlledSeats++;
                MyAiBlockComponent ai;
                if (block.Components.TryGet<MyAiBlockComponent>(out ai) &&
                    ai.AiBlockType == VRage.Game.ModAPI.MyAiBlockType.Movement && ai.IsActivated)
                    activeAiBehaviors++;
            }
            evidence = "grid=" + target.CubeGrid.EntityId.ToString(CultureInfo.InvariantCulture) +
                ";programmableBlocks=" + programmableBlocks + ";programmableRunning=" + runningProgrammableBlocks +
                ";movementBlocks=" + movementBlocks + ";autopilotActive=" + activeAutopilots +
                ";aiBehaviorActive=" + activeAiBehaviors + ";controlledSeats=" + controlledSeats +
                ";ownership=cooperative-server-plugin;externalCAS=unavailable";
            return runningProgrammableBlocks == 0 && activeAutopilots == 0 &&
                activeAiBehaviors == 0 && controlledSeats == 0;
        }

        private static bool AuditLocalVehicleTestAssemblyWriters(MyShipController target,
            IList<Sandbox.Game.Entities.Cube.MyMotorSuspension> wheels, out string evidence)
        {
            if (target == null || target.CubeGrid == null || wheels == null || wheels.Count != 4)
            { evidence = "assembly=incomplete;externalWriters=not_excluded"; return false; }
            var grids = new List<Sandbox.Game.Entities.MyCubeGrid>();
            var gridIds = new HashSet<long>();
            grids.Add(target.CubeGrid);
            gridIds.Add(target.CubeGrid.EntityId);
            foreach (var wheel in wheels)
            {
                var grid = wheel == null ? null : wheel.RotorGrid;
                if (grid == null || !gridIds.Add(grid.EntityId))
                { evidence = "assembly=duplicate_or_missing_rotor_grid;externalWriters=not_excluded"; return false; }
                grids.Add(grid);
            }
            if (grids.Count != 5)
            { evidence = "assemblyGridCount=" + grids.Count + ";externalWriters=not_excluded"; return false; }
            var modsMatch = LocalVehicleTestModsMatchAllowlist(out var modEvidence);
            var programmableBlocks = 0;
            var runningProgrammableBlocks = 0;
            var movementBlocks = 0;
            var activeAutopilots = 0;
            var activeAiBehaviors = 0;
            var controlledSeats = 0;
            var remoteControls = 0;
            var perGrid = new List<string>();
            var blockDefinitions = new List<string>();
            var blocksScanned = 0;
            foreach (var grid in grids)
            {
                var localProgrammable = 0;
                var localRunning = 0;
                foreach (var slim in grid.GetBlocks())
                {
                    var block = slim == null ? null : slim.FatBlock;
                    if (block == null) continue;
                    blocksScanned++;
                    blockDefinitions.Add(grid.EntityId.ToString(CultureInfo.InvariantCulture) + ":" +
                        block.BlockDefinition.Id.ToString());
                    var programmable = block as Sandbox.ModAPI.Ingame.IMyProgrammableBlock;
                    if (programmable != null)
                    {
                        programmableBlocks++; localProgrammable++;
                        if (programmable.IsRunning) { runningProgrammableBlocks++; localRunning++; }
                    }
                    var movement = block as Sandbox.ModAPI.Ingame.IMyFlightMovementBlock;
                    if (movement != null)
                    {
                        movementBlocks++;
                        if (movement.IsAutoPilotEnabled) activeAutopilots++;
                    }
                    var shipController = block as Sandbox.ModAPI.Ingame.IMyShipController;
                    if (shipController != null && shipController.IsUnderControl) controlledSeats++;
                    if (block is Sandbox.ModAPI.IMyRemoteControl) remoteControls++;
                    MyAiBlockComponent ai;
                    if (block.Components.TryGet<MyAiBlockComponent>(out ai) &&
                        ai.AiBlockType == VRage.Game.ModAPI.MyAiBlockType.Movement && ai.IsActivated)
                        activeAiBehaviors++;
                }
                perGrid.Add(grid.EntityId.ToString(CultureInfo.InvariantCulture) + ":pb=" +
                    localProgrammable + ":pbRunning=" + localRunning);
            }
            blockDefinitions.Sort(StringComparer.Ordinal);
            var blockManifest = Sha256Bytes(Encoding.UTF8.GetBytes(string.Join("\n", blockDefinitions.ToArray())));
            evidence = "assemblyGrids=" + string.Join(",", gridIds) +
                ";perGrid=" + string.Join(";", perGrid.ToArray()) +
                ";blocksScanned=" + blocksScanned + ";blockDefinitionManifestSha256=" + blockManifest +
                ";programmableBlocks=" + programmableBlocks + ";programmableRunning=" + runningProgrammableBlocks +
                ";movementBlocks=" + movementBlocks + ";autopilotActive=" + activeAutopilots +
                ";aiBehaviorActive=" + activeAiBehaviors + ";controlledSeats=" + controlledSeats +
                ";remoteControls=" + remoteControls +
                ";fixtureModAllowlist=none" +
                ";mods=" + modEvidence.Replace(' ', '_') +
                ";fixtureScriptAudit=programmable-block-inventory;externalWriters=not_excluded";
            return modsMatch && programmableBlocks == 0 && runningProgrammableBlocks == 0 && activeAutopilots == 0 &&
                activeAiBehaviors == 0 && controlledSeats == 0 && remoteControls == 0;
        }

        private static bool LocalVehicleTestModsMatchAllowlist(out string evidence)
        {
            try
            {
                var worldAccessors = Array.FindAll(MySession.Static.GetType().GetMethods(
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic), method => method.Name == "GetWorld" &&
                    method.GetParameters().Length == 0);
                if (worldAccessors.Length == 0)
                {
                    // The installed V4 server build exposes no parameterless
                    // GetWorld accessor. For this exact disposable fixture,
                    // VRageCage supplies the allowlist from its verified import
                    // receipt; compare it to the compiled fixture allowlist.
                    if (!string.Equals(Environment.GetEnvironmentVariable(
                            "VRAGECAGE_LOCAL_VEHICLE_TEST_INSTANCE"),
                            LocalVehicleTestBindingPolicy.FreshInstance, StringComparison.Ordinal))
                    { evidence = "unavailable_world_accessor_and_unpinned_instance"; return false; }
                    var configured = Environment.GetEnvironmentVariable(
                        "VRAGECAGE_LOCAL_VEHICLE_TEST_MOD_IDS");
                    var pinned = new List<ulong>();
                    foreach (var item in (configured ?? string.Empty).Split(','))
                    {
                        if (!ulong.TryParse(item, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                        { evidence = "invalid_host_pinned_mod_allowlist"; return false; }
                        pinned.Add(id);
                    }
                    var pinnedMatches = LocalVehicleTestBindingPolicy.HasExactModAllowlist(pinned);
                    evidence = "host_pinned:" + (configured ?? "missing");
                    return pinnedMatches;
                }
                if (worldAccessors.Length != 1)
                { evidence = "ambiguous_world_accessor_count_" + worldAccessors.Length; return false; }
                var world = worldAccessors[0].Invoke(MySession.Static, null);
                if (world == null) { evidence = "unavailable_world_null"; return false; }
                var checkpoint = world.GetType().GetProperty("Checkpoint").GetValue(world, null);
                var mods = checkpoint.GetType().GetProperty("Mods").GetValue(checkpoint, null)
                    as System.Collections.IEnumerable;
                if (mods == null) { evidence = "unavailable"; return false; }
                var observed = new List<ulong>();
                foreach (var item in mods)
                {
                    if (item == null) { evidence = "null_mod_entry"; return false; }
                    var value = item.GetType().GetProperty("PublishedFileId").GetValue(item, null);
                    observed.Add(Convert.ToUInt64(value, CultureInfo.InvariantCulture));
                }
                var matches = LocalVehicleTestBindingPolicy.HasExactModAllowlist(observed);
                evidence = string.Join(",", observed);
                return matches;
            }
            catch (Exception error)
            {
                evidence = "unavailable_" + error.GetType().Name;
                return false;
            }
        }

        private MovementPreflight BuildMovementPreflight()
        {
            var now = DateTime.UtcNow;
            var runId = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_RUN_ID") ?? string.Empty;
            var fixtureFingerprint = CurrentFixtureFingerprint();
            var stateFingerprint = CurrentMovementStateFingerprint();
            MyEntity entity;
            entity = null;
            var found = MySession.Static != null && controllerId > 0 && MyEntities.TryGetEntityById(controllerId, out entity);
            var observedController = found ? entity as MyShipController : null;
            MyEntity observedCameraEntity = null;
            var foundDepthCamera = navigationDepthCameraId > 0 && MyEntities.TryGetEntityById(navigationDepthCameraId, out observedCameraEntity);
            var observedDepthCamera = foundDepthCamera ? observedCameraEntity as IMyCameraBlock : null;
            var sameGridCamera = observedController != null && observedController.CubeGrid != null && observedDepthCamera != null &&
                !observedDepthCamera.Closed && observedDepthCamera.CubeGrid != null &&
                observedDepthCamera.CubeGrid.EntityId == observedController.CubeGrid.EntityId;
            var depthMode = commandKind == "drive-to-coordinate-depth";
            expectedGridId = DepthGridBindingPolicy.PinExpectedGrid(expectedGridId, depthMode,
                observedController != null && observedController.CubeGrid != null,
                observedController == null || observedController.CubeGrid == null ? 0 : observedController.CubeGrid.EntityId,
                sameGridCamera);
            var scanFresh = modHeartbeatFresh && modHeartbeat != null && modHeartbeat.SensorValid &&
                modHeartbeat.ValidRayCount == VoidwrightPreflightHeartbeat.RayCount && sameGridCamera &&
                modHeartbeat.GridId == observedController.CubeGrid.EntityId &&
                modHeartbeat.CameraId == navigationDepthCameraId;
            var position = observedController == null || observedController.CubeGrid == null || observedController.CubeGrid.Physics == null
                ? Vector3D.Zero : observedController.CubeGrid.Physics.CenterOfMassWorld;
            var positionValid = observedController != null && observedController.CubeGrid != null &&
                observedController.CubeGrid.Physics != null && Finite(position.X) && Finite(position.Y) && Finite(position.Z);
            var gridBrakeAvailable = observedController != null && observedController.GridWheels != null;
            var gridBrakeClear = gridBrakeAvailable && !observedController.GridWheels.HandBrake &&
                !observedController.GridWheels.Brake;
            var observedWheels = gridBrakeAvailable ? observedController.GridWheels.Wheels : null;
            var wheelBrakeStateAvailable = observedWheels != null && observedController.GridWheels.WheelCount > 0;
            var anyWheelBrakeActive = false;
            if (wheelBrakeStateAvailable)
                foreach (var wheel in observedWheels)
                {
                    if (wheel == null) { wheelBrakeStateAvailable = false; break; }
                    anyWheelBrakeActive |= wheel.Brake || wheel.Handbrake;
                }
            var brakeStatus = MovementPreflight.ResolveBrakeStatus(gridBrakeAvailable,
                gridBrakeAvailable && observedController.GridWheels.Brake,
                gridBrakeAvailable && observedController.GridWheels.HandBrake,
                wheelBrakeStateAvailable, anyWheelBrakeActive);
            var directManeuver = commandKind == "direct-maneuver";
            var preflightOnly = commandKind == "preflight-only";
            var sensorBoundMode = depthMode || directManeuver || preflightOnly;
            var exactGrid = !sensorBoundMode || (observedController != null && observedController.CubeGrid != null &&
                observedController.CubeGrid.EntityId == expectedGridId);
            // Inventory only once during direct-maneuver admission. This audit sees
            // cooperating/current grid writers but cannot guarantee global exclusivity.
            // The direct path remains fail-closed until authenticated live admission and
            // event-backed takeover handling are connected.
            var directWritersClear = !directManeuver || directWriterAuditAttempted && directWriterAuditPassed;
            var directBrakeStateMatches = !directManeuver || (gridBrakeAvailable && wheelBrakeStateAvailable &&
                observedController.GridWheels.Brake == directBrakeActive &&
                observedController.GridWheels.HandBrake == directBrakeActive &&
                anyWheelBrakeActive == directBrakeActive);
            if (directManeuver && directManeuverExecutor != null)
                brakeStatus = directBrakeStateMatches ? PreflightStatus.Pass : PreflightStatus.Fail;
            var speedValid = Finite(maxSpeed) && maxSpeed >= 0.1 && maxSpeed <= (sensorBoundMode ? 0.8 : 5.0);
            var timeoutValid = timeoutTicks >= 60 && timeoutTicks <= (directManeuver ? 2700 : 7200);
            var checks = new List<PreflightCheck>();

            checks.Add(Check("fixture_source_integrity", modActivationWritten ? PreflightStatus.Pass : PreflightStatus.Unknown,
                modActivationWritten ? "Host verified the immutable run context, source artifacts, fixture path, and staged mod tree before writing the activation." : modHandshakeReason,
                fixtureFingerprint, "PreflightRunContext host verifier", now));
            checks.Add(Check("runtime_connectivity", MySession.Static != null ? PreflightStatus.Pass : PreflightStatus.Unknown,
                MySession.Static != null ? "The game session is available in this update." : "Game session is not currently observable.",
                "enabled=" + enabled + ";session=" + (MySession.Static != null), "Plugin.Update / MySession.Static", now));
            var vehicleIdentityResolved = observedController != null && observedController.CubeGrid != null;
            var vehicleIdentityStatus = !vehicleIdentityResolved ? PreflightStatus.Unknown :
                (directManeuver || preflightOnly) ? (exactGrid ? PreflightStatus.Pass : PreflightStatus.Fail) : PreflightStatus.Pass;
            checks.Add(Check("vehicle_identity", vehicleIdentityStatus,
                !vehicleIdentityResolved ? "Requested controller identity is not currently resolvable." :
                    (directManeuver || preflightOnly) && !exactGrid ? "Resolved controller ID is not on the exact expected grid." :
                    "The requested controller ID resolves on its bound grid.",
                "controllerId=" + controllerId + ";gridId=" +
                    (observedController == null || observedController.CubeGrid == null ? "unknown" : observedController.CubeGrid.EntityId.ToString(CultureInfo.InvariantCulture)) +
                    ";gridName=" + (observedController == null || observedController.CubeGrid == null ? "unknown" : observedController.CubeGrid.DisplayName),
                "MyEntities.TryGetEntityById / pinned controller ID", now));
            var modAttestationReady = modHeartbeatFresh;
            checks.Add(Check("voidwright_module_and_session_marker", modAttestationReady ? PreflightStatus.Pass : PreflightStatus.Unknown,
                modHandshakeReason,
                "activation=" + modActivationWritten + ";session=" + (modHeartbeat == null ? "unknown" : modHeartbeat.SessionId) +
                    ";heartbeatTick=" + (modHeartbeat == null ? -1 : modHeartbeat.SimulationTick) +
                    ";modPreflight=" + modPreflightPass,
                "WorldStorage activation/heartbeat/host-approval handshake", now));
            var observedControlApi = observedController as Sandbox.ModAPI.Ingame.IMyShipController;
            var flightMovement = observedController as Sandbox.ModAPI.Ingame.IMyFlightMovementBlock;
            var controllerReady = observedController != null && !observedController.MarkedForClose && exactGrid &&
                    observedController.CubeGrid != null && observedController.CubeGrid.Physics != null && observedController.GridWheels != null &&
                    observedController.GridWheels.WheelCount > 0 && observedControlApi != null && !observedControlApi.IsUnderControl &&
                    (flightMovement == null || !flightMovement.IsAutoPilotEnabled) &&
                    (directManeuver ? directWritersClear && directBrakeStateMatches : gridBrakeClear) &&
                    brakeStatus == PreflightStatus.Pass;
            var controllerStatus = observedController == null || brakeStatus == PreflightStatus.Unknown
                ? PreflightStatus.Unknown : (controllerReady ? PreflightStatus.Pass : PreflightStatus.Fail);
            checks.Add(Check("controller_readiness", controllerStatus,
                directManeuver ? "Requires a live operator-free controller, a fresh exact grid brake readback, and no running programmable-block or autopilot writer on the disposable vehicle." : "Requires a live, non-closing controller with physics and wheels, no player input, and no active grid or wheel brake/handbrake.",
                "controllerId=" + controllerId +
                    ";entityResolved=" + (observedController != null) +
                    ";closing=" + (observedController == null ? "unknown" : observedController.MarkedForClose.ToString()) +
                    ";exactGrid=" + exactGrid +
                    ";physics=" + (observedController == null || observedController.CubeGrid == null ? "unknown" : (observedController.CubeGrid.Physics != null).ToString()) +
                    ";gridWheels=" + (observedController == null ? "unknown" : (observedController.GridWheels != null).ToString()) +
                    ";controlApi=" + (observedControlApi != null) +
                    ";operatorUnderControl=" + (observedControlApi == null ? "unknown" : observedControlApi.IsUnderControl.ToString()) +
                    ";flightMovement=" + (flightMovement != null) +
                    ";autopilotEnabled=" + (flightMovement == null ? "not-applicable" : flightMovement.IsAutoPilotEnabled.ToString()) +
                    ";gridBrakeAvailable=" + gridBrakeAvailable +
                    ";wheelBrakeStateAvailable=" + wheelBrakeStateAvailable +
                    ";brakeStatus=" + brakeStatus +
                    ";controllerReady=" + controllerReady +
                    ";wheels=" +
                    (observedController == null || observedController.GridWheels == null ? "unknown" : observedController.GridWheels.WheelCount.ToString(CultureInfo.InvariantCulture)) +
                    ";gridHandBrake=" + (observedController == null || observedController.GridWheels == null ? "unknown" : observedController.GridWheels.HandBrake.ToString()) +
                    ";gridBrake=" + (observedController == null || observedController.GridWheels == null ? "unknown" : observedController.GridWheels.Brake.ToString()) +
                    ";wheelBrakeState=" + DescribeWheelBrakeState(observedController) +
                    (directManeuver ? ";writerAudit=" + directWriterAuditEvidence + ";expectedBrokerBrake=" + directBrakeActive : string.Empty),
                "MyShipController / GridWheels", now));
            checks.Add(Check("actuator_availability", modHeartbeatFresh && modHeartbeat.ActuatorReady
                    ? PreflightStatus.Pass : PreflightStatus.Unknown,
                modHeartbeatFresh && modHeartbeat.ActuatorReady ? "The ordinary mod reports bound steerable and propulsive wheel channels." : "Waiting for the ordinary mod's fresh wheel readiness heartbeat.",
                "wheelCount=" + (modHeartbeat == null ? 0 : modHeartbeat.WheelCount) +
                    ";steering=" + (modHeartbeat == null ? 0 : modHeartbeat.SteeringWheelCount) +
                    ";propulsion=" + (modHeartbeat == null ? 0 : modHeartbeat.PropulsionWheelCount),
                "WorldStorage mod heartbeat; host uses no actuator interface", now));
            checks.Add(Check("sensor_validity_freshness", sensorBoundMode && sameGridCamera && scanFresh
                    ? PreflightStatus.Pass : PreflightStatus.Unknown,
                !sensorBoundMode ? "This command has no required obstacle-depth sensor binding." :
                    (!sameGridCamera ? "A same-grid depth camera is not resolved." :
                        (scanFresh ? "A fresh 19-ray depth scan is bound to the current same-grid camera." :
                            "No fresh depth scan is available at the movement boundary.")),
                "depthMode=" + depthMode + ";cameraId=" + navigationDepthCameraId +
                    ";scanReady=" + scanFresh + ";rayCount=" + (modHeartbeat == null ? 0 : modHeartbeat.ValidRayCount) +
                    ";scanAgeTicks=" + (modHeartbeat == null ? "unknown" : (modHeartbeat.SimulationTick - modHeartbeat.SensorScanTick).ToString(CultureInfo.InvariantCulture)),
                "depth camera binding and navigation depth scan telemetry", now));
            checks.Add(Check("position_validity_freshness", positionValid ? PreflightStatus.Pass : PreflightStatus.Unknown,
                positionValid ? "Current finite center-of-mass position is available." : "Current finite physics position is unavailable.",
                "controllerId=" + controllerId + ";positionSource=physics-center-of-mass;sample=current-update",
                "MyCubeGrid.Physics.CenterOfMassWorld", now));
            checks.Add(Check("safety_configuration", speedValid && timeoutValid
                    ? PreflightStatus.Pass : PreflightStatus.Fail,
                speedValid && timeoutValid ? "Speed and timeout are inside the plugin's validated command bounds." : "Speed or timeout is outside the validated command bounds.",
                "maxSpeedMps=" + F(maxSpeed) + ";timeoutTicks=" + timeoutTicks +
                    ";depthEmergencyStopMps=" + F(maxSpeed + 0.25),
                "ReadNavigationCommand / bounded command configuration", now));
            var movementInterfaceReady = modHeartbeatFresh && modActivationWritten &&
                (!directManeuver || directWritersClear && directManeuverPlan != null &&
                 Environment.GetEnvironmentVariable("VRAGECAGE_LAB_MOTION_OWNER") == "voidwright-plugin");
            checks.Add(Check("movement_interface_readiness", movementInterfaceReady
                    ? PreflightStatus.Pass : PreflightStatus.Unknown,
                directManeuver ? "The mod is passive; the server plugin serializes the exact run-bound wheel and brake writes." :
                    modHeartbeatFresh && modActivationWritten ? "The ordinary mod owns movement; Magnetar plugin actuator calls are not dispatched for this operation." : "Movement remains unavailable until host and mod complete the run-bound handshake.",
                "adapter=Voidwright.ServerPlugin;participatingWriter=host;externalCompareAndSet=unavailable;writerAudit=" + directWriterAuditEvidence +
                    ";commandKind=" + commandKind + ";configuredMotionOwner=" +
                    (Environment.GetEnvironmentVariable("VRAGECAGE_LAB_MOTION_OWNER") ?? "unset"),
                "VRageCage command ownership and WorldStorage handshake", now));

            return new MovementPreflight(runId, operationId, fixtureFingerprint, stateFingerprint, now, checks);
        }

        private PreflightCheck Check(string name, PreflightStatus status, string reason,
            string evidence, string source, DateTime timestampUtc)
        {
            return new PreflightCheck(name, status, reason, evidence, source, timestampUtc);
        }

        private string CurrentFixtureFingerprint()
        {
            try
            {
                var manifest = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_MANIFEST");
                if (string.IsNullOrWhiteSpace(manifest) || !File.Exists(manifest)) return "manifest-unavailable";
                using (var stream = File.OpenRead(manifest))
                using (var sha = SHA256.Create())
                    return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
            }
            catch (Exception exception)
            {
                return "manifest-error-" + exception.GetType().Name;
            }
        }

        private string CurrentMovementStateFingerprint()
        {
            MyEntity entity;
            entity = null;
            var found = MySession.Static != null && controllerId > 0 && MyEntities.TryGetEntityById(controllerId, out entity);
            var observedController = found ? entity as MyShipController : null;
            return controllerId.ToString(CultureInfo.InvariantCulture) + ":" +
                (observedController == null || observedController.CubeGrid == null ? "unknown" :
                    observedController.CubeGrid.EntityId.ToString(CultureInfo.InvariantCulture)) + ":" +
                navigationDepthCameraId.ToString(CultureInfo.InvariantCulture) + ":" + commandKind;
        }

        private bool PersistMovementPreflight(MovementPreflight preflight)
        {
            try
            {
                var runId = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_RUN_ID");
                var root = Path.GetFullPath(quarantine ?? string.Empty);
                if (!IsHexIdentity(runId) || !IsHexIdentity(operationId) ||
                    !Directory.Exists(root) || !string.Equals(Path.GetFileName(root), runId, StringComparison.Ordinal))
                    return false;
                MovementPreflightSnapshotStore.Write(root, runId, operationId,
                    preflight.CreatedAtUtc, preflight.ToJson());
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void LogMovementPreflight(MovementPreflight preflight)
        {
            var allPass = preflight.IsAuthorizing(DateTime.UtcNow, preflight.RunId, preflight.OperationId,
                preflight.FixtureFingerprint, preflight.StateFingerprint, TimeSpan.FromSeconds(1));
            MyLog.Default.WriteLineAndConsole("[VRageCage][PREFLIGHT] (" + preflight.PassingCount + "/" + preflight.TotalCount + ") " +
                (allPass ? "Preflight success" : "Preflight blocked") +
                " run=" + preflight.RunId + " operation=" + preflight.OperationId);
            var json = preflight.ToJson();
            MyLog.Default.WriteLineAndConsole("[VRageCage][PREFLIGHT][DETAIL] " + json);
        }

        private void ObserveCockpitOccupancy()
        {
            if (++cockpitPollTicks < CockpitPollIntervalTicks) return;
            cockpitPollTicks = 0;
            if (MyAPIGateway.Entities == null) return;

            try
            {
                var entities = new HashSet<VRage.ModAPI.IMyEntity>();
                MyAPIGateway.Entities.GetEntities(entities, entity => entity is IMyCubeGrid);
                var current = new Dictionary<long, CockpitOccupant>();
                foreach (var entity in entities)
                {
                    var grid = entity as IMyCubeGrid;
                    if (grid == null || grid.MarkedForClose) continue;
                    var cockpits = grid.GetFatBlocks<IMyCockpit>();
                    foreach (var cockpit in cockpits)
                    {
                        if (cockpit == null || cockpit.MarkedForClose) continue;
                        var pilot = cockpit.Pilot;
                        if (pilot == null) continue;
                        current[cockpit.EntityId] = new CockpitOccupant
                        {
                            PilotEntityId = pilot.EntityId,
                            PilotName = pilot.DisplayName,
                            GridId = grid.EntityId,
                            GridName = grid.DisplayName,
                            CockpitName = cockpit.CustomName
                        };
                    }
                }

                if (!cockpitBaselineReady)
                {
                    cockpitBaselineReady = true;
                    MyLog.Default.WriteLineAndConsole(
                        "Voidwright cockpit observer: baseline ready; occupied=" +
                        current.Count.ToString(CultureInfo.InvariantCulture) + ".");
                    foreach (var entry in current)
                        LogCockpitEvent("baseline-occupied", entry.Key, entry.Value, "observer-start");
                    cockpitOccupants.Clear();
                    foreach (var entry in current) cockpitOccupants.Add(entry.Key, entry.Value);
                    return;
                }

                foreach (var entry in current)
                {
                    CockpitOccupant previous;
                    if (!cockpitOccupants.TryGetValue(entry.Key, out previous))
                    {
                        LogCockpitEvent("entered", entry.Key, entry.Value, "pilot-present");
                        continue;
                    }
                    if (previous.PilotEntityId != entry.Value.PilotEntityId)
                    {
                        LogCockpitEvent("exited", entry.Key, previous, "pilot-replaced");
                        LogCockpitEvent("entered", entry.Key, entry.Value, "pilot-replaced");
                    }
                }

                foreach (var entry in cockpitOccupants)
                {
                    if (!current.ContainsKey(entry.Key))
                        LogCockpitEvent("exited", entry.Key, entry.Value, "cockpit-unoccupied-or-removed");
                }

                cockpitOccupants.Clear();
                foreach (var entry in current) cockpitOccupants.Add(entry.Key, entry.Value);
            }
            catch (Exception exception)
            {
                if (cockpitObserverErrorLogged) return;
                cockpitObserverErrorLogged = true;
                MyLog.Default.WriteLineAndConsole(
                    "Voidwright cockpit observer error: " + exception.GetType().Name + ".");
            }
        }

        private static void LogCockpitEvent(string eventName, long cockpitId, CockpitOccupant occupant, string reason)
        {
            MyLog.Default.WriteLineAndConsole(
                "Voidwright cockpit event: event=" + eventName +
                " utc=" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) +
                " cockpit_id=" + cockpitId.ToString(CultureInfo.InvariantCulture) +
                " cockpit=\"" + SafeLogValue(occupant.CockpitName) + "\"" +
                " grid_id=" + occupant.GridId.ToString(CultureInfo.InvariantCulture) +
                " grid=\"" + SafeLogValue(occupant.GridName) + "\"" +
                " pilot_entity_id=" + occupant.PilotEntityId.ToString(CultureInfo.InvariantCulture) +
                " pilot=\"" + SafeLogValue(occupant.PilotName) + "\"" +
                " reason=" + reason + ".");
        }

        private static string SafeLogValue(string value)
        {
            return (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Replace("\"", "'");
        }

        private void EnsurePassiveTelemetryMessageHandler()
        {
            if (!enabled || passiveTelemetryHandlerRegistered || MyAPIGateway.Multiplayer == null) return;
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(
                DrivingLabConsentWire.MessageId, OnPassiveTelemetryMessage);
            passiveTelemetryHandlerRegistered = true;
        }

        private void OnPassiveTelemetryMessage(ushort messageId, byte[] payload, ulong senderSteamId, bool fromServer)
        {
            if (pluginDisposing || messageId != DrivingLabConsentWire.MessageId || fromServer || payload == null ||
                MyAPIGateway.Players == null || MyAPIGateway.Multiplayer == null) return;
            byte operation;
            if (!DrivingLabConsentWire.TryReadRequest(payload, out operation))
            {
                ReplyPassiveTelemetry(senderSteamId, false, "Invalid request.");
                return;
            }

            var identityId = MyAPIGateway.Players.TryGetIdentityId(senderSteamId);
            if (identityId <= 0)
            {
                ReplyPassiveTelemetry(senderSteamId, false, "The authenticated player could not be resolved.");
                return;
            }
            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players, player => player != null && !player.IsBot && player.IdentityId == identityId);
            if (players.Count != 1)
            {
                ReplyPassiveTelemetry(senderSteamId, false, "A unique connected player identity is required.");
                return;
            }

            if (operation == DrivingLabConsentWire.StartRequest)
            {
                var runId = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_RUN_ID") ?? string.Empty;
                var sessionId = modSessionId ?? string.Empty;
                long gridId;
                var started = BeginDrivingLabPassiveCaptureFromTrustedOptIn(
                    players[0], sessionId, runId, DateTime.UtcNow.AddMinutes(15),
                    DrivingLabPassiveTelemetryPolicy.MaximumSampleRateHz,
                    DrivingLabPassiveTelemetryPolicy.MaximumBufferedRecords,
                    DrivingLabPassiveTelemetryPolicy.MaximumBufferedBytes, out gridId);
                ReplyPassiveTelemetry(senderSteamId, started, started
                    ? "Capture started for grid " + gridId.ToString(CultureInfo.InvariantCulture) + ". Use /vw telemetry stop to revoke."
                    : "Capture not started. It requires this to be the only connected human, occupying one cockpit on one grid, with the active run and mod session ready.");
                return;
            }

            var revoked = RevokeDrivingLabPassiveCapture(players[0]);
            ReplyPassiveTelemetry(senderSteamId, revoked, revoked
                ? "Capture revoked; the bounded files are being exported."
                : "No active capture is bound to your identity.");
        }

        private static void ReplyPassiveTelemetry(ulong recipientSteamId, bool accepted, string message)
        {
            if (MyAPIGateway.Multiplayer == null) return;
            MyAPIGateway.Multiplayer.SendMessageTo(DrivingLabConsentWire.MessageId,
                DrivingLabConsentWire.CreateResult(accepted, message), recipientSteamId, true);
        }

        private static bool TryFindUniqueOccupiedCockpitGrid(IMyPlayer player, out long gridEntityId)
        {
            gridEntityId = 0;
            if (player == null || player.IsBot || player.IdentityId <= 0 || MyAPIGateway.Entities == null)
                return false;
            var entities = new HashSet<VRage.ModAPI.IMyEntity>();
            MyAPIGateway.Entities.GetEntities(entities, entity => entity is IMyCubeGrid);
            var matches = new List<long>();
            foreach (var entity in entities)
            {
                var grid = entity as IMyCubeGrid;
                if (grid == null || grid.MarkedForClose) continue;
                foreach (var cockpit in grid.GetFatBlocks<IMyCockpit>())
                {
                    if (cockpit == null || cockpit.MarkedForClose || cockpit.Pilot == null ||
                        !cockpit.Pilot.IsPlayer || cockpit.Pilot.IsBot || cockpit.Pilot.ControllerInfo == null)
                        continue;
                    if (cockpit.Pilot.ControllerInfo.ControllingIdentityId == player.IdentityId)
                        matches.Add(grid.EntityId);
                }
            }
            if (matches.Count != 1 || matches[0] <= 0) return false;
            gridEntityId = matches[0];
            return true;
        }

        public void Dispose()
        {
            pluginDisposing = true;
            if (directManeuverExecutor != null || directEmergencyStopPending)
                EmergencyStopDirectManeuver("server plugin unload");
            ClearDirectControllerEventBindings();
            RevokeDirectCapabilityArtifact();
            if (passiveTelemetryHandlerRegistered)
            {
                try
                {
                    if (MyAPIGateway.Utilities != null)
                        MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                        {
                            try
                            {
                                if (MyAPIGateway.Multiplayer != null)
                                    MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(
                                        DrivingLabConsentWire.MessageId, OnPassiveTelemetryMessage);
                            }
                            catch (Exception exception)
                            {
                                MyLog.Default.WriteLineAndConsole(
                                    "Voidwright server plugin: deferred telemetry handler cleanup failed: " +
                                    exception.GetType().Name);
                            }
                        }, "Voidwright plugin dispose");
                    else if (MyAPIGateway.Multiplayer != null)
                        MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(
                            DrivingLabConsentWire.MessageId, OnPassiveTelemetryMessage);
                }
                catch (Exception exception)
                {
                    MyLog.Default.WriteLineAndConsole(
                        "Voidwright server plugin: telemetry handler cleanup scheduling failed: " +
                        exception.GetType().Name);
                }
            }
            passiveTelemetryHandlerRegistered = false;
            passiveTelemetry.StopAndExport();
            if (commandKind == "play-recorded-route" && recorderComponent != null && recorderComponent.IsPlaying)
            {
                try
                {
                    recorderComponent.StopPlay();
                    RecordedRouteEvidence(recorderComponent.IsPlaying ? "dispose-stop-not-confirmed" : "dispose-stopped");
                }
                catch (Exception exception)
                {
                    MyLog.Default.WriteLineAndConsole(
                        "Voidwright recorded-route dispose stop failure: " + exception.GetType().Name);
                }
            }
            if (commandKind == "play-recorded-route")
                StopRecordedRouteSystems();
            StopStatorMotion();
            Neutralize();
            if (enabled)
                MyLog.Default.WriteLineAndConsole("Voidwright server plugin: disposed.");
            enabled = false;
        }

        private static bool ValidateLabEnvironment()
        {
            var authority = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_AUTHORITY");
            var runId = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_RUN_ID");
            var manifest = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_MANIFEST");
            var quarantine = Environment.GetEnvironmentVariable("VRAGECAGE_QUARANTINE");
            return authority == "destructive-lab" && IsHexIdentity(runId) &&
                   !string.IsNullOrWhiteSpace(manifest) && File.Exists(manifest) &&
                   !string.IsNullOrWhiteSpace(quarantine) && Directory.Exists(quarantine) &&
                   string.Equals(Path.GetFileName(quarantine), runId, StringComparison.Ordinal);
        }

        private static bool IsHexIdentity(string value)
        {
            if (value == null || value.Length != 32) return false;
            foreach (var character in value)
                if (!Uri.IsHexDigit(character)) return false;
            return true;
        }

        private static bool IsHexDigest(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (var character in value)
                if (!Uri.IsHexDigit(character)) return false;
            return true;
        }

        private void ReadCommand()
        {
            ConfigureMotionTelemetry();
            commandKind = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_COMMAND");
            quarantine = Environment.GetEnvironmentVariable("VRAGECAGE_QUARANTINE");
            if (commandKind == "discovery-only") return;
            if (commandKind == "rotate-stator")
            {
                ReadStatorCommand();
                return;
            }
            if (commandKind == "sensor-frame")
            {
                ReadSensorCommand();
                return;
            }
            if (commandKind == "direct-maneuver" || commandKind == "preflight-only")
            {
                operationId = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_OPERATION_ID");
                long maneuverController, parsedCamera, parsedGrid, parsedAgentCore, parsedDiscoveryTick;
                double maneuverSpeed;
                int maneuverTicks;
                var preflightOnly = commandKind == "preflight-only";
                var compact = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_MANEUVER_SEGMENTS") ?? string.Empty;
                var parsedDiscoveryRun = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_AGENT_DISCOVERY_RUN_ID");
                var parsedDiscoveryFingerprint = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_AGENT_DISCOVERY_FINGERPRINT");
                expectedDiscoveryRunId = null;
                expectedDiscoveryFingerprint = null;
                expectedDiscoveryTick = 0;
                DirectManeuverPlan parsedManeuverPlan = null;
                if (!IsHexIdentity(operationId) ||
                    !long.TryParse(Environment.GetEnvironmentVariable("VRAGECAGE_LAB_CONTROLLER_ID"), NumberStyles.None, CultureInfo.InvariantCulture, out maneuverController) || maneuverController <= 0 ||
                    !long.TryParse(Environment.GetEnvironmentVariable("VRAGECAGE_LAB_DEPTH_CAMERA_ID"), NumberStyles.None, CultureInfo.InvariantCulture, out parsedCamera) || parsedCamera <= 0 ||
                    !long.TryParse(Environment.GetEnvironmentVariable("VRAGECAGE_LAB_GRID_ID"), NumberStyles.None, CultureInfo.InvariantCulture, out parsedGrid) || parsedGrid <= 0 ||
                    !long.TryParse(Environment.GetEnvironmentVariable("VRAGECAGE_LAB_AGENT_CORE_ID"), NumberStyles.None, CultureInfo.InvariantCulture, out parsedAgentCore) || parsedAgentCore <= 0 ||
                    !IsHexIdentity(parsedDiscoveryRun) ||
                    !long.TryParse(Environment.GetEnvironmentVariable("VRAGECAGE_LAB_AGENT_DISCOVERY_TICK"), NumberStyles.None, CultureInfo.InvariantCulture, out parsedDiscoveryTick) || parsedDiscoveryTick < 0 ||
                    !IsHexDigest(parsedDiscoveryFingerprint) ||
                    !double.TryParse(Environment.GetEnvironmentVariable("VRAGECAGE_LAB_MAX_SPEED"), NumberStyles.Float, CultureInfo.InvariantCulture, out maneuverSpeed) || !Finite(maneuverSpeed) || maneuverSpeed < 0.1 || maneuverSpeed > 0.8 ||
                    !int.TryParse(Environment.GetEnvironmentVariable("VRAGECAGE_LAB_TIMEOUT_TICKS"), NumberStyles.None, CultureInfo.InvariantCulture, out maneuverTicks) || maneuverTicks < 60 || maneuverTicks > (preflightOnly ? 7200 : 2700) ||
                    (!preflightOnly && !DirectManeuverPlan.TryParseCompactSegments(compact,
                        Environment.GetEnvironmentVariable("VRAGECAGE_LAB_RUN_ID"),
                        Environment.GetEnvironmentVariable("VRAGECAGE_LAB_FIXTURE_ID"), operationId,
                        parsedGrid, maneuverController, parsedAgentCore, out parsedManeuverPlan)) ||
                    (!preflightOnly && !DirectManeuverCommandAcceptancePolicy.IsZeroOnlyStationaryHold(
                        parsedManeuverPlan, Environment.GetEnvironmentVariable("VRAGECAGE_LAB_RUN_ID"),
                        Environment.GetEnvironmentVariable("VRAGECAGE_LAB_FIXTURE_ID"), operationId,
                        parsedGrid, maneuverController, parsedAgentCore, compact, maneuverTicks)) ||
                    (preflightOnly && !string.IsNullOrEmpty(compact)))
                    throw new InvalidOperationException(preflightOnly
                        ? "Invalid run-bound VRageCage preflight command."
                        : "Direct maneuver requires a well-formed, exact-identity, zero-only stationary-hold plan and its stop-confirmation budget.");
                controllerId = maneuverController;
                expectedGridId = parsedGrid;
                expectedAgentCoreId = parsedAgentCore;
                expectedDiscoveryRunId = parsedDiscoveryRun;
                expectedDiscoveryTick = parsedDiscoveryTick;
                expectedDiscoveryFingerprint = parsedDiscoveryFingerprint;
                navigationDepthCameraId = parsedCamera;
                maxSpeed = maneuverSpeed;
                timeoutTicks = maneuverTicks;
                directManeuverPlan = parsedManeuverPlan;
                directWriterAuditAttempted = false;
                directWriterAuditPassed = false;
                directWriterAuditEvidence = "not-audited";
                ClearDirectControllerEventBindings();
                System.Threading.Interlocked.Exchange(ref directControllerTransitionObserved, 0);
                directCachedActuators = null;
                directCachedActuatorGridId = 0;
                directCachedActuatorControllerId = 0;
                directCachedActuatorOperationId = null;
                directCachedActuatorExpiresUtc = DateTime.MinValue;
                directManeuverExecutor = null;
                directPreflightStationaryGate = new DirectManeuverStationaryGate();
                directManeuverBroker = null;
                directManeuverIdentity = null;
                directManeuverStartPositionReady = false;
                directManeuverStartedUtc = DateTime.MinValue;
                directBrakeActive = false;
                directEmergencyStopPending = false;
                directManeuverStopUnresolved = false;
                directEmergencyStopWriteAccepted = false;
                directEmergencyStopTimeoutLogged = false;
                directStopRequestId = null;
                modPreflightPersisted = false;
                lastPersistedModHeartbeatTick = long.MinValue;
                lastPersistedModPreflightPass = false;
                directStopRequestSequence = 0;
                if (preflightOnly) { arrivalRadius = 0.5; navigationStartDelayTicks = 0; driveReverse = false; }
                MyLog.Default.WriteLineAndConsole("Voidwright evidence: type=" + commandKind + " operation=" + operationId + " status=accepted; awaiting fresh run-bound preflight and writer audit.");
                return;
            }
            if (IsCoordinateNavigation())
            {
                ReadNavigationCommand();
                return;
            }
            if (commandKind == "play-recorded-route")
            {
                ReadRecordedRouteCommand();
                return;
            }
            if (commandKind == "drive-recorded-route")
            {
                ReadRouteCommand();
                return;
            }
            if (commandKind != "drive-distance")
                return;
            operationId = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_OPERATION_ID");
            long parsedController;
            double parsedDistance;
            double parsedSpeed;
            int parsedTicks;
            bool parsedReverse = false;
            var reverseSetting = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_REVERSE");
            if (!string.IsNullOrEmpty(reverseSetting) && !bool.TryParse(reverseSetting, out parsedReverse))
                throw new InvalidOperationException("Invalid bounded VRageCage reverse setting.");
            if (!IsHexIdentity(operationId) ||
                !long.TryParse(Environment.GetEnvironmentVariable("VRAGECAGE_LAB_CONTROLLER_ID"), out parsedController) ||
                !double.TryParse(Environment.GetEnvironmentVariable("VRAGECAGE_LAB_DISTANCE"),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out parsedDistance) ||
                !double.TryParse(Environment.GetEnvironmentVariable("VRAGECAGE_LAB_MAX_SPEED"),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out parsedSpeed) ||
                !int.TryParse(Environment.GetEnvironmentVariable("VRAGECAGE_LAB_TIMEOUT_TICKS"), out parsedTicks) ||
                parsedController <= 0 || parsedDistance < 0.25 || parsedDistance > (parsedReverse ? 3 : 25) ||
                parsedSpeed < 0.1 || parsedSpeed > 5 || parsedTicks < 60 || parsedTicks > 3600 ||
                (parsedReverse && (parsedSpeed > 0.5 || parsedTicks > 1800)) ||
                !Finite(parsedDistance) || !Finite(parsedSpeed))
                throw new InvalidOperationException("Invalid bounded VRageCage lab command.");
            controllerId = parsedController;
            distanceMeters = parsedDistance;
            maxSpeed = parsedSpeed;
            timeoutTicks = parsedTicks;
            driveReverse = parsedReverse;
            MyLog.Default.WriteLineAndConsole(
                "Voidwright evidence: type=server-control operation=" + operationId +
                " controllerId=" + controllerId + " status=accepted direction=" +
                (driveReverse ? "reverse" : "forward"));
        }

        private void ConfigureMotionTelemetry()
        {
            var raw = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_MOTION_TELEMETRY_INTERVAL_TICKS");
            if (string.IsNullOrWhiteSpace(raw)) return;
            int requested;
            if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out requested) ||
                requested < 1 || requested > 600)
            {
                MyLog.Default.WriteLineAndConsole(
                    "Voidwright motion telemetry interval ignored; expected 1-600 simulation ticks.");
                return;
            }
            navigationMotionTelemetryIntervalTicks = requested;
            MyLog.Default.WriteLineAndConsole(
                "Voidwright motion telemetry sampling configured: intervalTicks=" + requested +
                " source=read-only; default=10.");
        }

        private static bool DirectManeuverPlanWithinRuntimeEnvelope(DirectManeuverPlan plan, int commandTimeoutTicks)
        {
            if (plan == null || commandTimeoutTicks < 60 || commandTimeoutTicks > 2700) return false;
            var ticks = 0;
            var translationMeters = 0.0;
            foreach (var segment in plan.Segments)
            {
                if (segment == null) return false;
                ticks += segment.TimeoutTicks;
                if (segment.Kind == DirectManeuverKind.ForwardDistance || segment.Kind == DirectManeuverKind.ReverseDistance)
                    translationMeters += segment.Value;
            }
            return ticks + DirectManeuverPlan.MaximumStopConfirmationTicks <= commandTimeoutTicks &&
                translationMeters <= 8.0;
        }

        private void ReadNavigationCommand()
        {
            operationId = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_OPERATION_ID");
            navigationZone = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_ZONE");
            var rawDepthCamera = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_DEPTH_CAMERA_ID");
            long parsedController;
            long parsedDepthCamera = 0;
            double x, y, z, parsedSpeed, parsedRadius, parsedOffsetLeft;
            int parsedTicks;
            var rawStartDelay = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_START_DELAY_TICKS");
            var parsedStartDelay = 0;
            var depthMode = commandKind == "drive-to-coordinate-depth";
            expectedGridId = 0;
            expectedAgentCoreId = 0;
            if (!string.IsNullOrEmpty(rawStartDelay) &&
                !int.TryParse(rawStartDelay, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedStartDelay))
                throw new InvalidOperationException("Invalid bounded VRageCage navigation start delay.");
            if (!IsHexIdentity(operationId) || string.IsNullOrWhiteSpace(navigationZone) ||
                navigationZone.Length > 128 || navigationZone.IndexOfAny(new[] { '\r', '\n' }) >= 0 ||
                !long.TryParse(Environment.GetEnvironmentVariable("VRAGECAGE_LAB_CONTROLLER_ID"), out parsedController) ||
                !double.TryParse(Environment.GetEnvironmentVariable("VRAGECAGE_LAB_TARGET_X"), NumberStyles.Float, CultureInfo.InvariantCulture, out x) ||
                !double.TryParse(Environment.GetEnvironmentVariable("VRAGECAGE_LAB_TARGET_Y"), NumberStyles.Float, CultureInfo.InvariantCulture, out y) ||
                !double.TryParse(Environment.GetEnvironmentVariable("VRAGECAGE_LAB_TARGET_Z"), NumberStyles.Float, CultureInfo.InvariantCulture, out z) ||
                !double.TryParse(Environment.GetEnvironmentVariable("VRAGECAGE_LAB_MAX_SPEED"), NumberStyles.Float, CultureInfo.InvariantCulture, out parsedSpeed) ||
                !double.TryParse(Environment.GetEnvironmentVariable("VRAGECAGE_LAB_ARRIVAL_RADIUS"), NumberStyles.Float, CultureInfo.InvariantCulture, out parsedRadius) ||
                !double.TryParse(Environment.GetEnvironmentVariable("VRAGECAGE_LAB_TARGET_OFFSET_LEFT"), NumberStyles.Float, CultureInfo.InvariantCulture, out parsedOffsetLeft) ||
                (depthMode && !long.TryParse(rawDepthCamera, out parsedDepthCamera)) ||
                !int.TryParse(Environment.GetEnvironmentVariable("VRAGECAGE_LAB_TIMEOUT_TICKS"), out parsedTicks) ||
                parsedController <= 0 || parsedSpeed < 0.1 || parsedSpeed > 5 ||
                (depthMode && (parsedDepthCamera <= 0 || parsedSpeed > 0.8 || parsedTicks > 1800)) ||
                (parsedStartDelay < 0 || parsedStartDelay > 3600 || (!depthMode && parsedStartDelay != 0)) ||
                (!depthMode && !string.IsNullOrEmpty(rawDepthCamera)) ||
                parsedRadius < 0.5 || parsedRadius > 10 || Math.Abs(parsedOffsetLeft) > 10 ||
                parsedTicks < 60 || parsedTicks > 7200 ||
                !Finite(parsedSpeed) || !Finite(parsedRadius) || !Finite(parsedOffsetLeft) ||
                !Finite(x) || !Finite(y) || !Finite(z) ||
                Math.Abs(x) > 1000000000 || Math.Abs(y) > 1000000000 || Math.Abs(z) > 1000000000)
                throw new InvalidOperationException("Invalid bounded VRageCage drive-to-coordinate command.");
            controllerId = parsedController;
            navigationTarget = new Vector3D(x, y, z);
            maxSpeed = parsedSpeed;
            arrivalRadius = parsedRadius;
            navigationOffsetLeft = parsedOffsetLeft;
            navigationDepthCameraId = parsedDepthCamera;
            navigationDepthDirectClearFrames = 0;
            navigationDepthChosenHeading = 0;
            navigationDepthMinimumClearance = 0;
            navigationDepthDirectClearance = 0;
            navigationDepthDecision = "not-started";
            lastNavigationHeadingError = 0;
            navigationStartDelayTicks = parsedStartDelay;
            navigationStartDelayRemaining = parsedStartDelay;
            timeoutTicks = parsedTicks + parsedStartDelay;
            MyLog.Default.WriteLineAndConsole(
                "Voidwright evidence: type=server-control operation=" + operationId +
                " controllerId=" + controllerId + " action=" + commandKind + " zone=" + navigationZone +
                " status=accepted target=" + V(navigationTarget) +
                (depthMode ? " depthCameraId=" + navigationDepthCameraId + " maxSpeedMps=" + F(maxSpeed) +
                    " startDelayTicks=" + navigationStartDelayTicks : "") +
                " offsetLeftMeters=" + F(navigationOffsetLeft));
        }

        private bool IsCoordinateNavigation()
        {
            return commandKind == "drive-to-coordinate" || commandKind == "drive-to-coordinate-depth";
        }

        private void ReadRecordedRouteCommand()
        {
            operationId = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_OPERATION_ID");
            long parsedController;
            long parsedRecorder;
            int parsedTicks;
            if (!IsHexIdentity(operationId) ||
                !long.TryParse(Environment.GetEnvironmentVariable("VRAGECAGE_LAB_CONTROLLER_ID"), out parsedController) ||
                !long.TryParse(Environment.GetEnvironmentVariable("VRAGECAGE_LAB_RECORDER_ID"), out parsedRecorder) ||
                !int.TryParse(Environment.GetEnvironmentVariable("VRAGECAGE_LAB_DURATION_TICKS"), out parsedTicks) ||
                parsedController <= 0 || parsedRecorder <= 0 || parsedTicks < 60 || parsedTicks > 7200)
                throw new InvalidOperationException("Invalid bounded recorded-route playback command.");
            controllerId = parsedController;
            recorderId = parsedRecorder;
            timeoutTicks = parsedTicks;
            MyLog.Default.WriteLineAndConsole(
                "Voidwright evidence: type=recorded-route operation=" + operationId +
                " controllerId=" + controllerId + " recorderId=" + recorderId + " status=accepted");
        }

        private void ReadRouteCommand()
        {
            operationId = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_OPERATION_ID");
            long parsedController;
            long parsedRecorder;
            int parsedTicks;
            double parsedSpeed;
            double parsedRadius;
            if (!IsHexIdentity(operationId) ||
                !long.TryParse(Environment.GetEnvironmentVariable("VRAGECAGE_LAB_CONTROLLER_ID"), out parsedController) ||
                !long.TryParse(Environment.GetEnvironmentVariable("VRAGECAGE_LAB_RECORDER_ID"), out parsedRecorder) ||
                !int.TryParse(Environment.GetEnvironmentVariable("VRAGECAGE_LAB_TIMEOUT_TICKS"), out parsedTicks) ||
                !double.TryParse(Environment.GetEnvironmentVariable("VRAGECAGE_LAB_MAX_SPEED"), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out parsedSpeed) ||
                !double.TryParse(Environment.GetEnvironmentVariable("VRAGECAGE_LAB_ARRIVAL_RADIUS"), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out parsedRadius) ||
                parsedController <= 0 || parsedRecorder <= 0 || parsedTicks < 60 || parsedTicks > 7200 ||
                parsedSpeed < 0.1 || parsedSpeed > 5 || parsedRadius < 0.5 || parsedRadius > 10 ||
                !Finite(parsedSpeed) || !Finite(parsedRadius))
                throw new InvalidOperationException("Invalid bounded recorded-route guidance command.");
            controllerId = parsedController;
            recorderId = parsedRecorder;
            timeoutTicks = parsedTicks;
            maxSpeed = parsedSpeed;
            arrivalRadius = parsedRadius;
            MyLog.Default.WriteLineAndConsole(
                "Voidwright evidence: type=recorded-route operation=" + operationId +
                " controllerId=" + controllerId + " recorderId=" + recorderId + " status=accepted");
        }

        private void UpdateRouteFollowing()
        {
            elapsedTicks++;
            if (!started && !TryAcquireRecordedRoute())
            {
                if (elapsedTicks >= timeoutTicks) FinishRecordedRoute("entity-unavailable");
                return;
            }
            if (controller == null || controller.MarkedForClose || controller.GridWheels == null)
            {
                FinishNavigation("controller-lost", 0, 0, 0, 0);
                return;
            }
            if (elapsedTicks >= timeoutTicks)
            {
                var offset = navigationTarget - VehicleGroundReferencePosition();
                var verticalError = Vector3D.Dot(offset, navigationUp);
                var distance = (offset - navigationUp * verticalError).Length();
                FinishNavigation("timeout", distance, offset.Length(), verticalError, 0);
                return;
            }
            NavigateToTarget();
        }

        private bool TryAcquireRecordedRoute()
        {
            MyEntity controllerEntity;
            MyEntity recorderEntity;
            if (!MyEntities.TryGetEntityById(controllerId, out controllerEntity) ||
                !MyEntities.TryGetEntityById(recorderId, out recorderEntity)) return false;

            controller = controllerEntity as MyShipController;
            var recorderBlock = recorderEntity as IMyPathRecorderBlock;
            if (controller == null || controller.CubeGrid == null || controller.CubeGrid.IsStatic ||
                controller.CubeGrid.Physics == null || controller.GridWheels == null ||
                controller.GridWheels.WheelCount <= 0 || recorderBlock == null ||
                recorderBlock.CubeGrid == null || recorderBlock.CubeGrid.EntityId != controller.CubeGrid.EntityId ||
                !recorderBlock.Enabled || !recorderBlock.GetComponent(out recorderComponent) ||
                recorderComponent == null || recorderComponent.IsRecording)
            {
                FinishRecordedRoute("unsupported-recorder-or-controller");
                return false;
            }

            routeWaypoints = new List<Sandbox.ModAPI.Ingame.IMyAutopilotWaypoint>();
            recorderComponent.GetWaypoints(routeWaypoints);
            if (routeWaypoints.Count == 0 || routeWaypoints.Count > 256)
            {
                FinishRecordedRoute("invalid-waypoint-count");
                return false;
            }

            startPosition = VehicleGroundReferencePosition();
            navigationUp = controller.GetTotalGravity();
            if (navigationUp.LengthSquared() > 0.0001)
            {
                navigationUp.Normalize();
                navigationUp = -navigationUp;
            }
            else
            {
                navigationUp = controller.CubeGrid.WorldMatrix.Up;
            }
            ConfigureLabSteeringAxle();
            ConfigureLabWheelSpeedLimit();

            var nearestDistance = double.MaxValue;
            for (var index = 0; index < routeWaypoints.Count; index++)
            {
                var offset = routeWaypoints[index].Matrix.Translation - startPosition;
                var planar = offset - navigationUp * Vector3D.Dot(offset, navigationUp);
                if (planar.LengthSquared() >= nearestDistance) continue;
                nearestDistance = planar.LengthSquared();
                routeWaypointIndex = index;
            }
            navigationTarget = routeWaypoints[routeWaypointIndex].Matrix.Translation;
            var targetOffset = navigationTarget - startPosition;
            initialTargetDistance = (targetOffset - navigationUp * Vector3D.Dot(targetOffset, navigationUp)).Length();
            bestTargetDistance = initialTargetDistance;
            lastProgressTick = elapsedTicks;
            started = true;
            originalHandBrake = controller.GridWheels.HandBrake;
            controller.GridWheels.HandBrake = false;
            controller.GridWheels.Brake = false;
            MyLog.Default.WriteLineAndConsole(
                "Voidwright evidence: type=server-control operation=" + operationId +
                " controllerId=" + controllerId + " action=release-handbrake" +
                " original=" + originalHandBrake +
                " active=" + controller.GridWheels.HandBrake);
            RecordedRouteEvidence("following");
            return true;
        }

        private void UpdateRecordedRoute()
        {
            elapsedTicks++;
            if (!started)
            {
                MyEntity controllerEntity;
                MyEntity recorderEntity;
                if (!MyEntities.TryGetEntityById(controllerId, out controllerEntity) ||
                    !MyEntities.TryGetEntityById(recorderId, out recorderEntity))
                {
                    if (elapsedTicks >= timeoutTicks) FinishRecordedRoute("entity-unavailable");
                    return;
                }

                controller = controllerEntity as MyShipController;
                if (controller != null)
                    startPosition = VehicleGroundReferencePosition();
                var recorderBlock = recorderEntity as IMyPathRecorderBlock;
                if (controller == null || controller.CubeGrid == null || controller.CubeGrid.IsStatic ||
                    controller.CubeGrid.Physics == null || recorderBlock == null ||
                    recorderBlock.CubeGrid == null || recorderBlock.CubeGrid.EntityId != controller.CubeGrid.EntityId ||
                    !recorderBlock.Enabled || !recorderBlock.GetComponent(out recorderComponent) ||
                    recorderComponent == null || recorderComponent.IsRecording)
                {
                    FinishRecordedRoute("unsupported-recorder-or-controller");
                    return;
                }

                var waypoints = new List<Sandbox.ModAPI.Ingame.IMyAutopilotWaypoint>();
                recorderComponent.GetWaypoints(waypoints);
                if (waypoints.Count == 0 || waypoints.Count > 256)
                {
                    FinishRecordedRoute("invalid-waypoint-count");
                    return;
                }

                var movementBlockCount = 0;
                var movementBlockId = 0L;
                foreach (var slim in controller.CubeGrid.GetBlocks())
                {
                    var block = slim == null ? null : slim.FatBlock;
                    if (block == null) continue;
                    MyAiBlockComponent aiComponent;
                    if (block.Components.TryGet<MyAiBlockComponent>(out aiComponent) &&
                        aiComponent.AiBlockType == VRage.Game.ModAPI.MyAiBlockType.Movement)
                    {
                        movementBlockCount++;
                        recordedRouteAiComponent = aiComponent;
                        recordedRouteMovement = block as Sandbox.ModAPI.Ingame.IMyFlightMovementBlock;
                        movementBlockId = block.EntityId;
                        block.Components.TryGet<MyAutopilotComponent>(out recordedRouteAutopilot);
                    }

                    // Run only the onboard rover controller whose source was inspected
                    // in this fixture. Do not accidentally execute an unrelated PB.
                    var programmable = block as Sandbox.ModAPI.IMyProgrammableBlock;
                    if (programmable == null || programmable.HasCompileErrors || !programmable.Enabled ||
                        string.IsNullOrEmpty(programmable.ProgramData) ||
                        programmable.ProgramData.IndexOf("Rover wheel driver for a vanilla AI Flight",
                            StringComparison.Ordinal) < 0)
                        continue;
                    if (recordedRouteProgrammable != null)
                    {
                        FinishRecordedRoute("multiple-rover-programmable-blocks");
                        return;
                    }
                    recordedRouteProgrammable = programmable;
                }
                if (movementBlockCount != 1 || recordedRouteAiComponent == null ||
                    recordedRouteMovement == null || recordedRouteAutopilot == null)
                {
                    FinishRecordedRoute("ai-move-block-unavailable-or-ambiguous");
                    return;
                }
                if (recordedRouteProgrammable == null)
                {
                    FinishRecordedRoute("rover-programmable-block-unavailable");
                    return;
                }

                startPosition = VehicleGroundReferencePosition();
                recordedRouteAiWasActivated = recordedRouteAiComponent.IsActivated;
                if (!recordedRouteAiWasActivated)
                {
                    // Match the operator sequence: enable AI Behavior on the Move
                    // block before asking Path Recorder to publish its waypoints.
                    recordedRouteAiComponent.IsActivated = true;
                    recordedRouteAiActivatedByTrial = true;
                }
                recorderComponent.Play();
                if (!recordedRouteAiComponent.IsActivated || !recorderComponent.IsPlaying)
                {
                    FinishRecordedRoute("ai-waypoint-not-activated");
                    return;
                }

                // Path Recorder publishes its route to the active Flight Movement
                // block. Only after that handoff do we enable autopilot on that
                // exact block; the Remote Control is never activated or modified.
                if (!recordedRouteMovement.IsAutoPilotEnabled)
                {
                    recordedRouteAutopilot.SetAutoPilotEnabled(true);
                    recordedRouteAutopilotEnabledByTrial = recordedRouteMovement.IsAutoPilotEnabled;
                }
                if (!recordedRouteMovement.IsAutoPilotEnabled ||
                    recordedRouteMovement.CurrentWaypoint == null)
                {
                    FinishRecordedRoute("flight-movement-autopilot-or-waypoint-unavailable");
                    return;
                }

                var runningProgram = recordedRouteProgrammable as
                    Sandbox.ModAPI.Ingame.IMyProgrammableBlock;
                if (runningProgram == null)
                {
                    FinishRecordedRoute("rover-programmable-interface-unavailable");
                    return;
                }
                if (!runningProgram.IsRunning)
                {
                    recordedRouteProgrammable.Run();
                    recordedRouteProgrammableStartedByTrial = true;
                }
                started = true;
                MyLog.Default.WriteLineAndConsole(
                    "Voidwright evidence: type=recorded-route operation=" + operationId +
                    " controllerId=" + controllerId + " recorderId=" + recorderId +
                    " flightMovementBlockId=" + movementBlockId +
                    " status=playing waypointCount=" + waypoints.Count +
                    " repeat=" + recorderComponent.RepeatPath + " aiBehaviorEnabledBeforePlay=" +
                    recordedRouteAiWasActivated + " programmableStartedByTrial=" +
                    recordedRouteProgrammableStartedByTrial + " start=" + V(startPosition));
            }

            if (controller == null || controller.MarkedForClose || recorderComponent == null)
            {
                FinishRecordedRoute("entity-lost");
                return;
            }

            if (!recorderComponent.IsPlaying)
            {
                FinishRecordedRoute("playback-ended");
                return;
            }

            if (elapsedTicks % 60 == 0)
                RecordedRouteEvidence("running");

            if (elapsedTicks >= timeoutTicks)
            {
                try
                {
                    recorderComponent.StopPlay();
                    FinishRecordedRoute(recorderComponent.IsPlaying ? "stop-not-confirmed" : "bounded-window-complete");
                }
                catch (Exception exception)
                {
                    MyLog.Default.WriteLineAndConsole(
                        "Voidwright recorded-route stop failure: " + exception.GetType().Name);
                    FinishRecordedRoute("stop-failed");
                }
            }
        }

        private void FinishRecordedRoute(string status)
        {
            if (recorderComponent != null && recorderComponent.IsPlaying)
            {
                try { recorderComponent.StopPlay(); }
                catch (Exception exception)
                {
                    MyLog.Default.WriteLineAndConsole(
                        "Voidwright recorded-route stop failure: " + exception.GetType().Name);
                    status = "stop-failed";
                }
            }
            StopRecordedRouteSystems();
            terminal = true;
            RecordedRouteEvidence(status);
        }

        private void StopRecordedRouteSystems()
        {
            if (recordedRouteAutopilotEnabledByTrial && recordedRouteAutopilot != null)
            {
                try { recordedRouteAutopilot.SetAutoPilotEnabled(false); }
                catch (Exception exception)
                {
                    MyLog.Default.WriteLineAndConsole(
                        "Voidwright recorded-route autopilot stop failure: " + exception.GetType().Name);
                }
                recordedRouteAutopilotEnabledByTrial = false;
            }
            if (recordedRouteAiActivatedByTrial && recordedRouteAiComponent != null)
            {
                try { recordedRouteAiComponent.IsActivated = recordedRouteAiWasActivated; }
                catch (Exception exception)
                {
                    MyLog.Default.WriteLineAndConsole(
                        "Voidwright recorded-route AI behavior restore failure: " + exception.GetType().Name);
                }
                recordedRouteAiActivatedByTrial = false;
            }
            // The PB's Update1 loop is intentionally enabled only for this bounded
            // trial. Disable it on the disposable clone so its script cannot resume
            // after the worker restarts. The saved original is never touched.
            if (recordedRouteProgrammableStartedByTrial && recordedRouteProgrammable != null)
            {
                try { recordedRouteProgrammable.Enabled = false; }
                catch (Exception exception)
                {
                    MyLog.Default.WriteLineAndConsole(
                        "Voidwright recorded-route PB stop failure: " + exception.GetType().Name);
                }
                recordedRouteProgrammableStartedByTrial = false;
            }
            Neutralize();
        }

        private void RecordedRouteEvidence(string status)
        {
            var position = controller == null || controller.MarkedForClose
                ? Vector3D.Zero : VehicleGroundReferencePosition();
            var coexistingSystems = DescribeRecordedRouteSystems();
            MyLog.Default.WriteLineAndConsole(
                "Voidwright evidence: type=recorded-route operation=" + operationId +
                " controllerId=" + controllerId + " recorderId=" + recorderId +
                " status=" + status + " elapsedTicks=" + elapsedTicks +
                (routeWaypoints == null ? "" : " waypointIndex=" + routeWaypointIndex +
                    " waypointCount=" + routeWaypoints.Count) +
                " displacement=" + F(Vector3D.Distance(startPosition, position)) +
                " position=" + V(position) +
                " isPlaying=" + (recorderComponent != null && recorderComponent.IsPlaying) +
                coexistingSystems);
        }

        // This playback trial deliberately leaves the onboard programmable block,
        // AI movement block, and wheel overrides alone. Snapshot their observable
        // state together so a vanilla/PB wheel-control conflict is visible without
        // turning the measurement harness into another controller.
        private string DescribeRecordedRouteSystems()
        {
            if (controller == null || controller.CubeGrid == null) return "";

            var aiCount = 0;
            var aiActive = 0;
            var aiBehaviorActive = 0;
            var aiWaypoint = "none";
            var pbCount = 0;
            var pbRunning = 0;
            var pbCompileErrors = 0;
            var wheelCount = 0;
            var wheelBraking = 0;
            var wheelPropulsion = 0.0;
            var wheelSteering = 0.0;

            foreach (var slim in controller.CubeGrid.GetBlocks())
            {
                var block = slim == null ? null : slim.FatBlock;
                if (block == null) continue;

                var movement = block as Sandbox.ModAPI.Ingame.IMyFlightMovementBlock;
                if (movement != null)
                {
                    aiCount++;
                    if (movement.IsAutoPilotEnabled) aiActive++;
                    var waypoint = movement.CurrentWaypoint;
                    if (waypoint != null) aiWaypoint = waypoint.Name ?? "(unnamed)";
                }

                MyAiBlockComponent aiComponent;
                if (block.Components.TryGet<MyAiBlockComponent>(out aiComponent) &&
                    aiComponent.AiBlockType == VRage.Game.ModAPI.MyAiBlockType.Movement &&
                    aiComponent.IsActivated)
                    aiBehaviorActive++;

                var programmable = block as Sandbox.ModAPI.Ingame.IMyProgrammableBlock;
                if (programmable != null)
                {
                    pbCount++;
                    if (programmable.IsRunning) pbRunning++;
                    var programmableApi = block as Sandbox.ModAPI.IMyProgrammableBlock;
                    if (programmableApi != null && programmableApi.HasCompileErrors)
                        pbCompileErrors++;
                }

                var wheel = block as Sandbox.ModAPI.Ingame.IMyMotorSuspension;
                if (wheel != null)
                {
                    wheelCount++;
                    if (wheel.Brake) wheelBraking++;
                    wheelPropulsion += wheel.PropulsionOverride;
                    wheelSteering += wheel.SteeringOverride;
                }
            }

            var velocity = controller.CubeGrid.Physics == null
                ? Vector3D.Zero : new Vector3D(controller.CubeGrid.Physics.LinearVelocity);
            return " aiMoveBlocks=" + aiCount + " aiBehaviorActive=" + aiBehaviorActive +
                " aiAutopilotActive=" + aiActive +
                " aiCurrentWaypoint=" + aiWaypoint + " programmableBlocks=" + pbCount +
                " programmableRunning=" + pbRunning + " programmableCompileErrors=" + pbCompileErrors +
                " wheels=" + wheelCount + " wheelBrakes=" + wheelBraking +
                " wheelPropulsionOverrideSum=" + F(wheelPropulsion) +
                " wheelSteeringOverrideSum=" + F(wheelSteering) +
                " linearVelocity=" + V(velocity);
        }

        private void ReadStatorCommand()
        {
            operationId = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_OPERATION_ID");
            string expectedName = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_STATOR_EXPECTED_NAME");
            long parsedStator;
            long parsedCamera;
            double parsedAngle;
            double parsedRpm;
            int parsedTicks;
            if (!IsHexIdentity(operationId) ||
                !long.TryParse(Environment.GetEnvironmentVariable("VRAGECAGE_LAB_STATOR_ID"), out parsedStator) ||
                !long.TryParse(Environment.GetEnvironmentVariable("VRAGECAGE_LAB_STATOR_CAMERA_ID"), out parsedCamera) ||
                !double.TryParse(Environment.GetEnvironmentVariable("VRAGECAGE_LAB_STATOR_ANGLE_DEGREES"),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out parsedAngle) ||
                !double.TryParse(Environment.GetEnvironmentVariable("VRAGECAGE_LAB_STATOR_MAX_RPM"),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out parsedRpm) ||
                !int.TryParse(Environment.GetEnvironmentVariable("VRAGECAGE_LAB_TIMEOUT_TICKS"), out parsedTicks) ||
                parsedStator <= 0 || parsedCamera <= 0 || string.IsNullOrWhiteSpace(expectedName) ||
                expectedName.Length > 128 || expectedName.IndexOfAny(new[] { '\r', '\n' }) >= 0 ||
                parsedAngle < -360 || parsedAngle > 360 || parsedRpm < 0.1 || parsedRpm > 0.5 ||
                parsedTicks < 60 || parsedTicks > 3600 ||
                !Finite(parsedAngle) || !Finite(parsedRpm))
                throw new InvalidOperationException("Invalid bounded VRageCage stator command.");
            statorId = parsedStator;
            statorCameraId = parsedCamera;
            statorExpectedName = expectedName;
            statorDesiredAngleDegrees = parsedAngle;
            statorMaxRpm = parsedRpm;
            timeoutTicks = parsedTicks;
        }

        private void UpdateStatorRotation()
        {
            if (targetStator == null)
            {
                MyEntity entity;
                if (!MyEntities.TryGetEntityById(statorId, out entity))
                {
                    FinishStator("stator-unavailable");
                    return;
                }
                targetStator = entity as IMyMotorStator;
                if (targetStator == null)
                {
                    FinishStator("unsupported-stator");
                    return;
                }
                if (!string.Equals(targetStator.CustomName, statorExpectedName, StringComparison.Ordinal))
                {
                    FinishStator("stator-name-mismatch");
                    return;
                }
                if (!targetStator.IsAttached || targetStator.TopGrid == null)
                {
                    FinishStator("stator-not-attached");
                    return;
                }
                var cameraFound = false;
                foreach (var camera in targetStator.TopGrid.GetFatBlocks<IMyCameraBlock>())
                {
                    if (camera != null && camera.EntityId == statorCameraId)
                    {
                        cameraFound = true;
                        break;
                    }
                }
                if (!cameraFound)
                {
                    FinishStator("camera-not-attached");
                    return;
                }
                if (!targetStator.IsFunctional || !targetStator.Enabled || targetStator.RotorLock)
                {
                    FinishStator("stator-not-ready");
                    return;
                }
                try
                {
                    targetStator.RotateToAngle(Sandbox.ModAPI.Ingame.MyRotationDirection.AUTO,
                        (float)statorDesiredAngleDegrees, (float)statorMaxRpm);
                }
                catch
                {
                    FinishStator("stator-control-failed");
                    return;
                }
                StatorEvidence("accepted");
            }

            elapsedTicks++;
            var actualAngle = targetStator.Angle * 180.0 / Math.PI;
            var angleError = Math.Abs(((actualAngle - statorDesiredAngleDegrees + 540.0) % 360.0) - 180.0);
            statorStableTicks = angleError <= 1.5 ? statorStableTicks + 1 : 0;
            if (statorStableTicks >= 6)
            {
                FinishStator("complete");
                return;
            }
            if (elapsedTicks >= timeoutTicks)
            {
                FinishStator("timeout");
                return;
            }
            if (elapsedTicks % 60 == 0) StatorEvidence("running");
        }

        private void FinishStator(string status)
        {
            StopStatorMotion();
            terminal = true;
            StatorEvidence(status);
        }

        private void StopStatorMotion()
        {
            if (targetStator == null || targetStator.MarkedForClose) return;
            try { targetStator.TargetVelocityRPM = 0; }
            catch (Exception exception)
            {
                MyLog.Default.WriteLineAndConsole(
                    "Voidwright stator stop failed: " + exception.GetType().Name);
            }
        }

        private void StatorEvidence(string status)
        {
            var angle = targetStator == null ? "unavailable" : F(targetStator.Angle * 180.0 / Math.PI);
            MyLog.Default.WriteLineAndConsole(
                "Voidwright evidence: type=server-control operation=" + operationId +
                " action=rotate-stator statorId=" + statorId + " cameraId=" + statorCameraId +
                " status=" + status + " elapsedTicks=" + elapsedTicks +
                " actualAngleDegrees=" + angle + " targetAngleDegrees=" + F(statorDesiredAngleDegrees) +
                " maxRpm=" + F(statorMaxRpm));
        }

        private void ReadSensorCommand()
        {
            operationId = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_OPERATION_ID");
            long parsedCamera;
            int parsedWidth;
            int parsedHeight;
            double parsedRange;
            double parsedFov;
            if (!IsHexIdentity(operationId) ||
                !long.TryParse(Environment.GetEnvironmentVariable("VRAGECAGE_LAB_CAMERA_ID"), out parsedCamera) ||
                !int.TryParse(Environment.GetEnvironmentVariable("VRAGECAGE_LAB_FRAME_WIDTH"), out parsedWidth) ||
                !int.TryParse(Environment.GetEnvironmentVariable("VRAGECAGE_LAB_FRAME_HEIGHT"), out parsedHeight) ||
                !double.TryParse(Environment.GetEnvironmentVariable("VRAGECAGE_LAB_FRAME_RANGE"),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out parsedRange) ||
                !double.TryParse(Environment.GetEnvironmentVariable("VRAGECAGE_LAB_FRAME_FOV"),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out parsedFov) ||
                parsedCamera <= 0 || parsedWidth < 4 || parsedWidth > 64 ||
                parsedHeight < 4 || parsedHeight > 36 || parsedWidth * parsedHeight > 2304 ||
                parsedRange < 1 || parsedRange > 250 || parsedFov < 10 || parsedFov > 120 ||
                !Finite(parsedRange) || !Finite(parsedFov))
                throw new InvalidOperationException("Invalid bounded VRageCage sensor-frame command.");
            cameraId = parsedCamera;
            frameWidth = parsedWidth;
            frameHeight = parsedHeight;
            frameRange = parsedRange;
            frameFov = parsedFov;
            MyLog.Default.WriteLineAndConsole(
                "Voidwright evidence: type=sensor-frame operation=" + operationId +
                " cameraId=" + cameraId + " status=accepted");
        }

        private void CaptureSensorFrame()
        {
            if (sensorDepths == null)
            {
                MyEntity entity;
                if (!MyEntities.TryGetEntityById(cameraId, out entity))
                {
                    terminal = true;
                    SensorEvidence("camera-unavailable", 0, null, null, null, null);
                    return;
                }
                sensorCamera = entity as IMyCameraBlock;
                if (sensorCamera == null || sensorCamera.CubeGrid == null || entity.Closed ||
                    MyAPIGateway.Physics == null)
                {
                    terminal = true;
                    SensorEvidence("unsupported-camera", 0, null, null, null, null);
                    return;
                }
                sensorDepths = new ushort[frameWidth * frameHeight];
                sensorHitEntityIds = new long[frameWidth * frameHeight];
                sensorSurfaceNormals = new short[frameWidth * frameHeight * 3];
                sensorEntityHitCounts.Clear();
                sensorEntityDisplayNames.Clear();
                sensorEntityRuntimeTypes.Clear();
                sensorValidNormalHits = 0;
                sensorInvalidNormalHits = 0;
                sensorMatrix = sensorCamera.WorldMatrix;
                sensorOrigin = sensorMatrix.Translation + sensorMatrix.Forward * 1.5;
                sensorGridId = sensorCamera.CubeGrid.EntityId;
                var naturalGravity = MyAPIGateway.Physics.CalculateNaturalGravityAt(
                    sensorOrigin, out sensorNaturalGravityInterference);
                sensorNaturalGravity = new Vector3D(naturalGravity.X, naturalGravity.Y, naturalGravity.Z);
                var gravityMagnitude = sensorNaturalGravity.Length();
                sensorNaturalUp = gravityMagnitude > 0.000001
                    ? Vector3D.Normalize(-sensorNaturalGravity) : Vector3D.Zero;
            }

            try
            {
                var horizontalScale = Math.Tan(frameFov * Math.PI / 360.0);
                var verticalScale = horizontalScale * frameHeight / frameWidth;
                var end = Math.Min(sensorDepths.Length, sensorIndex + 128);
                for (; sensorIndex < end; sensorIndex++)
                {
                    var y = sensorIndex / frameWidth;
                    var x = sensorIndex % frameWidth;
                    var vertical = 1.0 - 2.0 * (y + 0.5) / frameHeight;
                    var horizontal = 2.0 * (x + 0.5) / frameWidth - 1.0;
                    var direction = Vector3D.Normalize(sensorMatrix.Forward +
                        sensorMatrix.Right * horizontal * horizontalScale + sensorMatrix.Up * vertical * verticalScale);
                    IHitInfo hit;
                    if (!MyAPIGateway.Physics.CastRay(sensorOrigin, sensorOrigin + direction * frameRange, out hit) ||
                        hit == null) continue;
                    var distance = Math.Min(frameRange, Vector3D.Distance(sensorOrigin, hit.Position));
                    sensorDepths[sensorIndex] = (ushort)Math.Max(1,
                        Math.Min(65535, Math.Round(distance / frameRange * 65535.0)));
                    var hitNormal = hit.Normal;
                    var normalLengthSquared = (double)hitNormal.LengthSquared();
                    var normalOffset = sensorIndex * 3;
                    if (Finite(normalLengthSquared) && normalLengthSquared > 0.000000000001)
                    {
                        var normalScale = 32767.0 / Math.Sqrt(normalLengthSquared);
                        sensorSurfaceNormals[normalOffset] = (short)Math.Round(hitNormal.X * normalScale);
                        sensorSurfaceNormals[normalOffset + 1] = (short)Math.Round(hitNormal.Y * normalScale);
                        sensorSurfaceNormals[normalOffset + 2] = (short)Math.Round(hitNormal.Z * normalScale);
                        sensorValidNormalHits++;
                    }
                    else
                    {
                        sensorInvalidNormalHits++;
                    }
                    var hitEntityId = hit.HitEntity == null ? 0L : hit.HitEntity.EntityId;
                    sensorHitEntityIds[sensorIndex] = hitEntityId;
                    if (hitEntityId != 0)
                    {
                        sensorKnownEntityHits++;
                        int entityHitCount;
                        sensorEntityHitCounts.TryGetValue(hitEntityId, out entityHitCount);
                        sensorEntityHitCounts[hitEntityId] = entityHitCount + 1;
                        if (!sensorEntityDisplayNames.ContainsKey(hitEntityId))
                        {
                            var displayName = hit.HitEntity.DisplayName ?? string.Empty;
                            if (displayName.Length > 128) displayName = displayName.Substring(0, 128);
                            sensorEntityDisplayNames.Add(hitEntityId, displayName);
                            var runtimeType = hit.HitEntity.GetType().FullName ?? hit.HitEntity.GetType().Name;
                            if (runtimeType.Length > 128) runtimeType = runtimeType.Substring(0, 128);
                            sensorEntityRuntimeTypes.Add(hitEntityId, runtimeType);
                        }
                    }
                    if (hitEntityId == sensorGridId) sensorSelfGridHits++;
                    sensorHits++;
                }
                if (sensorIndex < sensorDepths.Length) return;

                var baseName = "sensor-" + operationId;
                var depthPath = Path.Combine(quarantine, baseName + ".pgm");
                WriteDepthMap(depthPath, sensorDepths);
                var depthHash = Sha256(depthPath);
                var entityIdPath = Path.Combine(quarantine, baseName + ".i64le");
                WriteEntityIdMap(entityIdPath, sensorHitEntityIds);
                var entityIdHash = Sha256(entityIdPath);
                var surfaceNormalPath = Path.Combine(quarantine, baseName + ".n16le");
                WriteSurfaceNormalMap(surfaceNormalPath, sensorSurfaceNormals);
                var surfaceNormalHash = Sha256(surfaceNormalPath);
                var metadataPath = Path.Combine(quarantine, baseName + ".json");
                var metadata = "{\"schema\":\"vragecage.sensor-frame.v1\",\"operation_id\":\"" + operationId +
                    "\",\"camera_id\":" + cameraId + ",\"kind\":\"depth\",\"width\":" + frameWidth +
                    ",\"height\":" + frameHeight + ",\"range_meters\":" + F(frameRange) +
                    ",\"horizontal_fov_degrees\":" + F(frameFov) + ",\"hit_pixels\":" + sensorHits +
                    ",\"grid_id\":" + sensorGridId + ",\"origin\":" + V(sensorOrigin) +
                    ",\"forward\":" + V(sensorMatrix.Forward) + ",\"up\":" + V(sensorMatrix.Up) +
                    ",\"natural_gravity_world\":" + V(sensorNaturalGravity) +
                    ",\"natural_up_world\":" + V(sensorNaturalUp) +
                    ",\"natural_gravity_magnitude\":" + F(sensorNaturalGravity.Length()) +
                    ",\"natural_gravity_interference\":" + F(sensorNaturalGravityInterference) +
                    ",\"miss_value\":0,\"encoding\":\"pgm-p5-16bit-big-endian\",\"depth_sha256\":\"" +
                    depthHash + "\",\"entity_id_path\":\"" + baseName + ".i64le\",\"entity_id_encoding\":\"i64le-zero-is-no-entity\"" +
                    ",\"entity_id_sha256\":\"" + entityIdHash + "\",\"known_entity_hits\":" + sensorKnownEntityHits +
                    ",\"self_grid_hits\":" + sensorSelfGridHits + ",\"entity_summaries\":" +
                    SensorEntitySummariesJson() + ",\"normal_path\":\"" + baseName + ".n16le\",\"normal_encoding\":\"xyz-i16le-unit32767-zero-is-invalid-or-miss\"," +
                    "\"normal_sha256\":\"" + surfaceNormalHash + "\",\"valid_normal_hits\":" + sensorValidNormalHits +
                    ",\"invalid_normal_hits\":" + sensorInvalidNormalHits + "}\n";
                AtomicWrite(metadataPath, System.Text.Encoding.UTF8.GetBytes(metadata));
                terminal = true;
                SensorEvidence("complete", sensorHits, depthHash, entityIdHash, surfaceNormalHash, Sha256(metadataPath));
            }
            catch (Exception exception)
            {
                terminal = true;
                MyLog.Default.WriteLineAndConsole("Voidwright sensor-frame failure: " + exception);
                SensorEvidence("capture-failed", 0, null, null, null, null);
            }
        }

        private string SensorEntitySummariesJson()
        {
            var ids = new List<long>(sensorEntityHitCounts.Keys);
            ids.Sort();
            var result = new StringBuilder("[");
            for (var index = 0; index < ids.Count; index++)
            {
                if (index != 0) result.Append(',');
                var id = ids[index];
                string displayName;
                sensorEntityDisplayNames.TryGetValue(id, out displayName);
                result.Append("{\"entity_id\":").Append(id)
                    .Append(",\"hit_count\":").Append(sensorEntityHitCounts[id])
                    .Append(",\"display_name\":\"").Append(JsonString(displayName ?? string.Empty)).Append('"');
                string runtimeType;
                sensorEntityRuntimeTypes.TryGetValue(id, out runtimeType);
                result.Append(",\"runtime_type\":\"").Append(JsonString(runtimeType ?? string.Empty)).Append("\"}");
            }
            return result.Append(']').ToString();
        }

        private static string JsonString(string value)
        {
            var result = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                switch (character)
                {
                    case '\\': result.Append("\\\\"); break;
                    case '"': result.Append("\\\""); break;
                    case '\n': result.Append("\\n"); break;
                    case '\r': result.Append("\\r"); break;
                    case '\t': result.Append("\\t"); break;
                    default:
                        if (character < 32) result.Append(' ');
                        else result.Append(character);
                        break;
                }
            }
            return result.ToString();
        }

        private void WriteDepthMap(string path, ushort[] depths)
        {
            using (var stream = new MemoryStream())
            {
                var header = System.Text.Encoding.ASCII.GetBytes(
                    "P5\n" + frameWidth + " " + frameHeight + "\n65535\n");
                stream.Write(header, 0, header.Length);
                foreach (var depth in depths)
                {
                    stream.WriteByte((byte)(depth >> 8));
                    stream.WriteByte((byte)(depth & 255));
                }
                AtomicWrite(path, stream.ToArray());
            }
        }

        private void WriteEntityIdMap(string path, long[] entityIds)
        {
            using (var stream = new MemoryStream(entityIds.Length * 8))
            {
                foreach (var entityId in entityIds)
                {
                    var value = unchecked((ulong)entityId);
                    for (var shift = 0; shift < 64; shift += 8)
                        stream.WriteByte((byte)(value >> shift));
                }
                AtomicWrite(path, stream.ToArray());
            }
        }

        private void WriteSurfaceNormalMap(string path, short[] normals)
        {
            using (var stream = new MemoryStream(normals.Length * 2))
            {
                foreach (var normal in normals)
                {
                    var value = unchecked((ushort)normal);
                    stream.WriteByte((byte)value);
                    stream.WriteByte((byte)(value >> 8));
                }
                AtomicWrite(path, stream.ToArray());
            }
        }

        private static void AtomicWrite(string path, byte[] content)
        {
            var temporary = path + ".tmp";
            File.WriteAllBytes(temporary, content);
            if (File.Exists(path)) File.Delete(path);
            File.Move(temporary, path);
        }

        private static string Sha256(string path)
        {
            using (var algorithm = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        private static string Sha256Bytes(byte[] bytes)
        {
            using (var algorithm = SHA256.Create())
                return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        private static string F(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string F6(double value)
        {
            return value.ToString("0.000000", CultureInfo.InvariantCulture);
        }

        private static string V(Vector3D value)
        {
            return "[" + F(value.X) + "," + F(value.Y) + "," + F(value.Z) + "]";
        }

        private static string V6(Vector3D value)
        {
            return "[" + F6(value.X) + "," + F6(value.Y) + "," + F6(value.Z) + "]";
        }

        private void SensorEvidence(string status, int hits, string depthHash, string entityIdHash,
            string normalHash, string metadataHash)
        {
            MyLog.Default.WriteLineAndConsole(
                "Voidwright evidence: type=sensor-frame operation=" + operationId +
                " cameraId=" + cameraId + " status=" + status +
                " width=" + frameWidth + " height=" + frameHeight + " hits=" + hits +
                (depthHash == null ? "" : " depthSha256=" + depthHash) +
                (entityIdHash == null ? "" : " entityIdsSha256=" + entityIdHash) +
                (normalHash == null ? "" : " normalSha256=" + normalHash) +
                (metadataHash == null ? "" : " metadataSha256=" + metadataHash));
        }

        private bool TryAcquireController()
        {
            MyEntity entity;
            if (!MyEntities.TryGetEntityById(controllerId, out entity)) return false;
            controller = entity as MyShipController;
            if (controller == null || controller.CubeGrid == null || controller.CubeGrid.IsStatic ||
                controller.CubeGrid.Physics == null || controller.GridWheels == null ||
                controller.GridWheels.WheelCount <= 0)
            {
                Finish("unsupported-controller", 0);
                return false;
            }
            if (!InitializeWheelControl())
            {
                Finish("wheel-control-init-failed", 0);
                return false;
            }
            startPosition = VehicleGroundReferencePosition();
            startForward = controller.WorldMatrix.Forward;
            var gravityUp = controller.GetTotalGravity();
            if (gravityUp.LengthSquared() > 0.0001)
            {
                gravityUp.Normalize();
                startForward -= gravityUp * Vector3D.Dot(startForward, gravityUp);
            }
            if (startForward.LengthSquared() > 0.0001) startForward.Normalize();
            if (commandKind == "drive-distance")
                ConfigureLabWheelSpeedLimit();
            if (IsCoordinateNavigation() || commandKind == "drive-recorded-route")
            {
                navigationUp = controller.GetTotalGravity();
                if (navigationUp.LengthSquared() > 0.0001)
                {
                    navigationUp.Normalize();
                    navigationUp = -navigationUp;
                }
                else
                {
                    navigationUp = controller.CubeGrid.WorldMatrix.Up;
                }
                if (navigationDepthCameraId > 0)
                {
                    MyEntity cameraEntity;
                    if (!MyEntities.TryGetEntityById(navigationDepthCameraId, out cameraEntity))
                    {
                        FinishNavigation("depth-camera-unavailable", 0, 0, 0, 0);
                        return false;
                    }
                    navigationDepthCamera = cameraEntity as IMyCameraBlock;
                    if (navigationDepthCamera == null || navigationDepthCamera.CubeGrid == null ||
                        navigationDepthCamera.CubeGrid.EntityId != controller.CubeGrid.EntityId ||
                        cameraEntity.Closed || MyAPIGateway.Physics == null)
                    {
                        FinishNavigation("depth-camera-unsupported", 0, 0, 0, 0);
                        return false;
                    }
                    var cameraForward = navigationDepthCamera.WorldMatrix.Forward;
                    cameraForward -= navigationUp * Vector3D.Dot(cameraForward, navigationUp);
                    var controllerForward = controller.WorldMatrix.Forward;
                    controllerForward -= navigationUp * Vector3D.Dot(controllerForward, navigationUp);
                    if (cameraForward.LengthSquared() < 0.0001 || controllerForward.LengthSquared() < 0.0001)
                    {
                        FinishNavigation("depth-camera-frame-invalid", 0, 0, 0, 0);
                        return false;
                    }
                    cameraForward.Normalize();
                    controllerForward.Normalize();
                    if (Vector3D.Dot(cameraForward, controllerForward) < 0.5)
                    {
                        FinishNavigation("depth-camera-not-front-facing", 0, 0, 0, 0);
                        return false;
                    }
                    MyLog.Default.WriteLineAndConsole(
                        "Voidwright evidence: type=depth-controller operation=" + operationId +
                        " controllerId=" + controllerId + " cameraId=" + navigationDepthCameraId +
                        " status=ready gridId=" + controller.CubeGrid.EntityId +
                        " forwardAlignment=" + F(Vector3D.Dot(cameraForward, controllerForward)));
                }
                if (IsCoordinateNavigation() && Math.Abs(navigationOffsetLeft) > 0.001)
                {
                    var initialForward = controller.WorldMatrix.Forward;
                    initialForward -= navigationUp * Vector3D.Dot(initialForward, navigationUp);
                    if (initialForward.LengthSquared() < 0.0001)
                    {
                        FinishNavigation("invalid-offset-frame", 0, 0, 0, 0);
                        return false;
                    }
                    initialForward.Normalize();
                    var initialLeft = Vector3D.Cross(navigationUp, initialForward);
                    if (initialLeft.LengthSquared() < 0.0001)
                    {
                        FinishNavigation("invalid-offset-frame", 0, 0, 0, 0);
                        return false;
                    }
                    initialLeft.Normalize();
                    navigationTarget += initialLeft * navigationOffsetLeft;
                    MyLog.Default.WriteLineAndConsole(
                        "Voidwright evidence: type=server-control operation=" + operationId +
                        " controllerId=" + controllerId + " action=resolve-left-offset" +
                        " status=applied meters=" + F(navigationOffsetLeft) +
                        " offsetVector=" + V(initialLeft * navigationOffsetLeft) +
                        " resolvedTarget=" + V(navigationTarget));
                }
                ConfigureLabSteeringAxle();
                ConfigureLabWheelSpeedLimit();
                var navigationStartPosition = navigationDepthCameraId > 0
                    ? controller.CubeGrid.Physics.CenterOfMassWorld : startPosition;
                var offset = navigationTarget - navigationStartPosition;
                initialTargetDistance = (offset - navigationUp * Vector3D.Dot(offset, navigationUp)).Length();
                bestTargetDistance = initialTargetDistance;
                lastProgressTick = elapsedTicks;
                navigationPathStartCenterOfMass = controller.CubeGrid.Physics.CenterOfMassWorld;
                navigationPathLastCenterOfMass = navigationPathStartCenterOfMass;
                navigationPathReady = true;
                navigationPathLengthMeters = 0;
                navigationClosestApproachMeters = PlanarDistance(
                    navigationTarget, navigationPathStartCenterOfMass, navigationUp);
            }
            started = true;
            originalHandBrake = controller.GridWheels.HandBrake;
            if (navigationDepthCameraId > 0)
            {
                // The disposable V3 fixture can already be rolling on its grade
                // when the plugin acquires it. Hold it still before trusting a
                // low-speed controller; otherwise gravity can trigger an
                // overspeed handoff before the first useful steering interval.
                controller.GridWheels.HandBrake = true;
                controller.GridWheels.Brake = true;
            }
            else
            {
                controller.GridWheels.HandBrake = false;
                controller.GridWheels.Brake = false;
            }
            MyLog.Default.WriteLineAndConsole(
                "Voidwright evidence: type=server-control operation=" + operationId +
                " controllerId=" + controllerId + " action=" +
                (navigationDepthCameraId > 0 ? "depth-startup-brake" : "release-handbrake") +
                " original=" + originalHandBrake +
                " active=" + controller.GridWheels.HandBrake +
                " brake=" + controller.GridWheels.Brake);
            Evidence("acquired", 0, controller.CubeGrid.Physics.LinearVelocity.Length());
            if (IsCoordinateNavigation())
                CaptureNavigationMotionSample("acquired");
            return true;
        }

        private bool InitializeWheelControl()
        {
            var wheelSystem = controller.GridWheels;
            var initialize = wheelSystem.GetType().GetMethod("InitControl",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(MyEntity) }, null);
            if (initialize == null)
            {
                MyLog.Default.WriteLineAndConsole(
                    "Voidwright evidence: type=server-control operation=" + operationId +
                    " controllerId=" + controllerId + " action=initialize-wheel-control" +
                    " status=unavailable runtimeType=" + wheelSystem.GetType().FullName);
                return false;
            }

            try
            {
                initialize.Invoke(wheelSystem, new object[] { controller });
                MyLog.Default.WriteLineAndConsole(
                    "Voidwright evidence: type=server-control operation=" + operationId +
                    " controllerId=" + controllerId + " action=initialize-wheel-control" +
                    " status=initialized wheels=" + wheelSystem.WheelCount +
                    " controllerGridForwardAlignment=" +
                    F(Vector3D.Dot(controller.WorldMatrix.Forward, controller.CubeGrid.WorldMatrix.Forward)));
                return true;
            }
            catch (Exception exception)
            {
                MyLog.Default.WriteLineAndConsole(
                    "Voidwright evidence: type=server-control operation=" + operationId +
                    " controllerId=" + controllerId + " action=initialize-wheel-control" +
                    " status=failed error=" + exception.GetType().Name);
                return false;
            }
        }

        private void ConfigureLabSteeringAxle()
        {
            var existingSteering = 0;
            foreach (var wheel in controller.GridWheels.Wheels)
                if (wheel.Steering) existingSteering++;
            if (existingSteering > 0)
            {
                MyLog.Default.WriteLineAndConsole(
                    "Voidwright evidence: type=server-control operation=" + operationId +
                    " controllerId=" + controllerId + " action=configure-lab-steering" +
                    " status=preserved-existing count=" + existingSteering);
                return;
            }

            // If a disposable test fixture has no Steering-enabled suspensions,
            // select only the axle ahead of its Remote Control. Restore these flags
            // during neutralization; never carry fixture setup into a later run.
            // The Remote Control faces the truck's physical nose on V3R1.
            // The grid's Forward axis points rearward for this fixture.
            var vehicleForward = controller.WorldMatrix.Forward;
            vehicleForward -= navigationUp * Vector3D.Dot(vehicleForward, navigationUp);
            if (vehicleForward.LengthSquared() < 0.0001) return;
            vehicleForward.Normalize();
            var controlPosition = controller.PositionComp.GetPosition();
            var furthestAxle = double.NegativeInfinity;
            foreach (var wheel in controller.GridWheels.Wheels)
            {
                var relative = wheel.PositionComp.GetPosition() - controlPosition;
                var projection = Vector3D.Dot(relative, vehicleForward);
                if (projection > furthestAxle) furthestAxle = projection;
            }

            var configured = 0;
            foreach (var wheel in controller.GridWheels.Wheels)
            {
                var relative = wheel.PositionComp.GetPosition() - controlPosition;
                if (Vector3D.Dot(relative, vehicleForward) < furthestAxle - 1.0) continue;
                originalWheelSteering[wheel.EntityId] = wheel.Steering;
                wheel.Steering = true;
                configured++;
            }
            MyLog.Default.WriteLineAndConsole(
                "Voidwright evidence: type=server-control operation=" + operationId +
                " controllerId=" + controllerId + " action=configure-lab-steering" +
                " status=enabled-front-axle count=" + configured +
                " furthestProjection=" + F(furthestAxle));
        }

        private void ConfigureLabWheelSpeedLimit()
        {
            const float labSteerAngle = 0.698132f; // 40 degrees; low-speed lab maneuvering only.
            originalWheelSpeedLimits.Clear();
            originalWheelSteerLimits.Clear();
            var limitKmh = (float)Math.Min(18.0, maxSpeed * 3.6);
            var configured = 0;
            var steerAngle = 0;
            var maximumConfiguredPower = 0f;
            foreach (var wheel in controller.GridWheels.Wheels)
            {
                originalWheelSpeedLimits[wheel.EntityId] = wheel.SpeedLimit;
                var savedPower = wheel.Power;
                originalWheelPower[wheel.EntityId] = savedPower;
                // The command's bounded speed is the temporary cap for this lab
                // run; Neutralize restores the grid's saved per-wheel settings.
                wheel.SpeedLimit = limitKmh;
                // Depth mode must avoid a full-power one-tick impulse: V3 telemetry
                // showed it could jump from near-zero to >1 m/s despite a low duty
                // cycle. Keep the saved ratio, capped at 15%; legacy lab guidance
                // retains its existing temporary full-power setting.
                wheel.Power = navigationDepthCameraId > 0
                    ? DepthAvoidancePolicy.LimitWheelPower(savedPower)
                    : 1.0f;
                maximumConfiguredPower = Math.Max(maximumConfiguredPower, wheel.Power);
                wheel.PropulsionOverride = 0;
                if (wheel.Steering)
                {
                    originalWheelSteerLimits[wheel.EntityId] = wheel.MaxSteerAngle;
                    wheel.MaxSteerAngle = Math.Max(wheel.MaxSteerAngle, labSteerAngle);
                    steerAngle++;
                }
                configured++;
            }
            MyLog.Default.WriteLineAndConsole(
                "Voidwright evidence: type=server-control operation=" + operationId +
                " controllerId=" + controllerId + " action=configure-lab-wheel-speed" +
                " status=bounded count=" + configured + " maxSpeedMps=" + F(maxSpeed) +
                " limitKmh=" + F(limitKmh) + " steerAngleRad=" + F(labSteerAngle) +
                " steerWheels=" + steerAngle + " wheelPowerMax=" + F(maximumConfiguredPower));
        }

        private void NavigateToTarget()
        {
            var depthMode = navigationDepthCameraId > 0;
            var position = depthMode
                ? controller.CubeGrid.Physics.CenterOfMassWorld
                : VehicleGroundReferencePosition();
            UpdateNavigationPathMetrics();
            if (depthMode) AccumulateNavigationLinearVelocity();
            // Keep the independent displacement-speed comparison short enough
            // to expose suspension/physics oscillation instead of averaging it
            // away in one-second windows.
            if (depthMode && elapsedTicks % navigationMotionTelemetryIntervalTicks == 0)
                CaptureNavigationMotionSample("running");
            var targetOffset = navigationTarget - position;
            var spatialDistance = targetOffset.Length();
            var up = navigationUp;
            var verticalError = Vector3D.Dot(targetOffset, up);
            var planarArrivalRadius = arrivalRadius;
            var verticalWithinArrivalRadius = true;
            if (depthMode)
            {
                verticalWithinArrivalRadius = DepthArrivalPolicy.TryGetPlanarRadius(
                    arrivalRadius, verticalError, out planarArrivalRadius);
                if (!verticalWithinArrivalRadius) planarArrivalRadius = 0;
            }
            var gridUp = controller.CubeGrid.WorldMatrix.Up;
            if (gridUp.LengthSquared() > 0.0001)
            {
                gridUp.Normalize();
                // The high-speed R5 trial rolled the disposable truck while the
                // follower kept commanding wheels. The sensor-guided calibration
                // mode uses a tighter 45-degree attitude envelope than legacy navigation.
                var minimumUpAlignment = navigationDepthCameraId > 0 ? 0.70710678 : 0.5;
                if (Vector3D.Dot(gridUp, up) < minimumUpAlignment)
                {
                    var tiltedDesired = targetOffset - up * verticalError;
                    FinishNavigation("attitude-limit", tiltedDesired.Length(), spatialDistance,
                        verticalError, 0);
                    return;
                }
            }
            var desired = targetOffset - up * verticalError;
            var distance = desired.Length();
            var velocity = controller.CubeGrid.Physics == null
                ? Vector3D.Zero : new Vector3D(controller.CubeGrid.Physics.LinearVelocity);
            var controlVelocity = depthMode && navigationDepthPositionVelocityReady
                ? navigationDepthPositionVelocity : velocity;
            var planarVelocity = controlVelocity - up * Vector3D.Dot(controlVelocity, up);
            var speed = planarVelocity.Length();
            var safetySpeed = Math.Max(velocity.Length(), controlVelocity.Length());
            if (navigationDepthCameraId > 0 && !navigationDepthStartupSettled)
            {
                controller.GridWheels.AngularVelocity = Vector3.Zero;
                controller.GridWheels.Brake = true;
                controller.GridWheels.HandBrake = true;
                foreach (var wheel in controller.GridWheels.Wheels)
                    wheel.PropulsionOverride = 0;
                lastSteeringCommand = 0;
                lastDriveInput = 0;
                lastDriveDuty = 0;
                var rawStartupSpeed = velocity.Length();
                var positionVelocityFresh = navigationDepthPositionVelocityReady &&
                    elapsedTicks - navigationDepthVelocityAnchorTick <= 20;
                var stabilitySpeed = positionVelocityFresh
                    ? navigationDepthPositionVelocity.Length() : rawStartupSpeed;
                var startupMotionIsStable = DepthStartupSettlePolicy.IsStable(
                    rawStartupSpeed, positionVelocityFresh, stabilitySpeed);
                navigationDepthStableTicks = startupMotionIsStable
                    ? navigationDepthStableTicks + 1 : 0;
                var startupTimeout = Math.Min(NavigationDepthStartupTimeoutTicks,
                    Math.Max(30, timeoutTicks / 3));
                if (navigationDepthStableTicks >= NavigationDepthStableStartupTicks)
                {
                    navigationDepthStartupSettled = true;
                    navigationDepthDecision = "not-started";
                    navigationDepthDirectClearFrames = 0;
                    // The recorded setting may itself be handbraked. A depth
                    // drive command explicitly owns the actuator during its
                    // bounded run; release now, then Neutralize restores the
                    // captured value on every terminal path.
                    controller.GridWheels.HandBrake = false;
                    controller.GridWheels.Brake = false;
                    MyLog.Default.WriteLineAndConsole(
                        "Voidwright evidence: type=depth-controller operation=" + operationId +
                        " controllerId=" + controllerId + " cameraId=" + navigationDepthCameraId +
                        " status=startup-settled elapsedTicks=" + elapsedTicks +
                        " stableTicks=" + navigationDepthStableTicks + " speed=" + F(speed) +
                        " reportedSpeed=" + F(rawStartupSpeed) +
                        " stabilitySpeed=" + F(stabilitySpeed) +
                        " stabilitySpeedSource=" + (positionVelocityFresh ? "20-tick-center-of-mass" : "reported-linear") +
                        " handBrake=" + controller.GridWheels.HandBrake);
                }
                else if (elapsedTicks >= startupTimeout)
                {
                    navigationDepthDecision = "startup-motion";
                    FinishNavigation("depth-startup-motion", distance, spatialDistance,
                        verticalError, 0);
                    return;
                }
                else if (elapsedTicks % 30 == 0)
                {
                    MyLog.Default.WriteLineAndConsole(
                        "Voidwright evidence: type=depth-controller operation=" + operationId +
                        " controllerId=" + controllerId + " cameraId=" + navigationDepthCameraId +
                        " status=startup-settling elapsedTicks=" + elapsedTicks +
                        " stableTicks=" + navigationDepthStableTicks + " speed=" + F(speed) +
                        " reportedSpeed=" + F(rawStartupSpeed) +
                        " stabilitySpeed=" + F(stabilitySpeed) +
                        " stabilitySpeedSource=" + (positionVelocityFresh ? "20-tick-center-of-mass" : "reported-linear") +
                        " brake=true handBrake=true");
                }
                return;
            }
            if (depthMode && safetySpeed > maxSpeed + 0.25)
            {
                FinishNavigation("depth-overspeed", distance, spatialDistance, verticalError, 0);
                return;
            }
            if (depthMode && navigationStartDelayRemaining > 0)
            {
                controller.GridWheels.AngularVelocity = Vector3.Zero;
                controller.GridWheels.Brake = true;
                controller.GridWheels.HandBrake = true;
                foreach (var wheel in controller.GridWheels.Wheels)
                    wheel.PropulsionOverride = 0;
                lastSteeringCommand = 0;
                lastDriveInput = 0;
                lastDriveDuty = 0;
                navigationStartDelayRemaining--;
                if (navigationStartDelayRemaining == 0)
                {
                    navigationDepthScanReady = false;
                    navigationDepthDecision = "not-started";
                    navigationDepthDirectClearFrames = 0;
                    lastProgressTick = elapsedTicks;
                    bestTargetDistance = distance;
                    controller.GridWheels.HandBrake = false;
                    controller.GridWheels.Brake = false;
                    MyLog.Default.WriteLineAndConsole(
                        "Voidwright evidence: type=depth-controller operation=" + operationId +
                        " controllerId=" + controllerId + " cameraId=" + navigationDepthCameraId +
                        " status=start-delay-complete elapsedTicks=" + elapsedTicks +
                        " startDelayTicks=" + navigationStartDelayTicks);
                }
                else if (navigationStartDelayRemaining % 60 == 0)
                {
                    MyLog.Default.WriteLineAndConsole(
                        "Voidwright evidence: type=depth-controller operation=" + operationId +
                        " controllerId=" + controllerId + " cameraId=" + navigationDepthCameraId +
                        " status=start-delay-holding remainingTicks=" + navigationStartDelayRemaining +
                        " brake=true handBrake=true");
                }
                return;
            }
            if (distance < bestTargetDistance - 0.05)
            {
                bestTargetDistance = distance;
                lastProgressTick = elapsedTicks;
            }
            // Depth-guided land arrival is a world-space sphere. Convert the
            // remaining radius to a ground-plane threshold at the current
            // vertical offset instead of reporting arrival from planar distance
            // alone. If the vertical offset is currently unreachable, continue
            // approaching the ground-plane target; if stopped inside its planar
            // radius, return an explicit height-offset failure rather than
            // claiming arrival.
            if (depthMode && distance <= arrivalRadius && !verticalWithinArrivalRadius)
            {
                if (speed > 0.05)
                {
                    controller.GridWheels.Brake = true;
                    controller.GridWheels.AngularVelocity = Vector3.Zero;
                    lastSteeringCommand = 0;
                    lastDriveInput = 0;
                    if (elapsedTicks % 60 == 0)
                        MyLog.Default.WriteLineAndConsole(
                            "Voidwright evidence: type=server-control operation=" + operationId +
                            " controllerId=" + controllerId + " action=drive-to-coordinate" +
                            " status=settling zone=" + navigationZone +
                            " targetDistance=" + F(distance) + " speed=" + F(speed) + " brake=true");
                    return;
                }
                FinishNavigation("depth-arrival-vertical-offset", distance, spatialDistance,
                    verticalError, 0);
                return;
            }
            // Arrival also requires a near-stop; merely passing through the
            // radius is not a completed navigation result.
            if (distance <= planarArrivalRadius)
            {
                if (depthMode && !DepthArrivalPolicy.IsWithinWorldRadius(
                    distance, verticalError, arrivalRadius))
                {
                    FinishNavigation("depth-arrival-world-radius", distance, spatialDistance,
                        verticalError, 0);
                    return;
                }
                if (speed > 0.05)
                {
                    controller.GridWheels.Brake = true;
                    controller.GridWheels.AngularVelocity = Vector3.Zero;
                    lastSteeringCommand = 0;
                    lastDriveInput = 0;
                    if (elapsedTicks % 60 == 0)
                        MyLog.Default.WriteLineAndConsole(
                            "Voidwright evidence: type=server-control operation=" + operationId +
                            " controllerId=" + controllerId + " action=drive-to-coordinate" +
                            " status=settling zone=" + navigationZone +
                            " targetDistance=" + F(distance) + " spatialDistance=" + F(spatialDistance) +
                            " worldArrivalRadius=" + F(arrivalRadius) + " speed=" + F(speed) + " brake=true");
                    return;
                }
                if (commandKind == "drive-recorded-route" && routeWaypoints != null &&
                    routeWaypointIndex + 1 < routeWaypoints.Count)
                {
                    RecordedRouteEvidence("waypoint-reached");
                    routeWaypointIndex++;
                    navigationTarget = routeWaypoints[routeWaypointIndex].Matrix.Translation;
                    var nextOffset = navigationTarget - position;
                    initialTargetDistance = (nextOffset - up * Vector3D.Dot(nextOffset, up)).Length();
                    bestTargetDistance = initialTargetDistance;
                    lastProgressTick = elapsedTicks;
                    RecordedRouteEvidence("waypoint-selected");
                    return;
                }
                FinishNavigation("complete", distance, spatialDistance, verticalError, 0);
                return;
            }
            // Use the truck Remote Control's physical nose as its navigation
            // frame. On this V3R1 fixture it is opposite CubeGrid.Forward; the
            // front camera and steering axle agree with the Remote Control.
            var forward = controller.WorldMatrix.Forward;
            forward -= up * Vector3D.Dot(forward, up);
            if (desired.LengthSquared() < 0.0001 || forward.LengthSquared() < 0.0001)
            {
                FinishNavigation("invalid-guidance", distance, spatialDistance, verticalError, 0);
                return;
            }
            desired.Normalize();
            forward.Normalize();
            var forwardAlignment = Vector3D.Dot(forward, desired);
            // Keep side targets in one travel mode long enough for steering to
            // turn toward them. Run 16 showed that the old -0.35/+0.25 thresholds
            // flipped at a near-90-degree bearing, reversing the steering frame
            // before the turn could converge.
            if (depthMode)
            {
                // A front-only depth controller never backs into an unobserved
                // area. If its target is behind the camera fan, it stops for RGB
                // inspection and an operator-directed next step.
                navigationReversing = false;
            }
            else if (!navigationReversing && forwardAlignment < -0.85)
            {
                navigationReversing = true;
                MyLog.Default.WriteLineAndConsole(
                    "Voidwright evidence: type=server-control operation=" + operationId +
                    " controllerId=" + controllerId + " action=drive-to-coordinate" +
                    " status=reverse-enter targetDistance=" + F(distance) +
                    " forwardAlignment=" + F(forwardAlignment));
            }
            // The wider hysteresis also keeps a reverse approach in reverse
            // until its target is clearly ahead in the vehicle frame.
            else if (navigationDepthCameraId == 0 && navigationReversing && forwardAlignment > 0.85)
            {
                navigationReversing = false;
                MyLog.Default.WriteLineAndConsole(
                    "Voidwright evidence: type=server-control operation=" + operationId +
                    " controllerId=" + controllerId + " action=drive-to-coordinate" +
                    " status=forward-enter targetDistance=" + F(distance) +
                    " forwardAlignment=" + F(forwardAlignment));
            }
            var travelDirection = navigationReversing ? -forward : forward;
            // The four V3R1 HoloView camera poses establish the lateral
            // convention: up x forward points toward the camera named LEFT,
            // while forward x up aligns with the camera named RIGHT.
            var right = Vector3D.Cross(travelDirection, up);
            right.Normalize();
            var heading = Math.Atan2(Vector3D.Dot(desired, right), Vector3D.Dot(desired, travelDirection));
            if (depthMode)
            {
                if (!navigationDepthScanReady || elapsedTicks - navigationDepthScanTick >= NavigationDepthScanIntervalTicks)
                {
                    string scanFailure;
                    if (!CaptureNavigationDepthFan(out scanFailure))
                    {
                        navigationDepthDecision = scanFailure;
                        FinishNavigation("depth-scan-failed", distance, spatialDistance, verticalError, heading);
                        return;
                    }
                }
                if (elapsedTicks - navigationDepthScanTick > NavigationDepthScanIntervalTicks * 2)
                {
                    navigationDepthDecision = "stale";
                    FinishNavigation("depth-scan-stale", distance, spatialDistance, verticalError, heading);
                    return;
                }

                var cameraForward = navigationDepthCamera.WorldMatrix.Forward;
                cameraForward -= up * Vector3D.Dot(cameraForward, up);
                if (cameraForward.LengthSquared() < 0.0001)
                {
                    navigationDepthDecision = "invalid-camera-frame";
                    FinishNavigation("depth-camera-frame-invalid", distance, spatialDistance, verticalError, heading);
                    return;
                }
                cameraForward.Normalize();
                var cameraRight = Vector3D.Cross(cameraForward, up);
                if (cameraRight.LengthSquared() < 0.0001)
                {
                    navigationDepthDecision = "invalid-camera-frame";
                    FinishNavigation("depth-camera-frame-invalid", distance, spatialDistance, verticalError, heading);
                    return;
                }
                cameraRight.Normalize();
                var targetBearing = Math.Atan2(Vector3D.Dot(desired, cameraRight),
                    Vector3D.Dot(desired, cameraForward));
                // Only an actual detour is a side commitment. A previous clear
                // decision tracks the target bearing; carrying it forward as a
                // commitment can steer back into the obstacle when that bearing
                // changes on the next scan.
                var previousDetourHeading = navigationDepthDecision == "avoid"
                    ? navigationDepthChosenHeading : 0.0;
                var depthDecision = DepthAvoidancePolicy.SelectHeading(
                    targetBearing, NavigationDepthBeamAngles, navigationDepthClearances,
                    NavigationDepthRangeMeters, NavigationDepthRequiredClearanceMeters,
                    previousDetourHeading, navigationDepthDirectClearFrames);
                navigationDepthMinimumClearance = depthDecision.MinimumClearanceMeters;
                navigationDepthDirectClearance = depthDecision.DirectClearanceMeters;
                navigationDepthDirectClearFrames = depthDecision.ConsecutiveClearFrames;
                navigationDepthDecision = depthDecision.Status.ToString().ToLowerInvariant();
                if (!DepthAvoidancePolicy.IsNavigationPermitted(depthDecision.Status))
                {
                    var stopStatus = depthDecision.Status == DepthAvoidanceStatus.TargetOutOfView
                        ? "depth-target-out-of-view"
                        : depthDecision.Status == DepthAvoidanceStatus.NoClearCorridor
                            ? "depth-no-clear-corridor" : "depth-scan-invalid";
                    FinishNavigation(stopStatus, distance, spatialDistance, verticalError, heading);
                    return;
                }

                navigationDepthChosenHeading = depthDecision.HeadingRadians;
                var guidedDirection = cameraForward * Math.Cos(depthDecision.HeadingRadians) +
                                      cameraRight * Math.Sin(depthDecision.HeadingRadians);
                heading = Math.Atan2(Vector3D.Dot(guidedDirection, right),
                    Vector3D.Dot(guidedDirection, travelDirection));
                if (elapsedTicks % 60 == 0)
                    MyLog.Default.WriteLineAndConsole(
                        "Voidwright evidence: type=depth-controller operation=" + operationId +
                        " controllerId=" + controllerId + " cameraId=" + navigationDepthCameraId +
                        " status=" + navigationDepthDecision + " targetBearing=" + F(targetBearing) +
                        " chosenHeading=" + F(navigationDepthChosenHeading) +
                        " directClearance=" + F(navigationDepthDirectClearance) +
                        " directClearFrames=" + navigationDepthDirectClearFrames +
                        " minCorridorClearance=" + F(navigationDepthMinimumClearance) +
                        " selfGridHits=" + navigationDepthSelfHits +
                        " scanAgeTicks=" + (elapsedTicks - navigationDepthScanTick));
            }
            var headingMagnitude = Math.Abs(heading);
            lastNavigationHeadingError = heading;
            if (elapsedTicks >= timeoutTicks)
            {
                FinishNavigation("timeout", distance, spatialDistance, verticalError, heading);
                return;
            }
            // As the target leaves the forward travel cone, backing lets the truck
            // continue pursuing instead of committing to a wide loop. A broad
            // hysteresis band prevents forward/reverse mode chatter.
            var stallLimitTicks = navigationDepthCameraId > 0 ? 600 : 3600;
            if (elapsedTicks - lastProgressTick >= stallLimitTicks)
            {
                FinishNavigation(navigationDepthCameraId > 0 ? "depth-stalled" : "stalled",
                    distance, spatialDistance, verticalError, heading);
                return;
            }
            // The bounded straight-line heading-hold trial increased error with
            // the prior polarity. Forward correction uses the opposite sign;
            // reverse travel flips the mapping again.
            var steeringPolarity = navigationReversing ? -1 : 1;
            lastSteeringPolarity = steeringPolarity;
            var steering = headingMagnitude < 0.08
                ? 0 : depthMode
                    ? DepthAvoidancePolicy.SteeringCommand(heading, navigationReversing)
                    : Clamp(heading / (Math.PI / 2)) * steeringPolarity;
            lastSteeringCommand = steering;
            var desiredArrivalRadius = navigationDepthCameraId > 0 &&
                DepthArrivalPolicy.TryGetPlanarRadius(arrivalRadius, verticalError, out var planarRadius)
                    ? planarRadius : arrivalRadius;
            var desiredSpeed = Math.Min(maxSpeed,
                Math.Sqrt(Math.Max(0, distance - desiredArrivalRadius)));
            var targetApproachSpeed = Vector3D.Dot(planarVelocity, desired);
            if (navigationDepthCameraId > 0)
            {
                navigationDepthAwayTicks = targetApproachSpeed < -0.2
                    ? navigationDepthAwayTicks + 1 : 0;
                if (navigationDepthAwayTicks >= 120)
                {
                    FinishNavigation("depth-motion-divergence", distance, spatialDistance,
                        verticalError, heading);
                    return;
                }
            }
            // Total speed can be high while the vehicle is sliding sideways or
            // away from the objective. Regulate signed progress instead, while
            // retaining an independent ceiling for unsafe total velocity.
            var movingAway = targetApproachSpeed < -0.05;
            // Avoid full-power wheel-system inputs. Z is a forward/reverse button,
            // so scale propulsion with bounded pulse duty from signed progress.
            var speedError = desiredSpeed - Math.Max(0, targetApproachSpeed);
            var speedScale = desiredSpeed <= 0 ? 0 : Clamp(speedError / Math.Max(maxSpeed * 1.5, 0.1));
            var throttle = Math.Max(0, speedScale);
            if (movingAway && desiredSpeed > 0)
                throttle = Math.Max(throttle, 0.65);
            // The wheel speed-limit setting alone did not cap measured chassis
            // speed on V3R1 slopes. Apply the wheel brake just beyond a small
            // proportional tolerance instead of allowing a fixed 0.25 m/s
            // overshoot (which dwarfs low-speed calibration commands).
            var speedMargin = navigationDepthCameraId > 0
                ? Math.Max(0.025, maxSpeed * 0.05)
                : Math.Max(0.05, maxSpeed * 0.15);
            // Backward drift is a control error, not an overspeed condition:
            // keep propulsion available to recover instead of latching the
            // brake and guaranteeing that the truck cannot climb back toward
            // its target. The independent total-speed ceiling still bounds it.
            var brakingForOverspeed = targetApproachSpeed > desiredSpeed + speedMargin ||
                safetySpeed > maxSpeed + speedMargin;
            controller.GridWheels.Brake = brakingForOverspeed;
            var controllerForward = controller.WorldMatrix.Forward;
            controllerForward -= up * Vector3D.Dot(controllerForward, up);
            if (controllerForward.LengthSquared() > 0.0001) controllerForward.Normalize();
            var gridForward = controller.CubeGrid.WorldMatrix.Forward;
            gridForward -= up * Vector3D.Dot(gridForward, up);
            if (gridForward.LengthSquared() > 0.0001) gridForward.Normalize();
            lastControllerGridForwardAlignment = Vector3D.Dot(controllerForward, gridForward);
            lastForwardWheelInput = ForwardWheelInput();
            var driveDirection = navigationReversing ? -lastForwardWheelInput : lastForwardWheelInput;
            var driveDuty = brakingForOverspeed ? 0 : depthMode
                ? DepthAvoidancePolicy.LimitDriveDuty(throttle)
                : Math.Min(0.4, Math.Max(0, throttle));
            var drivePulsePeriodTicks = depthMode ? 20 : 10;
            var drivePulseTicks = (int)Math.Round(driveDuty * drivePulsePeriodTicks);
            var driveInput = drivePulseTicks > 0 && elapsedTicks % drivePulsePeriodTicks < drivePulseTicks
                ? driveDirection : 0;
            lastDriveInput = driveInput;
            lastDriveDuty = driveDuty;
            if (navigationDepthCameraId > 0)
            {
                navigationDepthNoMotionTicks = driveDuty >= 0.1 && speed < 0.05 &&
                    targetApproachSpeed < 0.05 && !brakingForOverspeed
                    ? navigationDepthNoMotionTicks + 1 : 0;
                if (navigationDepthNoMotionTicks >= NavigationDepthNoMotionTimeoutTicks)
                {
                    FinishNavigation("depth-actuation-stalled", distance, spatialDistance,
                        verticalError, heading);
                    return;
                }
            }
            controller.GridWheels.AngularVelocity = new Vector3((float)steering, 0, (float)driveInput);
            foreach (var wheel in controller.GridWheels.Wheels)
                wheel.PropulsionOverride = 0;
            if (elapsedTicks % 60 == 0)
                NavigationEvidence("running", distance, spatialDistance, verticalError, heading, speed,
                    desiredSpeed, brakingForOverspeed);
        }

        private string FormatDepthClearances()
        {
            var result = new StringBuilder();
            for (var beam = 0; beam < NavigationDepthBeamAngles.Length; beam++)
            {
                if (beam > 0) result.Append(',');
                result.Append(F(NavigationDepthBeamAngles[beam] * 180.0 / Math.PI));
                result.Append(':');
                result.Append(F(navigationDepthClearances[beam]));
            }
            return result.ToString();
        }

        private bool CaptureNavigationDepthFan(out string failure)
        {
            failure = "unavailable";
            MyEntity cameraEntity;
            if (!MyEntities.TryGetEntityById(navigationDepthCameraId, out cameraEntity) ||
                cameraEntity.Closed)
                return false;
            navigationDepthCamera = cameraEntity as IMyCameraBlock;
            if (navigationDepthCamera == null || navigationDepthCamera.CubeGrid == null ||
                navigationDepthCamera.CubeGrid.EntityId != controller.CubeGrid.EntityId ||
                MyAPIGateway.Physics == null)
                return false;

            try
            {
                var up = navigationUp;
                var matrix = navigationDepthCamera.WorldMatrix;
                var cameraForward = matrix.Forward;
                cameraForward -= up * Vector3D.Dot(cameraForward, up);
                if (cameraForward.LengthSquared() < 0.0001)
                {
                    failure = "invalid-camera-frame";
                    return false;
                }
                cameraForward.Normalize();
                var cameraRight = Vector3D.Cross(cameraForward, up);
                if (cameraRight.LengthSquared() < 0.0001)
                {
                    failure = "invalid-camera-frame";
                    return false;
                }
                cameraRight.Normalize();
                var origin = matrix.Translation + cameraForward * 1.5;
                var selfHits = 0;
                for (var beam = 0; beam < NavigationDepthBeamAngles.Length; beam++)
                {
                    var angle = NavigationDepthBeamAngles[beam];
                    var direction = cameraForward * Math.Cos(angle) + cameraRight * Math.Sin(angle);
                    var rayStart = origin;
                    var travelled = 0.0;
                    var clearance = NavigationDepthRangeMeters;
                    var resolved = false;
                    for (var attempt = 0; attempt < 4; attempt++)
                    {
                        var remaining = NavigationDepthRangeMeters - travelled;
                        if (remaining <= 0.05)
                        {
                            resolved = true;
                            break;
                        }
                        IHitInfo hit;
                        if (!MyAPIGateway.Physics.CastRay(rayStart, rayStart + direction * remaining, out hit) ||
                            hit == null)
                        {
                            clearance = NavigationDepthRangeMeters;
                            resolved = true;
                            break;
                        }

                        var hitDistance = Vector3D.Distance(rayStart, hit.Position);
                        if (!Finite(hitDistance) || hitDistance < 0 || hitDistance > remaining + 0.1)
                        {
                            failure = "invalid-ray-result";
                            return false;
                        }
                        if (hit.HitEntity != null && hit.HitEntity.EntityId == controller.CubeGrid.EntityId)
                        {
                            selfHits++;
                            var advance = Math.Max(0.05, hitDistance) + 0.05;
                            travelled += advance;
                            rayStart = hit.Position + direction * 0.05;
                            continue;
                        }

                        clearance = Math.Min(NavigationDepthRangeMeters, travelled + hitDistance);
                        resolved = true;
                        break;
                    }
                    if (!resolved) clearance = Math.Min(NavigationDepthRangeMeters, travelled);
                    if (!Finite(clearance) || clearance < 0 || clearance > NavigationDepthRangeMeters)
                    {
                        failure = "invalid-depth-clearance";
                        return false;
                    }
                    navigationDepthClearances[beam] = clearance;
                }

                navigationDepthSelfHits = selfHits;
                navigationDepthScanTick = elapsedTicks;
                navigationDepthScanReady = true;
                if (elapsedTicks <= 1)
                    MyLog.Default.WriteLineAndConsole(
                        "Voidwright evidence: type=depth-controller operation=" + operationId +
                        " controllerId=" + controllerId + " cameraId=" + navigationDepthCameraId +
                        " status=scan-ready beams=" + NavigationDepthBeamAngles.Length +
                        " rangeMeters=" + F(NavigationDepthRangeMeters) +
                        " requiredClearanceMeters=" + F(NavigationDepthRequiredClearanceMeters) +
                        " selfGridHits=" + navigationDepthSelfHits +
                        " cameraOrigin=" + V(origin) + " cameraForward=" + V(cameraForward));
                failure = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                failure = "raycast-exception-" + exception.GetType().Name;
                MyLog.Default.WriteLineAndConsole(
                    "Voidwright depth-controller failure: operation=" + operationId +
                    " cameraId=" + navigationDepthCameraId + " reason=" + failure);
                return false;
            }
        }

        private void FinishNavigation(string status, double distance, double spatialDistance,
                                      double verticalError, double heading)
        {
            Neutralize();
            terminal = true;
            if (navigationDepthCameraId > 0 && navigationDepthScanReady &&
                status == "depth-no-clear-corridor")
            {
                MyLog.Default.WriteLineAndConsole(
                    "Voidwright evidence: type=depth-controller operation=" + operationId +
                    " status=no-clear-corridor-fan beams=" + FormatDepthClearances());
            }
            var speed = controller == null || controller.CubeGrid == null || controller.CubeGrid.Physics == null
                ? 0.0 : controller.CubeGrid.Physics.LinearVelocity.Length();
            if (navigationDepthCameraId > 0 && navigationDepthPositionVelocityReady)
                speed = navigationDepthPositionVelocity.Length();
            var desiredArrivalRadius = navigationDepthCameraId > 0 &&
                DepthArrivalPolicy.TryGetPlanarRadius(arrivalRadius, verticalError, out var planarRadius)
                    ? planarRadius : arrivalRadius;
            var desiredSpeed = Math.Min(maxSpeed,
                Math.Sqrt(Math.Max(0, distance - desiredArrivalRadius)));
            NavigationEvidence(status, distance, spatialDistance, verticalError, heading, speed,
                desiredSpeed, speed > desiredSpeed + 0.1);
        }

        private void NavigationEvidence(string status, double distance, double spatialDistance,
                                        double verticalError, double heading, double speed,
                                        double desiredSpeed, bool brakingForOverspeed)
        {
            CaptureNavigationMotionSample(status);
            var position = controller == null ? Vector3D.Zero :
                navigationDepthCameraId > 0 && controller.CubeGrid != null && controller.CubeGrid.Physics != null
                    ? controller.CubeGrid.Physics.CenterOfMassWorld
                    : VehicleGroundReferencePosition();
            var wheelReferencePosition = controller == null ? Vector3D.Zero : VehicleGroundReferencePosition();
            var remoteControlPosition = controller == null
                ? Vector3D.Zero : controller.PositionComp.GetPosition();
            var controllerForward = controller == null
                ? Vector3D.Zero : controller.WorldMatrix.Forward;
            controllerForward -= navigationUp * Vector3D.Dot(controllerForward, navigationUp);
            if (controllerForward.LengthSquared() > 0.0001) controllerForward.Normalize();
            var forward = controllerForward;
            var gridForward = controller == null || controller.CubeGrid == null
                ? Vector3D.Zero : controller.CubeGrid.WorldMatrix.Forward;
            gridForward -= navigationUp * Vector3D.Dot(gridForward, navigationUp);
            if (gridForward.LengthSquared() > 0.0001) gridForward.Normalize();
            forward -= navigationUp * Vector3D.Dot(forward, navigationUp);
            if (forward.LengthSquared() > 0.0001) forward.Normalize();
            var reportedVelocity = controller == null || controller.CubeGrid == null || controller.CubeGrid.Physics == null
                ? Vector3D.Zero : new Vector3D(controller.CubeGrid.Physics.LinearVelocity);
            var controlVelocity = navigationDepthCameraId > 0 && navigationDepthPositionVelocityReady
                ? navigationDepthPositionVelocity : reportedVelocity;
            var planarVelocity = controlVelocity - navigationUp * Vector3D.Dot(controlVelocity, navigationUp);
            var reportedPlanarVelocity = reportedVelocity - navigationUp * Vector3D.Dot(reportedVelocity, navigationUp);
            var targetDirection = navigationTarget - position;
            targetDirection -= navigationUp * Vector3D.Dot(targetDirection, navigationUp);
            if (targetDirection.LengthSquared() > 0.0001) targetDirection.Normalize();
            var vehicleForward = controllerForward;
            lastControllerGridForwardAlignment = Vector3D.Dot(controllerForward, gridForward);
            var commandedTravelDirection = navigationReversing ? -vehicleForward : vehicleForward;
            var steeringWheels = 0;
            var averageSteerAngle = 0.0;
            var wheelEvidence = new StringBuilder();
            foreach (var wheel in controller.GridWheels.Wheels)
            {
                if (wheel.Steering)
                {
                    steeringWheels++;
                    averageSteerAngle += wheel.SteerAngle;
                }
                var relative = wheel.PositionComp.GetPosition() - position;
                if (wheelEvidence.Length > 0) wheelEvidence.Append(';');
                wheelEvidence.Append(wheel.EntityId).Append(':')
                    .Append(F(relative.X)).Append(',').Append(F(relative.Y)).Append(',').Append(F(relative.Z))
                    .Append(":prop=").Append(wheel.Propulsion ? '1' : '0')
                    .Append(":power=").Append(F(wheel.Power))
                    .Append(":brake=").Append(wheel.Brake ? '1' : '0')
                    .Append(":driveOverride=").Append(F(wheel.PropulsionOverride))
                    .Append(":steer=").Append(wheel.Steering ? '1' : '0')
                    .Append(":steerOverride=").Append(F(wheel.SteeringOverride))
                    .Append(":angle=").Append(F(wheel.SteerAngle));
            }
            if (steeringWheels > 0) averageSteerAngle /= steeringWheels;
            var angularVelocity = controller == null || controller.CubeGrid == null || controller.CubeGrid.Physics == null
                ? Vector3D.Zero : new Vector3D(controller.CubeGrid.Physics.AngularVelocity);
            var gravity = controller == null ? Vector3D.Zero : new Vector3D(controller.GetTotalGravity());
            var gridUp = controller == null || controller.CubeGrid == null
                ? Vector3D.Zero : controller.CubeGrid.WorldMatrix.Up;
            var depthCameraPose = string.Empty;
            if (navigationDepthCameraId > 0 && navigationDepthCamera != null &&
                navigationDepthCamera.CubeGrid != null)
            {
                var cameraMatrix = navigationDepthCamera.WorldMatrix;
                depthCameraPose = " depthCameraPosition=" + V(cameraMatrix.Translation) +
                    " depthCameraForward=" + V(cameraMatrix.Forward);
            }
            MyLog.Default.WriteLineAndConsole(
                "Voidwright evidence: type=server-control operation=" + operationId +
                " controllerId=" + controllerId + " action=" +
                (commandKind == "drive-recorded-route"
                    ? "follow-recorded-route waypoint=" + routeWaypointIndex
                    : commandKind + " zone=" + navigationZone) +
                " status=" + status + " elapsedTicks=" + elapsedTicks +
                " targetDistance=" + F(distance) + " bestTargetDistance=" + F(bestTargetDistance) +
                " initialTargetDistance=" + F(initialTargetDistance) +
                " spatialDistance=" + F(spatialDistance) + " verticalError=" + F(verticalError) +
                " arrivalMetric=" + (navigationDepthCameraId > 0 ? "world-3d" : "ground-plane") +
                " worldArrivalRadius=" + F(arrivalRadius) +
                " planarArrivalRadius=" + F(navigationDepthCameraId > 0 &&
                    DepthArrivalPolicy.TryGetPlanarRadius(arrivalRadius, verticalError, out var evidencePlanarRadius)
                    ? evidencePlanarRadius : arrivalRadius) +
                " headingError=" + F(heading) + " speed=" + F(speed) +
                " speedReference=" + (navigationDepthCameraId > 0 && navigationDepthPositionVelocityReady
                    ? "20-tick-center-of-mass-displacement" : "physics-linear-velocity") +
                " reportedLinearSpeed=" + F(reportedVelocity.Length()) +
                " positionDerivedSpeed=" + (navigationDepthPositionVelocityReady
                    ? F(navigationDepthPositionVelocity.Length()) : "unavailable") +
                " forwardSpeed=" + F(Vector3D.Dot(planarVelocity, forward)) +
                " reportedForwardSpeed=" + F(Vector3D.Dot(reportedPlanarVelocity, forward)) +
                " vehicleForwardSpeed=" + F(Vector3D.Dot(planarVelocity, vehicleForward)) +
                " controllerForwardSpeed=" + F(Vector3D.Dot(planarVelocity, controllerForward)) +
                " commandedTravelSpeed=" + F(Vector3D.Dot(planarVelocity, commandedTravelDirection)) +
                " targetApproachSpeed=" + F(Vector3D.Dot(planarVelocity, targetDirection)) +
                " desiredSpeed=" + F(desiredSpeed) + " braking=" + brakingForOverspeed +
                " steerCommand=" + F(lastSteeringCommand) +
                " driveInput=" + F(lastDriveInput) +
                " driveDuty=" + F(lastDriveDuty) +
                " yawRate=" + F(Vector3D.Dot(angularVelocity, navigationUp)) +
                " navigationUp=" + V(navigationUp) + " gravity=" + V(gravity) +
                " gridUp=" + V(gridUp) +
                " gridForward=" + V(gridForward) +
                " controllerForward=" + V(controllerForward) +
                " controllerGridForwardAlignment=" + F(lastControllerGridForwardAlignment) +
                " forwardWheelInput=" + F(lastForwardWheelInput) +
                " steeringPolarity=" + F(lastSteeringPolarity) +
                " navigationReversing=" + navigationReversing +
                (navigationDepthCameraId > 0
                    ? " depthCameraId=" + navigationDepthCameraId +
                      " depthDecision=" + navigationDepthDecision +
                      " depthHeading=" + F(navigationDepthChosenHeading) +
                      " depthDirectClearance=" + F(navigationDepthDirectClearance) +
                      " depthDirectClearFrames=" + navigationDepthDirectClearFrames +
                      " depthMinimumClearance=" + F(navigationDepthMinimumClearance) +
                      " depthScanAgeTicks=" + (elapsedTicks - navigationDepthScanTick) +
                      " depthSelfGridHits=" + navigationDepthSelfHits + depthCameraPose
                    : "") +
                " steeringWheels=" + steeringWheels + " avgSteerAngle=" + F(averageSteerAngle) +
                " steerWheels=[" + wheelEvidence + "]" +
                " vehicleForward=" + V(vehicleForward) + " targetDirection=" + V(targetDirection) +
                " positionReference=" + (navigationDepthCameraId > 0 ? "center-of-mass" : "wheel-center-mean") +
                " position=" + V(position) + " wheelReferencePosition=" + V(wheelReferencePosition) +
                " remoteControlPosition=" + V(remoteControlPosition) +
                " comPosition=" + V(controller.CubeGrid.Physics.CenterOfMassWorld) +
                " pathLengthMeters=" + F(navigationPathLengthMeters) +
                " comDisplacementMeters=" + F(Vector3D.Distance(
                    navigationPathStartCenterOfMass, controller.CubeGrid.Physics.CenterOfMassWorld)) +
                " closestApproachComMeters=" + F(navigationClosestApproachMeters));
        }

        private void UpdateNavigationPathMetrics()
        {
            if (!navigationPathReady || controller == null || controller.CubeGrid == null ||
                controller.CubeGrid.Physics == null) return;

            var centerOfMass = controller.CubeGrid.Physics.CenterOfMassWorld;
            navigationPathLengthMeters += Vector3D.Distance(navigationPathLastCenterOfMass, centerOfMass);
            navigationPathLastCenterOfMass = centerOfMass;
            navigationClosestApproachMeters = Math.Min(navigationClosestApproachMeters,
                PlanarDistance(navigationTarget, centerOfMass, navigationUp));
        }

        private void CaptureNavigationMotionSample(string reason)
        {
            if (controller == null || controller.CubeGrid == null || controller.CubeGrid.Physics == null ||
                (elapsedTicks == navigationMotionSampleTick && reason == "running")) return;

            var physics = controller.CubeGrid.Physics;
            var gridMatrix = controller.CubeGrid.WorldMatrix;
            var physicsMatrix = physics.GetWorldMatrix();
            var gridOrigin = gridMatrix.Translation;
            var centerOfMass = physics.CenterOfMassWorld;
            var gridPositionComponent = controller.CubeGrid.PositionComp.GetPosition();
            var controllerPositionComponent = controller.PositionComp.GetPosition();
            var wheelReference = VehicleGroundReferencePosition();
            var velocity = new Vector3D(physics.LinearVelocity);
            var angularVelocity = new Vector3D(physics.AngularVelocity);
            var yawRateAboutNavigationUp = navigationUp.LengthSquared() > 0.0001
                ? Vector3D.Dot(angularVelocity, Vector3D.Normalize(navigationUp)) : double.NaN;
            var steeringWheelCount = 0;
            var signedSteerAngleSum = 0.0;
            var steeringWheelEvidence = new StringBuilder();
            if (controller.GridWheels != null)
            {
                foreach (var wheel in controller.GridWheels.Wheels)
                {
                    if (!wheel.Steering) continue;
                    if (steeringWheelEvidence.Length > 0) steeringWheelEvidence.Append(';');
                    steeringWheelEvidence.Append(wheel.EntityId).Append(':')
                        .Append(F(wheel.SteerAngle)).Append('/')
                        .Append(F(wheel.SteeringOverride)).Append('/')
                        .Append(wheel.InvertSteer ? '1' : '0');
                    signedSteerAngleSum += wheel.SteerAngle;
                    steeringWheelCount++;
                }
            }
            var meanSignedSteerAngle = steeringWheelCount > 0
                ? signedSteerAngleSum / steeringWheelCount : double.NaN;
            var simulationSeconds = MyAPIGateway.Session == null
                ? double.NaN : MyAPIGateway.Session.ElapsedPlayTime.TotalSeconds;
            var sampleTicks = navigationMotionSampleReady
                ? elapsedTicks - navigationMotionSampleTick : 0;
            var sampleSeconds = navigationMotionSampleReady
                ? simulationSeconds - navigationMotionSampleTimeSeconds : double.NaN;
            var nominalTickSeconds = sampleTicks / 60.0;
            var derivedGridOriginVelocity = Vector3D.Zero;
            var derivedCenterOfMassVelocity = Vector3D.Zero;
            var derivedWheelReferenceVelocity = Vector3D.Zero;
            var derivedGridOriginVelocityAt60Hz = Vector3D.Zero;
            var derivedCenterOfMassVelocityAt60Hz = Vector3D.Zero;
            var derivedWheelReferenceVelocityAt60Hz = Vector3D.Zero;
            var measuredGridOriginSpeed = double.NaN;
            var measuredCenterOfMassSpeed = double.NaN;
            var measuredWheelReferenceSpeed = double.NaN;
            var measuredCenterOfMassSpeedAt60Hz = double.NaN;
            var reportedComVelocityError = double.NaN;
            var averageReportedLinearVelocity = Vector3D.Zero;
            var averageReportedLinearSpeed = double.NaN;
            var averageReportedComVelocityError = double.NaN;
            var originPointVelocity = Vector3D.Zero;
            var comPointVelocity = Vector3D.Zero;
            var wheelPointVelocity = Vector3D.Zero;
            var pointVelocityError = string.Empty;

            if (navigationMotionSampleReady && sampleTicks > 0)
            {
                var gridOriginDelta = gridOrigin - navigationMotionSampleGridOrigin;
                var centerOfMassDelta = centerOfMass - navigationMotionSampleCenterOfMass;
                var wheelReferenceDelta = wheelReference - navigationMotionSampleWheelReference;
                if (Finite(sampleSeconds) && sampleSeconds > 0)
                {
                    derivedGridOriginVelocity = gridOriginDelta / sampleSeconds;
                    derivedCenterOfMassVelocity = centerOfMassDelta / sampleSeconds;
                    derivedWheelReferenceVelocity = wheelReferenceDelta / sampleSeconds;
                    measuredGridOriginSpeed = derivedGridOriginVelocity.Length();
                    measuredCenterOfMassSpeed = derivedCenterOfMassVelocity.Length();
                    measuredWheelReferenceSpeed = derivedWheelReferenceVelocity.Length();
                    reportedComVelocityError = Vector3D.Distance(derivedCenterOfMassVelocity, velocity);
                    try
                    {
                        originPointVelocity = new Vector3D(physics.GetVelocityAtPoint(gridOrigin));
                        comPointVelocity = new Vector3D(physics.GetVelocityAtPoint(centerOfMass));
                        wheelPointVelocity = new Vector3D(physics.GetVelocityAtPoint(wheelReference));
                    }
                    catch (Exception exception)
                    {
                        pointVelocityError = exception.GetType().Name;
                    }
                }
                if (nominalTickSeconds > 0)
                {
                    derivedGridOriginVelocityAt60Hz = gridOriginDelta / nominalTickSeconds;
                    derivedCenterOfMassVelocityAt60Hz = centerOfMassDelta / nominalTickSeconds;
                    derivedWheelReferenceVelocityAt60Hz = wheelReferenceDelta / nominalTickSeconds;
                    measuredCenterOfMassSpeedAt60Hz = derivedCenterOfMassVelocityAt60Hz.Length();
                }
            }

            if (navigationMotionIntegratedSeconds > 0)
            {
                averageReportedLinearVelocity =
                    navigationMotionIntegratedLinearVelocity / navigationMotionIntegratedSeconds;
                averageReportedLinearSpeed = averageReportedLinearVelocity.Length();
                if (navigationMotionSampleReady && Finite(sampleSeconds) && sampleSeconds > 0)
                    averageReportedComVelocityError = Vector3D.Distance(
                        derivedCenterOfMassVelocity, averageReportedLinearVelocity);
            }

            if (navigationDepthCameraId > 0 && Finite(simulationSeconds))
                UpdateNavigationDepthPositionVelocity(centerOfMass, simulationSeconds);

            var distanceToGridOrigin = PlanarDistance(navigationTarget, gridOrigin, navigationUp);
            var distanceToCenterOfMass = PlanarDistance(navigationTarget, centerOfMass, navigationUp);
            var distanceToWheelReference = PlanarDistance(navigationTarget, wheelReference, navigationUp);
            var physicsApi = MyAPIGateway.Physics;
            MyLog.Default.WriteLineAndConsole(
                // VRageCage's durable operation receipt currently retains the
                // server-control evidence family, but not a new event type.
                // Keep these samples inside that existing allowlisted family
                // so the structured receipt can carry the measurement proof.
                "Voidwright evidence: type=server-control operation=" + operationId +
                " action=motion-telemetry sample=" + reason + " controllerId=" + controller.EntityId +
                " gridId=" + controller.CubeGrid.EntityId +
                " runId=" + (Environment.GetEnvironmentVariable("VRAGECAGE_LAB_RUN_ID") ?? string.Empty) +
                " sampleSequence=" + navigationMotionSampleSequence++ +
                " capturedAtUtc=" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) +
                " elapsedTicks=" + elapsedTicks +
                " elapsedSimulationSeconds=" + F6(simulationSeconds) +
                " sampleIntervalTicks=" + sampleTicks +
                " sampleIntervalSimulationSeconds=" + F6(sampleSeconds) +
                " nominalTickSecondsAt60Hz=" + F6(nominalTickSeconds) +
                " simulationRatio=" + (physicsApi == null ? "unavailable" : F6(physicsApi.SimulationRatio)) +
                " serverSimulationRatio=" + (physicsApi == null ? "unavailable" : F6(physicsApi.ServerSimulationRatio)) +
                " physicsStepsLastSecond=" + (physicsApi == null ? -1 : physicsApi.StepsLastSecond) +
                " targetWorld=" + V(navigationTarget) +
                " targetDistanceGridOriginPlanar=" + F6(distanceToGridOrigin) +
                " targetDistanceComPlanar=" + F6(distanceToCenterOfMass) +
                " targetDistanceWheelReferencePlanar=" + F6(distanceToWheelReference) +
                " gridOriginWorld=" + V6(gridOrigin) +
                " gridPositionComponentWorld=" + V6(gridPositionComponent) +
                " physicsWorldOrigin=" + V6(physicsMatrix.Translation) +
                " controllerPositionComponentWorld=" + V6(controllerPositionComponent) +
                " centerOfMassWorld=" + V6(centerOfMass) +
                " wheelReferenceWorld=" + V6(wheelReference) +
                " gridForwardWorld=" + V(gridMatrix.Forward) +
                " gridUpWorld=" + V(gridMatrix.Up) +
                " gridRightWorld=" + V(gridMatrix.Right) +
                " controllerForwardWorld=" + V(controller.WorldMatrix.Forward) +
                " physicsWorldForward=" + V(physicsMatrix.Forward) +
                " linearVelocityWorld=" + V6(velocity) +
                " reportedLinearSpeed=" + F6(velocity.Length()) +
                " angularVelocityWorld=" + V(angularVelocity) +
                " yawRateAboutNavigationUp=" + F6(yawRateAboutNavigationUp) +
                " priorHeadingErrorRad=" + F6(lastNavigationHeadingError) +
                " lastAppliedSteerCommand=" + F6(lastSteeringCommand) +
                " lastAppliedDriveInput=" + F6(lastDriveInput) +
                " lastDriveDuty=" + F6(lastDriveDuty) +
                " brake=" + controller.GridWheels.Brake +
                " handBrake=" + controller.GridWheels.HandBrake +
                " meanSignedSteerAngleRad=" + F6(meanSignedSteerAngle) +
                " steeringWheels=[" + steeringWheelEvidence + "]" +
                " intervalAverageLinearVelocityWorld=" + V6(averageReportedLinearVelocity) +
                " intervalAverageLinearSpeed=" + F6(averageReportedLinearSpeed) +
                " intervalAverageComVelocityError=" + F6(averageReportedComVelocityError) +
                " positionVelocityControlWorld=" + V6(navigationDepthPositionVelocity) +
                " positionVelocityControlReady=" + navigationDepthPositionVelocityReady +
                " positionVelocityControlSpeed=" + F6(navigationDepthPositionVelocity.Length()) +
                " physicsIsStatic=" + physics.IsStatic +
                " physicsIsKinematic=" + physics.IsKinematic +
                " physicsIsMoving=" + physics.IsMoving +
                " physicsEnabled=" + physics.Enabled +
                " derivedGridOriginVelocityWorld=" + V6(derivedGridOriginVelocity) +
                " derivedGridOriginSpeed=" + F6(measuredGridOriginSpeed) +
                " derivedGridOriginVelocityAt60Hz=" + V6(derivedGridOriginVelocityAt60Hz) +
                " velocityAtGridOriginWorld=" + V6(originPointVelocity) +
                " derivedComVelocityWorld=" + V6(derivedCenterOfMassVelocity) +
                " derivedComSpeed=" + F6(measuredCenterOfMassSpeed) +
                " derivedComVelocityAt60Hz=" + V6(derivedCenterOfMassVelocityAt60Hz) +
                " derivedComSpeedAt60Hz=" + F6(measuredCenterOfMassSpeedAt60Hz) +
                " velocityAtComWorld=" + V6(comPointVelocity) +
                " reportedComVelocityError=" + F6(reportedComVelocityError) +
                " derivedWheelReferenceVelocityWorld=" + V6(derivedWheelReferenceVelocity) +
                " derivedWheelReferenceSpeed=" + F6(measuredWheelReferenceSpeed) +
                " derivedWheelReferenceVelocityAt60Hz=" + V6(derivedWheelReferenceVelocityAt60Hz) +
                " velocityAtWheelReferenceWorld=" + V6(wheelPointVelocity) +
                " pointVelocityReadError=" + (string.IsNullOrEmpty(pointVelocityError) ? "none" : pointVelocityError));

            navigationMotionSampleTick = elapsedTicks;
            navigationMotionSampleTimeSeconds = simulationSeconds;
            navigationMotionSampleGridOrigin = gridOrigin;
            navigationMotionSampleCenterOfMass = centerOfMass;
            navigationMotionSampleWheelReference = wheelReference;
            navigationMotionSampleReady = true;
            navigationMotionIntegratedLinearVelocity = Vector3D.Zero;
            navigationMotionIntegratedSeconds = 0;
        }

        private void AccumulateNavigationLinearVelocity()
        {
            if (controller == null || controller.CubeGrid == null || controller.CubeGrid.Physics == null ||
                MyAPIGateway.Session == null) return;

            var currentTime = MyAPIGateway.Session.ElapsedPlayTime.TotalSeconds;
            var currentVelocity = new Vector3D(controller.CubeGrid.Physics.LinearVelocity);
            if (!navigationMotionVelocityIntegratorReady)
            {
                navigationMotionPreviousLinearVelocity = currentVelocity;
                navigationMotionPreviousVelocityTimeSeconds = currentTime;
                navigationMotionVelocityIntegratorReady = true;
                return;
            }

            var deltaSeconds = currentTime - navigationMotionPreviousVelocityTimeSeconds;
            if (Finite(deltaSeconds) && deltaSeconds > 0 && deltaSeconds <= 0.25)
            {
                navigationMotionIntegratedLinearVelocity +=
                    (navigationMotionPreviousLinearVelocity + currentVelocity) * (0.5 * deltaSeconds);
                navigationMotionIntegratedSeconds += deltaSeconds;
            }
            else if (!Finite(deltaSeconds) || deltaSeconds < 0 || deltaSeconds > 0.25)
            {
                // Do not bridge pauses, time jumps, or dropped update intervals
                // with an assumed velocity; start a fresh comparable window.
                navigationMotionIntegratedLinearVelocity = Vector3D.Zero;
                navigationMotionIntegratedSeconds = 0;
            }

            navigationMotionPreviousLinearVelocity = currentVelocity;
            navigationMotionPreviousVelocityTimeSeconds = currentTime;
        }

        private void UpdateNavigationDepthPositionVelocity(Vector3D centerOfMass, double simulationSeconds)
        {
            if (!navigationDepthVelocityAnchorReady)
            {
                navigationDepthVelocityAnchorPosition = centerOfMass;
                navigationDepthVelocityAnchorTimeSeconds = simulationSeconds;
                navigationDepthVelocityAnchorTick = elapsedTicks;
                navigationDepthVelocityAnchorReady = true;
                return;
            }

            if (elapsedTicks - navigationDepthVelocityAnchorTick < 20) return;
            var intervalSeconds = simulationSeconds - navigationDepthVelocityAnchorTimeSeconds;
            if (Finite(intervalSeconds) && intervalSeconds > 0)
            {
                navigationDepthPositionVelocity =
                    (centerOfMass - navigationDepthVelocityAnchorPosition) / intervalSeconds;
                navigationDepthPositionVelocityReady = true;
            }

            navigationDepthVelocityAnchorPosition = centerOfMass;
            navigationDepthVelocityAnchorTimeSeconds = simulationSeconds;
            navigationDepthVelocityAnchorTick = elapsedTicks;
        }

        private static double PlanarDistance(Vector3D target, Vector3D position, Vector3D up)
        {
            var offset = target - position;
            return (offset - up * Vector3D.Dot(offset, up)).Length();
        }

        private static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static string DescribeWheelBrakeState(MyShipController observedController)
        {
            if (observedController == null || observedController.GridWheels == null) return "unavailable";
            var state = new StringBuilder();
            foreach (var wheel in observedController.GridWheels.Wheels)
            {
                if (state.Length > 0) state.Append(';');
                state.Append(wheel.EntityId.ToString(CultureInfo.InvariantCulture))
                    .Append(":activeBrake=").Append(wheel.Brake ? "1" : "0")
                    .Append(":handbrake=").Append(wheel.Handbrake ? "1" : "0")
                    .Append(":brakingEnabled=").Append(wheel.BrakingEnabled ? "1" : "0")
                    .Append(":parkingEnabled=").Append(wheel.IsParkingEnabled ? "1" : "0");
            }
            return state.Length == 0 ? "none" : state.ToString();
        }

        private static double Clamp(double value)
        {
            return value < -1 ? -1 : value > 1 ? 1 : value;
        }

        private double Displacement()
        {
            return controller == null ? 0 : Vector3D.Distance(startPosition, VehicleGroundReferencePosition());
        }

        private double ForwardDisplacement()
        {
            if (controller == null || startForward.LengthSquared() < 0.0001) return 0;
            return Vector3D.Dot(VehicleGroundReferencePosition() - startPosition, startForward);
        }

        // The Remote Control is mounted off-center. When the truck yaws, its world
        // position orbits the chassis and can look like metres of translation even
        // when the vehicle barely moved. Average wheel positions for a stable
        // ground-plane reference in navigation and movement receipts.
        private Vector3D VehicleGroundReferencePosition()
        {
            if (controller == null || controller.GridWheels == null)
                return controller == null ? Vector3D.Zero : controller.PositionComp.GetPosition();

            var total = Vector3D.Zero;
            var count = 0;
            foreach (var wheel in controller.GridWheels.Wheels)
            {
                if (wheel == null || wheel.PositionComp == null) continue;
                total += wheel.PositionComp.GetPosition();
                count++;
            }

            return count == 0 ? controller.PositionComp.GetPosition() : total / count;
        }

        private double ForwardWheelInput()
        {
            if (controller == null || controller.CubeGrid == null) return 1;
            var controllerForward = controller.WorldMatrix.Forward;
            var gridForward = controller.CubeGrid.WorldMatrix.Forward;
            if (controllerForward.LengthSquared() < 0.0001 || gridForward.LengthSquared() < 0.0001)
                return 1;
            controllerForward.Normalize();
            gridForward.Normalize();
            // MyGridWheelSystem maps Z to grid-forward/backward, then its
            // per-suspension InitControl() mapping handles each wheel's mounting.
            // The isolated initialized Red Truck test showed this RC/grid frame
            // still needs the opposite sign to travel along the RC's physical nose.
            return Vector3D.Dot(controllerForward, gridForward) < 0 ? -1 : 1;
        }

        private void Finish(string status, double displacement)
        {
            Neutralize();
            terminal = true;
            var speed = controller == null || controller.CubeGrid == null || controller.CubeGrid.Physics == null
                ? 0 : controller.CubeGrid.Physics.LinearVelocity.Length();
            Evidence(status, displacement, speed);
        }

        private void Neutralize()
        {
            lastSteeringCommand = 0;
            lastDriveInput = 0;
            lastDriveDuty = 0;
            if (commandKind == "direct-maneuver")
            {
                if (directManeuverExecutor != null && directManeuverBroker != null &&
                    directManeuverIdentity != null && directManeuverBroker.SamplingRequired)
                    EmergencyStopDirectManeuver("direct-maneuver plugin teardown");
                return;
            }
            if (controller != null && !controller.MarkedForClose && controller.GridWheels != null)
            {
                controller.GridWheels.AngularVelocity = Vector3.Zero;
                foreach (var wheel in controller.GridWheels.Wheels)
                {
                    wheel.PropulsionOverride = 0;
                    float originalSpeedLimit;
                    if (originalWheelSpeedLimits.TryGetValue(wheel.EntityId, out originalSpeedLimit))
                        wheel.SpeedLimit = originalSpeedLimit;
                    float originalPower;
                    if (originalWheelPower.TryGetValue(wheel.EntityId, out originalPower))
                        wheel.Power = originalPower;
                    float originalSteerLimit;
                    if (originalWheelSteerLimits.TryGetValue(wheel.EntityId, out originalSteerLimit))
                        wheel.MaxSteerAngle = originalSteerLimit;
                    bool originalSteering;
                    if (originalWheelSteering.TryGetValue(wheel.EntityId, out originalSteering))
                        wheel.Steering = originalSteering;
                }
                controller.GridWheels.Brake = true;
                if (originalHandBrake.HasValue)
                    controller.GridWheels.HandBrake = originalHandBrake.Value;
            }
            originalHandBrake = null;
            originalWheelSpeedLimits.Clear();
            originalWheelPower.Clear();
            originalWheelSteerLimits.Clear();
            originalWheelSteering.Clear();
        }

        private void Evidence(string status, double displacement, double speed)
        {
            var position = controller == null ? Vector3D.Zero : VehicleGroundReferencePosition();
            var forward = controller == null ? Vector3D.Zero : controller.WorldMatrix.Forward;
            if (forward.LengthSquared() > 0.0001) forward.Normalize();
            var gridForward = controller == null || controller.CubeGrid == null
                ? Vector3D.Zero : controller.CubeGrid.WorldMatrix.Forward;
            if (gridForward.LengthSquared() > 0.0001) gridForward.Normalize();
            var controllerGridForwardAlignment = Vector3D.Dot(forward, gridForward);
            var velocity = controller == null || controller.CubeGrid == null || controller.CubeGrid.Physics == null
                ? Vector3D.Zero : new Vector3D(controller.CubeGrid.Physics.LinearVelocity);
            var angularVelocity = controller == null || controller.CubeGrid == null || controller.CubeGrid.Physics == null
                ? Vector3D.Zero : new Vector3D(controller.CubeGrid.Physics.AngularVelocity);
            var forwardSpeed = Vector3D.Dot(velocity, forward);
            var wheelCount = 0;
            var averageWheelPower = 0.0;
            var averageWheelSpeedLimit = 0.0;
            var steeringWheelCount = 0;
            var averageAbsoluteSteerAngle = 0.0;
            var wheelEvidence = new StringBuilder();
            if (controller != null && controller.GridWheels != null)
            {
                foreach (var wheel in controller.GridWheels.Wheels)
                {
                    averageWheelPower += wheel.Power;
                    averageWheelSpeedLimit += wheel.SpeedLimit;
                    if (wheelEvidence.Length > 0) wheelEvidence.Append(';');
                    wheelEvidence.Append(wheel.EntityId).Append(":invertProp=")
                        .Append(wheel.InvertPropulsion ? '1' : '0')
                        .Append(":invertSteer=").Append(wheel.InvertSteer ? '1' : '0')
                        .Append(":prop=").Append(wheel.Propulsion ? '1' : '0')
                        .Append(":steer=").Append(wheel.Steering ? '1' : '0')
                        .Append(":driveOverride=").Append(F(wheel.PropulsionOverride))
                        .Append(":steerOverride=").Append(F(wheel.SteeringOverride))
                        .Append(":steerAngle=").Append(F(wheel.SteerAngle))
                        .Append(":power=").Append(F(wheel.Power))
                        .Append(":up=").Append(V(wheel.WorldMatrix.Up));
                    if (wheel.Steering)
                    {
                        steeringWheelCount++;
                        averageAbsoluteSteerAngle += Math.Abs(wheel.SteerAngle);
                    }
                    wheelCount++;
                }
            }
            if (wheelCount > 0)
            {
                averageWheelPower /= wheelCount;
                averageWheelSpeedLimit /= wheelCount;
            }
            if (steeringWheelCount > 0)
                averageAbsoluteSteerAngle /= steeringWheelCount;
            MyLog.Default.WriteLineAndConsole(
                "Voidwright evidence: type=server-control operation=" + operationId +
                " controllerId=" + controllerId + " status=" + status +
                " direction=" + (driveReverse ? "reverse" : "forward") +
                " elapsedTicks=" + elapsedTicks +
                " displacement=" + Math.Round(displacement, 3) +
                " forwardDisplacement=" + F(ForwardDisplacement()) +
                " speed=" + Math.Round(speed, 3) +
                " forwardSpeed=" + F(forwardSpeed) +
                " angularVelocity=" + V(angularVelocity) +
                " handBrake=" + (controller != null && controller.GridWheels != null && controller.GridWheels.HandBrake) +
                " brake=" + (controller != null && controller.GridWheels != null && controller.GridWheels.Brake) +
                " steerCommand=" + F(lastSteeringCommand) +
                " steeringPolarity=" + F(lastSteeringPolarity) +
                " controllerGridForwardAlignment=" + F(controllerGridForwardAlignment) +
                " forwardWheelInput=" + F(ForwardWheelInput()) +
                // GridWheels exposes the internal MyMotorSuspension.Power ratio.
                // Its public API/terminal adapter multiplies this value by 100.
                " wheelPowerRatio=" + F(averageWheelPower) +
                " wheelPowerPercent=" + F(averageWheelPower * 100.0) +
                " wheelSpeedLimitKmh=" + F(averageWheelSpeedLimit) +
                " wheels=" + wheelCount +
                " steeringWheels=" + steeringWheelCount +
                " avgAbsSteerAngleRad=" + F(averageAbsoluteSteerAngle) +
                " wheelState=[" + wheelEvidence + "]" +
                " position=" + V(position) +
                " forward=" + V(forward));
        }

    }
}
