namespace Voidwright.AgentMcp;

/// <summary>
/// Human-approved, one-grid read-only selection. The host runtime must supply
/// consent after explicit user opt-in; this contract never grants control.
/// </summary>
public sealed record DrivingLabPassiveSelectionRequest(
    bool ExplicitOptIn,
    string SessionId,
    string RunId,
    long GridEntityId,
    DateTimeOffset ConfirmedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    int MaximumSampleRateHz,
    int MaximumBufferedRecords,
    int MaximumBufferedBytes);

/// <summary>Complete inventory from a passive server-side grid observer.</summary>
public sealed record DrivingLabPassiveGridInventory(
    string SessionId,
    string RunId,
    DateTimeOffset CapturedAtUtc,
    long SimulationTick,
    bool Complete,
    IReadOnlyList<long> GridEntityIds);

/// <summary>Resolved selection; contains observation scope only and no actuator permissions.</summary>
public sealed record BoundDrivingLabPassiveSelection(
    string SessionId,
    string RunId,
    long GridEntityId,
    DateTimeOffset ExpiresAtUtc,
    int MaximumSampleRateHz,
    int MaximumBufferedRecords,
    int MaximumBufferedBytes,
    string PermissionScope = "read_only_observation");

public enum DrivingLabSelectionRejection
{
    None,
    MissingOptIn,
    InvalidIdentity,
    ExpiredOptIn,
    InvalidLimits,
    StaleInventory,
    IncompleteInventory,
    InventoryIdentityMismatch,
    MissingGrid,
    AmbiguousGrid
}

/// <summary>
/// Binds explicit consent to one exact session/run/grid tuple and a complete,
/// fresh passive grid inventory. It does not enumerate entities or capture data.
/// </summary>
public static class DrivingLabPassiveSelectionContract
{
    public const int MaximumSampleRateHz = 10;
    public const int MaximumInventoryGridCount = 4096;
    public const int MaximumBufferedRecords = DrivingLabCaptureValidator.MaximumRecords;
    public const int MaximumBufferedBytes = (int)DrivingLabCaptureReplayReader.MaximumReplayBytes;
    public static readonly TimeSpan MaximumOptInLifetime = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan MaximumInventoryAge = TimeSpan.FromSeconds(5);

    public static DrivingLabSelectionRejection TryBind(
        DrivingLabPassiveSelectionRequest? request,
        DrivingLabPassiveGridInventory? inventory,
        DateTimeOffset nowUtc,
        out BoundDrivingLabPassiveSelection? selection)
    {
        selection = null;
        var now = nowUtc.ToUniversalTime();
        if (request is null || inventory is null) return DrivingLabSelectionRejection.InvalidIdentity;
        if (!request.ExplicitOptIn) return DrivingLabSelectionRejection.MissingOptIn;
        if (!Identity(request.SessionId) || !Identity(request.RunId) || request.GridEntityId <= 0 ||
            !Identity(inventory.SessionId) || !Identity(inventory.RunId) ||
            inventory.SimulationTick < 0 || inventory.GridEntityIds is null ||
            inventory.GridEntityIds.Count > MaximumInventoryGridCount)
            return DrivingLabSelectionRejection.InvalidIdentity;
        if (request.ConfirmedAtUtc.Offset != TimeSpan.Zero || request.ExpiresAtUtc.Offset != TimeSpan.Zero ||
            request.ConfirmedAtUtc > now || request.ExpiresAtUtc <= now ||
            request.ExpiresAtUtc <= request.ConfirmedAtUtc ||
            request.ExpiresAtUtc - request.ConfirmedAtUtc > MaximumOptInLifetime)
            return DrivingLabSelectionRejection.ExpiredOptIn;
        if (request.MaximumSampleRateHz is < 1 or > MaximumSampleRateHz ||
            request.MaximumBufferedRecords is < 1 or > MaximumBufferedRecords ||
            request.MaximumBufferedBytes is < 1024 or > MaximumBufferedBytes)
            return DrivingLabSelectionRejection.InvalidLimits;
        if (inventory.CapturedAtUtc.Offset != TimeSpan.Zero || inventory.CapturedAtUtc > now ||
            now - inventory.CapturedAtUtc > MaximumInventoryAge)
            return DrivingLabSelectionRejection.StaleInventory;
        if (!inventory.Complete || inventory.GridEntityIds.Count == 0)
            return DrivingLabSelectionRejection.IncompleteInventory;
        if (!string.Equals(request.SessionId, inventory.SessionId, StringComparison.Ordinal) ||
            !string.Equals(request.RunId, inventory.RunId, StringComparison.Ordinal))
            return DrivingLabSelectionRejection.InventoryIdentityMismatch;
        if (inventory.GridEntityIds.Any(id => id <= 0) || inventory.GridEntityIds.Distinct().Count() != inventory.GridEntityIds.Count)
            return DrivingLabSelectionRejection.AmbiguousGrid;
        var matches = inventory.GridEntityIds.Count(id => id == request.GridEntityId);
        if (matches == 0) return DrivingLabSelectionRejection.MissingGrid;
        if (matches != 1) return DrivingLabSelectionRejection.AmbiguousGrid;

        selection = new BoundDrivingLabPassiveSelection(request.SessionId, request.RunId,
            request.GridEntityId, request.ExpiresAtUtc, request.MaximumSampleRateHz,
            request.MaximumBufferedRecords, request.MaximumBufferedBytes);
        return DrivingLabSelectionRejection.None;
    }

    private static bool Identity(string value) => value is { Length: 32 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
