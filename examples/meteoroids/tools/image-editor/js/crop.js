// Crop mode: a rectangle over the image with eight handles, dimming everything outside it. Dragging inside
// moves it, dragging a handle resizes it, and dragging anywhere else draws a new one. The rectangle is kept in
// image pixels, inside the image, whole-pixel aligned and at least 1x1.

const HANDLE_SIZE = 8;
const HANDLE_REACH = 8;
const HANDLES = ['nw', 'n', 'ne', 'e', 'se', 's', 'sw', 'w'];
const CURSORS = {
    nw: 'nwse-resize', se: 'nwse-resize', ne: 'nesw-resize', sw: 'nesw-resize',
    n: 'ns-resize', s: 'ns-resize', e: 'ew-resize', w: 'ew-resize', move: 'move',
};

export function createCropTool({ onChanged }) {
    let bounds = { width: 1, height: 1 };
    let rect = { x: 0, y: 0, width: 1, height: 1 };
    let drag = null;
    let lastZoom = 1;

    function clampRect(r) {
        const width = Math.min(bounds.width, Math.max(1, Math.round(r.width)));
        const height = Math.min(bounds.height, Math.max(1, Math.round(r.height)));
        return {
            x: Math.min(bounds.width - width, Math.max(0, Math.round(r.x))),
            y: Math.min(bounds.height - height, Math.max(0, Math.round(r.y))),
            width,
            height,
        };
    }

    function set(next) {
        rect = clampRect(next);
        onChanged?.(rect);
    }

    function handleAt(point, zoom) {
        const reach = HANDLE_REACH / zoom;
        const left = rect.x;
        const top = rect.y;
        const right = rect.x + rect.width;
        const bottom = rect.y + rect.height;
        const midX = left + rect.width / 2;
        const midY = top + rect.height / 2;
        const positions = {
            nw: [left, top], n: [midX, top], ne: [right, top], e: [right, midY],
            se: [right, bottom], s: [midX, bottom], sw: [left, bottom], w: [left, midY],
        };
        for (const handle of HANDLES) {
            const [hx, hy] = positions[handle];
            if (Math.abs(point.x - hx) <= reach && Math.abs(point.y - hy) <= reach) {
                return handle;
            }
        }
        if (point.x >= left && point.x <= right && point.y >= top && point.y <= bottom) {
            return 'move';
        }
        return null;
    }

    // A rectangle from a fixed anchor corner to the pointer, held to the image.
    function fromAnchor(anchorX, anchorY, pointX, pointY) {
        const directionX = pointX >= anchorX ? 1 : -1;
        const directionY = pointY >= anchorY ? 1 : -1;
        const roomX = directionX > 0 ? bounds.width - anchorX : anchorX;
        const roomY = directionY > 0 ? bounds.height - anchorY : anchorY;
        const width = Math.max(1, Math.min(Math.abs(pointX - anchorX), roomX));
        const height = Math.max(1, Math.min(Math.abs(pointY - anchorY), roomY));
        return {
            x: directionX > 0 ? anchorX : anchorX - width,
            y: directionY > 0 ? anchorY : anchorY - height,
            width,
            height,
        };
    }

    // An edge handle moves one side.
    function fromEdge(start, handle, point) {
        const vertical = handle === 'n' || handle === 's';
        let { x, y, width, height } = start;

        if (vertical) {
            const anchor = handle === 'n' ? start.y + start.height : start.y;
            const next = Math.min(bounds.height, Math.max(0, point.y));
            height = Math.max(1, Math.abs(next - anchor));
            y = next < anchor ? anchor - height : anchor;
        } else {
            const anchor = handle === 'w' ? start.x + start.width : start.x;
            const next = Math.min(bounds.width, Math.max(0, point.x));
            width = Math.max(1, Math.abs(next - anchor));
            x = next < anchor ? anchor - width : anchor;
        }

        return { x, y, width, height };
    }

    const tool = {
        pointerDown(event, point) {
            const handle = handleAt(point, lastZoom);
            const clamped = {
                x: Math.min(bounds.width, Math.max(0, point.x)),
                y: Math.min(bounds.height, Math.max(0, point.y)),
            };
            drag = { handle: handle || 'new', start: { ...rect }, origin: clamped };
            return true;
        },
        pointerMove(event, point) {
            if (!drag) {
                return;
            }

            const { handle, start, origin } = drag;
            if (handle === 'move') {
                set({ ...start, x: start.x + point.x - origin.x, y: start.y + point.y - origin.y });
            } else if (handle === 'new') {
                set(fromAnchor(origin.x, origin.y, point.x, point.y));
            } else if (handle.length === 2) {
                const anchorX = handle.includes('w') ? start.x + start.width : start.x;
                const anchorY = handle.includes('n') ? start.y + start.height : start.y;
                set(fromAnchor(anchorX, anchorY, point.x, point.y));
            } else {
                set(fromEdge(start, handle, point));
            }
        },
        pointerUp() {
            drag = null;
        },
        cursorAt(point, zoom) {
            lastZoom = zoom;
            if (drag) {
                return CURSORS[drag.handle] || 'crosshair';
            }
            return CURSORS[handleAt(point, zoom)] || 'crosshair';
        },
    };

    function drawOverlay(context, view) {
        lastZoom = view.zoom;
        const { toScreen } = view;
        const image = view.rect;
        const topLeft = toScreen(rect.x, rect.y);
        const bottomRight = toScreen(rect.x + rect.width, rect.y + rect.height);
        const x = topLeft.x;
        const y = topLeft.y;
        const w = bottomRight.x - topLeft.x;
        const h = bottomRight.y - topLeft.y;

        context.save();
        context.fillStyle = 'rgba(0, 0, 0, 0.5)';
        context.beginPath();
        context.rect(image.x, image.y, image.width, image.height);
        context.rect(x, y, w, h);
        context.fill('evenodd');

        // Thirds guides, then the outline in two tones so it reads on any image.
        context.strokeStyle = 'rgba(255, 255, 255, 0.35)';
        context.lineWidth = 1;
        context.beginPath();
        for (const t of [1 / 3, 2 / 3]) {
            context.moveTo(x + w * t, y);
            context.lineTo(x + w * t, y + h);
            context.moveTo(x, y + h * t);
            context.lineTo(x + w, y + h * t);
        }
        context.stroke();

        context.strokeStyle = 'rgba(0, 0, 0, 0.6)';
        context.lineWidth = 3;
        context.strokeRect(x, y, w, h);
        context.strokeStyle = '#ffffff';
        context.lineWidth = 1;
        context.strokeRect(x, y, w, h);

        const half = HANDLE_SIZE / 2;
        const points = [[x, y], [x + w / 2, y], [x + w, y], [x + w, y + h / 2], [x + w, y + h], [x + w / 2, y + h], [x, y + h], [x, y + h / 2]];
        context.fillStyle = '#ffffff';
        context.strokeStyle = 'rgba(0, 0, 0, 0.7)';
        for (const [px, py] of points) {
            context.fillRect(px - half, py - half, HANDLE_SIZE, HANDLE_SIZE);
            context.strokeRect(px - half + 0.5, py - half + 0.5, HANDLE_SIZE - 1, HANDLE_SIZE - 1);
        }
        context.restore();
    }

    return {
        tool,
        drawOverlay,
        start(width, height) {
            bounds = { width, height };
            drag = null;
            set({ x: 0, y: 0, width, height });
        },
        rect: () => ({ ...rect }),
        bounds: () => ({ ...bounds }),
        setField(name, value) {
            set({ ...rect, [name]: value });
        },
        coversImage: () => rect.x === 0 && rect.y === 0 && rect.width === bounds.width && rect.height === bounds.height,
    };
}
