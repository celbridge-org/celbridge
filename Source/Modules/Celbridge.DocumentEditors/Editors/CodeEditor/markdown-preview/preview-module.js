// Markdown preview module.
// Runs inside the parent Monaco WebView page and renders markdown HTML into
// a same-origin sandboxed iframe. The iframe provides DOM isolation (no CSS
// bleed into Monaco, no script execution) but remains scriptable from the
// parent via contentDocument because of allow-same-origin.

import { marked, markedHighlight, hljs } from './lib/marked.esm.js';

let iframeElement = null;
let callbacks = null;
let documentBasePath = '';
let currentContent = '';
let previewContentElement = null;
let previewContainerElement = null;
let suppressScrollSync = false;
let pendingScrollPercentage = null;
let pendingScrollSourceLine = null;
let scrollResizeObserver = null;

// Total number of source lines in the markdown currently rendered. Populated
// by annotateTokensWithSourceLines; used by the bidirectional scroll sync to
// interpolate scroll positions past the last source-mapped block (where the
// preview's rendered blocks stop but the source still has trailing lines,
// and vice versa). Without this, scrolling past the last block produced a
// "dead zone" where one side stopped tracking the other.
let totalSourceLines = 0;

configureMarked();

function configureMarked() {
    marked.setOptions({
        gfm: true,
        breaks: false,
        pedantic: false,
        silent: true
    });

    marked.use(markedHighlight({
        langPrefix: 'hljs language-',
        highlight(code, lang) {
            if (lang && hljs.getLanguage(lang)) {
                try {
                    return hljs.highlight(code, { language: lang }).value;
                } catch (err) {
                    console.warn('Highlight error for language:', lang, err);
                }
            }
            try {
                return hljs.highlightAuto(code).value;
            } catch (err) {
                console.warn('Highlight auto-detect error:', err);
                return code;
            }
        }
    }));

    const renderer = {
        heading(token) {
            const text = this.parser.parseInline(token.tokens);
            const slug = token.text
                .toLowerCase()
                .trim()
                .replace(/\s+/g, '-')
                .replace(/[^\w\-]/g, '');
            return `<h${token.depth} id="${slug}"${sourceAttr(token)}>${text}</h${token.depth}>\n`;
        },

        paragraph(token) {
            const text = this.parser.parseInline(token.tokens);
            return `<p${sourceAttr(token)}>${text}</p>\n`;
        },

        blockquote(token) {
            const body = this.parser.parse(token.tokens);
            return `<blockquote${sourceAttr(token)}>\n${body}</blockquote>\n`;
        },

        code(token) {
            const lang = (token.lang || '').match(/\S*/)?.[0] ?? '';
            const escaped = token.escaped
                ? token.text
                : token.text
                    .replace(/&/g, '&amp;')
                    .replace(/</g, '&lt;')
                    .replace(/>/g, '&gt;');
            const langClass = lang ? ` class="language-${lang}"` : '';
            return `<pre${sourceAttr(token)}><code${langClass}>${escaped}\n</code></pre>\n`;
        },

        hr(token) {
            return `<hr${sourceAttr(token)}>\n`;
        },

        list(token) {
            const tag = token.ordered ? 'ol' : 'ul';
            const startAttr = token.ordered && token.start !== 1 ? ` start="${token.start}"` : '';
            const body = token.items.map((item) => this.listitem(item)).join('');
            return `<${tag}${startAttr}${sourceAttr(token)}>\n${body}</${tag}>\n`;
        },

        table(token) {
            let header = '';
            for (let i = 0; i < token.header.length; i++) {
                const cell = token.header[i];
                const alignAttr = token.align[i] ? ` align="${token.align[i]}"` : '';
                const cellText = this.parser.parseInline(cell.tokens);
                header += `<th${alignAttr}>${cellText}</th>`;
            }

            let body = '';
            for (const row of token.rows) {
                let rowHtml = '';
                for (let i = 0; i < row.length; i++) {
                    const cell = row[i];
                    const alignAttr = token.align[i] ? ` align="${token.align[i]}"` : '';
                    const cellText = this.parser.parseInline(cell.tokens);
                    rowHtml += `<td${alignAttr}>${cellText}</td>`;
                }
                body += `<tr>${rowHtml}</tr>\n`;
            }

            return `<table${sourceAttr(token)}>\n<thead>\n<tr>${header}</tr>\n</thead>\n<tbody>\n${body}</tbody>\n</table>\n`;
        },

        listitem(token) {
            try {
                let itemContent = '';
                if (token.tokens && Array.isArray(token.tokens)) {
                    itemContent = this.parser.parse(token.tokens);
                } else if (token.text) {
                    itemContent = token.text;
                }
                itemContent = itemContent.trim();

                if (token.task) {
                    const checkbox = token.checked
                        ? '<input type="checkbox" checked disabled>'
                        : '<input type="checkbox" disabled>';
                    return `<li class="task-list-item">${checkbox} ${itemContent}</li>\n`;
                }
                return `<li>${itemContent}</li>\n`;
            } catch (error) {
                console.warn('Error rendering list item:', error);
                return `<li>${token.text || token.raw || ''}</li>\n`;
            }
        },

        image(token) {
            try {
                let src = token.href || '';
                const alt = token.text || '';
                const title = token.title || '';

                if (src && !src.startsWith('http://') && !src.startsWith('https://') && !src.startsWith('data:')) {
                    const resolvedPath = resolveRelativePath(src);
                    src = `https://project.celbridge/${resolvedPath}`;
                }

                const titleAttr = title ? ` title="${title}"` : '';
                return `<img src="${src}" alt="${alt}"${titleAttr}>`;
            } catch (error) {
                console.warn('Error rendering image:', error);
                return `<span style="color: var(--hl-keyword);">[Image error]</span>`;
            }
        },

        link(token) {
            try {
                let href = token.href || '';
                let text = '';
                if (token.tokens && Array.isArray(token.tokens)) {
                    text = this.parser.parseInline(token.tokens);
                } else {
                    text = token.text || href;
                }
                const title = token.title || '';

                const isLocalLink = href &&
                    !href.startsWith('http://') &&
                    !href.startsWith('https://') &&
                    !href.startsWith('mailto:') &&
                    !href.startsWith('#');

                if (isLocalLink) {
                    const titleAttr = title ? ` title="${title}"` : '';
                    return `<a href="${href}" data-local-resource="true"${titleAttr}>${text}</a>`;
                }

                const titleAttr = title ? ` title="${title}"` : '';
                return `<a href="${href}"${titleAttr}>${text}</a>`;
            } catch (error) {
                console.warn('Error rendering link:', error);
                return `<span style="color: var(--hl-keyword);">[Link error]</span>`;
            }
        }
    };

    marked.use({ renderer });
}

/**
 * Attribute string used by the block-level renderer hooks to emit the
 * `data-source-line="N"` attribute. Returns empty string when the token
 * has no source line (e.g. inline-only tokens).
 */
function sourceAttr(token) {
    const line = token.sourceLine;
    if (typeof line !== 'number' || line < 1) {
        return '';
    }
    return ` data-source-line="${line}"`;
}

/**
 * Walks the top-level token list and annotates each block token with the
 * 1-based source line of its first character in the original markdown source.
 * marked tokens carry a `raw` property (the original text), so cumulative
 * offsets over `raw` lengths give each token its position in the source.
 * Annotation runs before parse so renderer hooks can read `token.sourceLine`.
 */
function annotateTokensWithSourceLines(tokens, source) {
    const lineStarts = [0];
    for (let i = 0; i < source.length; i++) {
        if (source.charCodeAt(i) === 10) {
            lineStarts.push(i + 1);
        }
    }
    // Count trailing-newline-terminated vs. un-terminated last lines the same
    // way Monaco does: a document with no final newline still has that last
    // line as a line.
    totalSourceLines = lineStarts.length + (source.length > 0 && source.charCodeAt(source.length - 1) !== 10 ? 0 : -1);
    if (totalSourceLines < 1) {
        totalSourceLines = 1;
    }

    const lineAtOffset = (offset) => {
        let lo = 0;
        let hi = lineStarts.length;
        while (lo < hi) {
            const mid = (lo + hi) >>> 1;
            if (lineStarts[mid] <= offset) {
                lo = mid + 1;
            } else {
                hi = mid;
            }
        }
        return lo;
    };

    let charOffset = 0;
    for (const token of tokens) {
        if (typeof token.raw !== 'string') {
            continue;
        }
        token.sourceLine = lineAtOffset(charOffset);
        charOffset += token.raw.length;

        // Propagate into list items so each `<li>` can also carry a source line.
        // List items carry their own `raw` inside the list's total raw length.
        if (token.type === 'list' && Array.isArray(token.items)) {
            let itemOffset = charOffset - token.raw.length;
            for (const item of token.items) {
                if (typeof item.raw === 'string') {
                    item.sourceLine = lineAtOffset(itemOffset);
                    itemOffset += item.raw.length;
                }
            }
        }
    }
}

function resolveRelativePath(relativePath) {
    if (!relativePath) return '';

    if (relativePath.startsWith('http://') ||
        relativePath.startsWith('https://') ||
        relativePath.startsWith('data:') ||
        relativePath.startsWith('/')) {
        return relativePath;
    }

    if (documentBasePath) {
        return documentBasePath + relativePath;
    }
    return relativePath;
}

/**
 * Initializes the preview module.
 * Loads this module's iframe shell (iframe.html) into the provided iframe element,
 * wires up scroll/click listeners, and stores the host callbacks.
 * @param {HTMLIFrameElement} iframe - The sandboxed iframe that hosts the preview DOM.
 * @param {Object} handlers - Callback handlers.
 * @param {Function} handlers.onLinkClicked - Called with an href when any link is clicked. The host resolves whether it is a local resource or external URL.
 * @param {Function} handlers.onSyncToEditor - Called with a scroll percentage (0-1) when the preview scrolls or is clicked.
 * @returns {Promise<void>} - Resolves once the iframe document is ready.
 */
export async function initialize(iframe, handlers) {
    iframeElement = iframe;
    callbacks = handlers || {};

    await loadIframeShell(iframe);

    const iframeDocument = iframe.contentDocument;
    previewContentElement = iframeDocument.getElementById('preview-content');
    previewContainerElement = iframeDocument.getElementById('preview-container');

    if (!previewContentElement || !previewContainerElement) {
        throw new Error('Preview iframe is missing #preview-content or #preview-container elements');
    }

    attachScrollListener();
    attachClickToSyncListener();
}

function loadIframeShell(iframe) {
    // Resolve iframe.html relative to this module's URL so the host doesn't need
    // to know where the preview's HTML lives.
    const shellUrl = new URL('./iframe.html', import.meta.url).href;

    return new Promise((resolve) => {
        if (iframe.contentDocument &&
            iframe.contentDocument.readyState === 'complete' &&
            iframe.contentDocument.getElementById('preview-content')) {
            resolve();
            return;
        }

        iframe.addEventListener('load', () => resolve(), { once: true });
        iframe.src = shellUrl;
    });
}

/**
 * Sets the base path used to resolve relative resources (images, links) in the markdown.
 */
export function setBasePath(basePath) {
    let normalized = basePath || '';
    if (normalized && !normalized.endsWith('/')) {
        normalized += '/';
    }
    documentBasePath = normalized;
}

/**
 * Renders the given markdown content into the preview iframe.
 * Preserves the preview's scroll position when the content changes so edits
 * don't jump the view back to the top.
 */
export function render(markdown) {
    if (!previewContentElement) {
        return;
    }

    currentContent = markdown ?? '';
    const savedScrollTop = previewContainerElement ? previewContainerElement.scrollTop : 0;

    if (!markdown || markdown.trim() === '') {
        previewContentElement.innerHTML = '';
        return;
    }

    try {
        const tokens = marked.lexer(markdown);
        annotateTokensWithSourceLines(tokens, markdown);
        const html = marked.parser(tokens);
        previewContentElement.innerHTML = html;
        attachLinkHandlers();
    } catch (error) {
        console.error('Error rendering markdown:', error);
        try {
            const escaped = markdown
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;');
            previewContentElement.innerHTML = `
                <div class="markdown-error">
                    <p><strong>Preview error:</strong> ${error.message}</p>
                    <p>Showing raw markdown:</p>
                </div>
                <pre style="white-space: pre-wrap; word-wrap: break-word;">${escaped}</pre>
            `;
        } catch (fallbackError) {
            previewContentElement.innerHTML = `<p style="color: var(--hl-keyword);">Error rendering markdown: ${error.message}</p>`;
        }
    }

    if (savedScrollTop > 0 && previewContainerElement) {
        requestAnimationFrame(() => {
            previewContainerElement.scrollTop = savedScrollTop;
        });
    }
}

/**
 * Scrolls the preview to the given percentage (0-1).
 * Used only for session-state restoration; live bidirectional scroll sync
 * goes through scrollToSourceLine, which uses the marked source map.
 * Returns true if the scroll was applied, false if it was queued for retry.
 */
export function setScrollPercentage(percentage) {
    if (!previewContainerElement) {
        pendingScrollPercentage = percentage;
        return false;
    }

    const scrollHeight = previewContainerElement.scrollHeight - previewContainerElement.clientHeight;
    if (scrollHeight <= 0) {
        pendingScrollPercentage = percentage;
        startScrollRetry();
        return false;
    }

    applyScrollTop(scrollHeight * Math.max(0, Math.min(1, percentage)));
    pendingScrollPercentage = null;
    stopScrollRetry();
    return true;
}

/**
 * Scrolls the preview so that the block with the given source line is at the
 * top of the viewport, offset by `fraction` (0-1) of the way through that block.
 * If no block matches the line, interpolates between the two nearest blocks.
 * Returns true if the scroll was applied, false if it was queued for retry
 * (container has no layout yet, common on deferred tab restore).
 */
export function scrollToSourceLine(line, fraction = 0) {
    if (!previewContainerElement) {
        pendingScrollSourceLine = { line, fraction };
        return false;
    }

    const target = resolveSourceLineToTop(line, fraction);
    if (target === null) {
        pendingScrollSourceLine = { line, fraction };
        startScrollRetry();
        return false;
    }

    applyScrollTop(target);
    pendingScrollSourceLine = null;
    stopScrollRetry();
    return true;
}

/**
 * Returns {line, fraction} for the source position corresponding to the
 * topmost visible point in the preview. `line` is an integer 1-based source
 * line; `fraction` is 0-1 into that line. Combined they describe a
 * sub-line-precision source position so the editor can reveal it without
 * quantising to block boundaries.
 *
 * The three-region interpolation mirrors resolveSourceLineToTop:
 *   (a) Above the first block — line 1, fraction 0.
 *   (b) Between two blocks — lerp between their source lines by scroll progress.
 *   (c) Past the last block — lerp from that block's line to totalSourceLines
 *       by how far into the trailing scroll range we are. Without this the
 *       editor stalled at the last block's line regardless of further preview
 *       scrolling.
 */
export function getTopSourceLine() {
    if (!previewContainerElement) {
        return null;
    }

    const map = buildSourceMap();
    if (map.length === 0) {
        return null;
    }

    const scrollTop = previewContainerElement.scrollTop;

    if (scrollTop <= map[0].top) {
        return { line: map[0].line, fraction: 0 };
    }

    let index = 0;
    for (let i = 0; i < map.length; i++) {
        if (map[i].top <= scrollTop) {
            index = i;
        } else {
            break;
        }
    }

    const current = map[index];
    const next = map[index + 1];

    if (next) {
        const segmentHeight = next.top - current.top;
        const progress = segmentHeight > 0
            ? Math.max(0, Math.min(1, (scrollTop - current.top) / segmentHeight))
            : 0;
        const sourceSpan = next.line - current.line;
        const sourcePosition = current.line + sourceSpan * progress;
        return splitSourcePosition(sourcePosition);
    }

    // Past the last block: project onto the remaining source lines using the
    // preview's remaining scroll range. When totalSourceLines is unknown
    // (never rendered), fall back to the pre-fix behaviour of parking on the
    // current block — safer than extrapolating.
    if (totalSourceLines <= current.line) {
        return { line: current.line, fraction: 0 };
    }

    const maxScroll = Math.max(0, previewContainerElement.scrollHeight - previewContainerElement.clientHeight);
    const remainingScroll = maxScroll - current.top;
    if (remainingScroll <= 0) {
        return { line: current.line, fraction: 0 };
    }

    const progress = Math.max(0, Math.min(1, (scrollTop - current.top) / remainingScroll));
    const sourceSpan = totalSourceLines - current.line;
    const sourcePosition = current.line + sourceSpan * progress;
    return splitSourcePosition(sourcePosition);
}

function splitSourcePosition(sourcePosition) {
    const line = Math.max(1, Math.floor(sourcePosition));
    const fraction = Math.max(0, Math.min(1, sourcePosition - line));
    return { line, fraction };
}

/**
 * Current percentage-based scroll position, retained for session state
 * persistence (onRequestState). Live sync uses getTopSourceLine instead.
 */
export function getScrollPercentage() {
    if (!previewContainerElement) {
        return 0;
    }

    const scrollHeight = previewContainerElement.scrollHeight - previewContainerElement.clientHeight;
    return scrollHeight > 0
        ? Math.max(0, Math.min(1, previewContainerElement.scrollTop / scrollHeight))
        : 0;
}

function applyScrollTop(scrollTop) {
    suppressScrollSync = true;
    previewContainerElement.scrollTop = Math.max(0, scrollTop);
    // Two rAFs to cover the case where the scroll event dispatches on the
    // frame after the assignment; clearing suppressScrollSync any sooner can
    // leak the programmatic scroll back as an onSyncToEditor callback.
    requestAnimationFrame(() => {
        requestAnimationFrame(() => {
            suppressScrollSync = false;
        });
    });
}

/**
 * Walks the rendered preview for `data-source-line` attributes and returns
 * an array of {line, top} entries sorted by top offset. `top` is measured
 * relative to the container's scroll origin, so it can be compared directly
 * against scrollTop.
 */
function buildSourceMap() {
    if (!previewContentElement) {
        return [];
    }

    const nodes = previewContentElement.querySelectorAll('[data-source-line]');
    const entries = [];
    for (const node of nodes) {
        const line = parseInt(node.dataset.sourceLine, 10);
        if (!Number.isFinite(line) || line < 1) {
            continue;
        }
        // offsetTop is relative to the nearest positioned ancestor. The preview
        // body has no transform/position hacks and the container is its offset
        // parent, so offsetTop matches the scrollTop coordinate space directly.
        entries.push({ line, top: node.offsetTop });
    }
    entries.sort((a, b) => a.top - b.top);
    return entries;
}

/**
 * Converts a source-line target (+ fraction within that line) into a container
 * scrollTop. `fraction` here is Monaco's sub-line position (0-1 across the top
 * visible line's height), not a fraction of the inter-block gap. Returns null
 * when the preview has no layout yet.
 *
 * Three regions:
 *   (a) Before the first mapped block — pin to scrollTop 0.
 *   (b) Between two mapped blocks — lerp by source-line progress.
 *   (c) Past the last mapped block — lerp by source-line progress into the
 *       remaining scroll range, using `totalSourceLines` so the tail of the
 *       source maps onto the tail of the preview rather than parking on the
 *       last block's top.
 */
function resolveSourceLineToTop(line, fraction) {
    const map = buildSourceMap();
    if (map.length === 0) {
        return null;
    }

    const clampedFraction = Math.max(0, Math.min(1, fraction));
    const sourcePosition = line + clampedFraction;

    if (sourcePosition <= map[0].line) {
        return 0;
    }

    // Binary search would be marginal at our sizes; walk is fine.
    let before = map[0];
    let after = null;
    for (const entry of map) {
        if (entry.line <= line) {
            before = entry;
        } else {
            after = entry;
            break;
        }
    }

    if (after) {
        const lineSpan = after.line - before.line;
        const progress = lineSpan > 0 ? (sourcePosition - before.line) / lineSpan : 0;
        return before.top + (after.top - before.top) * Math.max(0, Math.min(1, progress));
    }

    // Past the last mapped block: interpolate from that block's top to the
    // preview's maxScroll, based on how many source lines remain beyond the
    // last block. Without `totalSourceLines` this is the drift zone that
    // earlier implementations parked on — the editor continued but the
    // preview stalled.
    const maxScroll = Math.max(0, previewContainerElement.scrollHeight - previewContainerElement.clientHeight);
    const remainingLines = Math.max(1, totalSourceLines - before.line);
    const linesPastBefore = Math.max(0, sourcePosition - before.line);
    const progress = Math.max(0, Math.min(1, linesPastBefore / remainingLines));
    return before.top + (maxScroll - before.top) * progress;
}

function startScrollRetry() {
    if (scrollResizeObserver || !previewContainerElement) {
        return;
    }

    scrollResizeObserver = new ResizeObserver(() => {
        if (!previewContainerElement) {
            stopScrollRetry();
            return;
        }
        const scrollHeight = previewContainerElement.scrollHeight - previewContainerElement.clientHeight;
        if (scrollHeight <= 0) {
            return;
        }

        if (pendingScrollSourceLine !== null) {
            const { line, fraction } = pendingScrollSourceLine;
            const target = resolveSourceLineToTop(line, fraction);
            if (target !== null) {
                applyScrollTop(target);
                pendingScrollSourceLine = null;
            }
        }

        if (pendingScrollPercentage !== null) {
            applyScrollTop(scrollHeight * Math.max(0, Math.min(1, pendingScrollPercentage)));
            pendingScrollPercentage = null;
        }

        if (pendingScrollSourceLine === null && pendingScrollPercentage === null) {
            stopScrollRetry();
        }
    });
    scrollResizeObserver.observe(previewContainerElement);
}

function stopScrollRetry() {
    if (scrollResizeObserver) {
        scrollResizeObserver.disconnect();
        scrollResizeObserver = null;
    }
}

function attachLinkHandlers() {
    if (!previewContentElement) {
        return;
    }

    const links = previewContentElement.querySelectorAll('a[href]');
    const currentUrl = iframeElement.contentWindow.location.href.split('#')[0];

    links.forEach((link) => {
        const href = link.getAttribute('href');
        const isAnchorLink = href && (
            href.startsWith('#') ||
            (href.startsWith(currentUrl) && href.includes('#'))
        );

        if (isAnchorLink) {
            link.addEventListener('click', (e) => {
                e.preventDefault();
                const hashIndex = href.indexOf('#');
                const targetId = hashIndex >= 0 ? href.slice(hashIndex + 1) : '';
                const target = iframeElement.contentDocument.getElementById(targetId);
                if (target) {
                    target.scrollIntoView({ behavior: 'smooth' });
                }
            });
        } else if (href) {
            link.addEventListener('click', (e) => {
                e.preventDefault();
                if (callbacks && callbacks.onLinkClicked) {
                    callbacks.onLinkClicked(href);
                }
            });
        }
    });
}

function attachScrollListener() {
    if (!previewContainerElement) {
        return;
    }

    let scrollSyncPending = false;
    previewContainerElement.addEventListener('scroll', () => {
        if (suppressScrollSync || scrollSyncPending) {
            return;
        }
        scrollSyncPending = true;
        requestAnimationFrame(() => {
            scrollSyncPending = false;
            const target = getTopSourceLine();
            if (target !== null && callbacks && callbacks.onSyncToEditor) {
                callbacks.onSyncToEditor(target);
            }
        });
    });
}

function attachClickToSyncListener() {
    if (!previewContainerElement) {
        return;
    }

    previewContainerElement.addEventListener('click', (event) => {
        if (event.target.closest('a')) {
            return;
        }

        // Find the block the user clicked on and use its source line directly
        // so the editor reveals the exact block rather than interpolating.
        const clickedBlock = event.target.closest('[data-source-line]');
        if (clickedBlock && callbacks && callbacks.onSyncToEditor) {
            const line = parseInt(clickedBlock.dataset.sourceLine, 10);
            if (Number.isFinite(line) && line >= 1) {
                callbacks.onSyncToEditor({ line, fraction: 0 });
            }
        }
    });
}
