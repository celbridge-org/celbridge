// The canvas the image is shown on: zoom, pan, scrollbars, the checkerboard under transparent pixels, and an
// overlay hook the crop tool draws into. The view works in CSS pixels and scales its backing store by the device
// pixel ratio, so the preview stays sharp on any display.

export const MIN_ZOOM = 0.01;
export const MAX_ZOOM = 32;

// From this zoom up, pixels are drawn as crisp squares.
const PIXELATED_ZOOM = 4;
// The gap kept around the image when it is fitted, and the most the view scrolls past each edge of it.
const FIT_MARGIN = 16;
const MIN_THUMB = 24;
const CHECKER_SIZE = 8;
const ZOOM_STEPS = [
    0.01, 0.02, 0.03, 0.05, 0.08, 0.12, 0.17, 0.25, 0.33, 0.5, 0.67, 0.75, 1, 1.5, 2, 3, 4, 6, 8, 12, 16, 24, 32,
];

export function createImageView({ host, canvas, onChanged, onPointerImage }) {
    const context = canvas.getContext('2d');

    let image = null;
    let imageWidth = 0;
    let imageHeight = 0;
    let zoom = 1;
    let centerX = 0;
    let centerY = 0;
    let fitting = true;
    let cssWidth = 0;
    let cssHeight = 0;
    let pixelRatio = window.devicePixelRatio || 1;
    let frame = 0;
    let overlay = null;

    let spaceHeld = false;
    let pan = null;
    let tool = null;

    const scrollbars = { x: createScrollbar('x'), y: createScrollbar('y') };

    function measure() {
        const rect = host.getBoundingClientRect();
        pixelRatio = window.devicePixelRatio || 1;
        cssWidth = rect.width;
        cssHeight = rect.height;
        canvas.width = Math.max(1, Math.round(cssWidth * pixelRatio));
        canvas.height = Math.max(1, Math.round(cssHeight * pixelRatio));
        canvas.style.width = `${cssWidth}px`;
        canvas.style.height = `${cssHeight}px`;
        if (fitting) {
            applyFit();
        } else {
            clampCenter();
        }
        render();
    }

    function fitZoom() {
        if (!imageWidth || !imageHeight || !cssWidth || !cssHeight) {
            return 1;
        }
        // Small images are zoomed in as well as large ones zoomed out, so every image opens filling the view.
        const available = Math.min((cssWidth - FIT_MARGIN * 2) / imageWidth, (cssHeight - FIT_MARGIN * 2) / imageHeight);
        return clampZoom(available);
    }

    function clampZoom(value) {
        return Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, value));
    }

    function applyFit() {
        zoom = fitZoom();
        centerX = imageWidth / 2;
        centerY = imageHeight / 2;
    }

    // On an axis where the image fits the view it stays centred. Where it is larger, it scrolls only until its
    // edge reaches the edge of the view, so however the view is panned, zoomed or resized, the image is on
    // screen.
    function clampAxis(center, length, view) {
        if (length * zoom + FIT_MARGIN * 2 <= view) {
            return length / 2;
        }
        const min = (view / 2 - FIT_MARGIN) / zoom;
        return Math.min(length - min, Math.max(min, center));
    }

    function clampCenter() {
        centerX = clampAxis(centerX, imageWidth, cssWidth);
        centerY = clampAxis(centerY, imageHeight, cssHeight);
    }

    // Moves the view by a screen distance. Reports whether it moved, so a scroll against a fitted image does
    // not count as the user taking over the zoom.
    function panBy(dx, dy) {
        const beforeX = centerX;
        const beforeY = centerY;
        centerX += dx / zoom;
        centerY += dy / zoom;
        clampCenter();
        const moved = centerX !== beforeX || centerY !== beforeY;
        if (moved) {
            fitting = false;
            render();
            onChanged?.();
        }
        return moved;
    }

    // One axis as a scrolled document: the image plus a margin each side, the part of it on screen, and how
    // far the view is from its start. All in CSS pixels.
    function axisMetrics(axis) {
        const view = axis === 'x' ? cssWidth : cssHeight;
        const length = axis === 'x' ? imageWidth : imageHeight;
        const center = axis === 'x' ? centerX : centerY;
        const total = length * zoom + FIT_MARGIN * 2;
        return { view, total, offset: FIT_MARGIN + center * zoom - view / 2, scrollable: image !== null && total > view };
    }

    function scrollTo(axis, offset) {
        const { view } = axisMetrics(axis);
        const center = (offset + view / 2 - FIT_MARGIN) / zoom;
        panBy(axis === 'x' ? (center - centerX) * zoom : 0, axis === 'y' ? (center - centerY) * zoom : 0);
    }

    function createScrollbar(axis) {
        const track = document.createElement('div');
        track.className = `ie-scrollbar ie-scrollbar-${axis}`;
        track.hidden = true;
        const thumb = document.createElement('div');
        thumb.className = 'ie-scrollbar-thumb';
        track.append(thumb);
        host.append(track);

        const along = (event) => (axis === 'x' ? event.clientX : event.clientY);
        const trackLength = () => (axis === 'x' ? track.clientWidth : track.clientHeight);
        let drag = null;

        thumb.addEventListener('pointerdown', (event) => {
            if (event.button !== 0) {
                return;
            }
            event.preventDefault();
            event.stopPropagation();
            const { offset } = axisMetrics(axis);
            drag = { start: along(event), offset };
            thumb.setPointerCapture(event.pointerId);
            thumb.dataset.dragging = 'true';
        });
        thumb.addEventListener('pointermove', (event) => {
            if (!drag) {
                return;
            }
            const { view, total } = axisMetrics(axis);
            const thumbLength = axis === 'x' ? thumb.offsetWidth : thumb.offsetHeight;
            const travel = Math.max(1, trackLength() - thumbLength);
            scrollTo(axis, drag.offset + ((along(event) - drag.start) * (total - view)) / travel);
        });
        const endDrag = () => {
            drag = null;
            delete thumb.dataset.dragging;
        };
        thumb.addEventListener('pointerup', endDrag);
        thumb.addEventListener('pointercancel', endDrag);

        // A click on the track pages towards it.
        track.addEventListener('pointerdown', (event) => {
            if (event.button !== 0 || event.target !== track) {
                return;
            }
            event.preventDefault();
            const { view, offset } = axisMetrics(axis);
            const thumbRect = thumb.getBoundingClientRect();
            const before = along(event) < (axis === 'x' ? thumbRect.left : thumbRect.top);
            scrollTo(axis, offset + (before ? -1 : 1) * view * 0.9);
        });

        return { track, thumb };
    }

    function updateScrollbars() {
        for (const axis of ['x', 'y']) {
            const { track, thumb } = scrollbars[axis];
            const { view, total, offset, scrollable } = axisMetrics(axis);
            track.hidden = !scrollable;
            if (!scrollable) {
                continue;
            }

            const length = axis === 'x' ? track.clientWidth : track.clientHeight;
            const size = Math.max(MIN_THUMB, (view / total) * length);
            const position = (offset / (total - view)) * (length - size);
            thumb.style[axis === 'x' ? 'width' : 'height'] = `${size}px`;
            thumb.style[axis === 'x' ? 'left' : 'top'] = `${position}px`;
        }

        canvas.dataset.pannable = String(scrollbars.x.track.hidden === false || scrollbars.y.track.hidden === false);
    }

    function imageRect() {
        return {
            x: cssWidth / 2 - centerX * zoom,
            y: cssHeight / 2 - centerY * zoom,
            width: imageWidth * zoom,
            height: imageHeight * zoom,
        };
    }

    function toImage(clientX, clientY) {
        const bounds = canvas.getBoundingClientRect();
        const rect = imageRect();
        return {
            x: (clientX - bounds.left - rect.x) / zoom,
            y: (clientY - bounds.top - rect.y) / zoom,
        };
    }

    function toScreen(x, y) {
        const rect = imageRect();
        return { x: rect.x + x * zoom, y: rect.y + y * zoom };
    }

    function checkerColours() {
        const style = getComputedStyle(host);
        return [
            style.getPropertyValue('--ie-checker-light').trim() || '#ffffff',
            style.getPropertyValue('--ie-checker-dark').trim() || '#d9d9d9',
        ];
    }

    function render() {
        if (frame) {
            return;
        }
        frame = requestAnimationFrame(draw);
    }

    function draw() {
        frame = 0;
        context.setTransform(1, 0, 0, 1, 0, 0);
        context.clearRect(0, 0, canvas.width, canvas.height);
        updateScrollbars();
        if (!image) {
            return;
        }

        context.setTransform(pixelRatio, 0, 0, pixelRatio, 0, 0);
        const rect = imageRect();

        // The checkerboard sits in screen space, so its squares stay one size at every zoom.
        const [light, dark] = checkerColours();
        context.fillStyle = light;
        context.fillRect(rect.x, rect.y, rect.width, rect.height);
        context.save();
        context.beginPath();
        context.rect(rect.x, rect.y, rect.width, rect.height);
        context.clip();
        context.fillStyle = dark;
        const startX = Math.floor(Math.max(0, rect.x) / CHECKER_SIZE) * CHECKER_SIZE;
        const startY = Math.floor(Math.max(0, rect.y) / CHECKER_SIZE) * CHECKER_SIZE;
        const endX = Math.min(cssWidth, rect.x + rect.width);
        const endY = Math.min(cssHeight, rect.y + rect.height);
        for (let y = startY; y < endY; y += CHECKER_SIZE) {
            for (let x = startX + (((y / CHECKER_SIZE) & 1) ? CHECKER_SIZE : 0); x < endX; x += CHECKER_SIZE * 2) {
                context.fillRect(x, y, CHECKER_SIZE, CHECKER_SIZE);
            }
        }
        context.restore();

        context.imageSmoothingEnabled = zoom < PIXELATED_ZOOM;
        context.imageSmoothingQuality = 'high';
        context.drawImage(image, rect.x, rect.y, rect.width, rect.height);

        overlay?.(context, { toScreen, zoom, rect });
    }

    function setZoom(value, anchorX = cssWidth / 2, anchorY = cssHeight / 2) {
        const next = clampZoom(value);
        // Keep the image point under the anchor where it is.
        const rect = imageRect();
        const pointX = (anchorX - rect.x) / zoom;
        const pointY = (anchorY - rect.y) / zoom;
        zoom = next;
        centerX = pointX - (anchorX - cssWidth / 2) / zoom;
        centerY = pointY - (anchorY - cssHeight / 2) / zoom;
        fitting = false;
        clampCenter();
        render();
        onChanged?.();
    }

    function step(direction) {
        const steps = direction > 0 ? ZOOM_STEPS : [...ZOOM_STEPS].reverse();
        const next = steps.find((value) => (direction > 0 ? value > zoom * 1.001 : value < zoom * 0.999));
        setZoom(next ?? (direction > 0 ? MAX_ZOOM : MIN_ZOOM));
    }

    function localPoint(event) {
        const bounds = canvas.getBoundingClientRect();
        return { x: event.clientX - bounds.left, y: event.clientY - bounds.top };
    }

    // The wheel and two-finger scroll pan (Shift turns a vertical wheel sideways). Ctrl+wheel and pinch, which
    // the browser reports as Ctrl+wheel, zoom around the pointer.
    host.addEventListener('wheel', (event) => {
        if (!image) {
            return;
        }
        event.preventDefault();
        const lines = event.deltaMode === 1 ? 16 : event.deltaMode === 2 ? cssHeight : 1;
        let dx = event.deltaX * lines;
        let dy = event.deltaY * lines;

        if (event.ctrlKey || event.metaKey) {
            // A pinch sends small, frequent deltas; a mouse wheel notch sends about 100.
            const rate = Math.abs(dy) < 40 ? 0.01 : 0.002;
            const point = localPoint(event);
            setZoom(zoom * Math.exp(-dy * rate), point.x, point.y);
            return;
        }

        if (event.shiftKey && dx === 0) {
            [dx, dy] = [dy, 0];
        }
        panBy(dx, dy);
    }, { passive: false });

    canvas.addEventListener('pointerdown', (event) => {
        if (!image) {
            return;
        }
        host.focus({ preventScroll: true });

        // Dragging the image pans it, unless a tool such as crop owns the left button. Space+drag and the middle
        // button pan under a tool as well.
        if (event.button === 1 || (event.button === 0 && (spaceHeld || !tool))) {
            event.preventDefault();
            pan = { pointerId: event.pointerId, x: event.clientX, y: event.clientY, centerX, centerY };
            canvas.setPointerCapture(event.pointerId);
            canvas.dataset.panning = 'true';
            return;
        }

        if (event.button === 0 && tool?.pointerDown(event, toImage(event.clientX, event.clientY))) {
            canvas.setPointerCapture(event.pointerId);
        }
    });

    canvas.addEventListener('pointermove', (event) => {
        const point = toImage(event.clientX, event.clientY);
        const inside = image && point.x >= 0 && point.y >= 0 && point.x < imageWidth && point.y < imageHeight;
        onPointerImage?.(inside ? { x: Math.floor(point.x), y: Math.floor(point.y) } : null);

        if (pan && event.pointerId === pan.pointerId) {
            const targetX = pan.centerX - (event.clientX - pan.x) / zoom;
            const targetY = pan.centerY - (event.clientY - pan.y) / zoom;
            panBy((targetX - centerX) * zoom, (targetY - centerY) * zoom);
            return;
        }

        tool?.pointerMove(event, point);
        if (!pan && tool) {
            canvas.style.cursor = tool.cursorAt?.(point, zoom) || '';
        }
    });

    const endPointer = (event) => {
        if (pan && event.pointerId === pan.pointerId) {
            pan = null;
            delete canvas.dataset.panning;
            return;
        }
        tool?.pointerUp(event, toImage(event.clientX, event.clientY));
    };
    canvas.addEventListener('pointerup', endPointer);
    canvas.addEventListener('pointercancel', endPointer);
    canvas.addEventListener('pointerleave', () => onPointerImage?.(null));

    // The middle button would otherwise start the browser's autoscroll.
    canvas.addEventListener('mousedown', (event) => {
        if (event.button === 1) {
            event.preventDefault();
        }
    });

    window.addEventListener('keydown', (event) => {
        // Only over the canvas or the page, so Space still presses a focused button.
        if (event.code === 'Space' && (event.target === host || event.target === document.body)) {
            spaceHeld = true;
            canvas.dataset.spaceHeld = 'true';
            event.preventDefault();
        }
    });
    window.addEventListener('keyup', (event) => {
        if (event.code === 'Space') {
            spaceHeld = false;
            delete canvas.dataset.spaceHeld;
        }
    });
    window.addEventListener('blur', () => {
        spaceHeld = false;
        delete canvas.dataset.spaceHeld;
    });

    new ResizeObserver(measure).observe(host);

    // A move to a display with a different pixel density changes the ratio without resizing the host.
    function watchPixelRatio() {
        const query = matchMedia(`(resolution: ${window.devicePixelRatio || 1}dppx)`);
        query.addEventListener('change', () => {
            measure();
            watchPixelRatio();
        }, { once: true });
    }
    watchPixelRatio();

    // The checkerboard follows the theme, which the client mirrors onto the root element.
    new MutationObserver(render).observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] });

    return {
        measure,
        render,
        setImage(next, width, height, { keepView = false } = {}) {
            const sizeChanged = width !== imageWidth || height !== imageHeight;
            image = next;
            imageWidth = width;
            imageHeight = height;
            if (!keepView || fitting) {
                fitting = true;
                applyFit();
            } else if (sizeChanged) {
                clampCenter();
            }
            render();
            onChanged?.();
        },
        clear() {
            image = null;
            render();
        },
        fit() {
            fitting = true;
            applyFit();
            render();
            onChanged?.();
        },
        actualSize: () => setZoom(1),
        zoomIn: () => step(1),
        zoomOut: () => step(-1),
        zoom: () => zoom,
        setOverlay(draw) {
            overlay = draw;
            render();
        },
        setTool(next) {
            tool = next;
            canvas.style.cursor = '';
        },
        getState: () => ({ zoom, centerX, centerY, fitting }),
        setState(state) {
            if (!state || typeof state.zoom !== 'number') {
                return;
            }
            fitting = state.fitting === true;
            if (fitting) {
                applyFit();
            } else {
                zoom = clampZoom(state.zoom);
                centerX = Number(state.centerX) || 0;
                centerY = Number(state.centerY) || 0;
                clampCenter();
            }
            render();
            onChanged?.();
        },
    };
}

export function isTextTarget(target) {
    return target instanceof HTMLElement && (target.isContentEditable || ['INPUT', 'SELECT', 'TEXTAREA'].includes(target.tagName));
}
