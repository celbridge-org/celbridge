// Image popover module for Markdown editor
// All controls shown in a single view with immediate changes.
// Escape reverts to original state; clicking away keeps changes.

import { Image } from './lib/tiptap.js';
import { setupDismiss, positionAtTop, registerPopover, hideAllPopovers } from './popover-utils.js';

let ctx = null;
let imagePopoverEl = null;
let editorWrapper = null;
let toolbarEl = null;
let srcInputEl = null;

let currentPos = null;
let currentWrapperEl = null;
let isNewImage = false;
let originalAttrs = null;
let isPickerOpen = false;
let pendingPopoverOnSelect = false;
let isApplyingAttrs = false;

// ---------------------------------------------------------------------------
// Image extension
// ---------------------------------------------------------------------------

export function createImageExtension(context) {
    ctx = context;

    return Image.extend({
        addNodeView() {
            return ({ node, getPos, editor: ed }) => {
                const wrapper = document.createElement('div');
                wrapper.className = 'image-node-wrapper';
                wrapper.contentEditable = 'false';

                const img = document.createElement('img');

                function applyAttrs() {
                    const src = node.attrs.src || '';
                    img.src = ctx.resolveImageSrc(src);
                    if (node.attrs.alt) img.alt = node.attrs.alt;
                    if (node.attrs.title) img.title = node.attrs.title;
                }

                applyAttrs();
                wrapper.appendChild(img);

                img.addEventListener('click', (e) => {
                    e.stopPropagation();
                    const pos = typeof getPos === 'function' ? getPos() : null;
                    if (pos == null) return;

                    const sel = ed.state.selection;
                    const alreadySelected = sel.node != null && sel.from === pos;

                    if (!alreadySelected) {
                        ed.chain().setNodeSelection(pos).run();
                    }
                });

                // Clicking empty space around the image (wrapper but not img) hides popover but keeps selection
                wrapper.addEventListener('click', (e) => {
                    if (e.target === img) return; // handled by img click
                    e.stopPropagation();
                    hidePopover();
                });

                return {
                    dom: wrapper,
                    update(updatedNode) {
                        if (updatedNode.type.name !== 'image') return false;
                        node = updatedNode;
                        applyAttrs();
                        return true;
                    },
                    selectNode() {
                        img.classList.add('ProseMirror-selectednode');
                        if (pendingPopoverOnSelect) {
                            pendingPopoverOnSelect = false;
                            const pos = typeof getPos === 'function' ? getPos() : null;
                            showPopoverForImage(wrapper, pos, node);
                        }
                    },
                    deselectNode() {
                        img.classList.remove('ProseMirror-selectednode');
                        if (!isApplyingAttrs) {
                            hidePopover();
                        }
                    },
                };
            };
        },
    }).configure({ inline: false, allowBase64: true });
}

// ---------------------------------------------------------------------------
// Popover init
// ---------------------------------------------------------------------------

export function init(context) {
    ctx = context;
    imagePopoverEl = document.getElementById('image-popover');
    editorWrapper = document.getElementById('editor-wrapper');
    toolbarEl = document.getElementById('toolbar');
    srcInputEl = document.getElementById('image-popover-src-input');

    registerPopover(hidePopover);

    imagePopoverEl.addEventListener('mousedown', (e) => {
        if (e.target !== srcInputEl) {
            e.preventDefault();
        }
    });

    document.getElementById('image-popover-delete').addEventListener('click', () => deleteImage());

    document.getElementById('image-popover-src-browse').addEventListener('click', () => {
        isPickerOpen = true;
        ctx.sendMessage({ type: 'pick-image-resource' });
    });

    srcInputEl.addEventListener('input', () => {
        const src = srcInputEl.value.trim();
        if (src) {
            applyAttrsToNode({ src });
        }
    });

    srcInputEl.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') { e.preventDefault(); srcInputEl.blur(); }
        else if (e.key === 'Escape') { e.preventDefault(); cancelEdit(); }
    });

    // Dismiss on scroll, resize, click outside, or window blur
    setupDismiss(editorWrapper, imagePopoverEl, hidePopover, (e) => {
        if (currentWrapperEl && currentWrapperEl.contains(e.target)) return true;
        if (e.target.closest('.toolbar-btn[data-action="image"]')) return true;
        return false;
    }, () => isPickerOpen);

    editorWrapper.addEventListener('load', (e) => {
        if (e.target.tagName === 'IMG' && e.target.closest('.tiptap')) {
            e.target.classList.remove('note-broken');
        }
    }, true);

    editorWrapper.addEventListener('error', (e) => {
        if (e.target.tagName === 'IMG' && e.target.closest('.tiptap')) {
            e.target.classList.add('note-broken');
        }
    }, true);
}

// ---------------------------------------------------------------------------
// Show / hide
// ---------------------------------------------------------------------------

function showPopoverForImage(wrapperEl, pos, node) {
    hideAllPopovers();

    currentPos = pos;
    currentWrapperEl = wrapperEl;
    isNewImage = !node.attrs.src;

    originalAttrs = {
        src: node.attrs.src || '',
    };

    srcInputEl.value = node.attrs.src || '';

    imagePopoverEl.classList.add('visible');
    requestAnimationFrame(() => {
        positionAtTop(imagePopoverEl, toolbarEl);
        if (isNewImage) srcInputEl.focus();
    });
}

function hidePopover() {
    imagePopoverEl.classList.remove('visible');
    currentPos = null;
    currentWrapperEl = null;
    isNewImage = false;
    originalAttrs = null;
}

// ---------------------------------------------------------------------------
// Cancel (Escape) — reverts to original attrs
// ---------------------------------------------------------------------------

function cancelEdit() {
    if (isNewImage) { deleteImage(); return; }
    if (originalAttrs) {
        applyAttrsToNode(originalAttrs);
    }
    hidePopover();
}

function deleteImage() {
    if (currentPos == null) return;
    ctx.editor.chain().setNodeSelection(currentPos).deleteSelection().focus().run();
    hidePopover();
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function applyAttrsToNode(attrsUpdate) {
    if (currentPos == null) return;
    const node = ctx.editor.state.doc.nodeAt(currentPos);
    if (!node || node.type.name !== 'image') return;
    isApplyingAttrs = true;
    try {
        const tr = ctx.editor.state.tr.setNodeMarkup(currentPos, undefined, {
            ...node.attrs,
            ...attrsUpdate,
        });
        ctx.editor.view.dispatch(tr);
    } finally {
        isApplyingAttrs = false;
    }
}

// ---------------------------------------------------------------------------
// Toolbar entry point
// ---------------------------------------------------------------------------

export function toggleImage() {
    if (imagePopoverEl.classList.contains('visible')) {
        hidePopover();
        return;
    }

    const { state } = ctx.editor;
    const { selection } = state;

    if (selection.node && selection.node.type.name === 'image') {
        const pos = selection.from;
        const domNode = ctx.editor.view.nodeDOM(pos);
        const wrapperEl = domNode?.closest?.('.image-node-wrapper') || domNode;
        showPopoverForImage(wrapperEl, pos, selection.node);
    } else {
        pendingPopoverOnSelect = true;
        ctx.editor.chain().focus().setImage({ src: '' }).run();
    }
}

// ---------------------------------------------------------------------------
// Resource picker result (called from note.js when C# responds)
// ---------------------------------------------------------------------------

export function onPickImageResourceResult(resourceKey) {
    isPickerOpen = false;
    if (!resourceKey) return;
    srcInputEl.value = resourceKey;
    applyAttrsToNode({ src: resourceKey });
    srcInputEl.focus();
}
