import time
import os
import sys

# Constants
PROJECT_ROOT = r"c:\BASE\ROBOTWIN-STUDIO\robotwin-studio"
LOG_FILE = os.path.join(PROJECT_ROOT, "logs", "unity_live_error.log")
TRIGGER_FILE = os.path.join(PROJECT_ROOT, "Temp", "StartTest.trigger")

def main():
    print("--- ROBO-TWIN ZERO-TOUCH AUTOMATION ---")

    # 1. Reset Log
    # We want to catch new messages, not old ones.
    # But we can't delete the log if Unity holds a lock. 
    # Instead, we'll read from the current end of file.
    
    # 2. Set Trigger
    print(f"[1/3] Setting Trigger at {TRIGGER_FILE}...")
    try:
        os.makedirs(os.path.dirname(TRIGGER_FILE), exist_ok=True)
        with open(TRIGGER_FILE, 'w') as f:
            f.write("GO")
    except Exception as e:
        print(f"[ERROR] Could not write trigger: {e}")
        sys.exit(1)

    # 3. Monitor
    print("[2/3] Waiting for Auto-Pilot Response...")
    if not os.path.exists(LOG_FILE):
        print("[WARN] Log file missing. Is Unity running?")
        # Wait a bit just in case
        time.sleep(2)
    
    start_time = time.time()
    timeout = 30 # seconds
    
    if os.path.exists(LOG_FILE):
        with open(LOG_FILE, 'r') as f:
            f.seek(0, 2) # Go to execution start
            
            while (time.time() - start_time) < timeout:
                line = f.readline()
                if not line:
                    time.sleep(0.1)
                    continue
                
                clean = line.strip()
                
                # Feedback
                if "[TestBootstrapper]" in clean:
                    print(f"\033[94m[UNITY] {clean}\033[0m")
                if "[AUTOPILOT]" in clean:
                    print(f"\033[96m{clean}\033[0m")
                
                # Success/Fail
                if "[AUTOPILOT] SUCCESS" in clean:
                    print("\n\033[92m==============================================")
                    print("   [PASSED] ZERO-TOUCH CYCLE COMPLETED")
                    print("==============================================\033[0m")
                    sys.exit(0)
                
                if "[AUTOPILOT] FAILURE" in clean or "Exception" in clean:
                    if "[AUTOPILOT]" in clean:
                         print(f"\n\033[91m{clean}\033[0m")
                         sys.exit(1)
                    # Show exceptions but don't exit immediately unless critical?
                    # Actually, exception means likely fail.
                    if "Exception" in clean:
                        print(f"\033[91m[ERROR] {clean}\033[0m")

    print("\n\033[93m[TIMEOUT] Test did not complete in 30 seconds.\033[0m")
    sys.exit(1)

if __name__ == "__main__":
    main()
