import bpy
import sys
import os


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for block in bpy.data.meshes:
        bpy.data.meshes.remove(block)
    for block in bpy.data.materials:
        bpy.data.materials.remove(block)


def import_model(path):
    ext = os.path.splitext(path)[1].lower()
    if ext == ".fbx":
        patch_fbx_importer()
        bpy.ops.import_scene.fbx(filepath=path)
    elif ext == ".obj":
        bpy.ops.import_scene.obj(filepath=path)
    elif ext == ".gltf" or ext == ".glb":
        bpy.ops.import_scene.gltf(filepath=path)
    else:
        raise RuntimeError("Unsupported extension: " + ext)


def export_glb(path):
    # Prefer new Blender 4.x API, fallback for older versions.
    export_kwargs = dict(
        filepath=path,
        export_format="GLB",
        export_apply=True,
        export_yup=True,
        export_texcoords=True,
        export_normals=True,
        export_materials="EXPORT",
        export_cameras=False,
        export_lights=False,
    )
    try:
        bpy.ops.export_scene.gltf(**export_kwargs)
        return
    except TypeError:
        pass

    export_kwargs.pop("export_apply", None)
    export_kwargs.pop("export_yup", None)
    bpy.ops.export_scene.gltf(
        filepath=path,
        export_format="GLB",
        export_texcoords=True,
        export_normals=True,
        export_materials="EXPORT",
        export_cameras=False,
        export_lights=False,
    )


def patch_fbx_importer():
    try:
        import io_scene_fbx.import_fbx as import_fbx
    except Exception:
        return
    if getattr(import_fbx, "_rtwin_patched", False):
        return

    original = import_fbx.blen_read_light

    def patched(*args, **kwargs):
        try:
            return original(*args, **kwargs)
        except AttributeError as ex:
            text = str(ex)
            if "CyclesLightSettings" in text or "cast_shadow" in text:
                return None
            raise

    import_fbx.blen_read_light = patched
    import_fbx._rtwin_patched = True


def main():
    argv = sys.argv
    if "--" not in argv:
        raise SystemExit("Usage: blender --background --python blender_to_glb.py -- <input> <output>")
    idx = argv.index("--")
    user_args = argv[idx + 1 :]
    if len(user_args) < 2:
        raise SystemExit("Usage: blender --background --python blender_to_glb.py -- <input> <output>")
    input_path = user_args[0]
    output_path = user_args[1]

    clear_scene()
    import_model(input_path)
    try:
        export_glb(output_path)
    except Exception as ex:
        raise RuntimeError("GLB export failed: " + str(ex))

    if not os.path.exists(output_path):
        raise RuntimeError("GLB export did not create output file: " + output_path)
    print("Exported:", output_path)


if __name__ == "__main__":
    main()
