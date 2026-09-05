// Splitter drag gesture for a horizontal (column) split, matching the native WinUI Splitter. Put a
// `.cel-splitter` element (styled by ui/splitter.css) between two flex panes and call attachSplitter() to
// make it draggable. Uses pointer capture so a drag survives the pointer briefly leaving the thin grab
// area, and toggles a `dragging` class on the element for the accent styling. The class is not required:
// an editor that styles its own divider attaches the gesture to that instead.
//
//   attachSplitter(splitterElement, {
//     onDragStart() { /* capture the pane's current size */ },
//     onDrag(deltaX) { /* deltaX = pixels moved from the drag start. Resize the pane */ },
//     onReset() { /* optional: double-click resets, e.g. to 50/50 */ },
//     isEnabled() { return true; /* optional: skip the gesture while false */ },
//   });

// The gesture is ignored for this long after a reset, so the drag delta trailing the double click that
// raised it cannot resize straight back over the reset.
const resetDebounceMs = 500;

export function attachSplitter(splitterElement, options = {}) {
    if (!splitterElement) {
        return;
    }

    const { onDragStart, onDrag, onReset, isEnabled } = options;
    let dragging = false;
    let startX = 0;
    let lastResetTime = 0;

    function isDebouncingReset() {
        return performance.now() - lastResetTime < resetDebounceMs;
    }

    function onPointerMove(event) {
        if (!dragging) {
            return;
        }
        if (isDebouncingReset()) {
            return;
        }
        if (typeof onDrag === 'function') {
            onDrag(event.clientX - startX);
        }
    }

    function endDrag(event) {
        if (!dragging) {
            return;
        }
        dragging = false;
        splitterElement.classList.remove('dragging');
        window.removeEventListener('pointermove', onPointerMove);
        window.removeEventListener('pointerup', endDrag);
        window.removeEventListener('pointercancel', endDrag);
        try {
            splitterElement.releasePointerCapture(event.pointerId);
        } catch {
            // Pointer capture may already be released.
        }
    }

    splitterElement.addEventListener('pointerdown', (event) => {
        // Only the primary button starts a drag.
        if (event.button !== 0) {
            return;
        }
        if (typeof isEnabled === 'function' && !isEnabled()) {
            return;
        }
        if (isDebouncingReset()) {
            return;
        }
        dragging = true;
        startX = event.clientX;
        if (typeof onDragStart === 'function') {
            onDragStart();
        }
        splitterElement.classList.add('dragging');
        try {
            splitterElement.setPointerCapture(event.pointerId);
        } catch {
            // Some environments lack pointer capture. The window listeners still track the drag.
        }
        window.addEventListener('pointermove', onPointerMove);
        window.addEventListener('pointerup', endDrag);
        window.addEventListener('pointercancel', endDrag);
    });

    if (typeof onReset === 'function') {
        splitterElement.addEventListener('dblclick', () => {
            lastResetTime = performance.now();
            onReset();
        });
    }
}
