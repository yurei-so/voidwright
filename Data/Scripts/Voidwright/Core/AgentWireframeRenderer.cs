using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Voidwright.Core
{
    /// <summary>One bounded, read-only geometry query for an enrolled agent body.</summary>
    public sealed class AgentWireframeModel
    {
        public string AgentId;
        public long CapturedSimulationTick;
        public bool SourceSnapshotComplete = true;
        public readonly List<AgentWireframeBlock> Blocks = new List<AgentWireframeBlock>();
    }

    /// <summary>Eight corners in the Agent Core's right/up/forward coordinate frame.</summary>
    public sealed class AgentWireframeBlock
    {
        public long GridEntityId;
        public long BlockEntityId;
        public string DefinitionType;
        public string SubtypeId;
        public float DamageRatio;
        public bool IsFunctional;
        public bool HasIntegrityData;
        public readonly List<AgentWireframePoint> Corners = new List<AgentWireframePoint>(8);
    }

    public enum AgentWireframeVisualizationMode
    {
        Structural,
        Damage
    }

    public struct AgentWireframePoint
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Z;

        public AgentWireframePoint(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    /// <summary>
    /// A selector matches all supplied identity fields. Zero IDs and empty definition fields
    /// are wildcards; at least one non-wildcard field is required. Earlier selectors win.
    /// </summary>
    public sealed class AgentWireframeSelection
    {
        public long GridEntityId;
        public long BlockEntityId;
        public string DefinitionType;
        public string SubtypeId;
        public string Color;
    }

    public sealed class AgentWireframeRenderResult
    {
        public string Svg;
        public string VisualizationMode;
        public int RenderedBlocks;
        public int SelectedBlocks;
        public int HealthyBlocks;
        public int DamagedBlocks;
        public int CriticalBlocks;
        public int UnknownIntegrityBlocks;
        public bool Truncated;
    }

    /// <summary>
    /// Deterministic orthographic wireframe renderer. It has no game API dependency and never
    /// mutates the body. The SVG is self-contained and bounded for a future MCP resource result.
    /// </summary>
    public static class AgentWireframeRenderer
    {
        public const int MaximumBlocks = 4096;
        public const int MaximumInputBlocks = 16384;
        public const int MaximumSelections = 128;
        public const int MaximumSvgCharacters = 1500000;

        private const string DefaultColor = "#8190A5";
        private const string HealthyColor = "#36C98F";
        private const string DamagedColor = "#FFB547";
        private const string CriticalColor = "#FF5D70";
        private const string UnknownIntegrityColor = "#8190A5";
        private const string BackgroundColor = "#101722";
        private const string PanelColor = "#172230";
        private const int SvgWidth = 1440;
        private const int SvgHeight = 500;

        private enum ProfileAxis
        {
            Right,
            Up,
            Forward
        }

        private sealed class RenderBlock
        {
            public AgentWireframeBlock Source;
            public string Color;
            public bool Selected;
        }

        private sealed class ProjectedPoint
        {
            public double X;
            public double Y;
        }

        private sealed class Projection
        {
            public string Name;
            public string HorizontalLabel;
            public string VerticalLabel;
            public ProfileAxis Horizontal;
            public ProfileAxis Vertical;
            public int X;
            public int Y;
            public int Width;
            public int Height;
            public double MinX;
            public double MinY;
            public double Scale;
        }

        public static AgentWireframeRenderResult Render(
            AgentWireframeModel model,
            IList<AgentWireframeSelection> selections,
            AgentWireframeVisualizationMode visualizationMode = AgentWireframeVisualizationMode.Structural)
        {
            if (model == null) throw new ArgumentNullException("model");
            if (model.Blocks.Count > MaximumInputBlocks)
                throw new ArgumentException("Wireframe input block count exceeds the bounded limit.", "model");
            if (model.AgentId != null && model.AgentId.Length > 128)
                throw new ArgumentException("Agent identity exceeds the bounded wireframe metadata length.", "model");
            if (HasUnsafeSelectorText(model.AgentId))
                throw new ArgumentException("Agent identity cannot contain control characters.", "model");
            if (selections == null) selections = new List<AgentWireframeSelection>();
            if (visualizationMode != AgentWireframeVisualizationMode.Structural &&
                visualizationMode != AgentWireframeVisualizationMode.Damage)
                throw new ArgumentException("Unsupported wireframe visualization mode.", "visualizationMode");
            if (selections.Count > MaximumSelections)
                throw new ArgumentException("Wireframe selection count exceeds the bounded limit.", "selections");

            var validated = new List<AgentWireframeSelection>(selections.Count);
            foreach (var selection in selections)
            {
                ValidateSelection(selection);
                validated.Add(selection);
            }

            var renderBlocks = new List<RenderBlock>();
            var malformedGeometry = false;
            foreach (var block in model.Blocks)
            {
                if (block == null || block.Corners.Count != 8 || block.Corners.Any(point => !Finite(point.X) || !Finite(point.Y) || !Finite(point.Z)))
                {
                    malformedGeometry = true;
                    continue;
                }
                var selectionColor = SelectColor(block, validated);
                var color = selectionColor ?? (visualizationMode == AgentWireframeVisualizationMode.Damage
                    ? DamageColor(block)
                    : null);
                renderBlocks.Add(new RenderBlock
                {
                    Source = block,
                    Color = color ?? DefaultColor,
                    Selected = selectionColor != null
                });
            }

            // Keep highlighted geometry first if the snapshot exceeded the renderer's hard cap.
            renderBlocks = renderBlocks
                .OrderByDescending(block => block.Selected)
                .ThenBy(block => block.Source.GridEntityId)
                .ThenBy(block => block.Source.BlockEntityId)
                .ThenBy(block => block.Source.SubtypeId, StringComparer.Ordinal)
                .ToList();
            var truncated = !model.SourceSnapshotComplete || malformedGeometry || renderBlocks.Count > MaximumBlocks;
            if (renderBlocks.Count > MaximumBlocks)
                renderBlocks.RemoveRange(MaximumBlocks, renderBlocks.Count - MaximumBlocks);

            var projections = new[]
            {
                new Projection { Name = "top", HorizontalLabel = "RIGHT", VerticalLabel = "FORWARD", Horizontal = ProfileAxis.Right, Vertical = ProfileAxis.Forward, X = 16, Y = 62, Width = 456, Height = 420 },
                new Projection { Name = "side", HorizontalLabel = "FORWARD", VerticalLabel = "UP", Horizontal = ProfileAxis.Forward, Vertical = ProfileAxis.Up, X = 492, Y = 62, Width = 456, Height = 420 },
                new Projection { Name = "front", HorizontalLabel = "RIGHT", VerticalLabel = "UP", Horizontal = ProfileAxis.Right, Vertical = ProfileAxis.Up, X = 968, Y = 62, Width = 456, Height = 420 }
            };

            foreach (var projection in projections)
                FitProjection(projection, renderBlocks);

            var pathGroups = new[]
            {
                new Dictionary<string, StringBuilder>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, StringBuilder>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, StringBuilder>(StringComparer.OrdinalIgnoreCase)
            };
            var selectedBlockCount = 0;
            var renderedBlockCount = 0;
            var drawnBlocks = new List<AgentWireframeBlock>();
            foreach (var block in renderBlocks)
            {
                var pending = new List<string>(3);
                foreach (var projection in projections)
                    pending.Add(ProjectPolygon(block.Source, projection));
                var pendingLength = pending.Sum(path => path.Length + 3);
                var existingLength = pathGroups.Sum(groups => groups.Values.Sum(builder => builder.Length));
                if (existingLength + pendingLength > MaximumSvgCharacters - 90000)
                {
                    truncated = true;
                    break;
                }
                var styleKey = (block.Selected ? "selected:" : "normal:") + block.Color;
                for (var index = 0; index < projections.Length; index++)
                {
                    StringBuilder builder;
                    if (!pathGroups[index].TryGetValue(styleKey, out builder))
                    {
                        builder = new StringBuilder();
                        pathGroups[index].Add(styleKey, builder);
                    }
                    builder.Append(pending[index]).Append(' ');
                }
                renderedBlockCount++;
                drawnBlocks.Add(block.Source);
                if (block.Selected) selectedBlockCount++;
            }
            if (renderedBlockCount < renderBlocks.Count) truncated = true;

            var mode = visualizationMode == AgentWireframeVisualizationMode.Damage ? "damage" : "structural";
            var svg = BuildSvg(model, projections, pathGroups, truncated, renderedBlockCount, selectedBlockCount, mode);
            var integrityBlocks = drawnBlocks;
            var knownIntegrity = integrityBlocks.Where(HasValidIntegrity).ToList();
            return new AgentWireframeRenderResult
            {
                Svg = svg,
                VisualizationMode = mode,
                RenderedBlocks = renderedBlockCount,
                SelectedBlocks = selectedBlockCount,
                HealthyBlocks = visualizationMode == AgentWireframeVisualizationMode.Damage
                    ? knownIntegrity.Count(block => block.IsFunctional && block.DamageRatio == 0) : 0,
                DamagedBlocks = visualizationMode == AgentWireframeVisualizationMode.Damage
                    ? knownIntegrity.Count(block => block.IsFunctional && block.DamageRatio > 0) : 0,
                CriticalBlocks = visualizationMode == AgentWireframeVisualizationMode.Damage
                    ? knownIntegrity.Count(block => !block.IsFunctional) : 0,
                UnknownIntegrityBlocks = visualizationMode == AgentWireframeVisualizationMode.Damage
                    ? integrityBlocks.Count - knownIntegrity.Count : 0,
                Truncated = truncated || model.Blocks.Count > MaximumBlocks
            };
        }

        private static string DamageColor(AgentWireframeBlock block)
        {
            if (!HasValidIntegrity(block)) return UnknownIntegrityColor;
            if (!block.IsFunctional) return CriticalColor;
            return block.DamageRatio > 0 ? DamagedColor : HealthyColor;
        }

        private static bool HasValidIntegrity(AgentWireframeBlock block)
            => block.HasIntegrityData && Finite(block.DamageRatio) && block.DamageRatio >= 0 && block.DamageRatio <= 1;

        private static void ValidateSelection(AgentWireframeSelection selection)
        {
            if (selection == null) throw new ArgumentException("Wireframe selections cannot be null.", "selections");
            if (selection.GridEntityId < 0 || selection.BlockEntityId < 0 ||
                (selection.GridEntityId == 0 && selection.BlockEntityId == 0 &&
                 string.IsNullOrWhiteSpace(selection.DefinitionType) && string.IsNullOrWhiteSpace(selection.SubtypeId)))
                throw new ArgumentException("Each wireframe selector needs a grid, block, definition type, or subtype identity.", "selections");
            if (!IsColor(selection.Color))
                throw new ArgumentException("Wireframe colors must be opaque #RRGGBB values.", "selections");
            if ((selection.DefinitionType != null && selection.DefinitionType.Length > 128) ||
                (selection.SubtypeId != null && selection.SubtypeId.Length > 128))
                throw new ArgumentException("Wireframe selector text exceeds the bounded length.", "selections");
            if (HasUnsafeSelectorText(selection.DefinitionType) || HasUnsafeSelectorText(selection.SubtypeId))
                throw new ArgumentException("Wireframe selectors cannot contain control characters.", "selections");
        }

        private static bool HasUnsafeSelectorText(string value)
        {
            if (value == null) return false;
            foreach (var character in value)
                if (char.IsControl(character)) return true;
            return false;
        }

        private static bool IsColor(string value)
        {
            if (value == null || value.Length != 7 || value[0] != '#') return false;
            for (var index = 1; index < value.Length; index++)
                if (!IsAsciiHexDigit(value[index])) return false;
            return true;
        }

        private static bool IsAsciiHexDigit(char value)
        {
            return (value >= '0' && value <= '9') ||
                   (value >= 'A' && value <= 'F') ||
                   (value >= 'a' && value <= 'f');
        }

        private static string SelectColor(AgentWireframeBlock block, IList<AgentWireframeSelection> selections)
        {
            foreach (var selection in selections)
            {
                if (selection.GridEntityId != 0 && selection.GridEntityId != block.GridEntityId) continue;
                if (selection.BlockEntityId != 0 && selection.BlockEntityId != block.BlockEntityId) continue;
                if (!string.IsNullOrWhiteSpace(selection.DefinitionType) &&
                    !string.Equals(selection.DefinitionType, block.DefinitionType, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrWhiteSpace(selection.SubtypeId) &&
                    !string.Equals(selection.SubtypeId, block.SubtypeId, StringComparison.OrdinalIgnoreCase)) continue;
                return selection.Color.ToUpperInvariant();
            }
            return null;
        }

        private static void FitProjection(Projection projection, IList<RenderBlock> blocks)
        {
            var points = blocks.SelectMany(block => block.Source.Corners).ToList();
            if (points.Count == 0)
            {
                projection.MinX = projection.MinY = -0.5;
                projection.Scale = 1;
                return;
            }
            var minX = double.MaxValue;
            var minY = double.MaxValue;
            var maxX = double.MinValue;
            var maxY = double.MinValue;
            foreach (var point in points)
            {
                var x = Axis(point, projection.Horizontal);
                var y = Axis(point, projection.Vertical);
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }
            var spanX = Math.Max(0.01, maxX - minX);
            var spanY = Math.Max(0.01, maxY - minY);
            var viewportWidth = projection.Width - 72.0;
            var viewportHeight = projection.Height - 96.0;
            projection.Scale = Math.Min(viewportWidth / spanX, viewportHeight / spanY);
            projection.MinX = (minX + maxX) / 2.0;
            projection.MinY = (minY + maxY) / 2.0;
        }

        private static string ProjectPolygon(AgentWireframeBlock block, Projection projection)
        {
            var projected = block.Corners.Select(point => new ProjectedPoint
            {
                X = Axis(point, projection.Horizontal),
                Y = Axis(point, projection.Vertical)
            }).ToList();
            var hull = ConvexHull(projected);
            if (hull.Count == 0) return "";
            var viewportCenterX = projection.X + projection.Width / 2.0;
            var viewportCenterY = projection.Y + 38 + (projection.Height - 88) / 2.0;
            var builder = new StringBuilder("M");
            for (var index = 0; index < hull.Count; index++)
            {
                if (index > 0) builder.Append('L');
                var x = viewportCenterX + (hull[index].X - projection.MinX) * projection.Scale;
                var y = viewportCenterY - (hull[index].Y - projection.MinY) * projection.Scale;
                builder.Append(Format(x)).Append(',').Append(Format(y));
            }
            return builder.Append('Z').ToString();
        }

        private static List<ProjectedPoint> ConvexHull(List<ProjectedPoint> points)
        {
            points.Sort((left, right) =>
            {
                var compare = left.X.CompareTo(right.X);
                return compare != 0 ? compare : left.Y.CompareTo(right.Y);
            });
            var unique = new List<ProjectedPoint>();
            foreach (var point in points)
                if (unique.Count == 0 || Math.Abs(unique[unique.Count - 1].X - point.X) > 0.0000001 ||
                    Math.Abs(unique[unique.Count - 1].Y - point.Y) > 0.0000001)
                    unique.Add(point);
            if (unique.Count <= 2) return unique;

            var lower = new List<ProjectedPoint>();
            foreach (var point in unique)
            {
                while (lower.Count >= 2 && Cross(lower[lower.Count - 2], lower[lower.Count - 1], point) <= 0)
                    lower.RemoveAt(lower.Count - 1);
                lower.Add(point);
            }
            var upper = new List<ProjectedPoint>();
            for (var index = unique.Count - 1; index >= 0; index--)
            {
                var point = unique[index];
                while (upper.Count >= 2 && Cross(upper[upper.Count - 2], upper[upper.Count - 1], point) <= 0)
                    upper.RemoveAt(upper.Count - 1);
                upper.Add(point);
            }
            lower.RemoveAt(lower.Count - 1);
            upper.RemoveAt(upper.Count - 1);
            lower.AddRange(upper);
            return lower;
        }

        private static double Cross(ProjectedPoint origin, ProjectedPoint a, ProjectedPoint b)
            => (a.X - origin.X) * (b.Y - origin.Y) - (a.Y - origin.Y) * (b.X - origin.X);

        private static double Axis(AgentWireframePoint point, ProfileAxis axis)
        {
            switch (axis)
            {
                case ProfileAxis.Right: return point.X;
                case ProfileAxis.Up: return point.Y;
                default: return point.Z;
            }
        }

        private static string BuildSvg(
            AgentWireframeModel model,
            IList<Projection> projections,
            IList<Dictionary<string, StringBuilder>> paths,
            bool truncated,
            int renderedBlocks,
            int selectedBlocks,
            string visualizationMode)
        {
            var pathLength = paths.Sum(groups => groups.Values.Sum(path => path.Length));
            var builder = new StringBuilder(Math.Min(MaximumSvgCharacters, pathLength + 12000));
            builder.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(SvgWidth)
                .Append("\" height=\"").Append(SvgHeight).Append("\" viewBox=\"0 0 ").Append(SvgWidth).Append(' ').Append(SvgHeight)
                .Append("\" role=\"img\" aria-labelledby=\"title description\">");
            builder.Append("<title id=\"title\">Voidwright agent wireframe: ").Append(Xml(model.AgentId)).Append("</title>");
            builder.Append("<desc id=\"description\">Three orthographic projections, captured at simulation tick ")
                .Append(model.CapturedSimulationTick.ToString(CultureInfo.InvariantCulture)).Append(". Rendered ")
                .Append(renderedBlocks).Append(" blocks; selected ").Append(selectedBlocks).Append("; ")
                .Append(truncated ? "geometry truncated" : "complete geometry").Append(". Mode: ")
                .Append(Xml(visualizationMode)).Append(". ")
                .Append(visualizationMode == "damage"
                    ? (model.SourceSnapshotComplete
                        ? "Colors represent current observed integrity only; no historical destruction is inferred."
                        : "Incomplete coverage: omitted block state is unknown; colors represent current observed integrity only and no historical destruction is inferred.")
                    : "Structural geometry only.")
                .Append("</desc>");
            builder.Append("<rect width=\"100%\" height=\"100%\" fill=\"").Append(BackgroundColor).Append("\"/>");
            builder.Append("<text x=\"20\" y=\"30\" fill=\"#E7EDF5\" font-family=\"sans-serif\" font-size=\"18\" font-weight=\"700\">VOIDWRIGHT BODY · ")
                .Append(Xml(model.AgentId)).Append("</text>");
            builder.Append("<text x=\"20\" y=\"50\" fill=\"#A5B2C3\" font-family=\"sans-serif\" font-size=\"12\">tick ")
                .Append(model.CapturedSimulationTick.ToString(CultureInfo.InvariantCulture)).Append(" · ").Append(renderedBlocks).Append("/")
                .Append(model.Blocks.Count).Append(" blocks · ").Append(selectedBlocks).Append(" selected · ")
                .Append(visualizationMode == "damage" ? "DAMAGE MAP" : "STRUCTURAL MAP")
                .Append(truncated ? " · TRUNCATED" : "").Append("</text>");

            foreach (var projection in projections)
            {
                builder.Append("<g data-view=\"").Append(projection.Name).Append("\">");
                builder.Append("<rect x=\"").Append(projection.X).Append("\" y=\"").Append(projection.Y)
                    .Append("\" width=\"").Append(projection.Width).Append("\" height=\"").Append(projection.Height)
                    .Append("\" rx=\"10\" fill=\"").Append(PanelColor).Append("\" stroke=\"#35465A\"/>");
                builder.Append("<text x=\"").Append(projection.X + 16).Append("\" y=\"").Append(projection.Y + 24)
                    .Append("\" fill=\"#F2F5F9\" font-family=\"sans-serif\" font-size=\"16\" font-weight=\"700\">")
                    .Append(projection.Name.ToUpperInvariant()).Append("</text>");
                builder.Append("<text x=\"").Append(projection.X + projection.Width - 16).Append("\" y=\"").Append(projection.Y + 24)
                    .Append("\" text-anchor=\"end\" fill=\"#8796A8\" font-family=\"sans-serif\" font-size=\"10\">")
                    .Append(projection.HorizontalLabel).Append(" / ").Append(projection.VerticalLabel).Append("</text>");
                foreach (var path in paths[projections.IndexOf(projection)].OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
                {
                    var selected = path.Key.StartsWith("selected:", StringComparison.Ordinal);
                    var color = path.Key.Substring(path.Key.IndexOf(':') + 1);
                    builder.Append("<path d=\"").Append(path.Value).Append("\" fill=\"").Append(color)
                        .Append("\" fill-opacity=\"").Append(selected ? "0.18" : "0.035")
                        .Append("\" stroke=\"").Append(color).Append("\" stroke-width=\"").Append(selected ? "1.8" : "0.8")
                        .Append("\" stroke-linejoin=\"round\" vector-effect=\"non-scaling-stroke\"/>");
                }
                builder.Append("<text x=\"").Append(projection.X + projection.Width / 2).Append("\" y=\"")
                    .Append(projection.Y + projection.Height - 12).Append("\" text-anchor=\"middle\" fill=\"#9EACBC\" font-family=\"sans-serif\" font-size=\"10\">")
                    .Append(projection.HorizontalLabel).Append(" →  ·  ↑ ").Append(projection.VerticalLabel).Append("</text></g>");
            }
            if (visualizationMode == "damage")
            {
                AppendLegendItem(builder, 180, HealthyColor, "HEALTHY + FUNCTIONAL");
                AppendLegendItem(builder, 470, DamagedColor, "DAMAGED + FUNCTIONAL");
                AppendLegendItem(builder, 790, CriticalColor, "NONFUNCTIONAL");
                AppendLegendItem(builder, 1060, UnknownIntegrityColor, "INTEGRITY UNKNOWN");
            }
            builder.Append("</svg>");
            if (builder.Length > MaximumSvgCharacters)
                throw new InvalidOperationException("Wireframe output exceeded its hard character limit.");
            return builder.ToString();
        }

        private static void AppendLegendItem(StringBuilder builder, int x, string color, string label)
        {
            builder.Append("<rect x=\"").Append(x).Append("\" y=\"490\" width=\"10\" height=\"10\" rx=\"2\" fill=\"")
                .Append(color).Append("\"/><text x=\"").Append(x + 15)
                .Append("\" y=\"499\" fill=\"#A5B2C3\" font-family=\"sans-serif\" font-size=\"9\">")
                .Append(Xml(label)).Append("</text>");
        }

        private static string Xml(string value)
        {
            if (value == null) return "";
            return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                .Replace("\"", "&quot;").Replace("'", "&apos;");
        }

        private static string Format(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
