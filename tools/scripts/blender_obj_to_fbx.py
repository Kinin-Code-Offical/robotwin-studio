import os
import sys

import bpy


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for data in (bpy.data.meshes, bpy.data.materials, bpy.data.images, bpy.data.textures):
        for item in list(data):
            data.remove(item, do_unlink=True)


def import_obj(path):
    if hasattr(bpy.ops.wm, "obj_import"):
        bpy.ops.wm.obj_import(filepath=path)
    else:
        bpy.ops.import_scene.obj(filepath=path)


def apply_texture(texture_path):
    if not texture_path:
        return
    if not os.path.isfile(texture_path):
        print(f"Texture not found: {texture_path}")
        return

    image = bpy.data.images.load(texture_path)
    material = bpy.data.materials.new(name=os.path.splitext(os.path.basename(texture_path))[0])
    material.use_nodes = True

    nodes = material.node_tree.nodes
    links = material.node_tree.links
    bsdf = nodes.get("Principled BSDF")
    tex_node = nodes.new("ShaderNodeTexImage")
    tex_node.image = image
    if bsdf is not None:
        links.new(bsdf.inputs["Base Color"], tex_node.outputs["Color"])

    for obj in bpy.context.scene.objects:
        if obj.type != "MESH":
            continue
        if obj.data.materials:
            obj.data.materials[0] = material
        else:
            obj.data.materials.append(material)


def export_fbx(path):
    bpy.ops.export_scene.fbx(
        filepath=path,
        path_mode="COPY",
        embed_textures=True,
        add_leaf_bones=False,
        bake_space_transform=False,
        use_selection=False,
    )


def main():
    args = sys.argv
    if "--" not in args:
        print("Usage: blender --background --python blender_obj_to_fbx.py -- input.obj output.fbx [texture.png]")
        return 2
    idx = args.index("--")
    user_args = args[idx + 1 :]
    if len(user_args) < 2:
        print("Missing input/output arguments.")
        return 2

    input_obj = user_args[0]
    output_fbx = user_args[1]
    texture_path = user_args[2] if len(user_args) > 2 else ""

    clear_scene()
    import_obj(input_obj)
    apply_texture(texture_path)
    export_fbx(output_fbx)
    print(f"Exported: {output_fbx}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
