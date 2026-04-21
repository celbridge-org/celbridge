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
let scrollResizeObserver = null;

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
            return `<h${token.depth} id="${slug}">${text}</h${token.depth}>\n`;
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
        const html = marked.parse(markdown);
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
 * Returns true if the scroll was applied, false if it could not be (the
 * preview container is missing or has no measurable scroll range). In the
 * "not ready" case the value is queued internally and retried via a
 * ResizeObserver once the container gains layout — the common scenario is
 * project reload, where tabs are restored but not activated so the iframe
 * body has zero layout until the tab is selected.
 * The resulting scroll event is swallowed to prevent echoing back to the editor.
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

    applyScroll(scrollHeight, percentage);
    pendingScrollPercentage = null;
    stopScrollRetry();
    return true;
}

function applyScroll(scrollHeight, percentage) {
    suppressScrollSync = true;
    previewContainerElement.scrollTop = scrollHeight * Math.max(0, Math.min(1, percentage));
    requestAnimationFrame(() => {
        requestAnimationFrame(() => {
            suppressScrollSync = false;
        });
    });
}

function startScrollRetry() {
    if (scrollResizeObserver || !previewContainerElement) {
        return;
    }

    scrollResizeObserver = new ResizeObserver(() => {
        if (pendingScrollPercentage === null || !previewContainerElement) {
            stopScrollRetry();
            return;
        }
        const scrollHeight = previewContainerElement.scrollHeight - previewContainerElement.clientHeight;
        if (scrollHeight > 0) {
            applyScroll(scrollHeight, pendingScrollPercentage);
            pendingScrollPercentage = null;
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

/**
 * Returns the preview's current scroll position as a percentage (0-1).
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
            const percentage = getScrollPercentage();
            if (callbacks && callbacks.onSyncToEditor) {
                callbacks.onSyncToEditor(percentage);
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

        const rect = previewContainerElement.getBoundingClientRect();
        const clickY = event.clientY - rect.top + previewContainerElement.scrollTop;
        const totalHeight = previewContainerElement.scrollHeight;
        const percentage = totalHeight > 0 ? clickY / totalHeight : 0;

        if (callbacks && callbacks.onSyncToEditor) {
            callbacks.onSyncToEditor(Math.max(0, Math.min(1, percentage)));
        }
    });
}
