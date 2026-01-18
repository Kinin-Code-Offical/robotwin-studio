import os
import re

texture_dir = r"c:\BASE\ROBOTWIN-STUDIO\robotwin-studio\RobotWin\Assets\Resources\Prefabs\Circuit3D\Textures"
prefab_dir = r"c:\BASE\ROBOTWIN-STUDIO\robotwin-studio\RobotWin\Assets\Resources\Prefabs\Circuit3D"

# Guid -> Filename
guid_to_path = {}

# 1. Parse Texture Metas
if os.path.exists(texture_dir):
    for filename in os.listdir(texture_dir):
        if filename.endswith(".mat.meta"):
            path = os.path.join(texture_dir, filename)
            with open(path, 'r') as f:
                content = f.read()
                if match := re.search(r"guid:\s*([a-f0-9]+)", content):
                    guid = match[1]
                    mat_name = filename.replace(".mat.meta", "")
                    guid_to_path[guid] = f"Prefabs/Circuit3D/Textures/{mat_name}"

# 2. Parse Prefab Metas
prefab_map = {}

if os.path.exists(prefab_dir):
    for filename in os.listdir(prefab_dir):
        if filename.endswith(".fbx.meta"):
            path = os.path.join(prefab_dir, filename)
            prefab_name = filename.replace(".fbx.meta", "")

            with open(path, 'r') as f:
                content = f.read()

                # regex to find name followed by guid within a few lines
                # Pattern:
                # - first:
                # ...
                #   name: mat0
                # second: {..., guid: ...}

                # We will iterate manually to maintain state
                lines = content.split('\n')
                current_mat_name = None

                prefab_map[prefab_name] = {}

                for line in lines:
                    if name_match := re.search(r"^\s+name:\s*(.+)$", line):
                        current_mat_name = name_match[1].strip()

                    # Look for "guid: ..." in the "second" block
                    if "second:" in line and "guid:" in line and current_mat_name:
                        if guid_match := re.search(
                            r"guid:\s*([a-f0-9]+)", line
                        ):
                            guid = guid_match[1]
                            if guid in guid_to_path:
                                prefab_map[prefab_name][current_mat_name] = guid_to_path[guid]
                        # Reset after processing the pair
                        current_mat_name = None

# 3. Generate C# Code
csharp_code = "        private static readonly Dictionary<string, Dictionary<string, string>> MaterialRemap = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)\n        {\n"

for prefab, mapping in prefab_map.items():
    if not mapping:
        continue
    csharp_code += f'            {{ "{prefab}", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {{\n'
    for mat_name, resource_path in mapping.items():
        csharp_code += f'                {{ "{mat_name}", "{resource_path}" }},\n'
    csharp_code += "            } },\n"

csharp_code += "        };\n"

print(csharp_code)
