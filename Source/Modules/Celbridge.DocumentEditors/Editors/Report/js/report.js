// Report viewer for Celbridge WebView integration.
// Renders a .report file: the operation's summary, its fact sections as labelled readings, and its
// findings grouped so repeated occurrences of one finding read as a single row.
//
// Read-only by design: no save timer, no change notifications, and no cel.viewState subscription,
// since there is no writable state to apply.

import celbridge from '/assets/celbridge-client/celbridge.js';
import { ContentLoadedReason } from '/assets/celbridge-client/api/document-api.js';
import { parseReport } from './report-model.js';
import { renderReport, showParseError } from './report-view.js';

const client = celbridge;

// A report action opens the resource it names and reveals it in the Explorer, so the reader keeps
// their place in the tree as well as landing in the file. The host owns the position encoding: the
// action carries line and column, and document.open composes what the target editor reads.
async function openResource(resource, line, column) {
    try {
        await client.cel.document.open(resource, '', false, true, line, column);
        await client.cel.explorer.select(resource);
    } catch (e) {
        console.error('[Report] Failed to open resource:', e);
    }
}

function applyContent(content) {
    try {
        renderReport(parseReport(content), openResource);
    } catch (e) {
        console.error('[Report] Failed to parse report:', e);
        showParseError();
    }
}

async function initializeEditor() {
    try {
        await client.initializeDocument({
            onContent: (content) => {
                applyContent(content);
            },
            onExternalChange: async () => {
                try {
                    const result = await client.document.load();
                    applyContent(result.content);
                } catch (e) {
                    console.error('[Report] Failed to reload content:', e);
                }

                client.document.notifyContentLoaded(ContentLoadedReason.ExternalReload);
            }
        });
    } catch (e) {
        console.error('[Report] Failed to initialize:', e);
    }
}

initializeEditor();
