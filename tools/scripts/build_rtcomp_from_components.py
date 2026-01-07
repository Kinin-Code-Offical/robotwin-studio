import argparse
import json
import struct
import zipfile
from pathlib import Path


MAGIC = b"RTCOMP\0\0"
HEADER_SIZE = 24


def write_rtcomp(package_path: Path, component_json_text: str, assets: list[tuple[Path, str]]) -> None:
    package_path.parent.mkdir(parents=True, exist_ok=True)
    tmp_path = package_path.with_suffix(package_path.suffix + ".tmp")
    if tmp_path.exists():
        tmp_path.unlink()

    header = MAGIC + struct.pack("<4i", 1, HEADER_SIZE, HEADER_SIZE, 0)

    with tmp_path.open("wb") as file:
        file.write(header)
        with zipfile.ZipFile(file, mode="w", compression=zipfile.ZIP_DEFLATED) as zf:
            zf.writestr("component.json", component_json_text)
            for src_path, entry_name in assets:
                zf.write(src_path, entry_name.replace("\\", "/"))

    if package_path.exists():
        package_path.unlink()
    tmp_path.replace(package_path)


def load_component_definition(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def ensure_model_glb(step_venv_python: Path, model_path: Path) -> Path:
    ext = model_path.suffix.lower()
    if ext in (".glb", ".gltf"):
        return model_path

    if ext not in (".step", ".stp"):
        raise RuntimeError(f"Unsupported model extension: {model_path}")

    glb_path = model_path.with_suffix(".glb")
    if glb_path.exists() and glb_path.stat().st_size > 0:
        return glb_path

    script = Path("tools/scripts/step_to_glb.py")
    cmd = [str(step_venv_python), str(script), str(model_path), str(glb_path)]
    import subprocess

    res = subprocess.run(cmd, check=False, capture_output=True, text=True)
    if res.returncode != 0:
        raise RuntimeError(
            f"STEP->GLB failed for {model_path}\n"
            f"stdout:\n{res.stdout}\n"
            f"stderr:\n{res.stderr}\n"
        )
    return glb_path


def main() -> int:
    parser = argparse.ArgumentParser(description="Build .rtcomp packages from bundled component JSON and models.")
    parser.add_argument(
        "--venv-python",
        type=Path,
        default=Path(".venv_step/Scripts/python.exe"),
        help="Python executable in the cadquery-ocp venv used for STEP->GLB conversion.",
    )
    parser.add_argument(
        "--assets-root",
        type=Path,
        default=Path("RobotWin/Assets"),
        help="Unity Assets folder root.",
    )
    parser.add_argument(
        "--out-dir",
        type=Path,
        default=Path("RobotWin/Assets/StreamingAssets/Components"),
        help="Output directory for .rtcomp packages.",
    )
    args = parser.parse_args()

    venv_python: Path = args.venv_python
    if not venv_python.exists():
        raise SystemExit(
            f"Missing converter venv python: {venv_python}\n"
            f"Create it with: py -3.12 -m venv .venv_step; .venv_step\\Scripts\\python -m pip install cadquery-ocp==7.7.2"
        )

    assets_root: Path = args.assets_root
    components_dir = assets_root / "Resources" / "Components"
    models_dir = assets_root / "Resources" / "Prefabs" / "Circuit3D"

    # Explicit mapping for the newly added components that have physical models.
    # Values are model filenames under Resources/Prefabs/Circuit3D.
    component_to_model = {
        "battery_4xaa.json": "4xAA BATTERY HOLDER 1_5V.STEP",
        "dc_motor_25ga370.json": "Motor_25GA370-26x1-6V-320RPM.STEP",
        "ir_sensor_module.json": "IR YL-70.stp",
        "motor_shield_l293d_v1.json": "motor_shield_l293d.glb",
        "servo_mg90s.json": "Tower Pro MG90S Micro servo.STEP",
        "servo_sg90.json": "ServoSG90.glb",
        "tcs34725_module.json": "TCS34725 RGB sensor.STEP",
    }

    out_dir: Path = args.out_dir
    built = 0
    for json_name, model_name in component_to_model.items():
        json_path = components_dir / json_name
        if not json_path.exists():
            raise SystemExit(f"Missing component json: {json_path}")

        model_path = models_dir / model_name
        if not model_path.exists():
            raise SystemExit(f"Missing model file: {model_path}")

        glb_path = ensure_model_glb(venv_python, model_path)
        component_def = load_component_definition(json_path)
        component_def["modelFile"] = f"assets/{glb_path.name}"
        component_json_text = json.dumps(component_def, indent=2, ensure_ascii=False) + "\n"

        package_name = json_path.stem
        package_path = out_dir / f"{package_name}.rtcomp"

        assets = [(glb_path, f"assets/{glb_path.name}")]
        write_rtcomp(package_path, component_json_text, assets)
        built += 1

    print(f"Built {built} packages into {out_dir}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

