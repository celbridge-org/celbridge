// Spreadsheet editor initialization for Celbridge WebView integration.
// Uses celbridge.js for JSON-RPC communication with the host.

import celbridge from 'https://shared.celbridge/celbridge-client/celbridge.js';
import { ContentLoadedReason } from 'https://shared.celbridge/celbridge-client/api/document-api.js';

const client = celbridge;

let designer = null;

// True when the host has signalled the file is read-only. Drives the
// translucent overlay and gates the notifyChanged paths below so events
// fired during the locked window can't queue a save.
let frameworkReadOnly = false;

// The range the last getSelectedText returned, so a cut clears what it copied rather than the live selection.
let copiedRange = null;

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
    commandManager.addListener('appListener', () => {
        if (frameworkReadOnly) return;
        client.document.notifyChanged();
    });

    // ActiveSheetChanged is workbook-scoped and survives spread.import().
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
    reportEditAvailability();
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
        // Reported to the host log: DevTools are blocked for this package, so console output is unreachable.
        client.log.error('[Spreadsheet] SpreadJS license keys missing from injected secrets. ' +
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
        const container = document.getElementById('gc-designer-container');
        // The container size is logged because a WebView that loads while unarranged reports a zero viewport.
        client.log.error('[Spreadsheet] Designer construction failed'
            + ' (container ' + (container ? container.clientWidth + 'x' + container.clientHeight : 'missing')
            + ', viewport ' + window.innerWidth + 'x' + window.innerHeight + ')', e);
        return false;
    }
}

// The workbook and its active sheet, or null while the editor is unavailable (SpreadJS throws from
// getWorkbook when it rejected the license).
function getActiveSheet() {
    if (!designer) {
        return null;
    }

    let spread;
    try {
        spread = designer.getWorkbook();
    } catch {
        return null;
    }

    const sheet = spread?.getActiveSheet();
    if (!sheet) {
        return null;
    }

    return { spread, sheet };
}

// Applies Tab (or Shift+Tab) to the workbook, which the host forwards after swallowing the native key.
function handleTabKey(shift) {
    const context = getActiveSheet();
    if (!context) {
        return;
    }

    // SpreadJS binds Tab to these commands itself, so running them gives the key the behaviour it has on the
    // packaged Windows head, where it reaches the WebView: an open cell editor is committed before the
    // selection moves, the move stays inside a selected range, and it wraps to the next row at the end of one.
    const command = shift ? 'moveToPreviousCell' : 'moveToNextCell';

    context.spread.commandManager().execute({
        cmd: command,
        sheetName: context.sheet.name()
    });
}

// The selection as tab separated columns and newline separated rows, which the host fetches for copy and
// cut.
function getSelectedText() {
    const context = getActiveSheet();
    if (!context) {
        return '';
    }

    const sheet = context.sheet;
    const selection = (sheet.getSelections() ?? [])[0];
    if (!selection) {
        copiedRange = null;
        return '';
    }

    // A whole row or column selection carries -1 for the open axis, so the range is trimmed to the cells in
    // use rather than the sheet's whole empty extent. The trim never pulls the end below the start.
    const used = sheet.getUsedRange(GC.Spread.Sheets.UsedRangeType.all);
    const usedEndRow = used ? used.row + used.rowCount - 1 : -1;
    const usedEndColumn = used ? used.col + used.colCount - 1 : -1;

    const firstRow = Math.max(selection.row, 0);
    const firstColumn = Math.max(selection.col, 0);

    let endRow = selection.row < 0 ? usedEndRow : selection.row + selection.rowCount - 1;
    let endColumn = selection.col < 0 ? usedEndColumn : selection.col + selection.colCount - 1;

    if (usedEndRow >= firstRow) {
        endRow = Math.min(endRow, usedEndRow);
    }
    if (usedEndColumn >= firstColumn) {
        endColumn = Math.min(endColumn, usedEndColumn);
    }

    const rows = [];
    for (let row = firstRow; row <= endRow; row++) {
        const cells = [];
        for (let column = firstColumn; column <= endColumn; column++) {
            cells.push(sheet.getText(row, column) ?? '');
        }
        rows.push(cells.join('\t'));
    }

    // The cut's clear step removes exactly this, so a range the copy never read keeps its values.
    copiedRange = new GC.Spread.Sheets.Range(
        firstRow, firstColumn, endRow - firstRow + 1, endColumn - firstColumn + 1);

    return rows.join('\n');
}

// Applies clipboard text from the host. Empty text is the delete half of a cut, so it clears the selection.
function insertText(text) {
    const context = getActiveSheet();
    if (!context || frameworkReadOnly) {
        return;
    }

    const { spread, sheet } = context;
    const commandManager = spread.commandManager();

    if (!text) {
        // clearValues acts only on the ranges it is given, not on the sheet's own selection.
        const ranges = copiedRange ? [copiedRange] : [];
        copiedRange = null;
        if (ranges.length === 0) {
            return;
        }

        commandManager.execute({
            cmd: 'clearValues',
            sheetName: sheet.name(),
            ranges
        });
        return;
    }

    const row = sheet.getActiveRowIndex();
    const column = sheet.getActiveColumnIndex();

    commandManager.execute({
        cmd: 'clipboardPaste',
        sheetName: sheet.name(),
        clipboardText: text,
        pasteOption: GC.Spread.Sheets.ClipboardPasteOptions.all,
        pastedRanges: [new GC.Spread.Sheets.Range(row, column, 1, 1)]
    });
}

// Runs SpreadJS's own commands for the verbs that touch no clipboard.
function performEdit(command) {
    const context = getActiveSheet();
    if (!context) {
        return;
    }

    const { spread, sheet } = context;

    if (command === 'selectAll') {
        // SpreadJS's selectAll acts on the cell editor's text, not the grid, so select every cell as a range.
        sheet.setSelection(0, 0, sheet.getRowCount(), sheet.getColumnCount());
        return;
    }

    if (command === 'undo' || command === 'redo') {
        spread.commandManager().execute({
            cmd: command,
            sheetName: sheet.name()
        });
    }
}

// Reports which verbs the host Edit menu should offer. A grid always has an active cell, so copy has
// something to take even with no range selected.
function reportEditAvailability() {
    const available = getActiveSheet() !== null;

    client.input.notifyEditAvailability({
        canCopy: available,
        canCut: available && !frameworkReadOnly,
        canPaste: available && !frameworkReadOnly,
        canSelectAll: available,
        canUndo: available && !frameworkReadOnly,
        canRedo: available && !frameworkReadOnly,
        hostMediatedClipboard: true
    });
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
            // own rejection dialog. Either way we refuse to accept content
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

        // The host forwards Tab here (it swallows the native key so focus cannot leave the document).
        client.onNotification('input/tabKey', (params) => {
            handleTabKey(params?.shift === true);
        });

        // The host mediates the clipboard: SpreadJS reaches it through the browser, which the macOS WebView
        // refuses.
        client.onRequest('editor/getSelectedText', () => getSelectedText());
        client.onNotification('editor/insertText', (params) => {
            insertText(params?.text ?? '');
        });
        client.onNotification('input/performEdit', (params) => {
            performEdit(params?.command);
        });

        document.addEventListener('focusin', reportEditAvailability);
        reportEditAvailability();

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
                // data-changing commands. View-changing commands like set_active_view set
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
        // Reported to the host log: DevTools are blocked for this package, so console output is unreachable.
        client.log.error('[Spreadsheet] Failed to initialize', e);
    }
}

initializeEditor();
