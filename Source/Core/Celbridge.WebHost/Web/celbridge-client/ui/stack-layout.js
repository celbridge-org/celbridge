// Resolves an element between an `inline` and a `stacked` layout from its own width, and keeps the result
// current as the element resizes. A settings panel uses it to decide whether its section nav fits beside the
// content or has to sit above it; an editor uses it for the same decision about its rail.
//
//   const layout = attachStackLayout(rootElement, {
//     property: '--cel-section-stack-threshold',
//     fallback: 387,
//     attribute: 'layout',
//     onChange(name) { /* the layout changed to 'inline' or 'stacked' */ },
//   });
//   layout.current();   // the resolved layout, or null before the first measurement
//
// The threshold is read from the element's computed style rather than through var(), because the comparison
// happens here rather than in a rule. `fallback` stands in where the generated stylesheet has not been
// served. The two are one number written twice, so keep them in step.
//
// The resolved name is written to the element's dataset under `attribute`, which is what the stylesheet
// keys on. A name already there when this is called is the author's and is never overridden, so markup can
// pin a layout.

export function attachStackLayout(rootElement, options = {}) {
    const {
        property,
        fallback,
        attribute = 'layout',
        // Below this an element is hidden or has not been laid out, so no width it reports is worth
        // resolving from.
        minimumWidth = 1,
        onChange,
        onMeasure,
    } = options;

    // A layout named in the markup is the author's, so the measurement never overrides it.
    const authored = rootElement.dataset[attribute] || '';
    const isAuthored = authored === 'inline' || authored === 'stacked';

    // Null until the first measurement, so an authored layout is still written and reported once.
    let current = null;

    function resolve(width) {
        if (isAuthored) {
            return authored;
        }

        const declared = getComputedStyle(rootElement).getPropertyValue(property);
        let threshold = Number.parseFloat(declared);
        if (!Number.isFinite(threshold) ||
            threshold <= 0) {
            threshold = fallback;
        }

        if (width >= threshold) {
            return 'inline';
        }

        return 'stacked';
    }

    function measure(width) {
        if (width >= minimumWidth) {
            const layout = resolve(width);
            if (layout !== current) {
                current = layout;
                rootElement.dataset[attribute] = layout;

                if (typeof onChange === 'function') {
                    onChange(layout);
                }
            }
        }

        // Reported for every measurement, including the ones too narrow to resolve from, so a caller can
        // track the element coming back on screen.
        if (typeof onMeasure === 'function') {
            onMeasure(width);
        }
    }

    if (typeof ResizeObserver === 'function') {
        const resizeObserver = new ResizeObserver((entries) => {
            for (const entry of entries) {
                measure(entry.contentRect.width);
            }
        });

        resizeObserver.observe(rootElement);
    }

    measure(rootElement.getBoundingClientRect().width);

    return {
        current() {
            return current;
        },
    };
}
