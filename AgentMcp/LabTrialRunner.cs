using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Voidwright.AgentMcp;

/// <summary>Fixed-fixture adapter over VRageCage's reviewed, journaled trial workflow.</summary>
public sealed class LabTrialRunner
{
    private static readonly SemaphoreSlim TrialLock = new(1, 1);
    public const string CliPathVariable = "VOIDWRIGHT_LAB_CLI_PATH";
    public const string InstanceVariable = "VOIDWRIGHT_LAB_INSTANCE";
    public const string FixtureIdVariable = "VOIDWRIGHT_LAB_FIXTURE_ID";
    public const string GridNameVariable = "VOIDWRIGHT_LAB_GRID_NAME";
    public const string ControllerIdVariable = "VOIDWRIGHT_LAB_CONTROLLER_ID";
    public const string CameraIdVariable = "VOIDWRIGHT_LAB_DEPTH_CAMERA_ID";
    public const string SnapshotRelativePathVariable = "VOIDWRIGHT_LAB_SNAPSHOT_RELATIVE_PATH";
    private static readonly HashSet<string> AllowedTargets = new(StringComparer.Ordinal)
    { "Red Zone", "Yellow Zone", "Green Zone", "Blue Zone", "Safe Spawn Zone", "Starting Area", "End Position" };

    // Compatibility entry point. It uses the same new workflow, not a second
    // lifecycle implementation. No fallback to the old uncorrelated CLI path.
    public string RunGpsGoal(string gpsTarget)
    {
        Enter();
        try
        {
            var config = ReadConfiguration();
            var preview = Preview(config, gpsTarget);
            return Execute(config, gpsTarget, preview.RequestId, preview.PlanHash);
        }
        finally { TrialLock.Release(); }
    }

    public string PlanGpsGoal(string gpsTarget)
    {
        Enter();
        try { return Preview(ReadConfiguration(), gpsTarget).Json; }
        finally { TrialLock.Release(); }
    }

    public string ExecuteGpsPlan(string gpsTarget, string requestId, string expectedPlanSha256)
    {
        if (!IsHex(requestId, 32) || !IsHex(expectedPlanSha256, 64))
            throw new ArgumentException("Execution requires the request ID and exact SHA256 returned by the preview.");
        Enter();
        try { return Execute(ReadConfiguration(), gpsTarget, requestId, expectedPlanSha256); }
        finally { TrialLock.Release(); }
    }

    public string InspectGpsPlan(string gpsTarget, string requestId)
    {
        if (!AllowedTargets.Contains(gpsTarget) || !IsHex(requestId, 32)) throw new ArgumentException("Status requires the exact supported target and trial request identity.");
        var config = ReadConfiguration();
        var output = RunCli(config, ["lab-trial-status", config.Instance, "--fixture-id", config.FixtureId, "--request-id", requestId], TimeSpan.FromSeconds(30));
        using var parsed = ParseJson(output.Output);
        var root = parsed.RootElement;
        if (output.ExitCode != 0 || Text(root, "schema") != "vragecage.lab-trial-status.v1"
            || Text(root, "request_id") != requestId || Text(root, "fixture_id") != config.FixtureId)
            throw Failure(root, "Cannot read the pinned trial journal.");
        if (root.TryGetProperty("request", out var request) && request.ValueKind == JsonValueKind.Object
            && (Text(request, "target") != gpsTarget || Text(request, "controller_id") != config.ControllerId
                || Text(request, "depth_camera_id") != config.CameraId || Text(request, "grid_name") != config.GridName
                || Text(request, "snapshot_relative_path") != config.SnapshotRelativePath))
            throw new InvalidDataException("Journal does not match the pinned vehicle binding.");
        return root.GetRawText();
    }

    private static void Enter()
    {
        if (!TrialLock.Wait(0)) throw new InvalidOperationException("A bounded Voidwright lab request is already in progress.");
    }

    private static PreviewResult Preview(Configuration config, string target)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var result = RunCli(config, Arguments(config, target, requestId), TimeSpan.FromSeconds(45));
        using var parsed = ParseJson(result.Output);
        var root = parsed.RootElement;
        if (result.ExitCode != 0 || Text(root, "schema") != "vragecage.lab-trial-preview.v1")
            throw Failure(root, "VRageCage could not preview the pinned trial.");
        var hash = Text(root, "plan_sha256");
        if (!IsHex(hash, 64) || !root.TryGetProperty("plan", out var plan)
            || !plan.TryGetProperty("request", out var request)
            || Text(request, "instance") != config.Instance || Text(request, "fixture_id") != config.FixtureId
            || Text(request, "request_id") != requestId || Text(request, "controller_id") != config.ControllerId
            || Text(request, "depth_camera_id") != config.CameraId || Text(request, "target") != target
            || Text(request, "grid_name") != config.GridName || Text(request, "snapshot_relative_path") != config.SnapshotRelativePath
            || !plan.TryGetProperty("grid", out var grid) || Text(grid, "name") != config.GridName)
            throw new InvalidDataException("VRageCage preview does not match the pinned fixture and exact request.");
        return new PreviewResult(requestId, hash, root.GetRawText());
    }

    private static string Execute(Configuration config, string target, string requestId, string hash)
    {
        var arguments = Arguments(config, target, requestId);
        arguments.AddRange(["--execute", "--expect-plan-sha256", hash]);
        // Worker owns its startup/wait/shutdown deadlines and durable journal.
        var result = RunCli(config, arguments, TimeSpan.FromSeconds(720));
        using var parsed = ParseJson(result.Output);
        var root = parsed.RootElement;
        if (Text(root, "schema") != "vragecage.lab-trial-result.v1")
            throw Failure(root, "VRageCage refused the reviewed trial; inspect its journal before retrying.");
        if (Text(root, "request_id") != requestId || Text(root, "fixture_id") != config.FixtureId
            || Text(root, "plan_sha256") != hash)
            throw new InvalidDataException("VRageCage result does not match the submitted plan/request/fixture.");
        if (result.ExitCode != 0 && root.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.True)
            throw new InvalidDataException("VRageCage exit status disagrees with its success claim.");
        // Failed or review-required outcomes are structured results, not an
        // invitation to send the command again under a fresh request identity.
        return root.GetRawText();
    }

    private static List<string> Arguments(Configuration config, string target, string requestId)
    {
        if (!AllowedTargets.Contains(target)) throw new ArgumentException("gpsTarget must be an exact supported lab GPS label; Observation Towers is observation-only.");
        return ["lab-trial", config.Instance, "--fixture-id", config.FixtureId, "--request-id", requestId,
            "--controller-id", config.ControllerId, "--depth-camera-id", config.CameraId,
            "--grid-name", config.GridName, "--target", target, "--snapshot-relative-path", config.SnapshotRelativePath];
    }

    private static Configuration ReadConfiguration()
    {
        string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value : throw new InvalidOperationException(name + " must be explicitly configured.");
        var cli = Required(CliPathVariable);
        if (!Path.IsPathFullyQualified(cli) || !File.Exists(cli)) throw new InvalidOperationException("The configured lab CLI must be an existing absolute executable path.");
        var instance = Required(InstanceVariable);
        if (instance.Length > 64 || !char.IsAsciiLetterOrDigit(instance[0])
            || !instance.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'))
            throw new InvalidOperationException("Lab instance name is invalid.");
        var fixture = Required(FixtureIdVariable);
        if (!IsHex(fixture, 32)) throw new InvalidOperationException("Lab fixture identity must be exact lowercase 32-hex.");
        var gridName = Required(GridNameVariable);
        if (gridName.Length > 128 || gridName.Any(char.IsControl)) throw new InvalidOperationException("Lab grid name is invalid.");
        var controller = Required(ControllerIdVariable);
        var camera = Required(CameraIdVariable);
        foreach (var id in new[] { controller, camera })
            if (!long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value <= 0
                || value.ToString(CultureInfo.InvariantCulture) != id) throw new InvalidOperationException("Lab block IDs must be exact canonical decimal strings.");
        var snapshot = Required(SnapshotRelativePathVariable);
        if (snapshot.Length > 512 || !snapshot.StartsWith("Storage/", StringComparison.Ordinal)
            || !snapshot.EndsWith("/agent-core-snapshot-v1.json", StringComparison.Ordinal)
            || snapshot.Contains('\\') || snapshot.Split('/').Any(part => part is "" or "." or ".."))
            throw new InvalidOperationException("Lab snapshot must name an exact relative Storage/.../agent-core-snapshot-v1.json in the pinned fixture.");
        return new Configuration(Path.GetFullPath(cli), instance, fixture, gridName, controller, camera, snapshot);
    }

    private static ProcessResult RunCli(Configuration config, IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        var start = new ProcessStartInfo(config.CliPath) { UseShellExecute = false, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add("--local");
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new InvalidOperationException("Could not start the configured local VRageCage CLI.");
        using var deadline = new CancellationTokenSource(timeout);
        try
        {
            var output = ReadBounded(process.StandardOutput, 1_000_000, deadline);
            var error = ReadBounded(process.StandardError, 128_000, deadline);
            Task.WhenAll(output, error, process.WaitForExitAsync(deadline.Token)).GetAwaiter().GetResult();
            return new ProcessResult(process.ExitCode, output.Result);
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or InvalidDataException)
        {
            try { process.Kill(entireProcessTree: true); process.WaitForExit(2_000); } catch (InvalidOperationException) { }
            throw new InvalidOperationException("Lab CLI interrupted or exceeded its output/time bound. Inspect its durable trial journal; do not automatically retry with a new ID.", exception);
        }
    }

    private static async Task<string> ReadBounded(StreamReader reader, int maximum, CancellationTokenSource cancellation)
    {
        var result = new StringBuilder();
        var buffer = new char[4096];
        int count;
        try
        {
            while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellation.Token)) > 0)
            {
                if (result.Length + count > maximum) throw new InvalidDataException("Lab CLI output exceeded its bound.");
                result.Append(buffer, 0, count);
            }
        }
        catch { cancellation.Cancel(); throw; }
        return result.ToString();
    }

    private static JsonDocument ParseJson(string text)
    {
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Reverse())
        {
            try { return JsonDocument.Parse(line); } catch (JsonException) { }
        }
        throw new InvalidDataException("Configured CLI returned no trial JSON. Verify matching CLI/worker versions and inspect the existing request journal before retrying.");
    }
    private static Exception Failure(JsonElement result, string fallback)
        => new InvalidOperationException(Text(result, "error") is { Length: > 0 } error ? error[..Math.Min(error.Length, 600)] : fallback);
    private static string Text(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static bool IsHex(string value, int length) => value.Length == length && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    private sealed record Configuration(string CliPath, string Instance, string FixtureId, string GridName, string ControllerId, string CameraId, string SnapshotRelativePath);
    private sealed record PreviewResult(string RequestId, string PlanHash, string Json);
    private sealed record ProcessResult(int ExitCode, string Output);
}
