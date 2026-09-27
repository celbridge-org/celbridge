// Wires up the in-HTML editor toolbar: view-mode buttons, the optional button
// that reloads the preview, and the optional snippet insertion menu. All are
// plain DOM — no framework.
//
// The toolbar elements are declared in index.html and are hidden by default
// via CSS. initializeToolbar() reveals whichever sections the package options
// have activated and attaches click handlers. The reload button waits for
// showToolbarReloadButton(), since only the preview's renderer knows whether
// it needs one.

import { ViewMode } from './view-mode-controller.js';
import { t } from '/assets/celbridge-client/localization.js';
import { getSnippetSet } from './snippets.js';

// Module-scope state for the snippet button's effective disabled flag. The
// button has nowhere to insert into when the editor pane is hidden (Preview
// mode), and the writable-state contract forbids inserting into a read-only
// document. Track both inputs and let `syncSnippetButton` apply the combined
// state, so the two gates can fire in any order without one clobbering the
// other.
let snippetButtonViewMode = ViewMode.Source;
let snippetButtonReadOnly = false;

export function initializeToolbar({
    showViewMode,
    showSnippets,
    snippetSet,
    viewModeController,
    onReloadPreview,
    onInsertSnippet
}) {
    const toolbar = document.getElementById('toolbar');
    if (!toolbar) {
        return;
    }

    const viewModePanel = document.getElementById('view-mode-panel');
    const previewReloadButton = document.getElementById('preview-reload-button');
    const snippetPanel = document.getElementById('snippet-panel');
    const snippetButton = document.getElementById('snippet-button');
    const snippetMenu = document.getElementById('snippet-menu');
    const snippetSeparator = document.getElementById('snippet-separator');

    const hasViewMode = showViewMode && viewModePanel;
    const hasSnippets = showSnippets && snippetPanel && snippetMenu;

    if (!hasViewMode && !hasSnippets) {
        toolbar.hidden = true;
        return;
    }

    toolbar.hidden = false;

    if (hasViewMode) {
        viewModePanel.hidden = false;
        attachViewModeButtons(viewModeController);
    }

    if (onReloadPreview && previewReloadButton) {
        previewReloadButton.addEventListener('click', () => {
            onReloadPreview();
        });
    }

    if (hasSnippets) {
        snippetPanel.hidden = false;
        if (snippetSeparator && hasViewMode) {
            snippetSeparator.hidden = false;
        }
        populateSnippetMenu(snippetMenu, snippetSet, onInsertSnippet);
        attachSnippetButton(snippetButton, snippetMenu);
    }
}

/**
 * Updates the toolbar for the active view mode. The view-mode buttons show
 * which mode is active. The reload button is disabled in Source mode, where
 * the preview is hidden, and the snippet button is disabled in Preview mode,
 * where the editor is hidden.
 */
export function setToolbarViewMode(activeMode) {
    updateViewModeButtons(activeMode);
    syncReloadButton(activeMode);
    snippetButtonViewMode = activeMode;
    syncSnippetButton();
}

/**
 * Shows the reload button, for a preview that changes only when it is
 * reloaded. Its disabled state follows the active view mode from the start.
 */
export function showToolbarReloadButton(activeMode) {
    const panel = document.getElementById('preview-reload-panel');
    if (!panel) {
        return;
    }

    panel.hidden = false;

    const viewModePanel = document.getElementById('view-mode-panel');
    const separator = document.getElementById('preview-reload-separator');
    if (separator && viewModePanel && !viewModePanel.hidden) {
        separator.hidden = false;
    }

    syncReloadButton(activeMode);
}

/**
 * Presses the reload button for a key that means reload. Does nothing while
 * the button is hidden or disabled, so the key reloads only what a click
 * would.
 */
export function pressToolbarReloadButton() {
    const panel = document.getElementById('preview-reload-panel');
    const button = document.getElementById('preview-reload-button');
    if (!panel || panel.hidden || !button || button.disabled) {
        return;
    }
    button.click();
}

/**
 * Marks the reload button while the preview is older than the source, so a
 * reload would change what it shows.
 */
export function setToolbarPreviewStale(isStale) {
    const button = document.getElementById('preview-reload-button');
    if (!button) {
        return;
    }
    button.classList.toggle('is-stale', isStale === true);
}

/**
 * Updates the toolbar's gating on the document's writable state. The snippet
 * inserter mutates the editor buffer. When the document is read-only the
 * button must visibly match the editor's actual behaviour. Pair the menu
 * close with the disable so an open menu doesn't strand an active popup over
 * a now-disabled trigger.
 */
export function setToolbarReadOnly(isReadOnly) {
    snippetButtonReadOnly = isReadOnly === true;
    syncSnippetButton();
    if (snippetButtonReadOnly) {
        const snippetMenu = document.getElementById('snippet-menu');
        if (snippetMenu && !snippetMenu.hidden) {
            closeMenu(snippetMenu);
        }
    }
}

// A reload can only be seen while the preview shows.
function syncReloadButton(activeMode) {
    const button = document.getElementById('preview-reload-button');
    if (!button) {
        return;
    }
    button.disabled = activeMode === ViewMode.Source;
}

function syncSnippetButton() {
    const snippetButton = document.getElementById('snippet-button');
    if (!snippetButton) {
        return;
    }
    snippetButton.disabled = snippetButtonViewMode === ViewMode.Preview
        || snippetButtonReadOnly;
}

function attachViewModeButtons(viewModeController) {
    const buttons = [
        { id: 'view-mode-source', mode: ViewMode.Source },
        { id: 'view-mode-split', mode: ViewMode.Split },
        { id: 'view-mode-preview', mode: ViewMode.Preview }
    ];

    buttons.forEach(({ id, mode }) => {
        const button = document.getElementById(id);
        if (!button) {
            return;
        }

        button.addEventListener('click', () => {
            viewModeController.setMode(mode);
        });
    });

    updateViewModeButtons(viewModeController.getMode());
}

// Shows the active view mode as the pressed button.
function updateViewModeButtons(activeMode) {
    const mapping = {
        [ViewMode.Source]: 'view-mode-source',
        [ViewMode.Split]: 'view-mode-split',
        [ViewMode.Preview]: 'view-mode-preview'
    };

    for (const [mode, id] of Object.entries(mapping)) {
        const button = document.getElementById(id);
        if (!button) {
            continue;
        }
        if (mode === activeMode) {
            button.classList.add('active');
            button.setAttribute('aria-pressed', 'true');
        } else {
            button.classList.remove('active');
            button.setAttribute('aria-pressed', 'false');
        }
    }
}

function populateSnippetMenu(menu, snippetSet, onInsertSnippet) {
    const items = getSnippetSet(snippetSet);
    menu.innerHTML = '';

    items.forEach((item) => {
        if (item.separator) {
            const separator = document.createElement('div');
            separator.className = 'snippet-menu-separator';
            menu.appendChild(separator);
            return;
        }

        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'snippet-menu-item';
        button.textContent = t(item.locKey);
        button.setAttribute('data-loc-key', item.locKey);

        button.addEventListener('click', () => {
            closeMenu(menu);
            onInsertSnippet(item.text);
        });

        menu.appendChild(button);
    });

    // Skip the initial localization pass intentionally. Strings haven't been
    // loaded yet at this point — celbridge.initializeDocument() fetches the
    // package's localization/en.json during editorController.initializeHost(),
    // which runs after the toolbar is built. That later setStrings() call
    // fires applyLocalization(document) and picks up these data-loc-key nodes.
}

function attachSnippetButton(button, menu) {
    if (!button) {
        return;
    }

    button.addEventListener('click', (event) => {
        event.stopPropagation();
        toggleMenu(menu, button);
    });

    document.addEventListener('click', (event) => {
        if (!menu.hidden &&
            event.target !== button &&
            !menu.contains(event.target)) {
            closeMenu(menu);
        }
    });

    document.addEventListener('keydown', (event) => {
        if (event.key === 'Escape' && !menu.hidden) {
            closeMenu(menu);
            button.focus();
        }
    });
}

function toggleMenu(menu, button) {
    if (menu.hidden) {
        openMenu(menu, button);
    } else {
        closeMenu(menu);
    }
}

function openMenu(menu, button) {
    const buttonRect = button.getBoundingClientRect();
    menu.style.top = `${buttonRect.bottom + 2}px`;
    menu.style.left = `${buttonRect.left}px`;
    menu.hidden = false;
    button.setAttribute('aria-expanded', 'true');
}

function closeMenu(menu) {
    menu.hidden = true;
    const button = document.getElementById('snippet-button');
    if (button) {
        button.setAttribute('aria-expanded', 'false');
    }
}

