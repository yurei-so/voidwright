using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

internal static class VehicleProfileChecks
{
    public static async Task Run(string repositoryRoot, string serverProject, string snapshotPath,
        Dictionary<string, string?> environment, Action<bool, string> check, Action<string> pass)
    {
        var original = File.ReadAllText(snapshotPath);
        var snapshot = JsonNode.Parse(TestFixtures.Snapshot)!.AsObject();
        snapshot["capturedAtUtc"] = DateTimeOffset.UtcNow.ToString("O");
        const string exactGridId = "106313676705820376";
        snapshot["agents"]![0]!["rootGridEntityId"] = long.Parse(exactGridId);
        snapshot["agents"]![0]!["physicalGrids"]![0]!["entityId"] = long.Parse(exactGridId);
        File.WriteAllText(snapshotPath, snapshot.ToJsonString());
        var profileEnvironment = new Dictionary<string, string?>(environment)
        { ["VOIDWRIGHT_TOOL_PROFILE"] = "vehicle", ["VOIDWRIGHT_READ_ONLY_ONLY"] = "true" };
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "Voidwright compact read-only vehicle workflow", Command = "dotnet",
            Arguments = ["run", "--configuration", "Release", "--no-build", "--project", serverProject],
            WorkingDirectory = repositoryRoot, InheritEnvironmentVariables = false, EnvironmentVariables = profileEnvironment
        });
        try
        {
            await using var client = await McpClient.CreateAsync(transport);
            var names = (await client.ListToolsAsync()).Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);
            check(names.SetEquals(["voidwright_snapshot_status", "observe_voidwright_vehicle", "view_voidwright_vehicle", "query_voidwright_body"]),
                "Compact read-only profile must expose exactly four workflow tools and no actuator");
            var context = await client.CallToolAsync("voidwright_snapshot_status", new Dictionary<string, object?>());
            var contextJson = JsonDocument.Parse(context.Content.OfType<TextContentBlock>().Single().Text).RootElement;
            check(contextJson.GetProperty("fresh").GetBoolean() && contextJson.GetProperty("agents")[0].GetProperty("agentId").GetString() == "voidwright-core:42"
                && contextJson.GetProperty("agents")[0].GetProperty("rootGridEntityId").GetString() == exactGridId,
                "Compact context must discover usable exact agent/grid identities without the raw catalog tool");
            async Task<JsonElement> View(string preset, string grid = exactGridId, double distance = 0)
            {
                var result = await client.CallToolAsync("view_voidwright_vehicle", new Dictionary<string, object?>
                { ["agentId"] = "voidwright-core:42", ["gridEntityId"] = grid, ["preset"] = preset,
                  ["distanceMeters"] = distance, ["capture"] = false });
                check(result.IsError != true, "Named view unexpectedly failed: " + result.Content.OfType<TextContentBlock>().First().Text);
                return JsonDocument.Parse(result.Content.OfType<TextContentBlock>().Single().Text).RootElement.Clone();
            }
            var chase = await View("chase");
            var id = chase.GetProperty("perspectiveId").GetString();
            check(chase.GetProperty("gridEntityId").GetString() == exactGridId && chase.GetProperty("distanceMeters").GetDouble() > 4,
                "Automatic framing must use observed block envelopes with exact string identity");
            foreach (var preset in new[] { "front", "left", "right", "top", "chase" })
            {
                var view = await View(preset);
                check(view.GetProperty("perspectiveId").GetString() == id && view.GetProperty("perspectiveReused").GetBoolean(),
                    "Changing named presets must reuse a single rig instead of consuming perspective slots");
                var forward = view.GetProperty("worldForward").EnumerateArray().Select(value => value.GetDouble()).ToArray();
                var up = view.GetProperty("worldUp").EnumerateArray().Select(value => value.GetDouble()).ToArray();
                check(Math.Abs(forward.Zip(up, (a, b) => a * b).Sum()) < 1e-8,
                    "Named view basis must remain orthogonal, including vertical top views");
            }
            var gridNode = snapshot["agents"]![0]!["physicalGrids"]![0]!;
            gridNode["position"]![0] = 103;
            File.WriteAllText(snapshotPath, snapshot.ToJsonString());
            var translated = await View("chase");
            check(Math.Abs(translated.GetProperty("worldPosition")[0].GetDouble() - chase.GetProperty("worldPosition")[0].GetDouble() - 100) < 1e-8,
                "Named view must resolve the current vehicle transform");
            snapshot["runId"] = new string('2', 32);
            File.WriteAllText(snapshotPath, snapshot.ToJsonString());
            var restarted = await View("chase");
            check(restarted.GetProperty("perspectiveId").GetString() != id,
                "A new exporter run must retire the old named rig and create a newly bound one");
            var body = await client.CallToolAsync("query_voidwright_body", new Dictionary<string, object?> { ["agentId"] = "voidwright-core:42" });
            var bodyJson = JsonDocument.Parse(body.Content.OfType<TextContentBlock>().Single().Text).RootElement;
            check(body.IsError != true && bodyJson.GetProperty("rootGridEntityId").ValueKind == JsonValueKind.String
                && bodyJson.GetProperty("blocks")[0].GetProperty("blockEntityId").ValueKind == JsonValueKind.String,
                "Compact profile must serialize nested body entity IDs as strings");
            snapshot["capturedAtUtc"] = DateTimeOffset.UtcNow.ToString("O");
            File.WriteAllText(snapshotPath, snapshot.ToJsonString());
            var renderer = FakeHoloViewRenderer.CompleteOnce(Path.GetDirectoryName(snapshotPath)!);
            var captured = await client.CallToolAsync("view_voidwright_vehicle", new Dictionary<string, object?>
            { ["agentId"] = "voidwright-core:42", ["gridEntityId"] = exactGridId, ["preset"] = "front" });
            await renderer;
            var receipt = JsonDocument.Parse(captured.Content.OfType<TextContentBlock>().Last().Text).RootElement.GetProperty("receipt");
            check(captured.IsError != true && captured.Content.OfType<ImageContentBlock>().Single().MimeType == "image/png"
                && receipt.GetProperty("targetGridId").GetString() == exactGridId
                && receipt.GetProperty("sourceGridIds")[0].GetString() == exactGridId
                && !receipt.GetProperty("simultaneous").GetBoolean(),
                "Named view must return the image, keep exact receipt IDs and preserve separate clocks");
            pass("capture through a named view with a fake renderer and preserve image/provenance through the compact profile");
            foreach (var parameters in new[] {
                new Dictionary<string, object?> { ["agentId"] = "voidwright-core:42", ["gridEntityId"] = "11" },
                new Dictionary<string, object?> { ["agentId"] = "voidwright-core:42", ["gridEntityId"] = exactGridId, ["distanceMeters"] = 1.0 },
                new Dictionary<string, object?> { ["agentId"] = "voidwright-core:42", ["gridEntityId"] = exactGridId, ["preset"] = "teleport" } })
            {
                parameters["capture"] = false;
                var rejected = await client.CallToolAsync("view_voidwright_vehicle", parameters);
                check(rejected.IsError == true, "Named views must refuse wrong-grid, unsafe-distance and unknown-preset requests");
            }
            snapshot["capturedAtUtc"] = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O");
            File.WriteAllText(snapshotPath, snapshot.ToJsonString());
            var stale = await client.CallToolAsync("view_voidwright_vehicle", new Dictionary<string, object?>
            { ["agentId"] = "voidwright-core:42", ["gridEntityId"] = exactGridId, ["capture"] = false });
            check(stale.IsError == true, "Named views must refuse stale data instead of silently configuring a current camera");
            pass("compact profile frames and reuses named vehicle views with exact IDs, moving-grid and run/freshness guards");

            profileEnvironment["VOIDWRIGHT_READ_ONLY_ONLY"] = "false";
            var controlTransport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "Voidwright compact lab workflow catalog", Command = "dotnet",
                Arguments = ["run", "--configuration", "Release", "--no-build", "--project", serverProject],
                WorkingDirectory = repositoryRoot, InheritEnvironmentVariables = false, EnvironmentVariables = profileEnvironment
            });
            await using var controlClient = await McpClient.CreateAsync(controlTransport);
            var controlNames = (await controlClient.ListToolsAsync()).Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);
            check(controlNames.Count == 5 && controlNames.Contains("voidwright_lab_trial") && !controlNames.Contains("voidwright_drive_lab_target"),
                "Compact lab profile must add only the explicit reviewed trial tool");
            pass("keep compact observation at four tools and gated lab workflow at five");
        }
        finally { File.WriteAllText(snapshotPath, original); }
    }
}
