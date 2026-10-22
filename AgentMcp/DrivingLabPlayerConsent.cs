namespace Voidwright.AgentMcp;

/// <summary>
/// Consent supplied by a trusted UI/service after authenticating the human.
/// AuthenticatedPlayerIdentityId must come from the server's authenticated identity,
/// never from an MCP/model argument.
/// </summary>
public sealed record DrivingLabPlayerConsentRequest(
    bool ExplicitOptIn,
    long AuthenticatedPlayerIdentityId,
    string SessionId,
    string RunId,
    long GridEntityId,
    DateTimeOffset ConfirmedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    int MaximumSampleRateHz,
    int MaximumBufferedRecords,
    int MaximumBufferedBytes);

/// <summary>
/// Fresh authoritative identity/occupancy inventory. The connected roster may include
/// bystanders; the selected grid's occupants and human controllers must be the consenting player.
/// </summary>
public sealed record DrivingLabPlayerEnrollmentInventory(
    DrivingLabPassiveGridInventory GridInventory,
    IReadOnlyList<long> ConnectedHumanPlayerIdentityIds,
    long OccupantGridEntityId,
    IReadOnlyList<long> GridOccupantPlayerIdentityIds,
    bool AuthenticatedIdentityVerified,
    bool OccupantInventoryComplete,
    IReadOnlyList<long> GridControllerHumanPlayerIdentityIds,
    bool ControllerInventoryComplete);

/// <summary>Revocable, bounded authorization for one authenticated player and grid.</summary>
public sealed class BoundDrivingLabPlayerConsent
{
    private int revoked;

    internal BoundDrivingLabPlayerConsent(
        long playerIdentityId,
        BoundDrivingLabPassiveSelection selection)
    {
        PlayerIdentityId = playerIdentityId;
        Selection = selection;
    }

    public long PlayerIdentityId { get; }
    public BoundDrivingLabPassiveSelection Selection { get; }
    public string PermissionScope => "read_only_observation";
    public bool IsRevoked => Volatile.Read(ref revoked) != 0;

    public void Revoke() => Interlocked.Exchange(ref revoked, 1);
}

public enum DrivingLabPlayerConsentRejection
{
    None,
    MissingOptIn,
    InvalidPlayerIdentity,
    IncompleteOccupantInventory,
    OtherHumanControlsGrid,
    PlayerNotConnected,
    MultipleGridOccupants,
    PlayerNotOccupyingGrid,
    GridSelectionRejected
}

/// <summary>
/// Fails closed unless fresh inventory proves the authenticated player is connected
/// and is the only human occupying or controlling the consented grid.
/// </summary>
public static class DrivingLabPlayerConsentContract
{
    public static DrivingLabPlayerConsentRejection TryBind(
        DrivingLabPlayerConsentRequest? request,
        DrivingLabPlayerEnrollmentInventory? inventory,
        DateTimeOffset nowUtc,
        out BoundDrivingLabPlayerConsent? consent)
    {
        consent = null;
        if (request is null || inventory is null) return DrivingLabPlayerConsentRejection.InvalidPlayerIdentity;
        if (!request.ExplicitOptIn) return DrivingLabPlayerConsentRejection.MissingOptIn;
        if (request.AuthenticatedPlayerIdentityId <= 0) return DrivingLabPlayerConsentRejection.InvalidPlayerIdentity;
        if (!inventory.OccupantInventoryComplete || !inventory.ControllerInventoryComplete ||
            inventory.ConnectedHumanPlayerIdentityIds is null || inventory.GridOccupantPlayerIdentityIds is null ||
            inventory.GridControllerHumanPlayerIdentityIds is null || !inventory.AuthenticatedIdentityVerified)
            return DrivingLabPlayerConsentRejection.IncompleteOccupantInventory;
        if (inventory.ConnectedHumanPlayerIdentityIds.Count == 0 || inventory.ConnectedHumanPlayerIdentityIds.Any(id => id <= 0) ||
            inventory.ConnectedHumanPlayerIdentityIds.Distinct().Count() != inventory.ConnectedHumanPlayerIdentityIds.Count)
            return DrivingLabPlayerConsentRejection.InvalidPlayerIdentity;
        if (inventory.ConnectedHumanPlayerIdentityIds.Count(id => id == request.AuthenticatedPlayerIdentityId) != 1)
            return DrivingLabPlayerConsentRejection.PlayerNotConnected;
        if (inventory.OccupantGridEntityId != request.GridEntityId)
            return DrivingLabPlayerConsentRejection.PlayerNotOccupyingGrid;
        if (inventory.GridOccupantPlayerIdentityIds.Count != 1)
            return DrivingLabPlayerConsentRejection.MultipleGridOccupants;
        if (inventory.GridOccupantPlayerIdentityIds[0] != request.AuthenticatedPlayerIdentityId)
            return DrivingLabPlayerConsentRejection.PlayerNotOccupyingGrid;
        if (inventory.GridControllerHumanPlayerIdentityIds.Any(id => id <= 0))
            return DrivingLabPlayerConsentRejection.IncompleteOccupantInventory;
        if (inventory.GridControllerHumanPlayerIdentityIds.Any(id => id != request.AuthenticatedPlayerIdentityId))
            return DrivingLabPlayerConsentRejection.OtherHumanControlsGrid;

        var selectionRequest = new DrivingLabPassiveSelectionRequest(
            request.ExplicitOptIn, request.SessionId, request.RunId, request.GridEntityId,
            request.ConfirmedAtUtc, request.ExpiresAtUtc, request.MaximumSampleRateHz,
            request.MaximumBufferedRecords, request.MaximumBufferedBytes);
        var selectionResult = DrivingLabPassiveSelectionContract.TryBind(
            selectionRequest, inventory.GridInventory, nowUtc, out var selection);
        if (selectionResult != DrivingLabSelectionRejection.None || selection is null)
            return DrivingLabPlayerConsentRejection.GridSelectionRejected;

        consent = new BoundDrivingLabPlayerConsent(request.AuthenticatedPlayerIdentityId, selection);
        return DrivingLabPlayerConsentRejection.None;
    }
}

public enum DrivingLabPlayerCaptureRejection
{
    None,
    ConsentRevoked,
    CaptureRejected
}

/// <summary>
/// Consent-bound facade over the existing exact-grid passive producer. Revocation
/// stops further source reads; records remain server-observed and input stays unavailable.
/// </summary>
public sealed class DrivingLabPlayerPassiveCapture
{
    private readonly BoundDrivingLabPlayerConsent consent;
    private readonly DrivingLabPassiveCaptureProducer producer;

    private DrivingLabPlayerPassiveCapture(
        BoundDrivingLabPlayerConsent consent,
        DrivingLabPassiveCaptureProducer producer)
    {
        this.consent = consent;
        this.producer = producer;
    }

    public int RecordCount => producer.RecordCount;
    public int BufferedBytes => producer.BufferedBytes;
    public string PermissionScope => consent.PermissionScope;
    public long PlayerIdentityId => consent.PlayerIdentityId;

    public static DrivingLabPlayerConsentRejection TryStart(
        DrivingLabPlayerConsentRequest? request,
        DrivingLabPlayerEnrollmentInventory? inventory,
        IDrivingLabPassiveGridObservationSource source,
        DateTimeOffset nowUtc,
        out DrivingLabPlayerPassiveCapture? capture)
    {
        ArgumentNullException.ThrowIfNull(source);
        capture = null;
        var result = DrivingLabPlayerConsentContract.TryBind(request, inventory, nowUtc, out var consent);
        if (result != DrivingLabPlayerConsentRejection.None || consent is null) return result;

        var selection = consent.Selection;
        var producerRequest = new DrivingLabPassiveSelectionRequest(
            true, selection.SessionId, selection.RunId, selection.GridEntityId,
            request!.ConfirmedAtUtc, selection.ExpiresAtUtc, selection.MaximumSampleRateHz,
            selection.MaximumBufferedRecords, selection.MaximumBufferedBytes);
        if (DrivingLabPassiveCaptureProducer.TryStart(producerRequest, inventory!.GridInventory,
                source, nowUtc, out var producer) != DrivingLabSelectionRejection.None || producer is null)
            return DrivingLabPlayerConsentRejection.GridSelectionRejected;

        capture = new DrivingLabPlayerPassiveCapture(consent, producer);
        return DrivingLabPlayerConsentRejection.None;
    }

    public DrivingLabPlayerCaptureRejection TryCapture(
        DateTimeOffset nowUtc,
        out DrivingLabCaptureRecord? record)
    {
        record = null;
        if (consent.IsRevoked)
        {
            producer.Stop();
            return DrivingLabPlayerCaptureRejection.ConsentRevoked;
        }
        return producer.TryCapture(nowUtc, out record) == DrivingLabPassiveCaptureRejection.None
            ? DrivingLabPlayerCaptureRejection.None
            : DrivingLabPlayerCaptureRejection.CaptureRejected;
    }

    public void Revoke() => consent.Revoke();
    public void Stop() => producer.Stop();
    public byte[] ToArray() => producer.ToArray();
}
