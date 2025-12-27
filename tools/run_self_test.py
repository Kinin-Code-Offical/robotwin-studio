import requests
import time
import os
import sys

UNITY_URL = "http://localhost:8085"
LOG_FILE = r"c:\BASE\ROBOTWIN-STUDIO\robotwin-studio\logs\unity_live_error.log"

def tail_log(stop_event):
    if not os.path.exists(LOG_FILE):
        return

    with open(LOG_FILE, 'r') as f:
        f.seek(0, 2) # End
        while not stop_event.is_set():
            line = f.readline()
            if not line:
                time.sleep(0.1)
                continue
            
            clean_line = line.strip()
            print(f"[UNITY LOG] {clean_line}")

            if "TEST PASSED" in clean_line:
                print("\n\033[92m[SUCCESS] Smoke Test Passed!\033[0m")
                return True
            if "TEST FAILED" in clean_line:
                 print(f"\n\033[91m[FAILURE] {clean_line}\033[0m")
                 return False
    return False

def main():
    print("--- ROBO-TWIN AUTO-PILOT ---")
    
    # 1. Check Connection
    try:
        requests.get(f"{UNITY_URL}/query?target=ping", timeout=2)
        print("Unity is ONLINE.")
    except:
        print("Unity is OFFLINE. Please start the app first.")
        sys.exit(1)

    # 2. Trigger Test
    print("Triggering Smoke Test...")
    try:
        requests.get(f"{UNITY_URL}/run-tests", timeout=1)
    except:
        pass # Expect timeout or ignored response

    # 3. Monitor
    print(f"Watching {LOG_FILE}...")
    
    timeout = 10
    start_time = time.time()
    
    # Simple polling loop instead of thread for simplicity here
    if os.path.exists(LOG_FILE):
        with open(LOG_FILE, 'r') as f:
            f.seek(0, 2)
            while time.time() - start_time < timeout:
                line = f.readline()
                if not line:
                    time.sleep(0.1)
                    continue
                
                if "TEST PASSED" in line:
                    print(f"\033[92m{line.strip()}\033[0m")
                    sys.exit(0)
                if "TEST FAILED" in line:
                    print(f"\033[91m{line.strip()}\033[0m")
                    sys.exit(1)
                
    print("\033[93m[TIMEOUT] Test did not complete in time.\033[0m")
    sys.exit(1)

if __name__ == "__main__":
    main()
