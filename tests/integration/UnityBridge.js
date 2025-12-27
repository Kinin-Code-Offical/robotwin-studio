const ipc = require('node-ipc');

class UnityBridge {
    constructor() {
        this.config = {
            appspace: 'RoboTwin',
            id: 'TestRunner',
            retry: 1500,
            silent: true
        };
        ipc.config.id = this.config.id;
        ipc.config.retry = this.config.retry;
        ipc.config.silent = this.config.silent;
        
        this.connected = false;
        this.socket = null;
    }

    async connect() {
        return new Promise((resolve, reject) => {
            // Unity is the Server, we are the Client
            ipc.connectTo('FirmwareEngine', () => {
                ipc.of.FirmwareEngine.on('connect', () => {
                    console.log('[UnityBridge] Connected to Unity successfully.');
                    this.connected = true;
                    this.socket = ipc.of.FirmwareEngine;
                    resolve();
                });

                ipc.of.FirmwareEngine.on('disconnect', () => {
                    if (this.connected) {
                        console.log('[UnityBridge] Disconnected from Unity.');
                        this.connected = false;
                    }
                });
                
                ipc.of.FirmwareEngine.on('error', (err) => {
                    // console.error('[UnityBridge] Connection Error:', err);
                    // reject(err); // Typically we retry
                });
            });
            
            // Timeout if Unity is not running
            setTimeout(() => {
                if (!this.connected) {
                    // For mockup purposes: Resolve anyway to let tests run in "Mock Mode" if Unity isn't there
                    console.log('[UnityBridge] Connection timed out (Mock Mode activated).');
                    resolve(); 
                }
            }, 3000);
        });
    }

    async sendCommand(action, target) {
        console.log(`[UnityBridge] Sending Command: ${action} -> ${target}`);
        if(this.connected && this.socket) {
            this.socket.emit('message', JSON.stringify({ action, target }));
        }
        // Simulate delay for UI update
        return new Promise(r => setTimeout(r, 500));
    }

    async queryState(selector) {
        console.log(`[UnityBridge] Querying State: ${selector}`);
        // Mock Response for now
        if (selector === 'CurrentScene') return 'CircuitStudio';
        if (selector === '#RunMode') return true;
        return null;
    }

    async takeScreenshot(filename) {
        console.log(`[UnityBridge] Screenshot requested: ${filename}`);
        // In real impl, receive base64 from Unity or waitForFile
    }

    disconnect() {
        if (this.socket) {
            this.connected = false; // Prevent listener log
            ipc.disconnect('FirmwareEngine');
            this.socket = null;
        }
    }
}

module.exports = new UnityBridge();
