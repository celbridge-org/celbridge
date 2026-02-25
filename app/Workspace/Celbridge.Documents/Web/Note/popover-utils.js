// Shared utilities for popover modules

/**
 * Set up dismiss-on-scroll, dismiss-on-resize, dismiss-on-click-outside, and
 * dismiss-on-window-blur for a popover element.
 */
export function setupDismiss(editorWrapper, popoverEl, hideFn, extraIgnoreFn) {
    editorWrapper.addEventListener('scroll', () => {
        if (popoverEl.classList.contains('visible')) hideFn();
    });

    new ResizeObserver(() => {
        if (popoverEl.classList.contains('visible')) hideFn();
    }).observe(editorWrapper);

    document.addEventListener('mousedown', (e) => {
        if (!popoverEl.classList.contains('visible')) return;
        if (popoverEl.contains(e.target)) return;
        if (extraIgnoreFn && extraIgnoreFn(e)) return;
        hideFn();
    });

    window.addEventListener('blur', () => {
        if (popoverEl.classList.contains('visible')) hideFn();
    });
}

/**
 * Position a popover centered horizontally and directly below the toolbar.
 * Used by image and table popovers (position: fixed).
 */
export function positionAtTop(popoverEl, toolbarEl) {
    const toolbarRect = toolbarEl.getBoundingClientRect();
    const popoverWidth = popoverEl.offsetWidth;
    const viewportWidth = window.innerWidth;

    let left = (viewportWidth - popoverWidth) / 2;
    const maxLeft = viewportWidth - popoverWidth - 8;
    if (left > maxLeft) left = maxLeft;
    if (left < 8) left = 8;

    const top = toolbarRect.bottom + 8;

    popoverEl.style.top = top + 'px';
    popoverEl.style.left = left + 'px';
    popoverEl.style.maxHeight = '';
}
