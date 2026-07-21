// Maps a file's extension to a Monaco language id. The host's file type catalog
// supplies the language for each extension and is loaded once at startup.

let languageMap = null;

async function loadLanguageMap() {
    if (languageMap) {
        return languageMap;
    }

    try {
        const response = await fetch('/assets/celbridge-client/file-types.json');
        if (response.ok) {
            const catalog = await response.json();
            languageMap = {};
            for (const [extension, entry] of Object.entries(catalog)) {
                if (entry.language) {
                    languageMap[extension] = entry.language;
                }
            }
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
