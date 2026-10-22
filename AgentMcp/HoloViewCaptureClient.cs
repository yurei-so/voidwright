using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Voidwright.AgentMcp;

public sealed record HoloViewCaptureRequest(
    string PerspectiveId, string WorldScopeId, string RunId, string WorldName,
    long TargetGridId, long SnapshotSimulationTick, string SnapshotCapturedAtUtc,
    ViewPose Pose, double FovDegrees, double RangeMeters, int Width = 256, int Height = 256);

public sealed record HoloViewCaptureFrame(
    string RequestId, string PerspectiveId, string WorldScopeId, string RunId,
    string WorldNameObserved, string RendererSessionId, long TargetGridId,
    long StructuralSnapshotTick, string StructuralSnapshotCapturedAtUtc,
    long RendererUpdateCounter, string CapturedAtUtc, string SourceKind,
    ViewPose ActualPose, double FovDegrees, double RangeMeters, int Width, int Height,
    long[] SourceGridIds, int GeometryPrimitives, int VisiblePixels, int UniqueVisibleColors,
    byte[] Rgba);

/// <summary>
/// One-at-a-time request/response mailbox with HoloView's world-scoped storage.
/// The only writes are bounded capture requests and cleanup of their receipts; no game state is changed.
/// </summary>
public sealed class HoloViewCaptureClient
{
    public const string StoragePathVariable = "VOIDWRIGHT_HOLOVIEW_STORAGE_PATH";
    public const string WorldNameVariable = "VOIDWRIGHT_HOLOVIEW_EXPECTED_WORLD_NAME";
    public const int MaximumResponseBytes = 2 * 1024 * 1024;
    private const string RequestFileName = "voidwright-live-capture-request-v1.txt";
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<HoloViewCaptureFrame> CaptureAsync(HoloViewCaptureRequest request,
        TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (!await gate.WaitAsync(0, cancellationToken))
            throw new InvalidOperationException("A virtual capture is already in progress; refusing to queue frames.");

        string? directory = null;
        string? requestPath = null;
        string? metadataPath = null;
        string? pixelsPath = null;
        string? temporaryPath = null;
        var requestId = Guid.NewGuid().ToString("N");
        try
        {
            directory = ResolveDirectory();
            requestPath = Path.Combine(directory, RequestFileName);
            metadataPath = Path.Combine(directory, "voidwright-live-capture-" + requestId + ".json");
            pixelsPath = Path.Combine(directory, "voidwright-live-capture-" + requestId + ".rgba64.txt");
            temporaryPath = Path.Combine(directory, RequestFileName + "." + requestId + ".tmp");
            if (File.Exists(requestPath))
                throw new InvalidOperationException("The HoloView capture mailbox contains an earlier unclaimed request; refusing to overwrite it.");
            if (File.Exists(metadataPath) || File.Exists(pixelsPath))
                throw new InvalidOperationException("A capture receipt unexpectedly exists for the new request ID.");

            var payload = FormatRequest(requestId, request);
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.WriteThrough))
            {
                var bytes = Encoding.UTF8.GetBytes(payload);
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, requestPath, overwrite: false);

            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(metadataPath))
                    return ReadFrame(requestId, metadataPath, pixelsPath);
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
            }
            throw new TimeoutException("HoloView did not complete the bounded virtual capture before the deadline.");
        }
        finally
        {
            DeleteIfRegularFile(temporaryPath);
            DeleteIfRegularFile(requestPath);
            DeleteIfRegularFile(metadataPath);
            DeleteIfRegularFile(pixelsPath);
            gate.Release();
        }
    }

    public static string? ExpectedWorldName => Environment.GetEnvironmentVariable(WorldNameVariable);

    private static string ResolveDirectory()
    {
        var configured = Environment.GetEnvironmentVariable(StoragePathVariable);
        if (string.IsNullOrWhiteSpace(configured) || !Path.IsPathFullyQualified(configured))
            throw new InvalidOperationException($"{StoragePathVariable} must identify the exact HoloView storage directory for this disposable fixture.");
        var path = Path.GetFullPath(configured);
        var info = new DirectoryInfo(path);
        if (!info.Exists || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidOperationException("Configured HoloView storage is absent or is a symbolic link.");
        var expectedWorld = ExpectedWorldName;
        if (string.IsNullOrWhiteSpace(expectedWorld) || expectedWorld.Length > 128)
            throw new InvalidOperationException($"{WorldNameVariable} must match the isolated fixture's exact game world name.");
        return path;
    }

    private static string FormatRequest(string requestId, HoloViewCaptureRequest request)
    {
        if (request.Width != 256 || request.Height != 256 || request.TargetGridId <= 0 ||
            !double.IsFinite(request.FovDegrees) || request.FovDegrees is < 25 or > 100 ||
            !double.IsFinite(request.RangeMeters) || request.RangeMeters is < 10 or > 512)
            throw new ArgumentOutOfRangeException(nameof(request), "Capture must use a 256x256 frame, valid target grid, FOV 25..100 degrees, and range 10..512 m.");
        var expectedWorld = ExpectedWorldName;
        if (!string.Equals(expectedWorld, request.WorldName, StringComparison.Ordinal))
            throw new InvalidOperationException("Perspective world does not match the HoloView fixture binding.");
        var pose = request.Pose;
        var sb = new StringBuilder(768);
        void Line(string key, string value) => sb.Append(key).Append('=').Append(value).Append('\n');
        string Vector(double[] values) => string.Join(",", values.Select(x => x.ToString("R", CultureInfo.InvariantCulture)));
        Line("schema", "voidwright-holoview-request-v1");
        Line("request_id", requestId);
        Line("perspective_id", request.PerspectiveId);
        Line("world_scope_id", request.WorldScopeId);
        Line("run_id", request.RunId);
        Line("world_name_b64", Convert.ToBase64String(Encoding.UTF8.GetBytes(request.WorldName)));
        Line("target_grid_id", request.TargetGridId.ToString(CultureInfo.InvariantCulture));
        Line("snapshot_simulation_tick", request.SnapshotSimulationTick.ToString(CultureInfo.InvariantCulture));
        Line("snapshot_captured_at_utc_b64", Convert.ToBase64String(Encoding.UTF8.GetBytes(request.SnapshotCapturedAtUtc)));
        Line("request_utc_ticks", DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
        Line("origin", Vector(pose.Position));
        Line("forward", Vector(pose.Forward));
        Line("up", Vector(pose.Up));
        Line("fov_degrees", request.FovDegrees.ToString("R", CultureInfo.InvariantCulture));
        Line("range_meters", request.RangeMeters.ToString("R", CultureInfo.InvariantCulture));
        Line("width", request.Width.ToString(CultureInfo.InvariantCulture));
        Line("height", request.Height.ToString(CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    private static HoloViewCaptureFrame ReadFrame(string expectedRequestId, string metadataPath, string pixelsPath)
    {
        var metadataInfo = new FileInfo(metadataPath);
        var pixelsInfo = new FileInfo(pixelsPath);
        if (metadataInfo.Length <= 0 || metadataInfo.Length > 32 * 1024 ||
            pixelsInfo.Length <= 0 || pixelsInfo.Length > MaximumResponseBytes)
            throw new InvalidDataException("HoloView returned an empty or over-limit capture receipt.");
        using var document = JsonDocument.Parse(File.ReadAllBytes(metadataPath), new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement;
        if (GetString(root, "schema") != "voidwright.holoview-live-capture.v1" ||
            GetString(root, "status") != "complete" || GetString(root, "request_id") != expectedRequestId)
            throw new InvalidDataException("HoloView capture schema, status, or request ID is invalid.");
        var encoded = File.ReadAllText(pixelsPath, Encoding.ASCII).Trim();
        byte[] rgba;
        try { rgba = Convert.FromBase64String(encoded); }
        catch (FormatException exception) { throw new InvalidDataException("HoloView RGBA payload is invalid base64.", exception); }
        var width = GetInt32(root, "width");
        var height = GetInt32(root, "height");
        if (width != 256 || height != 256 || rgba.Length != checked(width * height * 4))
            throw new InvalidDataException("HoloView frame dimensions do not match the bounded 256x256 RGBA contract.");
        var sourceGridIds = root.GetProperty("source_grid_ids").EnumerateArray().Select(x => x.GetInt64()).ToArray();
        return new HoloViewCaptureFrame(
            expectedRequestId, GetString(root, "perspective_id"), GetString(root, "world_scope_id"),
            GetString(root, "run_id"), GetString(root, "world_name_observed"), GetString(root, "renderer_session_id"),
            GetInt64(root, "target_grid_id"), GetInt64(root, "structural_snapshot_tick"),
            GetString(root, "structural_snapshot_captured_at_utc"), GetInt64(root, "renderer_update_counter"),
            GetString(root, "captured_at_utc"), GetString(root, "source_kind"),
            new ViewPose(GetVector(root, "origin"), GetVector(root, "forward"), GetVector(root, "up")),
            GetDouble(root, "fov_degrees"), GetDouble(root, "range_meters"), width, height,
            sourceGridIds, GetInt32(root, "geometry_primitives"), GetInt32(root, "visible_pixels"),
            GetInt32(root, "unique_visible_colors"), rgba);
    }

    private static void DeleteIfRegularFile(string? path)
    {
        if (path is null) return;
        try
        {
            var info = new FileInfo(path);
            if (info.Exists && !info.Attributes.HasFlag(FileAttributes.ReparsePoint)) info.Delete();
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string GetString(JsonElement root, string name) => root.GetProperty(name).GetString() ?? "";
    private static long GetInt64(JsonElement root, string name) => root.GetProperty(name).GetInt64();
    private static int GetInt32(JsonElement root, string name) => root.GetProperty(name).GetInt32();
    private static double GetDouble(JsonElement root, string name) => root.GetProperty(name).GetDouble();
    private static double[] GetVector(JsonElement root, string name) => root.GetProperty(name).EnumerateArray().Select(x => x.GetDouble()).ToArray();
}
