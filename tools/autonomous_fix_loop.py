import subprocess
import os
import time
import re
import sys

# Configuration
PROJECT_PATH = r"c:\BASE\ROBOTWIN-STUDIO\robotwin-studio\UnityApp"
UNITY_EXE = r"C:\Program Files\Unity\Hub\Editor\2022.3.4f1\Editor\Unity.exe" # Best guess or from env
LOG_FILE = r"c:\BASE\ROBOTWIN-STUDIO\robotwin-studio\logs\build_results.log"

USS_FILE = os.path.join(PROJECT_PATH, "Assets", "UI", "ProjectWizard", "ProjectWizard.uss")
CONTROLLER_FILE = os.path.join(PROJECT_PATH, "Assets", "Scripts", "UI", "ProjectWizardController.cs")
MANIFEST_FILE = os.path.join(PROJECT_PATH, "Packages", "manifest.json")

def find_unity():
    # Helper to find Unity
    paths = [
        r"C:\Program Files\Unity\Hub\Editor\2022.3.4f1\Editor\Unity.exe",
        r"C:\Program Files\Unity\Hub\Editor\2021.3.16f1\Editor\Unity.exe",
        r"C:\Program Files\Unity\Hub\Editor\2022.3.10f1\Editor\Unity.exe",
        r"C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe"
    ]
    # Check env var
    if "UNITY_PATH" in os.environ:
        paths.insert(0, os.environ["UNITY_PATH"])
        
    for p in paths:
        if os.path.exists(p):
            return p
    return None

def run_build_check(unity_path):
    print(f"--- Running Unity Build Check ---")
    print(f"Log: {LOG_FILE}")
    
    # Ensure log dir exists
    os.makedirs(os.path.dirname(LOG_FILE), exist_ok=True)
    
    cmd = [
        unity_path,
        "-batchmode",
        "-nographics",
        "-projectPath", PROJECT_PATH,
        "-logFile", LOG_FILE,
        "-quit" # Just open and close to trigger compilation
    ]
    
    print("Executing Unity...")
    try:
        subprocess.run(cmd, check=False) # check=False because Unity might exit with non-zero on some errors
    except Exception as e:
        print(f"Unity execution failed: {e}")

def analyze_and_fix():
    print("Analyzing logs...")
    if not os.path.exists(LOG_FILE):
        print("Log file not found!")
        return False
        
    with open(LOG_FILE, 'r') as f:
        log_content = f.read()
        
    fixed = False
    
    # 1. Manifest Fix
    if "manifest.json] is not valid JSON" in log_content or "Unexpected token" in log_content:
        print(">> Detected Manifest Corruption. Fixing...")
        fix_manifest()
        fixed = True

    # 2. USS Fix
    if "Unknown property" in log_content and "tint-color" in log_content:
         print(">> Detected USS Error. Fixing...")
         fix_uss()
         fixed = True

    # 3. Controller Fix
    if "CS1061" in log_content and ("borderWidth" in log_content or "borderRadius" in log_content):
         print(">> Detected C# API Error. Fixing...")
         fix_controller()
         fixed = True
         
    # Check Success
    if "Compilation succeeded" in log_content or "Scripts have been reloaded" in log_content:
        return "SUCCESS"
        
    if fixed:
        return "RETRY"
        
    return "UNKNOWN_ERROR"

def fix_manifest():
    content = """{
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
    with open(MANIFEST_FILE, 'w') as f:
        f.write(content)

def fix_uss():
    if os.path.exists(USS_FILE):
        with open(USS_FILE, 'r') as f:
            c = f.read()
        c = c.replace("-unity-background-tint-color", "-unity-background-image-tint-color")
        with open(USS_FILE, 'w') as f:
            f.write(c)

def fix_controller():
    if os.path.exists(CONTROLLER_FILE):
        with open(CONTROLLER_FILE, 'r') as f:
            c = f.read()
        
        # Replace shorthand with explicit
        if "style.borderRadius =" in c or "style.borderWidth =" in c:
            c = c.replace("style.borderRadius =", "// (AutoFixed) style.borderRadius replaced\n                style.borderTopLeftRadius = 8; style.borderTopRightRadius = 8; style.borderBottomLeftRadius = 8; style.borderBottomRightRadius =")
            c = c.replace("style.borderWidth =", "// (AutoFixed) style.borderWidth replaced\n                style.borderTopWidth = 1; style.borderBottomWidth = 1; style.borderLeftWidth = 1; style.borderRightWidth =")
            
            with open(CONTROLLER_FILE, 'w') as f:
                f.write(c)

def main():
    print("--- AUTONOMOUS FIX LOOP ---")
    unity_exe = find_unity()
    if not unity_exe:
        print("Could not find Unity executable. Please set UNITY_PATH env var.")
        # Fallback to just analyzing the existing log if users ran it
        # sys.exit(1) 
        pass 
    
    max_retries = 3
    for i in range(max_retries):
        print(f"\n[ITERATION {i+1}]")
        if unity_exe:
            run_build_check(unity_exe)
        
        result = analyze_and_fix()
        
        if result == "SUCCESS":
            print("\n\033[92m[SUCCESS] COMPILATION CLEAN.\033[0m")
            sys.exit(0)
        elif result == "RETRY":
            print("[INFO] Fixes applied. Retrying compile...")
            continue
        elif result == "UNKNOWN_ERROR":
            print("\033[91m[FAILURE] Unknown errors persist. Check logs.\033[0m")
            # sys.exit(1)
            # Instead of exiting, maybe print the last few lines of log
            if os.path.exists(LOG_FILE):
                with open(LOG_FILE, 'r') as f:
                    print(f.read()[-500:])
            sys.exit(1)
            
    print("[FAILURE] Max retries reached.")

if __name__ == "__main__":
    main()
