import { Editor } from 'https://esm.sh/@tiptap/core@2';
import StarterKit from 'https://esm.sh/@tiptap/starter-kit@2';
import Underline from 'https://esm.sh/@tiptap/extension-underline@2';
import Link from 'https://esm.sh/@tiptap/extension-link@2';
import Placeholder from 'https://esm.sh/@tiptap/extension-placeholder@2';
import Image from 'https://esm.sh/@tiptap/extension-image@2';

const statusEl = document.getElementById('status');
const toolbarEl = document.getElementById('toolbar');
const editorEl = document.getElementById('editor');

let changeTimer = null;
const CHANGE_DEBOUNCE_MS = 300;
let isLoadingContent = false;
let projectBaseUrl = '';
let cancelActivePrompt = null;

function sendMessage(msg) {
    const json = JSON.stringify(msg);
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage(json);
    }
}

// Resolve an image src for display.
// Resource keys (no protocol) are resolved against the project base URL.
function resolveImageSrc(src) {
    if (!src) return src;
    if (/^https?:\/\//i.test(src) || src.startsWith('data:')) return src;
    if (projectBaseUrl) return projectBaseUrl + src;
    return src;
}

// Extract the original src value from a resolved URL.
// Strips the project base URL prefix so the doc stores resource keys.
function unresolveImageSrc(resolvedSrc) {
    if (!resolvedSrc) return resolvedSrc;
    if (projectBaseUrl && resolvedSrc.startsWith(projectBaseUrl)) {
        return resolvedSrc.substring(projectBaseUrl.length);
    }
    return resolvedSrc;
}

const editor = new Editor({
    element: editorEl,
    editorProps: {
        attributes: {
            class: 'tiptap',
        },
    },
    extensions: [
        StarterKit,
        Underline,
        Link.configure({
            openOnClick: false,
            autolink: true,
            HTMLAttributes: {
                rel: 'noopener noreferrer nofollow',
            },
        }),
        Placeholder.configure({
            placeholder: 'Start writing...',
        }),
        Image.extend({
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
                            if (cancelActivePrompt) {
                                cancelActivePrompt();
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
        }),
    ],
    content: '',
    onUpdate: () => {
        if (isLoadingContent) return;

        if (changeTimer) clearTimeout(changeTimer);
        changeTimer = setTimeout(() => {
            sendMessage({ type: 'doc-changed' });
        }, CHANGE_DEBOUNCE_MS);
    },
});

function updateToolbarSeparators() {
    const children = Array.from(toolbarEl.children);

    // First, show all separators
    children.forEach(child => {
        if (child.classList.contains('toolbar-separator')) {
            child.style.display = '';
        }
    });

    // Get only currently visible elements (groups may be hidden via CSS)
    const visibleChildren = children.filter(child => child.offsetParent !== null);

    // Hide separators that are at the start, end, or next to other separators
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
}

function updateToolbar() {
    toolbarEl.querySelectorAll('.toolbar-btn[data-action]').forEach(btn => {
        const action = btn.dataset.action;
        let isActive = false;

        switch (action) {
            case 'bold': isActive = editor.isActive('bold'); break;
            case 'italic': isActive = editor.isActive('italic'); break;
            case 'underline': isActive = editor.isActive('underline'); break;
            case 'strike': isActive = editor.isActive('strike'); break;
            case 'code': isActive = editor.isActive('code'); break;
            case 'heading1': isActive = editor.isActive('heading', { level: 1 }); break;
            case 'heading2': isActive = editor.isActive('heading', { level: 2 }); break;
            case 'heading3': isActive = editor.isActive('heading', { level: 3 }); break;
            case 'paragraph': isActive = editor.isActive('paragraph'); break;
            case 'bulletList': isActive = editor.isActive('bulletList'); break;
            case 'orderedList': isActive = editor.isActive('orderedList'); break;
            case 'blockquote': isActive = editor.isActive('blockquote'); break;
            case 'codeBlock': isActive = editor.isActive('codeBlock'); break;
            case 'link': isActive = editor.isActive('link'); break;
        }

        btn.classList.toggle('active', isActive);
    });

    updateToolbarSeparators();
}

toolbarEl.addEventListener('click', (e) => {
    const btn = e.target.closest('.toolbar-btn[data-action]');
    if (!btn) return;

    const action = btn.dataset.action;

    switch (action) {
        case 'bold': editor.chain().focus().toggleBold().run(); break;
        case 'italic': editor.chain().focus().toggleItalic().run(); break;
        case 'underline': editor.chain().focus().toggleUnderline().run(); break;
        case 'strike': editor.chain().focus().toggleStrike().run(); break;
        case 'code': editor.chain().focus().toggleCode().run(); break;
        case 'heading1': editor.chain().focus().toggleHeading({ level: 1 }).run(); break;
        case 'heading2': editor.chain().focus().toggleHeading({ level: 2 }).run(); break;
        case 'heading3': editor.chain().focus().toggleHeading({ level: 3 }).run(); break;
        case 'paragraph': editor.chain().focus().setParagraph().run(); break;
        case 'bulletList': editor.chain().focus().toggleBulletList().run(); break;
        case 'orderedList': editor.chain().focus().toggleOrderedList().run(); break;
        case 'blockquote': editor.chain().focus().toggleBlockquote().run(); break;
        case 'codeBlock': editor.chain().focus().toggleCodeBlock().run(); break;
        case 'horizontalRule': editor.chain().focus().setHorizontalRule().run(); break;
        case 'link': toggleLink(); break;
        case 'image': insertImage(); break;
        case 'undo': editor.chain().focus().undo().run(); break;
        case 'redo': editor.chain().focus().redo().run(); break;
    }
});

function showPrompt(label, defaultValue = '') {
    return new Promise((resolve) => {
        const bar = document.getElementById('prompt-bar');
        const input = document.getElementById('prompt-bar-input');
        const labelEl = document.getElementById('prompt-bar-label');
        const okBtn = document.getElementById('prompt-bar-ok');
        const cancelBtn = document.getElementById('prompt-bar-cancel');

        labelEl.textContent = label;
        input.value = defaultValue;
        bar.classList.add('visible');
        input.focus();
        input.select();

        function cleanup() {
            bar.classList.remove('visible');
            input.removeEventListener('keydown', onKey);
            okBtn.removeEventListener('click', onOk);
            cancelBtn.removeEventListener('click', onCancel);
            cancelActivePrompt = null;
        }

        function onOk() { cleanup(); resolve(input.value); }
        function onCancel() { cleanup(); resolve(null); }
        function onKey(e) {
            if (e.key === 'Enter') { e.preventDefault(); onOk(); }
            else if (e.key === 'Escape') { e.preventDefault(); onCancel(); }
        }

        cancelActivePrompt = onCancel;
        input.addEventListener('keydown', onKey);
        okBtn.addEventListener('click', onOk);
        cancelBtn.addEventListener('click', onCancel);
    });
}

async function insertImage() {
    const src = await showPrompt('Image URL or resource key:');
    if (src && src.trim()) {
        editor.chain().focus().setImage({ src: resolveImageSrc(src.trim()) }).run();
    }
}

async function toggleLink() {
    const currentHref = editor.isActive('link')
        ? editor.getAttributes('link').href || ''
        : '';

    const url = await showPrompt('URL:', currentHref);
    if (url === null) return;

    if (url.trim() === '') {
        editor.chain().focus().unsetLink().run();
    } else {
        editor.chain().focus().setLink({ href: url.trim() }).run();
    }
}

editor.on('selectionUpdate', updateToolbar);
editor.on('transaction', updateToolbar);

if (window.chrome && window.chrome.webview) {
    window.chrome.webview.addEventListener('message', (event) => {
        let msg;
        try {
            msg = typeof event.data === 'string' ? JSON.parse(event.data) : event.data;
        } catch (e) {
            return;
        }

        switch (msg.type) {
            case 'load-doc': {
                isLoadingContent = true;
                try {
                    if (msg.payload.projectBaseUrl) {
                        projectBaseUrl = msg.payload.projectBaseUrl;
                    }
                    const doc = JSON.parse(msg.payload.content);
                    resolveDocImageSrcs(doc);
                    editor.commands.setContent(doc);
                } catch (e) {
                    console.error('[Note] Failed to load doc:', e);
                }
                isLoadingContent = false;
                break;
            }
            case 'request-save': {
                const doc = editor.getJSON();
                unresolveDocImageSrcs(doc);
                const docJson = JSON.stringify(doc);
                sendMessage({
                    type: 'save-response',
                    payload: { content: docJson }
                });
                break;
            }
        }
    });
}

statusEl.style.display = 'none';
toolbarEl.classList.add('visible');

// Walk a ProseMirror JSON tree and resolve image srcs for display
function resolveDocImageSrcs(node) {
    if (!node) return;
    if (node.type === 'image' && node.attrs && node.attrs.src) {
        node.attrs.src = resolveImageSrc(node.attrs.src);
    }
    if (node.content) {
        node.content.forEach(resolveDocImageSrcs);
    }
}

// Walk a ProseMirror JSON tree and unresolve image srcs for storage
function unresolveDocImageSrcs(node) {
    if (!node) return;
    if (node.type === 'image' && node.attrs && node.attrs.src) {
        node.attrs.src = unresolveImageSrc(node.attrs.src);
    }
    if (node.content) {
        node.content.forEach(unresolveDocImageSrcs);
    }
}

// Floating image toolbar — positioned outside ProseMirror's tree
const imageToolbarEl = document.getElementById('image-toolbar');

function getSelectedImageInfo() {
    const { state } = editor;
    const { selection } = state;
    if (!selection || !selection.node || selection.node.type.name !== 'image') return null;
    const pos = selection.from;
    const node = selection.node;
    const dom = editor.view.nodeDOM(pos);
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
    srcLabelEl.textContent = unresolveImageSrc(info.node.attrs.src) || 'no source';
    srcLabelEl.title = unresolveImageSrc(info.node.attrs.src) || '';

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

editor.on('selectionUpdate', updateImageToolbar);
editor.on('transaction', updateImageToolbar);

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
        const currentSrc = unresolveImageSrc(info.node.attrs.src);
        const newSrc = await showPrompt('Image URL or resource key:', currentSrc || '');
        if (newSrc !== null && newSrc.trim()) {
            editor.chain().setNodeSelection(savedPos)
                .updateAttributes('image', { src: resolveImageSrc(newSrc.trim()) })
                .run();
        } else if (newSrc === null) {
            editor.chain().setNodeSelection(savedPos).run();
        }
        return;
    }

    const btn = e.target.closest('[data-img-action]');
    if (!btn) return;

    const action = btn.dataset.imgAction;
    const savedPos = info.pos;

    switch (action) {
        case 'src': {
            const currentSrc = unresolveImageSrc(info.node.attrs.src);
            const newSrc = await showPrompt('Image URL or resource key:', currentSrc || '');
            if (newSrc !== null && newSrc.trim()) {
                editor.chain().setNodeSelection(savedPos)
                    .updateAttributes('image', { src: resolveImageSrc(newSrc.trim()) })
                    .run();
            } else if (newSrc === null) {
                editor.chain().setNodeSelection(savedPos).run();
            }
            break;
        }
        case 'caption': {
            const currentCaption = info.node.attrs.caption || '';
            const newCaption = await showPrompt('Caption:', currentCaption);
            if (newCaption !== null) {
                editor.chain().setNodeSelection(savedPos)
                    .updateAttributes('image', { caption: newCaption || null })
                    .run();
            } else {
                editor.chain().setNodeSelection(savedPos).run();
            }
            break;
        }
        case 'size-25':
            editor.chain().updateAttributes('image', { width: '25%' }).run();
            break;
        case 'size-50':
            editor.chain().updateAttributes('image', { width: '50%' }).run();
            break;
        case 'size-75':
            editor.chain().updateAttributes('image', { width: '75%' }).run();
            break;
        case 'size-100':
            editor.chain().updateAttributes('image', { width: '100%' }).run();
            break;
        case 'size-custom': {
            const currentWidth = info.node.attrs.width || '100%';
            const currentNum = parseInt(currentWidth.replace('%', ''), 10) || 100;
            const input = await showPrompt('Width (1–100):', String(currentNum));
            if (input !== null && input.trim()) {
                let num = parseInt(input.trim(), 10);
                if (isNaN(num) || num < 1) num = 100;
                if (num > 100) num = 100;
                editor.chain().setNodeSelection(savedPos)
                    .updateAttributes('image', { width: num + '%' })
                    .run();
            } else if (input === null) {
                editor.chain().setNodeSelection(savedPos).run();
            }
            break;
        }
        case 'align-left':
            editor.chain().updateAttributes('image', { textAlign: 'left' }).run();
            break;
        case 'align-center':
            editor.chain().updateAttributes('image', { textAlign: 'center' }).run();
            break;
        case 'align-right':
            editor.chain().updateAttributes('image', { textAlign: 'right' }).run();
            break;
        case 'delete':
            editor.chain().focus().deleteSelection().run();
            break;
    }
});

// Ensure images stay resolved and clear broken styling on successful load
document.getElementById('editor-wrapper').addEventListener('load', (e) => {
    if (e.target.tagName === 'IMG' && e.target.closest('.tiptap')) {
        e.target.classList.remove('note-broken');
    }
}, true);

// Style broken images so they remain visible and clickable
document.getElementById('editor-wrapper').addEventListener('error', (e) => {
    if (e.target.tagName === 'IMG' && e.target.closest('.tiptap')) {
        const rawSrc = e.target.getAttribute('src');
        if (rawSrc && !/^https?:\/\//i.test(rawSrc) && !rawSrc.startsWith('data:')) {
            const resolved = resolveImageSrc(rawSrc);
            if (e.target.src !== resolved) {
                e.target.src = resolved;
                e.target.classList.remove('note-broken');
                return;
            }
        }

        e.target.classList.add('note-broken');
    }
}, true);

// Handle link clicks — send to C# host for routing
document.getElementById('editor-wrapper').addEventListener('click', (e) => {
    const link = e.target.closest('.tiptap a');
    if (!link) return;

    e.preventDefault();
    e.stopPropagation();

    const href = link.getAttribute('href');
    if (href) {
        sendMessage({ type: 'link-clicked', payload: { href } });
    }
});

// Re-evaluate toolbar group visibility when the editor panel is resized
new ResizeObserver(() => {
    updateImageToolbar();
}).observe(document.getElementById('editor-wrapper'));

// Update toolbar separators on window resize
// Use requestAnimationFrame to ensure layout is stable before checking
new ResizeObserver(() => {
    requestAnimationFrame(() => {
        updateToolbarSeparators();
    });
}).observe(toolbarEl);

sendMessage({ type: 'editor-ready' });
