// Test stub for the document-api module. Mirrors only the symbols
// editor-controller.js and preview-module.js consume.

export const ContentLoadedReason = Object.freeze({
    Initial: 'Initial',
    ExternalReload: 'ExternalReload'
});

export function projectUrl(resourceKey) {
    const key = resourceKey || '';
    const path = key.startsWith('project:')
        ? key.substring('project:'.length)
        : key;
    return `/project/${path}`;
}
