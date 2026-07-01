// Screenplay viewer initialization for Celbridge WebView integration.
// Uses celbridge.js for JSON-RPC communication with the host.

import celbridge from '/assets/celbridge-client/celbridge.js';
import { ContentLoadedReason } from '/assets/celbridge-client/api/document-api.js';

const client = celbridge;

// Apply theme to the body element
function applyTheme(theme) {
    const isDark = theme === 'Dark';
    document.body.className = isDark ? 'theme-dark' : 'theme-light';
}

client.appState.onChanged((appState) => {
    if (appState.theme) {
        applyTheme(appState.theme);
    }
});

// Initialize the editor
async function initializeEditor() {
    try {
        // Enable debug logging during development
        // client.setLogLevel('debug');

        await client.initializeDocument({
            onContent: (content) => {
                // Set the screenplay content
                document.getElementById('screenplay-container').innerHTML = content;
            },
            onExternalChange: async () => {
                try {
                    const result = await client.document.load();
                    document.getElementById('screenplay-container').innerHTML = result.content;
                } catch (e) {
                    console.error('[Screenplay] Failed to reload content:', e);
                }

                // Signal to the framework that the reload cycle is complete. Screenplay has no
                // editor state to preserve, but emitting the signal keeps the reload contract
                // uniform across editors and avoids the framework's 5s timeout.
                client.document.notifyContentLoaded(ContentLoadedReason.ExternalReload);
            }
        });
    } catch (e) {
        console.error('[Screenplay] Failed to initialize:', e);
    }
}

// Start initialization
initializeEditor();
