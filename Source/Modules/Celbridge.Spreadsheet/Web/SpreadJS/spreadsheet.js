// Spreadsheet editor initialization for Celbridge WebView integration.
// Uses celbridge.js for JSON-RPC communication with the host.

import celbridge from 'https://shared.celbridge/celbridge-client/celbridge.js';
import { ContentLoadedReason } from 'https://shared.celbridge/celbridge-client/api/document-api.js';

// Only proceed if running in WebView
if (!window.isWebView) {
    console.log('Not running in WebView, skipping client initialization');
}

// Get the client instance
const client = celbridge;

// SpreadJS designer instance (set in initializeSpreadsheet)
let designer = null;

// ---------------------------------------------------------------------------
// Spreadsheet Import/Export Functions
// ---------------------------------------------------------------------------

async function deserializeExcelData(base64Data, viewState = null) {
    if (!base64Data) {
        console.log('No data to import');
        client.document.notifyImportComplete(true);
        return;
    }

    const spread = designer.getWorkbook();

    try {
        const binary = atob(base64Data);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) {
            bytes[i] = binary.charCodeAt(i);
        }

        const blob = new Blob([bytes], {
            type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'
        });
        const file = new File([blob], 'imported.xlsx', { type: blob.type });

        // Suspend painting before import so SpreadJS does not flash the default
        // (0, 0) position before view state is restored in the rAF callback.
        if (viewState) {
            spread.suspendPaint();
        }

        await new Promise((resolve, reject) => {
            spread.import(file, () => {
                console.log("Base64 data import completed.");
                if (viewState) {
                    requestAnimationFrame(() => {
                        restoreViewState(viewState);
                        spread.resumePaint();
                        client.document.notifyImportComplete(true);
                        resolve();
                    });
                } else {
                    client.document.notifyImportComplete(true);
                    resolve();
                }
            }, (error) => {
                if (viewState) {
                    spread.resumePaint();
                }
                console.error("Import error:", error);
                client.document.notifyImportComplete(false, error?.message || 'Import failed');
                reject(error);
            }, {
                fileType: GC.Spread.Sheets.FileType.excel
            });
        });
    } catch (err) {
        if (viewState) {
            spread.resumePaint();
        }
        console.error("Import failed:", err);
        client.document.notifyImportComplete(false, err?.message || 'Import exception');
    }
}

async function serializeExcelData() {
    const spread = designer.getWorkbook();

    return new Promise((resolve, reject) => {
        try {
            spread.export(async (blob) => {
                const base64 = await blobToBase64(blob);
                resolve(base64);
            }, (error) => {
                console.error("Export error:", error);
                reject(error);
            }, {
                fileType: GC.Spread.Sheets.FileType.excel
            });
        } catch (err) {
            console.error("Export failed:", err);
            reject(err);
        }
    });
}

function blobToBase64(blob) {
    return new Promise((resolve, reject) => {
        const reader = new FileReader();
        reader.onloadend = () => resolve(reader.result.split(',')[1]);
        reader.onerror = reject;
        reader.readAsDataURL(blob);
    });
}

// ---------------------------------------------------------------------------
// View State Capture and Restore
// ---------------------------------------------------------------------------

function captureViewState() {
    if (!designer) return null;
    try {
        const spread = designer.getWorkbook();
        const activeSheet = spread.getActiveSheet();
        if (!activeSheet) return null;
        return {
            sheetName: activeSheet.name(),
            selections: activeSheet.getSelections(),
            scrollRow: activeSheet.getViewportTopRow(1),
            scrollColumn: activeSheet.getViewportLeftColumn(1)
        };
    } catch (error) {
        console.warn('[Spreadsheet] Failed to capture view state:', error);
        return null;
    }
}

function restoreViewState(state) {
    if (!state || !designer) return;
    try {
        const spread = designer.getWorkbook();

        // Restore active sheet - fall back to current sheet if the name no longer exists.
        const sheetIndex = spread.getSheetIndex(state.sheetName);
        if (sheetIndex >= 0) {
            spread.setActiveSheetIndex(sheetIndex);
        }

        const activeSheet = spread.getActiveSheet();

        // Restore all selections. If none were saved, select the origin cell.
        if (state.selections && state.selections.length > 0) {
            const first = state.selections[0];
            activeSheet.setSelection(first.row, first.col, first.rowCount, first.colCount);
            for (let i = 1; i < state.selections.length; i++) {
                const sel = state.selections[i];
                activeSheet.addSelection(sel.row, sel.col, sel.rowCount, sel.colCount);
            }
        }

        // Restore scroll position.
        activeSheet.showRow(state.scrollRow, GC.Spread.Sheets.VerticalPosition.top);
        activeSheet.showColumn(state.scrollColumn, GC.Spread.Sheets.HorizontalPosition.left);
    } catch (error) {
        console.warn('[Spreadsheet] Failed to restore view state:', error);
    }
}

// ---------------------------------------------------------------------------
// Change Tracking
// ---------------------------------------------------------------------------

function listenForChanges() {
    const workbook = designer.getWorkbook();
    const commandManager = workbook.commandManager();

    // SpreadJS doesn't have a unified way to detect when the spreadsheet is modified,
    // but as all modifications are performed via the command system we can just
    // listen for any executing commands and assume that the data has changed.
    commandManager.addListener('appListener', (args) => {
        client.document.notifyChanged();
    });
}

// ---------------------------------------------------------------------------
// Initialization
// ---------------------------------------------------------------------------

function initializeSpreadsheet() {
    // Apply license keys
    GC.Spread.Sheets.LicenseKey = SPREAD_JS_LICENSE_KEY;
    GC.Spread.Sheets.Designer.LicenseKey = SPREAD_JS_DESIGNER_LICENSE_KEY;

    const config = GC.Spread.Sheets.Designer.DefaultConfig;
    delete config.fileMenu;

    designer = new GC.Spread.Sheets.Designer.Designer(
        document.getElementById("gc-designer-container"), config
    );

    window.designer = designer;

    listenForChanges();
}

async function initializeEditor() {
    try {
        initializeSpreadsheet();

        await client.initializeDocument({
            onContent: async (content) => {
                if (content) {
                    await deserializeExcelData(content);
                } else {
                    client.document.notifyImportComplete(true);
                }
            },
            onRequestSave: async () => {
                try {
                    const base64Data = await serializeExcelData();
                    await client.document.save(base64Data);
                    console.log("Exported workbook to host");
                } catch (e) {
                    console.error('[Spreadsheet] Failed to save:', e);
                }
            },
            onExternalChange: async () => {
                // Editor state (zoom, active sheet, selection) is preserved by the framework via
                // onRequestState / onRestoreState, orchestrated around this handler by
                // SpreadsheetDocumentView. Just load the new content and signal completion.
                try {
                    const result = await client.document.load();
                    await deserializeExcelData(result.content);
                } catch (e) {
                    console.error('[Spreadsheet] Failed to reload content:', e);
                }

                client.document.notifyContentLoaded(ContentLoadedReason.ExternalReload);
            },
            onRequestState: () => {
                const state = captureViewState();
                return state ? JSON.stringify(state) : null;
            },
            onRestoreState: (stateJson) => {
                try {
                    const state = JSON.parse(stateJson);
                    restoreViewState(state);
                } catch (e) {
                    console.warn('[Spreadsheet] Failed to restore view state:', e);
                }
            }
        });
    } catch (e) {
        console.error('[Spreadsheet] Failed to initialize:', e);
    }
}

// Start initialization
initializeEditor();
