import subprocess
import os
import sys
import time
import signal

UNITY_PATH = r"C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe"
PROJECT_PATH = os.path.abspath("UnityApp")
LOG_PATH = os.path.abspath("logs/runtime_check.log")
TRIGGER_DIR = os.path.join(PROJECT_PATH, "Temp")
TRIGGER_FILE = os.path.join(TRIGGER_DIR, "StartTest.trigger")

def create_trigger():
    os.makedirs(TRIGGER_DIR, exist_ok=True)
    with open(TRIGGER_FILE, 'w') as f:
        f.write("go")
    print(f"Created trigger file at {TRIGGER_FILE}")

def tail_log_and_wait(process):
    print("Watching logs for [AUTOPILOT] status...")
    success_found = False
    start_time = time.time()
    
    # Wait loop
    while True:
        if process.poll() is not None:
             print("Unity exited prematurely.")
             break
        
        if time.time() - start_time > 300: # 5 minute timeout
            print("Timeout waiting for AutoPilot.")
            break

        if os.path.exists(LOG_PATH):
            try:
                with open(LOG_PATH, 'r', encoding='utf-8', errors='ignore') as f:
                    content = f.read()
                    if "[AUTOPILOT] SUCCESS" in content:
                        print("\n[AUTOPILOT] SUCCESS CONFIRMED!")
                        success_found = True
                        break
                    if "[AUTOPILOT] FAILURE" in content:
                         print("\n[AUTOPILOT] FAILED!")
                         break
            except Exception as e:
                pass # Ignore file read errors (locking etc)
        
        time.sleep(2)
    
    return success_found

def run_verification():
    # create_trigger() moved to after launch
    
    print(f"Starting Unity Runtime Check... Logs: {LOG_PATH}")
    if os.path.exists(LOG_PATH):
        os.remove(LOG_PATH)
    
    os.makedirs(os.path.dirname(LOG_PATH), exist_ok=True)

    cmd = [
        UNITY_PATH,
        "-batchmode",
        "-nographics",
        "-projectPath", PROJECT_PATH,
        "-logfile", LOG_PATH
        # No -quit, we kill it manually
    ]
    
    print("Launching Unity...")
    process = subprocess.Popen(cmd)
    
    print("Waiting 15 seconds for Unity to initialize...")
    time.sleep(15)
    create_trigger()
    
    success = tail_log_and_wait(process)
    
    print("Killing Unity process...")
    subprocess.call(["taskkill", "/F", "/T", "/PID", str(process.pid)])
    
    # Backup kill just in case
    subprocess.call(["taskkill", "/F", "/IM", "Unity.exe"])
    
    if success:
        print("PROJECT STABLE: ALL SYSTEMS GREEN")
        sys.exit(0)
    else:
        print("Runtime verification failed.")
        sys.exit(1)

if __name__ == "__main__":
    run_verification()
