import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { PreviewPipeline } from '../js/preview-pipeline.js';
import { PreviewController } from '../js/preview-controller.js';
import { ViewMode } from '../js/view-mode-controller.js';

const splitRootWidth = 1000;
const editorPaneWidth = 500;

function firePointerEvent(target, type, init = {}) {
    const event = new Event(type, { bubbles: true });
    Object.assign(event, { button: 0, clientX: 0, pointerId: 1, ...init });
    target.dispatchEvent(event);
}

function createEditorController() {
    return {
        layout: vi.fn(),
        setHidden: vi.fn(),
        refreshEditAvailability: vi.fn(),
        getValue: vi.fn().mockReturnValue(''),
        scrollToSourceLine: vi.fn(),
        onContentChanged: vi.fn(),
        onScrollChanged: vi.fn(),
        hasUnsavedEdits: vi.fn().mockReturnValue(false),
        getVersionId: vi.fn().mockReturnValue(1),
        getSavedVersionId: vi.fn().mockReturnValue(1)
    };
}

describe('PreviewPipeline divider', () => {
    let divider;
    let splitRoot;
    let pipeline;
    let editorController;
    let iframe;

    beforeEach(() => {
        splitRoot = document.createElement('div');
        const editorPane = document.createElement('div');
        divider = document.createElement('div');
        const previewPane = document.createElement('div');
        const previewIframe = document.createElement('iframe');
        iframe = previewIframe;
        splitRoot.append(editorPane, divider, previewPane);
        previewPane.appendChild(previewIframe);
        document.body.appendChild(splitRoot);

        // jsdom performs no layout, so the two measurements the drag reads are supplied here.
        Object.defineProperty(splitRoot, 'clientWidth', { value: splitRootWidth, configurable: true });
        editorPane.getBoundingClientRect = () => ({ width: editorPaneWidth });

        divider.setPointerCapture = vi.fn();
        divider.releasePointerCapture = vi.fn();

        editorController = createEditorController();
        pipeline = new PreviewPipeline({
            editorController,
            panes: {
                splitRoot,
                editorPane,
                previewPane,
                dividerElement: divider,
                previewIframe
            }
        });
    });

    afterEach(() => {
        document.body.innerHTML = '';
    });

    it('converts the drag delta into the editor pane share', () => {
        pipeline.viewModeController.setMode(ViewMode.Split);

        firePointerEvent(divider, 'pointerdown', { clientX: 500 });
        firePointerEvent(window, 'pointermove', { clientX: 600 });
        expect(pipeline.viewModeController.getFlexShare()).toBeCloseTo(0.6, 10);

        firePointerEvent(window, 'pointermove', { clientX: 400 });
        expect(pipeline.viewModeController.getFlexShare()).toBeCloseTo(0.4, 10);
    });

    it('ignores the drag outside Split mode', () => {
        firePointerEvent(divider, 'pointerdown', { clientX: 500 });
        firePointerEvent(window, 'pointermove', { clientX: 600 });
        expect(pipeline.viewModeController.getFlexShare()).toBeCloseTo(0.5, 10);
    });

    it('ignores the drag when the split root has no width', () => {
        Object.defineProperty(splitRoot, 'clientWidth', { value: 0, configurable: true });
        pipeline.viewModeController.setMode(ViewMode.Split);

        firePointerEvent(divider, 'pointerdown', { clientX: 500 });
        firePointerEvent(window, 'pointermove', { clientX: 600 });
        expect(pipeline.viewModeController.getFlexShare()).toBeCloseTo(0.5, 10);
    });

    it('resets to an even split on a double click', () => {
        pipeline.viewModeController.setMode(ViewMode.Split);

        firePointerEvent(divider, 'pointerdown', { clientX: 500 });
        firePointerEvent(window, 'pointermove', { clientX: 600 });
        firePointerEvent(window, 'pointerup', { pointerId: 1 });

        divider.dispatchEvent(new Event('dblclick'));
        expect(pipeline.viewModeController.getFlexShare()).toBeCloseTo(0.5, 10);
    });
    it('reports edit availability when focus moves into the preview', () => {
        iframe.dispatchEvent(new Event('load'));

        iframe.contentDocument.dispatchEvent(new Event('focusin'));

        expect(editorController.refreshEditAvailability).toHaveBeenCalled();
    });

});

function createPanes() {
    const splitRoot = document.createElement('div');
    const editorPane = document.createElement('div');
    const dividerElement = document.createElement('div');
    const previewPane = document.createElement('div');
    const previewIframe = document.createElement('iframe');
    splitRoot.append(editorPane, dividerElement, previewPane);
    previewPane.appendChild(previewIframe);
    document.body.appendChild(splitRoot);

    return {
        splitRoot,
        editorPane,
        previewPane,
        dividerElement,
        previewIframe
    };
}

// Serves a fake preview module with the given exports, each proxying to globalThis.__fakePreviewModule.
function makeFakeRendererUrl(exportNames) {
    const code = exportNames
        .map((name) =>
            `export function ${name}(...args) { return globalThis.__fakePreviewModule.${name}(...args); }`)
        .join('\n');
    const encoded = Buffer.from(code).toString('base64');
    return `data:text/javascript;base64,${encoded}`;
}

describe('PreviewPipeline preview updates', () => {
    let refreshSpy;

    beforeEach(() => {
        refreshSpy = vi.spyOn(PreviewController.prototype, 'refresh');
    });

    afterEach(() => {
        refreshSpy.mockRestore();
        document.body.innerHTML = '';
    });

    function createPipeline(initialViewMode, editorController = createEditorController()) {
        return new PreviewPipeline({
            editorController,
            initialViewMode,
            panes: createPanes()
        });
    }

    it('loads the preview when the document opens, and not after a save or a change on disk', () => {
        const pipeline = createPipeline(ViewMode.Split);

        pipeline.handleInitialContent('<p>First</p>', 'project:docs/page.html');
        pipeline.handleSaved();
        pipeline.handleExternalReload('<p>Second</p>');

        expect(refreshSpy).toHaveBeenCalledOnce();
        expect(refreshSpy).toHaveBeenCalledWith('/project/docs/page.html');
    });

    it('does not refresh the preview when the view mode changes', () => {
        const pipeline = createPipeline(ViewMode.Source);
        pipeline.handleInitialContent('<p>First</p>', 'project:docs/page.html');
        refreshSpy.mockClear();

        pipeline.viewModeController.setMode(ViewMode.Split);
        pipeline.viewModeController.setMode(ViewMode.Preview);

        expect(refreshSpy).not.toHaveBeenCalled();
    });

    it('moves the preview to the new address on a rename', () => {
        const pipeline = createPipeline(ViewMode.Split);
        pipeline.handleInitialContent('<p>First</p>', 'project:docs/page.html');
        refreshSpy.mockClear();

        pipeline.handleRenamed('project:site/renamed.html');

        expect(refreshSpy).toHaveBeenCalledOnce();
        expect(refreshSpy).toHaveBeenCalledWith('/project/site/renamed.html');
    });

    it('reloads at the address a change on disk reports', () => {
        const pipeline = createPipeline(ViewMode.Split);
        pipeline.handleInitialContent('<p>First</p>', 'project:docs/page.html');
        pipeline.handleExternalReload('<p>Second</p>', 'project:site/moved.html');
        refreshSpy.mockClear();

        pipeline.reload();

        expect(refreshSpy).toHaveBeenCalledWith('/project/site/moved.html');
    });

    it('waits for a pending save before reloading, then reloads once', () => {
        const editorController = createEditorController();
        const pipeline = createPipeline(ViewMode.Preview, editorController);
        pipeline.handleInitialContent('<p>First</p>', 'project:docs/page.html');
        refreshSpy.mockClear();

        editorController.hasUnsavedEdits.mockReturnValue(true);
        pipeline.reload();
        expect(refreshSpy).not.toHaveBeenCalled();

        editorController.hasUnsavedEdits.mockReturnValue(false);
        pipeline.handleSaved();
        pipeline.handleSaved();
        expect(refreshSpy).toHaveBeenCalledOnce();
        expect(refreshSpy).toHaveBeenCalledWith('/project/docs/page.html');
    });

    it('keeps waiting while edits made after a save are still unsaved', () => {
        const editorController = createEditorController();
        const pipeline = createPipeline(ViewMode.Preview, editorController);
        pipeline.handleInitialContent('<p>First</p>', 'project:docs/page.html');
        refreshSpy.mockClear();

        editorController.hasUnsavedEdits.mockReturnValue(true);
        pipeline.reload();
        pipeline.handleSaved();
        expect(refreshSpy).not.toHaveBeenCalled();

        editorController.hasUnsavedEdits.mockReturnValue(false);
        pipeline.handleSaved();
        expect(refreshSpy).toHaveBeenCalledOnce();
    });

    it('reloads at once when a change on disk drops the save a reload was waiting on', () => {
        const editorController = createEditorController();
        const pipeline = createPipeline(ViewMode.Preview, editorController);
        pipeline.handleInitialContent('<p>First</p>', 'project:docs/page.html');
        refreshSpy.mockClear();

        editorController.hasUnsavedEdits.mockReturnValue(true);
        pipeline.reload();
        pipeline.handleExternalReload('<p>Changed on disk</p>');

        expect(refreshSpy).toHaveBeenCalledOnce();
    });

    it('ignores a rename that names no document', () => {
        const pipeline = createPipeline(ViewMode.Split);
        pipeline.handleInitialContent('<p>First</p>', 'project:docs/page.html');
        refreshSpy.mockClear();

        pipeline.handleRenamed(undefined);

        expect(refreshSpy).not.toHaveBeenCalled();
    });

    it('reloads the preview at the document\'s address, wherever the page has gone', () => {
        const pipeline = createPipeline(ViewMode.Preview);
        pipeline.handleInitialContent('<p>First</p>', 'project:docs/page.html');
        refreshSpy.mockClear();

        pipeline.reload();

        expect(refreshSpy).toHaveBeenCalledOnce();
        expect(refreshSpy).toHaveBeenCalledWith('/project/docs/page.html');
    });

    it('reloads the preview at the address a rename gave it', () => {
        const pipeline = createPipeline(ViewMode.Preview);
        pipeline.handleInitialContent('<p>First</p>', 'project:docs/page.html');
        pipeline.handleRenamed('project:site/renamed.html');
        refreshSpy.mockClear();

        pipeline.reload();

        expect(refreshSpy).toHaveBeenCalledWith('/project/site/renamed.html');
    });

    it('ignores a reload before the document has loaded', () => {
        const pipeline = createPipeline(ViewMode.Preview);

        pipeline.reload();

        expect(refreshSpy).not.toHaveBeenCalled();
    });
});

describe('PreviewPipeline reload button', () => {
    afterEach(() => {
        document.body.innerHTML = '';
    });

    it('disables the reload button while Source mode hides the preview', () => {
        const button = document.createElement('button');
        button.id = 'preview-reload-button';
        document.body.appendChild(button);
        const pipeline = new PreviewPipeline({
            editorController: createEditorController(),
            panes: createPanes()
        });

        pipeline.viewModeController.setMode(ViewMode.Preview);
        expect(button.disabled).toBe(false);

        pipeline.viewModeController.setMode(ViewMode.Source);
        expect(button.disabled).toBe(true);

        pipeline.viewModeController.setMode(ViewMode.Split);
        expect(button.disabled).toBe(false);
    });

    describe('for a renderer', () => {
        const rendererExports = [
            'initialize',
            'render',
            'setBasePath',
            'setScrollPercentage',
            'getScrollPercentage'
        ];

        beforeEach(() => {
            globalThis.__fakePreviewModule = {
                initialize: vi.fn().mockResolvedValue(undefined),
                render: vi.fn(),
                setBasePath: vi.fn(),
                setScrollPercentage: vi.fn(),
                getScrollPercentage: vi.fn().mockReturnValue(0),
                refresh: vi.fn()
            };
            document.body.innerHTML = `
                <div id="view-mode-panel"></div>
                <div id="preview-reload-separator" hidden></div>
                <div id="preview-reload-panel" hidden>
                    <button id="preview-reload-button" type="button"></button>
                </div>
            `;
        });

        async function attach(exportNames) {
            const pipeline = new PreviewPipeline({
                editorController: createEditorController(),
                initialViewMode: ViewMode.Preview,
                panes: createPanes()
            });
            await pipeline.attachRenderer(makeFakeRendererUrl(exportNames));
        }

        it('shows the reload button for a renderer of the saved file', async () => {
            await attach([...rendererExports, 'refresh']);

            expect(document.getElementById('preview-reload-panel').hidden).toBe(false);
            expect(document.getElementById('preview-reload-separator').hidden).toBe(false);
        });

        it('never shows it for a renderer of the buffer, which follows every edit', async () => {
            await attach(rendererExports);

            expect(document.getElementById('preview-reload-panel').hidden).toBe(true);
        });
    });
});

describe('PreviewPipeline rename', () => {
    const rendererExports = [
        'initialize',
        'render',
        'setBasePath',
        'setScrollPercentage',
        'getScrollPercentage'
    ];

    beforeEach(() => {
        globalThis.__fakePreviewModule = {
            initialize: vi.fn().mockResolvedValue(undefined),
            render: vi.fn(),
            setBasePath: vi.fn(),
            setScrollPercentage: vi.fn(),
            getScrollPercentage: vi.fn().mockReturnValue(0),
            refresh: vi.fn()
        };
    });

    afterEach(() => {
        document.body.innerHTML = '';
    });

    it('renders the buffer again against the new folder for a renderer of the buffer', async () => {
        const editorController = createEditorController();
        editorController.getValue.mockReturnValue('![logo](logo.png)');
        const pipeline = new PreviewPipeline({
            editorController,
            initialViewMode: ViewMode.Preview,
            panes: createPanes()
        });
        await pipeline.attachRenderer(makeFakeRendererUrl(rendererExports));
        await pipeline.handleInitialContent('![logo](logo.png)', 'project:notes/notes.md');
        globalThis.__fakePreviewModule.render.mockClear();

        pipeline.handleRenamed('project:archive/notes.md');

        expect(globalThis.__fakePreviewModule.setBasePath).toHaveBeenLastCalledWith('archive/');
        expect(globalThis.__fakePreviewModule.render).toHaveBeenCalledWith('![logo](logo.png)');
    });

    it('reloads a preview of the saved file at the new address without copying the buffer', async () => {
        const editorController = createEditorController();
        const pipeline = new PreviewPipeline({
            editorController,
            initialViewMode: ViewMode.Preview,
            panes: createPanes()
        });
        await pipeline.attachRenderer(makeFakeRendererUrl([...rendererExports, 'refresh']));
        await pipeline.handleInitialContent('<p>Page</p>', 'project:site/page.html');

        pipeline.handleRenamed('project:site/renamed.html');

        expect(globalThis.__fakePreviewModule.refresh).toHaveBeenLastCalledWith('/project/site/renamed.html');
        expect(editorController.getValue).not.toHaveBeenCalled();
    });
});

describe('PreviewPipeline changes on disk', () => {
    const rendererExports = [
        'initialize',
        'render',
        'setBasePath',
        'setScrollPercentage',
        'getScrollPercentage'
    ];

    beforeEach(() => {
        globalThis.__fakePreviewModule = {
            initialize: vi.fn().mockResolvedValue(undefined),
            render: vi.fn(),
            setBasePath: vi.fn(),
            setScrollPercentage: vi.fn(),
            getScrollPercentage: vi.fn().mockReturnValue(0),
            refresh: vi.fn()
        };
    });

    afterEach(() => {
        document.body.innerHTML = '';
    });

    async function createLoadedPipeline(exportNames, content, resourceKey) {
        const pipeline = new PreviewPipeline({
            editorController: createEditorController(),
            initialViewMode: ViewMode.Preview,
            panes: createPanes()
        });
        await pipeline.attachRenderer(makeFakeRendererUrl(exportNames));
        await pipeline.handleInitialContent(content, resourceKey);

        return pipeline;
    }

    it('renders the new content in a preview of the buffer', async () => {
        const pipeline = await createLoadedPipeline(rendererExports, '# Notes', 'project:notes/notes.md');

        pipeline.handleExternalReload('# Changed on disk', 'project:notes/notes.md');

        expect(globalThis.__fakePreviewModule.render).toHaveBeenLastCalledWith('# Changed on disk');
    });

    it('leaves a preview of the saved file as it is until the user reloads it', async () => {
        const pipeline = await createLoadedPipeline([...rendererExports, 'refresh'], '<p>Page</p>', 'project:site/page.html');
        globalThis.__fakePreviewModule.refresh.mockClear();

        pipeline.handleExternalReload('<p>Changed on disk</p>', 'project:site/page.html');
        expect(globalThis.__fakePreviewModule.refresh).not.toHaveBeenCalled();

        pipeline.reload();
        expect(globalThis.__fakePreviewModule.refresh).toHaveBeenCalledWith('/project/site/page.html');
    });
});

describe('PreviewPipeline stale', () => {
    const rendererExports = [
        'initialize',
        'render',
        'setBasePath',
        'setScrollPercentage',
        'getScrollPercentage'
    ];

    let button;

    beforeEach(() => {
        globalThis.__fakePreviewModule = {
            initialize: vi.fn().mockResolvedValue(undefined),
            render: vi.fn(),
            setBasePath: vi.fn(),
            setScrollPercentage: vi.fn(),
            getScrollPercentage: vi.fn().mockReturnValue(0),
            refresh: vi.fn()
        };
        button = document.createElement('button');
        button.id = 'preview-reload-button';
        document.body.appendChild(button);
    });

    afterEach(() => {
        document.body.innerHTML = '';
    });

    // An editor whose buffer and saved versions a test moves, firing the content change an edit fires.
    function createVersionedEditor() {
        const editorController = createEditorController();
        let versionId = 1;
        let savedVersionId = 1;
        editorController.getVersionId.mockImplementation(() => versionId);
        editorController.getSavedVersionId.mockImplementation(() => savedVersionId);
        editorController.hasUnsavedEdits.mockImplementation(() => versionId !== savedVersionId);

        return {
            editorController,
            edit(nextVersionId) {
                versionId = nextVersionId;
                editorController.onContentChanged.mock.calls[0][0]();
            },
            save() {
                savedVersionId = versionId;
            }
        };
    }

    async function createLoadedPipeline(editor, exportNames) {
        const pipeline = new PreviewPipeline({
            editorController: editor.editorController,
            initialViewMode: ViewMode.Split,
            panes: createPanes()
        });
        await pipeline.attachRenderer(makeFakeRendererUrl(exportNames));
        await pipeline.handleInitialContent('<p>Page</p>', 'project:site/page.html');

        return pipeline;
    }

    function isMarked() {
        return button.classList.contains('is-stale');
    }

    it('marks the preview after an edit, and clears the mark once a reload shows it', async () => {
        const editor = createVersionedEditor();
        const pipeline = await createLoadedPipeline(editor, [...rendererExports, 'refresh']);
        expect(isMarked()).toBe(false);

        editor.edit(2);
        expect(isMarked()).toBe(true);

        editor.save();
        pipeline.reload();
        expect(isMarked()).toBe(false);
    });

    it('clears the mark when an undo returns to the text the preview shows', async () => {
        const editor = createVersionedEditor();
        await createLoadedPipeline(editor, [...rendererExports, 'refresh']);

        editor.edit(2);
        editor.edit(1);

        expect(isMarked()).toBe(false);
    });

    it('marks the preview when a change on disk replaces the buffer', async () => {
        const editor = createVersionedEditor();
        const pipeline = await createLoadedPipeline(editor, [...rendererExports, 'refresh']);

        editor.edit(2);
        editor.save();
        pipeline.handleExternalReload('<p>Changed on disk</p>', 'project:site/page.html');

        expect(isMarked()).toBe(true);
    });

    it('keeps the mark after a rename reloads the file without an unsaved edit', async () => {
        const editor = createVersionedEditor();
        const pipeline = await createLoadedPipeline(editor, [...rendererExports, 'refresh']);

        editor.edit(2);
        pipeline.handleRenamed('project:site/renamed.html');

        expect(isMarked()).toBe(true);
    });

    it('never marks a preview of the buffer, which follows every edit', async () => {
        const editor = createVersionedEditor();
        await createLoadedPipeline(editor, rendererExports);

        editor.edit(2);

        expect(isMarked()).toBe(false);
    });
});

describe('PreviewPipeline scroll sync', () => {
    const commonExports = [
        'initialize',
        'render',
        'setBasePath',
        'setScrollPercentage',
        'getScrollPercentage'
    ];

    beforeEach(() => {
        globalThis.__fakePreviewModule = {
            initialize: vi.fn().mockResolvedValue(undefined),
            render: vi.fn(),
            setBasePath: vi.fn(),
            setScrollPercentage: vi.fn(),
            getScrollPercentage: vi.fn().mockReturnValue(0),
            scrollToSourceLine: vi.fn(),
            getTopSourceLine: vi.fn().mockReturnValue(null),
            refresh: vi.fn()
        };
    });

    afterEach(() => {
        document.body.innerHTML = '';
    });

    it('follows the editor scroll for a renderer that maps source lines', async () => {
        const editorController = createEditorController();
        const pipeline = new PreviewPipeline({ editorController, panes: createPanes() });

        await pipeline.attachRenderer(makeFakeRendererUrl([...commonExports, 'scrollToSourceLine', 'getTopSourceLine']));

        expect(editorController.onScrollChanged).toHaveBeenCalledOnce();
    });

    it('leaves the editor scroll unwired for a renderer without a source map', async () => {
        const editorController = createEditorController();
        const pipeline = new PreviewPipeline({ editorController, panes: createPanes() });

        await pipeline.attachRenderer(makeFakeRendererUrl([...commonExports, 'refresh']));

        expect(editorController.onScrollChanged).not.toHaveBeenCalled();
    });

    it('hands a renderer of the buffer each edit', async () => {
        const editorController = createEditorController();
        editorController.getValue.mockReturnValue('# Edited');
        const pipeline = new PreviewPipeline({ editorController, panes: createPanes() });
        await pipeline.attachRenderer(makeFakeRendererUrl([...commonExports, 'scrollToSourceLine', 'getTopSourceLine']));

        const onContentChanged = editorController.onContentChanged.mock.calls[0][0];
        onContentChanged();

        expect(globalThis.__fakePreviewModule.render).toHaveBeenCalledWith('# Edited');
    });

    it('does not copy the buffer out for a renderer of the file', async () => {
        const editorController = createEditorController();
        const pipeline = new PreviewPipeline({ editorController, panes: createPanes() });
        await pipeline.attachRenderer(makeFakeRendererUrl([...commonExports, 'refresh']));

        const onContentChanged = editorController.onContentChanged.mock.calls[0][0];
        onContentChanged();

        expect(editorController.getValue).not.toHaveBeenCalled();
        expect(globalThis.__fakePreviewModule.render).not.toHaveBeenCalled();
    });
});

describe('PreviewPipeline initial content', () => {
    const rendererExports = [
        'initialize',
        'render',
        'setBasePath',
        'setScrollPercentage',
        'refresh'
    ];

    afterEach(() => {
        document.body.innerHTML = '';
    });

    it('finishes with the initial content once the renderer has loaded and received it', async () => {
        let finishLoading;
        globalThis.__fakePreviewModule = {
            initialize: vi.fn(() => new Promise((resolve) => { finishLoading = resolve; })),
            render: vi.fn(),
            setBasePath: vi.fn(),
            setScrollPercentage: vi.fn(),
            refresh: vi.fn()
        };
        const pipeline = new PreviewPipeline({
            editorController: createEditorController(),
            initialViewMode: ViewMode.Preview,
            panes: createPanes()
        });
        pipeline.attachRenderer(makeFakeRendererUrl(rendererExports));

        let isHandled = false;
        const handling = pipeline.handleInitialContent('<p>Page</p>', 'project:docs/page.html')
            .then(() => { isHandled = true; });

        await vi.waitFor(() => expect(finishLoading).toBeTypeOf('function'));
        expect(isHandled).toBe(false);

        finishLoading();
        await handling;

        expect(globalThis.__fakePreviewModule.refresh).toHaveBeenCalledWith('/project/docs/page.html');
    });

    it('finishes with the initial content when the renderer fails to load', async () => {
        globalThis.__fakePreviewModule = {
            initialize: vi.fn().mockRejectedValue(new Error('The renderer could not start')),
            render: vi.fn(),
            setBasePath: vi.fn(),
            setScrollPercentage: vi.fn(),
            refresh: vi.fn()
        };
        const pipeline = new PreviewPipeline({
            editorController: createEditorController(),
            initialViewMode: ViewMode.Preview,
            panes: createPanes()
        });
        const attaching = pipeline.attachRenderer(makeFakeRendererUrl(rendererExports));

        await pipeline.handleInitialContent('<p>Page</p>', 'project:docs/page.html');

        await expect(attaching).rejects.toThrow('The renderer could not start');
    });
});
