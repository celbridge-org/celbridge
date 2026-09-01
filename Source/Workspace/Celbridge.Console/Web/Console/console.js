// Console document editor. One WebView carries two channels: the standard document content/save channel
// (the .console TOML edited through the settings form) and a custom console/* RPC channel (the live pty
// terminal). The session itself runs host-side from the moment the document opens; this view attaches to
// it (console/attach), replays its buffered output, and streams from there.

import celbridge from '/assets/celbridge-client/celbridge.js';
import { ContentLoadedReason } from '/assets/celbridge-client/api/document-api.js';
import { t, applyLocalization } from '/assets/celbridge-client/localization.js';
import { attachSplitter } from '/assets/celbridge-client/ui/splitter.js';
import { attachNavTabs } from '/assets/celbridge-client/ui/nav-tabs.js';
import { createCardList } from '/assets/celbridge-client/ui/card-list.js';
import { createIconField, resolveIconClass } from '/assets/celbridge-client/ui/icon-field.js';
import { parseConsoleToml, serializeConsoleToml, defaultConsoleConfig } from './console-toml.js';
import {
    splitLines,
    parseEnvironmentLines,
    parseExtensionList,
    configsEqual,
    buildStartConfig,
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

// Metrics for the starting veil's status line, which sits at the terminal's first cell. The row height
// is on the rows container xterm just laid out.
const terminalRows = terminalElement.querySelector('.xterm-rows');
document.documentElement.style.setProperty('--console-terminal-font-size', `${term.options.fontSize}px`);
if (terminalRows) {
    const rowLineHeight = getComputedStyle(terminalRows).lineHeight;
    document.documentElement.style.setProperty('--console-terminal-line-height', rowLineHeight);
}

// DOM references.
const settingsToggle = document.getElementById('settings-toggle');
const pip = document.getElementById('pip');
const shortcutRail = document.getElementById('shortcut-rail');
const shortcutSeparator = document.getElementById('shortcut-separator');
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
const argumentsField = document.getElementById('arguments-field');
const argumentsInput = document.getElementById('arguments');
const dependenciesField = document.getElementById('dependencies-field');
const dependenciesInput = document.getElementById('dependencies');
const workingDirectoryInput = document.getElementById('working-directory');
const startupScriptInput = document.getElementById('startup-script');
const environmentInput = document.getElementById('environment');
const reopenSettingsButton = document.getElementById('reopen-settings');
const builtInRunnerList = document.getElementById('runner-built-in');
const builtInRunnerTemplate = document.getElementById('built-in-runner-template');

// The settings sections and their headers, selected by the shared nav tab strip. Both are present in the
// markup with only the active one shown, mirroring how the native settings panel toggles its section views.
const settingsSections = Array.from(settingsScroll.querySelectorAll('.settings-section'));
const sectionHeaders = Array.from(settingsView.querySelectorAll('.cel-section-header'));

function showSection(sectionId) {
    for (const section of settingsSections) {
        section.classList.toggle('hidden', section.dataset.section !== sectionId);
    }
    for (const header of sectionHeaders) {
        header.classList.toggle('hidden', header.dataset.section !== sectionId);
    }
    settingsScroll.scrollTop = 0;
}

const navTabs = attachNavTabs(document.getElementById('settings-tabs'), {
    onChange: (sectionId) => showSection(sectionId),
});

// State. currentConfig mirrors the settings form / .console file. launchedConfig is the config the live
// session was started from, so the pip can flag "changed, needs a reopen".
let currentConfig = defaultConsoleConfig();
let launchedConfig = null;
let configError = null;
// The runners each session type provides, keyed by type id, as the host reports them on attach. Empty until
// then, so the built-in list simply renders nothing on the first populate.
let builtInRunnersByType = {};
// The ids of the built-in runners switched off for this console. Held apart from the form inputs because a
// card carries no editable field, so readForm carries this through rather than reading it back out of the DOM.
let disabledBuiltInRunners = [];

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

// Terminal I/O over the console/* live-session channel. The live session owns the startup-noise
// trimming, so everything that arrives here is renderable output.
client.onNotification('console/write', (params) => {
    if (params && typeof params.text === 'string') {
        term.write(params.text);
    }
});

client.onNotification('console/sessionState', (params) => {
    if (params && params.state === 'ended') {
        showSessionFailed(t('Console_SessionEnded'));
    }
});

// The live session's startup phase is over and its output stream is clean, so reveal the terminal.
client.onNotification('console/startupComplete', () => {
    hideStartingVeil();
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
    // The rail capsule tracks the panel being open, not focused: the terminal holds focus most of the time,
    // so a focus-following capsule would read as "closed" while the panel is plainly on screen.
    settingsToggle.classList.toggle('selected', visible);
    refitTerminal();
    if (!visible) {
        term.focus();
    }
}

// The sidebar and the terminal each keep a reasonable minimum width, so dragging the splitter can never
// shrink either below these (a narrow window is handled by flex-shrink in console.css). SPLITTER_WIDTH
// mirrors --cel-splitter-width in celbridge-tokens.css.
const SIDEBAR_MIN_WIDTH = 240;
const TERMINAL_MIN_WIDTH = 360;
const SPLITTER_WIDTH = 8;
// Mirrors #settings-view width in console.css. Tracked in sidebarWidth so the width persists as view state
// even while the sidebar is hidden (a hidden element reports no layout width to read back).
const DEFAULT_SIDEBAR_WIDTH = 460;
let sidebarWidth = DEFAULT_SIDEBAR_WIDTH;

// The token divides its declared width by the engine's page zoom, so the band is SPLITTER_WIDTH CSS pixels
// wide only where the engine and the host render at the same scale. This arithmetic is all in CSS pixels.
function splitterWidth() {
    const pageZoom = Number.parseFloat(
        getComputedStyle(document.documentElement).getPropertyValue('--cel-page-zoom'));

    if (!Number.isFinite(pageZoom) ||
        pageZoom <= 0) {
        return SPLITTER_WIDTH;
    }

    return SPLITTER_WIDTH / pageZoom;
}

function clampSidebarWidth(width) {
    const maxWidth = Math.max(SIDEBAR_MIN_WIDTH, window.innerWidth - TERMINAL_MIN_WIDTH - splitterWidth());
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
    // Arguments are the executable's, so only a shell console has anything to pass them to. A python
    // console configures its REPL through the startup script instead.
    argumentsField.classList.toggle('hidden', !isShell);
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
    disabledBuiltInRunners = config.disabledBuiltInRunners || [];
    runnerCards.populate(config.runners);
    triggerCards.populate(config.triggers);
    shortcutCards.populate(config.shortcuts);
    renderBuiltInRunners();
    renderShortcutRail();
}

// The runners the selected session type provides, shown above the console's own so the Run menu's behaviour
// is visible in the form. They are not part of the config: the host layers them under whatever the file
// declares, and re-reads them from the provider on every launch. Switching one off is the exception: the
// config names it by id, which is what the host resolves against.
function renderBuiltInRunners() {
    const runners = builtInRunnersByType[sessionTypeSelect.value] || [];
    builtInRunnerList.replaceChildren();

    for (const runner of runners) {
        const card = builtInRunnerTemplate.content.firstElementChild.cloneNode(true);
        applyLocalization(card);

        const extensionList = (runner.extensions || []).join(', ');
        card.querySelector('.cel-card-title').textContent = extensionList;
        card.querySelector('.built-in-extensions').textContent = extensionList;
        card.querySelector('.built-in-command').textContent = runner.command || '';

        const isOff = isBuiltInRunnerDisabled(runner.builtInId);
        card.classList.toggle('off', isOff);

        const toggle = card.querySelector('.built-in-switch');
        toggle.setAttribute('aria-checked', String(!isOff));
        toggle.disabled = !isDocumentWritable();

        // The switch sits inside the summary, whose default action would otherwise toggle the card open.
        toggle.addEventListener('click', (event) => {
            event.preventDefault();
            setBuiltInRunnerDisabled(runner.builtInId, !isOff);
        });

        builtInRunnerList.appendChild(card);
    }
}

function isBuiltInRunnerDisabled(id) {
    return disabledBuiltInRunners.some((disabled) => disabled.toLowerCase() === (id || '').toLowerCase());
}

function setBuiltInRunnerDisabled(id, disabled) {
    const remaining = disabledBuiltInRunners.filter((entry) => entry.toLowerCase() !== (id || '').toLowerCase());

    disabledBuiltInRunners = disabled ? remaining.concat(id) : remaining;
    renderBuiltInRunners();
    onFormInput();
}

function readForm() {
    return {
        type: sessionTypeSelect.value || 'shell',
        executable: executableInput.value.trim(),
        pythonVersion: pythonVersionInput.value.trim(),
        arguments: splitLines(argumentsInput.value),
        dependencies: splitLines(dependenciesInput.value),
        workingDirectory: workingDirectoryInput.value.trim(),
        startupScript: startupScriptInput.value.trimEnd(),
        environment: parseEnvironmentLines(environmentInput.value),
        runners: runnerCards.read(),
        disabledBuiltInRunners: disabledBuiltInRunners.slice(),
        triggers: triggerCards.read(),
        shortcuts: shortcutCards.read(),
    };
}

// A shortcut with no icon, or one the bundled icon set does not carry, still needs a glyph to be clickable,
// so it falls back to the icon the Automation settings tab uses.
const SHORTCUT_FALLBACK_ICON = 'bs-lightning-charge';

// The Automation lists. Each runner and shortcut is edited through its own card, so the cards are the source
// of truth for those two settings, the way the inputs above are for the rest of the form.

const runnerCards = createCardList({
    listElement: document.getElementById('runner-cards'),
    emptyElement: document.getElementById('runner-empty'),
    addButton: document.getElementById('add-runner'),
    template: document.getElementById('runner-card-template'),
    blankItem: () => ({ extensions: [], command: '' }),
    focusSelector: '.runner-extensions',
    localize: applyLocalization,
    onChanged: () => onFormInput(),
    isWritable: isDocumentWritable,

    fillCard(card, runner) {
        card.querySelector('.runner-extensions').value = (runner.extensions || []).join(', ');
        card.querySelector('.runner-command').value = runner.command || '';
    },

    readCard(card) {
        const extensions = parseExtensionList(card.querySelector('.runner-extensions').value);
        const command = card.querySelector('.runner-command').value.trim();
        if (extensions.length === 0 || command === '') {
            return null;
        }

        return { extensions, command };
    },

    // The collapsed card identifies the runner by the extensions it handles; the command is one expand away.
    updateHeader(card) {
        const extensions = card.querySelector('.runner-extensions').value.trim();
        card.querySelector('.cel-card-title').textContent = extensions || t('Console_Runner_Untitled');
    },
});

const triggerCards = createCardList({
    listElement: document.getElementById('trigger-cards'),
    emptyElement: document.getElementById('trigger-empty'),
    addButton: document.getElementById('add-trigger'),
    template: document.getElementById('trigger-card-template'),
    blankItem: () => ({ pattern: '', command: '' }),
    focusSelector: '.trigger-pattern',
    localize: applyLocalization,
    onChanged: () => onFormInput(),
    isWritable: isDocumentWritable,

    fillCard(card, trigger) {
        card.querySelector('.trigger-pattern').value = trigger.pattern || '';
        card.querySelector('.trigger-command').value = trigger.command || '';
    },

    readCard(card) {
        const pattern = card.querySelector('.trigger-pattern').value.trim();
        const command = card.querySelector('.trigger-command').value.trim();
        if (pattern === '' || command === '') {
            return null;
        }

        return { pattern, command };
    },

    // The collapsed card identifies the trigger by the pattern it watches, as the runner cards do by the
    // extensions they handle.
    updateHeader(card) {
        const pattern = card.querySelector('.trigger-pattern').value.trim();
        card.querySelector('.cel-card-title').textContent = pattern || t('Console_Trigger_Untitled');
    },
});

const shortcutCards = createCardList({
    listElement: document.getElementById('shortcut-cards'),
    emptyElement: document.getElementById('shortcut-empty'),
    addButton: document.getElementById('add-shortcut'),
    template: document.getElementById('shortcut-card-template'),
    blankItem: () => ({ label: '', icon: '', text: '' }),
    focusSelector: '.shortcut-label',
    localize: applyLocalization,
    onChanged: () => onFormInput(),
    isWritable: isDocumentWritable,

    fillCard(card, shortcut) {
        card.querySelector('.shortcut-label').value = shortcut.label || '';
        card.querySelector('.shortcut-text').value = shortcut.text || '';

        createIconField({
            container: card.querySelector('.shortcut-icon-field'),
            value: shortcut.icon || '',
            defaultIconName: SHORTCUT_FALLBACK_ICON,
            pickIcon: (searchText) => client.dialog.pickIcon(searchText),
        });
    },

    readCard(card) {
        const label = card.querySelector('.shortcut-label').value.trim();
        const icon = card.querySelector('.cel-icon-field-input').value.trim();
        const text = card.querySelector('.shortcut-text').value.trim();
        if (label === '' && text === '') {
            return null;
        }

        return { label, icon, text };
    },

    // The header doubles as the shortcut's preview: the glyph and label here are what the rail button shows.
    updateHeader(card) {
        const label = card.querySelector('.shortcut-label').value.trim();
        card.querySelector('.cel-card-title').textContent = label || t('Console_Shortcut_Untitled');

        const iconName = card.querySelector('.cel-icon-field-input').value.trim();
        const iconElement = card.querySelector('.cel-card-icon');
        iconElement.className = 'cel-card-icon bi ' + resolveIconClass(iconName, SHORTCUT_FALLBACK_ICON, iconElement);
    },
});

// The per-console shortcuts, rendered as icon-only cells at the top of the inspector rail. Each injects its
// text into the pty on click; the tooltip carries the label, falling back to the text it types.
function renderShortcutRail() {
    shortcutRail.replaceChildren();
    const shortcuts = currentConfig.shortcuts || [];
    shortcutSeparator.classList.toggle('hidden', shortcuts.length === 0);

    for (const shortcut of shortcuts) {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'cel-rail-button';

        const tooltip = shortcut.label || shortcut.text || '';
        button.title = tooltip;
        button.setAttribute('aria-label', tooltip);

        const iconElement = document.createElement('i');
        button.appendChild(iconElement);
        button.addEventListener('click', () => injectShortcut(shortcut.text));
        shortcutRail.appendChild(button);

        // Resolved once the button is in the rail: the glyph check reads the loaded icon font.
        iconElement.className = 'bi ' + resolveIconClass(shortcut.icon, SHORTCUT_FALLBACK_ICON, iconElement);
    }
}

// Injects a shortcut's text into the pty: clear any partial input (Ctrl+U) then submit with a return.
// Submitted through console/submit rather than written as raw input, so the host owns how an invocation is
// entered and confirmed at the prompt. A terminal app that reads a burst of stdin as one paste treats a
// carriage return inside it as a newline, so the submit key cannot travel with the text.
function injectShortcut(text) {
    if (!text) {
        return;
    }
    client.sendNotification('console/submit', { invocation: text });
    term.focus();
}

function onFormInput() {
    currentConfig = readForm();
    // Editing the form yields a well-formed config, so any prior parse error is cleared.
    configError = null;
    // Mark the document dirty so the host's save timer flushes the serialised TOML through onRequestSave.
    client.document.notifyChanged();
    // The shortcut rail is pure client-side UI, so it previews live as the user edits. Every other
    // setting applies on the next reopen, flagged by updateAttention.
    renderShortcutRail();
    updateAttention();
}

// Changing the type resets the console: every other setting is written for the type selected at the time.
sessionTypeSelect.addEventListener('change', () => {
    const resetConfig = defaultConsoleConfig();
    resetConfig.type = sessionTypeSelect.value;

    populateForm(resetConfig);
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
];

for (const field of formFields) {
    if (field !== sessionTypeSelect) {
        field.addEventListener('input', onFormInput);
    }
}

function isDocumentWritable() {
    return client.viewState.current?.writable !== false;
}

// A read-only document disables the settings form so no edit marks the document dirty. The terminal stays
// interactive and Reopen stays available, since neither writes the file. The blanket pass runs first so the
// per-card refinement below it decides the final state of the move buttons.
function applyWritableState() {
    const writable = isDocumentWritable();
    for (const field of formFields) {
        field.disabled = !writable;
    }

    runnerCards.refreshState();
    triggerCards.refreshState();
    shortcutCards.refreshState();
    renderBuiltInRunners();
}

client.viewState.onChanged(() => applyWritableState());

reopenSettingsButton.addEventListener('click', () => { reopenSession(); });
reopenTerminalButton.addEventListener('click', () => { reopenSession(); });

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

// Backstop for a host that never reports the startup phase ending. The host reveals a quiet session itself
// and notifies, so this only has to outlast its silence window: no output reaches the client while the veil
// is up, which leaves nothing here to measure progress against.
const VEIL_BACKSTOP_MS = 15000;

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

// Arms (or shortens) the safety reveal, so a missed startup-complete notification cannot leave the
// terminal veiled forever.
function armVeilTimeout(delayMs) {
    if (sessionStarting.classList.contains('hidden')) {
        return;
    }

    if (veilTimeout !== null) {
        clearTimeout(veilTimeout);
    }

    veilTimeout = setTimeout(() => {
        veilTimeout = null;
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

let requestInFlight = false;

// Renders an attach or reopen outcome: the launched config drives the pip, the replay fills the
// terminal, and the state decides between the veil, the failed overlay, and a live prompt.
function applyAttachResult(result) {
    // The built-in runners are static host knowledge that rides along with the attach, so the settings form can
    // show them. The first attach lands after the form is populated, hence the re-render.
    if (result && result.builtInRunners) {
        builtInRunnersByType = result.builtInRunners;
        renderBuiltInRunners();
    }

    launchedConfig = null;
    if (result && result.launchedConfigToml) {
        try {
            launchedConfig = parseConsoleToml(result.launchedConfigToml);
        } catch {
            // An unparseable launched config just leaves the pip dark until the next reopen.
        }
    }
    updateAttention();

    if (!result || result.state === 'failed') {
        showSessionFailed((result && result.error) || t('Console_StartFailed'));
        return;
    }

    if (result.replay) {
        term.write(result.replay);
    }

    if (result.state === 'ended') {
        hideStartingVeil();
        showSessionFailed(t('Console_SessionEnded'));
        return;
    }

    if (result.startupPending) {
        showStartingVeil();
        armVeilTimeout(VEIL_BACKSTOP_MS);
    } else {
        hideStartingVeil();
    }

    term.focus();
}

// Attaches this view to the live session, which has been running since the document opened. The
// terminal size sent here is the first accurate one the session has had: a headless launch guesses.
async function attachSession() {
    if (requestInFlight) {
        return;
    }
    requestInFlight = true;

    hideSessionFailed();

    // The veil is already up: it is the page's initial state, so the terminal is covered from the first
    // paint until the attach result decides whether it stays. Attach waits on the session start, which on
    // a first run includes installing the runtime's toolchain.
    await waitForStableSize();
    fitAddon.fit();
    term.reset();

    try {
        const result = await client.sendRequest('console/attach', { cols: term.cols, rows: term.rows });
        applyAttachResult(result);
    } catch (error) {
        showSessionFailed((error && error.message) || String(error));
    } finally {
        requestInFlight = false;
    }
}

// Relaunches the session from the file on disk. The form is flushed to the document first, so the file
// is the single source of truth for what a reopen launches.
async function reopenSession() {
    if (requestInFlight) {
        return;
    }
    requestInFlight = true;

    hideSessionFailed();
    showStartingVeil();

    try {
        await client.document.save(serializeConsoleToml(currentConfig));
    } catch (error) {
        console.error('[Console] Failed to flush the config before reopen:', error);
    }

    fitAddon.fit();
    term.reset();

    try {
        const result = await client.sendRequest('console/reopen', { cols: term.cols, rows: term.rows });
        applyAttachResult(result);
    } catch (error) {
        showSessionFailed((error && error.message) || String(error));
    } finally {
        requestInFlight = false;
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
        // Persist the settings sidebar's open state and width, the selected section, and the scroll position
        // so they survive a reopen.
        onRequestState: () => JSON.stringify({
            settingsOpen: !settingsView.classList.contains('hidden'),
            sidebarWidth,
            activeSection: navTabs.selected(),
            scrollTop: settingsScroll.scrollTop,
        }),
        onRestoreState: (stateJson) => {
            try {
                const state = JSON.parse(stateJson);
                if (typeof state.sidebarWidth === 'number' && state.sidebarWidth > 0) {
                    applySidebarWidth(state.sidebarWidth);
                }
                // Select the section before showing the sidebar, so the scroll height is correct when the
                // scroll position is applied below. An unknown id leaves the default section selected.
                if (typeof state.activeSection === 'string') {
                    navTabs.select(state.activeSection);
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
    await attachSession();
}

main();
