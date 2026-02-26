// Image popover module for Note editor
// All controls shown in a single view with immediate changes.
// Escape reverts to original state; clicking away keeps changes.

import { Image } from './lib/tiptap.js';
import { setupDismiss, positionAtTop, registerPopover, hideAllPopovers } from './popover-utils.js';

let ctx = null;
let imagePopoverEl = null;
let editorWrapper = null;
let toolbarEl = null;
let srcInputEl = null;
let captionInputEl = null;
let customSizeInputEl = null;

let currentPos = null;
let currentWrapperEl = null;
let isNewImage = false;
let originalAttrs = null;
let isPickerOpen = false;

// ---------------------------------------------------------------------------
// Image extension
// ---------------------------------------------------------------------------

export function createImageExtension(context) {
    ctx = context;

    return Image.extend({
        addAttributes() {
            return {
                ...this.parent?.(),
                width: {
                    default: null,
                    parseHTML: el => el.getAttribute('data-width') || null,
                    renderHTML: attrs => {
                        if (!attrs.width) return {};
                        return { 'data-width': attrs.width, style: `width:${attrs.width}` };
                    },
                },
                textAlign: {
                    default: null,
                    parseHTML: el => el.getAttribute('data-align') || null,
                    renderHTML: attrs => {
                        if (!attrs.textAlign || attrs.textAlign === 'center') {
                            return attrs.textAlign ? { 'data-align': 'center' } : {};
                        }
                        if (attrs.textAlign === 'right') {
                            return { 'data-align': 'right', style: 'margin-left:auto;margin-right:0' };
                        }
                        return { 'data-align': 'left', style: 'margin-left:0;margin-right:auto' };
                    },
                },
                caption: {
                    default: null,
                    parseHTML: el => el.getAttribute('data-caption') || null,
                    renderHTML: attrs => {
                        if (!attrs.caption) return {};
                        return { 'data-caption': attrs.caption };
                    },
                },
            };
        },

        addNodeView() {
            return ({ node, getPos, editor: ed }) => {
                const wrapper = document.createElement('div');
                wrapper.className = 'image-node-wrapper';
                wrapper.contentEditable = 'false';

                const figure = document.createElement('figure');
                const img = document.createElement('img');
                const figcaption = document.createElement('figcaption');

                function applyAttrs() {
                    img.src = node.attrs.src || '';
                    if (node.attrs.alt) img.alt = node.attrs.alt;
                    if (node.attrs.title) img.title = node.attrs.title;
                    img.style.cssText = 'width:100%';

                    const figStyle = [];
                    const a = node.attrs.textAlign;
                    if (a === 'left') {
                        figStyle.push('margin-left:0', 'margin-right:auto');
                    } else if (a === 'right') {
                        figStyle.push('margin-left:auto', 'margin-right:0');
                    } else {
                        figStyle.push('margin-left:auto', 'margin-right:auto');
                    }
                    if (node.attrs.width) figStyle.push(`width:${node.attrs.width}`);
                    figure.style.cssText = figStyle.join(';');

                    if (node.attrs.caption) {
                        figcaption.textContent = node.attrs.caption;
                        figcaption.style.display = '';
                    } else {
                        figcaption.textContent = '';
                        figcaption.style.display = 'none';
                    }
                }

                applyAttrs();
                figure.appendChild(img);
                figure.appendChild(figcaption);
                wrapper.appendChild(figure);

                img.addEventListener('click', (e) => {
                    e.stopPropagation();
                    const pos = typeof getPos === 'function' ? getPos() : null;
                    if (pos == null) return;

                    const sel = ed.state.selection;
                    const alreadySelected = sel.node != null && sel.from === pos;

                    if (alreadySelected) {
                        // Always show popover when clicking on already-selected image
                        showPopoverForImage(wrapper, pos, node);
                    } else {
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
                        figure.classList.add('ProseMirror-selectednode');
                        img.classList.add('ProseMirror-selectednode');
                        const pos = typeof getPos === 'function' ? getPos() : null;
                        showPopoverForImage(wrapper, pos, node);
                    },
                    deselectNode() {
                        figure.classList.remove('ProseMirror-selectednode');
                        img.classList.remove('ProseMirror-selectednode');
                        hidePopover();
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
    captionInputEl = document.getElementById('image-popover-caption-input');
    customSizeInputEl = document.getElementById('image-popover-custom-size-input');

    registerPopover(hidePopover);

    imagePopoverEl.addEventListener('mousedown', (e) => {
        if (e.target !== srcInputEl && e.target !== captionInputEl && e.target !== customSizeInputEl) {
            e.preventDefault();
        }
    });

    document.getElementById('image-popover-delete').addEventListener('click', () => deleteImage());

    document.getElementById('image-popover-src-browse').addEventListener('click', () => {
        isPickerOpen = true;
        ctx.sendMessage({ type: 'pick-image-resource' });
    });

    // Handle clicks on size and align buttons
    imagePopoverEl.addEventListener('click', (e) => {
        const btn = e.target.closest('[data-img-action]');
        if (!btn) return;
        const action = btn.dataset.imgAction;

        if (action.startsWith('size-')) {
            const sizeValue = action.replace('size-', '');
            customSizeInputEl.value = sizeValue;
            updateSizeButtonHighlights(sizeValue);
            applyLayoutImmediate();
        } else if (action.startsWith('align-')) {
            imagePopoverEl.querySelectorAll('.img-popover-align-btn').forEach(b => b.classList.remove('active'));
            btn.classList.add('active');
            applyLayoutImmediate();
        }
    });

    customSizeInputEl.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') { e.preventDefault(); applyLayoutImmediate(); customSizeInputEl.blur(); }
        else if (e.key === 'Escape') { e.preventDefault(); cancelEdit(); }
    });
    customSizeInputEl.addEventListener('input', () => {
        updateSizeButtonHighlights(customSizeInputEl.value.trim());
    });
    customSizeInputEl.addEventListener('change', () => applyLayoutImmediate());

    srcInputEl.addEventListener('input', () => {
        const src = srcInputEl.value.trim();
        if (src) {
            applyAttrsToNode({ src: ctx.resolveImageSrc(src) });
        }
    });

    captionInputEl.addEventListener('input', () => {
        applyAttrsToNode({ caption: captionInputEl.value.trim() || null });
    });

    srcInputEl.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') { e.preventDefault(); captionInputEl.focus(); }
        else if (e.key === 'Escape') { e.preventDefault(); cancelEdit(); }
    });

    captionInputEl.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') { e.preventDefault(); captionInputEl.blur(); }
        else if (e.key === 'Escape') { e.preventDefault(); cancelEdit(); }
    });

    // Dismiss on scroll, resize, click outside, or window blur
    setupDismiss(editorWrapper, imagePopoverEl, hidePopover, (e) => {
        return currentWrapperEl && currentWrapperEl.contains(e.target);
    }, () => isPickerOpen);

    editorWrapper.addEventListener('load', (e) => {
        if (e.target.tagName === 'IMG' && e.target.closest('.tiptap')) {
            e.target.classList.remove('note-broken');
        }
    }, true);

    editorWrapper.addEventListener('error', (e) => {
        if (e.target.tagName === 'IMG' && e.target.closest('.tiptap')) {
            const rawSrc = e.target.getAttribute('src');
            if (rawSrc && !/^https?:\/\//i.test(rawSrc) && !rawSrc.startsWith('data:')) {
                const resolved = ctx.resolveImageSrc(rawSrc);
                if (e.target.src !== resolved) {
                    e.target.src = resolved;
                    e.target.classList.remove('note-broken');
                    return;
                }
            }
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
        caption: node.attrs.caption || null,
        width: node.attrs.width || null,
        textAlign: node.attrs.textAlign || null,
    };

    initLayoutButtons(node);
    srcInputEl.value = ctx.unresolveImageSrc(node.attrs.src) || '';
    captionInputEl.value = node.attrs.caption || '';

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
// Layout controls (immediate)
// ---------------------------------------------------------------------------

function initLayoutButtons(node) {
    const w = node.attrs.width || '100%';
    const a = node.attrs.textAlign || 'center';

    // Set the custom size input value (always visible)
    const sizeValue = parseInt(w) || 100;
    customSizeInputEl.value = sizeValue + '';
    updateSizeButtonHighlights(sizeValue + '');

    imagePopoverEl.querySelectorAll('.img-popover-align-btn').forEach(b => {
        const action = b.dataset.imgAction;
        b.classList.toggle('active',
            (action === 'align-left' && a === 'left') ||
            (action === 'align-center' && (!a || a === 'center')) ||
            (action === 'align-right' && a === 'right')
        );
    });
}

function updateSizeButtonHighlights(value) {
    const num = parseInt(value) || 0;
    imagePopoverEl.querySelectorAll('.img-popover-size-btn').forEach(b => {
        const action = b.dataset.imgAction;
        b.classList.toggle('active',
            (action === 'size-25' && num === 25) ||
            (action === 'size-75' && num === 75) ||
            (action === 'size-50' && num === 50) ||
            (action === 'size-100' && num === 100)
        );
    });
}

function applyLayoutImmediate() {
    if (currentPos == null) return;

    // Always read from the custom size input
    const width = parseAndClampCustomSize(customSizeInputEl.value);

    // Update the input to show the clamped/normalized value
    const numericValue = parseInt(width);
    if (customSizeInputEl.value.trim() !== numericValue + '') {
        customSizeInputEl.value = numericValue + '';
    }
    updateSizeButtonHighlights(numericValue + '');

    const activeAlignBtn = imagePopoverEl.querySelector('.img-popover-align-btn.active');
    let textAlign = 'center';
    if (activeAlignBtn) {
        const action = activeAlignBtn.dataset.imgAction;
        if (action === 'align-left') textAlign = 'left';
        else if (action === 'align-center') textAlign = 'center';
        else if (action === 'align-right') textAlign = 'right';
    }

    applyAttrsToNode({ width, textAlign });
}

function parseAndClampCustomSize(value) {
    const trimmed = value.trim();
    const num = parseFloat(trimmed);

    // Invalid input defaults to 100%
    if (isNaN(num)) return '100%';

    // Round decimals to integers and clamp to 1-100 range
    const rounded = Math.round(num);
    const clamped = Math.max(1, Math.min(100, rounded));
    return clamped + '%';
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
    const tr = ctx.editor.state.tr.setNodeMarkup(currentPos, undefined, {
        ...node.attrs,
        ...attrsUpdate,
    });
    ctx.editor.view.dispatch(tr);
}

// ---------------------------------------------------------------------------
// Insert image (toolbar button)
// ---------------------------------------------------------------------------

export function insertImage() {
    ctx.editor.chain().focus().setImage({ src: '' }).run();
}

// ---------------------------------------------------------------------------
// Resource picker result (called from note.js when C# responds)
// ---------------------------------------------------------------------------

export function onPickImageResourceResult(resourceKey) {
    isPickerOpen = false;
    if (!resourceKey) return;
    srcInputEl.value = resourceKey;
    applyAttrsToNode({ src: ctx.resolveImageSrc(resourceKey) });
    srcInputEl.focus();
}
