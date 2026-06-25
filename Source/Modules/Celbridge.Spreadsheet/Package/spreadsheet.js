// Spreadsheet editor initialization for Celbridge WebView integration.
// Uses celbridge.js for JSON-RPC communication with the host.

import celbridge from 'https://shared.celbridge/celbridge-client/celbridge.js';
import { ContentLoadedReason } from 'https://shared.celbridge/celbridge-client/api/document-api.js';

if (!window.isWebView) {
    console.log('Not running in WebView, skipping client initialization');
}

const client = celbridge;

let designer = null;

// True when the host has signalled the file is read-only. Drives the
// translucent overlay and gates the notifyChanged paths below so events
// fired during the locked window can't queue a save.
let frameworkReadOnly = false;

async function deserializeExcelData(base64Data, viewState = null, preserveView = true) {
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
                        restoreViewState(viewState, preserveView);
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

function restoreViewState(state, preserveView = false) {
    // Active sheet name is the one piece of identity we always honour: a sheet
    // rename collapses the captured snapshot's frame of reference so there is
    // no sensible scroll to apply. When preserveView is true (the default for
    // external watcher reloads and for data-changing commands) the snapshot's
    // scroll wins over disk. When preserveView is false, we only restore scroll
    // if disk's selection still matches the snapshot — preserving the original
    // contract for view-changing commands like set_active_view.
    if (!state || !designer) return;
    try {
        const spread = designer.getWorkbook();
        const activeSheet = spread.getActiveSheet();
        if (!activeSheet) return;

        if (activeSheet.name() !== state.sheetName) return;
        if (!preserveView && !selectionsMatch(activeSheet.getSelections(), state.selections)) return;

        activeSheet.showRow(state.scrollRow, GC.Spread.Sheets.VerticalPosition.top);
        activeSheet.showColumn(state.scrollColumn, GC.Spread.Sheets.HorizontalPosition.left);
    } catch (error) {
        console.warn('[Spreadsheet] Failed to restore view state:', error);
    }
}

function listenForChanges() {
    const workbook = designer.getWorkbook();
    const commandManager = workbook.commandManager();

    // Every edit flows through a command, so listening to all commands stands
    // in for a "doc modified" signal SpreadJS doesn't expose directly. Mouse-
    // driven selection changes bypass commandManager — SelectionChanged in
    // bindSheetEvents covers that gap.
    commandManager.addListener('appListener', (args) => {
        if (frameworkReadOnly) return;
        client.document.notifyChanged();
    });

    // ActiveSheetChanged is workbook-scoped and survives spread.import();
    // SelectionChanged is per-sheet and gets dropped on import, so it's
    // re-bound from bindSheetEvents.
    workbook.bind(GC.Spread.Sheets.Events.ActiveSheetChanged, () => {
        if (!frameworkReadOnly) {
            client.document.notifyChanged();
        }
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
                if (frameworkReadOnly) return;
                client.document.notifyChanged();
            });
        }
    } catch (error) {
        console.warn('[Spreadsheet] Failed to bind sheet selection events:', error);
    }
}

function applyWritableState(state) {
    frameworkReadOnly = state !== 'Writable';
    showReadOnlyOverlay(frameworkReadOnly);
}

// Visual cue and pointer-event sink. Not the durable read-only block — the
// frameworkReadOnly gates above are what stop saves.
function showReadOnlyOverlay(visible) {
    let overlay = document.getElementById('readonly-overlay');
    if (!overlay) {
        overlay = document.createElement('div');
        overlay.id = 'readonly-overlay';
        overlay.setAttribute('aria-label', 'Spreadsheet is read-only');
        overlay.setAttribute('role', 'status');
        document.body.appendChild(overlay);
    }
    overlay.style.display = visible ? 'block' : 'none';

    // Drop focus so a keypress can't land in a cell editor that was active
    // when the file got locked externally.
    if (visible
        && document.activeElement
        && document.activeElement !== document.body
        && typeof document.activeElement.blur === 'function') {
        document.activeElement.blur();
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
        // Resolve the host capability context before initializeSpreadsheet reads the
        // SpreadJS license keys from client.secrets. On the Skia head the context arrives
        // over the bridge (host/getContext), so the secrets are empty until ready()
        // resolves. The packaged WinUI head resolves immediately from the injected global.
        await client.ready();

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

        client.viewState.onChanged((viewState) => {
            if (viewState.writable) {
                applyWritableState(viewState.writable);
            }
        });

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
            onExternalChange: async (args) => {
                // Capture view state locally and pass it through deserializeExcelData so the
                // suspendPaint + requestAnimationFrame + restoreViewState path preserves scroll
                // and selection across the re-import. The host also sends onRestoreState after
                // notifyContentLoaded fires, but that RPC arrives while the SpreadJS viewport
                // is still settling and showRow/showColumn calls from it do not take effect.
                //
                // The host passes preserveViewState=true for watcher-driven reloads and for
                // data-changing commands; view-changing commands like set_active_view set
                // preserveViewState=false so disk's selection and scroll win.
                const preserveView = args?.preserveViewState ?? true;
                const savedViewState = captureViewState();
                try {
                    const result = await client.document.load();
                    await deserializeExcelData(result.content, savedViewState, preserveView);
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
