// Screenplay viewer initialization for Celbridge WebView integration.
// Uses celbridge.js for JSON-RPC communication with the host.

import celbridge from 'https://shared.celbridge/celbridge-client/celbridge.js';

// Only proceed if running in WebView
if (!window.isWebView) {
    console.log('Not running in WebView, skipping client initialization');
}

const client = celbridge;

// Apply theme to the body element
function applyTheme(theme) {
    const isDark = theme === 'Dark';
    document.body.className = isDark ? 'theme-dark' : 'theme-light';
}

// Listen for theme changes
client.theme.onChanged((theme) => {
    applyTheme(theme);
});

// Listen for external content changes (when scene data is updated)
client.document.onExternalChange(async () => {
    try {
        const result = await client.document.load();
        document.getElementById('screenplay-container').innerHTML = result.content;
    } catch (e) {
        console.error('[Screenplay] Failed to reload content:', e);
    }
});

// Initialize the editor
async function initializeEditor() {
    try {
        // Enable debug logging during development
        // client.setLogLevel('debug');

        // Initialize the client - this loads content from C#
        const result = await client.initialize();

        // Apply initial theme
        applyTheme(client.theme.current);

        // Set the screenplay content
        document.getElementById('screenplay-container').innerHTML = result.content;

    } catch (e) {
        console.error('[Screenplay] Failed to initialize:', e);
    }
}

// Start initialization
initializeEditor();
