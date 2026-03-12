// Note editor entry point
// TipTap-based rich text editor using native JSON storage format

import { Editor, StarterKit, Link, Placeholder, TaskList, TaskItem, CellSelection, TableMap } from './lib/tiptap.js';
import { setStrings, t } from 'https://shared.celbridge/celbridge-client/localization.js';
import celbridge from 'https://shared.celbridge/celbridge-client/celbridge.js';

import { createImageExtension, init as initImagePopover, toggleImage } from './note-image-popover.js';
import { init as initLinkPopover, toggleLink } from './note-link-popover.js';
import { createTableExtensions, init as initTablePopover, toggleTable } from './note-table-popover.js';

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
const editorWrapperEl = document.getElementById('editor-wrapper');
const tocPanel = document.getElementById('toc-panel');
const tocList = document.getElementById('toc-list');
const tocEmpty = document.getElementById('toc-empty');

// State
let changeTimer = null;
const CHANGE_DEBOUNCE_MS = 300;
let projectBaseUrl = '';
let documentBaseUrl = '';

// Get the client instance
const client = celbridge;

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
    client,
    resolveImageSrc,
    unresolveImageSrc,
};

// Create the Image extension before editor init (needs ctx for toolbar references)
const imageExtension = createImageExtension(ctx);

// Create the Table extensions
const tableExtensions = createTableExtensions();

// Create the TipTap editor
// Note: We don't use the Markdown extension since we're using JSON storage
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
        // Note: We do NOT include Markdown extension - using native JSON format
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
        // Debounce change notifications
        if (changeTimer) clearTimeout(changeTimer);
        changeTimer = setTimeout(() => {
            client.document.notifyChanged();
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

// ---------------------------------------------------------------------------
// Client-based initialization and event handling
// ---------------------------------------------------------------------------

// Handle external file changes
client.document.onExternalChange(async () => {
    // Preserve editor state during reload
    const scrollTop = editorWrapperEl.scrollTop;
    const { from, to } = editor.state.selection;

    try {
        const { content } = await client.document.load();
        // Parse JSON content and set it
        const jsonContent = content ? JSON.parse(content) : { type: 'doc', content: [{ type: 'paragraph' }] };
        editor.commands.setContent(jsonContent);

        // Restore selection if possible
        const maxPos = editor.state.doc.content.size;
        const newFrom = Math.min(from, maxPos);
        const newTo = Math.min(to, maxPos);
        editor.commands.setTextSelection({ from: newFrom, to: newTo });

        // Restore scroll position
        editorWrapperEl.scrollTop = scrollTop;
    } catch (e) {
        console.error('[Note] Failed to reload content:', e);
    }
});

// Handle save requests from host
client.document.onRequestSave(async () => {
    // Serialize to JSON (native TipTap format)
    const jsonContent = JSON.stringify(editor.getJSON());

    try {
        await client.document.save(jsonContent);
    } catch (e) {
        console.error('[Note] Failed to save:', e);
    }
});

// Handle theme changes
client.theme.onChanged((theme) => {
    // Theme is handled by CSS prefers-color-scheme via WebView2 settings
    // but we could add custom handling here if needed
});

// Handle localization updates
client.localization.onUpdated((strings) => {
    setStrings(strings);
    // Update the TipTap placeholder text dynamically
    const placeholderExt = editor.extensionManager.extensions.find(e => e.name === 'placeholder');
    if (placeholderExt) {
        placeholderExt.options.placeholder = t('Note_NoteEditor_Placeholder');
        editor.view.dispatch(editor.state.tr);
    }
});

// Initialize the client and load content
async function initializeEditor() {
    try {
        // Enable debug logging during development
        // client.setLogLevel('debug');

        const result = await client.initialize();

        // Set base URLs for resolving relative paths
        projectBaseUrl = 'https://project.celbridge/';
        const resourceKey = result.metadata?.resourceKey || '';
        const lastSlash = resourceKey.lastIndexOf('/');
        documentBaseUrl = lastSlash >= 0
            ? `${projectBaseUrl}${resourceKey.substring(0, lastSlash + 1)}`
            : projectBaseUrl;

        // Set localization strings
        if (result.localization) {
            setStrings(result.localization);
            const placeholderExt = editor.extensionManager.extensions.find(e => e.name === 'placeholder');
            if (placeholderExt) {
                placeholderExt.options.placeholder = t('Note_NoteEditor_Placeholder');
                editor.view.dispatch(editor.state.tr);
            }
        }

        // Parse JSON content and load into editor
        let jsonContent = { type: 'doc', content: [{ type: 'paragraph' }] };
        if (result.content) {
            try {
                jsonContent = JSON.parse(result.content);
            } catch (e) {
                console.warn('[Note] Failed to parse content as JSON, using empty document');
            }
        }
        editor.commands.setContent(jsonContent);

        // Show the editor
        editorWrapperEl.classList.add('visible');
        toolbarEl.classList.add('visible');

    } catch (e) {
        console.error('[Note] Failed to initialize:', e);
    }
}

// Start initialization
initializeEditor();

// Main toolbar resize observer
new ResizeObserver(() => {
    requestAnimationFrame(() => {
        updateToolbarSeparators();
    });
}).observe(toolbarEl);
