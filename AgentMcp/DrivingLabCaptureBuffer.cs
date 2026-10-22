namespace Voidwright.AgentMcp;

public enum DrivingLabCaptureAppendRejection
{
    None,
    IdentityMismatch,
    StaleRecord,
    TimestampOrder,
    SimulationOrder,
    RateLimit,
    SequenceOrder,
    RecordLimit,
    ByteLimit,
    InvalidRecord
}

/// <summary>In-memory JSONL buffer bounded by one explicit passive-grid selection.</summary>
public sealed class DrivingLabCaptureBuffer
{
    private readonly BoundDrivingLabPassiveSelection selection;
    private readonly MemoryStream buffer = new();
    private DateTimeOffset? lastCapturedAtUtc;
    private long lastSequence;
    private long? lastSimulationTick;
    private double? lastSimulationTimeSeconds;
    private int recordCount;

    public DrivingLabCaptureBuffer(BoundDrivingLabPassiveSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.SessionId is not { Length: 32 } || selection.RunId is not { Length: 32 } ||
            selection.SessionId.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')) ||
            selection.RunId.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')) ||
            selection.GridEntityId <= 0 || selection.ExpiresAtUtc.Offset != TimeSpan.Zero ||
            selection.MaximumSampleRateHz is < 1 or > DrivingLabPassiveSelectionContract.MaximumSampleRateHz ||
            selection.MaximumBufferedRecords is < 1 or > DrivingLabPassiveSelectionContract.MaximumBufferedRecords ||
            selection.MaximumBufferedBytes is < 1024 or > DrivingLabPassiveSelectionContract.MaximumBufferedBytes ||
            !string.Equals(selection.PermissionScope, "read_only_observation", StringComparison.Ordinal))
            throw new ArgumentException("Selection is not a valid bounded passive observation scope.", nameof(selection));
        this.selection = selection;
    }

    public int RecordCount => recordCount;
    public int BufferedBytes => checked((int)buffer.Length);

    public DrivingLabCaptureAppendRejection TryAppend(
        DrivingLabCaptureRecord record,
        DateTimeOffset receivedAtUtc)
    {
        if (!string.Equals(record.RunId, selection.RunId, StringComparison.Ordinal) ||
            !string.Equals(record.SessionId, selection.SessionId, StringComparison.Ordinal) ||
            record.GridEntityId != selection.GridEntityId)
            return DrivingLabCaptureAppendRejection.IdentityMismatch;
        var received = receivedAtUtc.ToUniversalTime();
        if (record.CapturedAtUtc > received || received - record.CapturedAtUtc > DrivingLabPassiveSelectionContract.MaximumInventoryAge)
            return DrivingLabCaptureAppendRejection.StaleRecord;
        if (record.CapturedAtUtc >= selection.ExpiresAtUtc)
            return DrivingLabCaptureAppendRejection.StaleRecord;
        if (lastCapturedAtUtc is { } previous)
        {
            if (record.CapturedAtUtc <= previous) return DrivingLabCaptureAppendRejection.TimestampOrder;
            var minimumSpacingTicks = (TimeSpan.TicksPerSecond + selection.MaximumSampleRateHz - 1) /
                selection.MaximumSampleRateHz;
            if (record.CapturedAtUtc.UtcTicks - previous.UtcTicks < minimumSpacingTicks)
                return DrivingLabCaptureAppendRejection.RateLimit;
        }
        if (record.Sequence <= lastSequence) return DrivingLabCaptureAppendRejection.SequenceOrder;
        if (record.SimulationTick is { } tick && lastSimulationTick is { } previousTick && tick < previousTick)
            return DrivingLabCaptureAppendRejection.SimulationOrder;
        if (record.SimulationTimeSeconds is { } simulationTime && lastSimulationTimeSeconds is { } previousTime &&
            simulationTime < previousTime)
            return DrivingLabCaptureAppendRejection.SimulationOrder;
        if (recordCount >= selection.MaximumBufferedRecords) return DrivingLabCaptureAppendRejection.RecordLimit;

        byte[] line;
        try { line = DrivingLabCaptureValidator.SerializeLine(record); }
        catch (InvalidDataException) { return DrivingLabCaptureAppendRejection.InvalidRecord; }
        var requiredBytes = checked(line.Length + 1L);
        if (buffer.Length + requiredBytes > selection.MaximumBufferedBytes)
            return DrivingLabCaptureAppendRejection.ByteLimit;
        buffer.Write(line);
        buffer.WriteByte((byte)'\n');
        lastCapturedAtUtc = record.CapturedAtUtc;
        lastSequence = record.Sequence;
        if (record.SimulationTick is { } acceptedTick) lastSimulationTick = acceptedTick;
        if (record.SimulationTimeSeconds is { } acceptedTime) lastSimulationTimeSeconds = acceptedTime;
        recordCount++;
        return DrivingLabCaptureAppendRejection.None;
    }

    public byte[] ToArray() => buffer.ToArray();
}
