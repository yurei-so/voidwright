using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Voidwright.AgentMcp;

public sealed record LiveViewCaptureReceipt(
    string WorldScopeId, string RunId, long TargetGridId, string PerspectiveId,
    long StructuralSnapshotTick, long RendererUpdateCounter,
    string TickRelationship, bool Simultaneous, string SourceKind,
    ViewPose ActualPose, double FovDegrees, int Width, int Height,
    string RendererSessionId, string CapturedAtUtc,
    double CaptureToSnapshotAgeMilliseconds, double RequestToCaptureMilliseconds,
    string RgbaSha256, string PngSha256, int RgbaBytes,
    int GeometryPrimitives, int VisiblePixels, int UniqueVisibleColors,
    long[] SourceGridIds);

public static class LiveViewCaptureReconciler
{
    private const double MaximumPositionErrorMeters = 0.001;
    private const double MinimumDirectionDot = 0.999999;
    private const int MaximumImageBytes = 8 * 1024 * 1024;

    public static LiveViewCaptureReceipt Reconcile(
        string expectedWorldName, string worldScopeId, string runId, long targetGridId,
        long structuralSnapshotTick, string structuralSnapshotCapturedAtUtc,
        ViewPose requestedPose, double requestedFovDegrees, string perspectiveId,
        DateTimeOffset requestStartedAtUtc, HoloViewCaptureFrame frame)
    {
        ArgumentNullException.ThrowIfNull(requestedPose);
        ArgumentNullException.ThrowIfNull(frame);
        if (string.IsNullOrWhiteSpace(worldScopeId) || string.IsNullOrWhiteSpace(runId) ||
            frame.WorldScopeId != worldScopeId || frame.RunId != runId || frame.PerspectiveId != perspectiveId ||
            targetGridId <= 0 || frame.TargetGridId != targetGridId)
            throw new InvalidDataException("Capture world scope, run, perspective, or target grid does not match the request.");
        if (!string.Equals(frame.WorldNameObserved, expectedWorldName, StringComparison.Ordinal))
            throw new InvalidDataException("HoloView observed a different game world name.");
        if (frame.SourceKind != "holoview-software-render-virtual")
            throw new InvalidDataException("Capture source is not an attested virtual HoloView software render.");
        if (string.IsNullOrWhiteSpace(frame.RendererSessionId) || frame.RendererSessionId.Length > 64 ||
            frame.RendererUpdateCounter < 0 || structuralSnapshotTick < 0)
            throw new InvalidDataException("Capture renderer session or update counter is invalid.");
        if (frame.StructuralSnapshotTick != structuralSnapshotTick ||
            !string.Equals(frame.StructuralSnapshotCapturedAtUtc, structuralSnapshotCapturedAtUtc, StringComparison.Ordinal))
            throw new InvalidDataException("HoloView response does not echo the exact structural snapshot used for this perspective.");
        if (frame.GeometryPrimitives <= 0 || !frame.SourceGridIds.Contains(targetGridId))
            throw new InvalidDataException("The requested target grid was not present in the rendered scene.");
        if (frame.VisiblePixels < 100 || frame.UniqueVisibleColors < 4)
            throw new InvalidDataException("HoloView returned a blank or insufficiently visible image.");
        ValidatePose(requestedPose);
        ValidatePose(frame.ActualPose);
        if (Distance(requestedPose.Position, frame.ActualPose.Position) > MaximumPositionErrorMeters ||
            Dot(requestedPose.Forward, frame.ActualPose.Forward) < MinimumDirectionDot ||
            Dot(requestedPose.Up, frame.ActualPose.Up) < MinimumDirectionDot)
            throw new InvalidDataException("Rendered camera pose differs from the requested perspective.");
        if (!double.IsFinite(requestedFovDegrees) || Math.Abs(requestedFovDegrees - frame.FovDegrees) > 1e-6)
            throw new InvalidDataException("Rendered FOV differs from the requested view.");
        if (frame.Width != 256 || frame.Height != 256 || frame.Rgba is null ||
            frame.Rgba.Length != checked(frame.Width * frame.Height * 4) || frame.Rgba.Length > MaximumImageBytes)
            throw new InvalidDataException("Capture RGBA dimensions or byte length are invalid.");

        var snapshotTime = DateTimeOffset.Parse(structuralSnapshotCapturedAtUtc, System.Globalization.CultureInfo.InvariantCulture);
        var capturedAt = DateTimeOffset.Parse(frame.CapturedAtUtc, System.Globalization.CultureInfo.InvariantCulture);
        var captureToSnapshotAge = (capturedAt - snapshotTime).TotalMilliseconds;
        var requestToCapture = (capturedAt - requestStartedAtUtc).TotalMilliseconds;
        if (!double.IsFinite(captureToSnapshotAge) || captureToSnapshotAge < 0 || captureToSnapshotAge > 30_000)
            throw new InvalidDataException("Capture is not within the freshness window of the structural snapshot.");
        if (!double.IsFinite(requestToCapture) || requestToCapture < 0 || requestToCapture > 30_000)
            throw new InvalidDataException("Capture timestamp falls outside the request window.");

        var png = PngEncoder.Encode(frame.Rgba, frame.Width, frame.Height);
        return new LiveViewCaptureReceipt(worldScopeId, runId, targetGridId, perspectiveId,
            structuralSnapshotTick, frame.RendererUpdateCounter,
            "incomparable-domains: HoloView update counter is not verified as the game simulation tick",
            false, frame.SourceKind, frame.ActualPose, frame.FovDegrees, frame.Width, frame.Height,
            frame.RendererSessionId, frame.CapturedAtUtc, captureToSnapshotAge, requestToCapture,
            Convert.ToHexString(SHA256.HashData(frame.Rgba)).ToLowerInvariant(),
            Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant(), frame.Rgba.Length,
            frame.GeometryPrimitives, frame.VisiblePixels, frame.UniqueVisibleColors, frame.SourceGridIds);
    }

    private static void ValidatePose(ViewPose pose)
    {
        if (!ValidVector(pose.Position) || !ValidVector(pose.Forward) || !ValidVector(pose.Up) ||
            Math.Abs(Length(pose.Forward) - 1) > 0.001 || Math.Abs(Length(pose.Up) - 1) > 0.001 ||
            Math.Abs(Dot(pose.Forward, pose.Up)) > 0.001)
            throw new InvalidDataException("Capture pose is not finite and orthonormal.");
    }

    private static bool ValidVector(double[]? value) => value is { Length: 3 } && value.All(double.IsFinite);
    private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
    private static double Length(double[] value) => Math.Sqrt(Dot(value, value));
    private static double Distance(double[] a, double[] b) =>
        Math.Sqrt(Math.Pow(a[0] - b[0], 2) + Math.Pow(a[1] - b[1], 2) + Math.Pow(a[2] - b[2], 2));
}

public static class PngEncoder
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static byte[] Encode(ReadOnlySpan<byte> rgba, int width, int height)
    {
        if (width <= 0 || height <= 0 || rgba.Length != checked(width * height * 4))
            throw new ArgumentException("RGBA length does not match image dimensions.");
        using var output = new MemoryStream();
        output.Write(Signature);
        Span<byte> header = stackalloc byte[13];
        WriteBigEndian(header, 0, (uint)width);
        WriteBigEndian(header, 4, (uint)height);
        header[8] = 8; header[9] = 6;
        header[10] = 0; header[11] = 0; header[12] = 0;
        WriteChunk(output, "IHDR", header);

        var stride = checked(width * 4);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            var row = new byte[stride + 1];
            for (var y = 0; y < height; y++)
            {
                row[0] = 0;
                rgba.Slice(y * stride, stride).CopyTo(row.AsSpan(1));
                zlib.Write(row);
            }
        }
        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", ReadOnlySpan<byte>.Empty);
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        WriteBigEndian(number, 0, (uint)data.Length);
        output.Write(number);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);
        var crc = 0xffffffffu;
        foreach (var value in typeBytes) crc = UpdateCrc(crc, value);
        foreach (var value in data) crc = UpdateCrc(crc, value);
        WriteBigEndian(number, 0, ~crc);
        output.Write(number);
    }

    private static uint UpdateCrc(uint crc, byte value)
    {
        crc ^= value;
        for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? 0xedb88320u ^ (crc >> 1) : crc >> 1;
        return crc;
    }

    private static void WriteBigEndian(Span<byte> target, int offset, uint value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }
}
