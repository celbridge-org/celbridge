import { describe, it, expect, beforeEach, vi } from 'vitest';
import {
    initializeToolbar,
    setToolbarReadOnly,
    syncSnippetButtonForViewMode
} from '../js/toolbar.js';
import { ViewMode } from '../js/view-mode-controller.js';

function buildToolbarDom() {
    document.body.innerHTML = `
        <div id="toolbar" hidden>
            <div id="view-mode-panel" class="toolbar-panel" hidden>
                <button id="view-mode-preview" type="button"></button>
                <button id="view-mode-split" type="button"></button>
                <button id="view-mode-source" type="button" aria-pressed="true"></button>
            </div>
            <div id="snippet-separator" class="toolbar-separator" hidden></div>
            <div id="snippet-panel" class="toolbar-panel" hidden>
                <button id="snippet-button" type="button" aria-haspopup="true" aria-expanded="false"></button>
            </div>
        </div>
        <div id="snippet-menu" class="snippet-menu" role="menu" hidden></div>
    `;
}

function fakeViewModeController(initialMode = ViewMode.Source) {
    let mode = initialMode;
    return {
        getMode: () => mode,
        setMode: (next) => { mode = next; }
    };
}

function initWithSnippets() {
    initializeToolbar({
        showViewMode: true,
        showSnippets: true,
        snippetSet: 'markdown',
        viewModeController: fakeViewModeController(),
        onInsertSnippet: vi.fn()
    });
}

describe('toolbar read-only gating', () => {
    beforeEach(() => {
        // Reset module-level state between tests by re-setting the gates to
        // their defaults. The toolbar module retains state across imports
        // (ES module singletons), so leaning on initialization alone leaks
        // earlier-test state into later ones.
        buildToolbarDom();
        syncSnippetButtonForViewMode(ViewMode.Source);
        setToolbarReadOnly(false);
    });

    it('disables the snippet button when read-only', () => {
        initWithSnippets();
        const button = document.getElementById('snippet-button');

        expect(button.disabled).toBe(false);

        setToolbarReadOnly(true);
        expect(button.disabled).toBe(true);
    });

    it('re-enables the snippet button when read-only clears in Source mode', () => {
        initWithSnippets();
        const button = document.getElementById('snippet-button');

        setToolbarReadOnly(true);
        setToolbarReadOnly(false);

        expect(button.disabled).toBe(false);
    });

    it('keeps the snippet button disabled when read-only clears but Preview mode is active', () => {
        initWithSnippets();
        const button = document.getElementById('snippet-button');

        syncSnippetButtonForViewMode(ViewMode.Preview);
        setToolbarReadOnly(true);
        setToolbarReadOnly(false);

        // Read-only cleared, but Preview mode still hides the editor pane —
        // the snippet inserter has nowhere to insert into.
        expect(button.disabled).toBe(true);
    });

    it('closes an open snippet menu when entering read-only', () => {
        initWithSnippets();
        const menu = document.getElementById('snippet-menu');
        menu.hidden = false;

        setToolbarReadOnly(true);

        expect(menu.hidden).toBe(true);
    });
});
