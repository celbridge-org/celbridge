// Splitter drag gesture. Matches the WinUI Splitter control: accent
// highlight fades in on hover (CSS), 4px drag line during drag (CSS),
// dblclick resets to 50/50 with a 500ms debounce so a trailing drag delta
// doesn't overwrite the reset.

const doubleClickDebounceMs = 500;

export function attachDividerDrag(dividerElement, viewModeController) {
    if (!dividerElement) {
        return;
    }

    let lastDoubleClickTime = 0;
    let dragStartX = 0;
    let dragStartWidth = 0;
    let isDragging = false;

    function onPointerMove(event) {
        if (!isDragging) {
            return;
        }
        if (performance.now() - lastDoubleClickTime < doubleClickDebounceMs) {
            return;
        }

        const totalWidth = viewModeController.getSplitRootWidth();
        if (totalWidth <= 0) {
            return;
        }

        const delta = event.clientX - dragStartX;
        const newEditorWidth = dragStartWidth + delta;
        const share = newEditorWidth / totalWidth;
        viewModeController.setFlexShare(share);
    }

    function onPointerUp(event) {
        if (!isDragging) {
            return;
        }
        isDragging = false;
        dividerElement.classList.remove('dragging');
        window.removeEventListener('pointermove', onPointerMove);
        window.removeEventListener('pointerup', onPointerUp);
        try {
            dividerElement.releasePointerCapture(event.pointerId);
        } catch {
            // Ignore if pointer capture was already released
        }
    }

    dividerElement.addEventListener('pointerdown', (event) => {
        if (!viewModeController.isSplitMode()) {
            return;
        }
        if (performance.now() - lastDoubleClickTime < doubleClickDebounceMs) {
            return;
        }
        isDragging = true;
        dragStartX = event.clientX;
        dragStartWidth = viewModeController.getEditorPaneWidth();
        dividerElement.classList.add('dragging');
        try {
            dividerElement.setPointerCapture(event.pointerId);
        } catch {
            // Some environments don't support pointer capture; fall back to window listeners
        }
        window.addEventListener('pointermove', onPointerMove);
        window.addEventListener('pointerup', onPointerUp);
    });

    dividerElement.addEventListener('dblclick', () => {
        lastDoubleClickTime = performance.now();
        viewModeController.setFlexShare(0.5);
    });
}
