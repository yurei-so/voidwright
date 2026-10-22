using System;
using System.Collections.Generic;

namespace Voidwright.Core
{
    /// <summary>Testable identity, inventory, and expiry checks for a cached exact-grid actuator set.</summary>
    public static class DirectManeuverActuatorSessionBindingPolicy
    {
        public static bool IsValid(DateTime nowUtc, DateTime expiresUtc,
            long expectedGridId, long expectedControllerId, string expectedOperationId,
            long cachedGridId, long cachedControllerId, string cachedOperationId,
            int cachedWheelCount, int inventoryWheelCount, int reportedWheelCount)
        {
            if (expectedGridId <= 0 || expectedControllerId <= 0 ||
                string.IsNullOrWhiteSpace(expectedOperationId) ||
                cachedGridId != expectedGridId || cachedControllerId != expectedControllerId ||
                !string.Equals(cachedOperationId, expectedOperationId, StringComparison.Ordinal) ||
                cachedWheelCount <= 0 || inventoryWheelCount != cachedWheelCount ||
                reportedWheelCount != cachedWheelCount) return false;
            return nowUtc.ToUniversalTime() < expiresUtc.ToUniversalTime();
        }
    }

    /// <summary>
    /// Runtime capability gate for the contained cooperative-writer experiment.
    /// It does not claim engine-enforced compare-and-set or universal exclusivity.
    /// </summary>
    public static class DirectManeuverBrokerDispatchGate
    {
        public static bool CanDispatch(bool independentlyVerifiedLeaseAuthority,
            bool freshHostAttestation, bool freshModAttestation, bool operatorFree,
            bool participatingWritersAudited, bool completeChannelReadback,
            bool operationScopedStop, bool controllerPriorityStable = false)
        {
            return independentlyVerifiedLeaseAuthority && freshHostAttestation &&
                freshModAttestation && operatorFree && participatingWritersAudited &&
                completeChannelReadback && operationScopedStop && controllerPriorityStable;
        }
    }

    public enum DirectManeuverActuatorChannel { SteeringOverride, PropulsionOverride, GridBrake, HandBrake }
    public enum DirectManeuverLeaseCheck { Valid, Expired, IdentityMismatch, ExternalChange, InvalidSnapshot }
    public enum DirectManeuverLeaseWriteResult { Accepted, LeaseExpired, IdentityMismatch, ExternalChange, WriteNotApplied, InvalidValue, UnknownWheel }
    public enum DirectManeuverStopKind { Cancellation, EmergencyStop }

    /// <summary>In-process exclusive grid authority; production needs one shared host/mod broker.</summary>
    public sealed class DirectManeuverOperationLeaseAuthority
    {
        private sealed class Claim
        {
            public readonly DirectManeuverOperationIdentity Identity;
            public readonly Guid Token;
            public DateTime ExpiresAtUtc;
            public Claim(DirectManeuverOperationIdentity identity, Guid token, DateTime expiresAtUtc)
            { Identity = identity; Token = token; ExpiresAtUtc = expiresAtUtc; }
        }

        private readonly object sync = new object();
        private readonly Dictionary<long, Claim> claims = new Dictionary<long, Claim>();

        internal bool TryClaim(DirectManeuverOperationIdentity identity, DateTime nowUtc,
            DateTime expiresAtUtc, out Guid token)
        {
            token = Guid.Empty;
            if (identity == null || identity.GridId <= 0) return false;
            lock (sync)
            {
                if (claims.TryGetValue(identity.GridId, out var existing) && existing.ExpiresAtUtc > nowUtc)
                    return false;
                token = Guid.NewGuid();
                claims[identity.GridId] = new Claim(identity, token, expiresAtUtc);
                return true;
            }
        }

        internal bool Renew(DirectManeuverOperationIdentity identity, Guid token,
            DateTime nowUtc, DateTime expiresAtUtc)
        {
            lock (sync)
            {
                if (!claims.TryGetValue(identity.GridId, out var claim) || claim.Token != token ||
                    !claim.Identity.Matches(identity) || nowUtc >= claim.ExpiresAtUtc) return false;
                claim.ExpiresAtUtc = expiresAtUtc;
                return true;
            }
        }

        internal void Release(DirectManeuverOperationIdentity identity, Guid token)
        {
            lock (sync)
            {
                if (claims.TryGetValue(identity.GridId, out var claim) && claim.Token == token &&
                    claim.Identity.Matches(identity)) claims.Remove(identity.GridId);
            }
        }
    }

    public sealed class DirectManeuverOperationIdentity
    {
        public readonly string RunId, FixtureId, OperationId;
        public readonly long AgentCoreId, GridId, ControllerId;

        public DirectManeuverOperationIdentity(string runId, string fixtureId, string operationId,
            long gridId, long controllerId, long agentCoreId = 0)
        {
            RunId = runId ?? string.Empty;
            FixtureId = fixtureId ?? string.Empty;
            OperationId = operationId ?? string.Empty;
            AgentCoreId = agentCoreId;
            GridId = gridId;
            ControllerId = controllerId;
        }

        public bool Matches(DirectManeuverOperationIdentity other) => other != null &&
            RunId == other.RunId && FixtureId == other.FixtureId && OperationId == other.OperationId &&
            AgentCoreId == other.AgentCoreId && GridId == other.GridId && ControllerId == other.ControllerId;

        public bool Matches(DirectManeuverPlan plan) => plan != null && RunId == plan.RunId &&
            FixtureId == plan.FixtureId && OperationId == plan.OperationId && GridId == plan.GridId &&
            AgentCoreId == plan.AgentCoreId && ControllerId == plan.ControllerId;
    }

    /// <summary>Current read-only state for one exact wheel block.</summary>
    public sealed class DirectManeuverWheelState
    {
        public readonly long WheelEntityId;
        public readonly double SteeringOverride, PropulsionOverride;
        public readonly bool BrakeStateKnown, BrakeActive;
        public readonly bool DriveStateKnown, RevolveInvert, InvertPropulsion, InvertSteer;
        public readonly bool BrakingEnabled, WheelBrakeActive, WheelHandBrakeActive, ShouldBrake;
        public readonly double Power;
        public readonly DateTime CapturedUtc;

        public DirectManeuverWheelState(long wheelEntityId, double steeringOverride,
            double propulsionOverride, bool brakeStateKnown, bool brakeActive, DateTime capturedUtc,
            bool driveStateKnown = false, bool revolveInvert = false, bool invertPropulsion = false,
            bool invertSteer = false, double power = 0, bool brakingEnabled = false,
            bool wheelBrakeActive = false, bool wheelHandBrakeActive = false, bool shouldBrake = false)
        {
            WheelEntityId = wheelEntityId;
            SteeringOverride = steeringOverride;
            PropulsionOverride = propulsionOverride;
            BrakeStateKnown = brakeStateKnown;
            BrakeActive = brakeActive;
            DriveStateKnown = driveStateKnown;
            RevolveInvert = revolveInvert;
            InvertPropulsion = invertPropulsion;
            InvertSteer = invertSteer;
            Power = power;
            BrakingEnabled = brakingEnabled;
            WheelBrakeActive = wheelBrakeActive;
            WheelHandBrakeActive = wheelHandBrakeActive;
            ShouldBrake = shouldBrake;
            CapturedUtc = capturedUtc.ToUniversalTime();
        }
    }

    public sealed class DirectManeuverBrakeState
    {
        public readonly bool GridBrakeKnown, GridBrakeActive, HandBrakeKnown, HandBrakeActive;
        public DirectManeuverBrakeState(bool gridBrakeKnown, bool gridBrakeActive,
            bool handBrakeKnown, bool handBrakeActive, DateTime capturedUtc)
        { GridBrakeKnown = gridBrakeKnown; GridBrakeActive = gridBrakeActive;
          HandBrakeKnown = handBrakeKnown; HandBrakeActive = handBrakeActive;
          CapturedUtc = capturedUtc.ToUniversalTime(); }
        public readonly DateTime CapturedUtc;
    }

    public sealed class DirectManeuverChannelRestore
    {
        public readonly long WheelEntityId;
        public readonly DirectManeuverActuatorChannel Channel;
        public readonly double ExpectedOwnedValue, OriginalValue;
        internal DirectManeuverChannelRestore(long wheelEntityId,
            DirectManeuverActuatorChannel channel, double expectedOwnedValue, double originalValue)
        { WheelEntityId = wheelEntityId; Channel = channel; ExpectedOwnedValue = expectedOwnedValue; OriginalValue = originalValue; }
    }

    /// <summary>
    /// Offline operation lease contract. It models exact preflight binding,
    /// baseline snapshot, expiry, heartbeat conflict detection, per-channel
    /// accepted writes, and equality-guarded cleanup. It is not a game adapter.
    /// </summary>
    public sealed class DirectManeuverOperationLease
    {
        public const double NeutralTolerance = 0.0001;
        public static readonly TimeSpan MinimumDuration = TimeSpan.FromMilliseconds(100);
        public static readonly TimeSpan MaximumDuration = TimeSpan.FromSeconds(2);

        private sealed class ChannelLease
        {
            public readonly long WheelId;
            public readonly DirectManeuverActuatorChannel Channel;
            public readonly double Original;
            public double LastAccepted;
            public bool HasAcceptedWrite;
            public bool Relinquished;
            public ChannelLease(long wheelId, DirectManeuverActuatorChannel channel, double original)
            { WheelId = wheelId; Channel = channel; Original = original; LastAccepted = original; }
        }

        private readonly Dictionary<(long Wheel, DirectManeuverActuatorChannel Channel), ChannelLease> channels =
            new Dictionary<(long Wheel, DirectManeuverActuatorChannel Channel), ChannelLease>();
        private readonly Dictionary<long, bool> brakeBaseline = new Dictionary<long, bool>();
        private readonly DirectManeuverBrakeState controllerBrakeBaseline;
        private readonly TimeSpan duration;
        private readonly DirectManeuverOperationLeaseAuthority authority;
        private readonly Guid leaseToken;
        private bool active = true;
        private bool released;
        private bool safetyLost;

        private DirectManeuverOperationLease(DirectManeuverOperationIdentity identity,
            TimeSpan duration, DateTime nowUtc, IList<DirectManeuverWheelState> snapshot,
            DirectManeuverBrakeState controllerBrakeState,
            DirectManeuverOperationLeaseAuthority authority, Guid leaseToken)
        {
            Identity = identity;
            this.duration = duration;
            this.authority = authority;
            this.leaseToken = leaseToken;
            HeartbeatAtUtc = nowUtc;
            ExpiresAtUtc = nowUtc + duration;
            controllerBrakeBaseline = controllerBrakeState;
            foreach (var wheel in snapshot)
            {
                brakeBaseline.Add(wheel.WheelEntityId, wheel.BrakeActive);
                channels.Add((wheel.WheelEntityId, DirectManeuverActuatorChannel.SteeringOverride),
                    new ChannelLease(wheel.WheelEntityId, DirectManeuverActuatorChannel.SteeringOverride, wheel.SteeringOverride));
                channels.Add((wheel.WheelEntityId, DirectManeuverActuatorChannel.PropulsionOverride),
                    new ChannelLease(wheel.WheelEntityId, DirectManeuverActuatorChannel.PropulsionOverride, wheel.PropulsionOverride));
            }
            channels.Add((0, DirectManeuverActuatorChannel.GridBrake),
                new ChannelLease(0, DirectManeuverActuatorChannel.GridBrake, controllerBrakeState.GridBrakeActive ? 1 : 0));
            channels.Add((0, DirectManeuverActuatorChannel.HandBrake),
                new ChannelLease(0, DirectManeuverActuatorChannel.HandBrake, controllerBrakeState.HandBrakeActive ? 1 : 0));
        }

        public DirectManeuverOperationIdentity Identity { get; }
        public DateTime HeartbeatAtUtc { get; private set; }
        public DateTime ExpiresAtUtc { get; private set; }
        public bool IsActive => active && !released && !safetyLost;
        public bool SafetyLost => safetyLost;

        public static bool TryAcquire(DirectManeuverPlan plan, DirectManeuverEvidence evidence,
            IList<DirectManeuverWheelState> snapshot, DirectManeuverBrakeState controllerBrakeState,
            DirectManeuverOperationLeaseAuthority authority, DateTime nowUtc, TimeSpan duration,
            out DirectManeuverOperationLease lease, out string reason)
        {
            lease = null;
            nowUtc = nowUtc.ToUniversalTime();
            if (plan == null || evidence == null || snapshot == null || snapshot.Count == 0 || controllerBrakeState == null || authority == null ||
                duration < MinimumDuration || duration > MaximumDuration)
            { reason = "lease acquisition needs a plan, fresh evidence, wheel snapshot, and bounded duration"; return false; }
            if (!plan.CanDispatch(evidence, nowUtc, out reason)) return false;
            if (evidence.RunId != plan.RunId || evidence.FixtureId != plan.FixtureId ||
                evidence.OperationId != plan.OperationId || evidence.GridId != plan.GridId ||
                evidence.ControllerId != plan.ControllerId || evidence.AgentCoreId <= 0 ||
                evidence.AgentCoreId != plan.AgentCoreId)
            { reason = "lease evidence does not match the exact operation identity"; return false; }
            if (!controllerBrakeState.GridBrakeKnown || controllerBrakeState.GridBrakeActive ||
                !controllerBrakeState.HandBrakeKnown || controllerBrakeState.HandBrakeActive ||
                !Fresh(controllerBrakeState.CapturedUtc, nowUtc))
            { reason = "controller/grid brake state is unknown or active"; return false; }
            var seen = new HashSet<long>();
            foreach (var wheel in snapshot)
            {
                if (wheel == null || wheel.WheelEntityId <= 0 || !seen.Add(wheel.WheelEntityId) ||
                    !Finite(wheel.SteeringOverride) || !Finite(wheel.PropulsionOverride) ||
                    Math.Abs(wheel.SteeringOverride) > 1 || Math.Abs(wheel.PropulsionOverride) > 1 ||
                    !wheel.BrakeStateKnown || wheel.BrakeActive || !Fresh(wheel.CapturedUtc, nowUtc))
                { reason = "wheel snapshot is duplicate, invalid, or has unknown/active brake state"; return false; }
                if (Math.Abs(wheel.SteeringOverride) > NeutralTolerance ||
                    Math.Abs(wheel.PropulsionOverride) > NeutralTolerance)
                { reason = "operation cannot adopt non-neutral steering or propulsion values"; return false; }
            }
            var identity = new DirectManeuverOperationIdentity(plan.RunId, plan.FixtureId,
                plan.OperationId, plan.GridId, plan.ControllerId, plan.AgentCoreId);
            if (!authority.TryClaim(identity, nowUtc, nowUtc + duration, out var token))
            { reason = "another unexpired operation already owns this grid lease"; return false; }
            lease = new DirectManeuverOperationLease(identity, duration, nowUtc, snapshot,
                controllerBrakeState, authority, token);
            reason = "operation lease acquired from fresh exact preflight and neutral observed wheel/brake snapshot";
            return true;
        }

        public DirectManeuverLeaseCheck Heartbeat(DirectManeuverOperationIdentity identity,
            IList<DirectManeuverWheelState> current, DirectManeuverBrakeState controllerBrakeState,
            DateTime nowUtc)
        {
            nowUtc = nowUtc.ToUniversalTime();
            if (!Identity.Matches(identity)) return DirectManeuverLeaseCheck.IdentityMismatch;
            if (nowUtc < HeartbeatAtUtc) { LoseLease(); return DirectManeuverLeaseCheck.InvalidSnapshot; }
            if (!IsActive || nowUtc >= ExpiresAtUtc)
            {
                ExpireLease();
                return DirectManeuverLeaseCheck.Expired;
            }
            if (current == null || current.Count != brakeBaseline.Count)
            { LoseLease(); return DirectManeuverLeaseCheck.InvalidSnapshot; }
            if (controllerBrakeState == null || !controllerBrakeState.GridBrakeKnown ||
                !controllerBrakeState.HandBrakeKnown || !Fresh(controllerBrakeState.CapturedUtc, nowUtc) ||
                !MatchesChannel(0, DirectManeuverActuatorChannel.GridBrake, controllerBrakeState.GridBrakeActive ? 1 : 0) ||
                !MatchesChannel(0, DirectManeuverActuatorChannel.HandBrake, controllerBrakeState.HandBrakeActive ? 1 : 0))
            { LoseLease(); return DirectManeuverLeaseCheck.ExternalChange; }
            var seen = new HashSet<long>();
            foreach (var wheel in current)
            {
                if (wheel == null || !brakeBaseline.ContainsKey(wheel.WheelEntityId) || !seen.Add(wheel.WheelEntityId) ||
                    !wheel.BrakeStateKnown || !Fresh(wheel.CapturedUtc, nowUtc))
                { LoseLease(); return DirectManeuverLeaseCheck.InvalidSnapshot; }
                var brokerBrakeApplied = IsChannelActive(DirectManeuverActuatorChannel.GridBrake) ||
                    IsChannelActive(DirectManeuverActuatorChannel.HandBrake);
                if (wheel.BrakeActive != (brakeBaseline[wheel.WheelEntityId] || brokerBrakeApplied))
                { LoseLease(); return DirectManeuverLeaseCheck.ExternalChange; }
                if (!MatchesChannel(wheel.WheelEntityId, DirectManeuverActuatorChannel.SteeringOverride, wheel.SteeringOverride))
                { Relinquish(wheel.WheelEntityId, DirectManeuverActuatorChannel.SteeringOverride); LoseLease(); return DirectManeuverLeaseCheck.ExternalChange; }
                if (!MatchesChannel(wheel.WheelEntityId, DirectManeuverActuatorChannel.PropulsionOverride, wheel.PropulsionOverride))
                { Relinquish(wheel.WheelEntityId, DirectManeuverActuatorChannel.PropulsionOverride); LoseLease(); return DirectManeuverLeaseCheck.ExternalChange; }
            }
            HeartbeatAtUtc = nowUtc;
            ExpiresAtUtc = nowUtc + duration;
            if (!authority.Renew(Identity, leaseToken, nowUtc, ExpiresAtUtc))
            { LoseLease(); return DirectManeuverLeaseCheck.Expired; }
            return DirectManeuverLeaseCheck.Valid;
        }

        public DirectManeuverLeaseWriteResult RecordAcceptedChannelWrite(
            DirectManeuverOperationIdentity identity, long wheelEntityId,
            DirectManeuverActuatorChannel channel, double observedBefore,
            double requestedValue, double observedAfter, DateTime nowUtc)
        {
            nowUtc = nowUtc.ToUniversalTime();
            if (!Identity.Matches(identity)) return DirectManeuverLeaseWriteResult.IdentityMismatch;
            if (nowUtc < HeartbeatAtUtc) { LoseLease(); return DirectManeuverLeaseWriteResult.LeaseExpired; }
            if (!IsActive || nowUtc >= ExpiresAtUtc || nowUtc - HeartbeatAtUtc > DirectManeuverPlan.EvidenceMaximumAge)
            { ExpireLease(); return DirectManeuverLeaseWriteResult.LeaseExpired; }
            ChannelLease lease;
            if (!channels.TryGetValue((wheelEntityId, channel), out lease)) return DirectManeuverLeaseWriteResult.UnknownWheel;
            if (!Finite(observedBefore) || !Finite(requestedValue) || !Finite(observedAfter) ||
                Math.Abs(requestedValue) > 1 || Math.Abs(observedAfter) > 1 ||
                ((channel == DirectManeuverActuatorChannel.GridBrake || channel == DirectManeuverActuatorChannel.HandBrake) &&
                 ((requestedValue != 0 && requestedValue != 1) || (observedAfter != 0 && observedAfter != 1))))
                return DirectManeuverLeaseWriteResult.InvalidValue;
            var expected = lease.HasAcceptedWrite ? lease.LastAccepted : lease.Original;
            if (lease.Relinquished || !NearlyEqual(observedBefore, expected))
            { lease.Relinquished = true; LoseLease(); return DirectManeuverLeaseWriteResult.ExternalChange; }
            if (!NearlyEqual(observedAfter, requestedValue))
            {
                if (NearlyEqual(observedAfter, expected)) return DirectManeuverLeaseWriteResult.WriteNotApplied;
                lease.Relinquished = true;
                LoseLease();
                return DirectManeuverLeaseWriteResult.ExternalChange;
            }
            lease.LastAccepted = observedAfter;
            lease.HasAcceptedWrite = true;
            HeartbeatAtUtc = nowUtc;
            ExpiresAtUtc = nowUtc + duration;
            if (!authority.Renew(Identity, leaseToken, nowUtc, ExpiresAtUtc))
            { LoseLease(); return DirectManeuverLeaseWriteResult.LeaseExpired; }
            return DirectManeuverLeaseWriteResult.Accepted;
        }

        internal bool CanWriteStopChannel(DirectManeuverOperationIdentity identity,
            long wheelEntityId, DirectManeuverActuatorChannel channel, double observedBefore)
        {
            if (released || safetyLost || !Identity.Matches(identity) || !Finite(observedBefore)) return false;
            ChannelLease lease;
            if (!channels.TryGetValue((wheelEntityId, channel), out lease) || lease.Relinquished) return false;
            var expected = lease.HasAcceptedWrite ? lease.LastAccepted : lease.Original;
            return NearlyEqual(observedBefore, expected);
        }

        internal DirectManeuverLeaseWriteResult RecordStopChannelWrite(
            DirectManeuverOperationIdentity identity, long wheelEntityId,
            DirectManeuverActuatorChannel channel, double observedBefore,
            double requestedValue, double observedAfter)
        {
            if (released || safetyLost || !Identity.Matches(identity))
                return DirectManeuverLeaseWriteResult.IdentityMismatch;
            var neutralWheel = (channel == DirectManeuverActuatorChannel.SteeringOverride ||
                channel == DirectManeuverActuatorChannel.PropulsionOverride) &&
                Math.Abs(requestedValue) <= NeutralTolerance;
            var activeBrake = (channel == DirectManeuverActuatorChannel.GridBrake ||
                channel == DirectManeuverActuatorChannel.HandBrake) && requestedValue == 1;
            if (!neutralWheel && !activeBrake) return DirectManeuverLeaseWriteResult.InvalidValue;
            if (!Finite(observedBefore) || !Finite(observedAfter) || !NearlyEqual(observedAfter, requestedValue))
                return DirectManeuverLeaseWriteResult.WriteNotApplied;
            ChannelLease lease;
            if (!channels.TryGetValue((wheelEntityId, channel), out lease))
                return DirectManeuverLeaseWriteResult.UnknownWheel;
            if (lease.Relinquished || !NearlyEqual(observedBefore,
                lease.HasAcceptedWrite ? lease.LastAccepted : lease.Original))
            { lease.Relinquished = true; LoseLease(); return DirectManeuverLeaseWriteResult.ExternalChange; }
            lease.LastAccepted = observedAfter;
            lease.HasAcceptedWrite = true;
            return DirectManeuverLeaseWriteResult.Accepted;
        }

        public bool TryCreateCleanup(DirectManeuverOperationIdentity identity,
            IList<DirectManeuverWheelState> current, DateTime nowUtc,
            out IList<DirectManeuverChannelRestore> restores)
        {
            restores = new List<DirectManeuverChannelRestore>();
            if (released || !Identity.Matches(identity) || current == null) return false;
            nowUtc = nowUtc.ToUniversalTime();
            var observed = new Dictionary<long, DirectManeuverWheelState>();
            foreach (var wheel in current)
            {
                if (wheel == null || observed.ContainsKey(wheel.WheelEntityId) || !Fresh(wheel.CapturedUtc, nowUtc)) return false;
                observed.Add(wheel.WheelEntityId, wheel);
            }
            foreach (var lease in channels.Values)
            {
                if (!lease.HasAcceptedWrite || lease.Relinquished) continue;
                DirectManeuverWheelState wheel;
                if (!observed.TryGetValue(lease.WheelId, out wheel) || !wheel.BrakeStateKnown ||
                    !brakeBaseline.TryGetValue(lease.WheelId, out var brake) || wheel.BrakeActive != brake) continue;
                var value = lease.Channel == DirectManeuverActuatorChannel.SteeringOverride
                    ? wheel.SteeringOverride : wheel.PropulsionOverride;
                if (NearlyEqual(value, lease.LastAccepted))
                    restores.Add(new DirectManeuverChannelRestore(lease.WheelId, lease.Channel,
                        lease.LastAccepted, lease.Original));
            }
            released = true;
            active = false;
            authority.Release(Identity, leaseToken);
            return true;
        }

        public bool IsDispatchAuthorized(DirectManeuverOperationIdentity identity, DateTime nowUtc)
        {
            nowUtc = nowUtc.ToUniversalTime();
            if (!Identity.Matches(identity)) return false;
            if (nowUtc < HeartbeatAtUtc) { LoseLease(); return false; }
            if (!IsActive || nowUtc >= ExpiresAtUtc || nowUtc - HeartbeatAtUtc > DirectManeuverPlan.EvidenceMaximumAge)
            { ExpireLease(); return false; }
            return true;
        }

        public bool HasAcceptedValuesForCommand(DirectManeuverOperationIdentity identity,
            double steeringValue, double propulsionValue, DateTime nowUtc, bool controlledStop = false,
            IDictionary<long, double> propulsionByWheel = null)
        {
            if (!IsDispatchAuthorized(identity, nowUtc) || !Finite(steeringValue) || !Finite(propulsionValue) ||
                Math.Abs(steeringValue) > 1 || Math.Abs(propulsionValue) > 1) return false;
            var propulsionChannelCount = 0;
            foreach (var channel in channels.Values)
            {
                double expected;
                if (channel.Channel == DirectManeuverActuatorChannel.SteeringOverride)
                    expected = steeringValue;
                else if (channel.Channel == DirectManeuverActuatorChannel.PropulsionOverride)
                {
                    propulsionChannelCount++;
                    if (propulsionByWheel != null)
                    {
                        if (!propulsionByWheel.TryGetValue(channel.WheelId, out expected) ||
                            !Finite(expected) || Math.Abs(expected) > 1) return false;
                    }
                    else expected = propulsionValue;
                }
                else expected = controlledStop ? 1 : channel.Original;
                var mustHaveWrite = channel.Channel == DirectManeuverActuatorChannel.SteeringOverride ||
                    channel.Channel == DirectManeuverActuatorChannel.PropulsionOverride || controlledStop;
                if ((mustHaveWrite && !channel.HasAcceptedWrite) || channel.Relinquished ||
                    !NearlyEqual(channel.HasAcceptedWrite ? channel.LastAccepted : channel.Original, expected))
                    return false;
            }
            return propulsionByWheel == null || propulsionByWheel.Count == propulsionChannelCount;
        }

        private bool MatchesChannel(long wheelId, DirectManeuverActuatorChannel channel, double actual)
        {
            ChannelLease lease;
            if (!channels.TryGetValue((wheelId, channel), out lease) || !Finite(actual) || lease.Relinquished) return false;
            return NearlyEqual(actual, lease.HasAcceptedWrite ? lease.LastAccepted : lease.Original);
        }

        public bool ReleaseAfterConfirmedStop(DirectManeuverOperationIdentity identity,
            IList<DirectManeuverWheelState> current, DirectManeuverBrakeState brakes, DateTime nowUtc)
        {
            nowUtc = nowUtc.ToUniversalTime();
            if (released || safetyLost || !Identity.Matches(identity) || nowUtc < HeartbeatAtUtc ||
                current == null || current.Count != brakeBaseline.Count || brakes == null ||
                !brakes.GridBrakeKnown || !brakes.HandBrakeKnown || !Fresh(brakes.CapturedUtc, nowUtc) ||
                !MatchesChannel(0, DirectManeuverActuatorChannel.GridBrake, brakes.GridBrakeActive ? 1 : 0) ||
                !MatchesChannel(0, DirectManeuverActuatorChannel.HandBrake, brakes.HandBrakeActive ? 1 : 0)) return false;
            var seen = new HashSet<long>();
            foreach (var wheel in current)
            {
                if (wheel == null || !seen.Add(wheel.WheelEntityId) || !wheel.BrakeStateKnown || !Fresh(wheel.CapturedUtc, nowUtc) ||
                    !MatchesChannel(wheel.WheelEntityId, DirectManeuverActuatorChannel.SteeringOverride, wheel.SteeringOverride) ||
                    !MatchesChannel(wheel.WheelEntityId, DirectManeuverActuatorChannel.PropulsionOverride, wheel.PropulsionOverride) ||
                    Math.Abs(wheel.SteeringOverride) > NeutralTolerance || Math.Abs(wheel.PropulsionOverride) > NeutralTolerance) return false;
            }
            if (seen.Count != brakeBaseline.Count) return false;
            released = true;
            active = false;
            authority.Release(Identity, leaseToken);
            return true;
        }

        private bool IsChannelActive(DirectManeuverActuatorChannel channel)
        {
            if (!channels.TryGetValue((0, channel), out var value)) return false;
            return value.HasAcceptedWrite ? value.LastAccepted >= 0.5 : value.Original >= 0.5;
        }

        private void Relinquish(long wheelId, DirectManeuverActuatorChannel channel)
        {
            if (channels.TryGetValue((wheelId, channel), out var lease)) lease.Relinquished = true;
        }

        private void LoseLease()
        {
            active = false;
            safetyLost = true;
            authority.Release(Identity, leaseToken);
        }

        private void ExpireLease()
        {
            active = false;
            authority.Release(Identity, leaseToken);
        }

        private static bool NearlyEqual(double left, double right) => Math.Abs(left - right) <= NeutralTolerance;
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static bool Fresh(DateTime capturedUtc, DateTime nowUtc) =>
            capturedUtc <= nowUtc && nowUtc - capturedUtc <= DirectManeuverPlan.EvidenceMaximumAge;
    }

    public sealed class DirectManeuverStopRequest
    {
        public readonly DirectManeuverOperationIdentity Identity;
        public readonly string RequestId;
        public readonly DirectManeuverStopKind Kind;
        public readonly DateTime RequestedUtc;

        public DirectManeuverStopRequest(DirectManeuverOperationIdentity identity, string requestId,
            DirectManeuverStopKind kind, DateTime requestedUtc)
        { Identity = identity; RequestId = requestId ?? string.Empty; Kind = kind; RequestedUtc = requestedUtc.ToUniversalTime(); }
    }

    public sealed class DirectManeuverStopAcceptance
    {
        public readonly DirectManeuverOperationIdentity Identity;
        public readonly string RequestId;
        public readonly bool Accepted;
        public readonly string Reason;
        public readonly DateTime AcceptedUtc;
        internal DirectManeuverStopAcceptance(DirectManeuverOperationIdentity identity,
            string requestId, bool accepted, string reason, DateTime acceptedUtc)
        { Identity = identity; RequestId = requestId; Accepted = accepted; Reason = reason; AcceptedUtc = acceptedUtc; }
    }

    public sealed class DirectManeuverCommandAcceptance
    {
        public readonly DirectManeuverOperationIdentity Identity;
        public readonly string CommandId;
        public readonly bool Accepted;
        public readonly DateTime AcknowledgedUtc;
        public readonly IDictionary<long, double> PropulsionByWheel;
        public DirectManeuverCommandAcceptance(DirectManeuverOperationIdentity identity,
            string commandId, bool accepted, DateTime acknowledgedUtc,
            IDictionary<long, double> propulsionByWheel = null)
        {
            Identity = identity;
            CommandId = commandId ?? string.Empty;
            Accepted = accepted;
            AcknowledgedUtc = acknowledgedUtc.ToUniversalTime();
            PropulsionByWheel = propulsionByWheel == null
                ? null : new Dictionary<long, double>(propulsionByWheel);
        }
    }

    public sealed class DirectManeuverTerminalReceipt
    {
        public readonly DirectManeuverOperationIdentity Identity;
        public readonly string RequestId;
        public readonly DirectManeuverOutcome Outcome;
        public readonly long FinalSimulationTick;
        public readonly long FinalHostSampleSequence;
        public readonly long FinalModSimulationTick;
        public readonly int StationarySamples;
        public readonly bool StationaryConfirmed;
        public readonly bool NeutralizationConfirmed;
        public readonly bool ServerTerminationRequired;
        public readonly DateTime IssuedUtc;
        internal DirectManeuverTerminalReceipt(DirectManeuverOperationIdentity identity,
            string requestId, DirectManeuverOutcome outcome, long finalHostSampleSequence,
            long finalModSimulationTick,
            int stationarySamples, bool stationaryConfirmed, bool neutralizationConfirmed,
            bool serverTerminationRequired, DateTime issuedUtc)
        { Identity = identity; RequestId = requestId; Outcome = outcome;
          FinalSimulationTick = finalModSimulationTick; FinalHostSampleSequence = finalHostSampleSequence;
          FinalModSimulationTick = finalModSimulationTick;
          StationarySamples = stationarySamples; StationaryConfirmed = stationaryConfirmed;
          NeutralizationConfirmed = neutralizationConfirmed; ServerTerminationRequired = serverTerminationRequired;
          IssuedUtc = issuedUtc; }
    }
}
