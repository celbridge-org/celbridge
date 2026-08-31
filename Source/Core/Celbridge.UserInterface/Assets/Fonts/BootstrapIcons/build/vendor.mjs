// vendor.mjs — Generate icon-keywords.json from the Bootstrap Icons documentation source.
// Run from the build/ directory: npm run vendor

import { readFile, writeFile } from 'fs/promises';
import { dirname, resolve } from 'path';
import { fileURLToPath } from 'url';
import { gunzipSync } from 'zlib';

// The Bootstrap Icons release the bundled font and glyph map both come from. Bump it with the font,
// never on its own: the keywords describe the icons that release carries.
const upstreamTag = 'v1.12.1';

// The keywords are the `categories` and `tags` each icon declares in the front matter of its
// documentation page, which is what the Bootstrap Icons site searches. That data is not part of the
// npm package, so it is read from the release tarball rather than from node_modules.
const tarballUrl = `https://codeload.github.com/twbs/icons/tar.gz/refs/tags/${upstreamTag}`;
const iconDocsFolder = 'docs/content/icons/';

const buildDir = dirname(fileURLToPath(import.meta.url));
const glyphMapPath = resolve(buildDir, '..', 'icon-glyphs.json');
const keywordMapPath = resolve(buildDir, '..', 'icon-keywords.json');

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

const response = await fetch(tarballUrl);
if (!response.ok) {
    throw new Error(`Failed to download ${tarballUrl}: ${response.status} ${response.statusText}`);
}
const archive = gunzipSync(Buffer.from(await response.arrayBuffer()));
console.log(`downloaded twbs/icons ${upstreamTag}`);

const keywordsByDocumentedName = new Map();
for (const entry of readTarEntries(archive)) {
    const folderIndex = entry.name.indexOf(iconDocsFolder);
    if (folderIndex < 0
        || !entry.name.endsWith('.md')) {
        continue;
    }

    const documentedName = entry.name.substring(folderIndex + iconDocsFolder.length, entry.name.length - '.md'.length);
    keywordsByDocumentedName.set(documentedName, readKeywords(entry.data.toString('utf8')));
}
console.log(`read front matter for ${keywordsByDocumentedName.size} documented icons`);

// The bundled glyph map decides which icons the file covers, so a documentation set that has moved ahead
// of the bundled font cannot introduce a name the font is unable to draw.
const glyphMap = JSON.parse(await readFile(glyphMapPath, 'utf8'));
const iconNames = Object.keys(glyphMap).sort();

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

    lines.push(`  ${JSON.stringify(iconName)}: ${JSON.stringify(keywords)}`);
}

await writeFile(keywordMapPath, `{\n${lines.join(',\n')}\n}\n`, 'utf8');

console.log(`wrote keywords for ${lines.length} of ${iconNames.length} bundled icons`);
console.log(`${undocumentedCount} bundled icons have no documentation page in ${upstreamTag}`);
console.log('done');
