using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Voidwright.Core
{
    /// <summary>Immutable identity map for the reviewed local vehicle test fixture.</summary>
    public static class LocalVehicleTestBindingPolicy
    {
        public const string FreshInstance = "land-local-test-v4r1-blue-20261022-a";
        public const long GridId = 105034224363529183L;
        public const long ControllerId = 83468039688685570L;
        public const long CameraId = 0L;
        public const long FreshInstanceCameraId = 91077977706459680L;

        public static long CameraIdFor(string instance)
        {
            return string.Equals(instance, FreshInstance, StringComparison.Ordinal)
                ? FreshInstanceCameraId : CameraId;
        }

        public static readonly long[] WheelIds =
        {
            72448287472244394L, 102872810511526201L,
            87663115116726466L, 95379321851010785L
        };

        public static readonly long[] WheelGridIds =
        {
            130825891980829223L, 127988954682183794L,
            141480516845941196L, 95733545729686414L
        };
        // Exact published mods in the V4R1 source save.
        public static readonly ulong[] FixtureModIds =
        { 3795893318UL, 2298956701UL, 1090562767UL, 3015328630UL, 516770964UL };

        public static bool HasExactModAllowlist(IList<ulong> observed)
        {
            if (observed == null || observed.Count != FixtureModIds.Length) return false;
            var seen = new HashSet<ulong>();
            foreach (var id in observed)
            {
                var expected = false;
                foreach (var allowed in FixtureModIds)
                    if (id == allowed) { expected = true; break; }
                if (!expected || !seen.Add(id)) return false;
            }
            return seen.Count == FixtureModIds.Length;
        }

        public static bool IsExactRun(string instance, string expectedRunId, string observedRunId,
            long gridId, long controllerId, long cameraId)
        {
            return string.Equals(instance, FreshInstance, StringComparison.Ordinal) &&
                Hex32(expectedRunId) && string.Equals(expectedRunId, observedRunId, StringComparison.Ordinal) &&
                gridId == GridId && controllerId == ControllerId && cameraId == CameraIdFor(instance);
        }

        public static bool HasExactWheelMap(IList<long> wheelIds, IList<long> wheelGridIds)
        {
            if (wheelIds == null || wheelGridIds == null || wheelIds.Count != WheelIds.Length ||
                wheelGridIds.Count != WheelGridIds.Length) return false;
            var seen = new HashSet<int>();
            for (var index = 0; index < wheelIds.Count; index++)
            {
                var found = -1;
                for (var expected = 0; expected < WheelIds.Length; expected++)
                    if (WheelIds[expected] == wheelIds[index] && WheelGridIds[expected] == wheelGridIds[index])
                    { found = expected; break; }
                if (found < 0 || !seen.Add(found)) return false;
            }
            return seen.Count == WheelIds.Length;
        }

        public static bool AreSubgridsStationary(IList<double> linearSpeeds, IList<double> angularSpeeds,
            IList<double> drifts, double maximumSpeed = 0.02, double maximumAngularSpeed = 0.02,
            double maximumDrift = 0.05)
        {
            if (linearSpeeds == null || angularSpeeds == null || drifts == null || linearSpeeds.Count == 0 ||
                linearSpeeds.Count != angularSpeeds.Count || linearSpeeds.Count != drifts.Count) return false;
            for (var index = 0; index < linearSpeeds.Count; index++)
                if (!Finite(linearSpeeds[index]) || !Finite(angularSpeeds[index]) || !Finite(drifts[index]) ||
                    linearSpeeds[index] < 0 || angularSpeeds[index] < 0 || drifts[index] < 0 ||
                    linearSpeeds[index] > maximumSpeed || angularSpeeds[index] > maximumAngularSpeed ||
                    drifts[index] > maximumDrift) return false;
            return true;
        }

        public static bool IsForwardOnly(LocalVehicleTestCommandKind kind, double propulsion)
        {
            return kind != LocalVehicleTestCommandKind.Propulsion && kind != LocalVehicleTestCommandKind.Drive ||
                Finite(propulsion) && propulsion >= 0;
        }

        public static bool IsDistinctStopSample(long previousTimestamp, long currentTimestamp)
        {
            return currentTimestamp > 0 && currentTimestamp > previousTimestamp;
        }

        public static bool ObserveRotorPosition(IDictionary<long, double[]> references, long gridId,
            double[] position, double maximumDrift, bool resetWindowOnExcess, out double drift)
        {
            drift = double.NaN;
            if (references == null || gridId <= 0 || position == null || position.Length != 3 ||
                !Finite(position[0]) || !Finite(position[1]) || !Finite(position[2]) ||
                !Finite(maximumDrift) || maximumDrift < 0) return false;
            double[] reference;
            if (!references.TryGetValue(gridId, out reference))
            {
                references[gridId] = new[] { position[0], position[1], position[2] };
                drift = 0;
                return true;
            }
            if (reference == null || reference.Length != 3 ||
                !Finite(reference[0]) || !Finite(reference[1]) || !Finite(reference[2])) return false;
            var dx = reference[0] - position[0];
            var dy = reference[1] - position[1];
            var dz = reference[2] - position[2];
            drift = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (!Finite(drift) || drift > maximumDrift)
            {
                if (resetWindowOnExcess) references.Clear();
                return false;
            }
            return true;
        }

        public static bool CanConfirmStop(bool neutralReadbacks, bool brakeReadbacks,
            bool chassisStationary, bool allSubgridsStationary, int distinctSamples,
            int requiredSamples = 3)
        {
            return neutralReadbacks && brakeReadbacks && chassisStationary && allSubgridsStationary &&
                requiredSamples > 0 && distinctSamples >= requiredSamples;
        }

        public static bool CanAcceptBaseline(bool exactIdentity, bool chassisPhysicsReady,
            bool allWheelPhysicsReady, bool cameraAvailable, bool cameraSameGrid,
            bool frontPathClear, bool controlsNeutral, bool groundContactVerified,
            bool gravityVerified, int sampleCount,
            double elapsedSeconds)
        {
            return exactIdentity && chassisPhysicsReady && allWheelPhysicsReady && cameraAvailable &&
                cameraSameGrid && frontPathClear && controlsNeutral && groundContactVerified &&
                gravityVerified && sampleCount >= 20 &&
                Finite(elapsedSeconds) && elapsedSeconds >= 2.0;
        }

        public static bool ShouldSample(int updateTick, int intervalTicks = 6, int maximumTicks = 1800)
        {
            return updateTick > 0 && intervalTicks > 0 && maximumTicks > 0 &&
                updateTick <= maximumTicks && updateTick % intervalTicks == 0;
        }

        private static bool Hex32(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 32) return false;
            foreach (var character in value)
                if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f'))) return false;
            return true;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public enum LocalVehicleTestCommandKind { Steering, Propulsion, Drive, Brake, Neutral }

    /// <summary>Run-scoped operator grant for the disposable local test source; never a player identity.</summary>
    public sealed class LocalVehicleTestAuthorization
    {
        public readonly string Instance, RunId, FixtureId, AuthorizationId;
        public readonly long GridId, ControllerId, CameraId;
        public readonly DateTime ExpiresUtc;

        public LocalVehicleTestAuthorization(string instance, string runId, string fixtureId,
            string authorizationId, long gridId, long controllerId, long cameraId, DateTime expiresUtc)
        {
            Instance = instance ?? string.Empty;
            RunId = runId ?? string.Empty;
            FixtureId = fixtureId ?? string.Empty;
            AuthorizationId = authorizationId ?? string.Empty;
            GridId = gridId;
            ControllerId = controllerId;
            CameraId = cameraId;
            ExpiresUtc = expiresUtc.Kind == DateTimeKind.Utc ? expiresUtc : expiresUtc.ToUniversalTime();
        }
    }

    public sealed class LocalVehicleTestCommand
    {
        public readonly string CommandId, Instance, RunId, FixtureId;
        public readonly long GridId, ControllerId;
        public readonly LocalVehicleTestCommandKind Kind;
        public readonly double Value, SteeringValue, MaximumSpeedMetersPerSecond;
        public readonly DateTime IssuedUtc, ExpiresUtc;

        public LocalVehicleTestCommand(string commandId, string instance, string runId, string fixtureId,
            long gridId, long controllerId, LocalVehicleTestCommandKind kind, double value,
            double maximumSpeedMetersPerSecond, DateTime issuedUtc, DateTime expiresUtc,
            double steeringValue = 0)
        {
            CommandId = commandId ?? string.Empty;
            Instance = instance ?? string.Empty;
            RunId = runId ?? string.Empty;
            FixtureId = fixtureId ?? string.Empty;
            GridId = gridId;
            ControllerId = controllerId;
            Kind = kind;
            Value = value;
            SteeringValue = steeringValue;
            MaximumSpeedMetersPerSecond = maximumSpeedMetersPerSecond;
            IssuedUtc = issuedUtc.Kind == DateTimeKind.Utc ? issuedUtc : issuedUtc.ToUniversalTime();
            ExpiresUtc = expiresUtc.Kind == DateTimeKind.Utc ? expiresUtc : expiresUtc.ToUniversalTime();
        }
    }

    /// <summary>Fresh live evidence presented by the game-side adapter before each command write.</summary>
    public sealed class LocalVehicleTestReadiness
    {
        public readonly string Instance, RunId, FixtureId;
        public readonly long GridId, ControllerId;
        public readonly IList<long> WheelIds, WheelGridIds;
        public readonly DateTime TelemetryUtc;
        public readonly double LinearSpeedMetersPerSecond, AngularSpeedRadiansPerSecond;
        public readonly bool InventoryComplete, IsolatedAndClear, StopPathReady, BrakePathReady;
        public readonly bool GroundContactVerified, GravityVerified;
        public readonly bool FullAssemblyClearanceVerified;
        public readonly bool StationarySteeringOnly;
        public readonly bool BrakeOnly;
        public readonly bool SteeringOrientationKnown, NoOtherMovementWriters;

        public LocalVehicleTestReadiness(string instance, string runId, string fixtureId,
            long gridId, long controllerId, IList<long> wheelIds, IList<long> wheelGridIds,
            DateTime telemetryUtc, bool inventoryComplete, bool isolatedAndClear,
            bool stopPathReady, bool brakePathReady, bool steeringOrientationKnown,
            bool noOtherMovementWriters, double linearSpeedMetersPerSecond = 0,
            double angularSpeedRadiansPerSecond = 0, bool stationarySteeringOnly = false,
            bool groundContactVerified = false, bool gravityVerified = false,
            bool fullAssemblyClearanceVerified = false, bool brakeOnly = false)
        {
            Instance = instance ?? string.Empty;
            RunId = runId ?? string.Empty;
            FixtureId = fixtureId ?? string.Empty;
            GridId = gridId;
            ControllerId = controllerId;
            WheelIds = wheelIds;
            WheelGridIds = wheelGridIds;
            TelemetryUtc = telemetryUtc.Kind == DateTimeKind.Utc ? telemetryUtc : telemetryUtc.ToUniversalTime();
            LinearSpeedMetersPerSecond = linearSpeedMetersPerSecond;
            AngularSpeedRadiansPerSecond = angularSpeedRadiansPerSecond;
            InventoryComplete = inventoryComplete;
            IsolatedAndClear = isolatedAndClear;
            StopPathReady = stopPathReady;
            BrakePathReady = brakePathReady;
            SteeringOrientationKnown = steeringOrientationKnown;
            NoOtherMovementWriters = noOtherMovementWriters;
            StationarySteeringOnly = stationarySteeringOnly;
            GroundContactVerified = groundContactVerified;
            GravityVerified = gravityVerified;
            FullAssemblyClearanceVerified = fullAssemblyClearanceVerified;
            BrakeOnly = brakeOnly;
        }
    }

    public static class LocalVehicleTestControlPolicy
    {
        public static readonly TimeSpan MaximumGrantLifetime = TimeSpan.FromMinutes(15);
        public static readonly TimeSpan MaximumCommandLifetime = TimeSpan.FromSeconds(20);
        public static readonly TimeSpan MaximumTelemetryAge = TimeSpan.FromSeconds(1);
        public const double MaximumSteeringOverride = 0.20;
        public const double MaximumPropulsionOverride = 0.03;
        public const double MaximumSpeedMetersPerSecond = 0.50;
        public const double MaximumSpeedOvershootToleranceMetersPerSecond = 0.02;

        public static bool IsAuthorized(LocalVehicleTestAuthorization authorization,
            string expectedInstance, string expectedRunId, string expectedFixtureId,
            long expectedGridId, long expectedControllerId, long expectedCameraId, DateTime nowUtc)
        {
            if (authorization == null || string.IsNullOrWhiteSpace(authorization.AuthorizationId) ||
                authorization.AuthorizationId.Length > 128 || authorization.AuthorizationId.IndexOfAny(new[] { '\r', '\n' }) >= 0 ||
                !string.Equals(authorization.Instance, expectedInstance, StringComparison.Ordinal) ||
                !Hex32(authorization.RunId) || authorization.RunId != expectedRunId ||
                !Hex32(authorization.FixtureId) || authorization.FixtureId != expectedFixtureId ||
                authorization.GridId <= 0 || authorization.GridId != expectedGridId ||
                authorization.ControllerId <= 0 || authorization.ControllerId != expectedControllerId ||
                authorization.CameraId <= 0 || authorization.CameraId != expectedCameraId ||
                expectedCameraId <= 0)
                return false;
            var now = nowUtc.ToUniversalTime();
            return authorization.ExpiresUtc > now && authorization.ExpiresUtc - now <= MaximumGrantLifetime;
        }

        public static bool CanDispatch(LocalVehicleTestAuthorization authorization,
            LocalVehicleTestReadiness readiness, LocalVehicleTestCommand command,
            string expectedInstance, string expectedRunId, string expectedFixtureId,
            long expectedGridId, long expectedControllerId, long expectedCameraId,
            DateTime nowUtc, out string reason)
        {
            return CanDispatch(authorization, readiness, command, expectedInstance, expectedRunId,
                expectedFixtureId, expectedGridId, expectedControllerId, expectedCameraId,
                null, null, nowUtc, out reason);
        }

        public static bool CanDispatch(LocalVehicleTestAuthorization authorization,
            LocalVehicleTestReadiness readiness, LocalVehicleTestCommand command,
            string expectedInstance, string expectedRunId, string expectedFixtureId,
            long expectedGridId, long expectedControllerId, long expectedCameraId, IList<long> expectedWheelIds,
            IList<long> expectedWheelGridIds, DateTime nowUtc, out string reason)
        {
            reason = "local test authorization or command identity mismatch";
            if (readiness == null || command == null ||
                !IsAuthorized(authorization, expectedInstance, expectedRunId, expectedFixtureId,
                    expectedGridId, expectedControllerId, expectedCameraId, nowUtc) ||
                command.Instance != expectedInstance || command.RunId != expectedRunId ||
                command.FixtureId != expectedFixtureId || command.GridId != expectedGridId ||
                command.ControllerId != expectedControllerId || string.IsNullOrWhiteSpace(command.CommandId) ||
                command.CommandId.Length > 128 || command.CommandId.IndexOfAny(new[] { '\r', '\n' }) >= 0 ||
                readiness.Instance != expectedInstance || readiness.RunId != expectedRunId ||
                readiness.FixtureId != expectedFixtureId || readiness.GridId != expectedGridId ||
                readiness.ControllerId != expectedControllerId)
                return false;

            var now = nowUtc.ToUniversalTime();
            if (command.ExpiresUtc <= now)
            { reason = "local test command expired before dispatch or during execution"; return false; }
            if (command.IssuedUtc > now || command.ExpiresUtc <= command.IssuedUtc ||
                command.ExpiresUtc - command.IssuedUtc > MaximumCommandLifetime)
            { reason = "command timestamp or expiry is invalid"; return false; }
            if (readiness.TelemetryUtc > now || now - readiness.TelemetryUtc > MaximumTelemetryAge)
            { reason = "linear/angular telemetry is stale or from the future"; return false; }
            if (readiness.BrakeOnly && command.Kind != LocalVehicleTestCommandKind.Brake)
            { reason = "only an explicit brake command is permitted in stationary brake-only mode"; return false; }
            if (!Finite(readiness.LinearSpeedMetersPerSecond) || !Finite(readiness.AngularSpeedRadiansPerSecond) ||
                readiness.LinearSpeedMetersPerSecond < 0 || readiness.AngularSpeedRadiansPerSecond < 0)
            { reason = "linear/angular telemetry is non-finite or invalid"; return false; }
            var steeringOnlySafeCommand = readiness.StationarySteeringOnly &&
                command.Kind != LocalVehicleTestCommandKind.Propulsion && command.Kind != LocalVehicleTestCommandKind.Drive &&
                readiness.LinearSpeedMetersPerSecond <= 0.03 &&
                readiness.AngularSpeedRadiansPerSecond <= 0.03;
            if (readiness.StationarySteeringOnly &&
                (command.Kind == LocalVehicleTestCommandKind.Propulsion || command.Kind == LocalVehicleTestCommandKind.Drive))
            { reason = "propulsion is hard-disabled in stationary-steering-only mode"; return false; }
            if (!readiness.InventoryComplete || (!readiness.IsolatedAndClear && !steeringOnlySafeCommand) ||
                !readiness.StopPathReady || !readiness.BrakePathReady ||
                !readiness.SteeringOrientationKnown || !readiness.NoOtherMovementWriters ||
                !readiness.GroundContactVerified || !readiness.GravityVerified)
            { reason = "fixture isolation, complete wheel inventory, ground support, gravity, steering, writer audit, telemetry, or stop readiness failed"; return false; }
            if ((command.Kind == LocalVehicleTestCommandKind.Propulsion ||
                 command.Kind == LocalVehicleTestCommandKind.Drive) &&
                !readiness.FullAssemblyClearanceVerified)
            { reason = "directional and full-assembly chassis clearance is not verified"; return false; }
            if (readiness.WheelIds == null || readiness.WheelGridIds == null || readiness.WheelIds.Count == 0 ||
                readiness.WheelIds.Count != readiness.WheelGridIds.Count)
            { reason = "wheel inventory is empty or incomplete"; return false; }
            if (expectedWheelIds != null || expectedWheelGridIds != null)
            {
                if (expectedWheelIds == null || expectedWheelGridIds == null ||
                    !SameWheelPairs(readiness.WheelIds, readiness.WheelGridIds,
                        expectedWheelIds, expectedWheelGridIds))
                { reason = "wheel identities or mechanical subgrids differ from the exact reviewed fixture"; return false; }
            }
            else
            {
                var seen = new HashSet<long>();
                for (var index = 0; index < readiness.WheelIds.Count; index++)
                    if (readiness.WheelIds[index] <= 0 || readiness.WheelGridIds[index] != expectedGridId ||
                        !seen.Add(readiness.WheelIds[index]))
                    { reason = "wheel identity is duplicated or outside the exact enrolled grid"; return false; }
            }

            if (!Finite(command.Value) || !Finite(command.SteeringValue) || !Finite(command.MaximumSpeedMetersPerSecond) ||
                command.MaximumSpeedMetersPerSecond < 0 || command.MaximumSpeedMetersPerSecond > MaximumSpeedMetersPerSecond)
            { reason = "command contains a non-finite value or exceeds the local test speed cap"; return false; }
            if (command.Kind != LocalVehicleTestCommandKind.Drive && command.SteeringValue != 0)
            { reason = "separate local command kinds cannot carry a hidden steering value"; return false; }
            switch (command.Kind)
            {
                case LocalVehicleTestCommandKind.Steering:
                    if (Math.Abs(command.Value) > MaximumSteeringOverride || command.MaximumSpeedMetersPerSecond != 0 ||
                        command.SteeringValue != 0 ||
                        readiness.LinearSpeedMetersPerSecond > 0.03 || readiness.AngularSpeedRadiansPerSecond > 0.03)
                    { reason = "steering command exceeds its stationary override bound"; return false; }
                    break;
                case LocalVehicleTestCommandKind.Propulsion:
                    if (Math.Abs(command.Value) > MaximumPropulsionOverride || command.MaximumSpeedMetersPerSecond <= 0 ||
                        readiness.LinearSpeedMetersPerSecond > command.MaximumSpeedMetersPerSecond + MaximumSpeedOvershootToleranceMetersPerSecond ||
                        readiness.AngularSpeedRadiansPerSecond > 0.1)
                    { reason = "propulsion command exceeds its bounded test envelope"; return false; }
                    break;
                case LocalVehicleTestCommandKind.Drive:
                    if (Math.Abs(command.Value) > MaximumPropulsionOverride ||
                        Math.Abs(command.SteeringValue) > MaximumSteeringOverride ||
                        command.MaximumSpeedMetersPerSecond <= 0 ||
                        readiness.LinearSpeedMetersPerSecond > command.MaximumSpeedMetersPerSecond + MaximumSpeedOvershootToleranceMetersPerSecond ||
                        readiness.AngularSpeedRadiansPerSecond > 0.1)
                    { reason = "combined drive command exceeds its bounded steering, propulsion, or speed envelope"; return false; }
                    break;
                case LocalVehicleTestCommandKind.Brake:
                    if (command.Value != 1 || command.MaximumSpeedMetersPerSecond != 0 || command.SteeringValue != 0)
                    { reason = "brake command must request active braking"; return false; }
                    break;
                case LocalVehicleTestCommandKind.Neutral:
                    if (command.Value != 0 || command.MaximumSpeedMetersPerSecond != 0 || command.SteeringValue != 0)
                    { reason = "neutral command must request zero actuator output"; return false; }
                    break;
                default:
                    reason = "unsupported local test command";
                    return false;
            }
            reason = "local test command is authorized for the exact disposable grid";
            return true;
        }

        private static bool SameWheelPairs(IList<long> observedIds, IList<long> observedGridIds,
            IList<long> expectedIds, IList<long> expectedGridIds)
        {
            if (observedIds == null || observedGridIds == null || expectedIds == null || expectedGridIds == null ||
                observedIds.Count != expectedIds.Count || observedGridIds.Count != expectedGridIds.Count ||
                expectedIds.Count != expectedGridIds.Count) return false;
            var seen = new HashSet<int>();
            for (var index = 0; index < observedIds.Count; index++)
            {
                var match = -1;
                for (var expected = 0; expected < expectedIds.Count; expected++)
                    if (observedIds[index] == expectedIds[expected] && observedGridIds[index] == expectedGridIds[expected])
                    { match = expected; break; }
                if (match < 0 || !seen.Add(match)) return false;
            }
            return seen.Count == expectedIds.Count;
        }

        /// <summary>
        /// A confirmed steering sign is required only when propulsion is paired
        /// with a nonzero steering request. Straight propulsion has no steering
        /// direction to confirm; steering-only calibration remains available.
        /// </summary>
        public static bool RequiresSteeringDirectionConfirmation(LocalVehicleTestCommand command)
        {
            if (command == null || command.Kind != LocalVehicleTestCommandKind.Drive) return false;
            return Math.Abs(command.SteeringValue) > 0.001;
        }

        private static bool Hex32(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 32) return false;
            foreach (var character in value)
                if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f'))) return false;
            return true;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    /// <summary>Fail-closed live checks required before carrying a verified steering sign across runs.</summary>
    public static class LocalVehicleTestDirectionProofPolicy
    {
        public static bool CanCarryForward(bool priorReceiptVerified, bool liveWheelMapMatches,
            bool steeringOrientationKnown, bool stopPathVerified, bool noOtherMovementWriters,
            bool actuatorsNeutral, double linearSpeedMetersPerSecond,
            double angularSpeedRadiansPerSecond)
        {
            return priorReceiptVerified && liveWheelMapMatches && steeringOrientationKnown &&
                stopPathVerified && noOtherMovementWriters && actuatorsNeutral &&
                Finite(linearSpeedMetersPerSecond) && Finite(angularSpeedRadiansPerSecond) &&
                linearSpeedMetersPerSecond >= 0 && angularSpeedRadiansPerSecond >= 0 &&
                linearSpeedMetersPerSecond <= 0.02 && angularSpeedRadiansPerSecond <= 0.02;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    /// <summary>Run-wide position baseline policy for the explicitly disposable local test lane.</summary>
    public static class LocalVehicleTestDisplacementPolicy
    {
        public static bool CanInitialize(bool baselineReady, bool neutral, bool stopPathReady,
            bool noOtherMovementWriters, double linearSpeedMetersPerSecond,
            double angularSpeedRadiansPerSecond)
        {
            return !baselineReady && neutral && stopPathReady && noOtherMovementWriters &&
                Finite(linearSpeedMetersPerSecond) && Finite(angularSpeedRadiansPerSecond) &&
                linearSpeedMetersPerSecond >= 0 && angularSpeedRadiansPerSecond >= 0 &&
                linearSpeedMetersPerSecond <= 0.02 && angularSpeedRadiansPerSecond <= 0.02;
        }

        public static bool TryMeasure(double startX, double startY, double startZ,
            double x, double y, double z, out double displacementMeters)
        {
            displacementMeters = double.NaN;
            if (!Finite(startX) || !Finite(startY) || !Finite(startZ) ||
                !Finite(x) || !Finite(y) || !Finite(z)) return false;
            var dx = x - startX;
            var dy = y - startY;
            var dz = z - startZ;
            displacementMeters = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            return Finite(displacementMeters);
        }

        public static bool Exceeds(double displacementMeters, double maximumDisplacementMeters)
        {
            return !Finite(displacementMeters) || !Finite(maximumDisplacementMeters) ||
                maximumDisplacementMeters <= 0 || displacementMeters >= maximumDisplacementMeters;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    /// <summary>Small invariant-culture mailbox wire shared by the local source and game adapter.</summary>
    public static class LocalVehicleTestWire
    {
        public static string FormatAuthorization(LocalVehicleTestAuthorization value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            return string.Join("|", new[] { "VWLA2", Atom(value.Instance), Atom(value.RunId), Atom(value.FixtureId),
                Atom(value.AuthorizationId), N(value.GridId), N(value.ControllerId), N(value.CameraId),
                N(value.ExpiresUtc.Ticks) }) + "\n";
        }

        public static bool TryParseAuthorization(string text, out LocalVehicleTestAuthorization value)
        {
            value = null;
            var fields = Fields(text, 9, "VWLA2");
            long grid, controller, camera, expires;
            if (fields == null || !SafeAtom(fields[1], 96) || !Hex(fields[2], 32) || !Hex(fields[3], 32) ||
                !SafeAtom(fields[4], 128) || !Long(fields[5], out grid) || grid <= 0 ||
                !Long(fields[6], out controller) || controller <= 0 ||
                !Long(fields[7], out camera) || camera <= 0 || !Long(fields[8], out expires)) return false;
            try
            {
                value = new LocalVehicleTestAuthorization(fields[1], fields[2], fields[3], fields[4],
                    grid, controller, camera, new DateTime(expires, DateTimeKind.Utc));
                return true;
            }
            catch { return false; }
        }

        public static string FormatCommand(LocalVehicleTestCommand value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            return string.Join("|", new[] { "VWLC2", Atom(value.CommandId), Atom(value.Instance), Atom(value.RunId),
                Atom(value.FixtureId), N(value.GridId), N(value.ControllerId), Kind(value.Kind),
                D(value.Value), D(value.MaximumSpeedMetersPerSecond), D(value.SteeringValue),
                N(value.IssuedUtc.Ticks), N(value.ExpiresUtc.Ticks) }) + "\n";
        }

        public static string FormatReceipt(LocalVehicleTestReceipt value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            return string.Join("|", new[] { "VWLR1", Atom(value.CommandId), Atom(value.State), ReasonAtom(value.Reason),
                N(value.CapturedUtc.Ticks), D(value.LinearSpeedMetersPerSecond), D(value.AngularSpeedRadiansPerSecond),
                N(value.ConsecutiveStationarySamples), D(value.ObservedSteeringWheelAngleRadians),
                value.SteeringDirectionConfirmed ? "1" : "0", value.ActuatorsNeutralized ? "1" : "0",
                value.BrakesRequested ? "1" : "0", value.PhysicalStopObserved ? "1" : "0" }) + "\n";
        }

        public static bool TryParseReceipt(string text, out LocalVehicleTestReceipt value)
        {
            value = null;
            var fields = Fields(text, 13, "VWLR1");
            long captured, samples;
            double linear, angular, steeringAngle;
            bool steeringConfirmed, neutralized, brakes, stopped;
            if (fields == null || !SafeAtom(fields[1], 128) || !SafeAtom(fields[2], 32) ||
                !SafeAtom(fields[3], 256) || !Long(fields[4], out captured) ||
                !Double(fields[5], out linear) || !Double(fields[6], out angular) ||
                !Long(fields[7], out samples) || samples < 0 || samples > int.MaxValue ||
                !Double(fields[8], out steeringAngle) || !Flag(fields[9], out steeringConfirmed) ||
                !Flag(fields[10], out neutralized) || !Flag(fields[11], out brakes) ||
                !Flag(fields[12], out stopped)) return false;
            try
            {
                value = new LocalVehicleTestReceipt(fields[1], fields[2], fields[3],
                    new DateTime(captured, DateTimeKind.Utc), linear, angular, (int)samples,
                    neutralized, brakes, stopped, steeringAngle, steeringConfirmed);
                return true;
            }
            catch { return false; }
        }

        public static bool TryParseCommand(string text, out LocalVehicleTestCommand value)
        {
            value = null;
            var v2 = Fields(text, 13, "VWLC2");
            var v1 = v2 == null ? Fields(text, 12, "VWLC1") : null;
            var fields = v2 ?? v1;
            long grid, controller, issued, expires;
            double commandValue, speed, steeringValue = 0;
            LocalVehicleTestCommandKind kind;
            if (fields == null || !SafeAtom(fields[1], 128) || !SafeAtom(fields[2], 96) ||
                !Hex(fields[3], 32) || !Hex(fields[4], 32) || !Long(fields[5], out grid) || grid <= 0 ||
                !Long(fields[6], out controller) || controller <= 0 || !TryKind(fields[7], out kind) ||
                !Double(fields[8], out commandValue) || !Double(fields[9], out speed) ||
                (v2 != null && !Double(fields[10], out steeringValue)) ||
                !Long(fields[v2 != null ? 11 : 10], out issued) ||
                !Long(fields[v2 != null ? 12 : 11], out expires)) return false;
            try
            {
                value = new LocalVehicleTestCommand(fields[1], fields[2], fields[3], fields[4], grid,
                    controller, kind, commandValue, speed, new DateTime(issued, DateTimeKind.Utc),
                    new DateTime(expires, DateTimeKind.Utc), steeringValue);
                return true;
            }
            catch { return false; }
        }

        private static string[] Fields(string text, int count, string schema)
        {
            if (string.IsNullOrEmpty(text) || text.Length > 4096 || text[text.Length - 1] != '\n' ||
                text.IndexOf('\r') >= 0) return null;
            var lines = text.Split('\n');
            if (lines.Length != 2 || lines[1].Length != 0) return null;
            var fields = lines[0].Split('|');
            return fields.Length == count && fields[0] == schema ? fields : null;
        }

        private static string Atom(string value)
        {
            if (!SafeAtom(value, 128)) throw new ArgumentException("Mailbox identity is invalid.");
            return value;
        }
        private static bool SafeAtom(string value, int maximum)
        {
            if (string.IsNullOrEmpty(value) || value.Length > maximum) return false;
            foreach (var c in value)
                if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == ':' || c == '.')) return false;
            return true;
        }
        private static bool Hex(string value, int length)
        {
            if (value == null || value.Length != length) return false;
            foreach (var c in value) if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            return true;
        }
        private static bool Flag(string value, out bool result)
        {
            result = value == "1";
            return result || value == "0";
        }
        private static string ReasonAtom(string value)
        {
            if (string.IsNullOrEmpty(value)) return "unspecified";
            var result = new StringBuilder(Math.Min(value.Length, 256));
            foreach (var c in value)
            {
                if (result.Length >= 256) break;
                result.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == ':' || c == '.' ? c : '_');
            }
            return result.Length == 0 ? "unspecified" : result.ToString();
        }
        private static string N(long value) => value.ToString(CultureInfo.InvariantCulture);
        private static string D(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        private static bool Long(string value, out long result)
            => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);
        private static bool Double(string value, out double result)
            => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) &&
               !double.IsNaN(result) && !double.IsInfinity(result);
        private static string Kind(LocalVehicleTestCommandKind kind)
        {
            switch (kind)
            {
                case LocalVehicleTestCommandKind.Steering: return "steering";
                case LocalVehicleTestCommandKind.Propulsion: return "propulsion";
                case LocalVehicleTestCommandKind.Drive: return "drive";
                case LocalVehicleTestCommandKind.Brake: return "brake";
                case LocalVehicleTestCommandKind.Neutral: return "neutral";
                default: return "invalid";
            }
        }
        private static bool TryKind(string text, out LocalVehicleTestCommandKind kind)
        {
            switch (text)
            {
                case "steering": kind = LocalVehicleTestCommandKind.Steering; return true;
                case "propulsion": kind = LocalVehicleTestCommandKind.Propulsion; return true;
                case "drive": kind = LocalVehicleTestCommandKind.Drive; return true;
                case "brake": kind = LocalVehicleTestCommandKind.Brake; return true;
                case "neutral": kind = LocalVehicleTestCommandKind.Neutral; return true;
                default: kind = LocalVehicleTestCommandKind.Neutral; return false;
            }
        }
    }

    public sealed class LocalVehicleTestReceipt
    {
        public readonly string CommandId, State, Reason;
        public readonly DateTime CapturedUtc;
        public readonly double LinearSpeedMetersPerSecond, AngularSpeedRadiansPerSecond;
        public readonly double ObservedSteeringWheelAngleRadians;
        public readonly int ConsecutiveStationarySamples;
        public readonly bool SteeringDirectionConfirmed;
        public readonly bool ActuatorsNeutralized, BrakesRequested, PhysicalStopObserved;

        public LocalVehicleTestReceipt(string commandId, string state, string reason, DateTime capturedUtc,
            double linearSpeedMetersPerSecond, double angularSpeedRadiansPerSecond,
            int consecutiveStationarySamples, bool actuatorsNeutralized, bool brakesRequested,
            bool physicalStopObserved, double observedSteeringWheelAngleRadians = 0,
            bool steeringDirectionConfirmed = false)
        {
            CommandId = commandId ?? string.Empty;
            State = state ?? string.Empty;
            Reason = reason ?? string.Empty;
            CapturedUtc = capturedUtc.Kind == DateTimeKind.Utc ? capturedUtc : capturedUtc.ToUniversalTime();
            LinearSpeedMetersPerSecond = linearSpeedMetersPerSecond;
            AngularSpeedRadiansPerSecond = angularSpeedRadiansPerSecond;
            ObservedSteeringWheelAngleRadians = observedSteeringWheelAngleRadians;
            ConsecutiveStationarySamples = consecutiveStationarySamples;
            SteeringDirectionConfirmed = steeringDirectionConfirmed;
            ActuatorsNeutralized = actuatorsNeutralized;
            BrakesRequested = brakesRequested;
            PhysicalStopObserved = physicalStopObserved;
        }
    }
}
