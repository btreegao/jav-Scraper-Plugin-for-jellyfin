import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const source = await readFile(new URL('../Jellyfin.Plugin.NumberCover/Configuration/configPage.js', import.meta.url), 'utf8');
const { default: controller } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));

function setup(getConfiguration) {
    const pageEvents = new Map();
    const formEvents = new Map();
    const saveButton = { disabled: false };
    const controls = Object.fromEntries(['NumberCoverConfigError', 'EnableJableSource', 'OnlyFillMissing', 'MaxRequestsPerMinute', 'TimeoutSeconds'].map(id => [id, { style: {}, checked: false, value: '', textContent: '' }]));
    controls.NumberCoverConfigForm = {
        querySelector: () => saveButton,
        addEventListener: (event, handler) => formEvents.set(event, handler)
    };
    const updates = [];
    let reads = 0;
    globalThis.window = {
        ApiClient: {
            getPluginConfiguration: async () => { reads++; return getConfiguration(); },
            updatePluginConfiguration: async (_, config) => { updates.push(config); return {}; }
        },
        Dashboard: { showLoadingMsg() {}, hideLoadingMsg() {}, processPluginConfigurationUpdateResult() {} }
    };
    controller({ querySelector: selector => controls[selector.slice(1)], addEventListener: (event, handler) => pageEvents.set(event, handler) });
    return { controls, saveButton, updates, reads: () => reads, load: () => pageEvents.get('viewshow')(), save: () => formEvents.get('submit')({ preventDefault() {} }) };
}

test('save before configuration loads cannot overwrite saved values', async () => {
    const page = setup(() => ({ EnableJableSource: false }));
    assert.equal(page.saveButton.disabled, true);
    await page.save();
    assert.equal(page.reads(), 0);
    assert.equal(page.updates.length, 0);
});

test('failed configuration load keeps saving blocked', async () => {
    const page = setup(() => { throw new Error('network unavailable'); });
    const originalError = console.error;
    console.error = () => {};
    try { await page.load(); } finally { console.error = originalError; }
    await page.save();
    assert.equal(page.saveButton.disabled, true);
    assert.equal(page.updates.length, 0);
});

test('successful load preserves stored settings and enables save', async () => {
    const page = setup(() => ({ EnableJableSource: false, OnlyFillMissing: true, MaxRequestsPerMinute: 6, TimeoutSeconds: 20, FutureSetting: 'preserve' }));
    await page.load();
    assert.equal(page.saveButton.disabled, false);
    assert.equal(page.controls.EnableJableSource.checked, false);
    assert.equal(page.controls.OnlyFillMissing.checked, true);
    page.controls.EnableJableSource.checked = true;
    page.controls.OnlyFillMissing.checked = false;
    await page.save();
    assert.equal(page.updates[0].EnableJableSource, true);
    assert.equal(page.updates[0].OnlyFillMissing, false);
    assert.equal(page.updates[0].FutureSetting, 'preserve');
});

test('missing properties use the new online and replacement defaults', async () => {
    const page = setup(() => ({}));
    await page.load();
    assert.equal(page.controls.EnableJableSource.checked, true);
    assert.equal(page.controls.OnlyFillMissing.checked, false);
    assert.equal(page.controls.MaxRequestsPerMinute.value, 12);
    assert.equal(page.controls.TimeoutSeconds.value, 10);
});
