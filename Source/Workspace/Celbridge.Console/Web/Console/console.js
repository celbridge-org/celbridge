// Console document editor. One WebView carries two channels: the standard document content/save channel
// (the .console TOML edited through the settings form) and a custom console/* RPC channel (the live pty
// terminal). This file is the terminal itself and the page the two surfaces share. The settings form and the
// live session are modules of their own.

import celbridge from '/assets/celbridge-client/celbridge.js';
import { ContentLoadedReason } from '/assets/celbridge-client/api/document-api.js';
import { attachStackLayout } from '/assets/celbridge-client/ui/stack-layout.js';
import { createConsoleSettings } from './console-settings.js';
import { createConsoleSession } from './console-session.js';
import { createWheelStepCounter, createNotchPacer } from './console-scroll.js';

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
    // Scales every wheel delta, on the paths xterm scrolls itself as well as the one below. Terminal
    // lines are shorter than the document lines a browser assumes, so a notch of a wheel would otherwise
    // cover twice the ground here that it covers elsewhere.
    scrollSensitivity: 0.5,
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

// Fits the terminal to its box, and reports whether that box was one worth measuring. A pty created at a
// placeholder size has to be resized once the view is shown, which costs the screen the output already
// painted on it, so the host's word on the geometry is what a fit waits for.
function fitTerminal() {
    if (!client.view.canMeasure(terminalElement)) {
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

// How long the attach waits for the terminal's size to settle. It sits inside the host's own launch wait
// (ConsoleSession.ViewSizeTimeoutMs), which holds the pty until a view reports a size, so raising it past
// that bound would have the launch give up first and create the pty at the fallback size.
const SIZE_SETTLE_TIMEOUT_MS = 4000;

const appElement = document.getElementById('app');
const railElement = appElement.querySelector('.cel-rail');
const openSettingsButton = document.getElementById('open-settings');
const terminalView = document.getElementById('terminal-view');
const closeSettingsButton = document.getElementById('close-settings');
const reopenSettingsButton = document.getElementById('reopen-settings');

const settings = createConsoleSettings({ client });

const session = createConsoleSession({
    client,
    term,
    settings,
    fitTerminal,
    waitForTerminalSize: () => client.view.waitForStableSize(terminalElement, {
        timeoutMs: SIZE_SETTLE_TIMEOUT_MS,
    }),
});

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
        session.sendInput(params.text);
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

const wheelLineCounter = createWheelStepCounter();
const wheelNotchCounter = createWheelStepCounter();

// A shell or TUI scrolls about this many lines for each wheel notch it is sent, so one notch stands in
// for that much of the travel counted here.
const LINES_PER_NOTCH = 3;

// The distance a wheel notch reports. The notches forwarded below carry at least this, so the terminal
// counts each one as a notch of its own rather than thinning the stream a second time.
const NOTCH_DELTA_PIXELS = 100;

// How far past one line a forwarded notch is sized, so the terminal's own threshold never swallows one
// at a line height this distance would otherwise fall short of.
const NOTCH_LINE_MARGIN = 2;

// Set while a forwarded notch is being dispatched, so the handler lets its own event through untouched.
let forwardingNotches = false;

// Stands in for the line height until the rows container can be measured, so the wheel still scrolls if
// the measurement is not available. Close to the height xterm lays out at the default font size.
const FALLBACK_LINE_HEIGHT = 17;

// The height xterm lays a line out at, which the wheel converts pixel deltas against. Measured on demand
// and held until the next fit, as a font or zoom change moves it.
let terminalLineHeight = 0;

function getTerminalLineHeight() {
    if (terminalLineHeight > 0) {
        return terminalLineHeight;
    }

    // xterm lays each line out as its own row element. The rows container computes a line height of
    // "normal", so the measurement has to come off a row rather than the container.
    const rowElement = terminalRows ? terminalRows.firstElementChild : null;
    if (rowElement) {
        const measuredLineHeight = rowElement.getBoundingClientRect().height;
        if (measuredLineHeight > 0) {
            terminalLineHeight = measuredLineHeight;
        }
    }

    return terminalLineHeight > 0 ? terminalLineHeight : FALLBACK_LINE_HEIGHT;
}

// Where the notches the pacer releases are aimed. The terminal reads the position to work out which cell
// the gesture is over, so they carry the last real event's target and coordinates.
let notchTarget = null;
let notchClientX = 0;
let notchClientY = 0;

// Sends one notch on to the terminal, which forwards it to whatever is running: a mouse event for a TUI
// that tracks the mouse, an arrow key for one that does not.
function releaseNotch(direction) {
    if (notchTarget === null) {
        return;
    }

    // The terminal counts a forwarded notch in lines before passing it on, so it has to clear one line
    // whatever the line height rather than trusting a fixed distance to.
    const clearsOneLine = getTerminalLineHeight() * NOTCH_LINE_MARGIN / term.options.scrollSensitivity;
    const notchDelta = Math.max(NOTCH_DELTA_PIXELS, clearsOneLine);

    forwardingNotches = true;
    try {
        notchTarget.dispatchEvent(new WheelEvent('wheel', {
            deltaY: direction * notchDelta,
            deltaMode: 0,
            clientX: notchClientX,
            clientY: notchClientY,
            bubbles: true,
            cancelable: true,
        }));
    } finally {
        forwardingNotches = false;
    }
}

const notchPacer = createNotchPacer(releaseNotch);

// Take the wheel over from the terminal, so the trackpad's event rate does not reach whatever is running.
// At the shell prompt this scrolls the viewport directly. Shift bypasses it.
terminalElement.addEventListener('wheel', (event) => {
    if (event.shiftKey || forwardingNotches) {
        return;
    }

    const terminalMetrics = {
        lineHeight: getTerminalLineHeight(),
        rows: term.rows,
        sensitivity: term.options.scrollSensitivity,
    };

    event.preventDefault();
    event.stopPropagation();

    // A TUI scrolls itself, so the wheel has to reach it. The terminal passes on at most one notch per
    // event it sees, which hands a trackpad's far higher event rate straight to the TUI, so the stream is
    // thinned to whole notches here and the events between them are dropped.
    if (term.modes.mouseTrackingMode !== 'none' ||
        term.buffer.active.type === 'alternate') {
        terminalMetrics.linesPerStep = LINES_PER_NOTCH;
        notchTarget = event.target;
        notchClientX = event.clientX;
        notchClientY = event.clientY;
        notchPacer.queue(wheelNotchCounter(event, terminalMetrics));

        return;
    }

    const lines = wheelLineCounter(event, terminalMetrics);
    if (lines !== 0) {
        term.scrollLines(lines);
    }
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
        terminalLineHeight = 0;
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
    settings.setVisible(visible);
    terminalView.classList.toggle('hidden', visible);
    // Takes the rail off screen while the surface is up, which the stylesheet keys on.
    appElement.dataset.surface = visible ? 'settings' : 'terminal';

    // Reported here rather than at the end, so the surface the settings replace stops claiming the
    // clipboard on the way in as well as on the way out.
    reportEditAvailability();

    if (visible) {
        settings.focusSelectedSection();
        return;
    }

    refitTerminal();
    term.focus();
}

closeSettingsButton.addEventListener('click', () => setSettingsVisible(false));

// Reopening from settings shows the terminal first. A reopen measures the terminal for the size it gives the
// new pty and paints a failed start into it, and neither works while it is the hidden half of the row.
reopenSettingsButton.addEventListener('click', () => {
    setSettingsVisible(false);
    session.reopen();
});

document.addEventListener('keydown', (event) => {
    // Escape belongs to whatever gesture is in progress first: a card drag cancels itself with it.
    if (event.key === 'Escape' &&
        !event.defaultPrevented &&
        settings.isVisible()) {
        setSettingsVisible(false);
        event.preventDefault();
    }
});

client.viewState.onChanged(() => settings.applyWritableState());

// The host reports the geometry this view will be read at, so a fit only counts from here.
client.view.onChanged(refitTerminal);

async function main() {
    await client.initializeDocument({
        onContent: (content) => settings.applyContent(content),
        onRequestSave: async () => {
            await settings.save();
        },
        onExternalChange: async () => {
            try {
                const result = await client.document.load();
                settings.applyContent(result.content);
            } catch (error) {
                console.error('[Console] Failed to reload config:', error);
            }
            // Ack the reload so the host's external-change handshake does not time out.
            client.document.notifyContentLoaded(ContentLoadedReason.ExternalReload);
        },
        onRequestState: () => settings.requestState(),
        onRestoreState: (stateJson) => settings.restoreState(stateJson),
    });

    settings.applyWritableState();
    await session.attach();
}

main();
