import { describe, it, expect, vi } from 'vitest';
import { ZoomController } from '../js/zoom-controller.js';

function fakeEditorController(fontSize = 14) {
    return {
        getFontSize: () => fontSize,
        setFontSize: vi.fn()
    };
}

function fakePreviewPipeline() {
    return {
        setZoom: vi.fn()
    };
}

describe('ZoomController', () => {
    it('starts at 100%', () => {
        const zoom = new ZoomController({ editorController: fakeEditorController(), previewPipeline: null });

        expect(zoom.getZoom()).toBe(1);
    });

    it('steps through the zoom levels in both directions', () => {
        const zoom = new ZoomController({ editorController: fakeEditorController(), previewPipeline: null });

        zoom.zoomIn();
        expect(zoom.getZoom()).toBe(1.1);
        zoom.zoomIn();
        expect(zoom.getZoom()).toBe(1.25);

        zoom.zoomOut();
        zoom.zoomOut();
        zoom.zoomOut();
        expect(zoom.getZoom()).toBe(0.9);
    });

    it('stops at the smallest and largest levels', () => {
        const zoom = new ZoomController({ editorController: fakeEditorController(), previewPipeline: null });

        for (let i = 0; i < 20; i++) {
            zoom.zoomIn();
        }
        expect(zoom.getZoom()).toBe(3);

        for (let i = 0; i < 20; i++) {
            zoom.zoomOut();
        }
        expect(zoom.getZoom()).toBe(0.5);
    });

    it('scales the editor font from its default size', () => {
        const editorController = fakeEditorController(12);
        const zoom = new ZoomController({ editorController, previewPipeline: null });

        zoom.setZoom(1.5);
        expect(editorController.setFontSize).toHaveBeenLastCalledWith(18);

        zoom.reset();
        expect(editorController.setFontSize).toHaveBeenLastCalledWith(12);
    });

    it('applies the level to the preview', () => {
        const previewPipeline = fakePreviewPipeline();
        const zoom = new ZoomController({ editorController: fakeEditorController(), previewPipeline });

        zoom.setZoom(2);

        expect(previewPipeline.setZoom).toHaveBeenLastCalledWith(2);
    });

    it('clamps a restored level to the supported range', () => {
        const zoom = new ZoomController({ editorController: fakeEditorController(), previewPipeline: null });

        zoom.setZoom(10);
        expect(zoom.getZoom()).toBe(3);

        zoom.setZoom(0.1);
        expect(zoom.getZoom()).toBe(0.5);
    });

    it('ignores a level that is not a number', () => {
        const editorController = fakeEditorController();
        const zoom = new ZoomController({ editorController, previewPipeline: null });

        zoom.setZoom('2');
        zoom.setZoom(Number.NaN);

        expect(zoom.getZoom()).toBe(1);
        expect(editorController.setFontSize).not.toHaveBeenCalled();
    });

    it('notifies listeners of each change', () => {
        const zoom = new ZoomController({ editorController: fakeEditorController(), previewPipeline: null });
        const listener = vi.fn();
        zoom.onChanged(listener);

        zoom.zoomIn();

        expect(listener).toHaveBeenCalledWith(1.1);
    });
});
