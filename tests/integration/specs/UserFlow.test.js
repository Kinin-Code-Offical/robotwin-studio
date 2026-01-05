const unity = require('../UnityBridge');
const shouldRun = process.env.UNITY_E2E === '1';
const runDescribe = shouldRun ? describe : describe.skip;
const runTest = shouldRun ? test : test.skip;

runDescribe('RoboTwin UI Integration Tests', () => {

    beforeAll(async () => {
        if (!shouldRun) return;
        await unity.connect();
    });

    afterAll(() => {
        unity.disconnect();
    });

    runTest('User can launch application and see Dashboard', async () => {
        const scene = await unity.queryState('CurrentScene');
        expect(scene).toBeDefined();
    });

    runTest('User can create a new project', async () => {
        await unity.sendCommand('CLICK', '#NewProjectBtn');
        await unity.takeScreenshot('step1_project_created.png');
        
        const scene = await unity.queryState('CurrentScene');
        expect(scene).toBe('CircuitStudio');
    });

    runTest('User can toggle Run Mode', async () => {
        await unity.sendCommand('CLICK', '#toolbar-sim'); // The lightning bolt icon
        await unity.takeScreenshot('step2_runmode_active.png');
        
        // Use a slight delay to allow transition
        await new Promise(r => setTimeout(r, 1000));
        
        const isRunMode = await unity.queryState('#RunMode');
        expect(isRunMode).toBeTruthy();
    });
});
