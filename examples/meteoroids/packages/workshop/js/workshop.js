// Workshop mockup: an interactive preview of the asset browser and package details page described in
// "Workshop and Package Redesign". The catalog is hardcoded from the packages already vendored in this
// project's tools/ folder — there is no real catalog, download or install behind any of this. Install,
// Update and Uninstall only flip a flag kept in this document's own state; the links to Celbridge.org,
// Learn and Forum are shown disabled, because the real Workshop opens each as its own WebView document,
// which this mockup does not implement.

import celbridge from '/assets/celbridge-client/celbridge.js';
import { ContentLoadedReason } from '/assets/celbridge-client/api/document-api.js';
import { t } from '/assets/celbridge-client/localization.js';

const client = celbridge;

const STATE_VERSION = 1;

// Catalog entries are invented for the mockup — titles and short descriptions are drawn from what each
// tools/ package actually does, but author, version, license and update date are placeholder values the
// real catalog would read from package.toml and the package's own hosted files.
const CATALOG = [
    {
        id: 'emoji-sprite',
        title: 'Emoji Sprite',
        author: 'Celbridge Team',
        version: '1.0.0',
        license: 'Apache-2.0',
        updated: 'Aug 12, 2026',
        color: '#c2650a',
        description: 'Generates PNG sprite sheets from emoji artwork, for games that want colorful icons with no art budget.',
        readme: [
            'Emoji Sprite turns a list of emoji characters into a folder of individually named PNG sprites, ready for a game to load.',
            'Pick a resolution and an output folder, and the generator fetches the matching Noto Color Emoji artwork and rasterises it. Generated sprites carry the Apache-2.0 license of the Noto artwork they come from.'
        ]
    },
    {
        id: 'image-editor',
        title: 'Image Editor',
        author: 'Celbridge Team',
        version: '1.0.0',
        license: 'MIT',
        updated: 'Jul 30, 2026',
        color: '#2563a8',
        description: 'Crops, resizes and converts PNG, JPEG, WebP, GIF, BMP and ICO images without leaving the project.',
        readme: [
            'A general-purpose image editor for the formats a game project collects: crop to a new canvas, resize, and convert between PNG, JPEG, WebP, GIF, BMP and ICO.',
            'Image metadata such as EXIF orientation is read and respected, so edited images keep the orientation they were captured with.'
        ]
    },
    {
        id: 'sound-fx',
        title: 'Sound FX',
        author: 'Celbridge Team',
        version: '1.0.0',
        license: 'MIT AND Unlicense',
        updated: 'Aug 21, 2026',
        color: '#6d3fa8',
        description: 'Designs retro sound effects and exports them as WAV, powered by the jsfxr synthesizer.',
        readme: [
            'Sound FX is a waveform synthesizer for short game sounds: jumps, hits, explosions, coins and more.',
            'Presets give a starting point, and Mutate and Randomize explore variations from there. Every sound exports as a WAV file in the project. Sound FX bundles jsfxr, under its own Unlicense.'
        ]
    }
];

function defaultState() {
    return {
        version: STATE_VERSION,
        search: '',
        installedOnly: false,
        view: 'browse',
        selectedId: null,
        // The packages above really are vendored into this project's tools/ folder, so showing them as
        // already installed matches what is actually on disk — the mockup starts from a true state.
        installed: Object.fromEntries(CATALOG.map(pkg => [pkg.id, true]))
    };
}

let state = defaultState();

const backBtn = document.getElementById('back-btn');
const searchInput = document.getElementById('search-input');
const installedFilterBtn = document.getElementById('installed-filter');
const noticeEl = document.getElementById('notice');
const noticeTextEl = document.getElementById('notice-text');

const browseView = document.getElementById('browse-view');
const emptyResultsEl = document.getElementById('empty-results');
const assetGridEl = document.getElementById('asset-grid');

const detailsView = document.getElementById('details-view');
const detailsThumbnailEl = document.getElementById('details-thumbnail');
const detailsTitleEl = document.getElementById('details-title');
const detailsBadgeEl = document.getElementById('details-badge');
const detailsAuthorEl = document.getElementById('details-author');
const detailsVersionEl = document.getElementById('details-version');
const detailsLicenseEl = document.getElementById('details-license');
const detailsUpdatedEl = document.getElementById('details-updated');
const detailsDescriptionEl = document.getElementById('details-description');
const detailsReadmeEl = document.getElementById('details-readme');
const installBtn = document.getElementById('install-btn');
const updateBtn = document.getElementById('update-btn');
const uninstallBtn = document.getElementById('uninstall-btn');

function packageById(id) {
    return CATALOG.find(pkg => pkg.id === id) ?? null;
}

function isInstalled(id) {
    return !!state.installed[id];
}

function matchesSearch(pkg, text) {
    if (!text) return true;
    const needle = text.toLowerCase();
    return pkg.title.toLowerCase().includes(needle) || pkg.description.toLowerCase().includes(needle);
}

function visiblePackages() {
    return CATALOG.filter(pkg => (!state.installedOnly || isInstalled(pkg.id)) && matchesSearch(pkg, state.search));
}

// — Rendering —

function render() {
    const onDetails = state.view === 'details' && packageById(state.selectedId);
    backBtn.hidden = !onDetails;
    browseView.hidden = !!onDetails;
    detailsView.hidden = !onDetails;

    if (onDetails) {
        renderDetails(packageById(state.selectedId));
    } else {
        renderGrid();
    }
}

function renderGrid() {
    const packages = visiblePackages();
    emptyResultsEl.hidden = packages.length > 0;
    assetGridEl.replaceChildren(...packages.map(renderTile));
}

function renderTile(pkg) {
    const tile = document.createElement('button');
    tile.type = 'button';
    tile.className = 'tile';
    tile.setAttribute('aria-label', pkg.title);

    const thumbnail = document.createElement('div');
    thumbnail.className = 'tile-thumbnail';
    thumbnail.style.background = pkg.color;
    thumbnail.textContent = pkg.title;
    tile.appendChild(thumbnail);

    const title = document.createElement('p');
    title.className = 'tile-title';
    title.textContent = pkg.title;
    tile.appendChild(title);

    const description = document.createElement('p');
    description.className = 'tile-description';
    description.textContent = pkg.description;
    tile.appendChild(description);

    if (isInstalled(pkg.id)) {
        const badge = document.createElement('span');
        badge.className = 'installed-badge';
        badge.textContent = t('Workshop_InstalledBadge');
        tile.appendChild(badge);
    }

    tile.addEventListener('click', () => openDetails(pkg.id));
    return tile;
}

function renderDetails(pkg) {
    detailsThumbnailEl.style.background = pkg.color;
    detailsThumbnailEl.textContent = pkg.title;
    detailsTitleEl.textContent = pkg.title;
    detailsBadgeEl.hidden = !isInstalled(pkg.id);

    detailsAuthorEl.textContent = t('Workshop_By', pkg.author);
    detailsVersionEl.textContent = t('Workshop_Version', pkg.version);
    detailsLicenseEl.textContent = pkg.license;
    detailsUpdatedEl.textContent = t('Workshop_Updated', pkg.updated);
    detailsDescriptionEl.textContent = pkg.description;

    detailsReadmeEl.replaceChildren(...pkg.readme.map(paragraph => {
        const p = document.createElement('p');
        p.textContent = paragraph;
        return p;
    }));

    const installed = isInstalled(pkg.id);
    installBtn.hidden = installed;
    updateBtn.hidden = !installed;
    uninstallBtn.hidden = !installed;
}

// — Navigation —

function openDetails(id) {
    state.view = 'details';
    state.selectedId = id;
    render();
    save();
}

function closeDetails() {
    state.view = 'browse';
    state.selectedId = null;
    render();
    save();
}

// — Events —

backBtn.addEventListener('click', closeDetails);

searchInput.addEventListener('input', () => {
    state.search = searchInput.value;
    renderGrid();
    save();
});

installedFilterBtn.addEventListener('click', () => {
    state.installedOnly = !state.installedOnly;
    installedFilterBtn.setAttribute('aria-pressed', String(state.installedOnly));
    renderGrid();
    save();
});

installBtn.addEventListener('click', () => {
    if (!state.selectedId) return;
    state.installed[state.selectedId] = true;
    renderDetails(packageById(state.selectedId));
    save();
});

uninstallBtn.addEventListener('click', () => {
    if (!state.selectedId) return;
    state.installed[state.selectedId] = false;
    renderDetails(packageById(state.selectedId));
    save();
});

// — State —

function migrate(data) {
    const fresh = defaultState();
    if (!data || typeof data !== 'object') return fresh;

    const search = typeof data.search === 'string' ? data.search : fresh.search;
    const installedOnly = typeof data.installedOnly === 'boolean' ? data.installedOnly : fresh.installedOnly;

    const installed = { ...fresh.installed };
    if (data.installed && typeof data.installed === 'object') {
        for (const pkg of CATALOG) {
            if (typeof data.installed[pkg.id] === 'boolean') {
                installed[pkg.id] = data.installed[pkg.id];
            }
        }
    }

    const selectedId = packageById(data.selectedId) ? data.selectedId : null;
    const view = data.view === 'details' && selectedId ? 'details' : 'browse';

    return { version: STATE_VERSION, search, installedOnly, view, selectedId, installed };
}

function parseState(content) {
    if (!content) return null;
    try {
        return JSON.parse(content);
    } catch {
        console.warn('[Workshop] Settings were not valid JSON; starting from defaults.');
        return null;
    }
}

function applyState(data) {
    state = migrate(data);
    searchInput.value = state.search;
    installedFilterBtn.setAttribute('aria-pressed', String(state.installedOnly));
    render();
}

function save() {
    client.document.notifyChanged();
}

function showNotice(key) {
    noticeTextEl.textContent = t(key);
    noticeEl.hidden = false;
}

// — Lifecycle —

client.document.onRequestSave(async () => {
    try {
        await client.document.save(JSON.stringify(state));
    } catch (e) {
        console.error('[Workshop] Failed to save Workshop state:', e);
    }
});

client.document.onExternalChange(async () => {
    try {
        const { content } = await client.document.load();
        applyState(parseState(content));
    } catch (e) {
        console.error('[Workshop] Failed to reload Workshop state:', e);
    }
});

client.localization.onLanguageChanged(async (locale) => {
    try {
        await client.localization.loadStrings(locale);
        render();
    } catch (e) {
        console.warn('[Workshop] Failed to reload localization:', e);
    }
});

// Seed the fallback strings before initialize(), so this package's own keys are already defined when
// initialize() applies the host's locale over them — see Emoji Sprite's js file for the full reasoning.
async function seedFallbackStrings() {
    try {
        const response = await fetch('localization/en.json');
        if (!response.ok) return;
        const { setStrings } = await import('/assets/celbridge-client/localization.js');
        setStrings(await response.json());
    } catch (e) {
        console.warn('[Workshop] Failed to seed localization:', e);
    }
}

async function initialize() {
    try {
        await seedFallbackStrings();
        const result = await client.initialize();
        applyState(parseState(result.content));

        document.getElementById('app').hidden = false;
        client.document.notifyContentLoaded(ContentLoadedReason.Initial);
    } catch (e) {
        console.error('[Workshop] Failed to initialize:', e);
        showNotice('Workshop_Error_InitFailed');
    }
}

initialize();
