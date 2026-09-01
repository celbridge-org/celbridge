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
        onDidChangeCursorSelection: vi.fn(),
        onDidFocusEditorText: vi.fn(),
        onDidBlurEditorText: vi.fn(),
        executeEdits: vi.fn(),
        hasTextFocus: vi.fn(() => true),
        getSelection: vi.fn(() => ({ isEmpty: () => true })),
        setSelection: vi.fn(),
        trigger: vi.fn(),
        updateOptions: vi.fn(),
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

    it('applies Monaco readOnly and forwards the writable state to the caller', async () => {
        const onWritableStateChanged = vi.fn();

        await controller.initializeHost({ onWritableStateChanged });
        expect(__capturedHandlers.onViewStateChanged).toBeTypeOf('function');

        __capturedHandlers.onViewStateChanged({ writable: 'Locked' });

        expect(editor.updateOptions).toHaveBeenCalledWith({ readOnly: true });
        expect(onWritableStateChanged).toHaveBeenCalledWith({ state: 'Locked', readOnly: true });

        __capturedHandlers.onViewStateChanged({ writable: 'Writable' });

        expect(editor.updateOptions).toHaveBeenCalledWith({ readOnly: false });
        expect(onWritableStateChanged).toHaveBeenLastCalledWith({ state: 'Writable', readOnly: false });
    });
});

describe('EditorController.performEdit', () => {
    let model;
    let editor;
    let controller;

    beforeEach(() => {
        model = createMockModel();
        editor = createMockEditor(model);
        installMonacoStub(editor);

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

    it('runs the matching Monaco clipboard action for copy', () => {
        controller.performEdit('copy');

        expect(editor.focus).toHaveBeenCalled();
        expect(editor.trigger).toHaveBeenCalledWith('celbridge', 'editor.action.clipboardCopyAction', null);
    });

    it('selects the full model range for selectAll', () => {
        controller.performEdit('selectAll');

        expect(editor.setSelection).toHaveBeenCalledWith(model.getFullModelRange());
        expect(editor.trigger).not.toHaveBeenCalled();
    });

    it('ignores an unknown intent', () => {
        controller.performEdit('frobnicate');

        expect(editor.trigger).not.toHaveBeenCalled();
        expect(editor.setSelection).not.toHaveBeenCalled();
    });
});

describe('EditorController clipboard text', () => {
    const EOL = '\n';

    function createDocumentModel(lines) {
        const model = createMockModel();

        model.getEOL = vi.fn(() => EOL);
        model.getLineCount = vi.fn(() => lines.length);
        model.getLineMaxColumn = vi.fn((lineNumber) => lines[lineNumber - 1].length + 1);
        model.getValueInRange = vi.fn((range) => {
            const parts = [];
            for (let lineNumber = range.startLineNumber; lineNumber <= range.endLineNumber; lineNumber++) {
                const line = lines[lineNumber - 1];
                const start = lineNumber === range.startLineNumber ? range.startColumn - 1 : 0;
                const end = lineNumber === range.endLineNumber ? range.endColumn - 1 : line.length;
                parts.push(line.slice(start, end));
            }

            return parts.join(EOL);
        });

        return model;
    }

    function at(startLineNumber, startColumn, endLineNumber = startLineNumber, endColumn = startColumn) {
        return {
            startLineNumber,
            startColumn,
            endLineNumber,
            endColumn,
            isEmpty: () => startLineNumber === endLineNumber && startColumn === endColumn
        };
    }

    let model;
    let editor;
    let controller;

    beforeEach(() => {
        model = createDocumentModel(['alpha', 'beta', 'gamma']);
        editor = createMockEditor(model);
        installMonacoStub(editor);

        controller = new EditorController();
        controller.create(document.createElement('div'));
    });

    it('takes the cursor line and its terminator when nothing is selected', () => {
        editor.getSelections.mockReturnValue([at(2, 3)]);

        expect(controller.getSelectedText()).toBe('beta\n');
    });

    it('terminates the last line, which carries no terminator of its own', () => {
        editor.getSelections.mockReturnValue([at(3, 1)]);

        expect(controller.getSelectedText()).toBe('gamma\n');
    });

    it('takes a line once however many cursors sit on it, in document order', () => {
        editor.getSelections.mockReturnValue([at(3, 2), at(1, 1), at(3, 4)]);

        expect(controller.getSelectedText()).toBe('alpha\ngamma\n');
    });

    it('takes the selection, not the line, once anything is selected', () => {
        editor.getSelections.mockReturnValue([at(1, 2, 1, 4)]);

        expect(controller.getSelectedText()).toBe('lp');
    });

    it('removes the whole line when the host clears an empty selection', () => {
        editor.getSelections.mockReturnValue([at(2, 3)]);

        controller.insertText('');

        expect(editor.executeEdits).toHaveBeenCalledWith('insert', [{
            range: { startLineNumber: 2, startColumn: 1, endLineNumber: 3, endColumn: 1 },
            text: ''
        }]);
    });

    it('inserts at the cursor rather than replacing its line', () => {
        editor.getSelections.mockReturnValue([at(2, 3)]);

        controller.insertText('x');

        expect(editor.executeEdits).toHaveBeenCalledWith('insert', [{
            range: { startLineNumber: 2, startColumn: 3, endLineNumber: 2, endColumn: 3 },
            text: 'x'
        }]);
    });
});
