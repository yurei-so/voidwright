using System.Collections.Concurrent;

namespace Voidwright.AgentMcp;

public sealed record VerifiedVehiclePrincipal(string Subject, IReadOnlySet<string> Operations, DateTime ExpiresAtUtc);

public interface IVerifiedVehiclePrincipalProvider
{
    VerifiedVehiclePrincipal? Resolve();
}

public interface ILiveVehicleAdapter
{
    long GridEntityId { get; }
    object Observe();
    void ApplyGoal(string commandId, VehicleGoal goal);
    void CancelGoal(string commandId);
    VehicleStopOutcome Stop();
    void Release();
}

/// <summary>
/// Transport-independent bounded vehicle command. Position goals are for future
/// navigation adapters; the actuator kinds are intentionally small, short-lived
/// primitives that both the local test source and future MCP source can submit.
/// </summary>
public sealed record VehicleGoal(string Type, double X, double Y, double Z, double MaximumSpeedMetersPerSecond,
    int TimeoutSeconds);

public sealed record VehicleGoalReceipt(string CommandId, string SessionId, long GridEntityId, string State,
    long SessionGeneration, string? Result, DateTime AcceptedAtUtc, DateTime ExpiresAtUtc,
    VehicleStopOutcome? StopOutcome = null);
public sealed record VehicleStopOutcome(bool ActuatorsNeutralized, bool BrakesRequested,
    double? LinearSpeedMetersPerSecond, double? AngularSpeedRadiansPerSecond,
    int ConsecutiveStationarySamples, bool PhysicalStopObserved);

/// <summary>Process-local lifecycle boundary. Production enrollment remains closed until a trusted
/// MCP principal provider and a game-backed adapter are supplied.</summary>
public sealed class LiveVehicleRuntime(IVerifiedVehiclePrincipalProvider principals, Func<long, ILiveVehicleAdapter?> adapters)
{
    private readonly ConcurrentDictionary<string, Session> sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, VehicleGoalReceipt> receipts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, VehicleGoal> submittedGoals = new(StringComparer.Ordinal);
    private readonly object commandSync = new();
    private long nextSessionGeneration;

    public string Enroll(long gridEntityId, DateTime nowUtc)
    {
        var principal = AuthorizedPrincipal(nowUtc, "enroll");
        if (gridEntityId <= 0) throw new ArgumentOutOfRangeException(nameof(gridEntityId));
        var adapter = adapters(gridEntityId) ?? throw new InvalidOperationException("Exact enrolled grid has no live adapter.");
        if (adapter.GridEntityId != gridEntityId) throw new InvalidOperationException("Adapter grid identity mismatch.");
        var sessionId = Guid.NewGuid().ToString("N");
        var session = new Session(sessionId, Interlocked.Increment(ref nextSessionGeneration), gridEntityId, principal, adapter);
        if (!sessions.TryAdd(sessionId, session)) throw new InvalidOperationException("Session ID collision.");
        return sessionId;
    }

    public object Observe(string sessionId, DateTime nowUtc) => RequireSession(sessionId, nowUtc, "observe").Adapter.Observe();

    public VehicleGoalReceipt Submit(string sessionId, string commandId, VehicleGoal goal, DateTime nowUtc, bool replace = false)
    {
        var session = RequireSession(sessionId, nowUtc, "control");
        ValidateGoal(commandId, goal);
        lock (session.Sync)
        lock (commandSync)
        {
            if (receipts.TryGetValue(commandId, out var prior))
            {
                if (prior.SessionId != sessionId || prior.GridEntityId != session.GridEntityId)
                    throw new InvalidOperationException("Command ID is already bound to another session.");
                if (!submittedGoals.TryGetValue(commandId, out var priorGoal) || priorGoal != goal)
                    throw new InvalidOperationException("A duplicate command ID cannot change its goal.");
                return prior;
            }
            if (session.ActiveCommandId != null && !replace)
                throw new InvalidOperationException("An active goal exists; use atomic replacement or cancel it first.");
            if (session.ActiveCommandId != null)
                Terminalize(session.ActiveCommandId, "CANCELLED", "replaced", nowUtc);
            var expires = nowUtc.ToUniversalTime().AddSeconds(goal.TimeoutSeconds);
            var accepted = new VehicleGoalReceipt(commandId, sessionId, session.GridEntityId, "ACCEPTED",
                session.Generation, null, nowUtc.ToUniversalTime(), expires);
            if (!submittedGoals.TryAdd(commandId, goal))
                throw new InvalidOperationException("Command ID is already in use.");
            if (!receipts.TryAdd(commandId, accepted))
            {
                submittedGoals.TryRemove(commandId, out _);
                throw new InvalidOperationException("Command ID is already in use.");
            }
            session.ActiveCommandId = commandId;
            try
            {
                session.Adapter.ApplyGoal(commandId, goal);
                accepted = accepted with { State = "ACTIVE" };
                receipts[commandId] = accepted;
                return accepted;
            }
            catch
            {
                session.ActiveCommandId = null;
                accepted = accepted with { State = "FAILED", Result = "adapter_rejected" };
                receipts[commandId] = accepted;
                throw;
            }
        }
    }

    public VehicleGoalReceipt Cancel(string sessionId, string commandId, DateTime nowUtc)
    {
        var session = RequireSession(sessionId, nowUtc, "control");
        lock (session.Sync)
        {
            if (!receipts.TryGetValue(commandId, out var receipt) || receipt.SessionId != sessionId)
                throw new InvalidOperationException("Command is not bound to this session.");
            if (receipt.State is "COMPLETED" or "FAILED" or "CANCELLED" or "EXPIRED") return receipt;
            session.Adapter.CancelGoal(commandId);
            var stopOutcome = session.Adapter.Stop();
            session.ActiveCommandId = null;
            var cancelled = Terminalize(commandId, "CANCELLED", "cancel_acknowledged", nowUtc);
            cancelled = cancelled with { StopOutcome = stopOutcome };
            receipts[commandId] = cancelled;
            return cancelled;
        }
    }

    public VehicleGoalReceipt? Status(string sessionId, string commandId, DateTime nowUtc)
    {
        var session = RequireSession(sessionId, nowUtc, "observe");
        if (!receipts.TryGetValue(commandId, out var receipt)) return null;
        if (receipt.SessionId != sessionId || receipt.GridEntityId != session.GridEntityId)
            throw new InvalidOperationException("Command is not bound to this exact session.");
        if (receipt.State is "ACCEPTED" or "ACTIVE" && nowUtc.ToUniversalTime() >= receipt.ExpiresAtUtc)
        {
            if (sessions.TryGetValue(receipt.SessionId, out var activeSession))
                lock (activeSession.Sync)
                {
                    if (activeSession.ActiveCommandId == commandId)
                    {
                        var outcome = activeSession.Adapter.Stop();
                        activeSession.ActiveCommandId = null;
                        var expired = Terminalize(commandId, "EXPIRED", "timeout_stop_requested", nowUtc);
                        expired = expired with { StopOutcome = outcome };
                        receipts[commandId] = expired;
                        return expired;
                    }
                }
            return Terminalize(commandId, "EXPIRED", "timeout_stop_requested_physical_stop_unconfirmed", nowUtc);
        }
        return receipt;
    }

    public VehicleGoalReceipt Complete(string sessionId, string commandId, DateTime nowUtc, string result)
    {
        var session = RequireSession(sessionId, nowUtc, "control");
        lock (session.Sync)
        {
            if (session.ActiveCommandId != commandId || !receipts.TryGetValue(commandId, out var receipt) ||
                receipt.SessionId != sessionId)
                throw new InvalidOperationException("Only the active goal in this session can complete.");
            session.ActiveCommandId = null;
            var completed = receipt with { State = "COMPLETED", Result = string.IsNullOrWhiteSpace(result) ? "completed" : result };
            receipts[commandId] = completed;
            return completed;
        }
    }

    public VehicleStopOutcome Stop(string sessionId, DateTime nowUtc)
    {
        // Stop is deliberately independent of ordinary command authorization and expiry.
        // Possession of an enrolled session ID can request only neutralization/braking.
        if (!sessions.TryGetValue(sessionId ?? string.Empty, out var session) ||
            session.GridEntityId != session.Adapter.GridEntityId)
            throw new InvalidOperationException("No exact enrolled session is available for emergency stop.");
        lock (session.Sync)
        {
            var outcome = session.Adapter.Stop();
            if (session.ActiveCommandId != null)
            {
                var receipt = Terminalize(session.ActiveCommandId, "CANCELLED",
                    "emergency_stop_requested", nowUtc);
                receipts[receipt.CommandId] = receipt with { StopOutcome = outcome };
                session.ActiveCommandId = null;
            }
            return outcome;
        }
    }

    public void Release(string sessionId, DateTime nowUtc)
    {
        var session = RequireSession(sessionId, nowUtc, "release");
        lock (session.Sync)
        {
            if (session.ActiveCommandId != null)
            {
                var outcome = session.Adapter.Stop();
                var receipt = Terminalize(session.ActiveCommandId, "CANCELLED", "session_release_stop_requested", nowUtc);
                receipts[receipt.CommandId] = receipt with { StopOutcome = outcome };
                session.ActiveCommandId = null;
            }
            session.Adapter.Release();
            session.Valid = false;
            sessions.TryRemove(sessionId, out _);
        }
    }

    private Session RequireSession(string sessionId, DateTime nowUtc, string operation)
    {
        var principal = AuthorizedPrincipal(nowUtc, operation);
        if (!sessions.TryGetValue(sessionId ?? string.Empty, out var session) || !session.Valid ||
            session.Principal.Subject != principal.Subject || session.Principal.ExpiresAtUtc <= nowUtc.ToUniversalTime() ||
            session.GridEntityId != session.Adapter.GridEntityId)
            throw new InvalidOperationException("Session is invalid, expired, or not authorized for this principal.");
        return session;
    }

    private VerifiedVehiclePrincipal AuthorizedPrincipal(DateTime nowUtc, string operation)
    {
        var principal = principals.Resolve();
        if (principal == null || string.IsNullOrWhiteSpace(principal.Subject) ||
            principal.ExpiresAtUtc.Kind != DateTimeKind.Utc || principal.ExpiresAtUtc <= nowUtc.ToUniversalTime() ||
            !principal.Operations.Contains(operation))
            throw new UnauthorizedAccessException("No verified, unexpired principal is authorized for this operation.");
        return principal;
    }

    private VehicleGoalReceipt Terminalize(string commandId, string state, string result, DateTime nowUtc)
    {
        var current = receipts[commandId];
        var terminal = current with { State = state, Result = result };
        receipts[commandId] = terminal;
        return terminal;
    }

    private static void ValidateGoal(string commandId, VehicleGoal goal)
    {
        if (string.IsNullOrWhiteSpace(commandId) || commandId.Length > 128 || commandId.Any(char.IsControl))
            throw new ArgumentException("Command ID is invalid.");
        if (goal == null || !Finite(goal.X) || !Finite(goal.Y) || !Finite(goal.Z) ||
            !Finite(goal.MaximumSpeedMetersPerSecond))
            throw new ArgumentException("Goal is malformed or exceeds offline bounded-control limits.");

        var valid = goal.Type switch
        {
            "position" => goal.MaximumSpeedMetersPerSecond is >= 0 and <= 0.8 &&
                          goal.TimeoutSeconds is >= 1 and <= 300,
            // Overrides are normalized proportions. The deliberately small
            // local-test envelope leaves room to verify sign before movement.
            "steering" => Math.Abs(goal.X) <= 0.2 && goal.Y == 0 && goal.Z == 0 &&
                          goal.MaximumSpeedMetersPerSecond == 0 && goal.TimeoutSeconds is >= 1 and <= 5,
            "propulsion" => Math.Abs(goal.X) <= 0.03 && goal.Y == 0 && goal.Z == 0 &&
                            goal.MaximumSpeedMetersPerSecond is > 0 and <= 0.2 &&
                            goal.TimeoutSeconds is >= 1 and <= 5,
            "brake" => goal.X == 1 && goal.Y == 0 && goal.Z == 0 &&
                       goal.MaximumSpeedMetersPerSecond == 0 && goal.TimeoutSeconds is >= 1 and <= 10,
            "neutral" => goal.X == 0 && goal.Y == 0 && goal.Z == 0 &&
                         goal.MaximumSpeedMetersPerSecond == 0 && goal.TimeoutSeconds is >= 1 and <= 5,
            _ => false
        };
        if (!valid)
            throw new ArgumentException("Goal is malformed or exceeds offline bounded-control limits.");
    }

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private sealed class Session(string id, long generation, long gridId, VerifiedVehiclePrincipal principal, ILiveVehicleAdapter adapter)
    {
        public string Id { get; } = id;
        public long Generation { get; } = generation;
        public long GridEntityId { get; } = gridId;
        public VerifiedVehiclePrincipal Principal { get; } = principal;
        public ILiveVehicleAdapter Adapter { get; } = adapter;
        public object Sync { get; } = new();
        public string? ActiveCommandId;
        public bool Valid = true;
    }
}

/// <summary>Fail-closed provider used by the stdio MCP host until transport authentication exists.</summary>
public sealed class NoVerifiedVehiclePrincipalProvider : IVerifiedVehiclePrincipalProvider
{
    public VerifiedVehiclePrincipal? Resolve() => null;
}
