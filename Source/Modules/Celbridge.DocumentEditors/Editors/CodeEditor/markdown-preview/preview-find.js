// In-content find for the Markdown preview iframe.
//
// Runs in the parent page (like the rest of the preview module) and operates on the same-origin iframe's
// document. Highlighting uses the CSS Custom Highlight API scoped to the iframe, so matches are confined to
// the preview and never touch the Monaco editor shell that shares the WebView. Built only where the WebView
// backend has no find bar of its own (macOS WKWebView); the Chromium heads use the built-in bar instead.

import { t } from '/assets/celbridge-client/localization.js';

const MATCH_HIGHLIGHT = 'preview-find';
const ACTIVE_MATCH_HIGHLIGHT = 'preview-find-active';

export class PreviewFind {
    #iframe;
    #doc = null;
    #win = null;
    #contentElement = null;

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

    constructor(iframe) {
        this.#iframe = iframe;
    }

    // Wires the find bar and the Cmd/Ctrl+F capture into the iframe document. A no-op if the CSS Custom
    // Highlight API is unavailable, so an older WebKit degrades to no preview find rather than a broken one.
    install() {
        this.#doc = this.#iframe.contentDocument;
        this.#win = this.#iframe.contentWindow;
        if (!this.#doc || !this.#win) {
            return;
        }

        this.#supported = !!(this.#win.CSS
            && this.#win.CSS.highlights
            && typeof this.#win.Highlight === 'function');
        if (!this.#supported) {
            console.warn('[preview-find] CSS Custom Highlight API unavailable; preview find disabled');
            return;
        }

        this.#contentElement = this.#doc.getElementById('preview-content');
        this.#bar = this.#doc.getElementById('preview-find-bar');
        this.#input = this.#doc.getElementById('preview-find-input');
        this.#countElement = this.#doc.getElementById('preview-find-count');
        this.#matchCaseButton = this.#doc.getElementById('preview-find-match-case');
        this.#wholeWordButton = this.#doc.getElementById('preview-find-whole-word');
        this.#previousButton = this.#doc.getElementById('preview-find-prev');
        this.#nextButton = this.#doc.getElementById('preview-find-next');
        this.#closeButton = this.#doc.getElementById('preview-find-close');

        if (!this.#contentElement || !this.#bar || !this.#input) {
            return;
        }

        // Capture Cmd/Ctrl+F when the preview holds focus. Monaco captures it in the parent document, so the
        // two never collide: whichever pane is focused handles the key.
        this.#doc.addEventListener('keydown', (event) => this.#onDocumentKeyDown(event));

        this.#input.addEventListener('input', () => this.#runSearch());
        this.#input.addEventListener('keydown', (event) => this.#onInputKeyDown(event));
        this.#matchCaseButton.addEventListener('click', () => this.#toggleOption(this.#matchCaseButton));
        this.#wholeWordButton.addEventListener('click', () => this.#toggleOption(this.#wholeWordButton));
        this.#previousButton.addEventListener('click', () => this.#stepOrStart(-1));
        this.#nextButton.addEventListener('click', () => this.#stepOrStart(1));
        this.#closeButton.addEventListener('click', () => this.close());
    }

    // Re-runs the current search after the preview content changes, so highlights track live edits without
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

        // Return focus to the preview so keystrokes reach the content, not the hidden find box.
        this.#win.focus();
    }

    #reveal() {
        // Reveals the bar without searching: a preserved term is selected so the next keystroke replaces it,
        // and the scroll stays put until the user explicitly searches.
        // Localized strings are applied here rather than at install time, which can precede string loading.
        this.#input.placeholder = t('CodeEditor_Find_Placeholder');
        this.#matchCaseButton.title = t('CodeEditor_Find_MatchCase');
        this.#wholeWordButton.title = t('CodeEditor_Find_WholeWord');
        this.#previousButton.title = t('CodeEditor_Find_Previous');
        this.#nextButton.title = t('CodeEditor_Find_Next');
        this.#closeButton.title = t('CodeEditor_Find_Close');

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

    // Walks the rendered preview's text nodes and returns a Range for each substring match, honouring the
    // match-case and whole-word toggles. Matches are confined to a single text node (matches spanning element
    // boundaries are not found), which is an accepted v1 limitation for prose.
    #collectRanges(term) {
        const caseSensitive = this.#matchCaseButton.getAttribute('aria-pressed') === 'true';
        const wholeWord = this.#wholeWordButton.getAttribute('aria-pressed') === 'true';

        const ranges = [];
        const needle = caseSensitive ? term : term.toLowerCase();
        const walker = this.#doc.createTreeWalker(this.#contentElement, NodeFilter.SHOW_TEXT);

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
            this.#countElement.textContent = this.#input.value ? t('CodeEditor_Find_NoResults') : '';
            return;
        }

        this.#countElement.textContent = t('CodeEditor_Find_Count', this.#activeIndex + 1, this.#ranges.length);
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
