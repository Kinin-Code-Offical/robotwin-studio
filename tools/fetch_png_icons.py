import os
import urllib.request
import shutil
ICONS_EIGHT_BASE = "https://img.icons8.com/ios-glyphs/64/ffffff/"
# Config
ICONS_DIR = r"c:\BASE\ROBOTWIN-STUDIO\robotwin-studio\UnityApp\Assets\UI\Icons"
ICON_MAP = {
    "home.png": ICONS_EIGHT_BASE + "home.png",
    "folder.png": ICONS_EIGHT_BASE + "folder-invoices.png",
    "settings.png": ICONS_EIGHT_BASE + "gear.png",
    "plus.png": ICONS_EIGHT_BASE + "plus-math.png",
    "search.png": ICONS_EIGHT_BASE + "search--v1.png",
    "menu.png": ICONS_EIGHT_BASE + "menu-vertical.png",
    "cpu.png": ICONS_EIGHT_BASE + "processor.png",
    "box.png": ICONS_EIGHT_BASE + "box.png",
    "activity.png": ICONS_EIGHT_BASE + "activity-history.png",
    "import.png": ICONS_EIGHT_BASE + "import.png",
    "copy.png": ICONS_EIGHT_BASE + "copy.png",
    "trash-2.png": ICONS_EIGHT_BASE + "trash.png",
    "filter.png": ICONS_EIGHT_BASE + "filter.png",
    "layout-template.png": ICONS_EIGHT_BASE + "layout.png",
    "more-vertical.png": ICONS_EIGHT_BASE + "menu-vertical.png",
    "arrow-right.png": ICONS_EIGHT_BASE + "arrow.png",
    "hexagon.png": ICONS_EIGHT_BASE + "hexagon.png",
    "zap.png": ICONS_EIGHT_BASE + "lightning-bolt.png",
    "clock.png": ICONS_EIGHT_BASE + "clock.png",
    "external-link.png": ICONS_EIGHT_BASE + "external-link.png",
    "play.png": ICONS_EIGHT_BASE + "play--v1.png",
    "pause.png": ICONS_EIGHT_BASE + "pause--v1.png",
    "stop.png": ICONS_EIGHT_BASE + "stop.png",
    "step.png": ICONS_EIGHT_BASE + "end.png",
    "console.png": ICONS_EIGHT_BASE + "console.png",
    "inspector.png": ICONS_EIGHT_BASE + "info.png",
    "hierarchy.png": ICONS_EIGHT_BASE + "list.png"
}

def clean_and_fetch():
    # 1. Clean Directory
    if os.path.exists(ICONS_DIR):
        print(f"Cleaning {ICONS_DIR}...")
        for filename in os.listdir(ICONS_DIR):
            file_path = os.path.join(ICONS_DIR, filename)
            try:
                if os.path.isfile(file_path) or os.path.islink(file_path):
                    os.unlink(file_path)
                elif os.path.isdir(file_path):
                    shutil.rmtree(file_path)
            except Exception as e:
                print(f"Failed to delete {file_path}. Reason: {e}")
    else:
        os.makedirs(ICONS_DIR)

    # 2. Download Icons
    print("Downloading PNG icons...")
    opener = urllib.request.build_opener()
    opener.addheaders = [('User-agent', 'Mozilla/5.0')]
    urllib.request.install_opener(opener)

    for filename, url in ICON_MAP.items():
        dest = os.path.join(ICONS_DIR, filename)
        try:
            print(f"Fetching {filename} from {url}...")
            urllib.request.urlretrieve(url, dest)
        except Exception as e:
            print(f"Error downloading {filename}: {e}")

if __name__ == "__main__":
    clean_and_fetch()
