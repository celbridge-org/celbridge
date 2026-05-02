// Terminal initialization and event handling for Celbridge Console.
// Uses console-client.js for JSON-RPC communication with the host.

import { consoleClient } from './console-client.js';

const darkTheme = window.VSCodeTerminalThemes.dark;
const lightTheme = window.VSCodeTerminalThemes.light;

// Default to dark theme, will be updated by host
const initialTheme = darkTheme;

// The host page injects window.celbridgeConsoleConfig before any scripts run.
// Falls back to undefined on non-Windows hosts, which xterm.js tolerates.
const windowsBuildNumber = window.celbridgeConsoleConfig?.windowsBuildNumber;

const term = new Terminal({
    theme: initialTheme,
    fontFamily: "'Cascadia Mono', monospace",
    scrollback: 10000,
    windowsPty: {
        backend: 'conpty',
        buildNumber: windowsBuildNumber
    }
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

// Function to apply theme based on host application
function applyTheme(isDark) {
    try {
        const theme = isDark ? darkTheme : lightTheme;
        term.options.theme = theme;
        document.body.style.background = theme.background;
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

// Debounce window resize so we only refit the terminal once the user stops
// dragging the panel edge. Each fit triggers a SetSize RPC to ConPTY which
// triggers a SIGWINCH redraw in the hosted TUI, and those redraws accumulate
// in scrollback. Coalescing to a single fit at the end of the gesture keeps
// scrollback clean while preserving full history.
const RESIZE_DEBOUNCE_MS = 250;
let resizeDebounceTimer = null;

window.addEventListener('resize', () => {
    if (resizeDebounceTimer !== null) {
        clearTimeout(resizeDebounceTimer);
    }
    resizeDebounceTimer = setTimeout(() => {
        resizeDebounceTimer = null;
        // Reflow naturally — xterm.js will push viewport rows into scrollback
        // on shrink and pull them back on grow. That is the Windows Terminal
        // behaviour: TUI redraws accumulate as scrollback rather than being
        // erased, so the user can scroll back through earlier renders.
        fitAddon.fit();
    }, RESIZE_DEBOUNCE_MS);
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
    // F11 is handled by WebViewFactory's injected script via JSON-RPC
    // Just consume the event here to prevent default browser behavior
    if (ev.key === 'F11') {
        return false;
    }

    // Copy: if there's a selection, ctl-c copies it to the clipboard
    if ((ev.ctrlKey || ev.metaKey) && ev.key.toLowerCase() === 'c') {
        if (term.hasSelection()) {
            navigator.clipboard.writeText(term.getSelection());
            ev.preventDefault?.();
            return false; // consume the event
        }
        return true; // no selection, ctrl-c clears the input buffer
    }

    // Paste: ctrl+v pastes clipboard into the terminal
    if ((ev.ctrlKey || ev.metaKey) && ev.key.toLowerCase() === 'v') {
        navigator.clipboard.readText().then(text => term.paste(text));
        ev.preventDefault?.();
        return false;  // consume the event
    }

    // No exit: Block Ctrl+D and Ctrl+Z (Windows EOF)
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

consoleClient.onSetTheme((theme) => {
    applyTheme(theme === 'dark');
});

consoleClient.onInjectCommand((command) => {
    // Send Ctrl+U (erase-line) to clear any partial input at the readline prompt,
    // then inject the command. Unlike Ctrl+C (\x03), Ctrl+U is not a signal
    // character and will not interrupt a script that is currently running.
    // Use \r (carriage return) as this is what terminals send for Enter.
    consoleClient.sendInput('\x15' + command + '\r');
});
