// Link popup module for Note editor
// Three modes: view (existing link), edit (editing existing), create (new link)

let ctx = null;
let linkPopupEl = null;
let linkInputEl = null;
let urlDisplayEl = null;
let editorWrapper = null;
let currentLinkEl = null;
let currentSelectionRange = null;
let originalHref = '';
let currentMode = null; // 'view' | 'edit' | 'create'

export function init(context) {
    ctx = context;
    linkPopupEl = document.getElementById('link-popup');
    linkInputEl = document.getElementById('link-popup-input');
    urlDisplayEl = document.getElementById('link-popup-url-display');
    editorWrapper = document.getElementById('editor-wrapper');

    // Handle clicks in the editor
    editorWrapper.addEventListener('click', (e) => {
        const link = e.target.closest('.tiptap a');

        if (!link) {
            if (!linkPopupEl.contains(e.target)) {
                hidePopup();
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
            showPopupForLink(link);
        }
    });

    // Input keydown (edit and create modes)
    linkInputEl.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') {
            e.preventDefault();
            confirmAndClose();
        } else if (e.key === 'Escape') {
            e.preventDefault();
            cancelAndClose();
        }
    });

    // Prevent mousedown inside popup from blurring input
    linkPopupEl.addEventListener('mousedown', (e) => {
        if (e.target !== linkInputEl) {
            e.preventDefault();
        }
    });

    // Clicking the URL label in view mode opens the link
    urlDisplayEl.addEventListener('click', () => {
        const href = currentLinkEl?.getAttribute('href');
        if (href) {
            ctx.sendMessage({ type: 'link-clicked', payload: { href } });
            hidePopup();
        }
    });

    // View mode buttons
    document.getElementById('link-popup-open').addEventListener('click', () => {
        const href = currentLinkEl?.getAttribute('href');
        if (href) {
            ctx.sendMessage({ type: 'link-clicked', payload: { href } });
            hidePopup();
        }
    });

    document.getElementById('link-popup-edit-btn').addEventListener('click', () => {
        switchToEditMode();
    });

    document.getElementById('link-popup-delete').addEventListener('click', () => {
        removeLink();
    });

    // Edit/create mode buttons
    document.getElementById('link-popup-confirm').addEventListener('click', () => {
        confirmAndClose();
    });

    document.getElementById('link-popup-cancel').addEventListener('click', () => {
        cancelAndClose();
    });

    // Dismiss on scroll or resize
    editorWrapper.addEventListener('scroll', () => {
        if (linkPopupEl.classList.contains('visible')) {
            cancelAndClose();
        }
    });

    new ResizeObserver(() => {
        if (linkPopupEl.classList.contains('visible')) {
            cancelAndClose();
        }
    }).observe(editorWrapper);

    // Dismiss on click outside popup
    document.addEventListener('mousedown', (e) => {
        if (linkPopupEl.classList.contains('visible') && !linkPopupEl.contains(e.target)) {
            cancelAndClose();
        }
    });
}

// --- Mode switching ---

function setMode(mode) {
    currentMode = mode;
    const viewEl = document.getElementById('link-popup-view-mode');
    const editEl = document.getElementById('link-popup-edit-mode');
    viewEl.classList.toggle('active', mode === 'view');
    editEl.classList.toggle('active', mode === 'edit' || mode === 'create');
}

function switchToEditMode() {
    const href = currentLinkEl?.getAttribute('href') || '';
    linkInputEl.value = href;
    originalHref = href;
    setMode('edit');
    requestAnimationFrame(() => {
        linkInputEl.focus();
        linkInputEl.select();
    });
}

// --- Show popup ---

function showPopupForLink(linkEl) {
    currentLinkEl = linkEl;
    currentSelectionRange = null;
    originalHref = linkEl.getAttribute('href') || '';

    urlDisplayEl.textContent = originalHref || '(no URL)';
    urlDisplayEl.title = originalHref;

    setMode('view');
    linkPopupEl.classList.add('visible');

    requestAnimationFrame(() => {
        positionBelowElement(linkEl);
    });
}

export function showPopupForSelection() {
    const { state } = ctx.editor;
    const { selection } = state;
    const { from, to, empty } = selection;

    if (empty) return false;

    if (ctx.editor.isActive('link')) {
        const info = getActiveLinkInfo();
        if (info?.linkEl) {
            showPopupForLink(info.linkEl);
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
    currentLinkEl = null;
    originalHref = '';
    linkInputEl.value = '';

    setMode('create');
    linkPopupEl.classList.add('visible');

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

function confirmAndClose() {
    const href = linkInputEl.value.trim();

    if (currentMode === 'create' && currentSelectionRange) {
        if (href) {
            ctx.editor.chain()
                .focus()
                .setTextSelection(currentSelectionRange)
                .setLink({ href })
                .run();
        }
    } else if ((currentMode === 'edit') && currentLinkEl) {
        if (href === '') {
            ctx.editor.chain().focus().extendMarkRange('link').unsetLink().run();
        } else if (href !== originalHref) {
            ctx.editor.chain().focus().extendMarkRange('link').setLink({ href }).run();
        } else {
            ctx.editor.commands.focus();
        }
    }

    hidePopup();
}

function cancelAndClose() {
    if (currentMode === 'edit') {
        // Revert to view mode rather than dismissing the popup entirely
        urlDisplayEl.textContent = originalHref || '(no URL)';
        urlDisplayEl.title = originalHref;
        setMode('view');
        return;
    }
    ctx.editor.commands.focus();
    hidePopup();
}

function removeLink() {
    if (currentLinkEl) {
        ctx.editor.chain().focus().extendMarkRange('link').unsetLink().run();
    }
    hidePopup();
}

// --- Hide ---

function hidePopup() {
    linkPopupEl.classList.remove('visible');
    currentLinkEl = null;
    currentSelectionRange = null;
    currentMode = null;
    originalHref = '';
}

// --- Positioning ---

function positionBelowElement(el) {
    positionBelowRect(el.getBoundingClientRect());
}

function positionBelowRect(rect) {
    const wrapperRect = editorWrapper.getBoundingClientRect();
    const popupHeight = linkPopupEl.offsetHeight;
    const popupWidth = linkPopupEl.offsetWidth;

    const rectBottomInWrapper = rect.bottom - wrapperRect.top;
    const rectLeftInWrapper = rect.left - wrapperRect.left;

    let top = rectBottomInWrapper + editorWrapper.scrollTop + 8;
    let left = rectLeftInWrapper;

    const maxLeft = editorWrapper.clientWidth - popupWidth - 8;
    if (left > maxLeft) left = maxLeft;
    if (left < 8) left = 8;

    // Flip above if not enough room below
    const visibleBottom = editorWrapper.scrollTop + editorWrapper.clientHeight;
    if (top + popupHeight > visibleBottom) {
        const rectTopInWrapper = rect.top - wrapperRect.top;
        top = rectTopInWrapper + editorWrapper.scrollTop - popupHeight - 8;
    }

    // Hide if anchor has scrolled out of view
    const rectTop = rect.top - wrapperRect.top;
    const rectBottom = rect.bottom - wrapperRect.top;
    if (rectBottom < 0 || rectTop > editorWrapper.clientHeight) {
        hidePopup();
        return;
    }

    linkPopupEl.style.top = top + 'px';
    linkPopupEl.style.left = left + 'px';
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
    if (linkPopupEl.classList.contains('visible')) {
        cancelAndClose();
        return;
    }
    showPopupForSelection();
}

export function triggerEdit() {
    const info = getActiveLinkInfo();
    if (info?.linkEl) {
        showPopupForLink(info.linkEl);
    }
}

