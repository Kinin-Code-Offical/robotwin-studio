import time
import os
import sys
import re

# Paths
PROJECT_ROOT = r"c:\BASE\ROBOTWIN-STUDIO\robotwin-studio"
MANIFEST_FILE = os.path.join(PROJECT_ROOT, "UnityApp", "Packages", "manifest.json")
USS_FILE = os.path.join(PROJECT_ROOT, "UnityApp", "Assets", "UI", "ProjectWizard", "ProjectWizard.uss")
CONTROLLER_FILE = os.path.join(PROJECT_ROOT, "UnityApp", "Assets", "Scripts", "UI", "ProjectWizardController.cs")
TRIGGER_FILE = os.path.join(PROJECT_ROOT, "UnityApp", "Assets", "Scripts", "UI", "ProjectWizardController.cs") # Touching script forces recompile

# Locate Unity Log (Default)
local_app_data = os.environ.get('LOCALAPPDATA', '')
DEFAULT_LOG = os.path.join(local_app_data, "Unity", "Editor", "Editor.log")

# Golden Manifest
GOLDEN_MANIFEST = """{
  "dependencies": {
    "com.unity.nuget.newtonsoft-json": "3.0.2",
    "com.unity.test-framework": "1.1.33",
    "com.unity.toolchain.win-x86_64-linux-x86_64": "2.0.11",
    "com.unity.modules.imgui": "1.0.0",
    "com.unity.modules.jsonserialize": "1.0.0",
    "com.unity.modules.physics": "1.0.0",
    "com.unity.modules.physics2d": "1.0.0",
    "com.unity.modules.screencapture": "1.0.0",
    "com.unity.modules.ui": "1.0.0",
    "com.unity.modules.uielements": "1.0.0"
  }
}"""

def touch_file(path):
    if os.path.exists(path):
        try:
            os.utime(path, None)
            print(f"👉 Touched {os.path.basename(path)} to trigger Unity recompile.")
        except:
            pass

def fix_manifest():
    print(f"\033[93m[DETECTED] Manifest Corruption. Injecting Golden Copy...\033[0m")
    try:
        with open(MANIFEST_FILE, 'w') as f:
            f.write(GOLDEN_MANIFEST)
        print(f"\033[92m[FIXED] manifest.json restored.\033[0m")
        touch_file(TRIGGER_FILE)
    except Exception as e:
        print(f"[ERROR] Fix failed: {e}")

def fix_uss():
    print(f"\033[93m[DETECTED] USS Style Error. Fixing properties...\033[0m")
    try:
        if os.path.exists(USS_FILE):
            with open(USS_FILE, 'r') as f:
                content = f.read()
            
            if "-unity-background-tint-color" in content:
                new_content = content.replace("-unity-background-tint-color", "-unity-background-image-tint-color")
                with open(USS_FILE, 'w') as f:
                    f.write(new_content)
                print(f"\033[92m[FIXED] USS properties updated.\033[0m")
                touch_file(TRIGGER_FILE)
    except Exception as e:
        print(f"[ERROR] Fix failed: {e}")

def fix_controller():
    print(f"\033[93m[DETECTED] C# API Error (borderRadius). Patching Controller...\033[0m")
    try:
        if os.path.exists(CONTROLLER_FILE):
            with open(CONTROLLER_FILE, 'r') as f:
                content = f.read()
            
            if "style.borderRadius =" in content:
                 new_content = content.replace("style.borderRadius =", "// style.borderRadius replaced\n                style.borderTopLeftRadius = 8;\n                style.borderTopRightRadius = 8;\n                style.borderBottomLeftRadius = 8;\n                style.borderBottomRightRadius =")
                 
                 with open(CONTROLLER_FILE, 'w') as f:
                    f.write(new_content)
                 print(f"\033[92m[FIXED] Controller API calls patched.\033[0m")
                 touch_file(TRIGGER_FILE)

    except Exception as e:
        print(f"[ERROR] Fix failed: {e}")

def main():
    print("--- FORENSIC DOCTOR [DEEP SCAN ACTIVE] ---")
    
    target_log = DEFAULT_LOG
    
    # Path resolution logic
    if not os.path.exists(target_log):
        # Fallback
        alt_log = os.path.expandvars(r"C:\Users\%USERNAME%\AppData\Local\Unity\Editor\Editor.log")
        if os.path.exists(alt_log):
             target_log = alt_log
        else:
            print(f"Targeting Native Log: {target_log}")
            print(f"\033[91m[ERROR] Unity Editor log not found at default locations.\033[0m")
            print("Please open Unity once to generate the log.")
            # We will try to loop anyway in case it appears
    
    print(f"Targeting Native Log: {target_log}")
    print("Scanning for compilation errors... (Ctrl+C to stop)")
    
    while True:
        if not os.path.exists(target_log):
             time.sleep(2)
             continue

        try:
            with open(target_log, 'r', encoding='utf-8', errors='ignore') as f:
                f.seek(0, 2) 
                
                while True:
                    line = f.readline()
                    if not line:
                        time.sleep(0.5)
                        continue
                    
                    if "error CS" in line or "Exception" in line or "Error" in line:
                         print(f"[UNITY RAW] {line.strip()}")

                    # 1. Manifest
                    if "manifest.json" in line and ("Parse error" in line or "json" in line or "Unexpected token" in line):
                         fix_manifest()

                    # 2. USS
                    if "Unknown property" in line and "tint-color" in line:
                         fix_uss()

                    # 3. Controller
                    if "CS1061" in line and ("borderWidth" in line or "borderRadius" in line):
                         fix_controller()
                    
                    if "Reloading assemblies" in line:
                        print("\033[96m[INFO] Unity is reloading assemblies...\033[0m")
        
        except Exception as e:
            print(f"[WARN] Log read error: {e}. Retrying...")
            time.sleep(1)

if __name__ == "__main__":
    main()
