// Reusable find bar for Celbridge WebView editors whose backend has no find bar of its own.
//
// Self-contained: given a document and a root element to search, it injects its own bar DOM and styles,
// captures Cmd/Ctrl+F on that document, and highlights matches with the CSS Custom Highlight API scoped to
// that document. Its FindBar_* strings ship with the client's localization files, loaded through the same
// {locale}.json workflow as everything else, so it stays consistent when Celbridge is localized.
//
// Usage:
//   import { createFindBar } from '/assets/celbridge-client/ui/find-bar.js';
//   const findBar = createFindBar({ document: iframe.contentDocument, searchRoot: contentElement });
//   findBar.refresh(); // when the searched content changes
//
// Callers decide whether to attach it: on backends with a built-in find bar (Chromium's WebView2) they should
// not, so Ctrl+F reaches the built-in bar instead.

import { t } from '../localization.js';

const MATCH_HIGHLIGHT = 'celbridge-find';
const ACTIVE_MATCH_HIGHLIGHT = 'celbridge-find-active';
const STYLE_ELEMENT_ID = 'celbridge-find-styles';
const ICON_FONT_LINK_ID = 'celbridge-find-icon-font';

const STYLES = `
.celbridge-find-bar {
    position: fixed;
    top: 0;
    right: 0;
    z-index: 2147483000;
    display: flex;
    align-items: center;
    gap: 4px;
    padding: 6px 8px;
    font-size: 13px;
    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', 'Noto Sans', Helvetica, Arial, sans-serif;
    background-color: #f6f8fa;
    color: #24292f;
    border: 1px solid #d0d7de;
    border-top: none;
    border-right: none;
    border-radius: 0 0 0 6px;
    box-shadow: 0 2px 8px rgba(0, 0, 0, 0.15);
}
.celbridge-find-bar[hidden] { display: none; }
.celbridge-find-input {
    width: 240px;
    padding: 6px 8px;
    font-size: 14px;
    border: 1px solid #d0d7de;
    border-radius: 4px;
    background-color: #ffffff;
    color: #24292f;
    outline: none;
}
.celbridge-find-count {
    /* Wide enough for the counter and the "No results" message, so the bar width does not jump between them. */
    min-width: 88px;
    text-align: center;
    white-space: nowrap;
    color: #656d76;
}
.celbridge-find-bar button {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    width: 28px;
    height: 28px;
    padding: 0;
    border: none;
    border-radius: 4px;
    background: transparent;
    color: inherit;
    cursor: pointer;
}
.celbridge-find-bar button:hover { background-color: rgba(0, 0, 0, 0.08); }
.celbridge-find-bar .bi { font-size: 15px; }
.celbridge-find-toggle { font-size: 13px; font-weight: 600; }
.celbridge-find-toggle[aria-pressed="true"] { background-color: #0969da; color: #ffffff; }

html[data-theme="dark"] .celbridge-find-bar {
    background-color: #282828;
    color: #d4d4d4;
    border-color: #444;
}
html[data-theme="dark"] .celbridge-find-input {
    background-color: #1e1e1e;
    color: #d4d4d4;
    border-color: #444;
}
html[data-theme="dark"] .celbridge-find-count { color: #9e9e9e; }
html[data-theme="dark"] .celbridge-find-bar button:hover { background-color: rgba(255, 255, 255, 0.1); }
html[data-theme="dark"] .celbridge-find-toggle[aria-pressed="true"] { background-color: #58a6ff; color: #1e1e1e; }

/* Yellow for all matches, orange for the active one, mirroring the familiar browser find colours. */
::highlight(celbridge-find) { background-color: #ffd54f; color: #000000; }
::highlight(celbridge-find-active) { background-color: #ff9800; color: #000000; }
`;

/**
 * Creates and installs a find bar. Returns a controller with refresh().
 * @param {{ document: Document, searchRoot: Element }} options
 */
export function createFindBar(options) {
    const findBar = new FindBar(options.document, options.searchRoot);
    findBar.install();
    return findBar;
}

class FindBar {
    #doc;
    #win = null;
    #searchRoot;

    #bar = null;
    #input = null;
    #countElement = null;
    #matchCaseButton = null;
    #wholeWordButton = null;
    #previousButton = null;
    #nextButton = null;
    #closeButton = null;

    #ranges = [];
    #activeIndex = -1;
    #open = false;
    #hasActiveFind = false;
    #supported = false;

    constructor(document, searchRoot) {
        this.#doc = document;
        this.#searchRoot = searchRoot;
    }

    // Injects the bar and styles and wires the Cmd/Ctrl+F capture. A no-op if the CSS Custom Highlight API is
    // unavailable, so an older engine degrades to no find bar rather than a broken one.
    install() {
        this.#win = this.#doc ? this.#doc.defaultView : null;
        if (!this.#win || !this.#searchRoot) {
            return;
        }

        this.#supported = !!(this.#win.CSS
            && this.#win.CSS.highlights
            && typeof this.#win.Highlight === 'function');
        if (!this.#supported) {
            console.warn('[find-bar] CSS Custom Highlight API unavailable; find disabled');
            return;
        }

        this.#injectStyles();
        this.#injectIconFont();
        this.#buildBar();

        // Capture Cmd/Ctrl+F on this document (fires when it holds focus). A host page that owns another find
        // affordance (e.g. Monaco in a sibling document) captures the key there, so the two never collide.
        this.#doc.addEventListener('keydown', (event) => this.#onDocumentKeyDown(event));

        this.#input.addEventListener('input', () => this.#runSearch());
        this.#input.addEventListener('keydown', (event) => this.#onInputKeyDown(event));
        this.#matchCaseButton.addEventListener('click', () => this.#toggleOption(this.#matchCaseButton));
        this.#wholeWordButton.addEventListener('click', () => this.#toggleOption(this.#wholeWordButton));
        this.#previousButton.addEventListener('click', () => this.#stepOrStart(-1));
        this.#nextButton.addEventListener('click', () => this.#stepOrStart(1));
        this.#closeButton.addEventListener('click', () => this.close());
    }

    // Re-runs the current search after the searched content changes, so highlights track live edits without
    // scrolling the view. A no-op when the bar is closed or no search is active.
    refresh() {
        if (!this.#supported || !this.#open || !this.#hasActiveFind) {
            return;
        }

        const term = this.#input.value;
        if (!term) {
            this.#clearHighlights();
            this.#updateCount();
            return;
        }

        this.#ranges = this.#collectRanges(term);
        this.#applyMatchHighlights();

        if (this.#activeIndex >= this.#ranges.length) {
            this.#activeIndex = this.#ranges.length - 1;
        }
        if (this.#activeIndex < 0 && this.#ranges.length > 0) {
            this.#activeIndex = 0;
        }

        this.#applyActiveHighlight(false);
        this.#updateCount();
    }

    close() {
        this.#open = false;
        this.#hasActiveFind = false;
        this.#clearHighlights();
        this.#bar.hidden = true;
        this.#countElement.textContent = '';

        // Return focus to the content so keystrokes reach it, not the hidden find box.
        this.#win.focus();
    }

    #injectStyles() {
        if (this.#doc.getElementById(STYLE_ELEMENT_ID)) {
            return;
        }

        const style = this.#doc.createElement('style');
        style.id = STYLE_ELEMENT_ID;
        style.textContent = STYLES;
        this.#doc.head.appendChild(style);
    }

    #injectIconFont() {
        if (this.#doc.getElementById(ICON_FONT_LINK_ID)) {
            return;
        }

        // Use the shared Bootstrap Icons font the rest of the Celbridge UI uses. Resolved relative to this
        // module so it loads from the client's asset root on any origin.
        const link = this.#doc.createElement('link');
        link.id = ICON_FONT_LINK_ID;
        link.rel = 'stylesheet';
        link.href = new URL('../../bootstrap-icons/bootstrap-icons.css', import.meta.url).href;
        link.addEventListener('load', () => this.#ensureIconFont(), { once: true });
        this.#doc.head.appendChild(link);
    }

    #ensureIconFont() {
        // Force the icon font to load rather than relying on WebKit's paint-triggered lazy loading, which
        // leaves the glyphs as tofu when the bar was created while the preview pane was hidden (the default
        // Source view mode) and later shown in a way that does not repaint them (e.g. split view).
        const fonts = this.#doc.fonts;
        if (fonts && typeof fonts.load === 'function') {
            fonts.load('16px "bootstrap-icons"').catch(() => {});
        }
    }

    #buildBar() {
        this.#input = this.#doc.createElement('input');
        this.#input.type = 'text';
        this.#input.className = 'celbridge-find-input';

        this.#matchCaseButton = this.#createToggleButton('<i class="bi bi-type"></i>');
        this.#wholeWordButton = this.#createToggleButton('ab');

        this.#countElement = this.#doc.createElement('span');
        this.#countElement.className = 'celbridge-find-count';

        this.#previousButton = this.#createIconButton('bi-chevron-up');
        this.#nextButton = this.#createIconButton('bi-chevron-down');
        this.#closeButton = this.#createIconButton('bi-x-lg');

        this.#bar = this.#doc.createElement('div');
        this.#bar.className = 'celbridge-find-bar';
        this.#bar.hidden = true;
        this.#bar.append(
            this.#input,
            this.#matchCaseButton,
            this.#wholeWordButton,
            this.#countElement,
            this.#previousButton,
            this.#nextButton,
            this.#closeButton);

        this.#doc.body.appendChild(this.#bar);
    }

    #createToggleButton(contentHtml) {
        const button = this.#doc.createElement('button');
        button.type = 'button';
        button.className = 'celbridge-find-toggle';
        button.innerHTML = contentHtml;
        button.setAttribute('aria-pressed', 'false');
        return button;
    }

    #createIconButton(iconClass) {
        const button = this.#doc.createElement('button');
        button.type = 'button';
        button.innerHTML = `<i class="bi ${iconClass}"></i>`;
        return button;
    }

    #reveal() {
        // Reveals the bar without searching: a preserved term is selected so the next keystroke replaces it,
        // and the scroll stays put until the user explicitly searches. Localized labels are applied here (not
        // at build time, which can precede string loading).
        this.#input.placeholder = t('FindBar_Placeholder');
        this.#matchCaseButton.title = t('FindBar_MatchCase');
        this.#wholeWordButton.title = t('FindBar_WholeWord');
        this.#previousButton.title = t('FindBar_Previous');
        this.#nextButton.title = t('FindBar_Next');
        this.#closeButton.title = t('FindBar_Close');

        // The bar is now visible, so make sure the icon font is loaded (see ensureIconFont).
        this.#ensureIconFont();

        this.#open = true;
        this.#bar.hidden = false;
        this.#input.focus();
        this.#input.select();
    }

    #onDocumentKeyDown(event) {
        const modifier = event.metaKey || event.ctrlKey;
        if (modifier && (event.key === 'f' || event.key === 'F')) {
            event.preventDefault();
            this.#reveal();
        } else if (event.key === 'Escape' && this.#open) {
            event.preventDefault();
            this.close();
        }
    }

    #onInputKeyDown(event) {
        if (event.key === 'Enter') {
            event.preventDefault();
            this.#stepOrStart(event.shiftKey ? -1 : 1);
        } else if (event.key === 'Escape') {
            event.preventDefault();
            this.close();
        }
    }

    #toggleOption(button) {
        const pressed = button.getAttribute('aria-pressed') === 'true';
        button.setAttribute('aria-pressed', pressed ? 'false' : 'true');

        // Re-run an active search so results reflect the new option.
        if (this.#hasActiveFind) {
            this.#runSearch();
        }
    }

    #stepOrStart(direction) {
        if (!this.#input.value) {
            return;
        }

        // First explicit search since the bar opened: run it (finding and scrolling to the first match). Once
        // active, next/previous step within it.
        if (!this.#hasActiveFind) {
            this.#runSearch();
            return;
        }

        this.#step(direction);
    }

    #runSearch() {
        const term = this.#input.value;
        if (!term) {
            this.#hasActiveFind = false;
            this.#clearHighlights();
            this.#updateCount();
            return;
        }

        this.#hasActiveFind = true;
        this.#ranges = this.#collectRanges(term);
        this.#applyMatchHighlights();
        this.#activeIndex = this.#ranges.length > 0 ? 0 : -1;
        this.#applyActiveHighlight(true);
        this.#updateCount();
    }

    #step(direction) {
        if (this.#ranges.length === 0) {
            return;
        }

        const count = this.#ranges.length;
        this.#activeIndex = (this.#activeIndex + direction + count) % count;
        this.#applyActiveHighlight(true);
        this.#updateCount();
    }

    // Walks the search root's text nodes and returns a Range for each substring match, honouring the
    // match-case and whole-word toggles. Matches are confined to a single text node (matches spanning element
    // boundaries are not found), which is an accepted limitation for prose.
    #collectRanges(term) {
        const caseSensitive = this.#matchCaseButton.getAttribute('aria-pressed') === 'true';
        const wholeWord = this.#wholeWordButton.getAttribute('aria-pressed') === 'true';

        const ranges = [];
        const needle = caseSensitive ? term : term.toLowerCase();
        const walker = this.#doc.createTreeWalker(this.#searchRoot, NodeFilter.SHOW_TEXT);

        let node = walker.nextNode();
        while (node) {
            const text = node.nodeValue;
            if (text) {
                const haystack = caseSensitive ? text : text.toLowerCase();
                let index = haystack.indexOf(needle);
                while (index !== -1) {
                    const end = index + term.length;
                    if (!wholeWord || isWholeWordMatch(text, index, end)) {
                        const range = this.#doc.createRange();
                        range.setStart(node, index);
                        range.setEnd(node, end);
                        ranges.push(range);
                    }
                    index = haystack.indexOf(needle, end);
                }
            }
            node = walker.nextNode();
        }

        return ranges;
    }

    #applyMatchHighlights() {
        const highlight = new this.#win.Highlight(...this.#ranges);
        this.#win.CSS.highlights.set(MATCH_HIGHLIGHT, highlight);
    }

    #applyActiveHighlight(scrollIntoView) {
        if (this.#activeIndex < 0 || this.#activeIndex >= this.#ranges.length) {
            this.#win.CSS.highlights.delete(ACTIVE_MATCH_HIGHLIGHT);
            return;
        }

        const activeRange = this.#ranges[this.#activeIndex];
        const highlight = new this.#win.Highlight(activeRange);
        // Paint the active match over the plain matches where they overlap.
        highlight.priority = 1;
        this.#win.CSS.highlights.set(ACTIVE_MATCH_HIGHLIGHT, highlight);

        if (scrollIntoView) {
            const element = activeRange.startContainer.parentElement;
            if (element) {
                element.scrollIntoView({ block: 'center', inline: 'nearest' });
            }
        }
    }

    #updateCount() {
        if (this.#ranges.length === 0) {
            this.#countElement.textContent = this.#input.value ? t('FindBar_NoResults') : '';
            return;
        }

        this.#countElement.textContent = t('FindBar_Count', this.#activeIndex + 1, this.#ranges.length);
    }

    #clearHighlights() {
        this.#ranges = [];
        this.#activeIndex = -1;
        this.#win.CSS.highlights.delete(MATCH_HIGHLIGHT);
        this.#win.CSS.highlights.delete(ACTIVE_MATCH_HIGHLIGHT);
    }
}

// A match [start, end) within a text node is a whole word when the characters on either side are not word
// characters (or the match sits at the node's edge). Boundaries across element edges count as word breaks,
// which is fine for prose.
function isWholeWordMatch(text, start, end) {
    const before = start > 0 ? text[start - 1] : '';
    const after = end < text.length ? text[end] : '';
    return !isWordCharacter(before) && !isWordCharacter(after);
}

function isWordCharacter(character) {
    return character !== '' && /\w/.test(character);
}
