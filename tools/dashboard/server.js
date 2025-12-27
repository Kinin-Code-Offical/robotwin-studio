const express = require('express');
const cors = require('cors');
const path = require('path');
const fs = require('fs');
const axios = require('axios');
const chokidar = require('chokidar');

const app = express();
const PORT = 3000;
const UNITY_URL = 'http://localhost:8085';

// Path to Unity Project Root (Up two levels from tools/dashboard)
const PROJECT_ROOT = path.resolve(__dirname, '../../');
const SCREENSHOT_DIR = path.join(PROJECT_ROOT, 'Screenshots');

app.use(cors());
app.use(express.static('public'));

// Ensure screenshot directory exists
if (!fs.existsSync(SCREENSHOT_DIR)) {
    fs.mkdirSync(SCREENSHOT_DIR, { recursive: true });
}

const TEST_DIR = path.resolve(__dirname, '../../tests/integration');

// Serve screenshots statically
app.use('/screenshots', express.static(SCREENSHOT_DIR));
// Serve test report
app.use('/report', express.static(TEST_DIR));

// Check Unity Status
app.get('/api/status', async (req, res) => {
    try {
        // Fast timeout ping
        await axios.get(`${UNITY_URL}/query?target=ping`, { timeout: 1000 });
        res.json({ connected: true });
    } catch (e) {
        res.json({ connected: false, error: e.code || e.message });
    }
});

// Proxy commands / Execute Tests
app.get('/api/command/:action', async (req, res) => {
    const action = req.params.action; // screenshot, run-tests, reset, generate-report
    console.log(`[Dashboard] Sending command: ${action}`);
    
    if (action === 'run-tests') {
        const { exec } = require('child_process');
        
        console.log(`[Dashboard] Running tests in: ${TEST_DIR}`);
        
        // Just run tests
        exec('npm test', { cwd: TEST_DIR }, (error, stdout, stderr) => {
            if (error) {
                console.error(`[Dashboard] Test Error: ${error.message}`);
                return res.json({ status: 'failed', output: stdout + "\n" + stderr });
            }
             return res.json({ status: 'success', output: stdout });
        });
        return; 
    }

    if (action === 'generate-report') {
        const { exec } = require('child_process');
        console.log(`[Dashboard] Generating Report in: ${TEST_DIR}`);
        
        exec('npm run report', { cwd: TEST_DIR }, (error, stdout, stderr) => {
            if (error) {
                console.error(`[Dashboard] Report Gen Error: ${error.message}`);
                return res.json({ status: 'failed', output: stderr });
            }
            // Return the URL where the report is served
            return res.json({ 
                status: 'success', 
                output: 'Report Generated!',
                url: '/report/test-report.html'
            });
        });
        return;
    }

    try {
        const response = await axios.get(`${UNITY_URL}/${action}`, { timeout: 5000 });
        res.status(response.status).send(response.data);
    } catch (error) {
        console.error(`[Dashboard] Unity Proxy Error: ${error.message}`);
        const code = error.code === 'ECONNREFUSED' ? 'UNITY_OFFLINE' : 'UNITY_ERROR';
        res.status(502).json({ error: code, details: error.message });
    }
});

// List images
app.get('/api/images', (req, res) => {
    fs.readdir(SCREENSHOT_DIR, (err, files) => {
        if (err) {
            return res.status(500).json({ error: 'Failed to read screenshot directory' });
        }
        // Filter for images and sort by modification time (newest first)
        const images = files
            .filter(file => /\.(png|jpg|jpeg)$/i.test(file))
            .map(file => {
                const filePath = path.join(SCREENSHOT_DIR, file);
                const stats = fs.statSync(filePath);
                return {
                    name: file,
                    url: `/screenshots/${file}`,
                    time: stats.mtime
                };
            })
            .sort((a, b) => b.time - a.time); // Newest first
            
        res.json(images);
    });
});

// Watcher (Optional: Could use websockets to push updates, but polling is simpler for MVP)
const watcher = chokidar.watch(SCREENSHOT_DIR, { ignored: /^\./, persistent: true });
watcher.on('add', path => console.log(`[Dashboard] New Screenshot: ${path}`));

app.listen(PORT, () => {
    console.log(`[Mission Control] Dashboard running at http://localhost:${PORT}`);
    console.log(`[Mission Control] Serving screenshots from: ${SCREENSHOT_DIR}`);
});
