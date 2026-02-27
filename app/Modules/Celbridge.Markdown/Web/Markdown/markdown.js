// Markdown editor entry point
// Slim module that creates the TipTap editor and wires up popover modules

import { Editor, StarterKit, Link, Placeholder, Markdown, TaskList, TaskItem, CellSelection, TableMap } from './lib/tiptap.js';
import { setStrings, t } from 'https://shared.celbridge/celbridge-localization.js';

import { createImageExtension, init as initImagePopover, toggleImage, onPickImageResourceResult } from './markdown-image-popover.js';
import { init as initLinkPopover, toggleLink, onPickLinkResourceResult } from './markdown-link-popover.js';
import { createTableExtensions, init as initTablePopover, toggleTable } from './markdown-table-popover.js';

// ---------------------------------------------------------------------------
// Table clipboard handling
// When all cells in a table are selected, copy/cut the entire table
// ---------------------------------------------------------------------------


function findTableFromCellSelection(state) {
    const { selection } = state;
    if (!(selection instanceof CellSelection)) return null;

    // Find the table node containing the selection
    const $anchor = selection.$anchorCell;
    for (let depth = $anchor.depth; depth >= 0; depth--) {
        const node = $anchor.node(depth);
        if (node.type.name === 'table') {
            return {
                node,
                pos: $anchor.before(depth),
                depth
            };
        }
    }
    return null;
}

function areAllCellsSelected(state) {
    const { selection } = state;
    if (!(selection instanceof CellSelection)) return false;

    const tableInfo = findTableFromCellSelection(state);
    if (!tableInfo) return false;

    const { node: tableNode } = tableInfo;

    // Count total cells in the table
    let totalCells = 0;
    tableNode.forEach(row => {
        row.forEach(() => {
            totalCells++;
        });
    });

    // Count selected cells
    let selectedCells = 0;
    selection.forEachCell(() => {
        selectedCells++;
    });

    return selectedCells === totalCells && totalCells > 0;
}

function handleTableClipboard(event, isCut, editor) {
    const { state } = editor;

    if (!areAllCellsSelected(state)) {
        return false; // Let default handling proceed
    }

    const tableInfo = findTableFromCellSelection(state);
    if (!tableInfo) return false;

    const { pos } = tableInfo;

    // Select the entire table node, then let default clipboard handling work
    // This converts cell selection to node selection which copies the whole table
    editor.commands.setNodeSelection(pos);

    // For cut, we need to delete after the copy completes
    if (isCut) {
        // Use setTimeout to let the copy happen first, then delete
        setTimeout(() => {
            editor.commands.deleteSelection();
        }, 0);
    }

    // Return false to let the default copy/cut proceed with the new node selection
    return false;
}

// DOM elements
const toolbarEl = document.getElementById('toolbar');
const editorEl = document.getElementById('editor');
const tocPanel = document.getElementById('toc-panel');
const tocList = document.getElementById('toc-list');
const tocEmpty = document.getElementById('toc-empty');

// State
let changeTimer = null;
const CHANGE_DEBOUNCE_MS = 300;
let isLoadingContent = false;
let projectBaseUrl = '';
let documentBaseUrl = '';

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
    // '/' prefix means project-root-relative path
    if (src.startsWith('/') && projectBaseUrl) {
        return projectBaseUrl + src.substring(1);
    }
    // Otherwise resolve relative to document
    if (documentBaseUrl) return documentBaseUrl + src;
    return src;
}

function unresolveImageSrc(resolvedSrc) {
    if (!resolvedSrc) return resolvedSrc;
    if (documentBaseUrl && resolvedSrc.startsWith(documentBaseUrl)) {
        return resolvedSrc.substring(documentBaseUrl.length);
    }
    return resolvedSrc;
}

// Shared context object for popover modules
const ctx = {
    editor: null,
    sendMessage,
    resolveImageSrc,
    unresolveImageSrc,
};

// Create the Image extension before editor init (needs ctx for toolbar references)
const imageExtension = createImageExtension(ctx);

// Create the Table extensions
const tableExtensions = createTableExtensions();

// Create the TipTap editor
const editor = new Editor({
    element: editorEl,
    editorProps: {
        attributes: {
            class: 'tiptap',
        },
        handleDOMEvents: {
            click: (view, event) => {
                if (event.target.closest('a')) {
                    event.preventDefault();
                }
                return false;
            },
            copy: (view, event) => {
                // ctx.editor is set after editor creation, so use it from closure
                if (ctx.editor) {
                    return handleTableClipboard(event, false, ctx.editor);
                }
                return false;
            },
            cut: (view, event) => {
                if (ctx.editor) {
                    return handleTableClipboard(event, true, ctx.editor);
                }
                return false;
            },
        },
    },
    extensions: [
        StarterKit.configure({
            // Disable Link in StarterKit to avoid duplicate with our custom Link configuration
            link: false,
        }),
        Markdown,
        Link.configure({
            openOnClick: false,
            autolink: true,
            HTMLAttributes: {
                target: null,
                rel: 'noopener noreferrer nofollow',
            },
            // Allow relative paths (which don't have a protocol) in addition to standard URLs
            isAllowedUri: (url, ctx) => {
                // Allow relative paths (no protocol/colon, or starts with ./ or ../)
                if (!url.includes(':') || url.startsWith('./') || url.startsWith('../')) {
                    return true;
                }
                // For URLs with protocols, use the default validation
                return ctx.defaultValidate(url);
            },
        }),
        Placeholder.configure({
            placeholder: 'Start writing...',
        }),
        TaskList,
        TaskItem.configure({
            nested: true,
        }),
        imageExtension,
        ...tableExtensions,
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
initTablePopover(ctx);

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
            case 'strike': isActive = editor.isActive('strike'); break;
            case 'code': isActive = editor.isActive('code'); break;
            case 'heading1': isActive = editor.isActive('heading', { level: 1 }); break;
            case 'heading2': isActive = editor.isActive('heading', { level: 2 }); break;
            case 'heading3': isActive = editor.isActive('heading', { level: 3 }); break;
            case 'paragraph': isActive = editor.isActive('paragraph'); break;
            case 'bulletList': isActive = editor.isActive('bulletList'); break;
            case 'orderedList': isActive = editor.isActive('orderedList'); break;
            case 'taskList': isActive = editor.isActive('taskList'); break;
            case 'blockquote': isActive = editor.isActive('blockquote'); break;
            case 'codeBlock': isActive = editor.isActive('codeBlock'); break;
            case 'link': isActive = editor.isActive('link'); break;
            case 'image': isActive = editor.state.selection.node?.type.name === 'image'; break;
            case 'table': isActive = editor.isActive('table'); break;
            case 'toc': isActive = tocPanel.classList.contains('visible'); break;
        }

        btn.classList.toggle('active', isActive);
    });

    updateToolbarSeparators();
}

// Table of Contents
let tocUpdatePending = false;

function collectHeadings() {
    const headings = [];
    editor.state.doc.descendants((node, pos) => {
        if (node.type.name === 'heading') {
            headings.push({
                level: node.attrs.level,
                text: node.textContent,
                pos: pos,
            });
        }
    });
    return headings;
}

function scheduleTocUpdate() {
    if (!tocPanel.classList.contains('visible') || tocUpdatePending) return;
    tocUpdatePending = true;
    requestAnimationFrame(() => {
        tocUpdatePending = false;
        updateToc();
    });
}

function updateToc() {
    if (!tocPanel.classList.contains('visible')) return;

    const headings = collectHeadings();

    tocList.innerHTML = '';
    tocEmpty.classList.toggle('visible', headings.length === 0);

    headings.forEach((h) => {
        const btn = document.createElement('button');
        btn.className = 'toc-item';
        btn.dataset.level = h.level;
        btn.textContent = h.text || '(empty heading)';
        btn.title = h.text || '(empty heading)';
        btn.addEventListener('click', () => {
            editor.chain().focus().setTextSelection(h.pos + 1).run();
            const domPos = editor.view.domAtPos(h.pos + 1);
            const el = domPos.node.nodeType === 1 ? domPos.node : domPos.node.parentElement;
            if (el) {
                el.scrollIntoView({ behavior: 'smooth', block: 'start' });
            }
        });
        tocList.appendChild(btn);
    });

    highlightActiveTocItem();
}

function highlightActiveTocItem() {
    if (!tocPanel.classList.contains('visible')) return;

    const { from } = editor.state.selection;
    const items = tocList.querySelectorAll('.toc-item');
    let activeIndex = -1;

    const headings = collectHeadings();
    for (let i = headings.length - 1; i >= 0; i--) {
        if (from >= headings[i].pos) {
            activeIndex = i;
            break;
        }
    }

    items.forEach((item, idx) => {
        item.classList.toggle('active', idx === activeIndex);
    });
}

function toggleToc() {
    tocPanel.classList.toggle('visible');
    if (tocPanel.classList.contains('visible')) {
        updateToc();
    }
    updateToolbar();
}

document.getElementById('toc-close').addEventListener('click', () => {
    toggleToc();
});

// Main toolbar click handler
toolbarEl.addEventListener('click', (e) => {
    const btn = e.target.closest('.toolbar-btn[data-action]');
    if (!btn) return;

    const action = btn.dataset.action;

    switch (action) {
        case 'bold': editor.chain().focus().toggleBold().run(); break;
        case 'italic': editor.chain().focus().toggleItalic().run(); break;
        case 'strike': editor.chain().focus().toggleStrike().run(); break;
        case 'code': editor.chain().focus().toggleCode().run(); break;
        case 'heading1': editor.chain().focus().toggleHeading({ level: 1 }).run(); break;
        case 'heading2': editor.chain().focus().toggleHeading({ level: 2 }).run(); break;
        case 'heading3': editor.chain().focus().toggleHeading({ level: 3 }).run(); break;
        case 'paragraph': editor.chain().focus().setParagraph().run(); break;
        case 'bulletList': editor.chain().focus().toggleBulletList().run(); break;
        case 'orderedList': editor.chain().focus().toggleOrderedList().run(); break;
        case 'taskList': editor.chain().focus().toggleTaskList().run(); break;
        case 'blockquote': editor.chain().focus().toggleBlockquote().run(); break;
        case 'codeBlock': editor.chain().focus().toggleCodeBlock().run(); break;
        case 'horizontalRule': editor.chain().focus().setHorizontalRule().run(); break;
        case 'link': toggleLink(); break;
        case 'image': toggleImage(); break;
        case 'table': toggleTable(); break;
        case 'toc': toggleToc(); break;
        case 'undo': editor.chain().focus().undo().run(); break;
        case 'redo': editor.chain().focus().redo().run(); break;
    }
});

// Toolbar state listeners
editor.on('selectionUpdate', () => {
    updateToolbar();
    highlightActiveTocItem();
});
editor.on('transaction', ({ transaction }) => {
    updateToolbar();
    if (transaction.docChanged) {
        scheduleTocUpdate();
    }
});

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
            case 'set-localization': {
                setStrings(msg.payload.strings);
                // Update the TipTap placeholder text dynamically, i.e. "Start writing..."
                const placeholderExt = editor.extensionManager.extensions.find(e => e.name === 'placeholder');
                if (placeholderExt) {
                    placeholderExt.options.placeholder = t('NoteEditor_Placeholder');
                    editor.view.dispatch(editor.state.tr);
                }
                break;
            }
            case 'load-doc': {
                isLoadingContent = true;
                try {
                    if (msg.payload.projectBaseUrl) {
                        projectBaseUrl = msg.payload.projectBaseUrl;
                    }
                    if (msg.payload.documentBaseUrl) {
                        documentBaseUrl = msg.payload.documentBaseUrl;
                    }
                    let content = msg.payload.content || '';
                    content = content.replace(/&nbsp;/g, '');
                    editor.commands.setContent(content, { contentType: 'markdown' });
                } catch (e) {
                    console.error('[Note] Failed to load doc:', e);
                }
                isLoadingContent = false;
                break;
            }
            case 'request-save': {
                let markdown = editor.getMarkdown();
                markdown = markdown.replace(/&nbsp;/g, '');
                sendMessage({
                    type: 'save-response',
                    payload: { content: markdown }
                });
                break;
            }
            case 'pick-image-resource-result': {
                onPickImageResourceResult(msg.payload.resourceKey);
                break;
            }
            case 'pick-link-resource-result': {
                onPickLinkResourceResult(msg.payload.resourceKey);
                break;
            }
        }
    });
}

// Show UI after initialization
toolbarEl.classList.add('visible');

// Main toolbar resize observer
new ResizeObserver(() => {
    requestAnimationFrame(() => {
        updateToolbarSeparators();
    });
}).observe(toolbarEl);

// Signal ready to C# host
sendMessage({ type: 'editor-ready' });
