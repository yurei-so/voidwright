using System.Text.Json;
using System.Text.Json.Serialization;

namespace Voidwright.AgentMcp;

public sealed record DrivingLabCaptureRecord
{
    public int SchemaVersion { get; init; } = DrivingLabCaptureValidator.CurrentSchemaVersion;
    public required string RunId { get; init; }
    public required string SessionId { get; init; }
    public required string StreamId { get; init; }
    public required string Source { get; init; }
    public long Sequence { get; init; }
    public DateTimeOffset CapturedAtUtc { get; init; }
    public required string ClockDomain { get; init; }
    public long? SimulationTick { get; init; }
    public double? SimulationTimeSeconds { get; init; }
    public long? GridEntityId { get; init; }
    public string? ConfigurationSha256 { get; init; }
    public long? DroppedRecordsTotal { get; init; }
    public CaptureSection<HumanInputFrame> HumanInput { get; init; } = CaptureSection<HumanInputFrame>.Unavailable("input_api_unavailable");
    public CaptureSection<ActuatorObservationFrame> Actuators { get; init; } = CaptureSection<ActuatorObservationFrame>.Unavailable("actuator_observation_unavailable");
    public CaptureSection<PhysicsObservationFrame> Physics { get; init; } = CaptureSection<PhysicsObservationFrame>.Unavailable("physics_observation_unavailable");
    public CaptureSection<PoseObservationFrame> Pose { get; init; } = CaptureSection<PoseObservationFrame>.Unavailable("pose_observation_unavailable");
    public CaptureSection<GridStateObservationFrame> GridState { get; init; } = CaptureSection<GridStateObservationFrame>.Unavailable("grid_state_unavailable");
    public CaptureSection<GeometryObservationFrame> SoftwareGeometry { get; init; } = CaptureSection<GeometryObservationFrame>.Unavailable("geometry_unavailable");
    public CaptureSection<VisualObservationFrame> VisualFrame { get; init; } = CaptureSection<VisualObservationFrame>.Unavailable("framebuffer_capture_unavailable");
}

public sealed record CaptureSection<T>(string Status, string? Reason, T? Value) where T : class
{
    public static CaptureSection<T> Unavailable(string reason) => new("unavailable", reason, null);
    public static CaptureSection<T> Observed(T value) => new("observed", null, value);
    public static CaptureSection<T> Partial(T value, string reason) => new("partial", reason, value);
}

// These channels are deliberately distinct; the current runtime does not expose human input.
public sealed record HumanInputFrame(double? Throttle, double? Steering, double? Brake, string Source);
public sealed record ActuatorObservationFrame(IReadOnlyList<ActuatorState> Items, string Ownership = "observed_only_no_control");
public sealed record ActuatorState(long EntityId, string Kind, bool? Enabled, bool? OverrideActive,
    double? OverrideValue, bool? BrakeEnabled, bool? ActiveBrake = null, bool? Handbrake = null,
    double? SteeringOverride = null, double? PropulsionOverride = null);
public sealed record PhysicsObservationFrame(double? VelocityX, double? VelocityY, double? VelocityZ,
    double? SpeedMetersPerSecond, double? SimulationRatio, long? PhysicsStepCount);
public sealed record PoseObservationFrame(double? PositionX, double? PositionY, double? PositionZ,
    double? RightX, double? RightY, double? RightZ, double? UpX, double? UpY, double? UpZ,
    double? ForwardX, double? ForwardY, double? ForwardZ);
public sealed record GridStateObservationFrame(bool? IsStatic, string? GridSizeClass, int? BlockCount,
    bool? BlockCoverageComplete);
public sealed record GeometryObservationFrame(string Coverage, int? GridCount, int? BlockCount,
    bool? TerrainIncluded, bool? UnrelatedEntitiesIncluded, string Uncertainty);
public sealed record VisualObservationFrame(string Renderer, string FrameStatus, string? ImageSha256,
    int? Width, int? Height, string? RendererClockDomain);

public sealed record ReplayCaptureRecord(DrivingLabCaptureRecord Record, long MissingSequencesBefore);

public static class DrivingLabCaptureValidator
{
    public const int CurrentSchemaVersion = 1;
    public const int MaximumLineBytes = 1024 * 1024;
    public const int MaximumRecords = 100_000;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static byte[] SerializeLine(DrivingLabCaptureRecord record)
    {
        Validate(record);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(record, JsonOptions);
        if (bytes.Length > MaximumLineBytes)
            throw new InvalidDataException("Capture record exceeds the one MiB line limit.");
        return bytes;
    }

    public static DrivingLabCaptureRecord ParseLine(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Length == 0 || utf8.Length > MaximumLineBytes)
            throw new InvalidDataException("Capture line is empty or exceeds the one MiB limit.");
        using (var document = JsonDocument.Parse(utf8.ToArray()))
            EnsureUniqueObjectKeys(document.RootElement);
        var record = JsonSerializer.Deserialize<DrivingLabCaptureRecord>(utf8, JsonOptions)
            ?? throw new InvalidDataException("Capture line contains no record.");
        Validate(record);
        return record;
    }

    private static void EnsureUniqueObjectKeys(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new InvalidDataException($"Capture JSON contains duplicate object key '{property.Name}'.");
                EnsureUniqueObjectKeys(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) EnsureUniqueObjectKeys(item);
        }
    }

    public static void Validate(DrivingLabCaptureRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.SchemaVersion != CurrentSchemaVersion || !Identity(record.RunId) || !Identity(record.SessionId) ||
            !Identity(record.StreamId) || string.IsNullOrWhiteSpace(record.Source) || record.Source.Length > 80 ||
            record.Sequence <= 0 || record.CapturedAtUtc == default || record.CapturedAtUtc.Offset != TimeSpan.Zero ||
            string.IsNullOrWhiteSpace(record.ClockDomain) || record.ClockDomain.Length > 80 ||
            (record.SimulationTick is < 0) || (record.SimulationTimeSeconds is { } t && (!double.IsFinite(t) || t < 0)) ||
            (record.GridEntityId is <= 0) || (record.DroppedRecordsTotal is < 0) ||
            (record.ConfigurationSha256 is not null && !Sha256(record.ConfigurationSha256)))
            throw new InvalidDataException("Capture identity, time, sequence, or digest is invalid.");
        ValidateSection(record.HumanInput);
        ValidateSection(record.Actuators);
        ValidateSection(record.Physics);
        ValidateSection(record.Pose);
        ValidateSection(record.GridState);
        ValidateSection(record.SoftwareGeometry);
        ValidateSection(record.VisualFrame);
        if (record.Actuators.Status != "unavailable" &&
            record.Actuators.Value?.Ownership != "observed_only_no_control")
            throw new InvalidDataException("Actuator records must be explicitly observation-only.");
        if (record.Actuators.Value?.Items.Any(item => item.EntityId <= 0 || string.IsNullOrWhiteSpace(item.Kind) ||
                item.Kind.Length > 80 || item.OverrideValue is { } overrideValue && !double.IsFinite(overrideValue) ||
                item.SteeringOverride is { } steering && !double.IsFinite(steering) ||
                item.PropulsionOverride is { } propulsion && !double.IsFinite(propulsion)) == true)
            throw new InvalidDataException("Actuator observations require exact positive entity IDs and kinds.");
        if (record.Physics.Value is { } physics)
        {
            if (new[] { physics.VelocityX, physics.VelocityY, physics.VelocityZ, physics.SpeedMetersPerSecond, physics.SimulationRatio }
                    .Any(value => value is { } number && (!double.IsFinite(number) || number < 0 &&
                        (value == physics.SpeedMetersPerSecond || value == physics.SimulationRatio))) ||
                physics.PhysicsStepCount is < 0)
                throw new InvalidDataException("Physics observations must contain finite values and nonnegative speed/ratio.");
        }
        if (record.Pose.Value is { } pose &&
            new[] { pose.PositionX, pose.PositionY, pose.PositionZ, pose.RightX, pose.RightY, pose.RightZ,
                pose.UpX, pose.UpY, pose.UpZ, pose.ForwardX, pose.ForwardY, pose.ForwardZ }
                .Any(value => value is { } number && !double.IsFinite(number)))
            throw new InvalidDataException("Pose observations must contain finite values.");
        if (record.GridState.Value is { } grid && (grid.BlockCount is < 0 ||
            (grid.GridSizeClass is not null && grid.GridSizeClass.Length > 40)))
            throw new InvalidDataException("Grid state observation is outside its bounds.");
    }

    private static void ValidateSection<T>(CaptureSection<T> section) where T : class
    {
        if (section is null || section.Status is not ("observed" or "partial" or "unavailable") ||
            (section.Status == "unavailable" && (section.Value is not null || string.IsNullOrWhiteSpace(section.Reason))) ||
            (section.Status == "observed" && (section.Value is null || section.Reason is not null)) ||
            (section.Status == "partial" && (section.Value is null || string.IsNullOrWhiteSpace(section.Reason))))
            throw new InvalidDataException("Capture section status and value disagree.");
    }

    private static bool Identity(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128 &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
    private static bool Sha256(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
    internal static JsonSerializerOptions SerializerOptions => JsonOptions;
}

/// <summary>Bounded JSONL reader. It validates each record and exposes sequence gaps without interpolating data.</summary>
public static class DrivingLabCaptureReplayReader
{
    public const int MaximumReplayBytes = 64 * 1024 * 1024;

    public static IReadOnlyList<ReplayCaptureRecord> Read(Stream input, int maximumRecords = DrivingLabCaptureValidator.MaximumRecords)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.CanRead || maximumRecords is < 1 or > DrivingLabCaptureValidator.MaximumRecords)
            throw new ArgumentOutOfRangeException(nameof(maximumRecords));
        var result = new List<ReplayCaptureRecord>();
        var lastSequence = new Dictionary<(string Run, string Session, string Stream, long? Grid), long>();
        var lastDropped = new Dictionary<(string Run, string Session, string Stream, long? Grid), long>();
        var lastSimulationTick = new Dictionary<(string Run, string Session, string Stream, long? Grid), long>();
        var lastSimulationTime = new Dictionary<(string Run, string Session, string Stream, long? Grid), double>();
        var totalBytes = 0;
        while (ReadBoundedLine(input) is { } bytes)
        {
            totalBytes = checked(totalBytes + bytes.Length + 1);
            if (totalBytes > MaximumReplayBytes) throw new InvalidDataException("Capture exceeds the 64 MiB replay limit.");
            if (bytes.Length == 0) continue;
            if (result.Count >= maximumRecords) throw new InvalidDataException("Capture exceeds the record limit.");
            var record = DrivingLabCaptureValidator.ParseLine(bytes);
            var key = (record.RunId, record.SessionId, record.StreamId, record.GridEntityId);
            lastSequence.TryGetValue(key, out var previous);
            if (record.Sequence <= previous) throw new InvalidDataException("Capture sequence is duplicate or moves backwards.");
            if (record.SimulationTick is { } tick && lastSimulationTick.TryGetValue(key, out var previousTick) && tick < previousTick)
                throw new InvalidDataException("Simulation tick moves backwards within a capture stream.");
            if (record.SimulationTimeSeconds is { } simulationTime &&
                lastSimulationTime.TryGetValue(key, out var previousTime) && simulationTime < previousTime)
                throw new InvalidDataException("Simulation time moves backwards within a capture stream.");
            if (record.DroppedRecordsTotal is { } dropped && lastDropped.TryGetValue(key, out var priorDropped) && dropped < priorDropped)
                throw new InvalidDataException("Cumulative dropped-record count moves backwards.");
            result.Add(new ReplayCaptureRecord(record, Math.Max(0, record.Sequence - previous - 1)));
            lastSequence[key] = record.Sequence;
            if (record.SimulationTick is { } acceptedTick) lastSimulationTick[key] = acceptedTick;
            if (record.SimulationTimeSeconds is { } acceptedTime) lastSimulationTime[key] = acceptedTime;
            if (record.DroppedRecordsTotal is { } count) lastDropped[key] = count;
        }
        return result;
    }

    private static byte[]? ReadBoundedLine(Stream input)
    {
        using var line = new MemoryStream();
        while (true)
        {
            var next = input.ReadByte();
            if (next < 0) return line.Length == 0 ? null : line.ToArray();
            if (next == '\n') return line.ToArray();
            if (line.Length >= DrivingLabCaptureValidator.MaximumLineBytes)
                throw new InvalidDataException("Capture line exceeds the one MiB limit.");
            line.WriteByte((byte)next);
        }
    }
}
