import { describe, it, expect } from 'vitest';
import { isAdoptableSize } from '../terminal-sizing.js';

describe('isAdoptableSize', () => {
    it('adopts a size the terminal is not already at', () => {
        expect(isAdoptableSize(120, 30, 80, 24)).toBe(true);
    });

    it('declines the size the terminal is already at', () => {
        expect(isAdoptableSize(80, 24, 80, 24)).toBe(false);
    });

    it('declines a size the session has not reported', () => {
        expect(isAdoptableSize(undefined, undefined, 80, 24)).toBe(false);
        expect(isAdoptableSize(null, null, 80, 24)).toBe(false);
    });

    it('declines a size that is not a whole number of cells', () => {
        expect(isAdoptableSize('120', '30', 80, 24)).toBe(false);
        expect(isAdoptableSize(120.5, 30, 80, 24)).toBe(false);
        expect(isAdoptableSize(Number.NaN, 30, 80, 24)).toBe(false);
    });

    it('declines an empty size', () => {
        expect(isAdoptableSize(0, 0, 80, 24)).toBe(false);
        expect(isAdoptableSize(-1, 30, 80, 24)).toBe(false);
    });
});
