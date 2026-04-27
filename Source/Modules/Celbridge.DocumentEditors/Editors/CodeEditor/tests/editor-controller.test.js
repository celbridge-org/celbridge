import { describe, it, expect, beforeEach, vi } from 'vitest';
import celbridge, { __capturedHandlers } from './fixtures/celbridge-stub.js';

import { EditorController } from '../js/editor-controller.js';

function createMockModel() {
    return {
        getFullModelRange: vi.fn(() => ({
            startLineNumber: 1,
            startColumn: 1,
            endLineNumber: 1,
            endColumn: 1
        })),
        applyEdits: vi.fn(),
        validatePosition: vi.fn((position) => position),
        getLineCount: vi.fn(() => 1),
        setEOL: vi.fn(),
        onDidChangeContent: vi.fn()
    };
}

function createMockEditor(model) {
    return {
        getModel: vi.fn(() => model),
        setValue: vi.fn(),
        getValue: vi.fn(() => ''),
        getScrollTop: vi.fn(() => 0),
        getPosition: vi.fn(() => ({ lineNumber: 1, column: 1 })),
        getSelections: vi.fn(() => []),
        setSelections: vi.fn(),
        setPosition: vi.fn(),
        setScrollTop: vi.fn(),
        onDidScrollChange: vi.fn(),
        focus: vi.fn(),
        layout: vi.fn(),
        dispose: vi.fn()
    };
}

function installMonacoStub(editor) {
    globalThis.monaco = {
        editor: {
            create: vi.fn(() => editor),
            EndOfLineSequence: { CRLF: 1, LF: 0 },
            setModelLanguage: vi.fn(),
            setTheme: vi.fn()
        },
        EditorOption: { lineHeight: 0 }
    };
}

async function flushMicrotasks() {
    await Promise.resolve();
    await Promise.resolve();
}

describe('EditorController.handleExternalChange', () => {
    let model;
    let editor;
    let controller;

    beforeEach(() => {
        for (const key of Object.keys(__capturedHandlers)) {
            delete __capturedHandlers[key];
        }
        model = createMockModel();
        editor = createMockEditor(model);
        installMonacoStub(editor);

        // matchMedia is required by the theme listener wired up in create().
        if (!window.matchMedia) {
            window.matchMedia = () => ({
                matches: false,
                addEventListener: () => {},
                removeEventListener: () => {}
            });
        }

        controller = new EditorController();
        controller.create(document.createElement('div'));
    });

    it('calls setValue on external change to wipe the undo stack', async () => {
        // External reloads must route through editor.setValue (which clears
        // Monaco's undo history) rather than model.applyEdits (which bypasses
        // the undo stack and leaves stale entries pointing at a baseline that
        // no longer exists).
        celbridge.document.load = vi.fn().mockResolvedValue({ content: 'reloaded content' });

        await controller.initializeHost({});
        expect(__capturedHandlers.onExternalChange).toBeTypeOf('function');

        await __capturedHandlers.onExternalChange();
        await flushMicrotasks();

        expect(editor.setValue).toHaveBeenCalledTimes(1);
        expect(editor.setValue).toHaveBeenCalledWith('reloaded content');
        expect(model.applyEdits).not.toHaveBeenCalled();
    });

    it('calls setValue on each external reload', async () => {
        celbridge.document.load = vi.fn()
            .mockResolvedValueOnce({ content: 'first reload' })
            .mockResolvedValueOnce({ content: 'second reload' });

        await controller.initializeHost({});
        expect(__capturedHandlers.onExternalChange).toBeTypeOf('function');

        await __capturedHandlers.onExternalChange();
        await __capturedHandlers.onExternalChange();
        await flushMicrotasks();

        expect(editor.setValue).toHaveBeenCalledTimes(2);
        expect(editor.setValue).toHaveBeenNthCalledWith(1, 'first reload');
        expect(editor.setValue).toHaveBeenNthCalledWith(2, 'second reload');
        expect(model.applyEdits).not.toHaveBeenCalled();
    });
});
