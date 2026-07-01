import { describe, it, expect } from 'vitest';
import { projectUrl } from '../api/document-api.js';

describe('projectUrl', () => {
    it('strips the project: prefix when present', () => {
        // Regression: a naive concatenation produced URLs like
        // /project/project:packages/foo.png which 404'd because the loopback
        // /project/ route treats the URL path as relative to the project folder.
        const url = projectUrl('project:packages/king-fury/sprites/piece_bishop.png');
        expect(url).toBe('/project/packages/king-fury/sprites/piece_bishop.png');
    });

    it('returns the bare base URL for an empty resource key', () => {
        expect(projectUrl('')).toBe('/project/');
        expect(projectUrl(null)).toBe('/project/');
        expect(projectUrl(undefined)).toBe('/project/');
    });

    it('passes through a key with no project: prefix', () => {
        // The helper is intentionally lenient on its input. A caller that
        // already trimmed the prefix should still get a sane URL.
        expect(projectUrl('packages/foo.png')).toBe('/project/packages/foo.png');
    });
});
