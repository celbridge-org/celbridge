// Spreadsheet editor initialization for Celbridge WebView integration.
// Uses celbridge.js for JSON-RPC communication with the host.

import celbridge from 'https://shared.celbridge/celbridge-client/celbridge.js';
import { ContentLoadedReason } from 'https://shared.celbridge/celbridge-client/api/document-api.js';

if (!window.isWebView) {
    console.log('Not running in WebView, skipping client initialization');
}

const client = celbridge;

let designer = null;

async function deserializeExcelData(base64Data, viewState = null) {
    if (!base64Data) {
        client.document.notifyImportComplete(true);
        return;
    }

    let spread;
    try {
        spread = designer.getWorkbook();
    } catch (err) {
        // If SpreadJS rejected the license post-designer-construction, the
        // workbook is in a broken state and getWorkbook() throws. Report the
        // failure but do not propagate — the outer initializeDocument must
        // still reach notifyContentLoaded so the host does not time out.
        console.error('[Spreadsheet] getWorkbook threw (editor unavailable):', err);
        client.document.notifyImportComplete(false, err?.message || 'Editor unavailable');
        return;
    }

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

        if (viewState) {
            spread.suspendPaint();
        }

        await new Promise((resolve, reject) => {
            spread.import(file, () => {
                // spread.import() rebuilds worksheets, dropping any sheet-scoped
                // event bindings. Re-bind before the user can interact again.
                bindSheetEvents();

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

function selectionsMatch(a, b) {
    if (!a || !b) return false;
    if (a.length !== b.length) return false;
    for (let i = 0; i < a.length; i++) {
        if (a[i].row !== b[i].row
            || a[i].col !== b[i].col
            || a[i].rowCount !== b[i].rowCount
            || a[i].colCount !== b[i].colCount) {
            return false;
        }
    }
    return true;
}

function restoreViewState(state) {
    // Active sheet and selection are auto-saved to disk on every change via the
    // ActiveSheetChanged and SelectionChanged hooks in listenForChanges, so the
    // freshly-imported workbook already reflects the user's pre-reload sheet and
    // selection (or the new ones written by an MCP set_active_view, if that's
    // what triggered the reload). Scroll position is the one piece of view state
    // we deliberately do not auto-save, so we restore it from the in-memory
    // snapshot here, but only when disk's active sheet and selection still match
    // the snapshot. If either differs, the reload was driven by a deliberate
    // view-state change and disk should win for scroll too.
    if (!state || !designer) return;
    try {
        const spread = designer.getWorkbook();
        const activeSheet = spread.getActiveSheet();
        if (!activeSheet) return;

        if (activeSheet.name() !== state.sheetName) return;
        if (!selectionsMatch(activeSheet.getSelections(), state.selections)) return;

        activeSheet.showRow(state.scrollRow, GC.Spread.Sheets.VerticalPosition.top);
        activeSheet.showColumn(state.scrollColumn, GC.Spread.Sheets.HorizontalPosition.left);
    } catch (error) {
        console.warn('[Spreadsheet] Failed to restore view state:', error);
    }
}

function listenForChanges() {
    const workbook = designer.getWorkbook();
    const commandManager = workbook.commandManager();

    // SpreadJS doesn't have a unified way to detect when the spreadsheet is modified,
    // but as all modifications are performed via the command system we can just
    // listen for any executing commands and assume that the data has changed.
    // Note that mouse-driven selection changes do not flow through commandManager,
    // so the SelectionChanged hook in bindSheetEvents covers that gap.
    commandManager.addListener('appListener', (args) => {
        client.document.notifyChanged();
    });

    // ActiveSheetChanged is workbook-scoped and survives spread.import().
    // SelectionChanged is sheet-scoped and is dropped when import rebuilds
    // worksheets, so it's re-bound from bindSheetEvents after every import.
    workbook.bind(GC.Spread.Sheets.Events.ActiveSheetChanged, () => {
        client.document.notifyChanged();
        bindSheetEvents();
    });

    bindSheetEvents();
}

function bindSheetEvents() {
    if (!designer) return;
    try {
        const workbook = designer.getWorkbook();
        const sheetCount = workbook.getSheetCount();
        for (let i = 0; i < sheetCount; i++) {
            const sheet = workbook.getSheet(i);
            sheet.unbind(GC.Spread.Sheets.Events.SelectionChanged);
            sheet.bind(GC.Spread.Sheets.Events.SelectionChanged, () => {
                client.document.notifyChanged();
            });
        }
    } catch (error) {
        console.warn('[Spreadsheet] Failed to bind sheet selection events:', error);
    }
}

function initializeSpreadsheet() {
    // Apply license keys from the host-injected secret map.
    const licenseKey = client.secrets.spreadjs_license_key;
    const designerLicenseKey = client.secrets.spreadjs_designer_license_key;

    // Fast-fail when either key is missing so SpreadJS never runs on empty strings.
    if (!licenseKey || !designerLicenseKey) {
        console.error('[Spreadsheet] SpreadJS license keys missing from injected secrets. ' +
            'Expected `spreadjs_license_key` and `spreadjs_designer_license_key`. ' +
            'Got: license=' + (licenseKey ? 'present' : 'missing') +
            ', designer=' + (designerLicenseKey ? 'present' : 'missing') + '.');
        return false;
    }

    GC.Spread.Sheets.LicenseKey = licenseKey;
    GC.Spread.Sheets.Designer.LicenseKey = designerLicenseKey;

    try {
        const config = GC.Spread.Sheets.Designer.DefaultConfig;
        delete config.fileMenu;

        designer = new GC.Spread.Sheets.Designer.Designer(
            document.getElementById("gc-designer-container"), config
        );

        window.designer = designer;

        listenForChanges();
        return true;
    } catch (e) {
        console.error('[Spreadsheet] Designer construction failed:', e);
        return false;
    }
}

async function initializeEditor() {
    try {
        const ready = initializeSpreadsheet();
        if (!ready) {
            // Still complete the document handshake so the host's load flow
            // doesn't hang waiting for onContent/notifyImportComplete. The
            // WebView either shows an empty gc-designer-container or SpreadJS's
            // own rejection dialog; either way we refuse to accept content
            // or produce saves.
            await client.initializeDocument({
                onContent: async () => {
                    client.document.notifyImportComplete(false, 'Spreadsheet editor unavailable');
                },
                onRequestSave: async () => {
                    throw new Error('Spreadsheet editor unavailable');
                },
                onExternalChange: async () => {
                    client.document.notifyContentLoaded(ContentLoadedReason.ExternalReload);
                },
                onRequestState: () => null,
                onRestoreState: () => {}
            });
            return;
        }

        await client.initializeDocument({
            onContent: async (content) => {
                // Wrap the whole onContent body so a throw here cannot prevent
                // initializeDocument from calling notifyContentLoaded. A timeout
                // on the host side is strictly worse than a failed import —
                // the host times out without ever showing the editor, while
                // a failed import at least puts a visible editor on screen.
                try {
                    if (content) {
                        await deserializeExcelData(content);
                    } else {
                        client.document.notifyImportComplete(true);
                    }
                } catch (err) {
                    console.error('[Spreadsheet] onContent failed:', err);
                    client.document.notifyImportComplete(false, err?.message || 'onContent failed');
                }
            },
            onRequestSave: async () => {
                try {
                    const base64Data = await serializeExcelData();
                    await client.document.save(base64Data);
                } catch (e) {
                    console.error('[Spreadsheet] Failed to save:', e);
                }
            },
            onExternalChange: async () => {
                // Capture view state locally and pass it through deserializeExcelData so the
                // suspendPaint + requestAnimationFrame + restoreViewState path preserves scroll
                // and selection across the re-import. The host also sends onRestoreState after
                // notifyContentLoaded fires, but that RPC arrives while the SpreadJS viewport
                // is still settling and showRow/showColumn calls from it do not take effect.
                const savedViewState = captureViewState();
                try {
                    const result = await client.document.load();
                    await deserializeExcelData(result.content, savedViewState);
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

initializeEditor();
