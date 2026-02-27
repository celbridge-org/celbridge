// Link popover module for Markdown editor
// Single-mode popover: URL input with browse, open, delete.
// Changes auto-apply on close; Escape cancels.

import { setupDismiss, registerPopover, hideAllPopovers } from './popover-utils.js';

let ctx = null;
let linkPopoverEl = null;
let linkInputEl = null;
let editorWrapper = null;
let deleteBtnEl = null;
let openBtnEl = null;
let currentLinkEl = null;
let currentSelectionRange = null;
let originalHref = '';
let isExistingLink = false;
let isPickerOpen = false;
let createModeRange = null;

export function init(context) {
    ctx = context;
    linkPopoverEl = document.getElementById('link-popover');
    linkInputEl = document.getElementById('link-popover-input');
    editorWrapper = document.getElementById('editor-wrapper');
    deleteBtnEl = document.getElementById('link-popover-delete');
    openBtnEl = document.getElementById('link-popover-open');

    registerPopover(() => applyAndClose());

    // Handle clicks in the editor
    editorWrapper.addEventListener('click', (e) => {
        const link = e.target.closest('.tiptap a');

        if (!link) {
            if (!linkPopoverEl.contains(e.target)) {
                applyAndClose();
            }
            return;
        }

        e.preventDefault();
        e.stopPropagation();

        if (e.ctrlKey || e.metaKey) {
            const href = link.getAttribute('href');
            if (href) {
                ctx.sendMessage({ type: 'link-clicked', payload: { href } });
            }
        } else {
            showPopoverForLink(link);
        }
    });

    linkInputEl.addEventListener('input', () => {
        const href = linkInputEl.value.trim();
        updateButtonStates(href);
    });

    // Input keydown
    linkInputEl.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') {
            e.preventDefault();
            applyAndClose();
        } else if (e.key === 'Escape') {
            e.preventDefault();
            cancelAndClose();
        }
    });

    // Prevent mousedown inside popover from blurring input
    linkPopoverEl.addEventListener('mousedown', (e) => {
        if (e.target !== linkInputEl) {
            e.preventDefault();
        }
    });

    // Browse button — pick a resource
    document.getElementById('link-popover-browse').addEventListener('click', () => {
        isPickerOpen = true;
        ctx.sendMessage({ type: 'pick-link-resource' });
    });

    // Open link button
    openBtnEl.addEventListener('click', () => {
        const href = linkInputEl.value.trim();
        if (href) {
            ctx.sendMessage({ type: 'link-clicked', payload: { href } });
        }
    });

    // Delete link button
    deleteBtnEl.addEventListener('click', () => {
        removeLink();
    });

    // Dismiss on scroll, resize, click outside, or window blur
    setupDismiss(editorWrapper, linkPopoverEl, applyAndClose, null, () => isPickerOpen);
}

// --- Show popover ---

function showPopoverForLink(linkEl) {
    hideAllPopovers();

    currentLinkEl = linkEl;
    currentSelectionRange = null;
    createModeRange = null;
    isExistingLink = true;

    // Try multiple ways to get the href
    let href = linkEl.getAttribute('href');
    if (!href && linkEl.href) {
        // Some browsers store full URL in .href property
        href = linkEl.href;
    }
    originalHref = href || '';

    linkInputEl.value = originalHref;
    updateButtonStates(originalHref);

    linkPopoverEl.classList.add('visible');

    requestAnimationFrame(() => {
        positionBelowElement(linkEl);
        linkInputEl.focus();
        linkInputEl.select();
    });
}

export function showPopoverForSelection() {
    hideAllPopovers();

    const { state } = ctx.editor;
    const { selection } = state;
    const { from, to, empty } = selection;

    if (empty) return false;

    if (ctx.editor.isActive('link')) {
        const info = getActiveLinkInfo();
        if (info?.linkEl) {
            showPopoverForLink(info.linkEl);
            return true;
        }
    }

    // Trim leading/trailing whitespace from the selection before applying the link
    const selectedText = state.doc.textBetween(from, to);
    const leadingSpaces = selectedText.length - selectedText.trimStart().length;
    const trailingSpaces = selectedText.length - selectedText.trimEnd().length;
    const trimmedFrom = from + leadingSpaces;
    const trimmedTo = to - trailingSpaces;
    if (trimmedFrom >= trimmedTo) return false;

    currentSelectionRange = { from: trimmedFrom, to: trimmedTo };
    createModeRange = { from: trimmedFrom, to: trimmedTo };
    currentLinkEl = null;
    isExistingLink = false;
    originalHref = '';
    linkInputEl.value = '';

    updateButtonStates('');

    linkPopoverEl.classList.add('visible');

    const domSel = window.getSelection();
    if (domSel?.rangeCount > 0) {
        const rect = domSel.getRangeAt(0).getBoundingClientRect();
        requestAnimationFrame(() => {
            positionBelowRect(rect);
            linkInputEl.focus();
        });
    }

    return true;
}

// --- Actions ---

function applyAndClose() {
    if (!linkPopoverEl.classList.contains('visible')) return;

    const href = linkInputEl.value.trim();

    if (createModeRange) {
        if (href) {
            ctx.editor.chain()
                .focus()
                .setTextSelection(createModeRange)
                .setLink({ href })
                .run();
        } else {
            ctx.editor.commands.focus();
        }
    } else if (isExistingLink && currentLinkEl) {
        if (href === '') {
            ctx.editor.chain().focus().extendMarkRange('link').unsetLink().run();
        } else if (href !== originalHref) {
            ctx.editor.chain().focus().extendMarkRange('link').setLink({ href }).run();
        }
    }

    hidePopover();
}

function cancelAndClose() {
    ctx.editor.commands.focus();
    hidePopover();
}

function removeLink() {
    if (!createModeRange && currentLinkEl) {
        ctx.editor.chain().focus().extendMarkRange('link').unsetLink().run();
    }
    hidePopover();
}

// --- Hide ---

function hidePopover() {
    linkPopoverEl.classList.remove('visible');
    currentLinkEl = null;
    currentSelectionRange = null;
    createModeRange = null;
    isExistingLink = false;
    originalHref = '';
}

function updateButtonStates(href) {
    const hasHref = href.length > 0;
    deleteBtnEl.disabled = !hasHref;
    openBtnEl.disabled = !hasHref;
}

// --- Positioning ---

function positionBelowElement(el) {
    positionBelowRect(el.getBoundingClientRect());
}

function positionBelowRect(rect) {
    const wrapperRect = editorWrapper.getBoundingClientRect();
    const popoverHeight = linkPopoverEl.offsetHeight;
    const popoverWidth = linkPopoverEl.offsetWidth;

    const rectBottomInWrapper = rect.bottom - wrapperRect.top;
    const rectLeftInWrapper = rect.left - wrapperRect.left;

    let top = rectBottomInWrapper + editorWrapper.scrollTop + 8;
    let left = rectLeftInWrapper;

    const maxLeft = editorWrapper.clientWidth - popoverWidth - 8;
    if (left > maxLeft) left = maxLeft;
    if (left < 8) left = 8;

    // Flip above if not enough room below
    const visibleBottom = editorWrapper.scrollTop + editorWrapper.clientHeight;
    if (top + popoverHeight > visibleBottom) {
        const rectTopInWrapper = rect.top - wrapperRect.top;
        top = rectTopInWrapper + editorWrapper.scrollTop - popoverHeight - 8;
    }

    // Hide if anchor has scrolled out of view
    const rectTop = rect.top - wrapperRect.top;
    const rectBottom = rect.bottom - wrapperRect.top;
    if (rectBottom < 0 || rectTop > editorWrapper.clientHeight) {
        hidePopover();
        return;
    }

    linkPopoverEl.style.top = top + 'px';
    linkPopoverEl.style.left = left + 'px';
}

// --- Helpers ---

function getActiveLinkInfo() {
    const { state } = ctx.editor;
    const { selection } = state;
    const { from, empty } = selection;

    if (!empty) return null;

    const $from = state.doc.resolve(from);
    const linkMark = $from.marks().find(m => m.type.name === 'link');
    if (!linkMark) return null;

    const domAtPos = ctx.editor.view.domAtPos(from);
    let linkEl = null;
    if (domAtPos.node.nodeType === Node.TEXT_NODE) {
        linkEl = domAtPos.node.parentElement?.closest('a');
    } else {
        linkEl = domAtPos.node.closest?.('a') || domAtPos.node.querySelector?.('a');
    }

    return linkEl ? { linkMark, linkEl, href: linkMark.attrs.href || '' } : null;
}

// --- Toolbar entry point ---

export function toggleLink() {
    if (linkPopoverEl.classList.contains('visible')) {
        applyAndClose();
        return;
    }
    showPopoverForSelection();
}

// --- Resource picker result (called from markdown.js when C# responds) ---

export function onPickLinkResourceResult(resourceKey) {
    isPickerOpen = false;
    if (!resourceKey) return;

    linkInputEl.value = resourceKey;
    updateButtonStates(resourceKey);

    const { state } = ctx.editor;

    // Apply the link change directly using ProseMirror transactions
    // (the cursor position may have been lost when the picker dialog took focus)
    if (createModeRange) {
        // Verify the range is still valid
        const docSize = state.doc.content.size;
        if (createModeRange.from < 0 || createModeRange.to > docSize) {
            linkInputEl.focus();
            return;
        }

        try {
            const linkMark = ctx.editor.schema.marks.link;
            if (!linkMark) {
                linkInputEl.focus();
                return;
            }

            const { tr } = ctx.editor.state;
            tr.addMark(
                createModeRange.from,
                createModeRange.to,
                linkMark.create({ href: resourceKey })
            );
            ctx.editor.view.dispatch(tr);

            // Focus the editor and set selection at the end of the link
            ctx.editor.chain().focus().setTextSelection(createModeRange.to).run();

            hidePopover();
        } catch (e) {
            console.warn('Failed to apply link:', e);
            linkInputEl.focus();
        }
    } else if (isExistingLink && currentLinkEl) {
        // Edit mode: use a direct transaction to update the link
        try {
            const linkPos = ctx.editor.view.posAtDOM(currentLinkEl, 0);
            const $pos = ctx.editor.state.doc.resolve(linkPos);
            const linkMark = $pos.marks().find(m => m.type.name === 'link');

            if (!linkMark) {
                linkInputEl.focus();
                return;
            }

            // Find the start and end of the link
            let start = linkPos;
            let end = linkPos;

            // Walk backwards to find link start
            while (start > 0) {
                const $s = ctx.editor.state.doc.resolve(start - 1);
                if (!$s.marks().some(m => m.type.name === 'link' && m.attrs.href === linkMark.attrs.href)) break;
                start--;
            }

            // Walk forwards to find link end
            const docSize = ctx.editor.state.doc.content.size;
            while (end < docSize) {
                const $e = ctx.editor.state.doc.resolve(end);
                if (!$e.marks().some(m => m.type.name === 'link' && m.attrs.href === linkMark.attrs.href)) break;
                end++;
            }

            // Use transaction to update the link
            const { tr } = ctx.editor.state;
            const linkMarkType = ctx.editor.schema.marks.link;
            tr.removeMark(start, end, linkMarkType);
            tr.addMark(start, end, linkMarkType.create({ href: resourceKey }));
            ctx.editor.view.dispatch(tr);

            hidePopover();
        } catch (e) {
            console.warn('Failed to update link:', e);
            linkInputEl.focus();
        }
    } else {
        // Fallback: just focus the input and let user apply manually
        linkInputEl.focus();
    }
}
