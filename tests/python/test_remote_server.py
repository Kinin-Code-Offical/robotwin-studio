import unittest
import requests
import time
import os

UNITY_URL = "http://localhost:8085"

class TestRemoteServer(unittest.TestCase):
    
    def setUp(self):
        # Ensure Unity is reachable before running tests
        try:
            requests.get(f"{UNITY_URL}/query?target=ping", timeout=0.5)
        except requests.exceptions.ConnectionError:
            self.skipTest("Unity is not running at localhost:8085")

    def test_01_status_check(self):
        """Verify the server is responding to generic queries."""
        response = requests.get(f"{UNITY_URL}/query?target=ping")
        self.assertEqual(response.status_code, 200)

    def test_02_take_screenshot(self):
        """Trigger a screenshot and verify response."""
        response = requests.get(f"{UNITY_URL}/screenshot")
        self.assertEqual(response.status_code, 200)
        # Note: We can't easily verify the file exists locally unless we know the absolute path 
        # Unity is using relative to itself, but obtaining a 200 OK is a good sign.
        print(f"Screenshot Response: {response.text}")

    def test_03_query_runmode(self):
        """Query the #RunMode state."""
        response = requests.get(f"{UNITY_URL}/query?target=%23RunMode") # %23 is #
        self.assertEqual(response.status_code, 200)
        json_data = response.json()
        # Mock returns {"value": true}
        self.assertIn("value", json_data)

    def test_04_unknown_command(self):
        """Ensure 404 for unknown endpoints."""
        response = requests.get(f"{UNITY_URL}/made_up_command")
        self.assertEqual(response.status_code, 404)

if __name__ == '__main__':
    unittest.main()
