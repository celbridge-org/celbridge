// In-page bridge for the webview_* MCP tool namespace. Injected by the
// Celbridge host into supported WebViews. Unrelated to WebView2's built-in
// browser DevTools.
(function () {
    'use strict';

    // Bail out in cross-origin frames. The Celbridge WebView serves editor content from the loopback
    // file server (127.0.0.1) on every head, except a synthetic-origin editor, which runs under its own
    // {package}.celbridge host. Any other origin is an embedded iframe loading external content and the
    // shim must not activate there.
    var hostname = (typeof location !== 'undefined' && location && location.hostname) || '';
    if (hostname !== '127.0.0.1'
        && !/\.celbridge$/.test(hostname)) {
        return;
    }

    // Hang the namespace off a Symbol-keyed slot on globalThis so editor
    // package code that walks window.* never sees it.
    var bridgeKey = Symbol.for('__cel_webview_tools');
    if (globalThis[bridgeKey]) {
        return;
    }

    // The name of the page itself, as opposed to a frame inside it. The host uses the same name.
    var TOP_FRAME = 'top';

    // A page marks the frame that shows its content with this attribute. A call that names no frame acts on
    // that frame.
    var CONTENT_FRAME_SELECTOR = '[data-cel-content-frame]';

    // Set on a frame's window when the shim reloads the frame. The page that replaces it gets a new window
    // without this mark, so a window that still has it has not been replaced yet.
    var reloadingKey = Symbol('reloading');

    // Bounded ring buffer for console messages and uncaught errors. Older
    // entries are evicted FIFO once the buffer fills up. Host code drains
    // and accumulates these before each get-console call so the cap is a
    // soft ceiling, not a hard one.
    var CONSOLE_BUFFER_LIMIT = 500;
    var CONSOLE_ARG_LIMIT = 4096;
    var consoleBuffer = [];

    function pushConsoleEntry(level, args, stack) {
        var serialised = [];
        for (var i = 0; i < args.length; i++) {
            serialised.push(stringifyArg(args[i]));
        }
        consoleBuffer.push({
            level: level,
            timestampMs: Date.now(),
            args: serialised,
            stack: stack || null
        });
        if (consoleBuffer.length > CONSOLE_BUFFER_LIMIT) {
            consoleBuffer.splice(0, consoleBuffer.length - CONSOLE_BUFFER_LIMIT);
        }
    }

    function stringifyArg(value) {
        var rendered;
        if (value === null) {
            rendered = 'null';
        } else if (value === undefined) {
            rendered = 'undefined';
        } else if (typeof value === 'string') {
            rendered = value;
        } else if (typeof value === 'number' || typeof value === 'boolean' || typeof value === 'bigint') {
            rendered = String(value);
        } else if (value instanceof Error) {
            rendered = (value.stack && String(value.stack)) || (value.name + ': ' + value.message);
        } else {
            try {
                rendered = JSON.stringify(value, jsonReplacer);
                if (rendered === undefined) {
                    rendered = String(value);
                }
            } catch (e) {
                rendered = '[unserialisable: ' + (e && e.message) + ']';
            }
        }
        if (rendered.length > CONSOLE_ARG_LIMIT) {
            rendered = rendered.slice(0, CONSOLE_ARG_LIMIT) + '...[truncated ' + (rendered.length - CONSOLE_ARG_LIMIT) + ' bytes]';
        }
        return rendered;
    }

    function jsonReplacer(_key, value) {
        if (value instanceof Error) {
            return { name: value.name, message: value.message, stack: value.stack };
        }
        if (typeof value === 'function') {
            return '[function ' + (value.name || 'anonymous') + ']';
        }
        if (typeof Node !== 'undefined' && value instanceof Node) {
            return '[node ' + (value.nodeName || 'unknown') + ']';
        }
        return value;
    }

    // Wrap console.* so messages are captured before being forwarded to the
    // original console implementation. Wrapping is one-shot (the marker stops
    // re-entry if the shim is somehow injected twice).
    var levels = ['log', 'info', 'warn', 'error', 'debug'];
    if (typeof console !== 'undefined' && !console.__celWrapped) {
        levels.forEach(function (level) {
            var original = console[level];
            console[level] = function () {
                var args = [];
                for (var i = 0; i < arguments.length; i++) {
                    args.push(arguments[i]);
                }
                pushConsoleEntry(level, args, null);
                if (typeof original === 'function') {
                    return original.apply(console, args);
                }
            };
        });
        try {
            Object.defineProperty(console, '__celWrapped', { value: true, enumerable: false });
        } catch {
            console.__celWrapped = true;
        }
    }

    // Reports a crash to the host's application log. The ring buffer below only reaches an agent that is
    // attached and asks for it, so without this a page that dies takes the reason with it. Prefers the
    // page's live transport, which the client exposes for injected scripts like this one, and falls back to
    // the native bridges for a page that never loaded the client.
    function reportToHostLog(message, stack) {
        try {
            var envelope = JSON.stringify({
                jsonrpc: '2.0',
                method: 'host/log',
                params: { level: 'error', message: stack ? message + '\n' + stack : message }
            });
            if (typeof globalThis.__hostSendMessage === 'function') {
                globalThis.__hostSendMessage(envelope);
            } else if (window.chrome && window.chrome.webview) {
                window.chrome.webview.postMessage(envelope);
            } else if (window.webkit
                && window.webkit.messageHandlers
                && window.webkit.messageHandlers.unoWebView) {
                window.webkit.messageHandlers.unoWebView.postMessage(envelope);
            }
        } catch {
            // Reporting a crash must never cause one.
        }
    }

    // Capture uncaught errors and unhandled promise rejections as synthetic
    // 'error'-level entries. Stack traces are preserved when the runtime
    // provides one.
    if (typeof window !== 'undefined' && typeof window.addEventListener === 'function') {
        window.addEventListener('error', function (event) {
            var err = event && event.error;
            var message = (event && event.message) || (err && err.message) || 'uncaught error';
            var stack = (err && err.stack) || null;
            pushConsoleEntry('error', [message], stack);
            reportToHostLog(message, stack);
        });
        // A resource that fails to load fires an error event at its element rather than at the window, and
        // that event does not bubble, so only a capture-phase listener sees it. Reported with the URL: a
        // script that never loads is otherwise silent here, and only surfaces much later as a missing
        // global. Uncaught exceptions reach this listener too, targeted at the window, and are left to the
        // handler above.
        window.addEventListener('error', function (event) {
            var target = event && event.target;
            if (!target || target === window || !target.tagName) {
                return;
            }
            var url = target.src || target.href || '';
            var message = 'failed to load ' + target.tagName.toLowerCase() + (url ? ' ' + url : '');
            pushConsoleEntry('error', [message], null);
            reportToHostLog(message, null);
        }, true);
        window.addEventListener('unhandledrejection', function (event) {
            var reason = event && event.reason;
            var message;
            var stack = null;
            if (reason instanceof Error) {
                message = reason.message;
                stack = reason.stack || null;
            } else {
                message = stringifyArg(reason);
            }
            pushConsoleEntry('error', ['unhandled rejection: ' + message], stack);
            reportToHostLog('unhandled rejection: ' + message, stack);
        });
    }

    var handlers = Object.create(null);

    // Hand-rolled equivalents of @testing-library/dom and @medv/finder, covering
    // only the surface the tools exercise: ARIA role + accessible name, visible
    // text, CSS selector for queries, and a unique-path selector generator.
    function getElementRole(el) {
        var explicit = el.getAttribute && el.getAttribute('role');
        if (explicit) {
            return explicit.trim().toLowerCase();
        }
        var tag = (el.tagName || '').toLowerCase();
        var typeAttr = el.getAttribute ? (el.getAttribute('type') || '').toLowerCase() : '';
        switch (tag) {
            case 'a':
                return el.getAttribute && el.getAttribute('href') ? 'link' : '';
            case 'button':
                return 'button';
            case 'h1': case 'h2': case 'h3': case 'h4': case 'h5': case 'h6':
                return 'heading';
            case 'nav':
                return 'navigation';
            case 'main':
                return 'main';
            case 'header':
                return 'banner';
            case 'footer':
                return 'contentinfo';
            case 'aside':
                return 'complementary';
            case 'section':
                return 'region';
            case 'article':
                return 'article';
            case 'form':
                return 'form';
            case 'ul': case 'ol':
                return 'list';
            case 'li':
                return 'listitem';
            case 'table':
                return 'table';
            case 'tr':
                return 'row';
            case 'td':
                return 'cell';
            case 'th':
                return 'columnheader';
            case 'img':
                return el.getAttribute && el.getAttribute('alt') === '' ? 'presentation' : 'img';
            case 'select':
                return 'combobox';
            case 'option':
                return 'option';
            case 'textarea':
                return 'textbox';
            case 'input':
                if (typeAttr === 'button' || typeAttr === 'submit' || typeAttr === 'reset') return 'button';
                if (typeAttr === 'checkbox') return 'checkbox';
                if (typeAttr === 'radio') return 'radio';
                if (typeAttr === 'range') return 'slider';
                if (typeAttr === 'search') return 'searchbox';
                if (typeAttr === 'number') return 'spinbutton';
                if (typeAttr === '' || typeAttr === 'text' || typeAttr === 'email' || typeAttr === 'tel' || typeAttr === 'url' || typeAttr === 'password') return 'textbox';
                return '';
            default:
                return '';
        }
    }

    function getAccessibleName(el) {
        if (!el || !el.getAttribute) return '';

        var ownerDocument = el.ownerDocument;

        var ariaLabel = el.getAttribute('aria-label');
        if (ariaLabel) return ariaLabel.trim();

        var labelledBy = el.getAttribute('aria-labelledby');
        if (labelledBy) {
            var ids = labelledBy.split(/\s+/);
            var parts = [];
            for (var i = 0; i < ids.length; i++) {
                var ref = ownerDocument.getElementById(ids[i]);
                if (ref) parts.push((ref.textContent || '').trim());
            }
            var joined = parts.join(' ').trim();
            if (joined) return joined;
        }

        if (el.id) {
            try {
                var label = ownerDocument.querySelector('label[for="' + cssEscape(el.id) + '"]');
                if (label) {
                    var labelText = (label.textContent || '').trim();
                    if (labelText) return labelText;
                }
            } catch { /* fallthrough */ }
        }

        var alt = el.getAttribute('alt');
        if (alt) return alt.trim();

        var title = el.getAttribute('title');
        if (title) return title.trim();

        var placeholder = el.getAttribute('placeholder');
        if (placeholder) return placeholder.trim();

        var value = el.value;
        if (typeof value === 'string' && value) return value.trim();

        var text = (el.textContent || '').replace(/\s+/g, ' ').trim();
        if (text.length > 200) text = text.slice(0, 200) + '...';
        return text;
    }

    function cssEscape(value) {
        if (typeof CSS !== 'undefined' && typeof CSS.escape === 'function') {
            return CSS.escape(value);
        }
        return String(value).replace(/[^a-zA-Z0-9_-]/g, function (c) {
            return '\\' + c;
        });
    }

    function isElementVisible(el) {
        if (!el || !el.getBoundingClientRect) return false;
        var rect = el.getBoundingClientRect();
        if (rect.width === 0 && rect.height === 0) return false;
        var style = getElementStyle(el);
        if (style && (style.display === 'none' || style.visibility === 'hidden' || style.opacity === '0')) return false;
        return true;
    }

    // Read through the element's own window, which is a frame's window for an element inside a frame.
    function getElementStyle(el) {
        var view = el.ownerDocument && el.ownerDocument.defaultView;
        if (!view || typeof view.getComputedStyle !== 'function') {
            return null;
        }
        return view.getComputedStyle(el);
    }

    function describeRect(el) {
        if (!el || !el.getBoundingClientRect) {
            return null;
        }
        var rect = el.getBoundingClientRect();
        return {
            x: Math.round(rect.left),
            y: Math.round(rect.top),
            width: Math.round(rect.width),
            height: Math.round(rect.height)
        };
    }

    function buildUniqueSelector(el) {
        if (!el || el.nodeType !== 1) return '';

        var ownerDocument = el.ownerDocument;
        if (el === ownerDocument.body) return 'body';
        if (el === ownerDocument.documentElement) return 'html';

        if (el.id) {
            var idSelector = '#' + cssEscape(el.id);
            try {
                if (ownerDocument.querySelectorAll(idSelector).length === 1) {
                    return idSelector;
                }
            } catch { /* fallthrough */ }
        }

        var parts = [];
        var current = el;
        while (current && current.nodeType === 1 && current !== ownerDocument.body && current !== ownerDocument.documentElement) {
            var part = current.tagName.toLowerCase();
            if (current.id) {
                part = part + '#' + cssEscape(current.id);
                parts.unshift(part);
                break;
            }

            var parent = current.parentElement;
            if (parent) {
                var sameTag = [];
                for (var i = 0; i < parent.children.length; i++) {
                    if (parent.children[i].tagName === current.tagName) {
                        sameTag.push(parent.children[i]);
                    }
                }
                if (sameTag.length > 1) {
                    var index = sameTag.indexOf(current) + 1;
                    part = part + ':nth-of-type(' + index + ')';
                }
            }

            parts.unshift(part);
            current = parent;
        }

        if (current === ownerDocument.body) {
            parts.unshift('body');
        }

        return parts.join(' > ');
    }

    function describeElement(el, options) {
        options = options || {};
        var attributes = {};
        if (el.attributes) {
            for (var i = 0; i < el.attributes.length; i++) {
                attributes[el.attributes[i].name] = el.attributes[i].value;
            }
        }

        var description = {
            tag: el.tagName ? el.tagName.toLowerCase() : '',
            selector: buildUniqueSelector(el),
            role: getElementRole(el),
            accessibleName: getAccessibleName(el),
            attributes: attributes,
            visible: isElementVisible(el),
            rect: describeRect(el)
        };

        if (options.includeComputedStyles) {
            description.computedStyles = curatedComputedStyles(el);
        }

        if (options.includeChildPreview) {
            var preview = [];
            var limit = options.childPreviewLimit > 0 ? options.childPreviewLimit : 5;
            var children = el.children || [];
            for (var j = 0; j < children.length && j < limit; j++) {
                preview.push({
                    tag: children[j].tagName ? children[j].tagName.toLowerCase() : '',
                    selector: buildUniqueSelector(children[j]),
                    accessibleName: getAccessibleName(children[j])
                });
            }
            description.children = {
                count: children.length,
                preview: preview
            };
        }

        return description;
    }

    function curatedComputedStyles(el) {
        var style = getElementStyle(el);
        if (!style) return {};
        var keys = [
            'display', 'visibility', 'opacity', 'position',
            'width', 'height', 'color', 'background-color',
            'font-family', 'font-size', 'font-weight',
            'overflow', 'z-index'
        ];
        var result = {};
        for (var i = 0; i < keys.length; i++) {
            try {
                result[keys[i]] = style.getPropertyValue(keys[i]);
            } catch { /* skip */ }
        }
        return result;
    }

    // Returns the document or subtree HTML with script/style bodies redacted
    // and depth bounded so the agent context budget is preserved.
    function serialiseHtml(root, maxDepth) {
        if (!root) return '';
        var clone = root.cloneNode(true);
        if (clone.nodeType === 1) {
            redactSensitiveContent(clone);
            if (typeof maxDepth === 'number' && maxDepth >= 0) {
                pruneDepth(clone, maxDepth);
            }
        }
        var serialiser = (typeof XMLSerializer !== 'undefined') ? new XMLSerializer() : null;
        var html = clone.outerHTML || (serialiser ? serialiser.serializeToString(clone) : '');
        return html.replace(/\s+/g, ' ').replace(/> </g, '><');
    }

    function redactSensitiveContent(root) {
        var heavy = root.querySelectorAll ? root.querySelectorAll('script, style') : [];
        for (var i = 0; i < heavy.length; i++) {
            var el = heavy[i];
            var bytes = (el.textContent || '').length;
            if (bytes > 0) {
                el.textContent = '[omitted ' + bytes + ' bytes]';
            }
        }
    }

    function pruneDepth(el, maxDepth) {
        if (maxDepth <= 0) {
            if (el.children && el.children.length > 0) {
                while (el.firstChild) el.removeChild(el.firstChild);
                el.appendChild(el.ownerDocument.createComment('truncated children'));
            }
            return;
        }
        if (!el.children) return;
        for (var i = 0; i < el.children.length; i++) {
            pruneDepth(el.children[i], maxDepth - 1);
        }
    }

    // Frames. A call acts on the page itself or on one frame inside it, and its result names that frame.
    // The shim reads a frame's document directly. A frame's console and network entries come from the shim
    // running in that frame.

    // Thrown while the frame a call acts on is still loading its page. The host waits and calls again.
    function FrameLoading(frameName) {
        this.frameName = frameName;
        this.message = "the frame '" + frameName + "' is still loading its page";
    }

    function topTarget() {
        return {
            name: TOP_FRAME,
            document: document,
            window: window,
            element: null
        };
    }

    function isFrameElement(el) {
        return el.localName === 'iframe' || el.localName === 'frame';
    }

    // Null when the frame shows a page from another origin, whose document cannot be reached from here.
    function frameTarget(frameElement) {
        var frameDocument;
        try {
            frameDocument = frameElement.contentDocument;
        } catch {
            frameDocument = null;
        }

        if (!frameDocument || !frameDocument.defaultView) {
            return null;
        }

        return {
            name: buildUniqueSelector(frameElement),
            document: frameDocument,
            window: frameDocument.defaultView,
            element: frameElement
        };
    }

    // A frame is loading while the page marks it busy, while a reload the shim started has not replaced its
    // page, and until its document has finished loading.
    function isFrameLoading(target) {
        return target.element.getAttribute('aria-busy') === 'true'
            || target.window[reloadingKey] === true
            || target.document.readyState !== 'complete';
    }

    // Resolves the frame a call acts on. An empty name means the content frame the page marks, or the page
    // itself when it marks none or the marked frame cannot be reached. "top" names the page itself. Any other
    // name is a CSS selector for a frame element in the page. Throws FrameLoading while the frame is still
    // loading, unless the caller accepts a loading frame.
    function resolveTarget(frameName, acceptLoading) {
        var target;
        if (!frameName) {
            var contentFrame = document.querySelector(CONTENT_FRAME_SELECTOR);
            target = contentFrame && isFrameElement(contentFrame) ? frameTarget(contentFrame) : null;
            if (!target) {
                return topTarget();
            }
        } else if (frameName === TOP_FRAME) {
            return topTarget();
        } else {
            var frameElement;
            try {
                frameElement = document.querySelector(frameName);
            } catch (e) {
                throw new Error('invalid frame selector: ' + (e && e.message));
            }
            if (!frameElement) {
                throw new Error("no frame matches '" + frameName + "'");
            }
            if (!isFrameElement(frameElement)) {
                throw new Error("the element matched by frame '" + frameName + "' is a <" + frameElement.localName + '>, not a frame');
            }
            target = frameTarget(frameElement);
            if (!target) {
                throw new Error("the frame '" + frameName + "' shows a page from another origin, which the tools cannot reach");
            }
        }

        if (!acceptLoading && isFrameLoading(target)) {
            throw new FrameLoading(target.name);
        }

        return target;
    }

    // The frame's viewport as a box in the page's viewport: the frame element's box inside its border and
    // padding.
    function getFrameViewportBox(frameElement) {
        var rect = frameElement.getBoundingClientRect();
        var style = getElementStyle(frameElement);
        var paddingLeft = style ? parseFloat(style.paddingLeft) || 0 : 0;
        var paddingTop = style ? parseFloat(style.paddingTop) || 0 : 0;
        var paddingRight = style ? parseFloat(style.paddingRight) || 0 : 0;
        var paddingBottom = style ? parseFloat(style.paddingBottom) || 0 : 0;
        return {
            x: rect.left + frameElement.clientLeft + paddingLeft,
            y: rect.top + frameElement.clientTop + paddingTop,
            width: Math.max(0, frameElement.clientWidth - paddingLeft - paddingRight),
            height: Math.max(0, frameElement.clientHeight - paddingTop - paddingBottom)
        };
    }

    // Tags each entry with the frame it came from.
    function tagWithFrame(entries, frameName) {
        for (var i = 0; i < entries.length; i++) {
            entries[i].frame = frameName;
        }
        return entries;
    }

    // Drains the buffers of the page and of each frame in it that runs this shim. A frame keeps its own
    // buffers, and they are lost when the frame loads another page.
    function drainPageAndFrames(drainMethodName) {
        var entries = tagWithFrame(bridge[drainMethodName](), TOP_FRAME);

        var frameElements = document.querySelectorAll('iframe, frame');
        for (var i = 0; i < frameElements.length; i++) {
            var frameBridge;
            try {
                frameBridge = frameElements[i].contentWindow && frameElements[i].contentWindow[bridgeKey];
            } catch {
                // A frame from another origin cannot be read.
                frameBridge = null;
            }

            if (frameBridge && typeof frameBridge[drainMethodName] === 'function') {
                var frameEntries = frameBridge[drainMethodName]();
                entries = entries.concat(tagWithFrame(frameEntries, buildUniqueSelector(frameElements[i])));
            }
        }

        return entries;
    }

    function takeConsoleEntries() {
        var entries = consoleBuffer.slice();
        consoleBuffer.length = 0;
        return entries;
    }

    // Handler implementations.

    handlers.flushConsole = function () {
        return drainPageAndFrames('drainConsole');
    };

    handlers.resolveFrame = function (args) {
        args = args || {};
        var target = resolveTarget(args.frame, false);
        return {
            frame: target.name,
            top: target.element === null
        };
    };

    // Evaluates an expression in the frame's own global scope and returns its value as JSON. An expression
    // that throws or does not parse gives null, as it does when the host evaluates in the page itself. An
    // EvalError is reported instead, because it means the frame's Content-Security-Policy forbids evaluation.
    handlers.evaluate = function (args) {
        args = args || {};
        var target = resolveTarget(args.frame, false);

        var value;
        try {
            value = target.window.eval(args.expression);
        } catch (e) {
            if (e instanceof target.window.EvalError) {
                throw new Error("the frame '" + target.name + "' does not allow evaluating JavaScript: " + e.message);
            }
            value = null;
        }

        var valueJson;
        try {
            valueJson = JSON.stringify(value);
        } catch (e) {
            throw new Error('the value cannot be converted to JSON: ' + (e && e.message));
        }

        return {
            frame: target.name,
            valueJson: valueJson === undefined ? 'null' : valueJson
        };
    };

    // Reloads a frame's page. The host reloads the page itself, so for the top frame this only names it.
    handlers.reload = function (args) {
        args = args || {};
        var target = resolveTarget(args.frame, true);
        if (target.element) {
            target.window[reloadingKey] = true;
            target.window.location.reload();
        }
        return {
            frame: target.name,
            top: target.element === null
        };
    };

    // Bounded ring buffer for fetch and XMLHttpRequest activity. Same eviction
    // pattern as the console buffer. Drained by the host through flushNetwork.
    var NETWORK_BUFFER_LIMIT = 500;
    var NETWORK_BODY_LIMIT = 16384;
    var networkBuffer = [];
    var networkSequence = 0;

    function pushNetworkEntry(entry) {
        networkBuffer.push(entry);
        if (networkBuffer.length > NETWORK_BUFFER_LIMIT) {
            networkBuffer.splice(0, networkBuffer.length - NETWORK_BUFFER_LIMIT);
        }
    }

    function truncateBody(text) {
        if (typeof text !== 'string') {
            return null;
        }
        if (text.length <= NETWORK_BODY_LIMIT) {
            return { text: text, truncatedBytes: 0 };
        }
        return {
            text: text.slice(0, NETWORK_BODY_LIMIT),
            truncatedBytes: text.length - NETWORK_BODY_LIMIT
        };
    }

    function headersToObject(headers) {
        var result = {};
        if (!headers) return result;
        try {
            if (typeof headers.forEach === 'function') {
                headers.forEach(function (value, key) { result[key] = value; });
                return result;
            }
            if (typeof headers === 'string') {
                var lines = headers.split(/\r?\n/);
                for (var i = 0; i < lines.length; i++) {
                    var line = lines[i];
                    var idx = line.indexOf(':');
                    if (idx > 0) {
                        var name = line.slice(0, idx).trim();
                        var value = line.slice(idx + 1).trim();
                        if (name) result[name] = value;
                    }
                }
                return result;
            }
            if (typeof headers === 'object') {
                for (var key in headers) {
                    if (Object.prototype.hasOwnProperty.call(headers, key)) {
                        result[key] = headers[key];
                    }
                }
            }
        } catch { /* swallow */ }
        return result;
    }

    function describeRequestBody(body) {
        if (body === null || body === undefined) return null;
        try {
            if (typeof body === 'string') return body;
            if (body instanceof URLSearchParams) return body.toString();
            if (body instanceof FormData) return '[FormData]';
            if (body instanceof Blob) return '[Blob ' + body.size + ' bytes]';
            if (body instanceof ArrayBuffer) return '[ArrayBuffer ' + body.byteLength + ' bytes]';
            return '[' + (body.constructor && body.constructor.name ? body.constructor.name : 'body') + ']';
        } catch {
            return '[unreadable body]';
        }
    }

    if (typeof window !== 'undefined' && typeof window.fetch === 'function' && !window.fetch.__celWrapped) {
        var originalFetch = window.fetch.bind(window);
        var wrappedFetch = function (input, init) {
            var id = ++networkSequence;
            var startTime = Date.now();
            var perfStart = (typeof performance !== 'undefined' && performance.now) ? performance.now() : startTime;
            var url;
            var method;
            var requestHeaders;
            var requestBodyDescription = null;
            try {
                if (typeof Request !== 'undefined' && input instanceof Request) {
                    url = input.url;
                    method = (init && init.method) || input.method || 'GET';
                    requestHeaders = headersToObject(input.headers);
                } else {
                    url = String(input);
                    method = (init && init.method) || 'GET';
                    requestHeaders = init ? headersToObject(init.headers) : {};
                }
                if (init && init.body !== undefined) {
                    requestBodyDescription = describeRequestBody(init.body);
                }
            } catch {
                url = String(input);
                method = 'GET';
                requestHeaders = {};
            }

            return originalFetch(input, init).then(function (response) {
                var duration = ((typeof performance !== 'undefined' && performance.now) ? performance.now() : Date.now()) - perfStart;
                var clone;
                try { clone = response.clone(); } catch { clone = null; }
                var responseHeaders = headersToObject(response.headers);
                var contentLengthHeader = responseHeaders['content-length'] || responseHeaders['Content-Length'];
                var responseSize = contentLengthHeader ? Number(contentLengthHeader) || 0 : 0;

                var entry = {
                    id: id,
                    type: 'fetch',
                    method: (method || 'GET').toUpperCase(),
                    url: url,
                    status: response.status,
                    startTimeMs: startTime,
                    durationMs: Math.round(duration),
                    requestSize: requestBodyDescription && typeof requestBodyDescription === 'string' ? requestBodyDescription.length : 0,
                    responseSize: responseSize,
                    requestHeaders: requestHeaders,
                    responseHeaders: responseHeaders,
                    requestBodyDescription: requestBodyDescription
                };

                if (clone) {
                    clone.text().then(function (text) {
                        entry.responseBody = truncateBody(text);
                        if (!entry.responseSize) entry.responseSize = text.length;
                        pushNetworkEntry(entry);
                    }, function () {
                        pushNetworkEntry(entry);
                    });
                } else {
                    pushNetworkEntry(entry);
                }
                return response;
            }, function (error) {
                var duration = ((typeof performance !== 'undefined' && performance.now) ? performance.now() : Date.now()) - perfStart;
                pushNetworkEntry({
                    id: id,
                    type: 'fetch',
                    method: (method || 'GET').toUpperCase(),
                    url: url,
                    status: 0,
                    startTimeMs: startTime,
                    durationMs: Math.round(duration),
                    requestSize: 0,
                    responseSize: 0,
                    requestHeaders: requestHeaders,
                    requestBodyDescription: requestBodyDescription,
                    error: (error && (error.message || String(error))) || 'fetch failed'
                });
                throw error;
            });
        };
        try {
            Object.defineProperty(wrappedFetch, '__celWrapped', { value: true, enumerable: false });
        } catch {
            wrappedFetch.__celWrapped = true;
        }
        window.fetch = wrappedFetch;
    }

    if (typeof XMLHttpRequest !== 'undefined' && !XMLHttpRequest.prototype.__celWrapped) {
        var originalOpen = XMLHttpRequest.prototype.open;
        var originalSend = XMLHttpRequest.prototype.send;
        var originalSetRequestHeader = XMLHttpRequest.prototype.setRequestHeader;

        XMLHttpRequest.prototype.open = function (method, url) {
            this.__celNetMeta = {
                id: ++networkSequence,
                method: (method || 'GET').toUpperCase(),
                url: String(url),
                requestHeaders: {},
                requestBodyDescription: null
            };
            return originalOpen.apply(this, arguments);
        };

        XMLHttpRequest.prototype.setRequestHeader = function (name, value) {
            if (this.__celNetMeta) {
                this.__celNetMeta.requestHeaders[name] = String(value);
            }
            return originalSetRequestHeader.apply(this, arguments);
        };

        XMLHttpRequest.prototype.send = function (body) {
            var meta = this.__celNetMeta;
            if (meta) {
                meta.startTimeMs = Date.now();
                meta.perfStart = (typeof performance !== 'undefined' && performance.now) ? performance.now() : meta.startTimeMs;
                if (body !== undefined && body !== null) {
                    meta.requestBodyDescription = describeRequestBody(body);
                }

                var xhr = this;
                var finalize = function () {
                    if (meta.completed) return;
                    meta.completed = true;
                    var duration = ((typeof performance !== 'undefined' && performance.now) ? performance.now() : Date.now()) - meta.perfStart;
                    var entry = {
                        id: meta.id,
                        type: 'xhr',
                        method: meta.method,
                        url: meta.url,
                        status: xhr.status || 0,
                        startTimeMs: meta.startTimeMs,
                        durationMs: Math.round(duration),
                        requestSize: meta.requestBodyDescription && typeof meta.requestBodyDescription === 'string' ? meta.requestBodyDescription.length : 0,
                        responseSize: 0,
                        requestHeaders: meta.requestHeaders,
                        responseHeaders: headersToObject(xhr.getAllResponseHeaders ? xhr.getAllResponseHeaders() : ''),
                        requestBodyDescription: meta.requestBodyDescription
                    };
                    try {
                        var responseType = xhr.responseType;
                        if (!responseType || responseType === 'text' || responseType === '') {
                            var text = xhr.responseText;
                            if (typeof text === 'string') {
                                entry.responseBody = truncateBody(text);
                                entry.responseSize = text.length;
                            }
                        } else {
                            entry.responseBody = { text: '[' + responseType + ']', truncatedBytes: 0 };
                        }
                    } catch { /* swallow */ }
                    pushNetworkEntry(entry);
                };

                xhr.addEventListener('loadend', finalize);
                xhr.addEventListener('error', function () {
                    if (meta.completed) return;
                    meta.completed = true;
                    pushNetworkEntry({
                        id: meta.id,
                        type: 'xhr',
                        method: meta.method,
                        url: meta.url,
                        status: 0,
                        startTimeMs: meta.startTimeMs,
                        durationMs: Math.round(((typeof performance !== 'undefined' && performance.now) ? performance.now() : Date.now()) - meta.perfStart),
                        requestSize: 0,
                        responseSize: 0,
                        requestHeaders: meta.requestHeaders,
                        requestBodyDescription: meta.requestBodyDescription,
                        error: 'xhr error'
                    });
                });
            }
            return originalSend.apply(this, arguments);
        };

        try {
            Object.defineProperty(XMLHttpRequest.prototype, '__celWrapped', { value: true, enumerable: false });
        } catch {
            XMLHttpRequest.prototype.__celWrapped = true;
        }
    }

    function takeNetworkEntries() {
        var entries = networkBuffer.slice();
        networkBuffer.length = 0;
        return entries;
    }

    handlers.flushNetwork = function () {
        return drainPageAndFrames('drainNetwork');
    };

    handlers.getHtml = function (args) {
        args = args || {};
        var target = resolveTarget(args.frame, false);
        var maxDepth = typeof args.maxDepth === 'number' ? args.maxDepth : 8;
        var selector = args.selector;
        var root;
        if (selector) {
            try {
                root = target.document.querySelector(selector);
            } catch (e) {
                throw new Error('invalid selector: ' + (e && e.message));
            }
            if (!root) {
                throw new Error("no element matches selector '" + selector + "'");
            }
        } else {
            root = target.document.documentElement;
        }
        return {
            frame: target.name,
            selector: selector || null,
            html: serialiseHtml(root, maxDepth)
        };
    };

    handlers.query = function (args) {
        args = args || {};
        var target = resolveTarget(args.frame, false);
        var role = args.role;
        var name = args.name;
        var text = args.text;
        var selector = args.selector;
        var maxResults = args.maxResults > 0 ? args.maxResults : 20;

        var modes = [role, text, selector].filter(function (v) { return typeof v === 'string' && v.length > 0; });
        if (modes.length !== 1) {
            throw new Error("query requires exactly one of 'role', 'text', or 'selector'");
        }

        var matches;
        if (selector) {
            try {
                matches = Array.prototype.slice.call(target.document.querySelectorAll(selector));
            } catch (e) {
                throw new Error('invalid selector: ' + (e && e.message));
            }
        } else if (role) {
            matches = matchByRoleAndName(target.document, role, name);
        } else {
            matches = matchByText(target.document, text);
        }

        var results = [];
        for (var i = 0; i < matches.length && i < maxResults; i++) {
            results.push(describeElement(matches[i], {}));
        }
        return {
            frame: target.name,
            mode: selector ? 'selector' : (role ? 'role' : 'text'),
            totalMatches: matches.length,
            returned: results.length,
            elements: results
        };
    };

    function matchByRoleAndName(targetDocument, role, name) {
        var candidates = targetDocument.querySelectorAll('*');
        var roleLower = role.toLowerCase();
        var nameLower = typeof name === 'string' ? name.toLowerCase() : null;
        var results = [];
        for (var i = 0; i < candidates.length; i++) {
            var el = candidates[i];
            var elementRole = getElementRole(el);
            if (elementRole !== roleLower) continue;
            if (nameLower !== null && nameLower !== undefined) {
                var actualName = (getAccessibleName(el) || '').toLowerCase();
                if (actualName.indexOf(nameLower) === -1) continue;
            }
            results.push(el);
        }
        return results;
    }

    function matchByText(targetDocument, text) {
        if (typeof text !== 'string' || text.length === 0) return [];
        var lower = text.toLowerCase();
        var iterator = targetDocument.createTreeWalker
            ? targetDocument.createTreeWalker(targetDocument.body || targetDocument.documentElement, NodeFilter.SHOW_ELEMENT, null)
            : null;
        var results = [];
        if (!iterator) return results;
        var node;
        while ((node = iterator.nextNode())) {
            if (!node.children || node.children.length > 0) continue;
            var content = (node.textContent || '').replace(/\s+/g, ' ').trim().toLowerCase();
            if (content && content.indexOf(lower) !== -1) {
                results.push(node);
            }
        }
        return results;
    }

    handlers.click = function (args) {
        args = args || {};
        var target = resolveTarget(args.frame, false);
        var selector = args.selector;
        if (typeof selector !== 'string' || selector.length === 0) {
            throw new Error("click requires a non-empty 'selector'");
        }
        var element;
        try {
            element = target.document.querySelector(selector);
        } catch (e) {
            throw new Error('invalid selector: ' + (e && e.message));
        }
        if (!element) {
            throw new Error("no element matches selector '" + selector + "'");
        }
        if (element.disabled) {
            throw new Error("element matched by selector '" + selector + "' is disabled");
        }

        var rect = element.getBoundingClientRect ? element.getBoundingClientRect() : null;
        var clientX = rect ? Math.round(rect.left + rect.width / 2) : 0;
        var clientY = rect ? Math.round(rect.top + rect.height / 2) : 0;

        // The events come from the frame's own window, so a page that checks an event's type sees its own
        // MouseEvent.
        var view = target.window;
        var visible = isElementVisible(element);
        var phases = ['mousedown', 'mouseup', 'click'];
        for (var i = 0; i < phases.length; i++) {
            var ev;
            try {
                ev = new view.MouseEvent(phases[i], {
                    bubbles: true,
                    cancelable: true,
                    view: view,
                    clientX: clientX,
                    clientY: clientY,
                    button: 0
                });
            } catch {
                ev = target.document.createEvent ? target.document.createEvent('MouseEvents') : null;
                if (ev && typeof ev.initMouseEvent === 'function') {
                    ev.initMouseEvent(phases[i], true, true, view, 0, 0, 0, clientX, clientY, false, false, false, false, 0, null);
                }
            }
            if (ev) element.dispatchEvent(ev);
        }

        return {
            frame: target.name,
            selector: selector,
            tag: element.tagName ? element.tagName.toLowerCase() : '',
            visible: visible,
            rect: describeRect(element),
            isTrusted: false
        };
    };

    handlers.fill = function (args) {
        args = args || {};
        var target = resolveTarget(args.frame, false);
        var selector = args.selector;
        if (typeof selector !== 'string' || selector.length === 0) {
            throw new Error("fill requires a non-empty 'selector'");
        }
        if (typeof args.value !== 'string') {
            throw new Error("fill requires a string 'value'");
        }
        var element;
        try {
            element = target.document.querySelector(selector);
        } catch (e) {
            throw new Error('invalid selector: ' + (e && e.message));
        }
        if (!element) {
            throw new Error("no element matches selector '" + selector + "'");
        }

        var tagName = (element.tagName || '').toLowerCase();
        var isFormControl = tagName === 'input' || tagName === 'textarea' || tagName === 'select';
        var isContentEditable = element.isContentEditable === true;
        if (!isFormControl && !isContentEditable) {
            throw new Error("element matched by selector '" + selector + "' is not a fillable input, textarea, select, or contenteditable element");
        }

        // Uses the frame's own window for the value setter and the events, as the page's own code would.
        var view = target.window;
        if (isFormControl) {
            try {
                var nativeSetter = null;
                if (tagName === 'textarea') {
                    var textareaProto = view.HTMLTextAreaElement && view.HTMLTextAreaElement.prototype;
                    var textareaDescriptor = textareaProto ? Object.getOwnPropertyDescriptor(textareaProto, 'value') : null;
                    nativeSetter = textareaDescriptor && textareaDescriptor.set;
                } else if (tagName === 'input') {
                    var inputProto = view.HTMLInputElement && view.HTMLInputElement.prototype;
                    var inputDescriptor = inputProto ? Object.getOwnPropertyDescriptor(inputProto, 'value') : null;
                    nativeSetter = inputDescriptor && inputDescriptor.set;
                } else if (tagName === 'select') {
                    var selectProto = view.HTMLSelectElement && view.HTMLSelectElement.prototype;
                    var selectDescriptor = selectProto ? Object.getOwnPropertyDescriptor(selectProto, 'value') : null;
                    nativeSetter = selectDescriptor && selectDescriptor.set;
                }
                if (nativeSetter) {
                    nativeSetter.call(element, args.value);
                } else {
                    element.value = args.value;
                }
            } catch {
                element.value = args.value;
            }
        } else {
            element.textContent = args.value;
        }

        var inputEvent;
        var changeEvent;
        try {
            inputEvent = new view.Event('input', { bubbles: true });
        } catch {
            inputEvent = target.document.createEvent ? target.document.createEvent('Event') : null;
            if (inputEvent && typeof inputEvent.initEvent === 'function') inputEvent.initEvent('input', true, true);
        }
        try {
            changeEvent = new view.Event('change', { bubbles: true });
        } catch {
            changeEvent = target.document.createEvent ? target.document.createEvent('Event') : null;
            if (changeEvent && typeof changeEvent.initEvent === 'function') changeEvent.initEvent('change', true, true);
        }
        if (inputEvent) element.dispatchEvent(inputEvent);
        if (changeEvent) element.dispatchEvent(changeEvent);

        return {
            frame: target.name,
            selector: selector,
            tag: tagName,
            value: isFormControl ? (typeof element.value === 'string' ? element.value : '') : (element.textContent || '')
        };
    };

    // The area a screenshot of the frame captures, as a box in the page's viewport.
    handlers.getViewport = function (args) {
        args = args || {};
        var target = resolveTarget(args.frame, false);
        var ratio = (typeof window !== 'undefined' && window.devicePixelRatio) ? window.devicePixelRatio : 1;

        if (!target.element) {
            var width = (typeof window !== 'undefined' ? (window.innerWidth || 0) : 0);
            var height = (typeof window !== 'undefined' ? (window.innerHeight || 0) : 0);
            return { frame: target.name, x: 0, y: 0, width: width, height: height, devicePixelRatio: ratio };
        }

        var box = getFrameViewportBox(target.element);
        if (box.width === 0 || box.height === 0) {
            throw new Error("the frame '" + target.name + "' is not showing, so there is nothing on screen to capture");
        }
        return { frame: target.name, x: box.x, y: box.y, width: box.width, height: box.height, devicePixelRatio: ratio };
    };

    // The area a screenshot of one element captures, as a box in the page's viewport. For an element in a
    // frame, only the part inside the frame's viewport is on screen.
    handlers.getRect = function (args) {
        args = args || {};
        var target = resolveTarget(args.frame, false);
        var selector = args.selector;
        if (typeof selector !== 'string' || selector.length === 0) {
            throw new Error("getRect requires a non-empty 'selector'");
        }
        var element;
        try {
            element = target.document.querySelector(selector);
        } catch (e) {
            throw new Error('invalid selector: ' + (e && e.message));
        }
        if (!element) {
            throw new Error("no element matches selector '" + selector + "'");
        }
        var rect = element.getBoundingClientRect();
        if (!target.element) {
            return {
                frame: target.name,
                x: rect.left,
                y: rect.top,
                width: rect.width,
                height: rect.height
            };
        }

        var box = getFrameViewportBox(target.element);
        var left = Math.max(box.x, box.x + rect.left);
        var top = Math.max(box.y, box.y + rect.top);
        var right = Math.min(box.x + box.width, box.x + rect.right);
        var bottom = Math.min(box.y + box.height, box.y + rect.bottom);
        if (right <= left || bottom <= top) {
            throw new Error("the element matched by selector '" + selector + "' is outside the visible part of the frame '" + target.name + "'");
        }
        return {
            frame: target.name,
            x: left,
            y: top,
            width: right - left,
            height: bottom - top
        };
    };

    handlers.inspect = function (args) {
        args = args || {};
        var target = resolveTarget(args.frame, false);
        var selector = args.selector;
        if (typeof selector !== 'string' || selector.length === 0) {
            throw new Error("inspect requires a non-empty 'selector'");
        }
        var element;
        try {
            element = target.document.querySelector(selector);
        } catch (e) {
            throw new Error('invalid selector: ' + (e && e.message));
        }
        if (!element) {
            throw new Error("no element matches selector '" + selector + "'");
        }
        var description = describeElement(element, {
            includeComputedStyles: true,
            includeChildPreview: true,
            childPreviewLimit: args.childPreviewLimit
        });
        return Object.assign({ frame: target.name }, description);
    };

    var bridge = {
        registerHandler: function (name, handler) {
            handlers[name] = handler;
        },
        getHandler: function (name) {
            return handlers[name] || null;
        },
        // Empty this shim's own buffers. The shim in the page calls these on the shims in its frames.
        drainConsole: takeConsoleEntries,
        drainNetwork: takeNetworkEntries,
        invoke: function (name, argsJson) {
            try {
                var handler = handlers[name];
                if (!handler) {
                    return { ok: false, error: "no handler registered for '" + name + "'" };
                }
                var parsed;
                if (typeof argsJson === 'string' && argsJson.length > 0) {
                    parsed = JSON.parse(argsJson);
                } else if (argsJson && typeof argsJson === 'object') {
                    parsed = argsJson;
                } else {
                    parsed = {};
                }
                var value = handler(parsed);
                return { ok: true, value: value === undefined ? null : value };
            } catch (e) {
                if (e instanceof FrameLoading) {
                    return { ok: false, pending: true, frame: e.frameName, error: e.message };
                }
                return { ok: false, error: (e && (e.message || String(e))) || 'handler threw' };
            }
        }
    };

    Object.defineProperty(globalThis, bridgeKey, {
        value: bridge,
        writable: false,
        configurable: false,
        enumerable: false
    });
})();
