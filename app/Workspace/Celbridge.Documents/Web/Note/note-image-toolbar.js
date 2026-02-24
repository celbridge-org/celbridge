// Image toolbar module for Note editor
// Provides the extended Image extension and toolbar event handling

import Image from 'https://esm.sh/@tiptap/extension-image@2';

let ctx = null;
let imageToolbarEl = null;

/**
 * Creates the extended Image extension with custom attributes and node view.
 * Must be called before editor creation.
 * @param {Object} context - Shared context object
 * @returns {Extension} - The configured Image extension
 */
export function createImageExtension(context) {
    ctx = context;
    imageToolbarEl = document.getElementById('image-toolbar');

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

                    // Image fills its figure container
                    img.style.cssText = 'width:100%';
                    if (node.attrs.width) {
                        img.setAttribute('data-width', node.attrs.width);
                    }

                    const figStyle = [];
                    const a = node.attrs.textAlign;
                    if (a === 'left') {
                        figStyle.push('margin-left:0', 'margin-right:auto');
                    } else if (a === 'right') {
                        figStyle.push('margin-left:auto', 'margin-right:0');
                    } else {
                        figStyle.push('margin-left:auto', 'margin-right:auto');
                    }
                    // Figure gets the width percentage
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
                    if (typeof getPos === 'function') {
                        const pos = getPos();
                        if (pos != null) {
                            ed.chain().setNodeSelection(pos).run();
                        }
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
                        wrapper.insertBefore(imageToolbarEl, figure);
                        imageToolbarEl.classList.add('visible');
                        updateImageToolbar();
                        figure.classList.add('ProseMirror-selectednode');
                        img.classList.add('ProseMirror-selectednode');
                    },
                    deselectNode() {
                        const cancelFn = ctx.getCancelActivePrompt();
                        if (cancelFn) {
                            cancelFn();
                        }
                        if (imageToolbarEl.parentNode === wrapper) {
                            document.body.appendChild(imageToolbarEl);
                        }
                        imageToolbarEl.classList.remove('visible');
                        figure.classList.remove('ProseMirror-selectednode');
                        img.classList.remove('ProseMirror-selectednode');
                    },
                };
            };
        },
    }).configure({
        inline: false,
        allowBase64: true,
    });
}

function getSelectedImageInfo() {
    const { state } = ctx.editor;
    const { selection } = state;
    if (!selection || !selection.node || selection.node.type.name !== 'image') return null;
    const pos = selection.from;
    const node = selection.node;
    const dom = ctx.editor.view.nodeDOM(pos);
    return { pos, node, dom };
}

function updateImageToolbar() {
    const info = getSelectedImageInfo();
    if (!info) return;

    const srcLabelNode = imageToolbarEl.querySelector('.img-src-label');
    const hideOrder = ['align', 'sizes', 'actions'];

    // Reset: show everything
    if (srcLabelNode) srcLabelNode.style.display = '';
    imageToolbarEl.querySelectorAll('[data-group]').forEach(g => g.style.display = '');

    // Update src label text before measuring so width is accurate
    let srcLabelEl = imageToolbarEl.querySelector('.img-src-label');
    if (!srcLabelEl) {
        srcLabelEl = document.createElement('span');
        srcLabelEl.className = 'img-src-label';
        imageToolbarEl.insertBefore(srcLabelEl, imageToolbarEl.firstChild);
    }
    srcLabelEl.textContent = ctx.unresolveImageSrc(info.node.attrs.src) || 'no source';
    srcLabelEl.title = ctx.unresolveImageSrc(info.node.attrs.src) || '';

    // Progressive hiding: the toolbar is width:100% so offsetWidth gives available space.
    // Temporarily switch to max-content to measure natural content width for comparison.
    const availableWidth = imageToolbarEl.offsetWidth;
    if (availableWidth > 0) {
        imageToolbarEl.style.width = 'max-content';

        if (imageToolbarEl.offsetWidth > availableWidth && srcLabelNode) {
            srcLabelNode.style.display = 'none';
        }

        for (const groupName of hideOrder) {
            if (imageToolbarEl.offsetWidth <= availableWidth) break;
            imageToolbarEl.querySelectorAll(`[data-group="${groupName}"]`).forEach(g => g.style.display = 'none');
        }

        imageToolbarEl.style.width = '';
    }

    // Hide separators that don't have visible content on both sides
    const allChildren = Array.from(imageToolbarEl.children);
    const visibleChildren = allChildren.filter(child => child.offsetParent !== null);

    visibleChildren.forEach((child, index) => {
        if (!child.classList.contains('toolbar-separator')) return;

        const isFirst = index === 0;
        const isLast = index === visibleChildren.length - 1;
        const prevIsSeparator = index > 0 && visibleChildren[index - 1].classList.contains('toolbar-separator');
        const nextIsSeparator = index < visibleChildren.length - 1 && visibleChildren[index + 1].classList.contains('toolbar-separator');

        if (isFirst || isLast || prevIsSeparator || nextIsSeparator) {
            child.style.display = 'none';
        }
    });

    // Update active states for size buttons
    const w = info.node.attrs.width || '100%';
    const a = info.node.attrs.textAlign || 'center';
    imageToolbarEl.querySelectorAll('[data-img-action]').forEach(btn => {
        const action = btn.dataset.imgAction;
        let isActive = false;
        if (action === 'size-25') isActive = w === '25%';
        else if (action === 'size-50') isActive = w === '50%';
        else if (action === 'size-75') isActive = w === '75%';
        else if (action === 'size-100') isActive = !w || w === '100%';
        else if (action === 'size-custom') isActive = w && w !== '25%' && w !== '50%' && w !== '75%' && w !== '100%';
        else if (action === 'align-left') isActive = a === 'left';
        else if (action === 'align-center') isActive = !a || a === 'center';
        else if (action === 'align-right') isActive = a === 'right';
        btn.classList.toggle('active', isActive);
    });
}

/**
 * Initialize image toolbar event handlers.
 * Call after editor is created.
 * @param {Object} context - Shared context with editor instance
 */
export function init(context) {
    ctx = context;
    imageToolbarEl = document.getElementById('image-toolbar');
    const editorWrapper = document.getElementById('editor-wrapper');

    ctx.editor.on('selectionUpdate', updateImageToolbar);
    ctx.editor.on('transaction', updateImageToolbar);

    imageToolbarEl.addEventListener('mousedown', (e) => {
        // Prevent the toolbar clicks from stealing focus/selection from the editor
        e.preventDefault();
    });

    imageToolbarEl.addEventListener('click', async (e) => {
        const info = getSelectedImageInfo();
        if (!info) return;

        // Clicking the src label triggers src editing
        if (e.target.closest('.img-src-label')) {
            const savedPos = info.pos;
            const currentSrc = ctx.unresolveImageSrc(info.node.attrs.src);
            const newSrc = await ctx.showPrompt('Image URL or resource key:', currentSrc || '');
            if (newSrc !== null && newSrc.trim()) {
                ctx.editor.chain().setNodeSelection(savedPos)
                    .updateAttributes('image', { src: ctx.resolveImageSrc(newSrc.trim()) })
                    .run();
            } else if (newSrc === null) {
                ctx.editor.chain().setNodeSelection(savedPos).run();
            }
            return;
        }

        const btn = e.target.closest('[data-img-action]');
        if (!btn) return;

        const action = btn.dataset.imgAction;
        const savedPos = info.pos;

        switch (action) {
            case 'src': {
                const currentSrc = ctx.unresolveImageSrc(info.node.attrs.src);
                const newSrc = await ctx.showPrompt('Image URL or resource key:', currentSrc || '');
                if (newSrc !== null && newSrc.trim()) {
                    ctx.editor.chain().setNodeSelection(savedPos)
                        .updateAttributes('image', { src: ctx.resolveImageSrc(newSrc.trim()) })
                        .run();
                } else if (newSrc === null) {
                    ctx.editor.chain().setNodeSelection(savedPos).run();
                }
                break;
            }
            case 'caption': {
                const currentCaption = info.node.attrs.caption || '';
                const newCaption = await ctx.showPrompt('Caption:', currentCaption);
                if (newCaption !== null) {
                    ctx.editor.chain().setNodeSelection(savedPos)
                        .updateAttributes('image', { caption: newCaption || null })
                        .run();
                } else {
                    ctx.editor.chain().setNodeSelection(savedPos).run();
                }
                break;
            }
            case 'size-25':
                ctx.editor.chain().updateAttributes('image', { width: '25%' }).run();
                break;
            case 'size-50':
                ctx.editor.chain().updateAttributes('image', { width: '50%' }).run();
                break;
            case 'size-75':
                ctx.editor.chain().updateAttributes('image', { width: '75%' }).run();
                break;
            case 'size-100':
                ctx.editor.chain().updateAttributes('image', { width: '100%' }).run();
                break;
            case 'size-custom': {
                const currentWidth = info.node.attrs.width || '100%';
                const currentNum = parseInt(currentWidth.replace('%', ''), 10) || 100;
                const widthInput = await ctx.showPrompt('Width % (1–100):', String(currentNum));
                if (widthInput !== null && widthInput.trim()) {
                    let num = parseInt(widthInput.trim(), 10);
                    if (isNaN(num) || num < 1) num = 100;
                    if (num > 100) num = 100;
                    const imageNode = ctx.editor.state.doc.nodeAt(savedPos);
                    if (imageNode && imageNode.type.name === 'image') {
                        const tr = ctx.editor.state.tr.setNodeMarkup(savedPos, undefined, {
                            ...imageNode.attrs,
                            width: num + '%'
                        });
                        ctx.editor.view.dispatch(tr);
                    }
                    ctx.editor.chain().setNodeSelection(savedPos).run();
                } else if (widthInput === null) {
                    ctx.editor.chain().setNodeSelection(savedPos).run();
                }
                break;
            }
            case 'align-left':
                ctx.editor.chain().updateAttributes('image', { textAlign: 'left' }).run();
                break;
            case 'align-center':
                ctx.editor.chain().updateAttributes('image', { textAlign: 'center' }).run();
                break;
            case 'align-right':
                ctx.editor.chain().updateAttributes('image', { textAlign: 'right' }).run();
                break;
            case 'delete':
                ctx.editor.chain().focus().deleteSelection().run();
                break;
        }
    });

    // Ensure images stay resolved and clear broken styling on successful load
    editorWrapper.addEventListener('load', (e) => {
        if (e.target.tagName === 'IMG' && e.target.closest('.tiptap')) {
            e.target.classList.remove('note-broken');
        }
    }, true);

    // Style broken images so they remain visible and clickable
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

    // Re-evaluate toolbar group visibility when the editor panel is resized
    new ResizeObserver(() => {
        updateImageToolbar();
    }).observe(editorWrapper);
}

/**
 * Insert a new image by prompting for the source.
 */
export async function insertImage() {
    const src = await ctx.showPrompt('Image URL or resource key:');
    if (src && src.trim()) {
        ctx.editor.chain().focus().setImage({ src: ctx.resolveImageSrc(src.trim()) }).run();
    }
}
