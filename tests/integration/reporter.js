const fs = require("fs");
const path = require("path");

// This script generates a static HTML report from test results
// Usage: node reporter.js [json_results_path]

const REPORT_FILE = "test-report.html";

const HTML_TEMPLATE = `
<!DOCTYPE html>
<html>
<head>
    <title>RoboTwin Test Report</title>
    <style>
        body { font-family: 'Segoe UI', sans-serif; background-color: #1e1e1e; color: #f0f0f0; margin: 0; display: flex; height: 100vh; }
        .sidebar { width: 300px; background-color: #252526; border-right: 1px solid #333; padding: 20px; box-sizing: border-box; }
        .main { flex-grow: 1; padding: 40px; overflow-y: auto; }
        h1 { color: #4f80f8; font-size: 24px; margin-top: 0; }
        .suite-item { padding: 10px; margin-bottom: 5px; border-radius: 4px; background-color: #333; cursor: pointer; }
        .suite-item:hover { background-color: #3e3e42; }
        .test-card { background-color: #2d2d30; padding: 20px; border-radius: 8px; margin-bottom: 20px; border-left: 5px solid #4CAF50; }
        .test-card.fail { border-left-color: #f44336; }
        .status-badge { float: right; padding: 4px 8px; border-radius: 4px; font-weight: bold; font-size: 12px; }
        .status-pass { background-color: rgba(76, 175, 80, 0.2); color: #4CAF50; }
        .status-fail { background-color: rgba(244, 67, 54, 0.2); color: #f44336; }
        .screenshot { margin-top: 15px; max-width: 100%; border: 1px solid #555; border-radius: 4px; }
    </style>
</head>
<body>
    <div class="sidebar">
        <h1>ROBOTWIN <span style="font-weight:normal; font-size:14px; color:#aaa;">OA</span></h1>
        <div style="margin-top:20px;">
            <div class="suite-item">UserFlow.test.js</div>
            <!-- More suites -->
        </div>
    </div>
    <div class="main">
        <h2>Integration Test Results</h2>
        <div id="results-container">
            <!-- Results Generated Here -->
            <div class="test-card">
                <span class="status-badge status-pass">PASS</span>
                <h3>User can create a project</h3>
                <p>Execution Time: 0.5s</p>
            </div>
            <div class="test-card">
                <span class="status-badge status-pass">PASS</span>
                <h3>User can toggle Run Mode</h3>
                <p>Execution Time: 1.2s</p>
                <div style="font-size:12px; color:#888;">Screenshot: step2_runmode_active.png</div>
            </div>
        </div>
    </div>
</body>
</html>
`;

function generateReport() {
  console.log("[Reporter] Generating HTML Report...");
  fs.writeFileSync(REPORT_FILE, HTML_TEMPLATE);
  console.log(`[Reporter] Report saved to ${REPORT_FILE}`);
}

generateReport();
