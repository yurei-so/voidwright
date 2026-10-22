using System.Globalization;
using System.Text;
using System.Text.Json;

// Synthetic pixels and echoed metadata for transport tests, never game footage.
internal static class FakeHoloViewRenderer
{
    public static async Task CompleteOnce(string directory)
    {
        var requestFile = Path.Combine(directory, "voidwright-live-capture-request-v1.txt");
        for (var attempt = 0; attempt < 300 && !File.Exists(requestFile); attempt++) await Task.Delay(20);
        if (!File.Exists(requestFile)) throw new TimeoutException("MCP did not write the bounded HoloView request.");
        var fields = File.ReadAllLines(requestFile).Select(line => line.Split('=', 2))
            .ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal);
        var stem = "voidwright-live-capture-" + fields["request_id"];
        var pixels = new byte[256 * 256 * 4];
        for (var pixel = 0; pixel < 256 * 256; pixel++)
        {
            pixels[pixel * 4] = (byte)(32 + pixel % 4 * 40);
            pixels[pixel * 4 + 1] = (byte)(48 + pixel % 3 * 30);
            pixels[pixel * 4 + 2] = 180; pixels[pixel * 4 + 3] = 255;
        }
        File.WriteAllText(Path.Combine(directory, stem + ".rgba64.txt"), Convert.ToBase64String(pixels));
        double[] Vector(string name) => fields[name].Split(',').Select(value => double.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        var response = new
        {
            schema = "voidwright.holoview-live-capture.v1", status = "complete", source_kind = "holoview-software-render-virtual",
            request_id = fields["request_id"], perspective_id = fields["perspective_id"], world_scope_id = fields["world_scope_id"],
            run_id = fields["run_id"], world_name_observed = "test_suite_world_v2_r3", renderer_session_id = "0123456789abcdef0123456789abcdef",
            target_grid_id = long.Parse(fields["target_grid_id"], CultureInfo.InvariantCulture),
            structural_snapshot_tick = long.Parse(fields["snapshot_simulation_tick"], CultureInfo.InvariantCulture),
            structural_snapshot_captured_at_utc = Encoding.UTF8.GetString(Convert.FromBase64String(fields["snapshot_captured_at_utc_b64"])),
            renderer_update_counter = 75L, captured_at_utc = DateTimeOffset.UtcNow.ToString("O"), width = 256, height = 256,
            fov_degrees = double.Parse(fields["fov_degrees"], CultureInfo.InvariantCulture),
            range_meters = double.Parse(fields["range_meters"], CultureInfo.InvariantCulture),
            origin = Vector("origin"), forward = Vector("forward"), up = Vector("up"),
            source_grid_ids = new[] { long.Parse(fields["target_grid_id"], CultureInfo.InvariantCulture) },
            geometry_primitives = 12, visible_pixels = 65536, unique_visible_colors = 12
        };
        var temporary = Path.Combine(directory, stem + ".json.tmp");
        File.WriteAllText(temporary, JsonSerializer.Serialize(response));
        File.Move(temporary, Path.Combine(directory, stem + ".json"));
    }
}
