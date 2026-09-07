import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { PreviewPipeline } from '../js/preview-pipeline.js';
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
        getValue: vi.fn().mockReturnValue(''),
        scrollToSourceLine: vi.fn(),
        onContentChanged: vi.fn(),
        onScrollChanged: vi.fn()
    };
}

describe('PreviewPipeline divider', () => {
    let divider;
    let splitRoot;
    let pipeline;

    beforeEach(() => {
        splitRoot = document.createElement('div');
        const editorPane = document.createElement('div');
        divider = document.createElement('div');
        const previewPane = document.createElement('div');
        const previewIframe = document.createElement('iframe');
        splitRoot.append(editorPane, divider, previewPane);
        previewPane.appendChild(previewIframe);
        document.body.appendChild(splitRoot);

        // jsdom performs no layout, so the two measurements the drag reads are supplied here.
        Object.defineProperty(splitRoot, 'clientWidth', { value: splitRootWidth, configurable: true });
        editorPane.getBoundingClientRect = () => ({ width: editorPaneWidth });

        divider.setPointerCapture = vi.fn();
        divider.releasePointerCapture = vi.fn();

        pipeline = new PreviewPipeline({
            editorController: createEditorController(),
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
});
