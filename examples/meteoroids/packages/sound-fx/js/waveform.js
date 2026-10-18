// The waveform canvas: the min and max sample under each pixel column, a centre line, and a playhead while
// the sound plays. Colours come from the theme's CSS custom properties, read at draw time, so a redraw after
// a theme change picks up the new ones.

function themeColour(name, fallback) {
    const value = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
    return value || fallback;
}

export function createWaveform(canvas) {
    let samples = null;
    let playhead = null;
    let columns = null;

    // Reduces the samples to one min/max pair per device pixel column. Recomputed only when the samples or
    // the canvas width change, so the playhead animation redraws cheaply.
    function computeColumns(width) {
        if (!samples || samples.length === 0 || width <= 0) {
            return null;
        }

        const min = new Float32Array(width);
        const max = new Float32Array(width);
        const perColumn = samples.length / width;
        for (let x = 0; x < width; x++) {
            const start = Math.floor(x * perColumn);
            const end = Math.max(start + 1, Math.floor((x + 1) * perColumn));
            let low = 1;
            let high = -1;
            for (let i = start; i < end && i < samples.length; i++) {
                const value = samples[i];
                if (value < low) {
                    low = value;
                }
                if (value > high) {
                    high = value;
                }
            }
            min[x] = low > high ? 0 : low;
            max[x] = low > high ? 0 : high;
        }

        return { width, min, max };
    }

    function resize() {
        const rect = canvas.getBoundingClientRect();
        const scale = window.devicePixelRatio || 1;
        const width = Math.max(1, Math.round(rect.width * scale));
        const height = Math.max(1, Math.round(rect.height * scale));
        if (canvas.width !== width || canvas.height !== height) {
            canvas.width = width;
            canvas.height = height;
            columns = null;
        }
    }

    function draw() {
        resize();
        const context = canvas.getContext('2d');
        const { width, height } = canvas;
        const scale = window.devicePixelRatio || 1;
        context.clearRect(0, 0, width, height);

        const middle = height / 2;
        context.fillStyle = themeColour('--cel-divider', '#8884');
        context.fillRect(0, Math.floor(middle), width, Math.max(1, Math.round(scale)));

        if (!columns || columns.width !== width) {
            columns = computeColumns(width);
        }

        if (columns) {
            // Samples are drawn at full scale, so a quiet sound looks quiet.
            const half = (height / 2) * 0.95;
            context.fillStyle = themeColour('--cel-accent', '#0078d4');
            for (let x = 0; x < width; x++) {
                const top = middle - columns.max[x] * half;
                const bottom = middle - columns.min[x] * half;
                context.fillRect(x, top, 1, Math.max(1, bottom - top));
            }
        }

        if (playhead !== null) {
            context.fillStyle = themeColour('--cel-text-primary', '#000');
            context.fillRect(Math.round(playhead * (width - 1)), 0, Math.max(1, Math.round(scale)), height);
        }
    }

    return {
        setSamples(values) {
            samples = values;
            columns = null;
            draw();
        },

        // A fraction from 0 to 1, or null to hide the playhead.
        setPlayhead(fraction) {
            playhead = fraction;
            draw();
        },

        draw,
    };
}
