// Image popover module for Note editor
// View mode: src label + immediate size/align controls
// Edit mode: live src + caption inputs, Cancel reverts to previous values

import Image from 'https://esm.sh/@tiptap/extension-image@2';

let ctx = null;
let imagePopoverEl = null;
let editorWrapper = null;
let srcDisplayEl = null;
let srcInputEl = null;
let captionInputEl = null;
let customSizeInputEl = null;
let customSizeWrapperEl = null;

let currentPos = null;
let currentWrapperEl = null;
let currentMode = null;
let isNewImage = false;
let originalSrc = '';
let originalCaption = '';

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

                img.addEventListener('click', () => {
                    const pos = typeof getPos === 'function' ? getPos() : null;
                    if (pos == null) return;

                    const sel = ed.state.selection;
                    const alreadySelected = sel.node != null && sel.from === pos;

                    if (alreadySelected) {
                        if (imagePopoverEl && !imagePopoverEl.classList.contains('visible')) {
                            showPopoverForImage(wrapper, pos, node);
                        }
                    } else {
                        ed.chain().setNodeSelection(pos).run();
                    }
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
    srcDisplayEl = document.getElementById('image-popover-src-display');
    srcInputEl = document.getElementById('image-popover-src-input');
    captionInputEl = document.getElementById('image-popover-caption-input');
    customSizeInputEl = document.getElementById('image-popover-custom-size-input');
    customSizeWrapperEl = document.getElementById('image-popover-custom-size-wrapper');

    imagePopoverEl.addEventListener('mousedown', (e) => {
        if (e.target !== srcInputEl && e.target !== captionInputEl && e.target !== customSizeInputEl) {
            e.preventDefault();
        }
    });

    document.getElementById('image-popover-edit-btn').addEventListener('click', () => switchToEditMode());
    document.getElementById('image-popover-delete').addEventListener('click', () => deleteImage());

    imagePopoverEl.addEventListener('click', (e) => {
        const btn = e.target.closest('[data-img-action]');
        if (!btn) return;
        const action = btn.dataset.imgAction;

        if (action === 'size-custom') {
            if (!btn.classList.contains('active')) {
                imagePopoverEl.querySelectorAll('.img-popover-size-btn').forEach(b => b.classList.remove('active'));
                btn.classList.add('active');
                customSizeWrapperEl.style.display = '';
                if (!customSizeInputEl.value) {
                    const node = ctx.editor.state.doc.nodeAt(currentPos);
                    if (node) customSizeInputEl.value = parseInt(node.attrs.width || '100') + '';
                }
                customSizeInputEl.focus();
                customSizeInputEl.select();
            }
            return;
        }

        if (action.startsWith('size-')) {
            imagePopoverEl.querySelectorAll('.img-popover-size-btn').forEach(b => b.classList.remove('active'));
            btn.classList.add('active');
            customSizeWrapperEl.style.display = 'none';
            applyLayoutImmediate();
        } else if (action.startsWith('align-')) {
            imagePopoverEl.querySelectorAll('.img-popover-align-btn').forEach(b => b.classList.remove('active'));
            btn.classList.add('active');
            applyLayoutImmediate();
        }
    });

    customSizeInputEl.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') { e.preventDefault(); applyLayoutImmediate(); customSizeInputEl.blur(); }
        else if (e.key === 'Escape') { e.preventDefault(); hidePopover(); }
    });
    customSizeInputEl.addEventListener('change', () => applyLayoutImmediate());

    srcInputEl.addEventListener('input', () => {
        const src = srcInputEl.value.trim();
        if (src) applyAttrsToNode({ src: ctx.resolveImageSrc(src) });
    });

    captionInputEl.addEventListener('input', () => {
        applyAttrsToNode({ caption: captionInputEl.value.trim() || null });
    });

    srcInputEl.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') { e.preventDefault(); captionInputEl.focus(); }
        else if (e.key === 'Escape') { e.preventDefault(); cancelEdit(); }
    });

    captionInputEl.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') { e.preventDefault(); confirmEdit(); }
        else if (e.key === 'Escape') { e.preventDefault(); cancelEdit(); }
    });

    document.getElementById('image-popover-confirm').addEventListener('click', () => confirmEdit());
    document.getElementById('image-popover-cancel').addEventListener('click', () => cancelEdit());

    editorWrapper.addEventListener('scroll', () => {
        if (imagePopoverEl.classList.contains('visible')) hidePopover();
    });

    new ResizeObserver(() => {
        if (imagePopoverEl.classList.contains('visible')) hidePopover();
    }).observe(editorWrapper);

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
    currentPos = pos;
    currentWrapperEl = wrapperEl;
    isNewImage = !node.attrs.src;

    initLayoutButtons(node);
    refreshViewMode();

    if (isNewImage) {
        originalSrc = '';
        originalCaption = '';
        srcInputEl.value = '';
        captionInputEl.value = '';
        setMode('edit');
    } else {
        setMode('view');
    }

    imagePopoverEl.classList.add('visible');
    requestAnimationFrame(() => {
        positionBelowElement(wrapperEl);
        if (isNewImage) srcInputEl.focus();
    });
}

function hidePopover() {
    imagePopoverEl.classList.remove('visible');
    currentPos = null;
    currentWrapperEl = null;
    currentMode = null;
    isNewImage = false;
    originalSrc = '';
    originalCaption = '';
}

// ---------------------------------------------------------------------------
// Mode switching
// ---------------------------------------------------------------------------

function setMode(mode) {
    currentMode = mode;
    document.getElementById('image-popover-view-mode').classList.toggle('active', mode === 'view');
    document.getElementById('image-popover-edit-mode').classList.toggle('active', mode === 'edit');
}

function switchToEditMode() {
    if (currentPos == null) return;
    const node = ctx.editor.state.doc.nodeAt(currentPos);
    if (!node) return;

    originalSrc = ctx.unresolveImageSrc(node.attrs.src) || '';
    originalCaption = node.attrs.caption || '';
    srcInputEl.value = originalSrc;
    captionInputEl.value = originalCaption;

    setMode('edit');
    requestAnimationFrame(() => { srcInputEl.focus(); srcInputEl.select(); });
}

// ---------------------------------------------------------------------------
// Layout controls (immediate)
// ---------------------------------------------------------------------------

function initLayoutButtons(node) {
    const w = node.attrs.width || '100%';
    const a = node.attrs.textAlign || 'center';
    const isPreset = ['25%', '50%', '75%', '100%'].includes(w);

    imagePopoverEl.querySelectorAll('.img-popover-size-btn').forEach(b => {
        const action = b.dataset.imgAction;
        b.classList.toggle('active',
            (action === 'size-25' && w === '25%') ||
            (action === 'size-50' && w === '50%') ||
            (action === 'size-75' && w === '75%') ||
            (action === 'size-100' && (!w || w === '100%')) ||
            (action === 'size-custom' && !isPreset)
        );
    });

    imagePopoverEl.querySelectorAll('.img-popover-align-btn').forEach(b => {
        const action = b.dataset.imgAction;
        b.classList.toggle('active',
            (action === 'align-left' && a === 'left') ||
            (action === 'align-center' && (!a || a === 'center')) ||
            (action === 'align-right' && a === 'right')
        );
    });

    if (!isPreset && w) {
        customSizeInputEl.value = parseInt(w) + '';
        customSizeWrapperEl.style.display = '';
    } else {
        customSizeInputEl.value = '';
        customSizeWrapperEl.style.display = 'none';
    }
}

function applyLayoutImmediate() {
    if (currentPos == null) return;

    const activeSizeBtn = imagePopoverEl.querySelector('.img-popover-size-btn.active');
    let width = '100%';
    if (activeSizeBtn) {
        const action = activeSizeBtn.dataset.imgAction;
        if (action === 'size-25') width = '25%';
        else if (action === 'size-50') width = '50%';
        else if (action === 'size-75') width = '75%';
        else if (action === 'size-100') width = '100%';
        else if (action === 'size-custom') {
            const num = parseInt(customSizeInputEl.value.trim(), 10);
            if (!isNaN(num) && num >= 1) width = Math.min(num, 100) + '%';
        }
    }

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

// ---------------------------------------------------------------------------
// Edit mode actions
// ---------------------------------------------------------------------------

function confirmEdit() {
    const src = srcInputEl.value.trim();
    if (!src) { cancelEdit(); return; }
    refreshViewMode();
    setMode('view');
}

function cancelEdit() {
    if (isNewImage) { deleteImage(); return; }
    applyAttrsToNode({
        src: originalSrc ? ctx.resolveImageSrc(originalSrc) : '',
        caption: originalCaption || null,
    });
    refreshViewMode();
    setMode('view');
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

function refreshViewMode() {
    if (currentPos == null) return;
    const node = ctx.editor.state.doc.nodeAt(currentPos);
    if (!node) return;
    const src = ctx.unresolveImageSrc(node.attrs.src) || '';
    srcDisplayEl.textContent = src || '(no source)';
    srcDisplayEl.title = src;
    srcDisplayEl.classList.toggle('empty', !src);
}

// ---------------------------------------------------------------------------
// Positioning
// ---------------------------------------------------------------------------

function positionBelowElement(el) {
    const rect = el.getBoundingClientRect();
    const wrapperRect = editorWrapper.getBoundingClientRect();
    const popupWidth = imagePopoverEl.offsetWidth;

    const rectCenterInWrapper = (rect.left + rect.right) / 2 - wrapperRect.left;
    let left = rectCenterInWrapper - popupWidth / 2;
    const maxLeft = editorWrapper.clientWidth - popupWidth - 8;
    if (left > maxLeft) left = maxLeft;
    if (left < 8) left = 8;

    const rectBottomInWrapper = rect.bottom - wrapperRect.top;
    const rectTopInWrapper = rect.top - wrapperRect.top;
    const spaceBelow = editorWrapper.clientHeight - rectBottomInWrapper - 16;
    const spaceAbove = rectTopInWrapper - 16;

    let top, maxHeight;
    if (spaceBelow >= spaceAbove) {
        top = rectBottomInWrapper + editorWrapper.scrollTop + 8;
        maxHeight = Math.max(spaceBelow, 120);
    } else {
        maxHeight = Math.max(spaceAbove, 120);
        top = rectTopInWrapper + editorWrapper.scrollTop - maxHeight - 8;
    }

    imagePopoverEl.style.top = top + 'px';
    imagePopoverEl.style.left = left + 'px';
    imagePopoverEl.style.maxHeight = maxHeight + 'px';
}

// ---------------------------------------------------------------------------
// Insert image (toolbar button)
// ---------------------------------------------------------------------------

export function insertImage() {
    ctx.editor.chain().focus().setImage({ src: '' }).run();
}
