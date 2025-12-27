import requests
import time
import os
import sys
import argparse

UNITY_URL = "http://localhost:8085"
LOG_FILE = r"c:\BASE\ROBOTWIN-STUDIO\robotwin-studio\logs\unity_live_error.log"

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--run-test", action="store_true", help="Trigger the AutoPilot Test")
    args = parser.parse_args()

    print("--- ROBO-TWIN NEURAL LINK (ROBUST) ---")
    
    # 1. Self-Healing Log
    if not os.path.exists(LOG_FILE):
        print(f"[INFO] Log file missing. Creating placeholder at {LOG_FILE}...")
        try:
            os.makedirs(os.path.dirname(LOG_FILE), exist_ok=True)
            with open(LOG_FILE, 'w') as f:
                f.write("[SYSTEM] Waiting for Unity...\n")
        except Exception as e:
            print(f"[ERROR] Could not create log file: {e}")
            sys.exit(1)

    # 2. Connection Logic (Non-blocking attempt or just check)
    print("Checking Unity Connection...")
    unity_online = False
    try:
        requests.get(f"{UNITY_URL}/query?target=ping", timeout=0.5)
        unity_online = True
        print("\033[92m[CONNECTED] Unity is Online.\033[0m")
    except:
        print("\033[93m[WARN] Unity not responding at localhost:8085.\033[0m")
        print(">> PLEASE OPEN UNITY PROJECT AND PRESS PLAY <<")

    # 3. Trigger Test (only if we think we can, or just try)
    if args.run_test:
        if unity_online:
             print(">>> INITIATING AUTO-PILOT SEQUENCE...")
             try:
                 requests.get(f"{UNITY_URL}/run-tests", timeout=1)
             except:
                 pass
        else:
             print("[INFO] Queuing Test Trigger... (Will try when Unity connects)")

    # 4. Monitor Loop
    print(f"Monitoring {LOG_FILE}...")
    
    last_trigger_attempt = 0
    
    with open(LOG_FILE, 'r') as f:
        f.seek(0, 2)
        while True:
            # Persistent Retry for Test Trigger if requested
            if args.run_test and not unity_online and (time.time() - last_trigger_attempt > 5):
                last_trigger_attempt = time.time()
                try:
                    requests.get(f"{UNITY_URL}/run-tests", timeout=0.5)
                    unity_online = True
                    print("\033[92m>>> UNITY CONNECTED! INITIALIZING TEST... <<<\033[0m")
                except:
                    pass

            line = f.readline()
            if not line:
                time.sleep(0.1)
                continue
            
            clean = line.strip()
            if "[AUTOPILOT]" in clean:
                print(f"\033[96m{clean}\033[0m") # Cyan
            
            if "TEST SEQUENCE COMPLETE: SUCCESS" in clean:
                print("\n\033[92m==============================================")
                print("   [SUCCESS] AUTO-PILOT MISSION ACCOMPLISHED")
                print("==============================================\033[0m")
                sys.exit(0)

            if "TEST FAILED" in clean or "Exception" in clean or "Error" in clean:
                if "TEST FAILED" in clean:
                     print(f"\n\033[91m[FAILURE] {clean}\033[0m")
                else: 
                     # Filter out benign logs if needed, but show errors
                     if "Error" in clean or "Exception" in clean:
                        print(f"[LOG] {clean}")

if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        print("\nLink Terminated.")
