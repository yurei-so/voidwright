using System;
using System.Collections.Generic;
using Voidwright.Core;

namespace Voidwright.ServerPlugin
{
    /// <summary>Only the participating local-writer scan passed; this says nothing about timers,
    /// attached grids, other mods/plugins, or races from writers outside this broker.</summary>
    public enum DirectManeuverExternalWriterState { Unknown, LocalParticipatingWriterAuditPassed }
    public enum DirectManeuverBrokerRoute { Reject, DirectBroker, LegacyCommand }

    /// <summary>Routing rule shared by the host gate and offline contract checks.</summary>
    public static class DirectManeuverBrokerRoutePolicy
    {
        public static DirectManeuverBrokerRoute Resolve(string commandKind, bool leaseActive,
            bool runtimeCapabilityReady)
        {
            if (string.Equals(commandKind, "direct-maneuver", StringComparison.Ordinal))
                return runtimeCapabilityReady ? DirectManeuverBrokerRoute.DirectBroker : DirectManeuverBrokerRoute.Reject;
            if (leaseActive) return DirectManeuverBrokerRoute.Reject;
            return DirectManeuverBrokerRoute.LegacyCommand;
        }
    }

    public static class DirectManeuverHostSamplingPolicy
    {
        public static bool ShouldContinue(bool terminal, bool commandActive,
            bool leaseOrStopPending, bool unresolvedStop)
        {
            return !terminal || commandActive || leaseOrStopPending || unresolvedStop;
        }
    }

    /// <summary>
    /// Offline/testable single-writer contract. The lock serializes only calls
    /// made through this broker; it cannot exclude arbitrary game or mod writers.
    /// </summary>
    public sealed class DirectManeuverActuatorBroker
    {
        private readonly object sync = new object();
        private readonly HashSet<string> participants = new HashSet<string>(StringComparer.Ordinal);
        private DirectManeuverOperationLease lease;
        private DirectManeuverOperationIdentity identity;
        private DirectManeuverExternalWriterState externalState;
        private bool stopPending;
        private string stopRequestId;
        private DirectManeuverStopCapability stopCapability;
        private string movementCapabilityToken;

        public DirectManeuverActuatorBroker(IEnumerable<string> participatingWriters,
            DirectManeuverExternalWriterState externalWriterState)
        {
            if (participatingWriters != null)
                foreach (var writer in participatingWriters)
                    if (!string.IsNullOrWhiteSpace(writer)) participants.Add(writer);
            externalState = externalWriterState;
        }

        public bool LeaseActive(DateTime nowUtc)
        {
            lock (sync) return lease != null && lease.IsDispatchAuthorized(identity, nowUtc);
        }

        /// <summary>True while host sampling is required to resolve an owned lease or stop request.</summary>
        public bool SamplingRequired
        {
            get { lock (sync) return lease != null || stopPending; }
        }

        public bool StopPending
        {
            get { lock (sync) return stopPending; }
        }

        public DirectManeuverOperationLease ActiveLease(DateTime nowUtc)
        {
            lock (sync) return lease != null && lease.IsDispatchAuthorized(identity, nowUtc) ? lease : null;
        }

        public bool TryAcquire(DirectManeuverPlan plan, DirectManeuverEvidence evidence,
            IList<DirectManeuverWheelState> snapshot, DirectManeuverBrakeState brakes,
            DateTime nowUtc, TimeSpan duration, bool runtimeCapabilityReady, out string reason)
        {
            lock (sync)
            {
                if (!runtimeCapabilityReady) { reason = "runtime dispatch capability is disabled"; return false; }
                if (externalState != DirectManeuverExternalWriterState.LocalParticipatingWriterAuditPassed)
                { reason = "external writer state is unknown"; return false; }
                if (stopPending)
                { reason = "the prior operation still has an unresolved stop"; return false; }
                if (lease != null && lease.IsDispatchAuthorized(identity, nowUtc))
                { reason = "a broker lease is already active"; return false; }
                if (!DirectManeuverOperationLease.TryAcquire(plan, evidence, snapshot, brakes,
                    new DirectManeuverOperationLeaseAuthority(), nowUtc, duration, out var acquired, out reason)) return false;
                lease = acquired;
                identity = acquired.Identity;
                stopPending = false;
                stopRequestId = null;
                stopCapability = null;
                movementCapabilityToken = Guid.NewGuid().ToString("N");
                return true;
            }
        }

        /// <summary>Runs an accepted participating writer transaction under the single broker lock.</summary>
        public DirectManeuverLeaseWriteResult TryWrite(string writerId,
            DirectManeuverOperationIdentity operation, long wheelId,
            DirectManeuverActuatorChannel channel, double before, double requested,
            double after, DateTime nowUtc)
        {
            lock (sync)
            {
                if (!participants.Contains(writerId ?? string.Empty) || externalState != DirectManeuverExternalWriterState.LocalParticipatingWriterAuditPassed)
                    return DirectManeuverLeaseWriteResult.IdentityMismatch;
                if (stopPending &&
                    ((channel == DirectManeuverActuatorChannel.SteeringOverride ||
                      channel == DirectManeuverActuatorChannel.PropulsionOverride) &&
                     Math.Abs(requested) > DirectManeuverOperationLease.NeutralTolerance ||
                     (channel == DirectManeuverActuatorChannel.GridBrake ||
                      channel == DirectManeuverActuatorChannel.HandBrake) && requested < 0.5))
                    return DirectManeuverLeaseWriteResult.IdentityMismatch;
                if (lease == null || !lease.IsDispatchAuthorized(operation, nowUtc))
                    return DirectManeuverLeaseWriteResult.LeaseExpired;
                return lease.RecordAcceptedChannelWrite(operation, wheelId, channel, before, requested, after, nowUtc);
            }
        }

        public bool TryRunParticipatingTransaction(string writerId,
            DirectManeuverOperationIdentity operation, DateTime nowUtc, Func<bool> transaction,
            bool controlledStop = false)
        {
            if (transaction == null) return false;
            lock (sync)
            {
                if (!participants.Contains(writerId ?? string.Empty) ||
                    externalState != DirectManeuverExternalWriterState.LocalParticipatingWriterAuditPassed ||
                    (stopPending && !controlledStop) ||
                    lease == null || !lease.IsDispatchAuthorized(operation, nowUtc)) return false;
                return transaction();
            }
        }

        public string GetMovementCapabilityToken(DirectManeuverOperationIdentity operation, DateTime nowUtc)
        {
            lock (sync)
            {
                if (identity == null || !identity.Matches(operation)) return string.Empty;
                if (stopPending || lease == null || string.IsNullOrEmpty(movementCapabilityToken) ||
                    !lease.IsDispatchAuthorized(operation, nowUtc))
                {
                    movementCapabilityToken = null;
                    return string.Empty;
                }
                return movementCapabilityToken;
            }
        }

        public bool IsMovementCapabilityCurrent(string writerId,
            DirectManeuverOperationIdentity operation, string token, DateTime nowUtc)
        {
            lock (sync)
                return participants.Contains(writerId ?? string.Empty) && !stopPending &&
                    externalState == DirectManeuverExternalWriterState.LocalParticipatingWriterAuditPassed &&
                    lease != null && !string.IsNullOrEmpty(movementCapabilityToken) &&
                    string.Equals(token, movementCapabilityToken, StringComparison.Ordinal) &&
                    lease.IsDispatchAuthorized(operation, nowUtc);
        }

        public bool TryRunCapabilityTransaction(string writerId,
            DirectManeuverOperationIdentity operation, string token, DateTime nowUtc,
            Func<bool> transaction)
        {
            if (transaction == null) return false;
            lock (sync)
            {
                if (!participants.Contains(writerId ?? string.Empty) || stopPending ||
                    externalState != DirectManeuverExternalWriterState.LocalParticipatingWriterAuditPassed ||
                    lease == null || string.IsNullOrEmpty(movementCapabilityToken) ||
                    !string.Equals(token, movementCapabilityToken, StringComparison.Ordinal) ||
                    !lease.IsDispatchAuthorized(operation, nowUtc)) return false;
                return transaction();
            }
        }

        public bool RequestStop(DirectManeuverStopRequest request)
        {
            DirectManeuverStopCapability ignored;
            return RequestStop(request, out ignored);
        }

        public bool RequestStop(DirectManeuverStopRequest request,
            out DirectManeuverStopCapability capability)
        {
            lock (sync)
            {
                capability = null;
                if (request == null || identity == null || !identity.Matches(request.Identity) ||
                    string.IsNullOrWhiteSpace(request.RequestId) || request.RequestId.Length > 128)
                    return false;
                if (stopPending)
                {
                    if (stopRequestId != request.RequestId || stopCapability == null) return false;
                    capability = stopCapability;
                    return true;
                }
                stopPending = true;
                stopRequestId = request.RequestId;
                stopCapability = new DirectManeuverStopCapability(identity, request.RequestId);
                movementCapabilityToken = null;
                capability = stopCapability;
                return true;
            }
        }

        /// <summary>
        /// The only engine-setter funnel for a stop-only capability. It accepts
        /// neutral wheel values or active brakes, validates the exact operation,
        /// and records the immediate readback while holding the broker lock.
        /// </summary>
        public DirectManeuverLeaseWriteResult TryWriteStopChannel(string writerId,
            DirectManeuverStopCapability capability, long wheelId,
            DirectManeuverActuatorChannel channel, double requested,
            Func<double> read, Action<double> write, DateTime nowUtc)
        {
            lock (sync)
            {
                var before = read == null ? double.NaN : SafeRead(read);
                if (read == null || write == null || capability == null ||
                    !participants.Contains(writerId ?? string.Empty) ||
                    externalState != DirectManeuverExternalWriterState.LocalParticipatingWriterAuditPassed ||
                    !stopPending || !ReferenceEquals(stopCapability, capability) ||
                    stopRequestId != capability.RequestId || identity == null ||
                    !identity.Matches(capability.Identity) || lease == null ||
                    !lease.CanWriteStopChannel(identity, wheelId, channel, before))
                    return DirectManeuverLeaseWriteResult.IdentityMismatch;
                if (!IsStopValueAllowed(channel, requested)) return DirectManeuverLeaseWriteResult.InvalidValue;
                try { write(requested); }
                catch { return DirectManeuverLeaseWriteResult.WriteNotApplied; }
                var observedAfter = SafeRead(read);
                return lease.RecordStopChannelWrite(identity, wheelId, channel,
                    before, requested, observedAfter);
            }
        }

        private static double SafeRead(Func<double> read)
        {
            try { return read(); }
            catch { return double.NaN; }
        }

        private static bool IsStopValueAllowed(DirectManeuverActuatorChannel channel, double requested)
        {
            if (channel == DirectManeuverActuatorChannel.SteeringOverride ||
                channel == DirectManeuverActuatorChannel.PropulsionOverride)
                return Math.Abs(requested) <= DirectManeuverOperationLease.NeutralTolerance;
            return (channel == DirectManeuverActuatorChannel.GridBrake ||
                channel == DirectManeuverActuatorChannel.HandBrake) && requested == 1;
        }

        public DirectManeuverBrokerStopReceipt CompleteStop(DirectManeuverStopRequest request,
            bool timedOut, bool stationaryConfirmed, IList<DirectManeuverWheelState> finalStates,
            DirectManeuverBrakeState finalBrakes, DateTime issuedUtc)
        {
            lock (sync)
            {
                var matches = request != null && identity != null && identity.Matches(request.Identity) &&
                    !string.IsNullOrWhiteSpace(request.RequestId) &&
                    (!stopPending || stopRequestId == request.RequestId);
                var neutralReadback = finalStates != null && finalStates.Count > 0;
                if (neutralReadback)
                    foreach (var wheel in finalStates)
                        if (wheel == null || !wheel.BrakeStateKnown ||
                            Math.Abs(wheel.SteeringOverride) > DirectManeuverOperationLease.NeutralTolerance ||
                            Math.Abs(wheel.PropulsionOverride) > DirectManeuverOperationLease.NeutralTolerance)
                        { neutralReadback = false; break; }
                var success = matches && !timedOut && stationaryConfirmed && neutralReadback &&
                    finalBrakes != null && finalBrakes.GridBrakeKnown && finalBrakes.GridBrakeActive &&
                    finalBrakes.HandBrakeKnown && finalBrakes.HandBrakeActive && lease != null &&
                    lease.ReleaseAfterConfirmedStop(identity, finalStates, finalBrakes, issuedUtc);
                if (success)
                {
                    lease = null;
                    stopPending = false;
                    stopRequestId = null;
                    stopCapability = null;
                    movementCapabilityToken = null;
                }
                if (matches && !success)
                {
                    stopPending = true;
                    stopRequestId = request.RequestId;
                }
                return new DirectManeuverBrokerStopReceipt(request == null ? null : request.Identity,
                    request == null ? string.Empty : request.RequestId, success,
                    timedOut ? "stop timed out before stationary confirmation" :
                    !matches ? "stop request identity does not match active operation" :
                    !stationaryConfirmed ? "stationary terminal confirmation is missing" :
                    !neutralReadback ? "owned actuator channels are not read back neutral" :
                    finalBrakes == null || !finalBrakes.GridBrakeKnown || !finalBrakes.HandBrakeKnown ||
                    !finalBrakes.GridBrakeActive || !finalBrakes.HandBrakeActive ? "controlled brake state is not read back active" :
                    !success ? "owned actuator cleanup could not be verified" : "stop confirmed",
                    issuedUtc);
            }
        }
    }

    /// <summary>Opaque, broker-minted permission to neutralize and brake one operation.</summary>
    public sealed class DirectManeuverStopCapability
    {
        internal DirectManeuverStopCapability(DirectManeuverOperationIdentity identity, string requestId)
        { Identity = identity; RequestId = requestId; }
        public DirectManeuverOperationIdentity Identity { get; }
        public string RequestId { get; }
    }

    public sealed class DirectManeuverBrokerStopReceipt
    {
        public readonly DirectManeuverOperationIdentity Identity;
        public readonly string RequestId, Reason;
        public readonly bool Succeeded;
        public readonly DateTime IssuedUtc;
        internal DirectManeuverBrokerStopReceipt(DirectManeuverOperationIdentity identity,
            string requestId, bool succeeded, string reason, DateTime issuedUtc)
        { Identity = identity; RequestId = requestId; Succeeded = succeeded; Reason = reason; IssuedUtc = issuedUtc.ToUniversalTime(); }
    }
}
