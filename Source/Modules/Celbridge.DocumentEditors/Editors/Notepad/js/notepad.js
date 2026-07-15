// Notepad utility document for Celbridge WebView integration.
// A minimal per-project scratchpad: its text is persisted as a JSON state blob in the utils: root
// through the standard document save contract. Served over the loopback file server, so the shared
// client is addressed root-relative under /assets/ (resolved against the page's own loopback origin).

import celbridge from '/assets/celbridge-client/celbridge.js';
import { ContentLoadedReason } from '/assets/celbridge-client/api/document-api.js';

const client = celbridge;

const inputEl = document.getElementById('notepad-input');

// Gates change notifications while the document is read-only or being loaded by the framework, so a
// non-user write does not schedule an auto-save.
let suppressChangeNotifications = false;
let notifyTimer = null;

function applyTheme(theme) {
    document.body.className = theme === 'Dark' ? 'theme-dark' : 'theme-light';
}

function applyReadOnlyState(readOnly) {
    suppressChangeNotifications = readOnly;
    inputEl.readOnly = readOnly;
}

function parseText(content) {
    if (!content) {
        return '';
    }
    try {
        const state = JSON.parse(content);
        return typeof state.text === 'string' ? state.text : '';
    } catch (e) {
        // Tolerate a file that is not the JSON envelope (e.g. hand-edited) by treating it as plain text.
        return content;
    }
}

function scheduleNotifyChanged() {
    if (suppressChangeNotifications) {
        return;
    }
    if (notifyTimer !== null) {
        clearTimeout(notifyTimer);
    }
    notifyTimer = setTimeout(() => {
        notifyTimer = null;
        client.document.notifyChanged();
    }, 500);
}

inputEl.addEventListener('input', scheduleNotifyChanged);

client.appState.onChanged((appState) => {
    if (appState.theme) {
        applyTheme(appState.theme);
    }
});

client.viewState.onChanged((viewState) => {
    if (viewState.writable) {
        applyReadOnlyState(viewState.writable !== 'Writable');
    }
});

async function initializeEditor() {
    try {
        await client.initializeDocument({
            onContent: (content) => {
                inputEl.value = parseText(content);
            },
            onRequestSave: async () => {
                const payload = JSON.stringify({ text: inputEl.value });
                try {
                    await client.document.save(payload);
                } catch (e) {
                    console.error('[Notepad] Failed to save:', e);
                }
            },
            onExternalChange: async () => {
                try {
                    const { content } = await client.document.load();
                    inputEl.value = parseText(content);
                } catch (e) {
                    console.error('[Notepad] Failed to reload content:', e);
                }

                client.document.notifyContentLoaded(ContentLoadedReason.ExternalReload);
            },
            onRequestState: () => JSON.stringify({
                scrollTop: inputEl.scrollTop,
                selectionStart: inputEl.selectionStart,
                selectionEnd: inputEl.selectionEnd
            }),
            onRestoreState: (stateJson) => {
                try {
                    const state = JSON.parse(stateJson);
                    if (typeof state.selectionStart === 'number' && typeof state.selectionEnd === 'number') {
                        inputEl.selectionStart = state.selectionStart;
                        inputEl.selectionEnd = state.selectionEnd;
                    }
                    if (typeof state.scrollTop === 'number') {
                        inputEl.scrollTop = state.scrollTop;
                    }
                } catch (e) {
                    console.error('[Notepad] Failed to restore state:', e);
                }
            }
        });
    } catch (e) {
        console.error('[Notepad] Failed to initialize:', e);
    }
}

initializeEditor();
