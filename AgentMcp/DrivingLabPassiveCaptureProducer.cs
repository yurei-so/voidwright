namespace Voidwright.AgentMcp;

/// <summary>
/// One bounded observation from the authoritative world observer. Implementations
/// must resolve only the requested grid and must never expose movement operations.
/// Human input must remain absent because server controller indicators also include autopilot.
/// </summary>
public sealed record DrivingLabPassiveGridSample(
    string SessionId,
    string RunId,
    long GridEntityId,
    DateTimeOffset CapturedAtUtc,
    long SimulationTick,
    double? SimulationTimeSeconds,
    PhysicsObservationFrame? Physics,
    PoseObservationFrame? Pose,
    GridStateObservationFrame? GridState,
    IReadOnlyList<ActuatorState>? WheelStates);

/// <summary>Read-only, exact-grid source. There is intentionally no command or actuator interface.</summary>
public interface IDrivingLabPassiveGridObservationSource
{
    bool TryObserveExactGrid(long gridEntityId, out DrivingLabPassiveGridSample? sample);
}

public enum DrivingLabPassiveCaptureRejection
{
    None,
    SelectionExpired,
    RateLimited,
    SourceUnavailable,
    IdentityMismatch,
    StaleSample,
    InvalidSample,
    BufferRejected,
    Stopped
}

/// <summary>
/// Opt-in one-grid producer over a trusted server observer. Selection is fixed at
/// construction, each poll addresses only that grid, and all records pass through
/// the existing bounded capture buffer.
/// </summary>
public sealed class DrivingLabPassiveCaptureProducer
{
    private readonly BoundDrivingLabPassiveSelection selection;
    private readonly IDrivingLabPassiveGridObservationSource source;
    private readonly DrivingLabCaptureBuffer buffer;
    private DateTimeOffset? lastAttemptAtUtc;
    private long sequence;
    private long droppedRecords;
    private bool stopped;

    private DrivingLabPassiveCaptureProducer(
        BoundDrivingLabPassiveSelection selection,
        IDrivingLabPassiveGridObservationSource source)
    {
        this.selection = selection;
        this.source = source;
        buffer = new DrivingLabCaptureBuffer(selection);
    }

    public static DrivingLabSelectionRejection TryStart(
        DrivingLabPassiveSelectionRequest? trustedUserSelection,
        DrivingLabPassiveGridInventory? currentInventory,
        IDrivingLabPassiveGridObservationSource source,
        DateTimeOffset nowUtc,
        out DrivingLabPassiveCaptureProducer? producer)
    {
        ArgumentNullException.ThrowIfNull(source);
        producer = null;
        var result = DrivingLabPassiveSelectionContract.TryBind(
            trustedUserSelection, currentInventory, nowUtc, out var selection);
        if (result != DrivingLabSelectionRejection.None || selection is null) return result;
        producer = new DrivingLabPassiveCaptureProducer(selection, source);
        return DrivingLabSelectionRejection.None;
    }

    public BoundDrivingLabPassiveSelection Selection => selection;
    public int RecordCount => buffer.RecordCount;
    public int BufferedBytes => buffer.BufferedBytes;

    public DrivingLabPassiveCaptureRejection TryCapture(
        DateTimeOffset nowUtc,
        out DrivingLabCaptureRecord? record)
    {
        record = null;
        if (stopped) return DrivingLabPassiveCaptureRejection.Stopped;
        var now = nowUtc.ToUniversalTime();
        if (now >= selection.ExpiresAtUtc) return DrivingLabPassiveCaptureRejection.SelectionExpired;
        var minimumSpacingTicks = (TimeSpan.TicksPerSecond + selection.MaximumSampleRateHz - 1) /
            selection.MaximumSampleRateHz;
        if (lastAttemptAtUtc is { } previous && now.UtcTicks - previous.UtcTicks < minimumSpacingTicks)
            return DrivingLabPassiveCaptureRejection.RateLimited;
        lastAttemptAtUtc = now;

        DrivingLabPassiveGridSample? sample;
        try
        {
            if (!source.TryObserveExactGrid(selection.GridEntityId, out sample) || sample is null)
                return DrivingLabPassiveCaptureRejection.SourceUnavailable;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return DrivingLabPassiveCaptureRejection.SourceUnavailable;
        }

        if (sample.SessionId != selection.SessionId || sample.RunId != selection.RunId ||
            sample.GridEntityId != selection.GridEntityId)
        {
            droppedRecords++;
            return DrivingLabPassiveCaptureRejection.IdentityMismatch;
        }
        if (sample.CapturedAtUtc.Offset != TimeSpan.Zero || sample.CapturedAtUtc > now ||
            now - sample.CapturedAtUtc > DrivingLabPassiveSelectionContract.MaximumInventoryAge ||
            sample.CapturedAtUtc >= selection.ExpiresAtUtc || sample.SimulationTick < 0 ||
            sample.SimulationTimeSeconds is { } simulationTime && (!double.IsFinite(simulationTime) || simulationTime < 0))
        {
            droppedRecords++;
            return DrivingLabPassiveCaptureRejection.StaleSample;
        }

        var nextSequence = sequence + 1;
        var candidate = new DrivingLabCaptureRecord
        {
            RunId = selection.RunId,
            SessionId = selection.SessionId,
            StreamId = "grid-" + selection.GridEntityId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Source = "server-passive-grid",
            Sequence = nextSequence,
            CapturedAtUtc = sample.CapturedAtUtc,
            ClockDomain = "server-simulation",
            SimulationTick = sample.SimulationTick,
            SimulationTimeSeconds = sample.SimulationTimeSeconds,
            GridEntityId = selection.GridEntityId,
            DroppedRecordsTotal = droppedRecords,
            HumanInput = CaptureSection<HumanInputFrame>.Unavailable(
                "human_intent_unavailable_server_indicators_mix_autopilot"),
            Physics = PhysicsSection(sample.Physics),
            Pose = PoseSection(sample.Pose),
            GridState = GridStateSection(sample.GridState),
            Actuators = WheelSection(sample.WheelStates),
            SoftwareGeometry = CaptureSection<GeometryObservationFrame>.Unavailable(
                "geometry_not_sampled_by_passive_grid_producer"),
            VisualFrame = CaptureSection<VisualObservationFrame>.Unavailable(
                "framebuffer_capture_unavailable")
        };

        try { DrivingLabCaptureValidator.Validate(candidate); }
        catch (InvalidDataException)
        {
            droppedRecords++;
            return DrivingLabPassiveCaptureRejection.InvalidSample;
        }
        var appendResult = buffer.TryAppend(candidate, now);
        if (appendResult != DrivingLabCaptureAppendRejection.None)
        {
            if (appendResult is not DrivingLabCaptureAppendRejection.RateLimit)
                droppedRecords++;
            return appendResult == DrivingLabCaptureAppendRejection.StaleRecord
                ? DrivingLabPassiveCaptureRejection.StaleSample
                : appendResult == DrivingLabCaptureAppendRejection.InvalidRecord
                    ? DrivingLabPassiveCaptureRejection.InvalidSample
                    : appendResult == DrivingLabCaptureAppendRejection.RateLimit
                        ? DrivingLabPassiveCaptureRejection.RateLimited
                        : DrivingLabPassiveCaptureRejection.BufferRejected;
        }

        sequence = nextSequence;
        record = candidate;
        return DrivingLabPassiveCaptureRejection.None;
    }

    public void Stop() => stopped = true;

    public byte[] ToArray() => buffer.ToArray();

    private static bool HasPhysics(PhysicsObservationFrame? value) => value is not null &&
        (value.VelocityX is not null || value.VelocityY is not null || value.VelocityZ is not null ||
         value.SpeedMetersPerSecond is not null || value.SimulationRatio is not null || value.PhysicsStepCount is not null);

    private static bool HasPose(PoseObservationFrame? value) => value is not null &&
        (value.PositionX is not null || value.PositionY is not null || value.PositionZ is not null ||
         value.RightX is not null || value.RightY is not null || value.RightZ is not null ||
         value.UpX is not null || value.UpY is not null || value.UpZ is not null ||
         value.ForwardX is not null || value.ForwardY is not null || value.ForwardZ is not null);

    private static bool HasGridState(GridStateObservationFrame? value) => value is not null &&
        (value.IsStatic is not null || value.GridSizeClass is not null || value.BlockCount is not null ||
         value.BlockCoverageComplete is not null);

    private static CaptureSection<PhysicsObservationFrame> PhysicsSection(PhysicsObservationFrame? value)
    {
        if (!HasPhysics(value)) return CaptureSection<PhysicsObservationFrame>.Unavailable("physics_observation_unavailable");
        return value!.VelocityX is not null && value.VelocityY is not null && value.VelocityZ is not null &&
               value.SpeedMetersPerSecond is not null
            ? CaptureSection<PhysicsObservationFrame>.Observed(value)
            : CaptureSection<PhysicsObservationFrame>.Partial(value, "physics_fields_unavailable");
    }

    private static CaptureSection<PoseObservationFrame> PoseSection(PoseObservationFrame? value)
    {
        if (!HasPose(value)) return CaptureSection<PoseObservationFrame>.Unavailable("pose_observation_unavailable");
        var fields = new[] { value!.PositionX, value.PositionY, value.PositionZ, value.RightX, value.RightY,
            value.RightZ, value.UpX, value.UpY, value.UpZ, value.ForwardX, value.ForwardY, value.ForwardZ };
        return fields.All(field => field is not null)
            ? CaptureSection<PoseObservationFrame>.Observed(value)
            : CaptureSection<PoseObservationFrame>.Partial(value, "pose_fields_unavailable");
    }

    private static CaptureSection<GridStateObservationFrame> GridStateSection(GridStateObservationFrame? value)
    {
        if (!HasGridState(value)) return CaptureSection<GridStateObservationFrame>.Unavailable("grid_state_unavailable");
        var complete = value!.IsStatic is not null && value.GridSizeClass is not null &&
                       value.BlockCount is not null && value.BlockCoverageComplete is true;
        return complete
            ? CaptureSection<GridStateObservationFrame>.Observed(value)
            : CaptureSection<GridStateObservationFrame>.Partial(value, "grid_state_incomplete");
    }

    private static CaptureSection<ActuatorObservationFrame> WheelSection(IReadOnlyList<ActuatorState>? value)
    {
        if (value is null) return CaptureSection<ActuatorObservationFrame>.Unavailable("wheel_state_unavailable");
        var frame = new ActuatorObservationFrame(value, "observed_only_no_control");
        var complete = value.All(wheel => wheel.Enabled is not null && wheel.OverrideActive is not null &&
                                          wheel.BrakeEnabled is not null &&
                                          (!wheel.OverrideActive.Value || wheel.OverrideValue is not null));
        return complete
            ? CaptureSection<ActuatorObservationFrame>.Observed(frame)
            : CaptureSection<ActuatorObservationFrame>.Partial(frame, "wheel_state_incomplete");
    }
}
