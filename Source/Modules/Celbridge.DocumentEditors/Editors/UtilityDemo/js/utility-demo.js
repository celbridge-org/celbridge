// Process utility document for Celbridge WebView integration.
// A persistent per-project surface that lives in the Utility Panel and can be docked into a document tab. It
// demonstrates the long-running "process" pattern. Its notes are persisted as a JSON state blob in the utils:
// root through the standard document save contract. Served over the loopback file server, so the shared client
// is addressed root-relative under /assets/ (resolved against the page's own loopback origin).

import celbridge from '/assets/celbridge-client/celbridge.js';
import { ContentLoadedReason } from '/assets/celbridge-client/api/document-api.js';
import { attachSplitter } from '/assets/celbridge-client/ui/splitter.js';
import { attachNavTabs } from '/assets/celbridge-client/ui/nav-tabs.js';

const client = celbridge;

const inputEl = document.getElementById('process-input');

// Demonstrate the shared splitter: drag the divider to resize the left pane. Double-click to reset.
const demoSplit = document.querySelector('.demo-split');
const demoLeftPane = document.querySelector('.demo-pane-left');
const demoSplitter = document.getElementById('demo-splitter');
let demoDragStartWidth = 0;
attachSplitter(demoSplitter, {
    onDragStart() {
        demoDragStartWidth = demoLeftPane.getBoundingClientRect().width;
    },
    onDrag(deltaX) {
        const maxWidth = demoSplit.getBoundingClientRect().width - 80 - 8;
        demoLeftPane.style.width = Math.max(80, Math.min(demoDragStartWidth + deltaX, maxWidth)) + 'px';
    },
    onReset() {
        demoLeftPane.style.width = '45%';
    },
});

// Demonstrate the shared inspector rail and nav tab strip: the rail's settings button toggles the inspector
// panel, and the tab strip switches which of the panel's sections is shown.
const inspectorPanel = document.getElementById('inspector-panel');
const inspectorToggle = document.getElementById('inspector-toggle');
const inspectorSections = Array.from(document.querySelectorAll('.inspector-section'));
const inspectorHeaders = Array.from(document.querySelectorAll('.cel-section-header'));

attachNavTabs(document.getElementById('inspector-tabs'), {
    onChange(sectionId) {
        for (const section of inspectorSections) {
            section.classList.toggle('hidden', section.dataset.section !== sectionId);
        }
        for (const header of inspectorHeaders) {
            header.classList.toggle('hidden', header.dataset.section !== sectionId);
        }
    },
});

inspectorToggle.addEventListener('click', () => {
    const visible = inspectorPanel.classList.toggle('hidden') === false;
    inspectorToggle.classList.toggle('selected', visible);
});

// Gates change notifications while the document is read-only or being loaded by the framework, so a
// non-user write does not schedule an auto-save.
let suppressChangeNotifications = false;
let notifyTimer = null;

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
                    console.error('[Process] Failed to save:', e);
                }
            },
            onExternalChange: async () => {
                try {
                    const { content } = await client.document.load();
                    inputEl.value = parseText(content);
                } catch (e) {
                    console.error('[Process] Failed to reload content:', e);
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
                    console.error('[Process] Failed to restore state:', e);
                }
            }
        });
    } catch (e) {
        console.error('[Process] Failed to initialize:', e);
    }
}

initializeEditor();
