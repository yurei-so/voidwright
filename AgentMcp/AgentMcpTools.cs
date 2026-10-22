using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Voidwright.Core;

namespace Voidwright.AgentMcp;

[McpServerToolType]
public sealed class AgentMcpTools(SnapshotFileReader snapshots)
{
    private const int MaximumPageSize = 200;
    private const int MaximumWireframeCharacters = 500_000;
    private static readonly JsonSerializerOptions OutputJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    [McpServerTool(Name = "voidwright_snapshot_status", ReadOnly = true), Description("Reports whether the explicitly configured local Voidwright world snapshot is available and how fresh it is. Read-only; never reveals the configured filesystem path.")]
    public string VoidwrightSnapshotStatus()
        => JsonSerializer.Serialize(snapshots.GetStatus(), OutputJson);

    [McpServerTool(Name = "list_voidwright_agents", ReadOnly = true), Description("Lists Agent Cores in the explicitly configured local world snapshot, including captured pose, grid/block counts, completeness, and simulation tick. Read-only; local stdio only.")]
    public CallToolResult ListVoidwrightAgents()
    {
        try
        {
            var snapshot = snapshots.Read();
            var capturedAt = DateTimeOffset.Parse(snapshot.CapturedAtUtc!, System.Globalization.CultureInfo.InvariantCulture);
            var result = new
            {
                schemaVersion = snapshot.SchemaVersion,
                capturedAtUtc = snapshot.CapturedAtUtc,
                ageSeconds = (long)Math.Max(0, (DateTimeOffset.UtcNow - capturedAt).TotalSeconds),
                fresh = IsFresh(capturedAt),
                snapshot.AgentsTruncated,
                agents = snapshot.Agents.Select(agent => new
                {
                    agent.AgentId,
                    agent.IdentityScope,
                    agent.CoreEntityId,
                    agent.CoreName,
                    agent.RootGridEntityId,
                    agent.RootGridName,
                    agent.CapturedSimulationTick,
                    agent.CorePosition,
                    agent.CoreRight,
                    agent.CoreUp,
                    agent.CoreForward,
                    agent.PhysicalGridCount,
                    listedPhysicalGrids = agent.PhysicalGrids.Count,
                    agent.BlocksObserved,
                    listedBlocks = agent.PhysicalGrids.Sum(grid => grid.Blocks.Count),
                    complete = agent.GridCoverageComplete && agent.BlockCoverageComplete,
                    agent.GridCoverageComplete,
                    agent.BlockCoverageComplete,
                    agent.StructureFingerprint
                })
            };
            return TextResult(JsonSerializer.Serialize(result, OutputJson));
        }
        catch (Exception exception) when (IsExpectedSnapshotError(exception))
        {
            return TextResult(Error(exception.Message), isError: true);
        }
    }

    [McpServerTool(Name = "get_voidwright_character_observation", ReadOnly = true), Description("Returns a fresh, anonymous server-side pose for the sole connected non-bot player's character in the configured disposable world. No Steam/account ID or display name is exported; when multiple human players are connected, positions are withheld as ambiguous.")]
    public CallToolResult GetVoidwrightCharacterObservation(
        [Description("Optional run ID from the latest snapshot; rejects a different server run.")] string? expectedRunId = null,
        [Description("Optional simulation tick from the latest snapshot; rejects a different observation tick.")] long? expectedSimulationTick = null)
    {
        try
        {
            var snapshot = snapshots.Read();
            var capturedAt = DateTimeOffset.Parse(snapshot.CapturedAtUtc!, System.Globalization.CultureInfo.InvariantCulture);
            if (!IsFresh(capturedAt))
                return TextResult(Error("Snapshot is stale; refusing to report a current player location."), isError: true);
            if (!string.IsNullOrWhiteSpace(expectedRunId) && !string.Equals(expectedRunId, snapshot.RunId, StringComparison.Ordinal))
                return TextResult(Error("Snapshot run ID does not match expectedRunId; refresh the world context."), isError: true);
            if (expectedSimulationTick.HasValue && expectedSimulationTick.Value != snapshot.CapturedSimulationTick)
                return TextResult(Error("Snapshot tick does not match expectedSimulationTick; refresh the observation."), isError: true);
            if (snapshot.PlayerObservation is null)
                return TextResult(Error("This Voidwright exporter does not include player-character observations; update the mod in this disposable world and restart it."), isError: true);

            var observation = snapshot.PlayerObservation;
            var result = new
            {
                schemaVersion = observation.SchemaVersion,
                capturedAtUtc = snapshot.CapturedAtUtc,
                ageSeconds = (long)Math.Max(0, (DateTimeOffset.UtcNow - capturedAt).TotalSeconds),
                fresh = true,
                worldScopeId = snapshots.WorldScopeId,
                runId = snapshot.RunId,
                snapshotSimulationTick = snapshot.CapturedSimulationTick,
                observation.CapturedSimulationTick,
                observation.Status,
                observation.ConnectedHumanPlayerCount,
                observation.CharacterObserved,
                characterEntityId = observation.CharacterObserved ? observation.CharacterEntityId : (long?)null,
                position = observation.CharacterObserved ? observation.Position : null,
                right = observation.CharacterObserved ? observation.Right : null,
                up = observation.CharacterObserved ? observation.Up : null,
                forward = observation.CharacterObserved ? observation.Forward : null,
                source = "server-side IMyPlayer.Character pose; world-space meters; no player account identifiers"
            };
            return TextResult(JsonSerializer.Serialize(result, OutputJson));
        }
        catch (Exception exception) when (IsExpectedSnapshotError(exception))
        {
            return TextResult(Error(exception.Message), isError: true);
        }
    }

    [McpServerTool(Name = "query_voidwright_body", ReadOnly = true), Description("Queries one enrolled grid body as a bounded page of block data, optionally attaching a three-profile SVG structural or damage map. Damage colors use only current exported DamageRatio/IsFunctional values; incomplete coverage remains unknown and no destruction is inferred. Selectors add caller-chosen opaque #RRGGBB highlights. Read-only; no movement or control tools are exposed.")]
    public CallToolResult QueryVoidwrightBody(
        [Description("Exact agentId returned by list_voidwright_agents, such as voidwright-core:12345.")] string agentId,
        [Description("Zero-based offset into the stable grid/block listing.")] int offset = 0,
        [Description("Number of block rows to return, from 1 to 200.")] int limit = 100,
        [Description("When true, attach an SVG with top, side, and front orthographic profiles. Large diagrams above 500 KB are declined safely.")] bool includeWireframe = false,
        [Description("Optional ordered color selectors. The first matching selector wins. Each needs at least one identity field and an opaque #RRGGBB color.")] WireframeSelectionInput[]? selections = null,
        [Description("Wireframe mode: structural (default) or damage. Damage uses current observed integrity only.")] string visualizationMode = "structural")
    {
        try
        {
            if (string.IsNullOrWhiteSpace(agentId) || agentId.Length > 128)
                return TextResult(Error("agentId must be an exact non-empty identity of at most 128 characters."), isError: true);
            if (offset < 0 || limit < 1 || limit > MaximumPageSize)
                return TextResult(Error($"offset must be nonnegative and limit must be 1..{MaximumPageSize}."), isError: true);
            if (!string.Equals(visualizationMode, "structural", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(visualizationMode, "damage", StringComparison.OrdinalIgnoreCase))
                return TextResult(Error("visualizationMode must be 'structural' or 'damage'."), isError: true);

            var envelope = snapshots.Read();
            var agent = envelope.Agents.SingleOrDefault(item => item.AgentId == agentId);
            if (agent is null)
                return TextResult(Error("No Agent Core with that exact identity exists in the configured snapshot."), isError: true);

            var blocks = agent.PhysicalGrids
                .SelectMany(grid => grid.Blocks.Select(block => new { Grid = grid, Block = block }))
                .OrderBy(item => item.Grid.EntityId)
                .ThenBy(item => item.Block.Position[0])
                .ThenBy(item => item.Block.Position[1])
                .ThenBy(item => item.Block.Position[2])
                .ThenBy(item => item.Block.EntityId)
                .ToList();
            var integrityCoverageComplete = agent.GridCoverageComplete && agent.BlockCoverageComplete &&
                agent.PhysicalGrids.Count == agent.PhysicalGridCount &&
                agent.PhysicalGrids.All(grid => grid.BlockCount == grid.Blocks.Count);
            var page = blocks.Skip(offset).Take(limit).Select(item => new
            {
                gridEntityId = item.Grid.EntityId,
                gridName = item.Grid.Name,
                gridPosition = item.Grid.Position,
                gridRight = item.Grid.Right,
                gridUp = item.Grid.Up,
                gridForward = item.Grid.Forward,
                gridSizeMeters = item.Grid.GridSizeMeters,
                blockEntityId = item.Block.EntityId,
                item.Block.DefinitionType,
                item.Block.SubtypeId,
                item.Block.Name,
                item.Block.Position,
                item.Block.Min,
                item.Block.Max,
                item.Block.Forward,
                item.Block.Up,
                item.Block.WorldCenter,
                item.Block.BuildLevelRatio,
                item.Block.DamageRatio,
                item.Block.HasEnabledState,
                item.Block.Enabled,
                item.Block.IsFunctional,
                item.Block.IsWorking
            }).ToArray();

            AgentWireframeRenderResult? render = null;
            if (includeWireframe)
            {
                var selectionsForRenderer = (selections ?? []).Select(selection => new AgentWireframeSelection
                {
                    GridEntityId = selection.GridEntityId,
                    BlockEntityId = selection.BlockEntityId,
                    DefinitionType = selection.DefinitionType,
                    SubtypeId = selection.SubtypeId,
                    Color = selection.Color
                }).ToArray();
                var renderMode = string.Equals(visualizationMode, "damage", StringComparison.OrdinalIgnoreCase)
                    ? AgentWireframeVisualizationMode.Damage
                    : AgentWireframeVisualizationMode.Structural;
                render = AgentWireframeRenderer.Render(AgentWireframeMapper.Create(agent), selectionsForRenderer, renderMode);
                if (render.Svg.Length > MaximumWireframeCharacters)
                    return TextResult(Error($"Wireframe is {render.Svg.Length} characters, above the {MaximumWireframeCharacters} character MCP output guard; no diagram was returned."), isError: true);
            }

            var body = new
            {
                schemaVersion = agent.SchemaVersion,
                agent.AgentId,
                agent.IdentityScope,
                agent.CapturedSimulationTick,
                capturedAtUtc = envelope.CapturedAtUtc,
                stale = !IsFresh(DateTimeOffset.Parse(envelope.CapturedAtUtc!, System.Globalization.CultureInfo.InvariantCulture)),
                agent.CoreEntityId,
                agent.CoreName,
                agent.RootGridEntityId,
                agent.RootGridName,
                agent.CorePosition,
                agent.CoreRight,
                agent.CoreUp,
                agent.CoreForward,
                agent.PhysicalGridCount,
                agent.BlocksObserved,
                listedPhysicalGrids = agent.PhysicalGrids.Count,
                agent.GridCoverageComplete,
                agent.BlockCoverageComplete,
                complete = agent.GridCoverageComplete && agent.BlockCoverageComplete,
                agent.StructureFingerprint,
                integritySummary = new
                {
                    observedBlockCount = blocks.Count,
                    knownDamagedBlockCount = blocks.Count(item => item.Block.DamageRatio > 0),
                    knownNonFunctionalBlockCount = blocks.Count(item => !item.Block.IsFunctional),
                    coverageComplete = integrityCoverageComplete,
                    unknownState = integrityCoverageComplete
                        ? "No block records were omitted; empty cells still do not prove that a block was previously destroyed."
                        : "Grid or block coverage is incomplete; omitted block state is unknown.",
                    destroyedOrMissingBlockCount = (int?)null,
                    destroyedOrMissingBasis = "Not inferred: this snapshot has no reliable historical block comparison."
                },
                physicalGrids = agent.PhysicalGrids.Select(grid => new
                {
                    grid.EntityId,
                    grid.Name,
                    grid.GridSizeClass,
                    grid.GridSizeMeters,
                    grid.IsStatic,
                    grid.Position,
                    grid.Right,
                    grid.Up,
                    grid.Forward,
                    grid.LinearVelocity,
                    grid.NaturalGravity,
                    grid.BlockCount,
                    listedBlocks = grid.Blocks.Count
                }),
                blockOffset = offset,
                blockLimit = limit,
                totalListedBlocks = blocks.Count,
                nextOffset = offset + page.Length < blocks.Count ? offset + page.Length : (int?)null,
                hasMore = offset + page.Length < blocks.Count,
                blocks = page,
                wireframe = render is null ? null : new
                {
                    mimeType = "image/svg+xml",
                    render.VisualizationMode,
                    render.RenderedBlocks,
                    render.SelectedBlocks,
                    render.HealthyBlocks,
                    render.DamagedBlocks,
                    render.CriticalBlocks,
                    render.UnknownIntegrityBlocks,
                    render.Truncated
                }
            };

            var content = new List<ContentBlock> { new TextContentBlock { Text = JsonSerializer.Serialize(body, OutputJson) } };
            if (render is not null)
            {
                content.Add(new EmbeddedResourceBlock
                {
                    Resource = new TextResourceContents
                    {
                        Uri = $"voidwright://agent/{Uri.EscapeDataString(agent.AgentId!)}/wireframe/{agent.CapturedSimulationTick}",
                        MimeType = "image/svg+xml",
                        Text = render.Svg
                    }
                });
            }
            return new CallToolResult { Content = content, IsError = false };
        }
        catch (Exception exception) when (IsExpectedSnapshotError(exception) || exception is ArgumentException or InvalidOperationException)
        {
            return TextResult(Error(exception.Message), isError: true);
        }
    }

    private static bool IsExpectedSnapshotError(Exception exception)
        => exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or JsonException;

    private static string Error(string message)
        => JsonSerializer.Serialize(new { error = message }, OutputJson);

    private static CallToolResult TextResult(string text, bool isError = false)
        => new() { Content = [new TextContentBlock { Text = text }], IsError = isError };

    private static bool IsFresh(DateTimeOffset capturedAt)
    {
        var age = DateTimeOffset.UtcNow - capturedAt;
        return age >= TimeSpan.Zero && age <= SnapshotFileReader.FreshnessWindow;
    }
}

public sealed class WireframeSelectionInput
{
    [Description("Optional exact physical grid entity ID; 0 is wildcard.")]
    public long GridEntityId { get; set; }

    [Description("Optional exact block entity ID; 0 is wildcard.")]
    public long BlockEntityId { get; set; }

    [Description("Optional block definition type selector.")]
    public string? DefinitionType { get; set; }

    [Description("Optional block subtype selector.")]
    public string? SubtypeId { get; set; }

    [Description("Required opaque RGB stroke/fill color in #RRGGBB form.")]
    public string? Color { get; set; }
}
