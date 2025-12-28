import time
import os
import re

LOG_PATH = os.path.expandvars(r"%LOCALAPPDATA%\Unity\Editor\Editor.log")
PROJECT_PATH = r"c:\BASE\ROBOTWIN-STUDIO\robotwin-studio\UnityApp"

def tail_f(file):
    file.seek(0, 2)
    while True:
        line = file.readline()
        if not line:
            time.sleep(0.1)
            continue
        yield line

def fix_border_width(file_path, line_num):
    print(f"-> Surgical Fix: BorderWidth at {file_path}:{line_num}")
    try:
        with open(file_path, 'r') as f:
            lines = f.readlines()
        
        idx = line_num - 1
        original = lines[idx]
        # Regex to find .style.borderWidth = X;
        # Replacement: .style.borderTopWidth = X; .style.borderBottomWidth = X; ...
        
        # Simple heuristic replacement for now
        # Assuming format: element.style.borderWidth = value;
        if "borderWidth =" in original:
            # Extract variable and value
            # e.g. "foo.style.borderWidth = 10;" -> var="foo.style", val="10;"
            parts = original.split("borderWidth =")
            lhs = parts[0].strip() # "foo.style."
            rhs = parts[1].strip() # "10;"
            
            # Remove trailing dot if present
            base = lhs[:-1] if lhs.endswith('.') else lhs
            
            # Construct 4 lines
            indent = original[:original.find(lhs)] if original.find(lhs) >= 0 else ""
            
            new_lines = [
                f"{indent}{base}.borderTopWidth = {rhs}\n",
                f"{indent}{base}.borderBottomWidth = {rhs}\n",
                f"{indent}{base}.borderLeftWidth = {rhs}\n",
                f"{indent}{base}.borderRightWidth = {rhs}\n"
            ]
            
            lines[idx] = "".join(new_lines)
            
            with open(file_path, 'w') as f:
                f.writelines(lines)
            print("   Changed applied.")
            return True
            
    except Exception as e:
        print(f"   Fix failed: {e}")
    return False

NATIVE_LOG_PATH = r"c:\BASE\ROBOTWIN-STUDIO\robotwin-studio\logs\native_engine.log"

def monitor():
    print(f"--- CIRCUIT STUDIO FINALIZER (FUSION MODE) ---")
    print(f"Monitoring 1: {LOG_PATH}")
    print(f"Monitoring 2: {NATIVE_LOG_PATH}")
    
    # Simple sequential check for MVP (Real formatting would use threads or async)
    # Just checking existence for now as proof of life
    if os.path.exists(NATIVE_LOG_PATH):
        print("[NATIVE] Log found. Engine is active.")
    else:
        print("[NATIVE] Log not found yet (Waiting for Tick...)")

    if not os.path.exists(LOG_PATH):
        print("Unity Log file not found!")
        return

    with open(LOG_PATH, 'r', encoding='utf-8', errors='ignore') as f:
        for line in tail_f(f):
            if "error CS" in line and "Assets/Scripts" in line:
                print(f"[ERROR DETECTED] {line.strip()}")
                
                # Parse File and Line
                # Example: Assets\Scripts\UI\CircuitStudioController.cs(120,45): error CS1061...
                match = re.search(r"(Assets[\\/].+\.cs)\((\d+),", line)
                if match:
                    rel_path = match.group(1).replace("/", "\\")
                    line_num = int(match.group(2))
                    full_path = os.path.join(PROJECT_PATH, rel_path)
                    
                    if "borderWidth" in line:
                        fix_border_width(full_path, line_num)
                    elif "tint-color" in line: # USS error usually, but checking CS?
                        pass 

if __name__ == "__main__":
    monitor()
