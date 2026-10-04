using System;
using System.IO;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using VRage.Game.Entity;
using VRage.Plugins;
using VRage.Utils;
using VRageMath;

namespace Voidwright.ServerPlugin
{
    /// <summary>
    /// Privileged actuator host for disposable VRageCage fixtures. This initial
    /// slice proves Magnetar can compile the required wheel-system surface; it
    /// deliberately applies no command until the lab contract is observed live.
    /// </summary>
    public sealed class Plugin : IPlugin
    {
        private bool enabled;
        private long controllerId;
        private string operationId;
        private double distanceMeters;
        private double maxSpeed;
        private int timeoutTicks;
        private int elapsedTicks;
        private MyShipController controller;
        private Vector3D startPosition;
        private bool started;
        private bool terminal;

        public void Init(object gameInstance)
        {
            enabled = ValidateLabEnvironment();
            if (!enabled)
                throw new InvalidOperationException(
                    "Voidwright Server Plugin requires an activated VRageCage destructive-lab run.");
            MyLog.Default.WriteLineAndConsole(
                "Voidwright server plugin: destructive-lab authority verified; actuator inactive.");
            ReadCommand();
        }

        public void Update()
        {
            if (!enabled || terminal || controllerId == 0 || MySession.Static == null) return;
            elapsedTicks++;
            if (!started && !TryAcquireController())
            {
                if (elapsedTicks >= timeoutTicks) Finish("controller-unavailable", 0);
                return;
            }
            if (controller == null || controller.MarkedForClose || controller.GridWheels == null)
            {
                Finish("controller-lost", Displacement());
                return;
            }

            var displacement = Displacement();
            if (displacement >= distanceMeters)
            {
                Finish("complete", displacement);
                return;
            }
            if (elapsedTicks >= timeoutTicks)
            {
                Finish("timeout", displacement);
                return;
            }

            controller.GridWheels.Brake = false;
            var speed = controller.CubeGrid.Physics == null
                ? 0 : controller.CubeGrid.Physics.LinearVelocity.Length();
            var throttle = speed >= maxSpeed ? 0f : -1f;
            controller.GridWheels.AngularVelocity = new Vector3(0, 0, throttle);
            if (elapsedTicks % 60 == 0)
                Evidence("running", displacement, speed);
        }

        public void Dispose()
        {
            Neutralize();
            if (enabled)
                MyLog.Default.WriteLineAndConsole("Voidwright server plugin: disposed.");
            enabled = false;
        }

        private static bool ValidateLabEnvironment()
        {
            var authority = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_AUTHORITY");
            var runId = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_RUN_ID");
            var manifest = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_MANIFEST");
            var quarantine = Environment.GetEnvironmentVariable("VRAGECAGE_QUARANTINE");
            return authority == "destructive-lab" && IsHexIdentity(runId) &&
                   !string.IsNullOrWhiteSpace(manifest) && File.Exists(manifest) &&
                   !string.IsNullOrWhiteSpace(quarantine) && Directory.Exists(quarantine) &&
                   string.Equals(Path.GetFileName(quarantine), runId, StringComparison.Ordinal);
        }

        private static bool IsHexIdentity(string value)
        {
            if (value == null || value.Length != 32) return false;
            foreach (var character in value)
                if (!Uri.IsHexDigit(character)) return false;
            return true;
        }

        private void ReadCommand()
        {
            if (Environment.GetEnvironmentVariable("VRAGECAGE_LAB_COMMAND") != "drive-distance")
                return;
            operationId = Environment.GetEnvironmentVariable("VRAGECAGE_LAB_OPERATION_ID");
            long parsedController;
            double parsedDistance;
            double parsedSpeed;
            int parsedTicks;
            if (!IsHexIdentity(operationId) ||
                !long.TryParse(Environment.GetEnvironmentVariable("VRAGECAGE_LAB_CONTROLLER_ID"), out parsedController) ||
                !double.TryParse(Environment.GetEnvironmentVariable("VRAGECAGE_LAB_DISTANCE"), out parsedDistance) ||
                !double.TryParse(Environment.GetEnvironmentVariable("VRAGECAGE_LAB_MAX_SPEED"), out parsedSpeed) ||
                !int.TryParse(Environment.GetEnvironmentVariable("VRAGECAGE_LAB_TIMEOUT_TICKS"), out parsedTicks) ||
                parsedController <= 0 || parsedDistance < 0.25 || parsedDistance > 25 ||
                parsedSpeed < 0.1 || parsedSpeed > 5 || parsedTicks < 60 || parsedTicks > 3600)
                throw new InvalidOperationException("Invalid bounded VRageCage lab command.");
            controllerId = parsedController;
            distanceMeters = parsedDistance;
            maxSpeed = parsedSpeed;
            timeoutTicks = parsedTicks;
            MyLog.Default.WriteLineAndConsole(
                "Voidwright evidence: type=server-control operation=" + operationId +
                " controllerId=" + controllerId + " status=accepted");
        }

        private bool TryAcquireController()
        {
            MyEntity entity;
            if (!MyEntities.TryGetEntityById(controllerId, out entity)) return false;
            controller = entity as MyShipController;
            if (controller == null || controller.CubeGrid == null || controller.CubeGrid.IsStatic ||
                controller.CubeGrid.Physics == null || controller.GridWheels == null ||
                controller.GridWheels.WheelCount <= 0)
            {
                Finish("unsupported-controller", 0);
                return false;
            }
            startPosition = controller.PositionComp.GetPosition();
            started = true;
            controller.GridWheels.Brake = false;
            Evidence("acquired", 0, controller.CubeGrid.Physics.LinearVelocity.Length());
            return true;
        }

        private double Displacement()
        {
            return controller == null ? 0 : Vector3D.Distance(startPosition, controller.PositionComp.GetPosition());
        }

        private void Finish(string status, double displacement)
        {
            Neutralize();
            terminal = true;
            var speed = controller == null || controller.CubeGrid == null || controller.CubeGrid.Physics == null
                ? 0 : controller.CubeGrid.Physics.LinearVelocity.Length();
            Evidence(status, displacement, speed);
        }

        private void Neutralize()
        {
            if (controller != null && !controller.MarkedForClose && controller.GridWheels != null)
            {
                controller.GridWheels.AngularVelocity = Vector3.Zero;
                controller.GridWheels.Brake = true;
            }
        }

        private void Evidence(string status, double displacement, double speed)
        {
            MyLog.Default.WriteLineAndConsole(
                "Voidwright evidence: type=server-control operation=" + operationId +
                " controllerId=" + controllerId + " status=" + status +
                " elapsedTicks=" + elapsedTicks +
                " displacement=" + Math.Round(displacement, 3) +
                " speed=" + Math.Round(speed, 3));
        }

        // Compile-only compatibility probe. Magnetar plugins run outside Keen's
        // mod-script whitelist and must be able to reach this exact surface before
        // we add live actuation. It is never called in this slice.
        private static void ProveWheelSystemAccess(MyShipController controller)
        {
            if (controller != null && controller.GridWheels != null)
                controller.GridWheels.AngularVelocity = Vector3.Zero;
        }
    }
}
