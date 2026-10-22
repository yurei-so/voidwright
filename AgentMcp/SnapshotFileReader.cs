using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace Voidwright.AgentMcp;

public sealed class SnapshotFileReader
{
    public const string PathEnvironmentVariable = "VOIDWRIGHT_AGENT_SNAPSHOT_PATH";
    public const int SchemaVersion = 1;
    public const long MaximumFileBytes = 16L * 1024 * 1024;
    public const int MaximumAgents = 8;
    public const int MaximumGridsPerAgent = 64;
    public const int MaximumBlocksPerAgent = 4096;
    public const int MaximumBlocksTotal = MaximumAgents * MaximumBlocksPerAgent;
    public static readonly TimeSpan FreshnessWindow = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = 64
    };

    private readonly string? configuredPath;
    internal string? ConfiguredPath => configuredPath;
    private readonly string? configurationIssue;
    public string? WorldScopeId => configuredPath is null ? null :
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(configuredPath))).ToLowerInvariant();

    public SnapshotFileReader()
    {
        var path = Environment.GetEnvironmentVariable(PathEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(path)) return;
        if (!Path.IsPathFullyQualified(path))
        {
            configurationIssue = $"{PathEnvironmentVariable} must be an absolute path to the exact snapshot file.";
            return;
        }
        try { configuredPath = Path.GetFullPath(path); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            configurationIssue = $"{PathEnvironmentVariable} is not a valid local file path ({exception.GetType().Name}).";
        }
    }

    public SnapshotEnvelope Read()
    {
        if (configuredPath is null)
        {
            if (configurationIssue is not null) throw new InvalidOperationException(configurationIssue);
            throw new InvalidOperationException(
                $"Set {PathEnvironmentVariable} to the exact world-scoped snapshot file path.");
        }
        if (!File.Exists(configuredPath))
            throw new FileNotFoundException("The configured Voidwright snapshot file does not exist yet.");

        byte[] bytes;
        try
        {
            using var stream = new FileStream(
                configuredPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, bufferSize: 64 * 1024,
                FileOptions.SequentialScan);
            if (stream.Length <= 0 || stream.Length > MaximumFileBytes)
                throw new InvalidDataException($"Snapshot size must be between 1 byte and {MaximumFileBytes} bytes.");

            bytes = new byte[checked((int)stream.Length)];
            var read = 0;
            while (read < bytes.Length)
            {
                var amount = stream.Read(bytes, read, bytes.Length - read);
                if (amount == 0) throw new InvalidDataException("Snapshot changed while being read; retry the query.");
                read += amount;
            }
            if (stream.ReadByte() != -1)
                throw new InvalidDataException("Snapshot grew beyond the read limit; retry the query.");
        }
        catch (UnauthorizedAccessException)
        {
            throw new UnauthorizedAccessException("The configured snapshot file cannot be read due to local permissions.");
        }
        catch (IOException)
        {
            throw new IOException("The configured snapshot could not be read; retry if the game is refreshing it.");
        }

        SnapshotEnvelope? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<SnapshotEnvelope>(bytes, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Snapshot is incomplete or invalid JSON; it may be refreshing, so retry.", exception);
        }
        if (snapshot is null) throw new InvalidDataException("Snapshot is empty.");
        Validate(snapshot);
        return snapshot;
    }

    public SnapshotStatus GetStatus()
    {
        if (configuredPath is null)
            return new SnapshotStatus
            {
                Configured = false,
                Issue = configurationIssue ?? $"{PathEnvironmentVariable} is not set."
            };

        var exists = File.Exists(configuredPath);
        if (!exists)
            return new SnapshotStatus { Configured = true, Exists = false, Issue = "Configured snapshot file is not present yet." };

        long bytes;
        try { bytes = new FileInfo(configuredPath).Length; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new SnapshotStatus { Configured = true, Exists = true, Issue = "Snapshot file cannot be inspected: " + exception.GetType().Name };
        }
        if (bytes <= 0 || bytes > MaximumFileBytes)
        {
            return new SnapshotStatus
            {
                Configured = true, Exists = true, Bytes = bytes,
                Issue = $"Snapshot size is outside the 1..{MaximumFileBytes} byte limit."
            };
        }

        try
        {
            var snapshot = Read();
            var captured = ParseTimestamp(snapshot.CapturedAtUtc);
            var age = DateTimeOffset.UtcNow - captured;
            return new SnapshotStatus
            {
                Configured = true,
                Exists = true,
                Readable = true,
                Fresh = age >= TimeSpan.Zero && age <= FreshnessWindow,
                CapturedAtUtc = snapshot.CapturedAtUtc,
                AgeSeconds = (long)Math.Max(0, age.TotalSeconds),
                Bytes = bytes,
                AgentCount = snapshot.Agents.Count,
                Issue = age < TimeSpan.Zero
                    ? "Snapshot timestamp is ahead of the local clock; freshness cannot be confirmed."
                    : age > FreshnessWindow ? "Snapshot is stale; it remains a valid last-known observation." : null
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            return new SnapshotStatus
            {
                Configured = true, Exists = true, Bytes = bytes,
                Issue = exception.Message
            };
        }
    }

    private static void Validate(SnapshotEnvelope snapshot)
    {
        if (snapshot.SchemaVersion != SchemaVersion)
            throw new InvalidDataException($"Unsupported snapshot schema {snapshot.SchemaVersion}; expected {SchemaVersion}.");
        if (snapshot.RunId is not null && (snapshot.RunId.Length != 32 || !Guid.TryParseExact(snapshot.RunId, "N", out _)))
            throw new InvalidDataException("Snapshot run identity is invalid.");
        _ = ParseTimestamp(snapshot.CapturedAtUtc);
        ValidatePlayerObservation(snapshot);
        if (snapshot.Agents.Count > MaximumAgents)
            throw new InvalidDataException("Snapshot agent count exceeds the reader limit.");

        var totalBlocks = 0;
        var agentIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var agent in snapshot.Agents)
        {
            if (agent.SchemaVersion != SchemaVersion || string.IsNullOrWhiteSpace(agent.AgentId) || agent.AgentId.Length > 128)
                throw new InvalidDataException("Snapshot contains an invalid agent identity or schema.");
            if (!agentIds.Add(agent.AgentId)) throw new InvalidDataException("Snapshot contains duplicate agent identities.");
            ValidateVector(agent.CorePosition, "corePosition");
            ValidateVector(agent.CoreRight, "coreRight");
            ValidateVector(agent.CoreUp, "coreUp");
            ValidateVector(agent.CoreForward, "coreForward");
            if (agent.PhysicalGrids.Count > MaximumGridsPerAgent)
                throw new InvalidDataException("Snapshot grid count exceeds the reader limit.");
            var agentBlockCount = 0;
            var gridIds = new HashSet<long>();
            foreach (var grid in agent.PhysicalGrids)
            {
                if (!gridIds.Add(grid.EntityId)) throw new InvalidDataException("Snapshot contains duplicate grid identities.");
                if (!double.IsFinite(grid.GridSizeMeters) || grid.GridSizeMeters <= 0)
                    throw new InvalidDataException("Snapshot contains invalid grid dimensions.");
                ValidateVector(grid.Position, "grid.position");
                ValidateVector(grid.Right, "grid.right");
                ValidateVector(grid.Up, "grid.up");
                ValidateVector(grid.Forward, "grid.forward");
                ValidateVector(grid.LinearVelocity, "grid.linearVelocity");
                ValidateVector(grid.NaturalGravity, "grid.naturalGravity");
                agentBlockCount += grid.Blocks.Count;
                foreach (var block in grid.Blocks)
                {
                    ValidateVector(block.Position, "block.position");
                    ValidateVector(block.Min, "block.min");
                    ValidateVector(block.Max, "block.max");
                    ValidateVector(block.WorldCenter, "block.worldCenter");
                    if (!float.IsFinite(block.BuildLevelRatio) || !float.IsFinite(block.DamageRatio))
                        throw new InvalidDataException("Snapshot contains invalid block status values.");
                }
            }
            if (agentBlockCount > MaximumBlocksPerAgent)
                throw new InvalidDataException("Snapshot block count per agent exceeds the reader limit.");
            totalBlocks += agentBlockCount;
        }
        if (totalBlocks > MaximumBlocksTotal)
            throw new InvalidDataException("Snapshot total block count exceeds the reader limit.");
    }

    private static void ValidatePlayerObservation(SnapshotEnvelope snapshot)
    {
        var observation = snapshot.PlayerObservation;
        if (observation is null) return; // Older v1 exporters remain readable.
        if (observation.SchemaVersion != 1 || observation.CapturedSimulationTick != snapshot.CapturedSimulationTick)
            throw new InvalidDataException("Player observation schema or simulation tick does not match its snapshot envelope.");
        if (observation.ConnectedHumanPlayerCount < 0 || observation.ConnectedHumanPlayerCount > 256)
            throw new InvalidDataException("Player observation count is outside the supported limit.");

        var status = observation.Status;
        if (status is not ("players_api_unavailable" or "no_connected_humans" or "ambiguous_multiple_humans" or "character_not_spawned" or "observed"))
            throw new InvalidDataException("Player observation has an unsupported status.");

        if (observation.CharacterObserved)
        {
            if (status != "observed" || observation.ConnectedHumanPlayerCount != 1 || observation.CharacterEntityId <= 0)
                throw new InvalidDataException("Observed player pose must refer to exactly one connected human character.");
            ValidateVector(observation.Position, "playerObservation.position");
            ValidateVector(observation.Right, "playerObservation.right");
            ValidateVector(observation.Up, "playerObservation.up");
            ValidateVector(observation.Forward, "playerObservation.forward");
            return;
        }

        if (status == "observed" || observation.CharacterEntityId != 0 ||
            observation.Position.Length != 0 || observation.Right.Length != 0 ||
            observation.Up.Length != 0 || observation.Forward.Length != 0)
            throw new InvalidDataException("Unavailable player observation must not include character pose data.");
        if ((status == "no_connected_humans" && observation.ConnectedHumanPlayerCount != 0) ||
            ((status == "character_not_spawned" || status == "ambiguous_multiple_humans") && observation.ConnectedHumanPlayerCount < 1) ||
            (status == "ambiguous_multiple_humans" && observation.ConnectedHumanPlayerCount < 2))
            throw new InvalidDataException("Player observation status does not match its connected-human count.");
    }

    private static DateTimeOffset ParseTimestamp(string? value)
    {
        if (!DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var timestamp))
            throw new InvalidDataException("Snapshot timestamp is missing or invalid.");
        return timestamp;
    }

    private static void ValidateVector(double[]? value, string field)
    {
        if (value is null || value.Length != 3 || value.Any(item => !double.IsFinite(item)))
            throw new InvalidDataException($"Snapshot vector '{field}' must contain three finite numbers.");
    }

    private static void ValidateVector(int[]? value, string field)
    {
        if (value is null || value.Length != 3)
            throw new InvalidDataException($"Snapshot vector '{field}' must contain three integers.");
    }
}
