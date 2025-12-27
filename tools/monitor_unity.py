import requests
import time
import sys
from datetime import datetime

UNITY_URL = "http://localhost:8085"

def get_status():
    try:
        t0 = time.time()
        requests.get(f"{UNITY_URL}/query?target=ping", timeout=0.5)
        latency = (time.time() - t0) * 1000
        return True, latency
    except requests.exceptions.ConnectionError:
        return False, 0
    except Exception:
        return False, 0

def main():
    print(f"Monitoring Unity at {UNITY_URL}...")
    print("Press Ctrl+C to stop.")
    
    last_status = None
    
    try:
        while True:
            online, latency = get_status()
            
            timestamp = datetime.now().strftime("%H:%M:%S")
            
            if online:
                status_str = f"ONLINE ({latency:.1f}ms)"
                color = "\033[92m" # Green
            else:
                status_str = "OFFLINE"
                color = "\033[91m" # Red
                
            reset = "\033[0m"
            
            # Print status line (overwrite same line if possible, or just log)
            # Simple logging for now
            if last_status != online:
                print(f"[{timestamp}] Status Change: {color}{status_str}{reset}")
            else:
                # Optional: specific heartbeat every 10s?
                # Just sleep fast
                pass
                
            last_status = online
            time.sleep(1)
            
    except KeyboardInterrupt:
        print("\nStopped.")

if __name__ == "__main__":
    main()
