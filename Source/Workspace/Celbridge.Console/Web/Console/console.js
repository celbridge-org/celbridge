// Console document editor. One WebView carries two channels: the standard document content/save channel
// (the .console TOML edited through the settings form) and a custom console/* RPC channel (the live pty
// terminal). The pty launch is JS-triggered (console/start) after console/write is registered, so no early
// output is lost.

import celbridge from '/assets/celbridge-client/celbridge.js';
import { ContentLoadedReason } from '/assets/celbridge-client/api/document-api.js';
import { t } from '/assets/celbridge-client/localization.js';
import { attachSplitter } from '/assets/celbridge-client/ui/splitter.js';
import { parseConsoleToml, serializeConsoleToml, defaultConsoleConfig } from './console-toml.js';
import {
    splitLines,
    parseEnvironmentLines,
    parseRunnerLines,
    formatRunnerLines,
    parseShortcutLines,
    formatShortcutLines,
    configsEqual,
    buildStartConfig,
    createMarkerScanner,
} from './console-config.js';

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
    // Assigning the location triggers a WebView navigation. The host intercepts NavigationStarting and
    // opens the system browser instead, so this never navigates the editor away.
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
const shortcutToolbar = document.getElementById('shortcut-toolbar');
const terminalView = document.getElementById('terminal-view');
const splitter = document.getElementById('splitter');
const settingsView = document.getElementById('settings-view');
const settingsScroll = document.getElementById('settings-scroll');
const sessionStarting = document.getElementById('session-starting');
const sessionFailed = document.getElementById('session-failed');
const sessionFailedMessage = document.getElementById('session-failed-message');
const reopenTerminalButton = document.getElementById('reopen-terminal');
const configErrorElement = document.getElementById('config-error');
const sessionTypeSelect = document.getElementById('session-type');
const executableField = document.getElementById('executable-field');
const executableInput = document.getElementById('executable');
const pythonVersionField = document.getElementById('python-version-field');
const pythonVersionInput = document.getElementById('python-version');
const argumentsInput = document.getElementById('arguments');
const dependenciesField = document.getElementById('dependencies-field');
const dependenciesInput = document.getElementById('dependencies');
const workingDirectoryInput = document.getElementById('working-directory');
const startupScriptInput = document.getElementById('startup-script');
const environmentInput = document.getElementById('environment');
const runnersInput = document.getElementById('runners');
const shortcutsInput = document.getElementById('shortcuts');
const reopenSettingsButton = document.getElementById('reopen-settings');

// The collapsible settings cards, in document order (Session, Environment, Script Runners, Shortcuts).
function settingsCards() {
    return settingsScroll.querySelectorAll('details.cel-expander');
}

// State. currentConfig mirrors the settings form / .console file. launchedConfig is the config the live
// session was started from, so the pip can flag "changed, needs a reopen".
let currentConfig = defaultConsoleConfig();
let launchedConfig = null;
let configError = null;

// Scans terminal output for the host's ready marker while the starting veil is up, or null once the
// terminal is revealed. Held here because the console/write handler runs before startSession assigns it.
let markerScanner = null;

// Theme.
function applyTheme(theme) {
    const isDark = theme === 'Dark';
    document.documentElement.setAttribute('data-theme', isDark ? 'dark' : 'light');
    try {
        term.options.theme = isDark ? darkTheme : lightTheme;
    } catch {
        // term.options not ready yet. The next change will apply it.
    }
}

client.appState.onChanged((appState) => {
    if (appState.theme) {
        applyTheme(appState.theme);
    }
});

// Terminal I/O over the console/* live-session channel. While the starting veil is up the output runs
// through the marker scanner, which strips the host's ready marker and reveals on it.
client.onNotification('console/write', (params) => {
    if (!params || typeof params.text !== 'string') {
        return;
    }

    if (markerScanner !== null) {
        const scanned = markerScanner.push(params.text);
        if (scanned.text) {
            term.write(scanned.text);
        }
        if (scanned.found) {
            markerScanner = null;

            // The shell cleared its screen immediately before the marker, so the startup noise is only
            // reachable by scrolling back. Erase saved lines (ED 3) to drop it, keeping the viewport.
            term.write('\x1b[3J');
            hideStartingVeil();
        }
        return;
    }

    term.write(params.text);
});

client.onNotification('console/sessionState', (params) => {
    if (params && params.state === 'ended') {
        showSessionFailed(t('Console_SessionEnded'));
    }
});

// The host's startup phase is over, so the ready marker is imminent and no further host milestone is
// coming. This only arms a short fallback reveal: a shell that cannot echo a marker (or one that failed
// to) must not stay veiled.
client.onNotification('console/startupComplete', () => {
    armVeilTimeout(1200);
});

term.onData((data) => client.sendNotification('console/input', { data }));
term.onResize(({ cols, rows }) => client.sendNotification('console/resize', { cols, rows }));

// Reports which edit verbs the console can perform: copy needs a selection, paste and select-all are
// always available. Sent on focus and selection change so the host Edit menu enables correctly.
function reportEditAvailability() {
    client.input.notifyEditAvailability({
        canCopy: term.hasSelection(),
        canPaste: true,
        canSelectAll: true,
    });
}

document.addEventListener('focusin', reportEditAvailability);
term.onSelectionChange(reportEditAvailability);

// Host-mediated clipboard: the host fetches the selection for copy and pushes clipboard text for paste,
// because the WebView's own JS clipboard is blocked on the Skia WKWebView. Paste writes the text straight
// to the pty as input. Select-all runs here.
client.onRequest('editor/getSelectedText', () => term.getSelection());
client.onNotification('editor/insertText', (params) => {
    if (params && typeof params.text === 'string' && params.text !== '') {
        client.sendNotification('console/input', { data: params.text });
    }
});
client.onNotification('input/performEdit', (params) => {
    if (params && params.command === 'selectAll') {
        term.selectAll();
    }
});

// Force the wheel to scroll the xterm viewport while the shell prompt owns the screen, so a TUI does not
// receive wheel-as-arrow-keys. Shift bypasses this. A TUI on the alternate buffer keeps native scroll.
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
    // Copy: with a selection, Ctrl+C copies it. Without one it falls through to the pty as an interrupt.
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'c') {
        if (term.hasSelection()) {
            navigator.clipboard.writeText(term.getSelection());
            event.preventDefault?.();
            return false;
        }
        return true;
    }
    // Paste: Ctrl+V arrives as a native paste event on xterm's hidden textarea, which xterm handles
    // itself. Returning false stops xterm also sending the Ctrl+V control character to the pty. Not
    // navigator.clipboard.readText(): over http that prompts for clipboard read permission.
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'v') {
        return false;
    }
    // Swallow Ctrl+D and Ctrl+Z. On Windows, IPython treats Ctrl+D as quit-with-confirmation and the
    // shell layer treats Ctrl+Z as the legacy MS-DOS EOF marker; neither is what a user pressing these
    // keys expects here.
    if (event.ctrlKey && (event.key === 'd' || event.key === 'z')) {
        return false;
    }
    return true;
});

window.addEventListener('resize', refitTerminal);

// The terminal is always visible. The settings form is a sidebar beside it. Refit the terminal whenever the
// space it occupies changes (sidebar toggled or resized, window resized), coalesced to one fit per frame.
let refitPending = false;
function refitTerminal() {
    if (refitPending) {
        return;
    }
    refitPending = true;
    requestAnimationFrame(() => {
        refitPending = false;
        try {
            fitAddon.fit();
        } catch {
            // The terminal may not be laid out yet. A later refit will apply.
        }
    });
}

// Settings sidebar toggle.
settingsToggle.addEventListener('click', () => {
    setSettingsVisible(settingsView.classList.contains('hidden'));
});

function setSettingsVisible(visible) {
    settingsView.classList.toggle('hidden', !visible);
    splitter.classList.toggle('hidden', !visible);
    settingsToggle.classList.toggle('active', visible);
    refitTerminal();
    if (!visible) {
        term.focus();
    }
}

// The sidebar and the terminal each keep a reasonable minimum width, so dragging the splitter can never
// shrink either below these (a narrow window is handled by flex-shrink in console.css). SPLITTER_WIDTH
// mirrors .cel-splitter in celbridge.css.
const SIDEBAR_MIN_WIDTH = 240;
const TERMINAL_MIN_WIDTH = 360;
const SPLITTER_WIDTH = 8;
// Mirrors #settings-view width in console.css. Tracked in sidebarWidth so the width persists as view state
// even while the sidebar is hidden (a hidden element reports no layout width to read back).
const DEFAULT_SIDEBAR_WIDTH = 460;
let sidebarWidth = DEFAULT_SIDEBAR_WIDTH;

function clampSidebarWidth(width) {
    const maxWidth = Math.max(SIDEBAR_MIN_WIDTH, window.innerWidth - TERMINAL_MIN_WIDTH - SPLITTER_WIDTH);
    return Math.max(SIDEBAR_MIN_WIDTH, Math.min(width, maxWidth));
}

// Applies a sidebar width (clamped) and records it so it can be persisted as view state.
function applySidebarWidth(width) {
    sidebarWidth = clampSidebarWidth(width);
    settingsView.style.width = sidebarWidth + 'px';
}

// The settings sidebar sits to the right of the terminal, so dragging the splitter left widens it.
let sidebarDragStartWidth = 0;
attachSplitter(splitter, {
    onDragStart() {
        sidebarDragStartWidth = settingsView.getBoundingClientRect().width;
    },
    onDrag(deltaX) {
        applySidebarWidth(sidebarDragStartWidth - deltaX);
        refitTerminal();
    },
});

// Settings form. The executable field is shown only for the shell type. The dependency field only for the
// Python types. The other fields apply to every type.
function applyTypeVisibility(type) {
    const isShell = type === 'shell';
    executableField.classList.toggle('hidden', !isShell);
    pythonVersionField.classList.toggle('hidden', isShell);
    dependenciesField.classList.toggle('hidden', isShell);
}

function populateForm(config) {
    sessionTypeSelect.value = config.type || 'shell';
    applyTypeVisibility(config.type || 'shell');
    executableInput.value = config.executable || '';
    pythonVersionInput.value = config.pythonVersion || '';
    argumentsInput.value = (config.arguments || []).join('\n');
    dependenciesInput.value = (config.dependencies || []).join('\n');
    workingDirectoryInput.value = config.workingDirectory || '';
    startupScriptInput.value = config.startupScript || '';
    environmentInput.value = Object.entries(config.environment || {})
        .map(([name, value]) => `${name}=${value}`)
        .join('\n');
    runnersInput.value = formatRunnerLines(config.runners);
    shortcutsInput.value = formatShortcutLines(config.shortcuts);
    renderShortcutToolbar();
}

function readForm() {
    return {
        type: sessionTypeSelect.value || 'shell',
        // Title is not a form field. Carry through whatever the .console file set.
        title: currentConfig.title || '',
        executable: executableInput.value.trim(),
        pythonVersion: pythonVersionInput.value.trim(),
        arguments: splitLines(argumentsInput.value),
        dependencies: splitLines(dependenciesInput.value),
        workingDirectory: workingDirectoryInput.value.trim(),
        startupScript: startupScriptInput.value.trimEnd(),
        environment: parseEnvironmentLines(environmentInput.value),
        runners: parseRunnerLines(runnersInput.value),
        shortcuts: parseShortcutLines(shortcutsInput.value),
    };
}

// The per-console shortcut toolbar: flat buttons that inject their text into the pty on click.
function renderShortcutToolbar() {
    shortcutToolbar.replaceChildren();
    const shortcuts = currentConfig.shortcuts || [];
    shortcutToolbar.classList.toggle('hidden', shortcuts.length === 0);

    for (const shortcut of shortcuts) {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'shortcut-button';
        button.title = shortcut.text || shortcut.label || '';

        if (shortcut.icon && shortcut.icon.startsWith('bs-')) {
            const iconElement = document.createElement('i');
            iconElement.className = 'bi bi-' + shortcut.icon.slice('bs-'.length);
            button.appendChild(iconElement);
        }
        if (shortcut.label) {
            const labelElement = document.createElement('span');
            labelElement.textContent = shortcut.label;
            button.appendChild(labelElement);
        }

        button.addEventListener('click', () => injectShortcut(shortcut.text));
        shortcutToolbar.appendChild(button);
    }
}

// Injects a shortcut's text into the pty: clear any partial input (Ctrl+U) then submit with a return.
function injectShortcut(text) {
    if (!text) {
        return;
    }
    client.sendNotification('console/input', { data: '\x15' + text + '\r' });
    term.focus();
}

function onFormInput() {
    currentConfig = readForm();
    // Editing the form yields a well-formed config, so any prior parse error is cleared.
    configError = null;
    // Mark the document dirty so the host's save timer flushes the serialised TOML through onRequestSave.
    client.document.notifyChanged();
    // The shortcut toolbar is pure client-side UI, so it previews live as the user edits. Every other
    // setting applies on the next reopen, flagged by updateAttention.
    renderShortcutToolbar();
    updateAttention();
}

sessionTypeSelect.addEventListener('change', () => {
    applyTypeVisibility(sessionTypeSelect.value);
    onFormInput();
});

const formFields = [
    sessionTypeSelect,
    executableInput,
    pythonVersionInput,
    argumentsInput,
    dependenciesInput,
    workingDirectoryInput,
    startupScriptInput,
    environmentInput,
    runnersInput,
    shortcutsInput,
];

for (const field of formFields) {
    if (field !== sessionTypeSelect) {
        field.addEventListener('input', onFormInput);
    }
}

// A read-only document disables the settings form so no edit marks the document dirty. The terminal stays
// interactive and Reopen stays available, since neither writes the file.
function applyWritableState() {
    const writable = client.viewState.current?.writable !== false;
    for (const field of formFields) {
        field.disabled = !writable;
    }
}

client.viewState.onChanged(() => applyWritableState());

reopenSettingsButton.addEventListener('click', () => { startSession(); });
reopenTerminalButton.addEventListener('click', () => { startSession(); });

// The pip flags either a config error or a config that diverges from the launched session.
function updateAttention() {
    const diverged = launchedConfig !== null && !configsEqual(currentConfig, launchedConfig);
    const needsAttention = diverged || configError !== null;
    pip.classList.toggle('hidden', !needsAttention);

    // The Reopen button stays enabled so the session can be restarted at any time. The accent colour appears
    // only when a reopen is needed to apply changed launch settings. The footer caption explains it.
    reopenSettingsButton.classList.toggle('cel-accent', diverged);

    if (configError) {
        configErrorElement.textContent = configError;
        configErrorElement.classList.remove('hidden');
    } else {
        configErrorElement.classList.add('hidden');
    }
}

// Session lifecycle.
function showSessionFailed(message) {
    hideStartingVeil();
    sessionFailedMessage.textContent = message;
    sessionFailed.classList.remove('hidden');
}

function hideSessionFailed() {
    sessionFailed.classList.add('hidden');
}

// The starting veil covers the terminal from launch until the shell reports its screen clear, hiding the
// shell-startup phase. The timers are the safety reveal for a shell that never echoes the ready marker.
const VEIL_FADE_MS = 240;

let veilTimeout = null;
let veilFadeTimer = null;

function showStartingVeil() {
    clearVeilTimers();
    sessionStarting.classList.remove('fading-out');
    sessionStarting.classList.remove('hidden');
}

function hideStartingVeil() {
    clearVeilTimers();

    if (sessionStarting.classList.contains('hidden')) {
        return;
    }

    // Fade rather than cut: the terminal materializes instead of appearing mid-repaint.
    sessionStarting.classList.add('fading-out');
    veilFadeTimer = setTimeout(() => {
        veilFadeTimer = null;
        sessionStarting.classList.add('hidden');
        sessionStarting.classList.remove('fading-out');
    }, VEIL_FADE_MS);
}

// Arms (or shortens) the safety reveal. Whatever the scanner has held back is written first, so no
// output is lost when the marker never arrives.
function armVeilTimeout(delayMs) {
    if (sessionStarting.classList.contains('hidden')) {
        return;
    }

    if (veilTimeout !== null) {
        clearTimeout(veilTimeout);
    }

    veilTimeout = setTimeout(() => {
        veilTimeout = null;
        if (markerScanner !== null) {
            const held = markerScanner.flush();
            markerScanner = null;
            if (held) {
                term.write(held);
            }
        }
        hideStartingVeil();
    }, delayMs);
}

function clearVeilTimers() {
    if (veilTimeout !== null) {
        clearTimeout(veilTimeout);
        veilTimeout = null;
    }
    if (veilFadeTimer !== null) {
        clearTimeout(veilFadeTimer);
        veilFadeTimer = null;
    }
}

// The pty is created at the terminal's measured size, so measure only once the layout has settled. On
// application startup the WebView is still being laid out when this script runs, and a resize that lands
// after the shell has painted makes ConPTY reflow its buffer, which shows up as a block of blank lines.
async function waitForStableSize() {
    const settled = (async () => {
        let previousWidth = -1;
        let previousHeight = -1;

        for (let frame = 0; frame < 30; frame++) {
            await new Promise((resolve) => requestAnimationFrame(resolve));

            const width = terminalView.clientWidth;
            const height = terminalView.clientHeight;
            if (width > 0 && height > 0 && width === previousWidth && height === previousHeight) {
                return;
            }

            previousWidth = width;
            previousHeight = height;
        }
    })();

    // Animation frames stop entirely while the document is hidden, so the measurement can never be what
    // gates the launch: a timer keeps the session starting even when the frames never arrive.
    const deadline = new Promise((resolve) => setTimeout(resolve, 500));

    await Promise.race([settled, deadline]);
}

let startInFlight = false;

async function startSession() {
    // A start doubles as reopen, so a second click while one is in flight would launch a second pty.
    if (startInFlight) {
        return;
    }
    startInFlight = true;

    // The terminal is always visible beside the settings sidebar. Launch and relaunch against it in place.
    hideSessionFailed();
    markerScanner = null;
    showStartingVeil();
    await waitForStableSize();
    fitAddon.fit();
    term.reset();

    // Snapshot the config being launched before the request: edits typed while the start is in flight must
    // still light the pip against the config the session actually received.
    const sentConfig = JSON.parse(JSON.stringify(currentConfig));

    // Send the full config, not the launch-only view: the host needs the runners to register the console
    // with the Run menu and the title for its display name, alongside the launch-affecting fields.
    const config = buildStartConfig(sentConfig);
    const cols = term.cols;
    const rows = term.rows;

    try {
        const result = await client.sendRequest('console/start', { cols, rows, config });
        if (result && result.ok) {
            launchedConfig = sentConfig;
            updateAttention();
            term.focus();

            // A plain shell has nothing to inject, so reveal at once. Otherwise hold the veil until the
            // injected command reports its screen clear, with a safety reveal past the injector's cap.
            if (!result.hasStartupCommand) {
                hideStartingVeil();
            } else {
                if (result.readyMarker) {
                    markerScanner = createMarkerScanner(result.readyMarker);
                }
                armVeilTimeout(4000);
            }
        } else {
            showSessionFailed((result && result.error) || t('Console_StartFailed'));
        }
    } catch (error) {
        showSessionFailed((error && error.message) || String(error));
    } finally {
        startInFlight = false;
    }
}

function applyContent(content) {
    try {
        currentConfig = parseConsoleToml(content);
        configError = null;
    } catch (error) {
        configError = (error && error.message) || t('Console_InvalidConfig');
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
        // Persist the settings sidebar's open state and width, each card's expanded state, and the scroll
        // position so they survive a reopen. openCards is by card order; the cards are a fixed set.
        onRequestState: () => JSON.stringify({
            settingsOpen: !settingsView.classList.contains('hidden'),
            sidebarWidth,
            openCards: Array.from(settingsCards()).map((card) => card.open),
            scrollTop: settingsScroll.scrollTop,
        }),
        onRestoreState: (stateJson) => {
            try {
                const state = JSON.parse(stateJson);
                if (typeof state.sidebarWidth === 'number' && state.sidebarWidth > 0) {
                    applySidebarWidth(state.sidebarWidth);
                }
                // Restore each card's expanded state before showing the sidebar, so the scroll height is
                // correct when the scroll position is applied below.
                if (Array.isArray(state.openCards)) {
                    settingsCards().forEach((card, index) => {
                        card.open = state.openCards[index] === true;
                    });
                }
                if (state.settingsOpen) {
                    setSettingsVisible(true);
                    // scrollTop only takes on a laid-out element, so apply it after the sidebar is shown.
                    if (typeof state.scrollTop === 'number' && state.scrollTop > 0) {
                        requestAnimationFrame(() => { settingsScroll.scrollTop = state.scrollTop; });
                    }
                }
            } catch (error) {
                // Ignore malformed state. Fall back to the defaults.
            }
        },
    });

    applyWritableState();
    await startSession();
}

main();
