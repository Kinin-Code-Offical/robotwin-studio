const express = require('express');
const cors = require('cors');
const path = require('path');
const fs = require('fs');
const axios = require('axios');
const chokidar = require('chokidar');
const http = require('http');

const app = express();
const PORT = 3000;
const UNITY_URL = 'http://localhost:8085';

// Keep-Alive Agent to prevent socket exhaustion
const keepAliveAgent = new http.Agent({ keepAlive: true });

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
        // Use the new /status endpoint with keepAlive agent
        const response = await axios.get(`${UNITY_URL}/status`, { 
            timeout: 1000,
            httpAgent: keepAliveAgent
        });
        
        res.json({ 
            connected: true, 
            engine: response.data.engine, 
            version: response.data.version 
        });
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
        const response = await axios.get(`${UNITY_URL}/${action}`, { 
            timeout: 5000,
            httpAgent: keepAliveAgent
        });
        res.status(response.status).send(response.data);
    } catch (error) {
        console.error(`[Dashboard] Unity Proxy Error: ${error.message}`);
        const code = error.code === 'ECONNREFUSED' ? 'UNITY_OFFLINE' : 'UNITY_ERROR';
        res.status(502).json({ error: code, details: error.message });
    }
});

// List images (Optimized)
app.get('/api/images', (req, res) => {
    fs.readdir(SCREENSHOT_DIR, (err, files) => {
        if (err) {
            return res.status(500).json({ error: 'Failed to read screenshot directory' });
        }
        
        try {
            const images = files
                .filter(file => /\.(png|jpg|jpeg)$/i.test(file))
                .map(file => {
                    const filePath = path.join(SCREENSHOT_DIR, file);
                    try {
                        const stats = fs.statSync(filePath);
                        return {
                            name: file,
                            url: `/screenshots/${file}`,
                            time: stats.mtime.getTime()
                        };
                    } catch (e) { return null; }
                })
                .filter(x => x !== null)
                .sort((a, b) => b.time - a.time)
                .slice(0, 20); // LIMIT to top 20
                
            res.json(images);
        } catch (e) {
            res.status(500).json({ error: e.message });
        }
    });
});

// Watcher (Optional: Could use websockets to push updates, but polling is simpler for MVP)
const watcher = chokidar.watch(SCREENSHOT_DIR, { ignored: /^\./, persistent: true });
watcher.on('add', path => console.log(`[Dashboard] New Screenshot: ${path}`));

app.listen(PORT, () => {
    console.log(`[Mission Control] Dashboard running at http://localhost:${PORT}`);
    console.log(`[Mission Control] Serving screenshots from: ${SCREENSHOT_DIR}`);
});
