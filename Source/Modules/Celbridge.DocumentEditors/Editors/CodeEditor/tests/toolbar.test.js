import { describe, it, expect, beforeEach, vi } from 'vitest';
import {
    initializeToolbar,
    setToolbarPreviewStale,
    setToolbarReadOnly,
    setToolbarViewMode,
    showToolbarReloadButton
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
            <div id="preview-reload-separator" class="toolbar-separator" hidden></div>
            <div id="preview-reload-panel" class="toolbar-panel" hidden>
                <button id="preview-reload-button" type="button"></button>
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
        setToolbarViewMode(ViewMode.Source);
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

        setToolbarViewMode(ViewMode.Preview);
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

describe('toolbar view mode', () => {
    beforeEach(() => {
        buildToolbarDom();
        setToolbarReadOnly(false);
    });

    it('presses the button for the active view mode and releases the others', () => {
        setToolbarViewMode(ViewMode.Split);

        expect(document.getElementById('view-mode-split').getAttribute('aria-pressed')).toBe('true');
        expect(document.getElementById('view-mode-source').getAttribute('aria-pressed')).toBe('false');
        expect(document.getElementById('view-mode-preview').getAttribute('aria-pressed')).toBe('false');
    });
});

describe('toolbar preview reload', () => {
    beforeEach(() => {
        buildToolbarDom();
        setToolbarReadOnly(false);
    });

    function initWithPreviewReload(initialMode, onReloadPreview = vi.fn()) {
        initializeToolbar({
            showViewMode: true,
            showSnippets: false,
            viewModeController: fakeViewModeController(initialMode),
            onReloadPreview,
            onInsertSnippet: vi.fn()
        });
        showToolbarReloadButton(initialMode);
    }

    it('shows the reload button after the view mode buttons once the preview needs it', () => {
        initWithPreviewReload(ViewMode.Preview);

        expect(document.getElementById('preview-reload-panel').hidden).toBe(false);
        expect(document.getElementById('preview-reload-separator').hidden).toBe(false);
    });

    it('leaves the reload button hidden until the preview needs it', () => {
        initializeToolbar({
            showViewMode: true,
            showSnippets: false,
            viewModeController: fakeViewModeController(ViewMode.Preview),
            onReloadPreview: vi.fn(),
            onInsertSnippet: vi.fn()
        });

        expect(document.getElementById('preview-reload-panel').hidden).toBe(true);
        expect(document.getElementById('preview-reload-separator').hidden).toBe(true);
    });

    it('reloads the preview when clicked', () => {
        const onReloadPreview = vi.fn();
        initWithPreviewReload(ViewMode.Preview, onReloadPreview);

        document.getElementById('preview-reload-button').click();

        expect(onReloadPreview).toHaveBeenCalledOnce();
    });

    it('starts disabled when the preview is hidden', () => {
        initWithPreviewReload(ViewMode.Source);

        expect(document.getElementById('preview-reload-button').disabled).toBe(true);
    });

    it('is enabled while the preview shows and disabled in Source mode', () => {
        initWithPreviewReload(ViewMode.Preview);
        const button = document.getElementById('preview-reload-button');

        expect(button.disabled).toBe(false);

        setToolbarViewMode(ViewMode.Source);
        expect(button.disabled).toBe(true);

        setToolbarViewMode(ViewMode.Split);
        expect(button.disabled).toBe(false);
    });

    it('is marked while the preview is stale', () => {
        initWithPreviewReload(ViewMode.Preview);
        const button = document.getElementById('preview-reload-button');

        setToolbarPreviewStale(true);
        expect(button.classList.contains('is-stale')).toBe(true);

        setToolbarPreviewStale(false);
        expect(button.classList.contains('is-stale')).toBe(false);
    });

    it('stays enabled for a read-only document', () => {
        initWithPreviewReload(ViewMode.Preview);

        setToolbarReadOnly(true);

        expect(document.getElementById('preview-reload-button').disabled).toBe(false);
    });
});
