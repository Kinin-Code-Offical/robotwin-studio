import os
import urllib.request
import shutil

# Config
ICONS_DIR = r"c:\BASE\ROBOTWIN-STUDIO\robotwin-studio\UnityApp\Assets\UI\Icons"
ICON_MAP = {
    "home.png": "https://img.icons8.com/ios-glyphs/64/ffffff/home.png",
    "folder.png": "https://img.icons8.com/ios-glyphs/64/ffffff/folder-invoices.png",
    "settings.png": "https://img.icons8.com/ios-glyphs/64/ffffff/gear.png",
    "plus.png": "https://img.icons8.com/ios-glyphs/64/ffffff/plus-math.png",
    "search.png": "https://img.icons8.com/ios-glyphs/64/ffffff/search--v1.png",
    "menu.png": "https://img.icons8.com/ios-glyphs/64/ffffff/menu-vertical.png",
    "cpu.png": "https://img.icons8.com/ios-glyphs/64/ffffff/processor.png",
    "box.png": "https://img.icons8.com/ios-glyphs/64/ffffff/box.png",
    "activity.png": "https://img.icons8.com/ios-glyphs/64/ffffff/activity-history.png",
    "import.png": "https://img.icons8.com/ios-glyphs/64/ffffff/import.png",
    "copy.png": "https://img.icons8.com/ios-glyphs/64/ffffff/copy.png",
    "trash-2.png": "https://img.icons8.com/ios-glyphs/64/ffffff/trash.png",
    "filter.png": "https://img.icons8.com/ios-glyphs/64/ffffff/filter.png",
    "layout-template.png": "https://img.icons8.com/ios-glyphs/64/ffffff/layout.png",
    "more-vertical.png": "https://img.icons8.com/ios-glyphs/64/ffffff/menu-vertical.png",
    "arrow-right.png": "https://img.icons8.com/ios-glyphs/64/ffffff/arrow.png",
    "hexagon.png": "https://img.icons8.com/ios-glyphs/64/ffffff/hexagon.png",
    "zap.png": "https://img.icons8.com/ios-glyphs/64/ffffff/lightning-bolt.png",
    "clock.png": "https://img.icons8.com/ios-glyphs/64/ffffff/clock.png",
    "external-link.png": "https://img.icons8.com/ios-glyphs/64/ffffff/external-link.png"
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
