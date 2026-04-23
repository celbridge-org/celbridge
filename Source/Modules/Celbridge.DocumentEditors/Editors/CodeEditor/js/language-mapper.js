// Maps a file's extension to a Monaco language id. The extension-to-language
// dictionary ships alongside the package as code-editor-types.json and is
// loaded once at startup.

let languageMap = null;

async function loadLanguageMap() {
    if (languageMap) {
        return languageMap;
    }

    try {
        const url = new URL('./code-editor-types.json', import.meta.url).href;
        const response = await fetch(url);
        if (response.ok) {
            languageMap = await response.json();
        } else {
            languageMap = {};
        }
    } catch {
        languageMap = {};
    }

    return languageMap;
}

export async function initializeLanguageMap() {
    await loadLanguageMap();
}

export function getLanguageForFile(fileName) {
    if (!fileName || !languageMap) {
        return 'plaintext';
    }

    const dotIndex = fileName.lastIndexOf('.');
    if (dotIndex < 0) {
        return 'plaintext';
    }

    const extension = fileName.substring(dotIndex).toLowerCase();
    return languageMap[extension] || 'plaintext';
}
