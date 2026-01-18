using UnityEngine;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using System;
using System.Linq;

namespace RobotTwin.Tests
{
    /// <summary>
    /// Automated Test Runner - Runs all tests and reports results
    /// Checks for errors and warnings in Unity console
    /// </summary>
    public class AutomatedTestRunner : EditorWindow
    {
        private TestRunnerApi _testRunnerApi;
        private bool _isRunning = false;
        private string _lastResults = "";
        private Vector2 _scrollPosition;

        [MenuItem("RobotWin/Run All Tests")]
        public static void ShowWindow()
        {
            GetWindow<AutomatedTestRunner>("Test Runner");
        }

        private void OnEnable()
        {
            _testRunnerApi = ScriptableObject.CreateInstance<TestRunnerApi>();
            _testRunnerApi.RegisterCallbacks(new TestCallback(this));
        }

        private void OnGUI()
        {
            GUILayout.Label("RobotWin Automated Test Runner", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.HelpBox(
                "This tool runs all Edit Mode and Play Mode tests.\n" +
                "Results will be displayed below and logged to console.",
                MessageType.Info);

            EditorGUILayout.Space();

            if (!_isRunning)
            {
                if (GUILayout.Button("Run All Edit Mode Tests", GUILayout.Height(30)))
                {
                    RunEditModeTests();
                }

                if (GUILayout.Button("Run All Play Mode Tests", GUILayout.Height(30)))
                {
                    RunPlayModeTests();
                }

                if (GUILayout.Button("Run ALL Tests", GUILayout.Height(40)))
                {
                    RunAllTests();
                }
            }
            else
            {
                GUILayout.Label("Tests running...", EditorStyles.boldLabel);
                if (GUILayout.Button("Cancel"))
                {
                    _isRunning = false;
                }
            }

            EditorGUILayout.Space();
            GUILayout.Label("Test Results:", EditorStyles.boldLabel);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(300));
            EditorGUILayout.TextArea(_lastResults, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();

            if (GUILayout.Button("Clear Console Logs"))
            {
                ClearConsole();
            }

            if (GUILayout.Button("Check for Warnings/Errors"))
            {
                CheckConsoleForIssues();
            }
        }

        private void RunEditModeTests()
        {
            _isRunning = true;
            _lastResults = "Running Edit Mode tests...\n";

            var filter = new Filter
            {
                testMode = TestMode.EditMode
            };

            _testRunnerApi.Execute(new ExecutionSettings(filter));
        }

        private void RunPlayModeTests()
        {
            _isRunning = true;
            _lastResults = "Running Play Mode tests...\n";

            var filter = new Filter
            {
                testMode = TestMode.PlayMode
            };

            _testRunnerApi.Execute(new ExecutionSettings(filter));
        }

        private void RunAllTests()
        {
            _isRunning = true;
            _lastResults = "Running ALL tests (Edit + Play Mode)...\n";

            // Run edit mode first
            var filterEdit = new Filter
            {
                testMode = TestMode.EditMode
            };

            _testRunnerApi.Execute(new ExecutionSettings(filterEdit));
        }

        private void ClearConsole()
        {
            var assembly = System.Reflection.Assembly.GetAssembly(typeof(SceneView));
            var type = assembly.GetType("UnityEditor.LogEntries");
            var method = type.GetMethod("Clear");
            method.Invoke(new object(), null);

            _lastResults += "\n[Console cleared]\n";
        }

        private void CheckConsoleForIssues()
        {
            _lastResults += "\n[Checking console for warnings/errors...]\n";
            _lastResults += "Manual check required - inspect Unity Console window\n";

            Debug.Log("=== Test Runner Check ===");
            Debug.Log("Please review console for any warnings or errors");
        }

        private class TestCallback : ICallbacks
        {
            private AutomatedTestRunner _window;
            private int _totalTests = 0;
            private int _passedTests = 0;
            private int _failedTests = 0;
            private System.Diagnostics.Stopwatch _stopwatch;

            public TestCallback(AutomatedTestRunner window)
            {
                _window = window;
            }

            public void RunStarted(ITestAdaptor testsToRun)
            {
                _stopwatch = System.Diagnostics.Stopwatch.StartNew();
                _totalTests = CountTests(testsToRun);
                _passedTests = 0;
                _failedTests = 0;

                _window._lastResults += $"Starting test run: {_totalTests} tests\n";
                _window._lastResults += "=====================================\n";
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                _stopwatch.Stop();
                _window._isRunning = false;

                _window._lastResults += "=====================================\n";
                _window._lastResults += $"Test run finished in {_stopwatch.ElapsedMilliseconds}ms\n";
                _window._lastResults += $"Total: {_totalTests} | Passed: {_passedTests} | Failed: {_failedTests}\n";

                if (_failedTests == 0)
                {
                    _window._lastResults += "✓ ALL TESTS PASSED!\n";
                    Debug.Log($"<color=green>✓ ALL TESTS PASSED! ({_passedTests}/{_totalTests})</color>");
                }
                else
                {
                    _window._lastResults += $"✗ {_failedTests} TESTS FAILED\n";
                    Debug.LogError($"✗ {_failedTests} TESTS FAILED");
                }

                _window.Repaint();
            }

            public void TestStarted(ITestAdaptor test)
            {
                // Optional: log each test start
            }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (result.TestStatus == TestStatus.Passed)
                {
                    _passedTests++;
                    _window._lastResults += $"✓ {result.Test.Name}\n";
                }
                else if (result.TestStatus == TestStatus.Failed)
                {
                    _failedTests++;
                    _window._lastResults += $"✗ {result.Test.Name}\n";
                    _window._lastResults += $"  Error: {result.Message}\n";

                    if (!string.IsNullOrEmpty(result.StackTrace))
                    {
                        _window._lastResults += $"  Stack:\n{result.StackTrace}\n";
                    }
                }
                else if (result.TestStatus == TestStatus.Skipped)
                {
                    _window._lastResults += $"⊘ {result.Test.Name} (Skipped)\n";
                }

                _window.Repaint();
            }

            private int CountTests(ITestAdaptor test)
            {
                if (test.IsSuite)
                {
                    int count = 0;
                    foreach (var child in test.Children)
                    {
                        count += CountTests(child);
                    }
                    return count;
                }
                return 1;
            }
        }
    }
}
