using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Voidwright.Core
{
    public enum PreflightStatus
    {
        Pass,
        Fail,
        Unknown
    }

    public sealed class PreflightCheck
    {
        public readonly string Name;
        public readonly PreflightStatus Status;
        public readonly string Reason;
        public readonly string Evidence;
        public readonly string Source;
        public readonly DateTime TimestampUtc;

        public PreflightCheck(string name, PreflightStatus status, string reason,
            string evidence, string source, DateTime timestampUtc)
        {
            Name = name;
            Status = status;
            Reason = reason;
            Evidence = evidence;
            Source = source;
            TimestampUtc = timestampUtc.ToUniversalTime();
        }
    }

    /// <summary>
    /// Immutable, run-bound evidence for the movement command gate. This class
    /// deliberately has no game API or actuator reference.
    /// </summary>
    public sealed class MovementPreflight
    {
        public const int ExpectedCheckCount = 10;
        private static readonly string[] RequiredCheckNames = new[]
        {
            "fixture_source_integrity", "runtime_connectivity", "vehicle_identity",
            "voidwright_module_and_session_marker", "controller_readiness", "actuator_availability",
            "sensor_validity_freshness", "position_validity_freshness", "safety_configuration",
            "movement_interface_readiness"
        };
        private readonly List<PreflightCheck> checks;
        public readonly string RunId;
        public readonly string OperationId;
        public readonly string FixtureFingerprint;
        public readonly string StateFingerprint;
        public readonly DateTime CreatedAtUtc;
        public readonly bool Invalidated;
        public readonly string InvalidationReason;

        public MovementPreflight(string runId, string operationId, string fixtureFingerprint,
            string stateFingerprint, DateTime createdAtUtc, IList<PreflightCheck> checks)
            : this(runId, operationId, fixtureFingerprint, stateFingerprint, createdAtUtc, checks, false, string.Empty)
        {
        }

        private MovementPreflight(string runId, string operationId, string fixtureFingerprint,
            string stateFingerprint, DateTime createdAtUtc, IList<PreflightCheck> checks,
            bool invalidated, string invalidationReason)
        {
            RunId = runId ?? string.Empty;
            OperationId = operationId ?? string.Empty;
            FixtureFingerprint = fixtureFingerprint ?? string.Empty;
            StateFingerprint = stateFingerprint ?? string.Empty;
            CreatedAtUtc = createdAtUtc.ToUniversalTime();
            this.checks = new List<PreflightCheck>(checks ?? new PreflightCheck[0]);
            Invalidated = invalidated;
            InvalidationReason = invalidationReason ?? string.Empty;
        }

        public MovementPreflight Invalidate(DateTime nowUtc, string reason)
        {
            return new MovementPreflight(RunId, OperationId, FixtureFingerprint, StateFingerprint,
                nowUtc, checks, true, reason);
        }

        public int PassingCount
        {
            get
            {
                var count = 0;
                foreach (var check in checks)
                    if (check.Status == PreflightStatus.Pass) count++;
                return count;
            }
        }

        public int FailedCount
        {
            get
            {
                var count = 0;
                foreach (var check in checks)
                    if (check.Status == PreflightStatus.Fail) count++;
                return count;
            }
        }

        public int TotalCount { get { return checks.Count; } }

        public string NonPassingSummary()
        {
            var builder = new StringBuilder();
            foreach (var check in checks)
            {
                if (check == null || check.Status == PreflightStatus.Pass) continue;
                if (builder.Length > 0) builder.Append(';');
                builder.Append(check.Name).Append(':').Append(check.Status.ToString().ToUpperInvariant())
                    .Append(':').Append(Compact(check.Reason, 120))
                    .Append(':').Append(Compact(check.Evidence, 180));
                if (builder.Length >= 1200) return builder.ToString(0, 1200);
            }
            return builder.ToString();
        }

        private static string Compact(string value, int maximumLength)
        {
            if (string.IsNullOrEmpty(value)) return "unknown";
            var builder = new StringBuilder(Math.Min(value.Length, maximumLength));
            foreach (var c in value)
            {
                if (builder.Length >= maximumLength) break;
                builder.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' || c == ',' ? c : '_');
            }
            return builder.ToString();
        }

        /// <summary>
        /// Once the plugin host has approved this exact live heartbeat, any
        /// remaining failed, unknown, or incomplete mod-side check is a
        /// terminal denial. This prevents a rejected operation from spinning
        /// forever while the adapter keeps every actuator neutral.
        /// </summary>
        public static bool MustStopAfterHostApproval(MovementPreflight preflight, bool hostApprovalValid)
        {
            return preflight != null && hostApprovalValid &&
                   (preflight.Invalidated || preflight.TotalCount != ExpectedCheckCount ||
                    preflight.PassingCount != preflight.TotalCount);
        }

        public static bool HasOperationTimedOut(long currentTick, long startTick, int timeoutTicks)
        {
            return startTick >= 0 && currentTick >= startTick && timeoutTicks > 0 &&
                   currentTick - startTick >= timeoutTicks;
        }

        public static PreflightStatus ResolveSafetyStatus(bool contextAvailable,
            bool controllerAvailable, bool speedAvailable, bool safeConfiguration, bool operatorFree)
        {
            if (!contextAvailable || !controllerAvailable || !speedAvailable)
                return PreflightStatus.Unknown;
            return safeConfiguration && operatorFree ? PreflightStatus.Pass : PreflightStatus.Fail;
        }

        public static PreflightStatus ResolveActuatorStatus(bool controllerAvailable, int wheelCount,
            bool steeringAvailable, bool propulsionAvailable, bool overridesOwnedOrNeutral)
        {
            if (!controllerAvailable || wheelCount <= 0) return PreflightStatus.Unknown;
            return steeringAvailable && propulsionAvailable && overridesOwnedOrNeutral
                ? PreflightStatus.Pass : PreflightStatus.Fail;
        }

        public static PreflightStatus ResolveBrakeStatus(bool gridBrakeStateAvailable,
            bool gridBrakeActive, bool handbrakeActive, bool wheelBrakeStateAvailable,
            bool anyWheelBrakeActive)
        {
            if (!gridBrakeStateAvailable || !wheelBrakeStateAvailable) return PreflightStatus.Unknown;
            return gridBrakeActive || handbrakeActive || anyWheelBrakeActive
                ? PreflightStatus.Fail : PreflightStatus.Pass;
        }

        public static PreflightStatus RequireHostBrakeAttestation(PreflightStatus localControllerStatus,
            bool freshHostBrakeApproval)
        {
            if (localControllerStatus != PreflightStatus.Pass) return localControllerStatus;
            return freshHostBrakeApproval ? PreflightStatus.Pass : PreflightStatus.Unknown;
        }

        public static string ResolveTerminalStatus(string reason, string forcedStatus)
        {
            if (!string.IsNullOrWhiteSpace(forcedStatus)) return forcedStatus;
            if (string.IsNullOrEmpty(reason)) return "preflight-blocked";
            if (reason.StartsWith("preflight evidence write failed", StringComparison.Ordinal)) return "evidence-write-failed";
            if (reason == "preflight-only complete; no actuator dispatch was attempted") return "complete";
            if (reason == "arrival radius reached") return "complete";
            if (reason == "run tick limit reached") return "timeout";
            if (reason == "operator took control; autonomous movement stopped") return "human-connected";
            if (reason.StartsWith("sensor has no safe corridor", StringComparison.Ordinal)) return "depth-no-clear-corridor";
            if (reason.StartsWith("route guidance rejected", StringComparison.Ordinal)) return "invalid-guidance";
            if (reason.StartsWith("wheel already has an external", StringComparison.Ordinal)) return "actuator-conflict";
            return "preflight-blocked";
        }

        public bool IsAuthorizing(DateTime nowUtc, string runId, string operationId,
            string fixtureFingerprint, string stateFingerprint, TimeSpan maximumAge)
        {
            if (Invalidated || !IsIdentity(RunId) || !IsIdentity(OperationId) ||
                !string.Equals(RunId, runId, StringComparison.Ordinal) ||
                !string.Equals(OperationId, operationId, StringComparison.Ordinal) ||
                !string.Equals(FixtureFingerprint, fixtureFingerprint, StringComparison.Ordinal) ||
                !string.Equals(StateFingerprint, stateFingerprint, StringComparison.Ordinal) ||
                maximumAge < TimeSpan.Zero || nowUtc.ToUniversalTime() < CreatedAtUtc ||
                nowUtc.ToUniversalTime() - CreatedAtUtc > maximumAge ||
                !HasCompleteCheckSet())
                return false;
            foreach (var check in checks)
                if (check.Status != PreflightStatus.Pass ||
                    nowUtc.ToUniversalTime() < check.TimestampUtc ||
                    nowUtc.ToUniversalTime() - check.TimestampUtc > maximumAge)
                    return false;
            return true;
        }

        public bool HasOnlyUnknownChecks(params string[] unknownCheckNames)
        {
            if (Invalidated || !HasCompleteCheckSet() || unknownCheckNames == null || unknownCheckNames.Length == 0)
                return false;
            var expectedUnknown = new HashSet<string>(unknownCheckNames, StringComparer.Ordinal);
            if (expectedUnknown.Count != unknownCheckNames.Length) return false;
            foreach (var name in expectedUnknown)
                if (Array.IndexOf(RequiredCheckNames, name) < 0) return false;
            foreach (var check in checks)
            {
                var expectedStatus = expectedUnknown.Contains(check.Name)
                    ? PreflightStatus.Unknown : PreflightStatus.Pass;
                if (check.Status != expectedStatus) return false;
            }
            return true;
        }

        private bool HasCompleteCheckSet()
        {
            if (checks.Count != RequiredCheckNames.Length) return false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var check in checks)
                if (check == null || !seen.Add(check.Name)) return false;
            foreach (var name in RequiredCheckNames)
                if (!seen.Contains(name)) return false;
            return true;
        }

        public string ToJson()
        {
            var passing = PassingCount;
            var success = IsAuthorizing(CreatedAtUtc, RunId, OperationId, FixtureFingerprint,
                StateFingerprint, TimeSpan.FromSeconds(1));
            var builder = new StringBuilder();
            builder.Append("{\"schema\":\"vragecage.movement-preflight.v1\"");
            builder.Append(",\"run_id\":\"").Append(Escape(RunId)).Append('"');
            builder.Append(",\"operation_id\":\"").Append(Escape(OperationId)).Append('"');
            builder.Append(",\"created_at_utc\":\"").Append(Stamp(CreatedAtUtc)).Append('"');
            builder.Append(",\"fixture_fingerprint\":\"").Append(Escape(FixtureFingerprint)).Append('"');
            builder.Append(",\"state_fingerprint\":\"").Append(Escape(StateFingerprint)).Append('"');
            builder.Append(",\"invalidated\":").Append(Invalidated ? "true" : "false");
            builder.Append(",\"invalidation_reason\":\"").Append(Escape(InvalidationReason)).Append('"');
            builder.Append(",\"summary\":{\"passing\":").Append(passing)
                .Append(",\"total\":").Append(checks.Count)
                .Append(",\"success\":").Append(success ? "true" : "false").Append('}');
            builder.Append(",\"checks\":[");
            for (var index = 0; index < checks.Count; index++)
            {
                if (index != 0) builder.Append(',');
                var check = checks[index];
                builder.Append("{\"name\":\"").Append(Escape(check.Name)).Append('"');
                builder.Append(",\"status\":\"").Append(check.Status.ToString().ToUpperInvariant()).Append('"');
                builder.Append(",\"reason\":\"").Append(Escape(check.Reason)).Append('"');
                builder.Append(",\"evidence\":\"").Append(Escape(check.Evidence)).Append('"');
                builder.Append(",\"source\":\"").Append(Escape(check.Source)).Append('"');
                builder.Append(",\"timestamp_utc\":\"").Append(Stamp(check.TimestampUtc)).Append("\"}");
            }
            return builder.Append("]}").ToString();
        }

        private static bool IsIdentity(string value)
        {
            if (value == null || value.Length != 32) return false;
            foreach (var character in value)
                if (!((character >= '0' && character <= '9') ||
                      (character >= 'a' && character <= 'f') ||
                      (character >= 'A' && character <= 'F'))) return false;
            return true;
        }

        private static string Stamp(DateTime value)
        {
            return value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
        }

        private static string Escape(string value)
        {
            if (value == null) return string.Empty;
            var builder = new StringBuilder(value.Length + 8);
            foreach (var character in value)
            {
                switch (character)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (character < 0x20)
                            builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        else builder.Append(character);
                        break;
                }
            }
            return builder.ToString();
        }
    }

    public static class MovementCommandGate
    {
        public static bool TryDispatch(MovementPreflight preflight, DateTime nowUtc,
            string runId, string operationId, string fixtureFingerprint, string stateFingerprint,
            TimeSpan maximumAge, Action movementCommand)
        {
            if (preflight == null || movementCommand == null ||
                !preflight.IsAuthorizing(nowUtc, runId, operationId, fixtureFingerprint,
                    stateFingerprint, maximumAge))
                return false;
            movementCommand();
            return true;
        }

        /// <summary>
        /// Dispatch a movement callback only when its evidence is current. If the callback
        /// throws after it may have touched actuators, invoke the emergency stop before
        /// reporting the fault. A stop/reporting exception never turns the dispatch into
        /// success.
        /// </summary>
        public static bool TryDispatchSafely(MovementPreflight preflight, DateTime nowUtc,
            string runId, string operationId, string fixtureFingerprint, string stateFingerprint,
            TimeSpan maximumAge, Action movementCommand, Action emergencyStop,
            Action<Exception> onFailure)
        {
            if (preflight == null || movementCommand == null || emergencyStop == null ||
                !preflight.IsAuthorizing(nowUtc, runId, operationId, fixtureFingerprint,
                    stateFingerprint, maximumAge))
                return false;
            try
            {
                movementCommand();
                return true;
            }
            catch (Exception error)
            {
                try { emergencyStop(); } catch { }
                try { if (onFailure != null) onFailure(error); } catch { }
                return false;
            }
        }
    }

    public enum PreflightOnlyDecision
    {
        Wait,
        Complete,
        Failed
    }

    /// <summary>
    /// Read-only preflight collection may retry unknown startup observations, but it never
    /// completes without all checks and the run-bound host approval.
    /// </summary>
    public static class PreflightOnlyPolicy
    {
        public static PreflightOnlyDecision Decide(MovementPreflight preflight, bool hostApprovalValid)
        {
            if (preflight == null || preflight.Invalidated || preflight.TotalCount != MovementPreflight.ExpectedCheckCount ||
                preflight.FailedCount > 0)
                return PreflightOnlyDecision.Failed;
            if (hostApprovalValid && preflight.PassingCount == preflight.TotalCount)
                return PreflightOnlyDecision.Complete;
            return PreflightOnlyDecision.Wait;
        }
    }
}
