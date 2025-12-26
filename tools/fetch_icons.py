import os
import urllib.request

ICONS = [
    "hexagon", "home", "folder", "folder-open", "layout-template", "settings",
    "plus", "import", "copy", "trash-2", "external-link",
    "box", "cpu", "activity", "zap",
    "clock", "search", "filter", "more-vertical", "arrow-right"
]

# Path relative to the repository root (where the script is run from)
TARGET_DIR = os.path.join("UnityApp", "Assets", "UI", "Icons")
BASE_URL = "https://unpkg.com/lucide-static@latest/icons"

def main():
    # Ensure target directory exists
    if not os.path.exists(TARGET_DIR):
        print(f"Creating directory: {TARGET_DIR}")
        os.makedirs(TARGET_DIR)

    print(f"Starting download of {len(ICONS)} icons to '{TARGET_DIR}'...")

    for icon in ICONS:
        url = f"{BASE_URL}/{icon}.svg"
        dest = os.path.join(TARGET_DIR, f"{icon}.svg")
        try:
            print(f"Downloading {icon}.svg...", end=" ")
            urllib.request.urlretrieve(url, dest)
            print("OK")
        except Exception as e:
            print(f"FAILED: {e}")

    # Verification
    verification_file = os.path.join(TARGET_DIR, "hexagon.svg")
    if os.path.exists(verification_file):
        print(f"\nSUCCESS: Verification passed. '{verification_file}' exists.")
    else:
        print(f"\nERROR: Verification failed. '{verification_file}' was not found.")

if __name__ == "__main__":
    main()
