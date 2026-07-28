// Splitter drag gesture for a horizontal (column) split, matching the native WinUI Splitter. Put a
// `.cel-splitter` element (styled by celbridge.css) between two flex panes and call attachSplitter() to make
// it draggable. Uses pointer capture so a drag survives the pointer briefly leaving the thin grab area, and
// toggles a `dragging` class on the element for the accent/thicken styling.
//
//   attachSplitter(splitterElement, {
//     onDragStart() { /* capture the pane's current size */ },
//     onDrag(deltaX) { /* deltaX = pixels moved from the drag start; resize the pane */ },
//     onReset() { /* optional: double-click resets, e.g. to 50/50 */ },
//     isEnabled() { return true; /* optional: skip the gesture while false */ },
//   });

export function attachSplitter(splitterElement, options = {}) {
    if (!splitterElement) {
        return;
    }

    const { onDragStart, onDrag, onReset, isEnabled } = options;
    let dragging = false;
    let startX = 0;

    function onPointerMove(event) {
        if (!dragging) {
            return;
        }
        if (typeof onDrag === 'function') {
            onDrag(event.clientX - startX);
        }
    }

    function onPointerUp(event) {
        if (!dragging) {
            return;
        }
        dragging = false;
        splitterElement.classList.remove('dragging');
        window.removeEventListener('pointermove', onPointerMove);
        window.removeEventListener('pointerup', onPointerUp);
        try {
            splitterElement.releasePointerCapture(event.pointerId);
        } catch {
            // Pointer capture may already be released.
        }
    }

    splitterElement.addEventListener('pointerdown', (event) => {
        if (typeof isEnabled === 'function' && !isEnabled()) {
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
            // Some environments lack pointer capture; the window listeners still track the drag.
        }
        window.addEventListener('pointermove', onPointerMove);
        window.addEventListener('pointerup', onPointerUp);
    });

    if (typeof onReset === 'function') {
        splitterElement.addEventListener('dblclick', () => onReset());
    }
}
