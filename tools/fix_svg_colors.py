import os

ICONS_DIR = r"c:\BASE\ROBOTWIN-STUDIO\robotwin-studio\UnityApp\Assets\UI\Icons"

count = 0
for filename in os.listdir(ICONS_DIR):
    if filename.endswith(".svg"):
        path = os.path.join(ICONS_DIR, filename)
        with open(path, "r", encoding="utf-8") as f:
            content = f.read()
        
        if "currentColor" in content:
            # Replace currentColor with white (#FFFFFF) so Unity Importer can parse it
            new_content = content.replace("currentColor", "#FFFFFF")
            with open(path, "w", encoding="utf-8") as f:
                f.write(new_content)
            print(f"Fixed: {filename}")
            count += 1

print(f"Total files fixed: {count}")
