// Terminal initialization and event handling for Celbridge Console.
// Uses console-client.js for JSON-RPC communication with the host.

import { consoleClient } from './console-client.js';

const darkTheme = window.VSCodeTerminalThemes.dark;
const lightTheme = window.VSCodeTerminalThemes.light;

// Default to dark theme, will be updated by host
const initialTheme = darkTheme;

const term = new Terminal({
    theme: initialTheme,
    windowsPty: {
        backend: 'conpty'
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

term.open(document.getElementById('terminal'));

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
    console.log(`Terminal now ${cols} cols by ${rows} rows`);

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
