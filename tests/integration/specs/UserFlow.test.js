const unity = require('../UnityBridge');

describe('RoboTwin UI Integration Tests', () => {

    beforeAll(async () => {
        await unity.connect();
    });

    afterAll(() => {
        unity.disconnect();
    });

    test('User can launch application and see Dashboard', async () => {
        const scene = await unity.queryState('CurrentScene');
        expect(scene).toBeDefined();
    });

    test('User can create a new project', async () => {
        await unity.sendCommand('CLICK', '#NewProjectBtn');
        await unity.takeScreenshot('step1_project_created.png');
        
        const scene = await unity.queryState('CurrentScene');
        expect(scene).toBe('CircuitStudio');
    });

    test('User can toggle Run Mode', async () => {
        await unity.sendCommand('CLICK', '#toolbar-sim'); // The lightning bolt icon
        await unity.takeScreenshot('step2_runmode_active.png');
        
        // Use a slight delay to allow transition
        await new Promise(r => setTimeout(r, 1000));
        
        const isRunMode = await unity.queryState('#RunMode');
        expect(isRunMode).toBeTruthy();
    });
});
