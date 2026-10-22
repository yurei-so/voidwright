using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

#pragma warning disable CS8618, CS8625

namespace Voidwright.ServerPlugin
{
    public sealed class DrivingLabPassiveConsentRequest
    {
        public bool ExplicitOptIn;
        public long AuthenticatedPlayerIdentityId;
        public string SessionId;
        public string RunId;
        public long GridEntityId;
        public DateTime ConfirmedAtUtc;
        public DateTime ExpiresAtUtc;
        public int MaximumSampleRateHz;
        public int MaximumBufferedRecords;
        public int MaximumBufferedBytes;
    }

    public sealed class DrivingLabPassiveIdentityInventory
    {
        public string SessionId;
        public string RunId;
        public DateTime CapturedAtUtc;
        public long SimulationTick;
        public bool Complete;
        public bool IdentityVerified;
        public IReadOnlyList<long> GridEntityIds;
        public IReadOnlyList<long> ConnectedHumanIdentityIds;
        public long OccupantGridEntityId;
        public IReadOnlyList<long> GridOccupantIdentityIds;
        public bool ControllerInventoryComplete;
        public IReadOnlyList<long> GridControllerHumanIdentityIds;
    }

    public sealed class DrivingLabPassiveTelemetrySelection
    {
        public string SessionId { get; internal set; }
        public string RunId { get; internal set; }
        public long GridEntityId { get; internal set; }
        public long PlayerIdentityId { get; internal set; }
        public DateTime ExpiresAtUtc { get; internal set; }
        public int MaximumSampleRateHz { get; internal set; }
        public int MaximumBufferedRecords { get; internal set; }
        public int MaximumBufferedBytes { get; internal set; }
        public string PermissionScope { get { return "read_only_observation"; } }
        public bool IsRevoked { get; private set; }
        public void Revoke() { IsRevoked = true; }
    }

    public enum DrivingLabPassiveTelemetryRejection
    {
        None,
        MissingOptIn,
        InvalidIdentity,
        ExpiredConsent,
        InvalidLimits,
        StaleInventory,
        IncompleteInventory,
        PlayerMismatch,
        MultipleOccupants,
        OccupantMismatch,
        OtherHumanControlsGrid,
        GridMismatch,
        Revoked
    }

    /// <summary>Pure, offline policy boundary shared by the host adapter and tests.</summary>
    public static class DrivingLabPassiveTelemetryPolicy
    {
        public const int MaximumSampleRateHz = 10;
        public const int MaximumBufferedRecords = 100000;
        public const int MaximumBufferedBytes = 64 * 1024 * 1024;
        public static readonly TimeSpan MaximumConsentLifetime = TimeSpan.FromMinutes(15);
        public static readonly TimeSpan MaximumInventoryAge = TimeSpan.FromSeconds(5);

        public static DrivingLabPassiveTelemetryRejection TryBind(
            DrivingLabPassiveConsentRequest request,
            DrivingLabPassiveIdentityInventory inventory,
            DateTime nowUtc,
            out DrivingLabPassiveTelemetrySelection selection)
        {
            selection = null;
            if (request == null || inventory == null || inventory.GridEntityIds == null ||
                inventory.ConnectedHumanIdentityIds == null || inventory.GridOccupantIdentityIds == null ||
                inventory.GridControllerHumanIdentityIds == null ||
                !Identity(request.SessionId) || !Identity(request.RunId) || request.GridEntityId <= 0 ||
                request.AuthenticatedPlayerIdentityId <= 0 || !Identity(inventory.SessionId) || !Identity(inventory.RunId))
                return DrivingLabPassiveTelemetryRejection.InvalidIdentity;
            if (!request.ExplicitOptIn) return DrivingLabPassiveTelemetryRejection.MissingOptIn;
            var now = nowUtc.ToUniversalTime();
            if (request.ConfirmedAtUtc.Kind != DateTimeKind.Utc || request.ExpiresAtUtc.Kind != DateTimeKind.Utc ||
                request.ConfirmedAtUtc > now || request.ExpiresAtUtc <= now ||
                request.ExpiresAtUtc <= request.ConfirmedAtUtc ||
                request.ExpiresAtUtc - request.ConfirmedAtUtc > MaximumConsentLifetime)
                return DrivingLabPassiveTelemetryRejection.ExpiredConsent;
            if (request.MaximumSampleRateHz < 1 || request.MaximumSampleRateHz > MaximumSampleRateHz ||
                request.MaximumBufferedRecords < 1 || request.MaximumBufferedRecords > MaximumBufferedRecords ||
                request.MaximumBufferedBytes < 1024 || request.MaximumBufferedBytes > MaximumBufferedBytes)
                return DrivingLabPassiveTelemetryRejection.InvalidLimits;
            if (inventory.CapturedAtUtc.Kind != DateTimeKind.Utc || inventory.CapturedAtUtc > now ||
                now - inventory.CapturedAtUtc > MaximumInventoryAge || inventory.SimulationTick < 0)
                return DrivingLabPassiveTelemetryRejection.StaleInventory;
            if (!inventory.Complete || !inventory.IdentityVerified || inventory.GridEntityIds.Count == 0)
                return DrivingLabPassiveTelemetryRejection.IncompleteInventory;
            if (inventory.SessionId != request.SessionId || inventory.RunId != request.RunId ||
                inventory.GridEntityIds.Any(id => id <= 0) ||
                inventory.GridEntityIds.Distinct().Count() != inventory.GridEntityIds.Count)
                return DrivingLabPassiveTelemetryRejection.GridMismatch;
            if (inventory.GridEntityIds.Count(id => id == request.GridEntityId) != 1)
                return DrivingLabPassiveTelemetryRejection.GridMismatch;
            if (inventory.ConnectedHumanIdentityIds.Count == 0 ||
                inventory.ConnectedHumanIdentityIds.Any(id => id <= 0) ||
                inventory.ConnectedHumanIdentityIds.Distinct().Count() != inventory.ConnectedHumanIdentityIds.Count)
                return DrivingLabPassiveTelemetryRejection.InvalidIdentity;
            if (inventory.OccupantGridEntityId != request.GridEntityId)
                return DrivingLabPassiveTelemetryRejection.OccupantMismatch;
            if (inventory.GridOccupantIdentityIds.Count != 1)
                return DrivingLabPassiveTelemetryRejection.MultipleOccupants;
            if (!inventory.ControllerInventoryComplete || inventory.GridControllerHumanIdentityIds.Any(id => id <= 0))
                return DrivingLabPassiveTelemetryRejection.IncompleteInventory;
            if (inventory.GridControllerHumanIdentityIds.Count != 1)
                return DrivingLabPassiveTelemetryRejection.OtherHumanControlsGrid;

            selection = new DrivingLabPassiveTelemetrySelection
            {
                SessionId = request.SessionId,
                RunId = request.RunId,
                GridEntityId = request.GridEntityId,
                PlayerIdentityId = request.AuthenticatedPlayerIdentityId,
                ExpiresAtUtc = request.ExpiresAtUtc,
                MaximumSampleRateHz = request.MaximumSampleRateHz,
                MaximumBufferedRecords = request.MaximumBufferedRecords,
                MaximumBufferedBytes = request.MaximumBufferedBytes
            };
            return DrivingLabPassiveTelemetryRejection.None;
        }

        public static DrivingLabPassiveTelemetryRejection ValidateCurrentBinding(
            DrivingLabPassiveTelemetrySelection selection,
            DrivingLabPassiveIdentityInventory inventory,
            DateTime nowUtc)
        {
            if (selection == null || inventory == null || selection.IsRevoked)
                return DrivingLabPassiveTelemetryRejection.Revoked;
            if (nowUtc.ToUniversalTime() >= selection.ExpiresAtUtc)
                return DrivingLabPassiveTelemetryRejection.ExpiredConsent;
            if (!inventory.Complete || !inventory.IdentityVerified || inventory.ConnectedHumanIdentityIds == null ||
                inventory.GridOccupantIdentityIds == null || inventory.GridEntityIds == null ||
                inventory.GridControllerHumanIdentityIds == null || !inventory.ControllerInventoryComplete ||
                inventory.CapturedAtUtc.Kind != DateTimeKind.Utc || inventory.CapturedAtUtc > nowUtc.ToUniversalTime() ||
                nowUtc.ToUniversalTime() - inventory.CapturedAtUtc > MaximumInventoryAge)
                return DrivingLabPassiveTelemetryRejection.IncompleteInventory;
            if (inventory.SessionId != selection.SessionId || inventory.RunId != selection.RunId ||
                inventory.OccupantGridEntityId != selection.GridEntityId ||
                inventory.GridEntityIds.Count(id => id == selection.GridEntityId) != 1)
                return DrivingLabPassiveTelemetryRejection.GridMismatch;
            if (inventory.ConnectedHumanIdentityIds.Count == 0 ||
                inventory.ConnectedHumanIdentityIds.Any(id => id <= 0) ||
                inventory.ConnectedHumanIdentityIds.Distinct().Count() != inventory.ConnectedHumanIdentityIds.Count)
                return DrivingLabPassiveTelemetryRejection.InvalidIdentity;
            if (inventory.GridOccupantIdentityIds.Count != 1)
                return DrivingLabPassiveTelemetryRejection.MultipleOccupants;
            if (inventory.GridControllerHumanIdentityIds.Any(id => id <= 0))
                return DrivingLabPassiveTelemetryRejection.IncompleteInventory;
            return inventory.GridControllerHumanIdentityIds.Count != 1
                ? DrivingLabPassiveTelemetryRejection.OtherHumanControlsGrid
                : DrivingLabPassiveTelemetryRejection.None;
        }

        public static bool CanBuffer(
            DrivingLabPassiveTelemetrySelection selection,
            int currentRecordCount,
            int currentByteCount,
            int nextRecordByteCount)
        {
            return selection != null && currentRecordCount >= 0 && currentByteCount >= 0 &&
                   nextRecordByteCount > 0 && currentRecordCount < selection.MaximumBufferedRecords &&
                   currentByteCount <= selection.MaximumBufferedBytes - nextRecordByteCount;
        }

        private static bool Identity(string value)
        {
            return value != null && value.Length == 32 && value.All(character =>
                character >= '0' && character <= '9' || character >= 'a' && character <= 'f');
        }
    }

    public sealed class DrivingLabPassiveWheelTelemetry
    {
        public long EntityId;
        public bool? BrakingEnabled;
        public bool? ActiveBrake;
        public bool? Handbrake;
        public double? SteeringOverride;
        public double? PropulsionOverride;
    }

    public sealed class DrivingLabPassiveTelemetryFrame
    {
        public DateTime CapturedAtUtc;
        public long SimulationTick;
        public double SimulationTimeSeconds;
        public long? CockpitEntityId;
        public string CockpitDefinitionType;
        public string CockpitSubtypeId;
        public double PositionX;
        public double PositionY;
        public double PositionZ;
        public double RightX;
        public double RightY;
        public double RightZ;
        public double UpX;
        public double UpY;
        public double UpZ;
        public double ForwardX;
        public double ForwardY;
        public double ForwardZ;
        public double VelocityX;
        public double VelocityY;
        public double VelocityZ;
        public double SpeedMetersPerSecond;
        public IReadOnlyList<DrivingLabPassiveWheelTelemetry> WheelStates;
    }

    /// <summary>Produces the bounded v1 JSONL telemetry shape without game API dependencies.</summary>
    public static class DrivingLabPassiveTelemetryRecordFormatter
    {
        public static string Format(
            DrivingLabPassiveTelemetrySelection selection,
            DrivingLabPassiveTelemetryFrame frame,
            long sequence,
            long droppedRecordsTotal)
        {
            if (selection == null || frame == null || sequence <= 0 || droppedRecordsTotal < 0 ||
                frame.CapturedAtUtc.Kind != DateTimeKind.Utc || frame.SimulationTick < 0 ||
                !Finite(frame.SimulationTimeSeconds) || frame.SimulationTimeSeconds < 0 ||
                !Finite(frame.SpeedMetersPerSecond) || frame.SpeedMetersPerSecond < 0)
                throw new ArgumentException("Passive telemetry frame is invalid.");

            var wheelSection = frame.WheelStates == null
                ? "{\"status\":\"unavailable\",\"reason\":\"wheel_state_unavailable\",\"value\":null}"
                : "{\"status\":\"observed\",\"reason\":null,\"value\":{\"ownership\":\"observed_only_no_control\",\"items\":[" +
                    string.Join(",", frame.WheelStates.Where(wheel => wheel != null).Select(WheelJson)) + "]}}";
            var cockpitSection = frame.CockpitEntityId.HasValue && frame.CockpitEntityId.Value > 0 &&
                                 !string.IsNullOrWhiteSpace(frame.CockpitDefinitionType) &&
                                 !string.IsNullOrWhiteSpace(frame.CockpitSubtypeId)
                ? "{\"status\":\"observed\",\"reason\":null,\"value\":{\"blockEntityId\":" +
                    frame.CockpitEntityId.Value.ToString(CultureInfo.InvariantCulture) +
                    ",\"definitionType\":" + Quote(frame.CockpitDefinitionType) +
                    ",\"subtypeId\":" + Quote(frame.CockpitSubtypeId) + "}}"
                : "{\"status\":\"unavailable\",\"reason\":\"occupied_cockpit_identity_unavailable\",\"value\":null}";

            return "{\"schemaVersion\":1,\"runId\":" + Quote(selection.RunId) +
                ",\"sessionId\":" + Quote(selection.SessionId) +
                ",\"streamId\":" + Quote("grid-" + selection.GridEntityId.ToString(CultureInfo.InvariantCulture)) +
                ",\"source\":\"server-passive-grid\",\"sequence\":" + sequence.ToString(CultureInfo.InvariantCulture) +
                ",\"capturedAtUtc\":" + Quote(frame.CapturedAtUtc.ToString("O", CultureInfo.InvariantCulture)) +
                ",\"clockDomain\":\"server-simulation\",\"simulationTick\":" + frame.SimulationTick.ToString(CultureInfo.InvariantCulture) +
                ",\"simulationTimeSeconds\":" + Number(frame.SimulationTimeSeconds) +
                ",\"gridEntityId\":" + selection.GridEntityId.ToString(CultureInfo.InvariantCulture) +
                ",\"droppedRecordsTotal\":" + droppedRecordsTotal.ToString(CultureInfo.InvariantCulture) +
                ",\"driverSeat\":" + cockpitSection +
                ",\"humanInput\":{\"status\":\"unavailable\",\"reason\":\"human_intent_unavailable_server_indicators_mix_autopilot\",\"value\":null}" +
                ",\"actuators\":" + wheelSection +
                ",\"physics\":{\"status\":\"observed\",\"reason\":null,\"value\":{\"velocityX\":" + Number(frame.VelocityX) +
                ",\"velocityY\":" + Number(frame.VelocityY) + ",\"velocityZ\":" + Number(frame.VelocityZ) +
                ",\"speedMetersPerSecond\":" + Number(frame.SpeedMetersPerSecond) + ",\"simulationRatio\":null,\"physicsStepCount\":null}}" +
                ",\"pose\":{\"status\":\"observed\",\"reason\":null,\"value\":{\"positionX\":" + Number(frame.PositionX) +
                ",\"positionY\":" + Number(frame.PositionY) + ",\"positionZ\":" + Number(frame.PositionZ) +
                ",\"rightX\":" + Number(frame.RightX) + ",\"rightY\":" + Number(frame.RightY) + ",\"rightZ\":" + Number(frame.RightZ) +
                ",\"upX\":" + Number(frame.UpX) + ",\"upY\":" + Number(frame.UpY) + ",\"upZ\":" + Number(frame.UpZ) +
                ",\"forwardX\":" + Number(frame.ForwardX) + ",\"forwardY\":" + Number(frame.ForwardY) + ",\"forwardZ\":" + Number(frame.ForwardZ) + "}}" +
                ",\"gridState\":{\"status\":\"unavailable\",\"reason\":\"grid_state_not_sampled\",\"value\":null}" +
                ",\"softwareGeometry\":{\"status\":\"unavailable\",\"reason\":\"geometry_not_sampled_by_passive_grid_producer\",\"value\":null}" +
                ",\"visualFrame\":{\"status\":\"unavailable\",\"reason\":\"framebuffer_capture_unavailable\",\"value\":null}}";
        }

        private static string WheelJson(DrivingLabPassiveWheelTelemetry wheel)
        {
            var steering = wheel.SteeringOverride;
            var propulsion = wheel.PropulsionOverride;
            var active = steering.HasValue && Math.Abs(steering.Value) > 1e-9 ||
                         propulsion.HasValue && Math.Abs(propulsion.Value) > 1e-9;
            return "{\"entityId\":" + wheel.EntityId.ToString(CultureInfo.InvariantCulture) +
                ",\"kind\":\"wheel\",\"enabled\":null,\"overrideActive\":" + (active ? "true" : "false") +
                ",\"overrideValue\":null,\"brakeEnabled\":" + Boolean(wheel.BrakingEnabled) +
                ",\"activeBrake\":" + Boolean(wheel.ActiveBrake) + ",\"handbrake\":" + Boolean(wheel.Handbrake) +
                ",\"steeringOverride\":" + Number(steering) + ",\"propulsionOverride\":" + Number(propulsion) + "}";
        }

        private static string Quote(string value)
        {
            var output = new StringBuilder("\"");
            foreach (var character in value ?? string.Empty)
            {
                switch (character)
                {
                    case '"': output.Append("\\\""); break;
                    case '\\': output.Append("\\\\"); break;
                    case '\n': output.Append("\\n"); break;
                    case '\r': output.Append("\\r"); break;
                    case '\t': output.Append("\\t"); break;
                    default: if (character < 0x20) output.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture)); else output.Append(character); break;
                }
            }
            return output.Append('"').ToString();
        }

        private static string Number(double value)
        {
            if (!Finite(value)) throw new ArgumentException("Passive telemetry contains a non-finite number.");
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string Number(double? value) { return value.HasValue ? Number(value.Value) : "null"; }
        private static string Boolean(bool? value) { return value.HasValue ? (value.Value ? "true" : "false") : "null"; }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
#pragma warning restore CS8618, CS8625
