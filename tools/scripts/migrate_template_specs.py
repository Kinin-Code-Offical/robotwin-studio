import argparse
import json
from pathlib import Path


def load_metadata(path: Path) -> dict:
    raw = json.loads(path.read_text(encoding="utf-8"))
    return raw if isinstance(raw, dict) else {}


def first_non_empty(*values: str) -> str:
    for value in values:
        if value and str(value).strip():
            return str(value).strip()
    return ""


def resolve_system_type(tag: str) -> str:
    tag_lower = (tag or "").lower()
    if "robot" in tag_lower:
        return "Robot"
    if "mechatronic" in tag_lower:
        return "Mechatronic"
    return "CircuitOnly"


def build_template_spec(folder: Path, metadata: dict) -> dict:
    display = first_non_empty(
        metadata.get("name"),
        metadata.get("Name"),
        metadata.get("displayName"),
        metadata.get("DisplayName"),
        metadata.get("title"),
        metadata.get("Title"),
    )
    description = first_non_empty(metadata.get("description"), metadata.get("Description"))
    tag = first_non_empty(metadata.get("tag"), metadata.get("Tag"))

    template_id = f"template.{folder.name}"
    system_type = resolve_system_type(tag)

    return {
        "TemplateId": template_id,
        "DisplayName": display,
        "Description": description,
        "SystemType": system_type,
        "ID": template_id,
        "Name": display,
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path("RobotWin/Assets/Templates"))
    parser.add_argument("--force", action="store_true", help="Overwrite existing template.json")
    args = parser.parse_args()

    root = args.root.resolve()
    if not root.exists():
        raise SystemExit(f"Template root not found: {root}")

    updated = 0
    for folder in root.iterdir():
        if not folder.is_dir():
            continue
        metadata_path = folder / "metadata.json"
        if not metadata_path.exists():
            continue
        data = load_metadata(metadata_path)
        spec = build_template_spec(folder, data)
        if not spec["DisplayName"]:
            continue
        output_path = folder / "template.json"
        if output_path.exists() and not args.force:
            continue
        output_path.write_text(json.dumps(spec, indent=2), encoding="utf-8")
        updated += 1

    print(f"Migrated {updated} template(s) under {root}")


if __name__ == "__main__":
    main()
