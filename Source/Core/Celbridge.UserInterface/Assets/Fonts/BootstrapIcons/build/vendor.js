// vendor.js — Refresh the vendored Bootstrap Icons files that are generated, and check the ones that are
// copied by hand against the same upstream release.
// Run from the build/ directory: npm install && npm run vendor
// Run from the Source folder: npm run vendor:icons

import { readFile, writeFile } from 'fs/promises';
import { existsSync } from 'fs';
import { dirname, join } from 'path';
import { fileURLToPath } from 'url';
import { gunzipSync } from 'zlib';
import woff2 from 'wawoff2';

// The Bootstrap Icons release every vendored file comes from. Bump it with the font, never on its own:
// the keywords describe the icons that release carries.
const upstreamTag = 'v1.12.1';

// The keywords are the `categories` and `tags` each icon declares in the front matter of its
// documentation page, which is what the Bootstrap Icons site searches. That data is not part of the
// npm package, so it is read from the release tarball rather than from node_modules.
const tarballUrl = `https://codeload.github.com/twbs/icons/tar.gz/refs/tags/${upstreamTag}`;
const iconDocsFolder = 'docs/content/icons/';

function findRepositoryRoot(startFolder) {
    let folder = startFolder;
    while (!existsSync(join(folder, 'Celbridge.slnx'))) {
        const parent = dirname(folder);
        if (parent === folder) {
            throw new Error('Could not find the repository root: no Celbridge.slnx above this script.');
        }

        folder = parent;
    }

    return folder;
}

// The vendored files sit in two projects, so they are addressed from the repository root rather than by
// counting folders up from here.
const buildFolder = dirname(fileURLToPath(import.meta.url));
const repositoryRoot = findRepositoryRoot(buildFolder);
const fontFolder = join(repositoryRoot, 'Source', 'Core', 'Celbridge.UserInterface', 'Assets', 'Fonts', 'BootstrapIcons');
const webFolder = join(repositoryRoot, 'Source', 'Core', 'Celbridge.WebHost', 'Web', 'bootstrap-icons');

const glyphMapPath = join(fontFolder, 'icon-glyphs.json');
const keywordMapPath = join(fontFolder, 'icon-keywords.json');
const fontPath = join(fontFolder, 'bootstrap-icons.ttf');
const nativeLicensePath = join(fontFolder, 'LICENSE');
const stylesheetPath = join(webFolder, 'bootstrap-icons.css');
const webFontPath = join(webFolder, 'fonts', 'bootstrap-icons.woff2');
const webLicensePath = join(webFolder, 'fonts', 'BOOTSTRAP-ICONS-LICENSE');

// A tar entry is a 512 byte header followed by its data padded to a 512 byte boundary. Only the name
// and the size are needed here, so the archive is walked directly rather than through a dependency.
function readTarEntries(archive) {
    const entries = [];
    let offset = 0;

    while (offset + 512 <= archive.length) {
        const header = archive.subarray(offset, offset + 512);
        const name = readHeaderString(header, 0, 100);
        if (name === '') {
            // Two zero-filled blocks mark the end of the archive.
            break;
        }

        const size = parseInt(readHeaderString(header, 124, 12).trim() || '0', 8);
        const dataStart = offset + 512;

        entries.push({ name, data: archive.subarray(dataStart, dataStart + size) });

        offset = dataStart + Math.ceil(size / 512) * 512;
    }

    return entries;
}

function readHeaderString(header, start, length) {
    const field = header.subarray(start, start + length);
    const terminator = field.indexOf(0);

    return field.toString('utf8', 0, terminator < 0 ? length : terminator);
}

// The front matter holds one scalar per line and one list item per line, so it is read directly rather
// than through a YAML parser. Anything that is not a `categories` or `tags` list item is skipped.
function readKeywords(markdown) {
    const frontMatter = /^---\r?\n([\s\S]*?)\r?\n---/.exec(markdown);
    if (frontMatter === null) {
        return [];
    }

    const keywords = [];
    let listKey = null;

    for (const line of frontMatter[1].split(/\r?\n/)) {
        const keyLine = /^([A-Za-z_]+):\s*(.*)$/.exec(line);
        if (keyLine !== null) {
            // A key with a value of its own is a scalar, so no list follows it.
            listKey = keyLine[2] === '' ? keyLine[1] : null;
            continue;
        }

        const itemLine = /^\s+-\s+(.+?)\s*$/.exec(line);
        if (itemLine === null) {
            continue;
        }

        if (listKey === 'categories' || listKey === 'tags') {
            keywords.push(itemLine[1].replace(/^["']|["']$/g, ''));
        }
    }

    return keywords;
}

// A keyword the icon name already contains would only ever match alongside the name, so it is dropped:
// the file holds the words that reach an icon the name does not.
function selectSearchKeywords(iconName, keywords) {
    const selected = [];

    for (const keyword of keywords) {
        const keywordText = keyword.toLowerCase();
        if (keywordText === ''
            || iconName.includes(keywordText)
            || selected.includes(keywordText)) {
            continue;
        }

        selected.push(keywordText);
    }

    return selected;
}

const problems = [];

function verify(description, isSatisfied, detail = '') {
    if (isSatisfied) {
        console.log(`  ok      ${description}`);
        return;
    }

    console.log(`  FAILED  ${description}${detail === '' ? '' : ` (${detail})`}`);
    problems.push(description);
}

const response = await fetch(tarballUrl);
if (!response.ok) {
    throw new Error(`Failed to download ${tarballUrl}: ${response.status} ${response.statusText}`);
}
const archive = gunzipSync(Buffer.from(await response.arrayBuffer()));
console.log(`downloaded twbs/icons ${upstreamTag}`);

const releaseFiles = new Map();
const keywordsByDocumentedName = new Map();

for (const entry of readTarEntries(archive)) {
    const folderIndex = entry.name.indexOf(iconDocsFolder);
    if (folderIndex >= 0
        && entry.name.endsWith('.md')) {
        const documentedName = entry.name.substring(folderIndex + iconDocsFolder.length, entry.name.length - '.md'.length);
        keywordsByDocumentedName.set(documentedName, readKeywords(entry.data.toString('utf8')));
        continue;
    }

    // The archive root is named for the release, so entries are keyed by their path below it.
    const separatorIndex = entry.name.indexOf('/');
    if (separatorIndex >= 0) {
        releaseFiles.set(entry.name.substring(separatorIndex + 1), entry.data);
    }
}

console.log(`read front matter for ${keywordsByDocumentedName.size} documented icons`);

// The bundled glyph map decides which icons the file covers, so a documentation set that has moved ahead
// of the bundled font cannot introduce a name the font is unable to draw.
const glyphMap = JSON.parse(await readFile(glyphMapPath, 'utf8'));
const iconNames = Object.keys(glyphMap).sort();

console.log(`\nchecking the vendored files against ${upstreamTag}`);

// The glyph map is the release's own name to codepoint map, reformatted, so it is compared as data rather
// than as bytes.
const releaseGlyphMap = JSON.parse(releaseFiles.get('font/bootstrap-icons.json').toString('utf8'));
const releaseNames = Object.keys(releaseGlyphMap).sort();
const glyphMapMatches = JSON.stringify(iconNames) === JSON.stringify(releaseNames)
    && iconNames.every((iconName) => parseInt(glyphMap[iconName], 16) === releaseGlyphMap[iconName]);
verify(
    'icon-glyphs.json matches the release glyph map',
    glyphMapMatches,
    `${iconNames.length} vendored, ${releaseNames.length} in the release`);

const releaseLicense = releaseFiles.get('LICENSE');
verify('LICENSE is the release licence', releaseLicense.equals(await readFile(nativeLicensePath)));
verify('fonts/BOOTSTRAP-ICONS-LICENSE is the release licence', releaseLicense.equals(await readFile(webLicensePath)));

const releaseWebFont = releaseFiles.get('font/fonts/bootstrap-icons.woff2');
verify('fonts/bootstrap-icons.woff2 is the release font', releaseWebFont.equals(await readFile(webFontPath)));

// The stylesheet is a local fork of the release one, so what is checked is the property that matters: that
// it draws a glyph for every icon the picker offers.
const stylesheet = await readFile(stylesheetPath, 'utf8');
const styledNames = new Set([...stylesheet.matchAll(/\.bi-([a-z0-9-]+)::before/g)].map((match) => match[1]));
const unstyledNames = iconNames.filter((iconName) => !styledNames.has(iconName));
verify(
    'bootstrap-icons.css draws every icon in the glyph map',
    unstyledNames.length === 0,
    `${unstyledNames.length} missing, starting at ${unstyledNames.slice(0, 3).join(', ')}`);

if (problems.length > 0) {
    console.error(`\n${problems.length} vendored file(s) do not match ${upstreamTag}. Refresh them before regenerating the font and the keywords.`);
    process.exit(1);
}

// Upstream publishes no TrueType build, and WinUI can load neither of the web font formats it does
// publish, so the native font is decompressed from the release web font rather than converted by hand.
const nativeFont = Buffer.from(await woff2.decompress(releaseWebFont));
if (nativeFont.readUInt32BE(0) !== 0x00010000) {
    throw new Error('The font decompressed from the release web font is not a TrueType font.');
}
await writeFile(fontPath, nativeFont);
console.log(`\nwrote bootstrap-icons.ttf, ${nativeFont.length} bytes from the release web font`);

const lines = [];
let undocumentedCount = 0;

for (const iconName of iconNames) {
    const documented = keywordsByDocumentedName.get(iconName);
    if (documented === undefined) {
        undocumentedCount++;
        continue;
    }

    const keywords = selectSearchKeywords(iconName, documented);
    if (keywords.length === 0) {
        continue;
    }

    lines.push(`    ${JSON.stringify(iconName)}: ${JSON.stringify(keywords)}`);
}

// The release is recorded alongside the keywords, so a file left behind by a font upgrade is visible in
// the data itself rather than only in whichever pin someone remembered to bump.
const keywordMap = `{\n  "release": ${JSON.stringify(upstreamTag)},\n  "keywords": {\n${lines.join(',\n')}\n  }\n}\n`;
await writeFile(keywordMapPath, keywordMap, 'utf8');

console.log(`\nwrote keywords for ${lines.length} of ${iconNames.length} bundled icons`);
console.log(`${undocumentedCount} bundled icons have no documentation page in ${upstreamTag}`);
console.log('done');
