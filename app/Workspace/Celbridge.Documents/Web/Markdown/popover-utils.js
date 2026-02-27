// Shared utilities for popover modules

// ---------------------------------------------------------------------------
// Popover registry — ensures only one popover is visible at a time
// ---------------------------------------------------------------------------

const registeredPopovers = [];

/**
 * Register a popover's hide function so it can be dismissed when another opens.
 */
export function registerPopover(hideFn) {
    registeredPopovers.push(hideFn);
}

/**
 * Hide every registered popover. Call before showing a new one.
 */
export function hideAllPopovers() {
    registeredPopovers.forEach(fn => fn());
}

/**
 * Set up dismiss-on-scroll, dismiss-on-resize, dismiss-on-click-outside, and
 * dismiss-on-window-blur for a popover element.
 * @param {Function} extraIgnoreFn - Optional function to ignore certain click targets
 * @param {Function} suppressBlurFn - Optional function that returns true when blur should be ignored
 */
export function setupDismiss(editorWrapper, popoverEl, hideFn, extraIgnoreFn, suppressBlurFn) {
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
        if (suppressBlurFn && suppressBlurFn()) return;
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
