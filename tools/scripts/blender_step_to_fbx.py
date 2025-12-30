import os
import sys

import addon_utils
import bpy


def _parse_args():
    argv = sys.argv
    if "--" in argv:
        argv = argv[argv.index("--") + 1 :]
    if len(argv) < 2:
        raise SystemExit("Usage: blender_step_to_fbx.py -- <input.step> <output.fbx> [scale]")
    source = os.path.abspath(argv[0])
    target = os.path.abspath(argv[1])
    scale = float(argv[2]) if len(argv) > 2 else 0.001
    return source, target, scale


def main():
    source, target, scale = _parse_args()
    os.makedirs(os.path.dirname(target), exist_ok=True)

    addon_utils.enable("import_step", default_set=True, persistent=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    addon_utils.enable("import_step", default_set=True, persistent=True)
    prefs = bpy.context.preferences.addons["import_step"].preferences
    prefs.freecad_path = r"C:\Program Files\FreeCAD 1.0\bin\freecadcmd.exe"
    bpy.ops.import_scene.step(filepath=source)

    bpy.ops.object.select_all(action="SELECT")
    for obj in bpy.context.selected_objects:
        obj.scale = (scale, scale, scale)

    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    bpy.ops.export_scene.fbx(
        filepath=target,
        use_selection=True,
        apply_unit_scale=True,
        bake_space_transform=True,
        add_leaf_bones=False,
    )


if __name__ == "__main__":
    main()
