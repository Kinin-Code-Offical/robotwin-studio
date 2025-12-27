import subprocess
import os
import sys
import time

UNITY_PATH = r"C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe"
PROJECT_PATH = os.path.abspath("UnityApp")
LOG_PATH = os.path.abspath("logs/build_check.log")

def run_unity():
    print(f"Starting Unity Build Check... Logs: {LOG_PATH}")
    if os.path.exists(LOG_PATH):
        os.remove(LOG_PATH)
    
    os.makedirs(os.path.dirname(LOG_PATH), exist_ok=True)

    cmd = [
        UNITY_PATH,
        "-batchmode",
        "-nographics",
        "-projectPath", PROJECT_PATH,
        "-logfile", LOG_PATH,
        "-quit"
    ]
    
    print("Executing Unity...")
    process = subprocess.Popen(cmd)
    
    # Wait for process to finish
    process.wait()
    print(f"Unity process exited with code {process.returncode}")
    
    return check_logs()

def check_logs():
    if not os.path.exists(LOG_PATH):
        print("Log file not found!")
        return False

    with open(LOG_PATH, 'r', encoding='utf-8', errors='ignore') as f:
        content = f.read()

    if "Batchmode quit successfully invoked" in content or "Compilation succeeded" in content:
        # Double check for actual errors
        has_errors = False
        lines = content.split('\n')
        for line in lines:
            if "error CS" in line and "Compiler message" not in line:
                has_errors = True
                print(f"[ERROR] {line.strip()}")
        
        if not has_errors:
            print("SUCCESS: Compilation succeeded (verified).")
            return True
    
    print("FAILURE: Compilation errors found or incomplete log.")
    
    return False

if __name__ == "__main__":
    success = run_unity()
    if success:
        sys.exit(0)
    else:
        sys.exit(1)
