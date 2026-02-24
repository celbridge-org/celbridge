// Note editor entry point
// Slim module that creates the TipTap editor and wires up popover modules

import { Editor } from 'https://esm.sh/@tiptap/core@2';
import StarterKit from 'https://esm.sh/@tiptap/starter-kit@2';
import Underline from 'https://esm.sh/@tiptap/extension-underline@2';
import Link from 'https://esm.sh/@tiptap/extension-link@2';
import Placeholder from 'https://esm.sh/@tiptap/extension-placeholder@2';

import { createImageExtension, init as initImagePopover, insertImage } from './note-image-popover.js';
import { init as initLinkPopover, toggleLink } from './note-link-popover.js';

// DOM elements
const statusEl = document.getElementById('status');
const toolbarEl = document.getElementById('toolbar');
const editorEl = document.getElementById('editor');

// State
let changeTimer = null;
const CHANGE_DEBOUNCE_MS = 300;
let isLoadingContent = false;
let projectBaseUrl = '';
let cancelActivePrompt = null;

// WebView2 messaging
function sendMessage(msg) {
    const json = JSON.stringify(msg);
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage(json);
    }
}

// Resource key resolution for images
function resolveImageSrc(src) {
    if (!src) return src;
    if (/^https?:\/\//i.test(src) || src.startsWith('data:')) return src;
    if (projectBaseUrl) return projectBaseUrl + src;
    return src;
}

function unresolveImageSrc(resolvedSrc) {
    if (!resolvedSrc) return resolvedSrc;
    if (projectBaseUrl && resolvedSrc.startsWith(projectBaseUrl)) {
        return resolvedSrc.substring(projectBaseUrl.length);
    }
    return resolvedSrc;
}

// Shared context object for popover modules
const ctx = {
    editor: null,
    showPrompt,
    sendMessage,
    resolveImageSrc,
    unresolveImageSrc,
};

// Create the Image extension before editor init (needs ctx for toolbar references)
const imageExtension = createImageExtension(ctx);

// Create the TipTap editor
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
        imageExtension,
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

// Store editor in context for modules
ctx.editor = editor;

// Initialize popover modules
initImagePopover(ctx);
initLinkPopover(ctx);

// Main toolbar separator logic
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

// Main toolbar state updates
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

// Main toolbar click handler
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

// Prompt bar implementation
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

// Toolbar state listeners
editor.on('selectionUpdate', updateToolbar);
editor.on('transaction', updateToolbar);

// WebView2 message handling
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

// Show UI after initialization
statusEl.style.display = 'none';
toolbarEl.classList.add('visible');

// Document tree traversal for image src resolution
function resolveDocImageSrcs(node) {
    if (!node) return;
    if (node.type === 'image' && node.attrs && node.attrs.src) {
        node.attrs.src = resolveImageSrc(node.attrs.src);
    }
    if (node.content) {
        node.content.forEach(resolveDocImageSrcs);
    }
}

function unresolveDocImageSrcs(node) {
    if (!node) return;
    if (node.type === 'image' && node.attrs && node.attrs.src) {
        node.attrs.src = unresolveImageSrc(node.attrs.src);
    }
    if (node.content) {
        node.content.forEach(unresolveDocImageSrcs);
    }
}

// Main toolbar resize observer
new ResizeObserver(() => {
    requestAnimationFrame(() => {
        updateToolbarSeparators();
    });
}).observe(toolbarEl);

// Signal ready to C# host
sendMessage({ type: 'editor-ready' });
