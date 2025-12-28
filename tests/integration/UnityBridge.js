const axios = require('axios');

class UnityBridge {
    constructor() {
        this.baseUrl = 'http://localhost:8085';
        this.connected = false;
    }

    async connect() {
        console.log(`[UnityBridge] Connecting to ${this.baseUrl}...`);
        try {
            // Simple ping to check if server is up. 
            // We use /query just to check connectivity.
            await axios.get(`${this.baseUrl}/query?target=ping`);
            console.log('[UnityBridge] Connected to Unity successfully.');
            this.connected = true;
        } catch (error) {
            this.connected = false;
            throw new Error('Unity connection failed. Start the editor and ensure RemoteCommandServer is running.');
        }
    }

    async sendCommand(action, target) {
        console.log(`[UnityBridge] Sending Command: ${action} -> ${target}`);
        if(this.connected) {
            try {
                await axios.get(`${this.baseUrl}/action`, {
                    params: { type: action, target: target }
                });
            } catch (err) {
                console.error(`[UnityBridge] Command failed: ${err.message}`);
            }
        }
        // Simulate delay for UI update
        return new Promise(r => setTimeout(r, 500));
    }

    async queryState(selector) {
        console.log(`[UnityBridge] Querying State: ${selector}`);
        if (!this.connected) {
            throw new Error('Unity connection not established.');
        }

        try {
            const res = await axios.get(`${this.baseUrl}/query`, {
                params: { target: selector }
            });
            return res.data.value;
        } catch (err) {
            console.error(`[UnityBridge] Query failed: ${err.message}`);
        }

        return null;
    }

    async takeScreenshot(filename) {
        console.log(`[UnityBridge] Screenshot requested: ${filename}`);
        if (!this.connected) {
            throw new Error('Unity connection not established.');
        }

        try {
            // The server saves it to the folder, we just trigger it.
            await axios.get(`${this.baseUrl}/screenshot`);
        } catch (err) {
            console.error(`[UnityBridge] Screenshot failed: ${err.message}`);
        }
    }

    disconnect() {
        this.connected = false;
    }
}

module.exports = new UnityBridge();
