// File viewer initialization for Celbridge WebView integration.
// Renders an image, audio, video, or PDF file by loading it from the
// project virtual host (https://project.celbridge/{resourceKey}).

import celbridge from 'https://shared.celbridge/celbridge-client/celbridge.js';
import { ContentLoadedReason } from 'https://shared.celbridge/celbridge-client/api/document-api.js';

if (!window.isWebView) {
    console.log('Not running in WebView, skipping client initialization');
}

const client = celbridge;

const PROJECT_HOST = 'https://project.celbridge/';

const IMAGE_EXTENSIONS = new Set(['.jpg', '.jpeg', '.png', '.gif', '.webp', '.svg', '.bmp', '.ico']);
const AUDIO_EXTENSIONS = new Set(['.mp3', '.wav', '.ogg', '.flac', '.m4a']);
const VIDEO_EXTENSIONS = new Set(['.mp4', '.webm', '.avi', '.mov', '.mkv']);
const PDF_EXTENSIONS = new Set(['.pdf']);

function applyTheme(theme) {
    const isDark = theme === 'Dark';
    document.body.className = isDark ? 'theme-dark' : 'theme-light';
}

client.theme.onChanged((theme) => {
    applyTheme(theme);
});

function getExtension(fileName) {
    const dotIndex = fileName.lastIndexOf('.');
    if (dotIndex < 0) {
        return '';
    }
    return fileName.substring(dotIndex).toLowerCase();
}

function buildResourceUrl(resourceKey) {
    // Cache-bust on every load so external changes immediately replace the rendered media.
    const cacheBuster = Date.now();
    return `${PROJECT_HOST}${resourceKey}?t=${cacheBuster}`;
}

function renderFile(metadata) {
    const container = document.getElementById('file-viewer-container');
    container.innerHTML = '';

    const extension = getExtension(metadata.fileName || '');
    const url = buildResourceUrl(metadata.resourceKey);

    let element;

    if (IMAGE_EXTENSIONS.has(extension)) {
        element = document.createElement('img');
        element.className = 'file-image';
        element.alt = metadata.fileName;
        element.src = url;
    } else if (AUDIO_EXTENSIONS.has(extension)) {
        element = document.createElement('audio');
        element.className = 'file-audio';
        element.controls = true;
        element.src = url;
    } else if (VIDEO_EXTENSIONS.has(extension)) {
        element = document.createElement('video');
        element.className = 'file-video';
        element.controls = true;
        element.src = url;
    } else if (PDF_EXTENSIONS.has(extension)) {
        element = document.createElement('embed');
        element.className = 'file-pdf';
        element.type = 'application/pdf';
        element.src = url;
    } else {
        element = document.createElement('div');
        element.className = 'file-error';
        element.textContent = `Unsupported file type: ${extension || metadata.fileName}`;
    }

    container.appendChild(element);
}

async function initializeEditor() {
    try {
        await client.initializeDocument({
            onContent: (_content, metadata) => {
                applyTheme(client.theme.current);
                renderFile(metadata);
            },
            onExternalChange: async () => {
                try {
                    const result = await client.document.load();
                    renderFile(result.metadata);
                } catch (e) {
                    console.error('[FileViewer] Failed to reload content:', e);
                }

                client.document.notifyContentLoaded(ContentLoadedReason.ExternalReload);
            }
        });
    } catch (e) {
        console.error('[FileViewer] Failed to initialize:', e);
    }
}

initializeEditor();
