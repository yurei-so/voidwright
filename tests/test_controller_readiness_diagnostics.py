from pathlib import Path


source = (Path(__file__).resolve().parents[1] / "ServerPlugin" / "Plugin.cs").read_text()
start = source.index('checks.Add(Check("controller_readiness"')
end = source.index('checks.Add(Check("actuator_availability"', start)
evidence = source[start:end]

required_fields = (
    ";entityResolved=",
    ";closing=",
    ";exactGrid=",
    ";physics=",
    ";gridWheels=",
    ";controlApi=",
    ";operatorUnderControl=",
    ";flightMovement=",
    ";autopilotEnabled=",
    ";gridBrakeAvailable=",
    ";wheelBrakeStateAvailable=",
    ";brakeStatus=",
    ";controllerReady=",
)
missing = [field for field in required_fields if field not in evidence]
if missing:
    raise SystemExit("controller readiness evidence is missing: " + ", ".join(missing))
print("PASS controller readiness receipt exposes every host predicate input")
