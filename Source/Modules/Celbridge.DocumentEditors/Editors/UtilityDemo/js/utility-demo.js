// Utility demo: a small per-project surface built on the shared section switcher, the component the console
// settings surface is also built on. Its notes are persisted as a JSON state blob in the utils: root through
// the standard document save contract. Served over the loopback file server, so the shared client is
// addressed root-relative under /assets/ (resolved against the page's own loopback origin).

import celbridge from '/assets/celbridge-client/celbridge.js';
import { ContentLoadedReason } from '/assets/celbridge-client/api/document-api.js';
import { attachSectionSwitcher } from '/assets/celbridge-client/ui/section-switcher.js';

const client = celbridge;

const notesInput = document.getElementById('notes-input');
const showNotesButton = document.getElementById('show-notes');

const switcher = attachSectionSwitcher(document.getElementById('demo-switcher'));

// Gates change notifications while the document is read-only or being loaded by the framework, so a
// non-user write does not schedule an auto-save.
let suppressChangeNotifications = false;

function applyReadOnlyState(readOnly) {
    suppressChangeNotifications = readOnly;
    notesInput.readOnly = readOnly;
}

function parseText(content) {
    if (!content) {
        return '';
    }
    try {
        const state = JSON.parse(content);
        return typeof state.text === 'string' ? state.text : '';
    } catch {
        // Tolerate a file that is not the JSON envelope (e.g. hand-edited) by treating it as plain text.
        return content;
    }
}

// The host resets the document's save timer on every notification, so the wait before the write is already
// a trailing debounce and each edit can report as it happens.
function notifyChanged() {
    if (suppressChangeNotifications) {
        return;
    }

    client.document.notifyChanged();
}

notesInput.addEventListener('input', notifyChanged);

// The host's dialog rather than the browser's: nothing in the application handles a JavaScript alert(), so a
// bare one risks being a silent no-op on the macOS head.
async function showNotes() {
    try {
        await client.dialog.alert('Notes', notesInput.value || 'Nothing saved yet.');
    } catch (e) {
        console.error('[UtilityDemo] Failed to show the notes:', e);
    }
}

showNotesButton.addEventListener('click', () => { showNotes(); });

client.viewState.onChanged((viewState) => {
    if (viewState.writable) {
        applyReadOnlyState(viewState.writable !== 'Writable');
    }
});

async function initializeEditor() {
    try {
        await client.initializeDocument({
            onContent: (content) => {
                notesInput.value = parseText(content);
            },
            onRequestSave: async () => {
                const payload = JSON.stringify({ text: notesInput.value });
                try {
                    await client.document.save(payload);
                } catch (e) {
                    console.error('[UtilityDemo] Failed to save:', e);
                }
            },
            onExternalChange: async () => {
                try {
                    const { content } = await client.document.load();
                    notesInput.value = parseText(content);
                } catch (e) {
                    console.error('[UtilityDemo] Failed to reload content:', e);
                }

                client.document.notifyContentLoaded(ContentLoadedReason.ExternalReload);
            },
            onRequestState: () => JSON.stringify({
                activeSection: switcher.selected(),
                scrollTop: switcher.scrollTop()
            }),
            onRestoreState: (stateJson) => {
                try {
                    const state = JSON.parse(stateJson);
                    if (typeof state.activeSection === 'string') {
                        switcher.select(state.activeSection);
                    }
                    if (typeof state.scrollTop === 'number' && state.scrollTop > 0) {
                        switcher.setScrollTop(state.scrollTop);
                    }
                } catch (e) {
                    console.error('[UtilityDemo] Failed to restore state:', e);
                }
            }
        });
    } catch (e) {
        console.error('[UtilityDemo] Failed to initialize:', e);
    }
}

initializeEditor();
