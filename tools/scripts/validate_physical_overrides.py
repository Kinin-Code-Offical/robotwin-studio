import json
import sys
import zipfile
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_SAMPLE = REPO_ROOT / "tools" / "fixtures" / "hf06_sample.rtcomp"


def fail(message: str) -> int:
    print(f"[physical-overrides] ERROR: {message}")
    return 1


def load_component_json(path: Path) -> dict:
    if not path.exists():
        raise FileNotFoundError(path)
    with zipfile.ZipFile(path, "r") as archive:
        if "component.json" not in archive.namelist():
            raise ValueError("component.json missing from package")
        raw = archive.read("component.json").decode("utf-8")
        return json.loads(raw)


def main() -> int:
    sample_path = Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_SAMPLE
    try:
        data = load_component_json(sample_path)
    except Exception as exc:
        return fail(f"Failed to read {sample_path}: {exc}")

    parts = data.get("parts") or []
    if not parts:
        return fail("Sample component.json has no parts[] entries")

    required = {
        "physicalMaterial",
        "densityKgPerM3",
        "massKg",
        "volumeM3",
        "friction",
        "elasticity",
        "strength",
    }
    for part in parts:
        missing = [key for key in required if key not in part]
        if missing:
            return fail(f"Part '{part.get('name', '(unknown)')}' missing keys: {', '.join(missing)}")

    body = next((p for p in parts if p.get("name") == "Body"), None)
    if body is None:
        return fail("Sample component missing 'Body' part")

    density = float(body.get("densityKgPerM3", 0.0))
    volume = float(body.get("volumeM3", 0.0))
    expected_mass = 0.27
    derived = density * volume
    if abs(derived - expected_mass) > 1e-6:
        return fail(f"Derived mass mismatch: {derived} != {expected_mass}")

    core = next((p for p in parts if p.get("name") == "Core"), None)
    if core is None:
        return fail("Sample component missing 'Core' part")
    if float(core.get("massKg", 0.0)) <= 0.0:
        return fail("Core part massKg should be > 0")

    print(f"[physical-overrides] OK: {sample_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
