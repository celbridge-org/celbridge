// Console document editor. One WebView carries two channels: the standard document content/save channel
// (the .console TOML edited through the settings form) and a custom console/* RPC channel (the live pty
// terminal). The pty launch is JS-triggered (console/start) after console/write is registered, so no early
// output is lost.

import celbridge from '/assets/celbridge-client/celbridge.js';
import { ContentLoadedReason } from '/assets/celbridge-client/api/document-api.js';
import { parseConsoleToml, serializeConsoleToml, defaultConsoleConfig } from './console-toml.js';

const client = celbridge;

const darkTheme = window.VSCodeTerminalThemes.dark;
const lightTheme = window.VSCodeTerminalThemes.light;

const initialIsDark = typeof window !== 'undefined' && window.matchMedia
    ? window.matchMedia('(prefers-color-scheme: dark)').matches
    : true;

const term = new Terminal({
    theme: initialIsDark ? darkTheme : lightTheme,
    fontFamily: "'Cascadia Mono', monospace",
    allowProposedApi: true,
    scrollSensitivity: 3,
});

const fitAddon = new FitAddon.FitAddon();
term.loadAddon(fitAddon);
term.loadAddon(new ClipboardAddon.ClipboardAddon());

const unicode11Addon = new Unicode11Addon.Unicode11Addon();
term.loadAddon(unicode11Addon);
term.unicode.activeVersion = '11';

function handleLink(event, uri) {
    event?.preventDefault?.();
    location.assign(uri);
}

term.options.linkHandler = {
    activate: handleLink,
    hover() { },
    leave() { },
    allowNonHttpProtocols: true,
};
term.loadAddon(new WebLinksAddon.WebLinksAddon(handleLink));

// Wait for the bundled monospace font before opening the terminal, so xterm measures cell dimensions
// against the correct font rather than an inflated fallback width.
await Promise.all([
    document.fonts.load('1em "Cascadia Mono"'),
    document.fonts.load('bold 1em "Cascadia Mono"'),
    document.fonts.load('italic 1em "Cascadia Mono"'),
    document.fonts.load('bold italic 1em "Cascadia Mono"'),
]);

const terminalElement = document.getElementById('terminal');
term.open(terminalElement);
terminalElement.querySelector('.xterm-helper-textarea')?.setAttribute('name', 'terminal-input');
fitAddon.fit();

// DOM references.
const settingsToggle = document.getElementById('settings-toggle');
const pip = document.getElementById('pip');
const terminalView = document.getElementById('terminal-view');
const settingsView = document.getElementById('settings-view');
const sessionFailed = document.getElementById('session-failed');
const sessionFailedMessage = document.getElementById('session-failed-message');
const reopenTerminalButton = document.getElementById('reopen-terminal');
const configErrorElement = document.getElementById('config-error');
const executableInput = document.getElementById('executable');
const argumentsInput = document.getElementById('arguments');
const workingDirectoryInput = document.getElementById('working-directory');
const environmentInput = document.getElementById('environment');
const reopenSettingsButton = document.getElementById('reopen-settings');

// State. currentConfig mirrors the settings form / .console file; launchedConfig is the config the live
// session was started from, so the pip can flag "changed, needs a reopen".
let currentConfig = defaultConsoleConfig();
let launchedConfig = null;
let configError = null;

// Theme.
function applyTheme(theme) {
    const isDark = theme === 'Dark';
    document.documentElement.setAttribute('data-theme', isDark ? 'dark' : 'light');
    try {
        term.options.theme = isDark ? darkTheme : lightTheme;
    } catch {
        // term.options not ready yet; the next change will apply it.
    }
}

client.appState.onChanged((appState) => {
    if (appState.theme) {
        applyTheme(appState.theme);
    }
});

// Terminal I/O over the console/* live-session channel.
client.onNotification('console/write', (params) => {
    if (params && typeof params.text === 'string') {
        term.write(params.text);
    }
});

client.onNotification('console/sessionState', (params) => {
    if (params && params.state === 'ended') {
        showSessionFailed('The console session ended.');
    }
});

term.onData((data) => client._notify('console/input', { data }));
term.onResize(({ cols, rows }) => client._notify('console/resize', { cols, rows }));

// Force the wheel to scroll the xterm viewport while the shell prompt owns the screen, so a TUI does not
// receive wheel-as-arrow-keys. Shift bypasses this; a TUI on the alternate buffer keeps native scroll.
terminalElement.addEventListener('wheel', (event) => {
    if (event.shiftKey) {
        return;
    }
    if (term.modes.mouseTrackingMode !== 'none' || term.buffer.active.type === 'alternate') {
        return;
    }
    event.preventDefault();
    event.stopPropagation();
    const lines = Math.sign(event.deltaY) * Math.max(1, Math.round(Math.abs(event.deltaY) / 40));
    term.scrollLines(lines);
}, { capture: true, passive: false });

term.attachCustomKeyEventHandler((event) => {
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'c') {
        if (term.hasSelection()) {
            navigator.clipboard.writeText(term.getSelection());
            event.preventDefault?.();
            return false;
        }
        return true;
    }
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'v') {
        return false;
    }
    if (event.ctrlKey && (event.key === 'd' || event.key === 'z')) {
        return false;
    }
    return true;
});

window.addEventListener('resize', () => {
    if (!terminalView.classList.contains('hidden')) {
        fitAddon.fit();
    }
});

// Mode toggle.
settingsToggle.addEventListener('click', () => {
    if (settingsView.classList.contains('hidden')) {
        showSettings();
    } else {
        showTerminal();
    }
});

function showSettings() {
    terminalView.classList.add('hidden');
    settingsView.classList.remove('hidden');
    settingsToggle.classList.add('active');
}

function showTerminal() {
    settingsView.classList.add('hidden');
    terminalView.classList.remove('hidden');
    settingsToggle.classList.remove('active');
    // The terminal had zero size while hidden, so refit once it is laid out again.
    requestAnimationFrame(() => {
        fitAddon.fit();
        term.focus();
    });
}

// Settings form.
function populateForm(config) {
    executableInput.value = config.executable || '';
    argumentsInput.value = (config.arguments || []).join('\n');
    workingDirectoryInput.value = config.workingDirectory || '';
    environmentInput.value = Object.entries(config.environment || {})
        .map(([name, value]) => `${name}=${value}`)
        .join('\n');
}

function readForm() {
    return {
        type: currentConfig.type || 'shell',
        executable: executableInput.value.trim(),
        arguments: argumentsInput.value.split('\n').map((line) => line.trim()).filter((line) => line !== ''),
        workingDirectory: workingDirectoryInput.value.trim(),
        environment: parseEnvironmentLines(environmentInput.value),
    };
}

function parseEnvironmentLines(text) {
    const environment = {};
    for (const rawLine of text.split('\n')) {
        const line = rawLine.trim();
        if (line === '') {
            continue;
        }
        const equalsIndex = line.indexOf('=');
        if (equalsIndex < 0) {
            continue;
        }
        const name = line.slice(0, equalsIndex).trim();
        const value = line.slice(equalsIndex + 1).trim();
        if (name !== '') {
            environment[name] = value;
        }
    }
    return environment;
}

function onFormInput() {
    currentConfig = readForm();
    // Editing the form yields a well-formed config, so any prior parse error is cleared.
    configError = null;
    // Mark the document dirty so the host's save timer flushes the serialised TOML through onRequestSave.
    client.document.notifyChanged();
    updateAttention();
}

for (const field of [executableInput, argumentsInput, workingDirectoryInput, environmentInput]) {
    field.addEventListener('input', onFormInput);
}

reopenSettingsButton.addEventListener('click', () => { startSession(); });
reopenTerminalButton.addEventListener('click', () => { startSession(); });

// The pip flags either a config error or a config that diverges from the launched session.
function updateAttention() {
    const diverged = launchedConfig !== null && !configsEqual(currentConfig, launchedConfig);
    const needsAttention = diverged || configError !== null;
    pip.classList.toggle('hidden', !needsAttention);
    reopenSettingsButton.disabled = !diverged;

    if (configError) {
        configErrorElement.textContent = configError;
        configErrorElement.classList.remove('hidden');
    } else {
        configErrorElement.classList.add('hidden');
    }
}

function configsEqual(a, b) {
    return JSON.stringify(normalizeConfig(a)) === JSON.stringify(normalizeConfig(b));
}

// A comparable/launch-ready view of a config: the launch-affecting fields only, with a stable env order.
function normalizeConfig(config) {
    const environment = {};
    for (const name of Object.keys(config.environment || {}).sort()) {
        environment[name] = config.environment[name];
    }
    return {
        type: config.type || 'shell',
        executable: config.executable || '',
        arguments: config.arguments || [],
        workingDirectory: config.workingDirectory || '',
        environment,
    };
}

// Session lifecycle.
function showSessionFailed(message) {
    sessionFailedMessage.textContent = message;
    sessionFailed.classList.remove('hidden');
}

function hideSessionFailed() {
    sessionFailed.classList.add('hidden');
}

async function startSession() {
    // Launch (and relaunch) always run against the visible, sized terminal.
    showTerminal();
    hideSessionFailed();
    fitAddon.fit();
    term.reset();

    const config = normalizeConfig(currentConfig);
    const cols = term.cols;
    const rows = term.rows;

    try {
        const result = await client._request('console/start', { cols, rows, config });
        if (result && result.ok) {
            launchedConfig = JSON.parse(JSON.stringify(currentConfig));
            updateAttention();
            term.focus();
        } else {
            showSessionFailed((result && result.error) || 'Failed to start the console session.');
        }
    } catch (error) {
        showSessionFailed((error && error.message) || String(error));
    }
}

function applyContent(content) {
    try {
        currentConfig = parseConsoleToml(content);
        configError = null;
    } catch (error) {
        configError = (error && error.message) || 'Invalid .console configuration.';
    }
    populateForm(currentConfig);
    updateAttention();
}

async function main() {
    await client.initializeDocument({
        onContent: (content) => applyContent(content),
        onRequestSave: async () => {
            await client.document.save(serializeConsoleToml(currentConfig));
        },
        onExternalChange: async () => {
            try {
                const result = await client.document.load();
                applyContent(result.content);
            } catch (error) {
                console.error('[Console] Failed to reload config:', error);
            }
            // Ack the reload so the host's external-change handshake does not time out.
            client.document.notifyContentLoaded(ContentLoadedReason.ExternalReload);
        },
    });

    await startSession();
}

main();
