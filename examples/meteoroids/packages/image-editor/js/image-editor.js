// Image Editor document entry point. The document is the file's decoded pixels plus a list of whole-image
// operations (crop, resize, rotate, flip). Each save encodes the current image once, from the original
// pixels, and splices the file's metadata back in, so repeated auto-saves never compound loss.
//
// The editor is declared binary, so the host hands it the document as base64 and saves the base64 it is given.

import celbridge from '/assets/celbridge-client/celbridge.js';
import { ContentLoadedReason } from '/assets/celbridge-client/api/document-api.js';
import { t } from '/assets/celbridge-client/localization.js';
import { base64ByteLength, base64ToBytes, bytesToBase64 } from './bytes.js';
import { FORMATS, detectFormat, extensionOf, extractLargestIcon, formatForExtension, isAnimated } from './formats.js';
import {
    adjustMetadata, emptyMetadata, extractMetadata, gifComments, metadataKinds, needsColourManagement, writeMetadata,
} from './metadata.js';
import { decodeBitmap, encodeCanvas, transparencyOf } from './codec.js';
import { changesGeometry, createHistory, createPipeline, effectiveOperations, sizeAfter } from './operations.js';
import { createImageView, isTextTarget } from './view.js';
import { createCropTool } from './crop.js';
import {
    MAX_PIXELS, MAX_SIDE, anyPopoverOpen, closeAllPopovers, createResizePanel, createSettingsPanel,
    formatBytes,
} from './panels.js';

// DOM elements
const app = document.getElementById('app');
const toolbar = document.getElementById('toolbar');
const undoButton = document.getElementById('undo');
const redoButton = document.getElementById('redo');
const cropButton = document.getElementById('crop');
const resizeButton = document.getElementById('resize');
const rotateLeftButton = document.getElementById('rotate-left');
const rotateRightButton = document.getElementById('rotate-right');
const flipHorizontalButton = document.getElementById('flip-horizontal');
const flipVerticalButton = document.getElementById('flip-vertical');
const zoomActualButton = document.getElementById('zoom-actual');
const zoomOutButton = document.getElementById('zoom-out');
const zoomInButton = document.getElementById('zoom-in');
const zoomLabel = document.getElementById('zoom-label');
const saveSettingsButton = document.getElementById('save-settings');
const cropBar = document.getElementById('crop-bar');
const cropFields = {
    x: document.getElementById('crop-x'),
    y: document.getElementById('crop-y'),
    width: document.getElementById('crop-width'),
    height: document.getElementById('crop-height'),
};
const cropApplyButton = document.getElementById('crop-apply');
const cropCancelButton = document.getElementById('crop-cancel');
const notice = document.getElementById('notice');
const noticeText = document.getElementById('notice-text');
const stage = document.getElementById('stage');
const viewCanvas = document.getElementById('view');
const statusDimensions = document.getElementById('status-dimensions');
const statusFormat = document.getElementById('status-format');
const statusSize = document.getElementById('status-size');
const statusMetadata = document.getElementById('status-metadata');
const statusPointer = document.getElementById('status-pointer');
const statusZoom = document.getElementById('status-zoom');
const errorView = document.getElementById('error-view');
const errorText = document.getElementById('error-text');

const client = celbridge;

// State
let documentResource = '';
let fileFormat = null;
let detectedFormat = null;
let originalBase64 = '';
let originalSize = 0;
let fileSize = 0;
let metadata = emptyMetadata();
let pipeline = null;
let displayPipeline = null;
let sourceWidth = 0;
let sourceHeight = 0;
let defaultEncoding = { quality: 90, lossless: false };
let encoding = { ...defaultEncoding };
// Whether there are changes the host has not yet saved.
let dirty = false;
let writable = true;
let blocked = null;
let loadError = null;
let cropping = false;
let pointer = null;
let warnedJpegTransparency = false;
let warnedMalformedExif = false;
let transparencyCache = { key: null, value: 'none' };

// Set whenever the document must not be written: a locked file, a format the editor cannot save, an
// animated or oversized image, or one that failed to decode. Every path that reports a change checks it.
let frameworkReadOnly = false;

const history = createHistory();

const view = createImageView({
    host: stage,
    canvas: viewCanvas,
    onChanged: updateZoom,
    onPointerImage: (point) => {
        pointer = point;
        statusPointer.textContent = point ? t('IE_Status_Pointer', point.x, point.y) : '';
    },
});

const cropTool = createCropTool({ onChanged: syncCropFields });

const resizePanel = createResizePanel({
    panel: document.getElementById('resize-panel'),
    anchor: resizeButton,
    getSize: () => currentSize(),
    onApply: (operation) => applyOperation(operation),
});

const settingsPanel = createSettingsPanel({
    panel: document.getElementById('settings-panel'),
    anchor: saveSettingsButton,
    getEncoding: () => encoding,
    getFormat: () => fileFormat,
    onChange: (change) => {
        if (frameworkReadOnly) {
            return;
        }
        encoding = { ...encoding, ...change };
        reportChange();
    },
});

function currentOperations() {
    return effectiveOperations(history.applied());
}

function currentSize() {
    return sizeAfter(sourceWidth, sourceHeight, currentOperations());
}

// The host saves about a second after the last reported change, by calling onRequestSave.
function reportChange() {
    if (frameworkReadOnly) {
        return;
    }
    dirty = true;
    client.document.notifyChanged();
}

function showImage({ keepView = true } = {}) {
    const operations = currentOperations();
    const { width, height } = sizeAfter(sourceWidth, sourceHeight, operations);
    view.setImage((displayPipeline || pipeline).render(operations), width, height, { keepView });
    updateStatus();
}

function applyOperation(operation) {
    if (frameworkReadOnly || !pipeline) {
        return;
    }
    history.push(operation);
    afterEdit();
}

function undo() {
    if (!frameworkReadOnly && history.undo()) {
        afterEdit();
    }
}

function redo() {
    if (!frameworkReadOnly && history.redo()) {
        afterEdit();
    }
}

function afterEdit() {
    try {
        showImage();
    } catch (e) {
        console.error('[Image Editor] Failed to render:', e);
        client.dialog.showNotification('error', t('IE_Toast_RenderFailed'));
    }
    updateControls();
    reportChange();
}

function rotate(degrees) {
    applyOperation({ type: 'rotate', degrees });
}

function flip(axis) {
    applyOperation({ type: 'flip', axis });
}

// Crop mode

function syncCropFields(rect = cropTool.rect()) {
    const { width, height } = cropTool.bounds();
    cropFields.x.value = rect.x;
    cropFields.y.value = rect.y;
    cropFields.width.value = rect.width;
    cropFields.height.value = rect.height;
    cropFields.x.max = width - 1;
    cropFields.y.max = height - 1;
    cropFields.width.max = width;
    cropFields.height.max = height;
    view.render();
}

function commitCropField(name) {
    const value = Number(cropFields[name].value);
    if (cropFields[name].value.trim() === '' || !Number.isFinite(value)) {
        syncCropFields();
        return;
    }
    cropTool.setField(name, value);
}

function enterCrop() {
    if (frameworkReadOnly || cropping || !pipeline) {
        return;
    }

    closeAllPopovers();
    cropping = true;

    const { width, height } = currentSize();
    cropTool.start(width, height);
    toolbar.hidden = true;
    cropBar.hidden = false;
    view.setTool(cropTool.tool);
    view.setOverlay(cropTool.drawOverlay);
    stage.focus({ preventScroll: true });
}

function exitCrop() {
    if (!cropping) {
        return;
    }
    cropping = false;
    cropBar.hidden = true;
    toolbar.hidden = false;
    view.setTool(null);
    view.setOverlay(null);
    cropButton.focus({ preventScroll: true });
}

function applyCrop() {
    if (!cropping) {
        return;
    }
    const rect = cropTool.rect();
    const covers = cropTool.coversImage();
    exitCrop();
    if (!covers) {
        applyOperation({ type: 'crop', ...rect });
    }
}

// Read-only state, the notice bar and the status bar

function updateControls() {
    frameworkReadOnly = !writable || blocked !== null || loadError !== null || !pipeline;

    for (const element of document.querySelectorAll('[data-edit]')) {
        element.disabled = frameworkReadOnly;
    }
    undoButton.disabled = frameworkReadOnly || !history.canUndo();
    redoButton.disabled = frameworkReadOnly || !history.canRedo();
    saveSettingsButton.hidden = !FORMATS[fileFormat]?.quality || blocked !== null;

    if (frameworkReadOnly) {
        exitCrop();
        resizePanel.close();
        settingsPanel.close();
    }

    updateNotice();
}

function updateNotice() {
    let message = '';
    if (blocked === 'format') {
        message = t('IE_Notice_ReadOnlyFormat', FORMATS[fileFormat]?.label || extensionOf(documentResource));
    } else if (blocked === 'animated') {
        message = t('IE_Notice_Animated');
    } else if (blocked === 'tooLarge') {
        message = t('IE_Notice_TooLarge', MAX_SIDE, MAX_PIXELS / 1_000_000);
    } else if (!writable) {
        message = t('IE_Notice_ReadOnlyDocument');
    }

    noticeText.textContent = message;
    notice.hidden = !message;
}

function updateStatus() {
    if (!pipeline) {
        return;
    }

    const { width, height } = currentSize();
    let dimensions = t('IE_Status_Dimensions', width, height);
    if (width !== sourceWidth || height !== sourceHeight) {
        dimensions += ` ${t('IE_Status_Original', sourceWidth, sourceHeight)}`;
    }
    statusDimensions.textContent = dimensions;

    let format = FORMATS[fileFormat]?.label || '';
    if (detectedFormat && detectedFormat !== fileFormat) {
        format += ` ${t('IE_Status_Detected', FORMATS[detectedFormat].label)}`;
    }
    statusFormat.textContent = format;
    statusSize.textContent = formatBytes(fileSize);

    const kinds = metadataKinds(metadata);
    statusMetadata.textContent = kinds.length > 0 ? t('IE_Status_Metadata', kinds.join(', ')) : '';
    statusMetadata.title = kinds.length > 0 ? t('IE_Status_Metadata_Tooltip') : '';
    statusPointer.textContent = pointer ? t('IE_Status_Pointer', pointer.x, pointer.y) : '';
    updateZoom();
}

function updateZoom() {
    const zoom = view.zoom() * 100;
    const text = `${zoom < 10 ? zoom.toFixed(1) : Math.round(zoom)}%`;
    zoomLabel.textContent = text;
    statusZoom.textContent = pipeline ? text : '';
}

// Loading

function showLoadError(key) {
    loadError = key;
    errorText.textContent = t(key);
    app.hidden = true;
    errorView.hidden = false;
}

function releaseBitmaps() {
    pipeline?.source.close?.();
    displayPipeline?.source.close?.();
    pipeline = null;
    displayPipeline = null;
}

async function loadContent(base64, { keepView = false } = {}) {
    // A change on disk wins over edits not yet saved.
    dirty = false;
    closeAllPopovers();
    exitCrop();
    history.reset();
    releaseBitmaps();
    transparencyCache = { key: null, value: 'none' };

    loadError = null;
    blocked = null;
    originalBase64 = base64 || '';
    originalSize = base64ByteLength(originalBase64);
    fileSize = originalSize;
    fileFormat = formatForExtension(extensionOf(documentResource));
    detectedFormat = null;
    metadata = emptyMetadata();

    try {
        const bytes = await base64ToBytes(originalBase64);
        if (bytes.length === 0) {
            showLoadError('IE_Error_Empty');
            return;
        }

        detectedFormat = detectFormat(bytes);
        if (!detectedFormat) {
            showLoadError('IE_Error_NotImage');
            return;
        }

        metadata = await extractMetadata(detectedFormat, bytes);
        if (metadata.problems.length > 0) {
            console.warn('[Image Editor] Metadata problems:', metadata.problems);
        }

        const decodable = detectedFormat === 'ico' ? extractLargestIcon(bytes) : bytes;
        if (!decodable) {
            showLoadError('IE_Error_Decode');
            return;
        }

        const mime = FORMATS[detectedFormat].mime;
        let source;
        try {
            source = await decodeBitmap(decodable, mime, { colourManaged: false });
        } catch (e) {
            console.warn('[Image Editor] Failed to decode:', e);
            showLoadError('IE_Error_Decode');
            return;
        }

        let display = null;
        if (needsColourManagement(metadata)) {
            try {
                display = await decodeBitmap(decodable, mime, { colourManaged: true });
            } catch {
                // The preview falls back to the unconverted pixels.
            }
        }

        pipeline = createPipeline(source);
        displayPipeline = display ? createPipeline(display) : null;
        sourceWidth = source.width;
        sourceHeight = source.height;

        if (!FORMATS[fileFormat]?.writable) {
            blocked = 'format';
        } else if (isAnimated(detectedFormat, bytes)) {
            blocked = 'animated';
        } else if (sourceWidth > MAX_SIDE || sourceHeight > MAX_SIDE || sourceWidth * sourceHeight > MAX_PIXELS) {
            blocked = 'tooLarge';
        }

        defaultEncoding = { quality: 90, lossless: fileFormat === 'webp' && metadata.webpLossless };
        encoding = { ...defaultEncoding };

        errorView.hidden = true;
        app.hidden = false;
        view.measure();
        showImage({ keepView });
    } catch (e) {
        console.error('[Image Editor] Failed to open:', e);
        releaseBitmaps();
        showLoadError('IE_Error_Decode');
    } finally {
        updateControls();
    }
}

// Saving

function transparencyFor(operations, canvas) {
    const key = JSON.stringify(operations);
    if (transparencyCache.key !== key) {
        transparencyCache = { key, value: transparencyOf(canvas) };
    }
    return transparencyCache.value;
}

async function encodeFile(format, operations, { quality, lossless, background }) {
    const canvas = pipeline.render(operations);
    const adjusted = adjustMetadata(metadata, {
        width: canvas.width,
        height: canvas.height,
        geometryChanged: changesGeometry(operations),
        edited: operations.length > 0,
    });

    const encoded = await encodeCanvas(canvas, format, {
        quality,
        lossless,
        background,
        comments: format === 'gif' ? gifComments(adjusted) : [],
        icc: adjusted.icc,
        resolution: adjusted.resolution,
    });
    const { bytes, dropped } = await writeMetadata(format, encoded, adjusted);

    return { bytes, dropped, canvas, droppedMalformedExif: adjusted.droppedMalformedExif };
}

async function save() {
    if (!dirty || frameworkReadOnly || !pipeline) {
        return;
    }

    const operations = currentOperations();
    const settingsChanged = encoding.quality !== defaultEncoding.quality || encoding.lossless !== defaultEncoding.lossless;

    try {
        // Nothing to encode: the original bytes are saved, so the file ends byte-identical to how it started.
        if (operations.length === 0 && !settingsChanged) {
            await client.document.save(originalBase64);
            dirty = false;
            fileSize = originalSize;
            updateStatus();
            return;
        }

        const { bytes, dropped, canvas, droppedMalformedExif } = await encodeFile(fileFormat, operations, {
            ...encoding,
            background: '#ffffff',
        });

        await client.document.save(await bytesToBase64(bytes));
        dirty = false;
        fileSize = bytes.length;
        updateStatus();

        if (dropped.length > 0) {
            console.warn(`[Image Editor] Metadata this format cannot hold was not saved: ${dropped.join(', ')}`);
        }
        if (fileFormat === 'jpeg' && !warnedJpegTransparency && transparencyFor(operations, canvas) !== 'none') {
            warnedJpegTransparency = true;
            await client.dialog.showNotification('warning', t('IE_Toast_JpegTransparency'));
        }
        if (droppedMalformedExif && !warnedMalformedExif) {
            warnedMalformedExif = true;
            await client.dialog.showNotification('warning', t('IE_Toast_MalformedExif'));
        }
    } catch (e) {
        console.error('[Image Editor] Failed to save:', e);
        await client.dialog.showNotification('error', t('IE_Toast_SaveFailed', fileName()));
    }
}

function fileName() {
    return documentResource.slice(documentResource.lastIndexOf('/') + 1).replace(/^project:/, '');
}

// Wiring

undoButton.addEventListener('click', undo);
redoButton.addEventListener('click', redo);
cropButton.addEventListener('click', enterCrop);
resizeButton.addEventListener('click', () => resizePanel.toggle());
rotateLeftButton.addEventListener('click', () => rotate(270));
rotateRightButton.addEventListener('click', () => rotate(90));
flipHorizontalButton.addEventListener('click', () => flip('horizontal'));
flipVerticalButton.addEventListener('click', () => flip('vertical'));
zoomActualButton.addEventListener('click', () => view.actualSize());
zoomOutButton.addEventListener('click', () => view.zoomOut());
zoomInButton.addEventListener('click', () => view.zoomIn());
saveSettingsButton.addEventListener('click', () => settingsPanel.toggle());

for (const name of Object.keys(cropFields)) {
    cropFields[name].addEventListener('change', () => commitCropField(name));
}
cropApplyButton.addEventListener('click', applyCrop);
cropCancelButton.addEventListener('click', exitCrop);

document.addEventListener('keydown', (event) => {
    if (event.defaultPrevented) {
        return;
    }

    const ctrl = event.ctrlKey || event.metaKey;
    const key = event.key.toLowerCase();

    if (cropping) {
        if (event.key === 'Enter' && event.target.tagName !== 'BUTTON') {
            const field = Object.keys(cropFields).find((name) => cropFields[name] === event.target);
            if (field) {
                commitCropField(field);
            }
            applyCrop();
            event.preventDefault();
            return;
        }
        if (event.key === 'Escape') {
            exitCrop();
            event.preventDefault();
            return;
        }
    }

    if (isTextTarget(event.target) || anyPopoverOpen() || !pipeline) {
        return;
    }

    if (ctrl && key === '0') {
        view.fit();
    } else if (ctrl && key === '1') {
        view.actualSize();
    } else if (ctrl && (key === '=' || key === '+')) {
        view.zoomIn();
    } else if (ctrl && (key === '-' || key === '_')) {
        view.zoomOut();
    } else if (cropping) {
        return;
    } else if (ctrl && !event.shiftKey && key === 'z') {
        undo();
    } else if (ctrl && (key === 'y' || (event.shiftKey && key === 'z'))) {
        redo();
    } else if (!ctrl && !event.altKey && key === 'c') {
        enterCrop();
    } else if (!ctrl && !event.altKey && key === 'r') {
        rotate(event.shiftKey ? 270 : 90);
    } else {
        return;
    }

    event.preventDefault();
});

client.viewState.onChanged((viewState) => {
    if (viewState.writable) {
        writable = viewState.writable === 'Writable';
        updateControls();
    }
});

// The host reports this view's geometry as it arranges it. The canvas backing store is sized from it.
client.view?.onChanged?.(() => view.measure());

client.localization.onLanguageChanged(async (locale) => {
    try {
        await client.localization.loadStrings(locale);
        if (loadError) {
            errorText.textContent = t(loadError);
        }
        updateNotice();
        updateStatus();
    } catch (e) {
        console.warn('[Image Editor] Failed to reload localization:', e);
    }
});

try {
    await client.initializeDocument({
        onContent: async (content, documentMetadata) => {
            documentResource = documentMetadata?.resourceKey || '';
            try {
                await loadContent(content);
            } catch (e) {
                console.error('[Image Editor] Failed to read the file:', e);
                showLoadError('IE_Error_Decode');
                updateControls();
            }
        },

        onRequestSave: save,

        onExternalChange: async (args) => {
            try {
                const { content } = await client.document.load();
                await loadContent(content, { keepView: Boolean(args?.preserveViewState) });
            } catch (e) {
                console.error('[Image Editor] Failed to reload content:', e);
            }
            client.document.notifyContentLoaded(ContentLoadedReason.ExternalReload);
        },

        onRequestState: () => (pipeline ? JSON.stringify(view.getState()) : null),

        onRestoreState: (stateJson) => {
            try {
                view.setState(JSON.parse(stateJson));
            } catch {
                // Ignore malformed state. The image stays fitted.
            }
        },
    });
} catch (e) {
    console.error('[Image Editor] Failed to initialize:', e);
}
