import time
import os
import sys

# Paths
PROJECT_ROOT = r"c:\BASE\ROBOTWIN-STUDIO\robotwin-studio"
LOG_FILE = os.path.join(PROJECT_ROOT, "logs", "unity_live_error.log")

def main():
    print(f"Neural Link Active. Monitoring: {LOG_FILE}")
    print("Waiting for Unity Errors...")

    # Ensure file exists
    if not os.path.exists(LOG_FILE):
        try:
            os.makedirs(os.path.dirname(LOG_FILE), exist_ok=True)
            with open(LOG_FILE, 'w') as f:
                f.write("[MONITOR START]\n")
        except:
            pass

    # Tail logic
    with open(LOG_FILE, 'r') as f:
        # Go to end
        f.seek(0, 2)
        
        while True:
            line = f.readline()
            if not line:
                time.sleep(0.1)
                continue
            
            # Print everything that comes in, highlighting errors
            val = line.strip()
            if "[ERROR]" in val or "[EXCEPTION]" in val or "[ASSERT]" in val:
                print("\n\033[91m================ RED ALERT ================\033[0m")
                print(f"\033[96m{line.strip()}\033[0m")
            elif len(val) > 0:
                 print(line.strip())

if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        print("\nLink Terminated.")
