// Draws a file into the page: an image, a sound, a video, a font specimen or a PDF. Talks to the DOM and the
// localization table only, so the rendering can run outside the app.
//
// The extension sets below must match the file types in fileviewer.editor.toml. A contract test compares the two lists.

import { t } from '/assets/celbridge-client/localization.js';

const IMAGE_EXTENSIONS = new Set([
    '.apng', '.avif', '.bmp', '.gif', '.heic', '.heif', '.ico', '.jfif', '.jpeg', '.jpg', '.jxl', '.png', '.svg',
    '.tif', '.tiff', '.webp'
]);
const AUDIO_EXTENSIONS = new Set(['.aac', '.flac', '.m4a', '.mp3', '.oga', '.ogg', '.opus', '.wav', '.weba']);
const VIDEO_EXTENSIONS = new Set(['.m4v', '.mov', '.mp4', '.webm']);
const FONT_EXTENSIONS = new Set(['.otf', '.ttf', '.woff', '.woff2']);
const PDF_EXTENSIONS = new Set(['.pdf']);

// Font sizes in pixels for the specimen's sample lines.
const SPECIMEN_SIZES = [12, 18, 24, 36, 48, 72];

let fontCount = 0;
let currentFont = null;

function getExtension(fileName) {
    const dotIndex = fileName.lastIndexOf('.');
    if (dotIndex < 0) {
        return '';
    }

    return fileName.substring(dotIndex).toLowerCase();
}

function createMessage() {
    const message = document.createElement('div');
    message.className = 'file-error';
    message.textContent = t('FileViewer_CannotDisplay');

    return message;
}

// Codec support differs by platform and by machine, so a file of a registered type can still fail to decode. A newer
// file may already have replaced the element, so the message replaces the element only while the element is on show.
function replaceOnError(element) {
    element.addEventListener('error', () => {
        if (element.isConnected) {
            element.replaceWith(createMessage());
        }
    });
}

function createMedia(tagName, className, url) {
    const element = document.createElement(tagName);
    element.className = className;
    element.controls = true;
    element.src = url;
    replaceOnError(element);

    return element;
}

function createImage(fileName, url) {
    const element = document.createElement('img');
    element.className = 'file-image';
    element.alt = fileName;
    element.src = url;
    replaceOnError(element);

    return element;
}

function createPdf(fileName, url) {
    // The PDF loads in an iframe. WebKit sends the menu's copy: and the other edit commands to a PDF plugin only when
    // the PDF is the frame's whole document. An embedded PDF gets only its context menu.
    const element = document.createElement('iframe');
    element.className = 'file-pdf';
    element.title = fileName;
    element.src = url;

    return element;
}

function createSpecimen(fileName, family) {
    const specimen = document.createElement('div');
    specimen.className = 'file-font';

    const name = document.createElement('h1');
    name.className = 'file-font-name';
    name.textContent = fileName;
    specimen.appendChild(name);

    for (const size of SPECIMEN_SIZES) {
        const sample = document.createElement('p');
        sample.className = 'file-font-sample';
        sample.style.fontFamily = `"${family}"`;
        sample.style.fontSize = `${size}px`;
        sample.textContent = t('FileViewer_FontSample');
        specimen.appendChild(sample);
    }

    return specimen;
}

// Each font loads under a new family name. A font installed on the machine never shares that name, so the specimen
// always draws the loaded font.
async function renderFont(container, fileName, url) {
    const placeholder = document.createElement('div');
    container.appendChild(placeholder);

    fontCount += 1;
    const face = new FontFace(`celbridge-font-${fontCount}`, `url("${url}")`);

    let loaded = true;
    try {
        await face.load();
    } catch (e) {
        console.error('[FileViewer] Failed to load font:', e);
        loaded = false;
    }

    if (!placeholder.isConnected) {
        return;
    }

    if (!loaded) {
        placeholder.replaceWith(createMessage());
        return;
    }

    if (currentFont) {
        document.fonts.delete(currentFont);
    }
    document.fonts.add(face);
    currentFont = face;

    placeholder.replaceWith(createSpecimen(fileName, face.family));
}

/**
 * Draws the file into the container, replacing the container's previous content. Resolves once the file is on show.
 * A font is on show once the font has loaded.
 * @param {HTMLElement} container
 * @param {string} fileName - the file's name. The extension picks how the file is drawn
 * @param {string} url - where the page loads the file from
 */
export async function renderFile(container, fileName, url) {
    container.replaceChildren();

    const extension = getExtension(fileName);

    if (IMAGE_EXTENSIONS.has(extension)) {
        container.appendChild(createImage(fileName, url));
    } else if (AUDIO_EXTENSIONS.has(extension)) {
        container.appendChild(createMedia('audio', 'file-audio', url));
    } else if (VIDEO_EXTENSIONS.has(extension)) {
        container.appendChild(createMedia('video', 'file-video', url));
    } else if (FONT_EXTENSIONS.has(extension)) {
        await renderFont(container, fileName, url);
    } else if (PDF_EXTENSIONS.has(extension)) {
        container.appendChild(createPdf(fileName, url));
    } else {
        container.appendChild(createMessage());
    }
}
