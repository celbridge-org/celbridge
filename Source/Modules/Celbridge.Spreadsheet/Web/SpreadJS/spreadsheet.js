// Spreadsheet editor initialization for Celbridge WebView integration.
// Uses celbridge.js for JSON-RPC communication with the host.

import celbridge from 'https://shared.celbridge/celbridge-client/celbridge.js';

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

async function deserializeExcelData(base64Data) {
    if (!base64Data) {
        console.log('No data to import');
        client.document.notifyImportComplete(true);
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

        const spread = designer.getWorkbook();

        await spread.import(file, () => {
            console.log("Base64 data import completed.");
            client.document.notifyImportComplete(true);
        }, (error) => {
            console.error("Import error:", error);
            client.document.notifyImportComplete(false, error?.message || 'Import failed');
        }, {
            fileType: GC.Spread.Sheets.FileType.excel
        });
    } catch (err) {
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
// Client Event Handlers
// ---------------------------------------------------------------------------

// Handle external file changes - reload from disk
client.document.onExternalChange(async () => {
    try {
        const result = await client.document.load();
        await deserializeExcelData(result.content);
    } catch (e) {
        console.error('[Spreadsheet] Failed to reload content:', e);
    }
});

// Handle save requests from host
client.document.onRequestSave(async () => {
    try {
        const base64Data = await serializeExcelData();
        await client.document.save(base64Data);
        console.log("Exported workbook to host");
    } catch (e) {
        console.error('[Spreadsheet] Failed to save:', e);
    }
});

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
        // Initialize the SpreadJS designer
        initializeSpreadsheet();

        // Enable debug logging during development
        // client.setLogLevel('debug');

        // Initialize the client - this loads content from C#
        const result = await client.initialize();

        // Import the spreadsheet data (content contains base64 Excel data)
        if (result.content) {
            await deserializeExcelData(result.content);
        } else {
            // No content means new/empty spreadsheet
            client.document.notifyImportComplete(true);
        }

    } catch (e) {
        console.error('[Spreadsheet] Failed to initialize:', e);
    }
}

// Start initialization
initializeEditor();
