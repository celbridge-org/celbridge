// File viewer initialization for Celbridge WebView integration.
// Loads the document's file from the file server and shows the file. Shows the file again after each change on disk.
// Served over the loopback file server, so the shared client is addressed root-relative
// under /assets/ (resolved against the page's own loopback origin).

import celbridge from '/assets/celbridge-client/celbridge.js';
import { ContentLoadedReason, projectUrl } from '/assets/celbridge-client/api/document-api.js';
import { renderFile } from './file-render.js';

const client = celbridge;

function applyTheme(theme) {
    const isDark = theme === 'Dark';
    document.body.className = isDark ? 'theme-dark' : 'theme-light';
}

client.appState.onChanged((appState) => {
    if (appState.theme) {
        applyTheme(appState.theme);
    }
});

function buildResourceUrl(resourceKey) {
    // Cache-bust on every load so external changes immediately replace the rendered media. The file server's
    // no-store header is not enough, because a page reuses an image it has already loaded from the same URL.
    const cacheBuster = Date.now();
    return `${projectUrl(resourceKey)}?t=${cacheBuster}`;
}

function showFile(metadata) {
    const container = document.getElementById('file-viewer-container');
    renderFile(container, metadata.fileName || '', buildResourceUrl(metadata.resourceKey));
}

async function initializeEditor() {
    try {
        await client.initializeDocument({
            onContent: (_content, metadata) => {
                showFile(metadata);
            },
            onExternalChange: async () => {
                try {
                    const result = await client.document.load();
                    showFile(result.metadata);
                } catch (e) {
                    console.error('[FileViewer] Failed to reload content:', e);
                }

                client.document.notifyContentLoaded(ContentLoadedReason.ExternalReload);
            }
        });
    } catch (e) {
        console.error('[FileViewer] Failed to initialize:', e);
    }
}

initializeEditor();
