import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = dirname(dirname(fileURLToPath(import.meta.url)));
const client = readFileSync(
    join(root, 'Jellyfin.Plugin.SleepTimer', 'Web', 'client.js'),
    'utf8');
const css = readFileSync(
    join(root, 'Jellyfin.Plugin.SleepTimer', 'Web', 'client.css'),
    'utf8');

const menuSource = client.slice(
    client.indexOf('    function createMenuEntry'),
    client.indexOf('    function ensureSettingsMenuEntries'));
assert.match(menuSource, /actionSheetMenuItem/,
    'Sleep Timer must inherit the native player-settings row geometry.');
assert.match(menuSource, /event\.stopPropagation\(\)/,
    'The injected row must not reach Jellyfin\'s delegated unknown-item handler.');
assert.doesNotMatch(menuSource, /sleepTimerPluginMenuSecondary/,
    'The player-settings row must stay on one line.');

assert.match(client,
    /type="text" inputmode="numeric" pattern="\[0-9\]\*"[^>]*enterkeyhint="done"/,
    'Custom duration must request the mobile numeric keyboard without number spinners.');
assert.doesNotMatch(client, /is="emby-input"/,
    'Customized built-in emby-input initialization breaks dynamically inserted inputs.');
assert.match(css, /#sleepTimerPluginMinutes:focus[\s\S]*?outline: none !important;/,
    'The input must suppress the browser/theme default focus outline.');

const focusRule = css.match(
    /\.sleepTimerPluginInputShell:focus-within\s*\{(?<body>[\s\S]*?)\}/)?.groups?.body;
assert.ok(focusRule, 'The input shell needs a focus treatment.');
assert.doesNotMatch(focusRule, /,\s*(?:inset\s+)?(?:-?\d|\.)/,
    'The input focus treatment must be a single ring, not stacked rings.');

const presetRule = css.match(
    /\.sleepTimerPluginPreset\s*\{(?<body>[\s\S]*?)\}/)?.groups?.body;
assert.ok(presetRule, 'Preset buttons need a dedicated layout rule.');
assert.match(presetRule, /justify-content:\s*center\s*!important;/,
    'Preset values must be horizontally centered even under custom Jellyfin themes.');
assert.match(presetRule, /text-align:\s*center\s*!important;/,
    'Preset labels must retain centered text alignment.');

const presetFormatter = client.slice(
    client.indexOf('    function formatPreset'),
    client.indexOf('    function actionLabel'));
assert.match(presetFormatter, /text\('minutePreset',\s*\{ value: minutes \}\)/,
    'Every preset must use one minute-based label format.');
assert.doesNotMatch(presetFormatter, /hour|\/\s*60/,
    'Preset labels must not switch to hour-based wording.');

assert.doesNotMatch(client, /runClientFailsafe|failsafeTimerId/,
    'The browser must not expire a server-owned timer using wall-clock time.');
assert.match(client, /document\.addEventListener\('pause', handlePlaybackStateChange, true\)/,
    'Pause events must freeze the locally rendered countdown immediately.');

const transitionSource = client.slice(
    client.indexOf('    function actionSheetIsVisible'),
    client.indexOf('    function markPlayerSettingsOpen'));

class FakeHTMLElement {}

function createHarness({ closeOnBack = true, initiallyVisible = true } = {}) {
    let visible = initiallyVisible;
    let inert = false;
    let historyCalls = 0;
    let blurCalls = 0;
    let dialogCalls = 0;
    let toastCalls = 0;
    let nextId = 1;
    const frames = new Map();
    const timers = new Map();
    const listeners = new Map();

    const focusTarget = {
        blur() {
            blurCalls += 1;
            document.activeElement = null;
        }
    };
    const sheet = Object.assign(new FakeHTMLElement(), {
        isConnected: true,
        classList: { contains: () => false },
        getClientRects: () => visible ? [{ width: 320, height: 400 }] : [],
        contains: (element) => element === focusTarget,
        hasAttribute: (name) => name === 'inert' && inert,
        setAttribute: (name) => {
            if (name === 'inert') inert = true;
        },
        removeAttribute: (name) => {
            if (name === 'inert') inert = false;
        },
        addEventListener(name, listener, options) {
            const entries = listeners.get(name) || [];
            entries.push({ listener, once: Boolean(options?.once) });
            listeners.set(name, entries);
        },
        removeEventListener(name, listener) {
            listeners.set(name,
                (listeners.get(name) || []).filter((entry) => entry.listener !== listener));
        },
        dispatch(name) {
            const entries = [...(listeners.get(name) || [])];
            for (const entry of entries) {
                if (entry.once) sheet.removeEventListener(name, entry.listener);
                entry.listener();
            }
        }
    });
    const button = Object.assign(new FakeHTMLElement(), {
        closest: (selector) => selector === '.actionSheet' ? sheet : null
    });
    const state = {
        pendingDialogOpen: false,
        expectedActionSheetCloseUntil: 0,
        settingsButton: null
    };
    const document = {
        activeElement: focusTarget,
        contains: () => false,
        querySelectorAll: (selector) => selector === '.actionSheet' ? [sheet] : []
    };
    const window = {
        getComputedStyle: () => ({ display: 'block', visibility: 'visible' }),
        requestAnimationFrame(callback) {
            const id = nextId++;
            frames.set(id, callback);
            return id;
        },
        cancelAnimationFrame: (id) => frames.delete(id),
        setTimeout(callback) {
            const id = nextId++;
            timers.set(id, callback);
            return id;
        },
        clearTimeout: (id) => timers.delete(id),
        history: {
            back() {
                historyCalls += 1;
                if (closeOnBack) {
                    window.requestAnimationFrame(() => {
                        visible = false;
                        sheet.dispatch('close');
                    });
                }
            }
        }
    };
    const warnings = [];
    const fakeConsole = { warn: (...args) => warnings.push(args) };
    const createFunctions = new Function(
        'state', 'window', 'document', 'HTMLElement', 'showDialog', 'showToast',
        'text', 'console',
        `"use strict";\n${transitionSource}\nreturn { actionSheetIsVisible, openDialogFromSettings };`);
    const functions = createFunctions(
        state,
        window,
        document,
        FakeHTMLElement,
        () => {
            assert.equal(visible, false, 'Dialog opened while native settings was visible.');
            dialogCalls += 1;
        },
        () => { toastCalls += 1; },
        (key) => key,
        fakeConsole);

    function flushFrames(limit = 20) {
        let runs = 0;
        while (frames.size && runs < limit) {
            const [id, callback] = frames.entries().next().value;
            frames.delete(id);
            callback();
            runs += 1;
        }
        assert.ok(runs < limit, 'Animation-frame polling did not settle.');
    }

    function runTimers() {
        const pending = [...timers.values()];
        timers.clear();
        pending.forEach((callback) => callback());
    }

    return {
        button,
        functions,
        state,
        flushFrames,
        runTimers,
        metrics: () => ({
            historyCalls,
            blurCalls,
            dialogCalls,
            toastCalls,
            inert,
            visible,
            warnings
        })
    };
}

{
    const harness = createHarness();
    harness.functions.openDialogFromSettings(harness.button);
    harness.functions.openDialogFromSettings(harness.button);
    assert.equal(harness.metrics().historyCalls, 1,
        'Repeated taps must not trigger multiple history transitions.');
    assert.equal(harness.metrics().dialogCalls, 0,
        'Dialog must wait for the native settings sheet to close.');
    assert.equal(harness.metrics().blurCalls, 1,
        'Focus must leave the closing native settings sheet.');
    assert.equal(harness.metrics().inert, true,
        'Closing native settings must become non-interactive immediately.');

    harness.flushFrames();
    assert.equal(harness.metrics().dialogCalls, 1,
        'Dialog must open exactly once after native settings closes.');
    assert.equal(harness.metrics().toastCalls, 0);
    assert.equal(harness.metrics().inert, false);
    assert.equal(harness.state.pendingDialogOpen, false);
}

{
    const harness = createHarness({ closeOnBack: false });
    harness.functions.openDialogFromSettings(harness.button);
    harness.runTimers();
    assert.equal(harness.metrics().dialogCalls, 0,
        'Dialog must not overlap a native settings sheet that failed to close.');
    assert.equal(harness.metrics().toastCalls, 1,
        'A failed settings transition must be reported.');
    assert.equal(harness.metrics().inert, false,
        'A failed transition must restore the native settings sheet.');
    assert.equal(harness.state.pendingDialogOpen, false);
}

{
    const harness = createHarness({ initiallyVisible: false });
    harness.functions.openDialogFromSettings(harness.button);
    assert.equal(harness.metrics().historyCalls, 0);
    assert.equal(harness.metrics().dialogCalls, 1);
}

console.log('Client UI regression checks passed.');
