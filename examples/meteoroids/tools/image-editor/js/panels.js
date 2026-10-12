// The small panels that open under a toolbar button: resize and save settings. Each is a popover
// that closes on Cancel, on Escape and on a click outside it.

import { t } from '/assets/celbridge-client/localization.js';
import { FORMATS } from './formats.js';

export const MAX_SIDE = 16384;
export const MAX_PIXELS = 100_000_000;

const openPopovers = new Set();

export function closeAllPopovers() {
    for (const popover of [...openPopovers]) {
        popover.close();
    }
}

export function anyPopoverOpen() {
    return openPopovers.size > 0;
}

function createPopover(panel, anchor, { onKeyDown } = {}) {
    function position() {
        const containerRect = panel.parentElement.getBoundingClientRect();
        const anchorRect = anchor.getBoundingClientRect();
        const width = panel.offsetWidth;
        const left = Math.min(containerRect.width - width - 8, Math.max(8, anchorRect.left - containerRect.left));
        panel.style.left = `${Math.max(8, left)}px`;
        panel.style.top = `${anchorRect.bottom - containerRect.top + 4}px`;
    }

    const outside = (event) => {
        if (!panel.contains(event.target) && !anchor.contains(event.target)) {
            popover.close();
        }
    };

    const popover = {
        open() {
            closeAllPopovers();
            panel.hidden = false;
            anchor.setAttribute('aria-expanded', 'true');
            position();
            openPopovers.add(popover);
            document.addEventListener('pointerdown', outside, true);
        },
        close() {
            if (panel.hidden) {
                return;
            }
            panel.hidden = true;
            anchor.setAttribute('aria-expanded', 'false');
            openPopovers.delete(popover);
            document.removeEventListener('pointerdown', outside, true);
        },
        isOpen: () => !panel.hidden,
        position,
    };

    panel.addEventListener('keydown', (event) => {
        if (event.key === 'Escape') {
            event.preventDefault();
            event.stopPropagation();
            popover.close();
            anchor.focus();
            return;
        }
        onKeyDown?.(event);
    });

    return popover;
}

function parseWholeNumber(text) {
    const trimmed = String(text).trim();
    if (!/^\d+$/.test(trimmed)) {
        return null;
    }
    return Number(trimmed);
}

export function formatBytes(bytes) {
    if (bytes < 1024) {
        return t('IE_Size_Bytes', bytes);
    }
    if (bytes < 1024 * 1024) {
        return t('IE_Size_KB', (bytes / 1024).toFixed(1));
    }
    if (bytes < 1024 * 1024 * 1024) {
        return t('IE_Size_MB', (bytes / (1024 * 1024)).toFixed(1));
    }
    return t('IE_Size_GB', (bytes / (1024 * 1024 * 1024)).toFixed(2));
}

export function createResizePanel({ panel, anchor, getSize, onApply }) {
    const widthInput = panel.querySelector('#resize-width');
    const heightInput = panel.querySelector('#resize-height');
    const percentInput = panel.querySelector('#resize-percent');
    const lockButton = panel.querySelector('#resize-lock');
    const resampling = panel.querySelector('#resize-resampling');
    const summary = panel.querySelector('#resize-summary');
    const warning = panel.querySelector('#resize-warning');
    const applyButton = panel.querySelector('#resize-apply');
    const cancelButton = panel.querySelector('#resize-cancel');

    let original = { width: 1, height: 1 };

    const locked = () => lockButton.getAttribute('aria-pressed') === 'true';

    const popover = createPopover(panel, anchor, {
        onKeyDown: (event) => {
            if (event.key === 'Enter' && event.target.tagName !== 'BUTTON') {
                event.preventDefault();
                apply();
            }
        },
    });

    function read() {
        return { width: parseWholeNumber(widthInput.value), height: parseWholeNumber(heightInput.value) };
    }

    function validate() {
        const { width, height } = read();
        let message = '';
        if (width === null || height === null || width < 1 || height < 1 || width > MAX_SIDE || height > MAX_SIDE) {
            message = t('IE_Warning_ResizeRange', MAX_SIDE);
        } else if (width * height > MAX_PIXELS) {
            message = t('IE_Warning_ResizePixels', MAX_PIXELS / 1_000_000);
        }

        warning.textContent = message;
        warning.hidden = !message;
        applyButton.disabled = Boolean(message);

        summary.textContent = message ? '' : t('IE_Resize_Summary', width, height, formatBytes(width * height * 4));
        return message ? null : { width, height };
    }

    widthInput.addEventListener('input', () => {
        const width = parseWholeNumber(widthInput.value);
        if (width !== null && width > 0) {
            if (locked()) {
                heightInput.value = Math.max(1, Math.round((width * original.height) / original.width));
            }
            percentInput.value = +((width / original.width) * 100).toFixed(2);
        }
        validate();
    });

    heightInput.addEventListener('input', () => {
        const height = parseWholeNumber(heightInput.value);
        if (height !== null && height > 0) {
            if (locked()) {
                widthInput.value = Math.max(1, Math.round((height * original.width) / original.height));
            }
            percentInput.value = +((height / original.height) * 100).toFixed(2);
        }
        validate();
    });

    percentInput.addEventListener('input', () => {
        const percent = Number(percentInput.value);
        if (percentInput.value.trim() !== '' && Number.isFinite(percent) && percent > 0) {
            widthInput.value = Math.max(1, Math.round((original.width * percent) / 100));
            heightInput.value = Math.max(1, Math.round((original.height * percent) / 100));
        }
        validate();
    });

    lockButton.addEventListener('click', () => {
        const next = !locked();
        lockButton.setAttribute('aria-pressed', String(next));
        if (next) {
            widthInput.dispatchEvent(new Event('input'));
        }
    });

    function apply() {
        const size = validate();
        if (!size) {
            return;
        }
        popover.close();
        anchor.focus();
        if (size.width !== original.width || size.height !== original.height) {
            onApply({ type: 'resize', width: size.width, height: size.height, resampling: resampling.value });
        }
    }

    applyButton.addEventListener('click', apply);
    cancelButton.addEventListener('click', () => {
        popover.close();
        anchor.focus();
    });

    return {
        open() {
            original = getSize();
            widthInput.value = original.width;
            heightInput.value = original.height;
            percentInput.value = 100;
            lockButton.setAttribute('aria-pressed', 'true');
            validate();
            popover.open();
            widthInput.focus();
            widthInput.select();
        },
        toggle() {
            if (popover.isOpen()) {
                popover.close();
            } else {
                this.open();
            }
        },
        close: () => popover.close(),
        isOpen: () => popover.isOpen(),
    };
}

// The options the file itself is saved with. Changing one is a change to the document.
export function createSettingsPanel({ panel, anchor, getEncoding, getFormat, onChange }) {
    const qualityField = panel.querySelector('#settings-quality-field');
    const quality = panel.querySelector('#settings-quality');
    const qualityValue = panel.querySelector('#settings-quality-value');
    const losslessField = panel.querySelector('#settings-lossless-field');
    const lossless = panel.querySelector('#settings-lossless');
    const closeButton = panel.querySelector('#settings-close');

    const popover = createPopover(panel, anchor);

    function sync() {
        const encoding = getEncoding();
        const format = FORMATS[getFormat()];
        quality.value = encoding.quality;
        qualityValue.textContent = encoding.quality;
        lossless.checked = encoding.lossless;
        losslessField.hidden = !format.lossless;
        qualityField.hidden = format.lossless && encoding.lossless;
    }

    quality.addEventListener('input', () => {
        qualityValue.textContent = quality.value;
    });
    // Reported on release rather than per step, so dragging the slider is one change.
    quality.addEventListener('change', () => onChange({ quality: Number(quality.value) }));
    lossless.addEventListener('change', () => {
        onChange({ lossless: lossless.checked });
        sync();
    });
    closeButton.addEventListener('click', () => {
        popover.close();
        anchor.focus();
    });

    return {
        toggle() {
            if (popover.isOpen()) {
                popover.close();
            } else {
                sync();
                popover.open();
                quality.focus();
            }
        },
        close: () => popover.close(),
        sync,
    };
}
