using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Voidwright.Core
{
    // Small, display-only packet shared by the MCP adapter and game mod.
    // No block, entity, or control instruction is carried on this channel.
    public sealed class VirtualCameraMarker
    {
        public string Id;
        public long SnapshotTick;
        public DateTime SnapshotUtc;
        public double[] Position;
        public double[] Forward;
        public double[] Up;
    }

    public sealed class VirtualCameraMarkerBatch
    {
        public string RunId;
        public DateTime PublishedUtc;
        public readonly List<VirtualCameraMarker> Markers = new List<VirtualCameraMarker>();
    }

    public static class VirtualCameraMarkerWire
    {
        public const string FileName = "virtual-camera-markers-v1.txt";
        public const ushort Channel = 42137;
        public const int MaxMarkers = 16;
        public const int MaxBytes = 8192;
        private const string Header = "VWCM1";

        public static string Format(VirtualCameraMarkerBatch batch)
        {
            if (batch == null || !HexId(batch.RunId) || batch.Markers.Count > MaxMarkers)
                throw new ArgumentException("Invalid camera marker batch.");
            var text = new StringBuilder(Header).Append('\n').Append(batch.RunId).Append('\n')
                .Append(batch.PublishedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)).Append('\n')
                .Append(batch.Markers.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
            foreach (var marker in batch.Markers)
            {
                if (!Valid(marker)) throw new ArgumentException("Invalid camera marker pose.");
                text.Append(marker.Id).Append('|').Append(marker.SnapshotTick).Append('|')
                    .Append(marker.SnapshotUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                AppendVector(text, marker.Position);
                AppendVector(text, marker.Forward);
                AppendVector(text, marker.Up);
                text.Append('\n');
            }
            if (Encoding.UTF8.GetByteCount(text.ToString()) > MaxBytes)
                throw new ArgumentException("Camera marker packet exceeds bound.");
            return text.ToString();
        }

        public static bool TryParse(string text, out VirtualCameraMarkerBatch batch)
        {
            batch = null;
            if (string.IsNullOrEmpty(text) || Encoding.UTF8.GetByteCount(text) > MaxBytes) return false;
            var lines = text.Split(new[] { '\n' }, StringSplitOptions.None);
            int count;
            DateTime published;
            if (lines.Length < 5 || lines[0] != Header || !HexId(lines[1]) ||
                !Utc(lines[2], out published) ||
                !int.TryParse(lines[3], NumberStyles.None, CultureInfo.InvariantCulture, out count) ||
                count < 0 || count > MaxMarkers || lines.Length != count + 5 || lines[lines.Length - 1] != "")
                return false;
            var parsed = new VirtualCameraMarkerBatch { RunId = lines[1], PublishedUtc = published };
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < count; i++)
            {
                var fields = lines[i + 4].Split('|');
                long tick;
                DateTime sampled;
                if (fields.Length != 12 || !HexId(fields[0]) || !ids.Add(fields[0]) ||
                    !long.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out tick) ||
                    !Utc(fields[2], out sampled)) return false;
                var numbers = new double[9];
                for (var n = 0; n < numbers.Length; n++)
                    if (!double.TryParse(fields[n + 3], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[n]) ||
                        double.IsNaN(numbers[n]) || double.IsInfinity(numbers[n])) return false;
                var marker = new VirtualCameraMarker
                {
                    Id = fields[0], SnapshotTick = tick, SnapshotUtc = sampled,
                    Position = new[] { numbers[0], numbers[1], numbers[2] },
                    Forward = new[] { numbers[3], numbers[4], numbers[5] },
                    Up = new[] { numbers[6], numbers[7], numbers[8] }
                };
                if (!Valid(marker)) return false;
                parsed.Markers.Add(marker);
            }
            batch = parsed;
            return true;
        }

        public static bool Fresh(VirtualCameraMarkerBatch batch, VirtualCameraMarker marker, DateTime now)
        {
            if (batch == null || marker == null) return false;
            var publishAge = now.ToUniversalTime() - batch.PublishedUtc;
            var sampleAge = now.ToUniversalTime() - marker.SnapshotUtc;
            return publishAge >= TimeSpan.Zero && publishAge <= TimeSpan.FromSeconds(10) &&
                   sampleAge >= TimeSpan.Zero && sampleAge <= TimeSpan.FromSeconds(30);
        }

        private static bool Valid(VirtualCameraMarker marker)
        {
            if (marker == null || !HexId(marker.Id) || marker.SnapshotTick < 0 ||
                marker.SnapshotUtc.Kind != DateTimeKind.Utc || !Finite(marker.Position) ||
                !Finite(marker.Forward) || !Finite(marker.Up)) return false;
            var forwardLength = Length(marker.Forward);
            var upLength = Length(marker.Up);
            return Math.Abs(forwardLength - 1) < 0.02 && Math.Abs(upLength - 1) < 0.02 &&
                   Math.Abs(Dot(marker.Forward, marker.Up)) < 0.02;
        }

        private static bool Finite(double[] value)
        {
            if (value == null || value.Length != 3) return false;
            foreach (var number in value)
                if (double.IsNaN(number) || double.IsInfinity(number)) return false;
            return true;
        }

        private static double Length(double[] value) => Math.Sqrt(Dot(value, value));
        private static double Dot(double[] a, double[] b) =>
            a[0] * b[0] + a[1] * b[1] + a[2] * b[2];

        private static bool HexId(string id)
        {
            if (id == null || id.Length != 32) return false;
            foreach (var c in id)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            return true;
        }

        private static bool Utc(string value, out DateTime result)
        {
            if (!DateTime.TryParseExact(value, "O", CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out result) || result.Kind != DateTimeKind.Utc)
                return false;
            return true;
        }

        private static void AppendVector(StringBuilder text, double[] vector)
        {
            text.Append('|').Append(vector[0].ToString("R", CultureInfo.InvariantCulture))
                .Append('|').Append(vector[1].ToString("R", CultureInfo.InvariantCulture))
                .Append('|').Append(vector[2].ToString("R", CultureInfo.InvariantCulture));
        }
    }
}
