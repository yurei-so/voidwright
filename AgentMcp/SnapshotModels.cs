namespace Voidwright.AgentMcp;

public sealed class SnapshotEnvelope
{
    public int SchemaVersion { get; set; }
    public string? CapturedAtUtc { get; set; }
    public long CapturedSimulationTick { get; set; }
    public string? RunId { get; set; }
    public PlayerCharacterObservationSnapshot? PlayerObservation { get; set; }
    public bool AgentsTruncated { get; set; }
    public List<AgentSnapshot> Agents { get; set; } = [];
}

public sealed class PlayerCharacterObservationSnapshot
{
    public int SchemaVersion { get; set; }
    public long CapturedSimulationTick { get; set; }
    public int ConnectedHumanPlayerCount { get; set; }
    public string? Status { get; set; }
    public bool CharacterObserved { get; set; }
    public long CharacterEntityId { get; set; }
    public double[] Position { get; set; } = [];
    public double[] Right { get; set; } = [];
    public double[] Up { get; set; } = [];
    public double[] Forward { get; set; } = [];
}

public sealed class AgentSnapshot
{
    public int SchemaVersion { get; set; }
    public string? IdentityScope { get; set; }
    public string? AgentId { get; set; }
    public long CapturedSimulationTick { get; set; }
    public long CoreEntityId { get; set; }
    public string? CoreName { get; set; }
    public long RootGridEntityId { get; set; }
    public string? RootGridName { get; set; }
    public double[] CorePosition { get; set; } = [];
    public double[] CoreRight { get; set; } = [];
    public double[] CoreUp { get; set; } = [];
    public double[] CoreForward { get; set; } = [];
    public int PhysicalGridCount { get; set; }
    public int BlocksObserved { get; set; }
    public bool GridCoverageComplete { get; set; }
    public bool BlockCoverageComplete { get; set; }
    public string? StructureFingerprint { get; set; }
    public List<GridSnapshot> PhysicalGrids { get; set; } = [];
}

public sealed class GridSnapshot
{
    public long EntityId { get; set; }
    public string? Name { get; set; }
    public string? GridSizeClass { get; set; }
    public double GridSizeMeters { get; set; }
    public bool IsStatic { get; set; }
    public double[] Position { get; set; } = [];
    public double[] Right { get; set; } = [];
    public double[] Up { get; set; } = [];
    public double[] Forward { get; set; } = [];
    public double[] LinearVelocity { get; set; } = [];
    public double[] NaturalGravity { get; set; } = [];
    public int BlockCount { get; set; }
    public List<BlockSnapshot> Blocks { get; set; } = [];
}

public sealed class BlockSnapshot
{
    public long EntityId { get; set; }
    public string? DefinitionType { get; set; }
    public string? SubtypeId { get; set; }
    public string? Name { get; set; }
    public int[] Position { get; set; } = [];
    public int[] Min { get; set; } = [];
    public int[] Max { get; set; } = [];
    public string? Forward { get; set; }
    public string? Up { get; set; }
    public float BuildLevelRatio { get; set; }
    public float DamageRatio { get; set; }
    public bool HasEnabledState { get; set; }
    public bool Enabled { get; set; }
    public bool IsFunctional { get; set; }
    public bool IsWorking { get; set; }
    public double[] WorldCenter { get; set; } = [];
}

public sealed class SnapshotStatus
{
    public bool Configured { get; init; }
    public bool Exists { get; init; }
    public bool Readable { get; init; }
    public bool Fresh { get; init; }
    public string? CapturedAtUtc { get; init; }
    public long? AgeSeconds { get; init; }
    public long? Bytes { get; init; }
    public int? AgentCount { get; init; }
    public string? Issue { get; init; }
}
