// Table popover module for Note editor
// Create mode: configure rows, columns, and header row before inserting
// View mode: add/remove rows/columns, toggle header, delete table

import Table from 'https://esm.sh/@tiptap/extension-table@2.27.2';
import TableRow from 'https://esm.sh/@tiptap/extension-table-row@2.27.2';
import TableCell from 'https://esm.sh/@tiptap/extension-table-cell@2.27.2';
import TableHeader from 'https://esm.sh/@tiptap/extension-table-header@2.27.2';
import { setupDismiss, positionAtTop } from './popover-utils.js';

let ctx = null;
let tablePopoverEl = null;
let editorWrapper = null;
let toolbarEl = null;
let currentMode = null; // 'create' | 'view'
let rowsInputEl = null;
let colsInputEl = null;
let headerCheckboxEl = null;
let viewInfoEl = null;
let toggleHeaderEl = null;
let createModeEl = null;
let viewModeEl = null;

// ---------------------------------------------------------------------------
// Table extensions
// ---------------------------------------------------------------------------

export function createTableExtensions() {
    return [
        Table.configure({ resizable: false }),
        TableRow,
        TableCell,
        TableHeader,
    ];
}

// ---------------------------------------------------------------------------
// Popover init
// ---------------------------------------------------------------------------

export function init(context) {
    ctx = context;
    tablePopoverEl = document.getElementById('table-popover');
    editorWrapper = document.getElementById('editor-wrapper');
    toolbarEl = document.getElementById('toolbar');
    rowsInputEl = document.getElementById('table-create-rows');
    colsInputEl = document.getElementById('table-create-cols');
    headerCheckboxEl = document.getElementById('table-create-header');
    viewInfoEl = document.getElementById('table-view-info');
    toggleHeaderEl = document.getElementById('table-toggle-header');
    createModeEl = document.getElementById('table-popover-create-mode');
    viewModeEl = document.getElementById('table-popover-view-mode');

    // Prevent mousedown inside popover from stealing focus
    tablePopoverEl.addEventListener('mousedown', (e) => {
        const inputs = tablePopoverEl.querySelectorAll('input');
        let isInput = false;
        inputs.forEach(inp => { if (inp === e.target) isInput = true; });
        if (!isInput) {
            e.preventDefault();
        }
    });

    // Create mode: confirm / cancel
    document.getElementById('table-popover-create-confirm').addEventListener('click', () => confirmCreate());
    document.getElementById('table-popover-create-cancel').addEventListener('click', () => hidePopover());

    // Create mode: enter key on inputs
    rowsInputEl.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') { e.preventDefault(); confirmCreate(); }
        else if (e.key === 'Escape') { e.preventDefault(); hidePopover(); }
    });
    colsInputEl.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') { e.preventDefault(); confirmCreate(); }
        else if (e.key === 'Escape') { e.preventDefault(); hidePopover(); }
    });

    // View mode: row/column actions
    document.getElementById('table-add-row').addEventListener('click', () => {
        ctx.editor.chain().focus().addRowAfter().run();
        refreshViewInfo();
    });
    document.getElementById('table-delete-row').addEventListener('click', () => {
        ctx.editor.chain().focus().deleteRow().run();
        refreshViewInfo();
    });
    document.getElementById('table-add-col').addEventListener('click', () => {
        ctx.editor.chain().focus().addColumnAfter().run();
        refreshViewInfo();
    });
    document.getElementById('table-delete-col').addEventListener('click', () => {
        ctx.editor.chain().focus().deleteColumn().run();
        refreshViewInfo();
    });

    // View mode: toggle header row
    toggleHeaderEl.addEventListener('change', () => {
        ctx.editor.chain().focus().toggleHeaderRow().run();
        refreshViewInfo();
    });

    // View mode: delete table
    document.getElementById('table-delete').addEventListener('click', () => {
        ctx.editor.chain().focus().deleteTable().run();
        hidePopover();
    });

    // Dismiss on scroll, resize, click outside, or window blur
    setupDismiss(editorWrapper, tablePopoverEl, hidePopover);
}

// ---------------------------------------------------------------------------
// Show / hide
// ---------------------------------------------------------------------------

function showCreateMode() {
    rowsInputEl.value = '3';
    colsInputEl.value = '3';
    headerCheckboxEl.checked = true;

    setMode('create');
    tablePopoverEl.classList.add('visible');

    requestAnimationFrame(() => {
        positionAtTop(tablePopoverEl, toolbarEl);
        rowsInputEl.focus();
        rowsInputEl.select();
    });
}

function showViewMode() {
    refreshViewInfo();
    setMode('view');
    tablePopoverEl.classList.add('visible');

    requestAnimationFrame(() => {
        positionAtTop(tablePopoverEl, toolbarEl);
    });
}

function hidePopover() {
    tablePopoverEl.classList.remove('visible');
    currentMode = null;
}

// ---------------------------------------------------------------------------
// Mode switching
// ---------------------------------------------------------------------------

function setMode(mode) {
    currentMode = mode;
    createModeEl.classList.toggle('active', mode === 'create');
    viewModeEl.classList.toggle('active', mode === 'view');
}

// ---------------------------------------------------------------------------
// Create mode
// ---------------------------------------------------------------------------

function confirmCreate() {
    let rows = parseInt(rowsInputEl.value) || 3;
    let cols = parseInt(colsInputEl.value) || 3;
    rows = Math.max(1, Math.min(20, rows));
    cols = Math.max(1, Math.min(20, cols));

    const withHeaderRow = headerCheckboxEl.checked;

    ctx.editor.chain().focus().insertTable({
        rows: withHeaderRow ? rows + 1 : rows,
        cols,
        withHeaderRow,
    }).run();

    hidePopover();
}

// ---------------------------------------------------------------------------
// View mode info
// ---------------------------------------------------------------------------

function refreshViewInfo() {
    const info = getTableInfo();
    if (!info) return;

    viewInfoEl.textContent = `${info.rows} rows × ${info.cols} columns`;
    toggleHeaderEl.checked = info.hasHeader;
}

function getTableInfo() {
    const { editor } = ctx;
    if (!editor.isActive('table')) return null;

    const { state } = editor;
    const { selection } = state;
    const $anchor = selection.$anchor;

    // Walk up to find the table node
    for (let depth = $anchor.depth; depth >= 0; depth--) {
        const node = $anchor.node(depth);
        if (node.type.name === 'table') {
            const rows = node.childCount;
            let cols = 0;
            if (rows > 0) {
                cols = node.child(0).childCount;
            }
            const hasHeader = rows > 0 && node.child(0).child(0)?.type.name === 'tableHeader';
            return { rows, cols, hasHeader };
        }
    }
    return null;
}

// ---------------------------------------------------------------------------
// Toolbar entry point
// ---------------------------------------------------------------------------

export function toggleTable() {
    if (tablePopoverEl.classList.contains('visible')) {
        hidePopover();
        return;
    }

    if (ctx.editor.isActive('table')) {
        showViewMode();
    } else {
        showCreateMode();
    }
}
