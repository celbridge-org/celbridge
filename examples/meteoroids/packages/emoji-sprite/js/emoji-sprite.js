// Emoji-To-Sprite utility: rasterises Noto Color Emoji artwork into PNG sprites.
//
// Canvas fillText cannot rasterise a web-loaded colour font in either WebKit or
// Chromium, so the glyphs are fetched as SVG source artwork and drawn with
// drawImage instead. Artwork is fetched at generate time; the PNGs written into
// the project are the durable artifact, so games consuming them need no network.
//
// Work is organised into named sprite sets — each a glyph list, a resolution, and
// a destination folder — so one consumer (a game, say) can be regenerated without
// disturbing another's sprites.

import celbridge from '/assets/celbridge-client/celbridge.js';
import { ContentLoadedReason } from '/assets/celbridge-client/api/document-api.js';
import { t } from '/assets/celbridge-client/localization.js';

const NOTO_SVG_BASE = 'https://raw.githubusercontent.com/googlefonts/noto-emoji/main/2D/svg';

// Sprites are named after the emoji ('grinning-face.png'), not its code points.
// JS has no emoji-name API, so names come from CLDR — the same annotation data
// that gives emoji their standard names. 'annotationsDerived' covers sequences
// built from a base glyph (skin tones, keycaps, flags) and is loaded second so
// its more specific name wins.
const CLDR_SOURCES = [
    {
        url: 'https://cdn.jsdelivr.net/npm/cldr-annotations-modern/annotations/en/annotations.json',
        section: 'annotations'
    },
    {
        url: 'https://cdn.jsdelivr.net/npm/cldr-annotations-derived-modern/annotationsDerived/en/annotations.json',
        section: 'annotationsDerived'
    }
];

const STATE_VERSION = 2;
const DEFAULT_SIZE = 128;

const client = celbridge;

const setSelect = document.getElementById('set-select');
const newBtn = document.getElementById('set-new');
const duplicateBtn = document.getElementById('set-duplicate');
const deleteBtn = document.getElementById('set-delete');
const emptyEl = document.getElementById('empty');
const editorEl = document.getElementById('editor');
const nameInput = document.getElementById('name-input');
const glyphInput = document.getElementById('glyph-input');
const sizeInput = document.getElementById('size-input');
const folderInput = document.getElementById('folder-input');
const folderBrowse = document.getElementById('folder-browse');
const folderList = document.getElementById('folder-list');
const generateBtn = document.getElementById('generate-btn');
const glyphCount = document.getElementById('glyph-count');
const statusEl = document.getElementById('status');
const noticeEl = document.getElementById('notice');
const resultsEl = document.getElementById('results');

let state = { version: STATE_VERSION, selectedId: null, sets: [] };
let deleteArmed = false;
let deleteTimer = null;

// Both messages are held as a key and its arguments rather than as finished text, so a language
// change part-way through a run can re-render whatever is currently on screen.
let status = null;
let notice = null;

// Neutral progress and the run's summary, reading out under the Generate button.
function setStatus(key, ...args) {
    status = key ? { key, args } : null;
    renderMessages();
}

// The warning surface above the fields: input that cannot be generated from, or a run that failed.
function showNotice(key, ...args) {
    notice = key ? { key, args } : null;
    renderMessages();
}

function renderMessages() {
    statusEl.textContent = status ? t(status.key, ...status.args) : '';
    noticeEl.textContent = notice ? t(notice.key, ...notice.args) : '';
    noticeEl.hidden = !notice;
}

// --- Glyph set ---

// Graphemes, not code points: a flag or ZWJ sequence is one glyph the user typed.
// Separators are ignored so "😀 😎, 🤓" and "😀😎🤓" mean the same thing.
function parseGlyphs(text) {
    const segmenter = new Intl.Segmenter('en', { granularity: 'grapheme' });
    const glyphs = [];
    const seen = new Set();
    for (const { segment } of segmenter.segment(text ?? '')) {
        if (/^[\s,]+$/.test(segment)) continue;
        if (seen.has(segment)) continue;
        seen.add(segment);
        glyphs.push(segment);
    }
    return glyphs;
}

// Noto names its source artwork after the code points, lowercase hex, joined with
// '_', with VS16 (U+FE0F) stripped: ⛏️ (U+26CF U+FE0F) is emoji_u26cf. ZWJ (U+200D)
// is kept. This addresses the file upstream; it is not what we name the output.
function glyphToAssetName(glyph) {
    return 'emoji_u' + codePointsOf(glyph).map(cp => cp.toString(16)).join('_');
}

function codePointsOf(glyph) {
    return [...glyph].map(c => c.codePointAt(0)).filter(cp => cp !== 0xfe0f);
}

// --- Naming ---

let cldrNames = null;

async function loadEmojiNames() {
    if (cldrNames) return cldrNames;

    const names = new Map();
    for (const { url, section } of CLDR_SOURCES) {
        const response = await fetch(url);
        if (!response.ok) {
            throw new Error(`CLDR names unavailable (HTTP ${response.status})`);
        }
        const json = await response.json();
        for (const [glyph, entry] of Object.entries(json?.[section]?.annotations ?? {})) {
            // CLDR models 'tts' as a list, though in practice it holds one name.
            const name = Array.isArray(entry?.tts) ? entry.tts[0] : entry?.tts;
            if (typeof name === 'string' && name) names.set(glyph, name);
        }
    }

    if (names.size === 0) {
        throw new Error('CLDR names were empty');
    }
    cldrNames = names;
    return names;
}

// CLDR is inconsistent about whether a key carries VS16, so try both spellings
// before giving up on a glyph.
function lookupName(names, glyph) {
    const bare = String.fromCodePoint(...codePointsOf(glyph));
    for (const candidate of [glyph, bare, bare + '\ufe0f']) {
        const name = names.get(candidate);
        if (name) return name;
    }
    return null;
}

// 'flag: Ireland' -> 'flag-ireland', 'waving hand: light skin tone' ->
// 'waving-hand-light-skin-tone'.
function slugify(name) {
    return name
        .normalize('NFKD')
        .replace(/[\u0300-\u036f]/g, '')
        .toLowerCase()
        .replace(/[^a-z0-9]+/g, '-')
        .replace(/^-+|-+$/g, '');
}

// Distinct glyphs can slug to the same stem ('keycap: #' and 'keycap: *' both
// reduce to 'keycap'), and a silent overwrite would drop a sprite. Fall back to
// the code points, which are unique by construction.
function outputName(glyph, assetName, names, used) {
    const name = names ? lookupName(names, glyph) : null;
    const slug = name ? slugify(name) : '';
    let candidate = slug || assetName;

    if (used.has(candidate)) {
        candidate = `${slug}-${assetName.replace(/^emoji_/, '')}`;
    }
    used.add(candidate);
    return candidate;
}

// --- Rasterisation ---

// Noto's SVGs carry a viewBox but no width/height. An image with no intrinsic
// size draws unreliably across browsers, so give it one before rasterising.
// Skipping this is the difference between a sprite and a silently blank canvas.
function ensureIntrinsicSize(svgText) {
    const doc = new DOMParser().parseFromString(svgText, 'image/svg+xml');
    if (doc.querySelector('parsererror')) {
        throw new Error(t('EmojiSprite_Sprite_InvalidSvg'));
    }

    const root = doc.documentElement;
    if (!root.hasAttribute('width') || !root.hasAttribute('height')) {
        const viewBox = (root.getAttribute('viewBox') || '').trim().split(/[\s,]+/).map(Number);
        const [width, height] = viewBox.length === 4 && viewBox[2] > 0 && viewBox[3] > 0
            ? [viewBox[2], viewBox[3]]
            : [128, 128];
        root.setAttribute('width', String(width));
        root.setAttribute('height', String(height));
    }
    return new XMLSerializer().serializeToString(doc);
}

// A blob URL is same-origin, so the canvas stays untainted and toDataURL works.
async function rasterise(assetName, size) {
    const response = await fetch(`${NOTO_SVG_BASE}/${assetName}.svg`);
    if (!response.ok) {
        if (response.status === 404) {
            throw new Error(t('EmojiSprite_Sprite_NoArtwork', assetName));
        }
        throw new Error(t('EmojiSprite_Sprite_FetchFailed', response.status));
    }

    const svgText = ensureIntrinsicSize(await response.text());
    const url = URL.createObjectURL(new Blob([svgText], { type: 'image/svg+xml' }));
    try {
        const img = new Image();
        img.src = url;
        await img.decode();

        const canvas = document.createElement('canvas');
        canvas.width = canvas.height = size;
        const ctx = canvas.getContext('2d');
        ctx.drawImage(img, 0, 0, size, size);
        return canvas;
    } finally {
        URL.revokeObjectURL(url);
    }
}

// decode() can resolve on artwork that still paints nothing, which would write a
// transparent PNG and look like a rendering bug much later. Catch it here.
function isBlank(canvas) {
    const ctx = canvas.getContext('2d');
    const { data } = ctx.getImageData(0, 0, canvas.width, canvas.height);
    for (let i = 3; i < data.length; i += 4) {
        if (data[i] !== 0) return false;
    }
    return true;
}

function toBase64Png(canvas) {
    return canvas.toDataURL('image/png').split(',')[1];
}

// --- Results ---

// A written sprite is its own tile: the image alone, named by its tooltip, and a click opens the PNG as a
// document. A failed one has no image, so it shows the glyph and says what went wrong.
function addResult(glyph, resource, canvas, error) {
    if (error) {
        const card = document.createElement('div');
        card.className = 'result failed';
        card.title = t('EmojiSprite_Result_FailedTooltip', glyph, error);

        const placeholder = document.createElement('div');
        placeholder.className = 'no-preview';
        placeholder.textContent = glyph;
        card.appendChild(placeholder);

        const message = document.createElement('span');
        message.className = 'name';
        message.textContent = error;
        card.appendChild(message);

        resultsEl.appendChild(card);
        return;
    }

    const fileName = resource.split('/').pop();
    const card = document.createElement('button');
    card.type = 'button';
    card.className = 'result sprite';
    card.title = fileName;
    card.setAttribute('aria-label', fileName);

    const img = document.createElement('img');
    img.src = canvas.toDataURL('image/png');
    img.alt = '';
    card.appendChild(img);

    card.addEventListener('click', async () => {
        try {
            await cel.document.open(resource, '', false, true);
        } catch (e) {
            console.warn(`[Emoji Sprite] Could not open ${resource}:`, e);
        }
    });

    resultsEl.appendChild(card);
}

// --- Folder picker ---

// The field holds a project folder key, so the choices are the project's own folders. It stays an
// editable text field rather than a fixed list: the output folder usually does not exist yet, and
// the run creates it.
async function loadFolderList() {
    try {
        const tree = JSON.parse(await cel.file.getTree('', 12, '', 'folder'));

        const folders = [];
        (function walk(node, prefix) {
            for (const child of node.children ?? []) {
                const key = prefix ? `${prefix}/${child.name}` : child.name;
                folders.push(key);
                walk(child, key);
            }
        })(tree, '');

        folders.sort();
        folderList.replaceChildren(...folders.map(folder => {
            const option = document.createElement('option');
            option.value = folder;
            return option;
        }));
    } catch (e) {
        // Browsing is a convenience; the field still takes a typed folder.
        console.warn('[Emoji Sprite] Could not list the project folders:', e);
    }
}

// --- Sprite sets ---

function newId() {
    return crypto.randomUUID();
}

function currentSet() {
    return state.sets.find(s => s.id === state.selectedId) ?? null;
}

function uniqueName(base) {
    const taken = new Set(state.sets.map(s => s.name));
    if (!taken.has(base)) return base;
    for (let n = 2; ; n++) {
        const candidate = `${base} ${n}`;
        if (!taken.has(candidate)) return candidate;
    }
}

function createSet(seed = {}) {
    return {
        id: newId(),
        name: uniqueName(seed.name || t('EmojiSprite_NewSet_Name')),
        glyphs: seed.glyphs ?? '',
        size: seed.size ?? DEFAULT_SIZE,
        folder: seed.folder ?? ''
    };
}

function selectSet(id) {
    state.selectedId = id;
    disarmDelete();
    renderSetList();
    renderEditor();
    resultsEl.replaceChildren();
    setStatus('');
    showNotice(null);
}

// --- Rendering ---

function renderSetList() {
    setSelect.replaceChildren(
        ...state.sets.map((s) => {
            const option = document.createElement('option');
            option.value = s.id;
            option.textContent = s.name || t('EmojiSprite_Set_Unnamed');
            option.selected = s.id === state.selectedId;
            return option;
        })
    );

    const hasSets = state.sets.length > 0;
    setSelect.disabled = !hasSets;
    duplicateBtn.disabled = !hasSets;
    deleteBtn.disabled = !hasSets;
    generateBtn.disabled = !hasSets;
    emptyEl.hidden = hasSets;
    editorEl.hidden = !hasSets;
}

function renderEditor() {
    const set = currentSet();
    if (!set) return;

    nameInput.value = set.name;
    glyphInput.value = set.glyphs;
    sizeInput.value = set.size;
    folderInput.value = set.folder;
    updateGlyphCount();
    fitGlyphInput();
}

// The emoji field grows to show every glyph rather than scrolling inside the panel. A stretched textarea
// reports its stretched height as scrollHeight, so it is collapsed for the measurement; the panel's
// scroll position is put back because the collapse can clamp it.
function fitGlyphInput() {
    const panel = document.getElementById('panel');
    const scrollTop = panel.scrollTop;
    const { style } = glyphInput;

    style.minHeight = '0px';
    style.height = '0px';
    style.flex = 'none';
    const border = glyphInput.offsetHeight - glyphInput.clientHeight;
    const contentHeight = glyphInput.scrollHeight + border;
    style.minHeight = style.height = style.flex = '';

    style.setProperty('--content-height', `${contentHeight}px`);
    panel.scrollTop = scrollTop;
}

function updateGlyphCount() {
    const count = parseGlyphs(glyphInput.value).length;
    glyphCount.textContent = count === 0 ? ''
        : count === 1 ? t('EmojiSprite_GlyphCount_One')
        : t('EmojiSprite_GlyphCount_Many', count);
}

// --- Generate ---

async function generate() {
    const set = currentSet();
    if (!set) return;

    const glyphs = parseGlyphs(set.glyphs);
    const size = Math.trunc(Number(set.size));
    const folder = String(set.folder).trim().replace(/^\/+|\/+$/g, '');

    if (glyphs.length === 0) {
        showNotice('EmojiSprite_Error_NoGlyphs');
        return;
    }
    if (!Number.isFinite(size) || size < 16 || size > 1024) {
        showNotice('EmojiSprite_Error_Size');
        return;
    }
    if (!folder) {
        showNotice('EmojiSprite_Error_NoFolder');
        return;
    }

    showNotice(null);
    generateBtn.disabled = true;
    resultsEl.replaceChildren();
    setStatus(glyphs.length === 1
        ? 'EmojiSprite_Status_Starting_One'
        : 'EmojiSprite_Status_Starting_Many', glyphs.length);

    // Whatever happens, leave the button usable so a failed run can be retried.
    try {
        await runGeneration(set.name, glyphs, size, folder);
    } catch (e) {
        console.error('[Emoji Sprite] Generation failed:', e);
        setStatus('');
        showNotice('EmojiSprite_Error_Failed', e.message ?? e);
    } finally {
        generateBtn.disabled = false;
    }
}

async function runGeneration(setName, glyphs, size, folder) {
    try {
        // Succeeds if the folder already exists.
        await cel.explorer.createFolder(folder);
    } catch {
        // A genuine failure surfaces on the first write below.
    }

    // Naming is a convenience, so a CLDR outage degrades to code-point names
    // rather than failing the run outright.
    let names = null;
    try {
        names = await loadEmojiNames();
    } catch (e) {
        console.warn('[Emoji Sprite] Falling back to code-point names:', e);
        showNotice('EmojiSprite_Error_NamesUnavailable');
    }

    const used = new Set();
    let written = 0;
    let failed = 0;

    for (const glyph of glyphs) {
        const assetName = glyphToAssetName(glyph);
        const resource = `${folder}/${outputName(glyph, assetName, names, used)}.png`;
        try {
            const canvas = await rasterise(assetName, size);
            if (isBlank(canvas)) {
                throw new Error(t('EmojiSprite_Sprite_Blank'));
            }
            await cel.file.writeBinary(resource, toBase64Png(canvas));
            addResult(glyph, resource, canvas, null);
            written++;
        } catch (e) {
            addResult(glyph, resource, null, e.message ?? String(e));
            failed++;
        }
        setStatus('EmojiSprite_Status_Progress', written + failed, glyphs.length);
    }

    if (failed === 0) {
        if (written === 1) setStatus('EmojiSprite_Status_Done_One', folder);
        else setStatus('EmojiSprite_Status_Done_Many', written, folder);
    } else {
        setStatus('EmojiSprite_Status_DoneWithFailures', written, glyphs.length, folder, failed);
    }

    // The log carries the same summary the user is reading, rather than a second wording of it.
    await cel.app.log(`[Emoji Sprite] ${setName}: ${statusEl.textContent} at ${size}px`);

    if (written > 0) {
        // The run may have created the folder, so it is a choice for next time.
        await loadFolderList();

        try {
            // Don't raise the Explorer panel: this utility may be docked there.
            await cel.explorer.select(folder, false);
        } catch {
            // Revealing the output is a convenience, not part of the job.
        }
    }
}

// --- State ---

// v1 held a single { glyphs, size, folder }. Wrap it as one sprite set, named
// after the package it wrote into so the migrated set is recognisable.
function migrate(data) {
    if (!data || typeof data !== 'object') {
        return { version: STATE_VERSION, selectedId: null, sets: [] };
    }
    if (Array.isArray(data.sets)) {
        const sets = data.sets
            .filter(s => s && typeof s === 'object')
            .map(s => ({
                id: s.id || newId(),
                name: String(s.name ?? t('EmojiSprite_Set_Fallback')),
                glyphs: String(s.glyphs ?? ''),
                size: Number(s.size) || DEFAULT_SIZE,
                folder: String(s.folder ?? '')
            }));
        const selectedId = sets.some(s => s.id === data.selectedId) ? data.selectedId : sets[0]?.id ?? null;
        return { version: STATE_VERSION, selectedId, sets };
    }

    const set = {
        id: newId(),
        name: nameFromFolder(String(data.folder ?? '')),
        glyphs: String(data.glyphs ?? ''),
        size: Number(data.size) || DEFAULT_SIZE,
        folder: String(data.folder ?? '')
    };
    return { version: STATE_VERSION, selectedId: set.id, sets: [set] };
}

function nameFromFolder(folder) {
    const parts = folder.split('/').filter(Boolean);
    const packageIndex = parts.indexOf('packages');
    const name = packageIndex >= 0 ? parts[packageIndex + 1] : parts[parts.length - 1];
    return name || t('EmojiSprite_Set_Fallback');
}

function parseState(content) {
    if (!content) return null;
    try {
        return JSON.parse(content);
    } catch {
        console.warn('[Emoji Sprite] Settings were not valid JSON; starting from defaults.');
        return null;
    }
}

function save() {
    client.document.notifyChanged();
}

// --- Events ---

setSelect.addEventListener('change', () => {
    selectSet(setSelect.value);
    save();
});

newBtn.addEventListener('click', () => {
    const set = createSet();
    state.sets.push(set);
    selectSet(set.id);
    save();
    nameInput.focus();
    nameInput.select();
});

duplicateBtn.addEventListener('click', () => {
    const set = currentSet();
    if (!set) return;
    const copy = createSet({ ...set, name: t('EmojiSprite_Set_Copy', set.name) });
    state.sets.splice(state.sets.indexOf(set) + 1, 0, copy);
    selectSet(copy.id);
    save();
});

// Two clicks to delete: a set can hold a long glyph list, and there is no undo
// for it, so a stray click should not be able to destroy one.
deleteBtn.addEventListener('click', () => {
    const set = currentSet();
    if (!set) return;

    if (!deleteArmed) {
        deleteArmed = true;
        deleteBtn.classList.add('armed');
        setDeleteTooltip('EmojiSprite_Toolbar_DeleteConfirm');
        deleteTimer = setTimeout(disarmDelete, 4000);
        return;
    }

    const index = state.sets.indexOf(set);
    state.sets.splice(index, 1);
    disarmDelete();
    selectSet(state.sets[Math.min(index, state.sets.length - 1)]?.id ?? null);
    save();
});

function disarmDelete() {
    deleteArmed = false;
    clearTimeout(deleteTimer);
    deleteBtn.classList.remove('armed');
    setDeleteTooltip('EmojiSprite_Toolbar_Delete');
}

// A rail button is a fixed-size icon, so the armed state cannot announce itself by swapping the icon
// out for text the way a labelled button could. The colour carries it, and the tooltip says what the
// next click does. data-loc-key moves with it so the host's own localization pass agrees.
function setDeleteTooltip(key) {
    deleteBtn.dataset.locKey = key;
    const text = t(key);
    deleteBtn.title = text;
    deleteBtn.setAttribute('aria-label', text);
}

nameInput.addEventListener('input', () => {
    const set = currentSet();
    if (!set) return;
    set.name = nameInput.value;
    renderSetList();
    save();
});

glyphInput.addEventListener('input', () => {
    const set = currentSet();
    if (!set) return;
    set.glyphs = glyphInput.value;
    updateGlyphCount();
    fitGlyphInput();
    save();
});

sizeInput.addEventListener('input', () => {
    const set = currentSet();
    if (!set) return;
    set.size = Math.trunc(Number(sizeInput.value)) || DEFAULT_SIZE;
    save();
});

folderInput.addEventListener('input', () => {
    const set = currentSet();
    if (!set) return;
    set.folder = folderInput.value;
    save();
});

folderBrowse.addEventListener('click', () => {
    folderInput.focus();
    // Drops the folder list open. Not every engine implements showPicker for a datalist, and the list
    // opens on typing regardless, so a failure here costs nothing.
    try { folderInput.showPicker(); } catch { /* focused is enough */ }
});

generateBtn.addEventListener('click', generate);

// A narrower field wraps the glyphs onto more lines: a resized panel does that, and so does the panel's
// scrollbar appearing. Only width matters; fitting changes the height, which must not refit again. The fit
// waits a frame because resizing an observed element inside its own callback trips the observer's loop error.
let glyphInputWidth = 0;
new ResizeObserver(([entry]) => {
    const width = entry.contentRect.width;
    if (width === glyphInputWidth) return;
    glyphInputWidth = width;
    requestAnimationFrame(fitGlyphInput);
}).observe(glyphInput);

client.document.onRequestSave(async () => {
    try {
        await client.document.save(JSON.stringify(state));
    } catch (e) {
        console.error('[Emoji Sprite] Failed to save sprite sets:', e);
    }
});

client.document.onExternalChange(async () => {
    try {
        const { content } = await client.document.load();
        applyState(parseState(content));
    } catch (e) {
        console.error('[Emoji Sprite] Failed to reload sprite sets:', e);
    }
});

client.localization.onLanguageChanged(async (locale) => {
    try {
        await client.localization.loadStrings(locale);

        // The client re-localizes the static markup itself; these are the strings script owns.
        renderSetList();
        updateGlyphCount();
        renderMessages();
        setDeleteTooltip(deleteArmed ? 'EmojiSprite_Toolbar_DeleteConfirm' : 'EmojiSprite_Toolbar_Delete');
    } catch (e) {
        console.warn('[Emoji Sprite] Failed to reload localization:', e);
    }
});

// Seed the fallback strings before initialize(). The client applies localization to the
// DOM once per setStrings call, and initialize() loads the client's own strings before
// this package's — so that first pass would warn that every EmojiSprite_* key is missing,
// even though the second pass resolves them. Seeding en up front keeps the keys defined
// throughout; initialize() then overwrites them with the host's actual locale.
async function seedFallbackStrings() {
    try {
        const response = await fetch('localization/en.json');
        if (!response.ok) return;
        const { setStrings } = await import('/assets/celbridge-client/localization.js');
        setStrings(await response.json());
    } catch (e) {
        console.warn('[Emoji Sprite] Failed to seed localization:', e);
    }
}

function applyState(data) {
    state = migrate(data);
    renderSetList();
    renderEditor();
}

async function initialize() {
    try {
        await seedFallbackStrings();
        const result = await client.initialize();
        applyState(parseState(result.content));
        await loadFolderList();

        // The host's readiness contract (and the webview devtools surface) wait on
        // this before dispatching against the document.
        client.document.notifyContentLoaded(ContentLoadedReason.Initial);
    } catch (e) {
        console.error('[Emoji Sprite] Failed to initialize:', e);
        showNotice('EmojiSprite_Error_InitFailed');
        generateBtn.disabled = true;
    }
}

initialize();
