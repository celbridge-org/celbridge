// Console document editor. One WebView carries two channels: the standard document content/save channel
// (the .console TOML edited through the settings form) and a custom console/* RPC channel (the live pty
// terminal). The session itself runs host-side from the moment the document opens; this view attaches to
// it (console/attach), replays its buffered output, and streams from there.

import celbridge from '/assets/celbridge-client/celbridge.js';
import { ContentLoadedReason } from '/assets/celbridge-client/api/document-api.js';
import { t, applyLocalization } from '/assets/celbridge-client/localization.js';
import { attachSectionSwitcher } from '/assets/celbridge-client/ui/section-switcher.js';
import { attachStackLayout } from '/assets/celbridge-client/ui/stack-layout.js';
import { createCardList } from '/assets/celbridge-client/ui/card-list.js';
import { createIconField, resolveIconClass } from '/assets/celbridge-client/ui/icon-field.js';
import { parseConsoleToml, serializeConsoleToml, defaultConsoleConfig } from './console-toml.js';
import {
    splitLines,
    parseEnvironmentLines,
    parseExtensionList,
    configsEqual,
} from './console-config.js';
import shellType from './types/shell.js';
import pythonType from './types/python.js';

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

// Whether a measurement taken now is worth acting on. The host gives this surface the geometry its content
// will be read at, which for a view in a tab that has not been shown is the size its section will present
// it at, and reports when it has. Without that a page cannot tell a placeholder from a real layout, and a
// pty created at a placeholder has to be resized once the view is shown, which costs the screen the output
// already painted on it.
function canMeasure() {
    return client.viewState.current?.isSized === 'true' &&
        terminalElement.clientWidth > 0 &&
        terminalElement.clientHeight > 0;
}

// Fits the terminal to its box, and reports whether that box was one worth measuring.
function fitTerminal() {
    if (!canMeasure()) {
        return false;
    }

    try {
        fitAddon.fit();
    } catch {
        // The terminal may not be laid out yet. A later fit will apply.
    }

    return true;
}

fitTerminal();

// Metrics for the starting veil's status line, which sits at the terminal's first cell. The row height
// is on the rows container xterm just laid out.
const terminalRows = terminalElement.querySelector('.xterm-rows');
document.documentElement.style.setProperty('--console-terminal-font-size', `${term.options.fontSize}px`);
if (terminalRows) {
    const rowLineHeight = getComputedStyle(terminalRows).lineHeight;
    document.documentElement.style.setProperty('--console-terminal-line-height', rowLineHeight);
}

// The width below which the rail lays out across the top of the content, mirroring
// --cel-rail-stack-threshold. Used where the generated stylesheet has not been served.
const RAIL_STACK_FALLBACK = 400;

// The narrowest a laid-out document can be, mirroring WorkspaceConstants.DocumentMinWidth and
// --cel-document-min-width.
const DOCUMENT_MIN_WIDTH = 230;

// DOM references.
const appElement = document.getElementById('app');
const railElement = appElement.querySelector('.cel-rail');
const openSettingsButton = document.getElementById('open-settings');
const pip = document.getElementById('pip');
const shortcutRail = document.getElementById('shortcut-rail');
const shortcutSeparator = document.getElementById('shortcut-separator');
const terminalView = document.getElementById('terminal-view');
const settingsView = document.getElementById('settings-view');
const sessionStarting = document.getElementById('session-starting');
const sessionFailed = document.getElementById('session-failed');
const sessionFailedMessage = document.getElementById('session-failed-message');
const reopenTerminalButton = document.getElementById('reopen-terminal');
const sessionTypeSelect = document.getElementById('session-type');
const typeNavItem = document.getElementById('type-nav-item');
const typeNavIcon = document.getElementById('type-nav-icon');
const typeNavLabel = document.getElementById('type-nav-label');
const typeSectionTitle = document.getElementById('type-section-title');
const typeSectionDescription = document.getElementById('type-section-description');
const typeFieldsElement = document.getElementById('type-fields');
const workingDirectoryInput = document.getElementById('working-directory');
const environmentInput = document.getElementById('environment');
const closeSettingsButton = document.getElementById('close-settings');
const reopenSettingsButton = document.getElementById('reopen-settings');
const builtInRunnerList = document.getElementById('runner-built-in');
const builtInRunnerTemplate = document.getElementById('built-in-runner-template');

// The settings surface.
const settingsSwitcher = attachSectionSwitcher(document.getElementById('settings-switcher'));

// State. currentConfig mirrors the settings form / .console file. launchedConfig is the config the live
// session was started from, so the pip can flag "changed, needs a reopen".
let currentConfig = defaultConsoleConfig();
let launchedConfig = null;
let configError = null;
let sessionStartFailed = false;
// What the terminal's failure overlay is saying, so the settings surface can say it too while the
// terminal is the hidden half of the row.
let sessionFailedText = '';
// The registered session types, in the order the form offers them, each carrying the keys it accepts and
// the runners it contributes. Null until an attach reports them.
let hostSessionTypes = null;
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

// A resize the session asked for is not one to report back: the session is already at that size.
let adoptingSessionSize = false;

term.onResize(({ cols, rows }) => {
    if (adoptingSessionSize) {
        return;
    }

    client.sendNotification('console/resize', { cols, rows });
});

// Resizes the terminal to the size the session painted its buffered output at, so nothing is rewrapped.
// Anything but a positive whole number of cells is not a size a terminal has, and the size it is already at
// is not worth taking.
function adoptSessionSize(cols, rows) {
    if (!Number.isInteger(cols) ||
        !Number.isInteger(rows) ||
        cols <= 0 ||
        rows <= 0 ||
        (cols === term.cols && rows === term.rows)) {
        return;
    }

    adoptingSessionSize = true;
    try {
        term.resize(cols, rows);
    } finally {
        adoptingSessionSize = false;
    }
}

// Whether the terminal is the view on screen. The settings form replaces it, and its fields are ordinary
// controls the platform edits itself.
function isTerminalShowing() {
    return !terminalView.classList.contains('hidden');
}

// Reports which edit verbs the console can perform: copy needs a selection, paste and select-all are
// always available. Sent on focus, selection change and view switch so the host Edit menu enables
// correctly. The console claims nothing while the settings form is showing, so an edit verb acts on the
// focused field rather than on the hidden terminal.
function reportEditAvailability() {
    const terminalShowing = isTerminalShowing();

    client.input.notifyEditAvailability({
        canCopy: terminalShowing && term.hasSelection(),
        canPaste: terminalShowing,
        canSelectAll: terminalShowing,
        hostMediatedClipboard: terminalShowing,
    });
}

document.addEventListener('focusin', reportEditAvailability);
term.onSelectionChange(reportEditAvailability);

// Host-mediated clipboard: the host fetches the selection for copy and pushes clipboard text for paste,
// because the WebView's own JS clipboard is blocked on the Skia WKWebView. Paste writes the text straight
// to the pty as input. Select-all runs here. Each verb is refused with the settings form showing, so a
// report still in flight cannot send clipboard text to the hidden pty.
client.onRequest('editor/getSelectedText', () => (isTerminalShowing() ? term.getSelection() : ''));
client.onNotification('editor/insertText', (params) => {
    if (!isTerminalShowing()) {
        return;
    }

    if (params && typeof params.text === 'string' && params.text !== '') {
        client.sendNotification('console/input', { data: params.text });
    }
});
client.onNotification('input/performEdit', (params) => {
    if (!isTerminalShowing()) {
        return;
    }

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

// A page hidden with its tab has no size to fit to.
document.addEventListener('visibilitychange', refitTerminal);

// Refit the terminal whenever the space it occupies changes, coalesced to one fit per frame.
let refitPending = false;
function refitTerminal() {
    if (refitPending) {
        return;
    }
    refitPending = true;
    requestAnimationFrame(() => {
        refitPending = false;
        fitTerminal();
    });
}

// The rail lays out down the left of a wide console and across the top of a narrow one. A fixed rail takes
// its width out of the row the terminal shares with it, so the terminal has resized either way.
attachStackLayout(appElement, {
    property: '--cel-rail-stack-threshold',
    fallback: RAIL_STACK_FALLBACK,
    attribute: 'rail',
    minimumWidth: DOCUMENT_MIN_WIDTH,
    onChange(arrangement) {
        railElement.classList.toggle('cel-rail-horizontal', arrangement === 'stacked');
        refitTerminal();
    },
});

openSettingsButton.addEventListener('click', () => setSettingsVisible(true));

// Settings and the terminal take turns filling the content row. A hidden terminal has no size to fit to,
// which fitTerminal() declines to measure, so the pty holds the size it was left at.
function setSettingsVisible(visible) {
    settingsView.classList.toggle('hidden', !visible);
    terminalView.classList.toggle('hidden', visible);
    // Takes the rail off screen while the surface is up, which the stylesheet keys on.
    appElement.dataset.surface = visible ? 'settings' : 'terminal';

    // Reported here rather than at the end, so the surface the settings replace stops claiming the
    // clipboard on the way in as well as on the way out.
    reportEditAvailability();

    if (visible) {
        // Hiding the rail destroys the focus the settings button was holding.
        settingsView.querySelector('.cel-section-nav-item[aria-selected="true"]')?.focus();
        return;
    }

    refitTerminal();
    term.focus();
}

closeSettingsButton.addEventListener('click', () => setSettingsVisible(false));

document.addEventListener('keydown', (event) => {
    // Escape belongs to whatever gesture is in progress first: a card drag cancels itself with it.
    if (event.key === 'Escape' &&
        !event.defaultPrevented &&
        !settingsView.classList.contains('hidden')) {
        setSettingsVisible(false);
        event.preventDefault();
    }
});

// The session types this client can edit, in the order the Type control offers them. A type is one module
// under types/, carrying the icon its rail row shows and the fields the form edits it through.
const typeModules = new Map([shellType, pythonType].map((typeModule) => [typeModule.typeId, typeModule]));

// A type needs a module to be offered.
const clientTypeIds = Array.from(typeModules.keys());

// The selected type's fields, resolved from the markup applyType injected. Each entry pairs a control with
// the key it holds in the type's [session.<type>] table.
let typeFields = [];

// The type section is one slot the selected type fills, not a section per type.
function applyType(type) {
    const label = typeLabel(type);
    const typeModule = typeModules.get(type) || null;

    typeNavLabel.textContent = label;
    typeNavItem.title = label;
    typeNavItem.setAttribute('aria-label', label);
    typeNavIcon.className = `bi ${typeModule?.icon || 'bi-terminal'}`;

    typeSectionTitle.textContent = label;
    typeSectionDescription.textContent = localizedTypeString('Console_Desc_', type, '');

    typeFieldsElement.innerHTML = typeModule ? typeModule.markup : '';
    applyLocalization(typeFieldsElement);

    typeFields = (typeModule?.fields || []).map((field) => ({
        ...field,
        input: typeFieldsElement.querySelector(`#${field.id}`),
    }));

    // These controls arrive after the switcher's blanket read-only pass, so each takes the document's state
    // and its dirty-marking listener as it is bound.
    const readOnly = !isFormEditable();
    for (const field of typeFields) {
        field.input.disabled = readOnly;
        field.input.addEventListener('input', onFormInput);
    }
}

// A type with no string of its own shows its raw id.
function typeLabel(type) {
    return localizedTypeString('Console_Type_', type, type);
}

// A type id is lowercase and the resource keys are title-cased, so the id is capitalized to name its key.
// t() returns the key itself when it does not resolve, which is what stands in for "no string defined".
function localizedTypeString(prefix, type, fallback) {
    const key = `${prefix}${type.charAt(0).toUpperCase()}${type.slice(1)}`;
    const value = t(key);

    return value === key ? fallback : value;
}

function findSessionType(typeId) {
    return (hostSessionTypes || []).find((sessionType) => sessionType.typeId === typeId) || null;
}

// The Type options, offering the types the host reports as registered that this client also has fields for,
// in the host's order. Before the attach that carries that list, the client's own set stands in.
function renderSessionTypeOptions() {
    const offered = hostSessionTypes === null
        ? clientTypeIds
        : hostSessionTypes
            .map((sessionType) => sessionType.typeId)
            .filter((typeId) => clientTypeIds.includes(typeId));

    const selected = sessionTypeSelect.value;
    sessionTypeSelect.replaceChildren();

    for (const typeId of offered) {
        const option = document.createElement('option');
        option.value = typeId;
        option.textContent = typeLabel(typeId);
        sessionTypeSelect.appendChild(option);
    }

    sessionTypeSelect.value = selected;
}

// True when the document names a type this client has no fields for, which makes the form read-only.
function isUnknownSessionType() {
    return !clientTypeIds.includes(currentConfig.type || 'shell');
}

// A stored option value as its control shows it. A value is typed by the TOML it was written as, not by the
// field, so one of the wrong shape shows blank.
function fieldText(value, kind) {
    if (kind === 'lines') {
        if (!Array.isArray(value)) {
            return '';
        }

        return value.filter((entry) => typeof entry === 'string' && entry.trim() !== '').join('\n');
    }

    if (typeof value !== 'string') {
        return '';
    }

    return value;
}

function populateForm(config) {
    const type = config.type || 'shell';
    const options = (config.optionsBySessionType || {})[type] || {};

    // The option labels need the localized strings, which arrive with the host handshake that also delivers
    // the first config.
    renderSessionTypeOptions();

    sessionTypeSelect.value = type;
    applyType(type);

    for (const field of typeFields) {
        field.input.value = fieldText(options[field.key], field.kind);
    }

    workingDirectoryInput.value = config.workingDirectory || '';
    environmentInput.value = Object.entries(config.environment || {})
        .map(([name, value]) => `${name}=${value}`)
        .join('\n');
    disabledBuiltInRunners = config.disabledBuiltInRunners || [];
    runnerCards.populate(config.runners);
    triggerCards.populate(config.triggers);
    shortcutCards.populate(config.shortcuts);
    renderShortcutRail();
}

// The runners the selected session type provides, shown above the console's own so the Run menu's behaviour
// is visible in the form. They are not part of the config: the host layers them under whatever the file
// declares, and re-reads them from the provider on every launch. Switching one off is the exception: the
// config names it by id, which is what the host resolves against.
function renderBuiltInRunners() {
    const runners = findSessionType(sessionTypeSelect.value)?.builtInRunners || [];
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
        toggle.disabled = !isFormEditable();

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
    // The config's own type stands in when the control holds no selection, which is what a type the host
    // does not offer leaves behind.
    const type = sessionTypeSelect.value || currentConfig.type || 'shell';

    // Every type's table is carried forward and only the selected type's is rewritten. An empty field
    // writes no key, matching what parsing a file that omits it gives.
    const optionsBySessionType = { ...(currentConfig.optionsBySessionType || {}) };
    const options = {};
    for (const field of typeFields) {
        if (field.kind === 'lines') {
            const values = splitLines(field.input.value);
            if (values.length > 0) {
                options[field.key] = values;
            }
            continue;
        }

        const text = field.kind === 'script' ? field.input.value.trimEnd() : field.input.value.trim();
        if (text !== '') {
            options[field.key] = text;
        }
    }

    if (Object.keys(options).length > 0) {
        optionsBySessionType[type] = options;
    } else {
        delete optionsBySessionType[type];
    }

    return {
        type,
        workingDirectory: workingDirectoryInput.value.trim(),
        optionsBySessionType,
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
    isWritable: isFormEditable,

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
    isWritable: isFormEditable,

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
    isWritable: isFormEditable,

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

// Injects a shortcut's text into the pty. Submitted through console/submit rather than as raw input, so the
// host owns how an invocation is entered at the prompt. The submit key cannot travel with the text: an app
// that reads a burst of stdin as one paste would treat a carriage return inside it as a newline.
function injectShortcut(text) {
    if (!text) {
        return;
    }
    client.sendNotification('console/submit', { invocation: text });
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

// Re-renders the form from the config already loaded, with the new type selected.
sessionTypeSelect.addEventListener('change', () => {
    populateForm({ ...currentConfig, type: sessionTypeSelect.value });
    applyWritableState();
    onFormInput();
});

// The controls every type shares. A type's own controls are bound as its markup is injected, since they
// exist only while that type is selected.
const formFields = [
    workingDirectoryInput,
    environmentInput,
];

for (const field of formFields) {
    field.addEventListener('input', onFormInput);
}

// The host mirrors the writable state as its enum name, so Writable is the only editable value. A view
// state that has not been seeded yet leaves the form editable.
function isDocumentWritable() {
    const writable = client.viewState.current?.writable;

    return writable === undefined || writable === 'Writable';
}

// Whether an edit is allowed to reach the document. A type this client has no fields for is read-only too.
function isFormEditable() {
    return isDocumentWritable() && !isUnknownSessionType();
}

// A read-only document disables the settings form so no edit marks the document dirty. The switcher's
// blanket pass over the sections runs first, so the card lists below decide the final state of the controls
// they own.
function applyWritableState() {
    settingsSwitcher.setReadOnly(!isFormEditable());

    runnerCards.refreshState();
    triggerCards.refreshState();
    shortcutCards.refreshState();
    renderBuiltInRunners();
}

client.viewState.onChanged(() => {
    applyWritableState();

    // The host reports the geometry this view will be read at, so a fit only counts from here.
    refitTerminal();
});

// Reopening from settings shows the terminal first. A reopen measures the terminal for the size it gives the
// new pty and paints a failed start into it, and neither works while it is the hidden half of the row.
reopenSettingsButton.addEventListener('click', () => {
    setSettingsVisible(false);
    reopenSession();
});

reopenTerminalButton.addEventListener('click', () => { reopenSession(); });

function unknownTypeText() {
    if (!isUnknownSessionType()) {
        return '';
    }

    return t('Console_UnknownType', currentConfig.type || '');
}

// The pip flags a config error, a config that diverges from the launched session, or a session that never
// started.
function updateAttention() {
    const diverged = launchedConfig !== null && !configsEqual(currentConfig, launchedConfig);
    const needsAttention = diverged || configError !== null || sessionStartFailed || isUnknownSessionType();
    pip.classList.toggle('hidden', !needsAttention);

    // The Reopen button stays enabled so the session can be restarted at any time. The accent colour appears
    // only when a reopen is needed to apply changed launch settings. The footer caption explains it.
    reopenSettingsButton.classList.toggle('cel-accent', diverged);

    // One slot, so a parse error wins: it is the one the surface showing it can also fix.
    settingsSwitcher.setNotice(configError || unknownTypeText() || sessionFailedText);
}

// Session lifecycle.
function showSessionFailed(message) {
    hideStartingVeil();
    sessionFailedMessage.textContent = message;
    sessionFailed.classList.remove('hidden');
    sessionStartFailed = true;
    sessionFailedText = message;
    updateAttention();
}

function hideSessionFailed() {
    sessionFailed.classList.add('hidden');
    sessionStartFailed = false;
    sessionFailedText = '';
    updateAttention();
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

// How long the attach waits for the view's size to settle before launching the session at whatever it has.
const ARRANGE_TIMEOUT_MS = 4000;

// How often the size is sampled while the page is off screen, where there are no animation frames.
const HIDDEN_SAMPLE_MS = 100;

// The next moment worth measuring at. A hidden page's geometry only changes when the host sizes its
// surface, so a timer samples it.
function nextSizeSample() {
    if (document.hidden) {
        return new Promise((resolve) => setTimeout(resolve, HIDDEN_SAMPLE_MS));
    }

    return new Promise((resolve) => requestAnimationFrame(resolve));
}

// The pty is created at the terminal's measured size, so measure only once the layout has settled. A resize
// that lands after the shell has painted costs the screen the output already on it.
async function waitForStableSize() {
    let waiting = true;

    const settled = (async () => {
        let previousWidth = -1;
        let previousHeight = -1;

        while (waiting) {
            await nextSizeSample();

            const width = terminalView.clientWidth;
            const height = terminalView.clientHeight;
            if (canMeasure() && width > 0 && height > 0 && width === previousWidth && height === previousHeight) {
                return;
            }

            previousWidth = width;
            previousHeight = height;
        }
    })();

    // A timer keeps the session starting even when the size never settles.
    const deadline = new Promise((resolve) => setTimeout(resolve, ARRANGE_TIMEOUT_MS));

    await Promise.race([settled, deadline]);
    waiting = false;
}

// The terminal size an attach or reopen carries. Zero means there was no box to measure.
function terminalSize(isSized) {
    if (!isSized) {
        return { cols: 0, rows: 0 };
    }

    return { cols: term.cols, rows: term.rows };
}

let requestInFlight = false;

// Renders an attach or reopen outcome: the launched config drives the pip, the replay fills the
// terminal, and the state decides between the veil, the failed overlay, and a live prompt.
function applyAttachResult(result) {
    // The registered session types are static host knowledge that rides along with the attach. The first
    // attach lands after the form is populated, hence the re-render.
    if (result && Array.isArray(result.sessionTypes)) {
        hostSessionTypes = result.sessionTypes;
        renderSessionTypeOptions();
        applyWritableState();
    }

    launchedConfig = null;
    if (result && result.launchedConfigToml) {
        try {
            launchedConfig = parseConsoleToml(result.launchedConfigToml, clientTypeIds);
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
        adoptSessionSize(result.cols, result.rows);
        term.write(result.replay);
    }

    if (result.state === 'ended') {
        hideStartingVeil();
        showSessionFailed(t('Console_SessionEnded'));
        return;
    }

    // A session that has not launched yet has nothing on its screen to show, and reports no startup phase
    // until it reaches one.
    if (result.startupPending || result.state === 'starting') {
        showStartingVeil();
        armVeilTimeout(VEIL_BACKSTOP_MS);
    } else {
        hideStartingVeil();
    }

    term.focus();
}

// Attaches this view to the live session, which has been running since the document opened.
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
    const isSized = fitTerminal();
    term.reset();

    try {
        const result = await client.sendRequest('console/attach', terminalSize(isSized));
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

    const isSized = fitTerminal();
    term.reset();

    try {
        const result = await client.sendRequest('console/reopen', terminalSize(isSized));
        applyAttachResult(result);
    } catch (error) {
        showSessionFailed((error && error.message) || String(error));
    } finally {
        requestInFlight = false;
    }
}

function applyContent(content) {
    try {
        currentConfig = parseConsoleToml(content, clientTypeIds);
        configError = null;
    } catch (error) {
        configError = (error && error.message) || t('Console_InvalidConfig');
    }
    populateForm(currentConfig);
    applyWritableState();
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
        // Persist the selected section and its scroll position so they survive a reopen. Which surface was
        // showing is deliberately not persisted: a reopen comes back to the terminal.
        onRequestState: () => JSON.stringify({
            activeSection: settingsSwitcher.selected(),
            scrollTop: settingsSwitcher.scrollTop(),
        }),
        onRestoreState: (stateJson) => {
            try {
                const state = JSON.parse(stateJson);
                // An unknown id leaves the default section selected.
                if (typeof state.activeSection === 'string') {
                    settingsSwitcher.select(state.activeSection);
                }
                // The switcher holds the offset until the surface is on screen to apply it to.
                if (typeof state.scrollTop === 'number' && state.scrollTop > 0) {
                    settingsSwitcher.setScrollTop(state.scrollTop);
                }
            } catch {
                // Ignore malformed state. Fall back to the defaults.
            }
        },
    });

    applyWritableState();
    await attachSession();
}

main();
