// Terminal initialization and event handling for Celbridge Console.
// Uses console-client.js for JSON-RPC communication with the host.

import { consoleClient } from './console-client.js';

const darkTheme = window.VSCodeTerminalThemes.dark;
const lightTheme = window.VSCodeTerminalThemes.light;

// Default to the OS theme so the first paint matches the system; the host delivers the app's effective
// theme (which may be an in-app override) over the app-state store, pushed on connect.
const initialIsDark = typeof window !== 'undefined' && window.matchMedia
    ? window.matchMedia('(prefers-color-scheme: dark)').matches
    : true;
const initialTheme = initialIsDark ? darkTheme : lightTheme;

const term = new Terminal({
    theme: initialTheme,
    fontFamily: "'Cascadia Mono', monospace"
});

const fitAddon = new FitAddon.FitAddon();
term.loadAddon(fitAddon);

const clipboardAddon = new ClipboardAddon.ClipboardAddon();
term.loadAddon(clipboardAddon);

// Web link support

function handleLink(ev, uri) {
    ev?.preventDefault?.();

    // Clicked links trigger the WebView2 to navigate to the URI.
    // We intercept the navigation in the WebView2 and open the system browser instead.
    location.assign(uri);
}

term.options.linkHandler = {
    activate: handleLink,
    hover() { },
    leave() { },
    allowNonHttpProtocols: true
};

const webLinksAddon = new WebLinksAddon.WebLinksAddon(handleLink);
term.loadAddon(webLinksAddon);

// Wait for the bundled monospace font to load before opening the terminal.
// xterm.js measures cell dimensions when the terminal is opened, so the font
// must be available by then. Otherwise FitAddon measures against the fallback
// font and reports an inflated column count, which causes TUI content to be
// drawn wider than the viewport.
await document.fonts.load('1em "Cascadia Mono"');

const terminalElement = document.getElementById('terminal');
term.open(terminalElement);

// Xterm.js creates a hidden textarea for input but does not give it an id or
// name. DevTools flags this as an a11y warning, so set a name after open.
terminalElement.querySelector('.xterm-helper-textarea')?.setAttribute('name', 'terminal-input');

// Apply the xterm color theme. The page background (and the 8px margin around the terminal) follows
// html[data-theme] via CSS, set by the celbridge client, so it is not set here.
function applyTheme(isDark) {
    try {
        term.options.theme = isDark ? darkTheme : lightTheme;
    } catch (e) {
        // ignore if term.options isn't available yet
    }
}

// Force the wheel to always scroll the xterm.js viewport, even when the TUI
// has enabled application cursor mode (DECCKM) — without this, xterm.js
// converts wheel events into arrow-key sequences that go to the TUI, and
// Claude Code in particular notices and complains that "Scroll wheel is
// sending arrow keys". We attach the handler at the DOM capture phase on
// the terminal container so it runs before xterm.js's own wheel listener.
// Holding Shift bypasses our handler and lets xterm.js handle the event
// normally (which then either scrolls or sends to TUI per its default).
terminalElement.addEventListener('wheel', (ev) => {
    if (ev.shiftKey) {
        return;
    }
    ev.preventDefault();
    ev.stopPropagation();
    const lines = Math.sign(ev.deltaY) * Math.max(1, Math.round(Math.abs(ev.deltaY) / 40));
    term.scrollLines(lines);
}, { capture: true, passive: false });

window.addEventListener('resize', () => {
    fitAddon.fit();
});

// Supress context menu if user clicks in shim area at the bottom of the console window
document.addEventListener('contextmenu', (e) => {
    if (!e.composedPath().some(el => el.classList && el.classList.contains('xterm'))) {
        e.preventDefault();
    }
});

term.onResize(({ cols, rows }) => {
    // Notify console host that the terminal has resized
    consoleClient.sendResize(cols, rows);
});

// Fit the terminal size to the available space in the host WebView2 control.
fitAddon.fit();

term.attachCustomKeyEventHandler((ev) => {
    // Copy: if there's a selection, ctl-c copies it to the clipboard
    if ((ev.ctrlKey || ev.metaKey) && ev.key.toLowerCase() === 'c') {
        if (term.hasSelection()) {
            navigator.clipboard.writeText(term.getSelection());
            ev.preventDefault?.();
            return false; // consume the event
        }
        return true; // no selection, ctrl-c clears the input buffer
    }

    // Paste: ctrl+v is delivered as a native paste event on xterm's hidden
    // textarea, which xterm.js handles internally. Returning false stops xterm
    // from also sending the Ctrl+V control char to the PTY. We deliberately do
    // not call navigator.clipboard.readText() here as that would prompt the
    // user for clipboard read permission when the WebView is served over http.
    if ((ev.ctrlKey || ev.metaKey) && ev.key.toLowerCase() === 'v') {
        return false;
    }

    // Swallow Ctrl+D and Ctrl+Z so they do not reach the PTY. On Windows, IPython treats
    // Ctrl+D as quit-with-confirmation and the shell layer treats Ctrl+Z as the legacy
    // MS-DOS EOF marker; prompt_toolkit's default Ctrl+Z handler just inserts a literal
    // ^Z into the line buffer. Neither behaviour is what a user pressing these keys in
    // the Celbridge console expects, and there is no undo binding to forward to anyway.
    if (ev.ctrlKey && (ev.key === 'd' || ev.key === 'z')) {
        return false;
    }

    return true;
});

// Send terminal input to host via JSON-RPC
term.onData(data => {
    consoleClient.sendInput(data);
});

// Register handlers for host notifications
consoleClient.onWrite((text) => {
    term.write(text);
});

consoleClient.onFocus(() => {
    term.focus();
});

consoleClient.appState.onChanged((appState) => {
    if (appState.theme) {
        applyTheme(appState.theme === 'Dark');
    }
});
// No subscribe call: the host pushes the current theme on connect, caught by the handler above.

consoleClient.onInjectCommand((command) => {
    // Send Ctrl+U (erase-line) to clear any partial input at the readline prompt,
    // then inject the command. Unlike Ctrl+C (\x03), Ctrl+U is not a signal
    // character and will not interrupt a script that is currently running.
    // Use \r (carriage return) as this is what terminals send for Enter.
    consoleClient.sendInput('\x15' + command + '\r');
});

// Reports which edit verbs the console can do. Copy needs a selection; paste and select-all are always
// available. Sent on focus and whenever the selection changes so the Edit menu enables correctly.
function reportConsoleEditAvailability() {
    consoleClient.notifyEditAvailability({
        canCopy: term.hasSelection(),
        canPaste: true,
        canSelectAll: true
    });
}

// Report focus + edit availability to the host, and clear focus on blur. On the Skia heads GotFocus does not
// fire for clicks inside the console WebView, so DOM focus is the reliable signal it became active.
let hasReportedConsoleFocus = false;
document.addEventListener('focusin', () => {
    if (!hasReportedConsoleFocus) {
        hasReportedConsoleFocus = true;
        consoleClient.notifyFocusReceived();
    }
    reportConsoleEditAvailability();
});

// Release the terminal's focus when the host signals focus moved to another panel, so its caret stops
// on heads where WebView and host focus are not integrated (Skia).
consoleClient.onReleaseFocus(() => {
    hasReportedConsoleFocus = false;
    const active = document.activeElement;
    if (active && active !== document.body && typeof active.blur === 'function') {
        active.blur();
    }
});

// Copy and paste are host-mediated: the WebView's own JS clipboard is blocked on the Skia WKWebView, so
// the host fetches the selection for copy and writes clipboard text straight to the pty for paste.
// Select-all runs here.
consoleClient.onGetSelection(() => term.getSelection());
consoleClient.onPerformEdit((command) => {
    if (command === 'selectAll') {
        term.selectAll();
    }
});

term.onSelectionChange(() => reportConsoleEditAvailability());
