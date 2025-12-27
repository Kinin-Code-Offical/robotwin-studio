import os
import sys
import subprocess
import shutil

ROOT = os.getcwd()
NATIVE_DIR = os.path.join(ROOT, "NativeEngine")
SRC_DIR = os.path.join(NATIVE_DIR, "src")
BUILD_DIR = os.path.join(NATIVE_DIR, "build")
UNITY_PLUGINS_DIR = os.path.join(ROOT, "UnityApp", "Assets", "Plugins", "x86_64")

def ensure_dirs():
    if not os.path.exists(BUILD_DIR):
        os.makedirs(BUILD_DIR)
    if not os.path.exists(UNITY_PLUGINS_DIR):
        os.makedirs(UNITY_PLUGINS_DIR)

def build_dll():
    print("[Polyglot] Building Native Engine DLL...")
    ensure_dirs()
    
    source = os.path.join(SRC_DIR, "main.cpp")
    output_dll = os.path.join(BUILD_DIR, "RoboTwinCore.dll")
    
    # Try generic compilation with g++ (MinGW) first as it's common in cross-platform dev envs
    # If not found, user might need to run this in VS Dev Cmd
    
    cmd = []
    # Check if we can just use cl (MSVC)
    # Using 'where' to check availability is tricky in python without shell=True, 
    # so we just try/except
    
    built = False
    
    # Attempt 1: g++
    try:
        print("  > Attempting build with g++...")
        # g++ -shared -o RoboTwinCore.dll main.cpp
        subprocess.check_call(["g++", "-shared", "-o", output_dll, source])
        print("  > g++ build successful.")
        built = True
    except (FileNotFoundError, subprocess.CalledProcessError):
        print("  > g++ failed or not found.")
    
    # Attempt 2: cl (MSVC)
    if not built:
        try:
            print("  > Attempting build with MSVC (cl.exe)...")
            # cl /LD main.cpp /Fe:RoboTwinCore.dll
            subprocess.check_call(["cl", "/LD", source, f"/Fe:{output_dll}"], cwd=BUILD_DIR) 
            # Note: cwd=BUILD_DIR to keep .obj files there
            print("  > MSVC build successful.")
            built = True
        except (FileNotFoundError, subprocess.CalledProcessError):
            print("  > cl.exe failed or not found. Ensure you are in a Developer Command Prompt.")

    if built:
        print("[Polyglot] Copying DLL to Unity Plugins...")
        dest = os.path.join(UNITY_PLUGINS_DIR, "RoboTwinCore.dll")
        shutil.copy2(output_dll, dest)
        print(f"  > Copied to {dest}")
    else:
        print("[Polyglot] ERROR: Could not build DLL. No valid compiler found.")

def main():
    print("=== RoboTwin Polyglot Builder ===")
    ensure_dirs()
    build_dll()
    print("\n=== Build Process Finished ===")

if __name__ == "__main__":
    main()
