// Table popover module for Note editor
// Unified popover: immediate table creation with live editing of dimensions

import { Table, TableRow, TableCell, TableHeader } from './lib/tiptap.js';
import { setupDismiss, positionAtTop, registerPopover, hideAllPopovers } from './popover-utils.js';

let ctx = null;
let tablePopoverEl = null;
let editorWrapper = null;
let toolbarEl = null;
let rowsValueEl = null;
let colsValueEl = null;
let rowsDecBtn = null;
let rowsIncBtn = null;
let colsDecBtn = null;
let colsIncBtn = null;
let isNewTable = false;

const MIN_ROWS = 1;
const MAX_ROWS = 20;
const MIN_COLS = 1;
const MAX_COLS = 20;

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
    rowsValueEl = document.getElementById('table-rows-value');
    colsValueEl = document.getElementById('table-cols-value');
    rowsDecBtn = document.getElementById('table-rows-dec');
    rowsIncBtn = document.getElementById('table-rows-inc');
    colsDecBtn = document.getElementById('table-cols-dec');
    colsIncBtn = document.getElementById('table-cols-inc');

    // Prevent mousedown inside popover from stealing focus
    tablePopoverEl.addEventListener('mousedown', (e) => {
        if (e.target.tagName !== 'INPUT') {
            e.preventDefault();
        }
    });

    registerPopover(hidePopover);

    // Row +/- buttons
    rowsDecBtn.addEventListener('click', () => changeRows(-1));
    rowsIncBtn.addEventListener('click', () => changeRows(1));

    // Column +/- buttons
    colsDecBtn.addEventListener('click', () => changeCols(-1));
    colsIncBtn.addEventListener('click', () => changeCols(1));

    // Escape key on popover
    tablePopoverEl.addEventListener('keydown', (e) => {
        if (e.key === 'Escape') { e.preventDefault(); cancelEdit(); }
    });

    // Delete table
    document.getElementById('table-delete').addEventListener('click', () => {
        ctx.editor.chain().focus().deleteTable().run();
        hidePopover();
    });

    // Dismiss on scroll, resize, click outside, or window blur
    setupDismiss(editorWrapper, tablePopoverEl, hidePopover, (e) => {
        return !!e.target.closest('.toolbar-btn[data-action="table"]');
    });
}

// ---------------------------------------------------------------------------
// Show / hide
// ---------------------------------------------------------------------------

function showPopover() {
    hideAllPopovers();

    refreshPopover();

    tablePopoverEl.classList.add('visible');
    requestAnimationFrame(() => {
        positionAtTop(tablePopoverEl, toolbarEl);
    });
}

function refreshPopover() {
    const info = getTableInfo();
    if (!info) return;

    rowsValueEl.textContent = info.dataRows;
    colsValueEl.textContent = info.cols;

    // Update button states
    rowsDecBtn.disabled = info.dataRows <= MIN_ROWS;
    rowsIncBtn.disabled = info.dataRows >= MAX_ROWS;
    colsDecBtn.disabled = info.cols <= MIN_COLS;
    colsIncBtn.disabled = info.cols >= MAX_COLS;
}

function hidePopover() {
    tablePopoverEl.classList.remove('visible');
    isNewTable = false;
}

function cancelEdit() {
    if (isNewTable) {
        ctx.editor.chain().focus().deleteTable().run();
    }
    hidePopover();
}

// ---------------------------------------------------------------------------
// Row/column changes
// ---------------------------------------------------------------------------

function changeRows(delta) {
    const info = getTableInfo();
    if (!info) return;

    const targetRows = info.dataRows + delta;
    if (targetRows < MIN_ROWS || targetRows > MAX_ROWS) return;

    if (delta > 0) {
        // Add row at the end
        setCursorToLastRow();
        ctx.editor.chain().focus().addRowAfter().run();
    } else {
        // Remove last row
        setCursorToLastRow();
        ctx.editor.chain().focus().deleteRow().run();
    }

    // Ensure caret is inside a valid cell
    ensureCursorInTable();
    refreshPopover();
}

function changeCols(delta) {
    const info = getTableInfo();
    if (!info) return;

    const targetCols = info.cols + delta;
    if (targetCols < MIN_COLS || targetCols > MAX_COLS) return;

    if (delta > 0) {
        // Add column at the end
        setCursorToLastCol();
        ctx.editor.chain().focus().addColumnAfter().run();
    } else {
        // Remove last column
        setCursorToLastCol();
        ctx.editor.chain().focus().deleteColumn().run();
    }

    // Ensure caret is inside a valid cell
    ensureCursorInTable();
    refreshPopover();
}

// ---------------------------------------------------------------------------
// Cursor positioning helpers
// ---------------------------------------------------------------------------

function findTableContext() {
    const { editor } = ctx;
    if (!editor.isActive('table')) return null;

    const { state } = editor;
    const { selection } = state;
    const $anchor = selection.$anchor;

    for (let depth = $anchor.depth; depth >= 0; depth--) {
        const node = $anchor.node(depth);
        if (node.type.name === 'table') {
            return { node, pos: $anchor.before(depth) };
        }
    }
    return null;
}

function getCellContentPos(tablePos, tableNode, rowIndex, colIndex) {
    let pos = tablePos + 1; // enter table
    for (let r = 0; r < rowIndex; r++) {
        pos += tableNode.child(r).nodeSize;
    }
    pos += 1; // enter row
    const row = tableNode.child(rowIndex);
    for (let c = 0; c < colIndex; c++) {
        pos += row.child(c).nodeSize;
    }
    pos += 1; // enter cell
    pos += 1; // enter paragraph inside cell
    return pos;
}

function setCursorToLastRow() {
    const tc = findTableContext();
    if (!tc) return;
    const lastRowIdx = tc.node.childCount - 1;
    const pos = getCellContentPos(tc.pos, tc.node, lastRowIdx, 0);
    ctx.editor.commands.setTextSelection(pos);
}

function setCursorToLastCol() {
    const tc = findTableContext();
    if (!tc) return;
    const firstRow = tc.node.child(0);
    const lastColIdx = firstRow.childCount - 1;
    const pos = getCellContentPos(tc.pos, tc.node, 0, lastColIdx);
    ctx.editor.commands.setTextSelection(pos);
}

function ensureCursorInTable() {
    // After a dimension change, verify cursor is still in table
    // If not, place it in the first data cell
    if (ctx.editor.isActive('table')) return;

    // Find the table we were editing (should be nearby in the doc)
    // For now, re-find and position to first cell
    const tc = findTableContextFromDoc();
    if (tc) {
        const firstDataRowIdx = tc.hasHeader ? 1 : 0;
        const rowIdx = Math.min(firstDataRowIdx, tc.node.childCount - 1);
        const pos = getCellContentPos(tc.pos, tc.node, rowIdx, 0);
        ctx.editor.commands.setTextSelection(pos);
    }
}

function findTableContextFromDoc() {
    // Walk the document to find the first table (used after cursor falls outside)
    const { state } = ctx.editor;
    let result = null;
    state.doc.descendants((node, pos) => {
        if (result) return false;
        if (node.type.name === 'table') {
            const hasHeader = node.childCount > 0 && node.child(0).child(0)?.type.name === 'tableHeader';
            result = { node, pos, hasHeader };
            return false;
        }
    });
    return result;
}

// ---------------------------------------------------------------------------
// Table info
// ---------------------------------------------------------------------------

function getTableInfo() {
    const { editor } = ctx;
    if (!editor.isActive('table')) return null;

    const tc = findTableContext();
    if (!tc) return null;

    const rows = tc.node.childCount;
    let cols = 0;
    if (rows > 0) {
        cols = tc.node.child(0).childCount;
    }
    const hasHeader = rows > 0 && tc.node.child(0).child(0)?.type.name === 'tableHeader';
    const dataRows = hasHeader ? rows - 1 : rows;

    return { rows, cols, hasHeader, dataRows };
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
        isNewTable = false;
        showPopover();
    } else {
        // Insert 3x3 table with header immediately and show popover
        isNewTable = true;
        ctx.editor.chain().focus().insertTable({
            rows: 4, // 3 data rows + 1 header row
            cols: 3,
            withHeaderRow: true,
        }).run();
        showPopover();
    }
}
