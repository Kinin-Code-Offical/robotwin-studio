import requests
import sys
import json
from colorama import init, Fore, Style

# Initialize Colorama
init()

UNITY_URL = "http://localhost:8085"
COMMANDS = {
    'screenshot': 'Take a screenshot',
    'reset': 'Reset the current scene',
    'run-tests': 'Trigger generic test runner hook',
    'status': 'Ping the server',
    'quit': 'Exit console',
    'help': 'Show this list'
}

def print_header():
    print(Fore.CYAN + "="*40)
    print(Fore.CYAN + "   ROBOTWIN UNITY CONSOLE")
    print(Fore.CYAN + "="*40 + Style.RESET_ALL)

def send_command(endpoint):
    try:
        url = f"{UNITY_URL}/{endpoint}"
        print(f"{Fore.YELLOW}Sending request to {url}...{Style.RESET_ALL}")
        res = requests.get(url, timeout=2)
        
        if res.status_code == 200:
            print(f"{Fore.GREEN}SUCCESS: {res.text}{Style.RESET_ALL}")
        else:
            print(f"{Fore.RED}ERROR {res.status_code}: {res.text}{Style.RESET_ALL}")
            
    except requests.exceptions.ConnectionError:
        print(f"{Fore.RED}CONNECTION ERROR: Unity is probably offline.{Style.RESET_ALL}")
    except Exception as e:
        print(f"{Fore.RED}ERROR: {e}{Style.RESET_ALL}")

def main():
    print_header()
    print(f"Target: {UNITY_URL}\n")
    
    while True:
        try:
            cmd = input(f"{Fore.BLUE}rt-console>{Style.RESET_ALL} ").strip().lower()
        except KeyboardInterrupt:
            print("\nExiting...")
            break
        
        if not cmd:
            continue
            
        if cmd == 'quit' or cmd == 'exit':
            break
            
        if cmd == 'help':
            for k, v in COMMANDS.items():
                print(f"  {Fore.GREEN}{k.ljust(12)}{Style.RESET_ALL} : {v}")
            continue
            
        if cmd == 'status':
            send_command('query?target=ping')
            continue

        if cmd in ['screenshot', 'reset', 'run-tests']:
            send_command(cmd)
        else:
            # Try as generic command?
            # print(f"Unknown command: {cmd}")
            # Assume it's a generic action or query if advanced syntax used?
            # For now just simple map.
            send_command(cmd)

if __name__ == "__main__":
    main()
