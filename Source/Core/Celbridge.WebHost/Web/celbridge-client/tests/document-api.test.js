import { describe, it, expect } from 'vitest';
import { PROJECT_HOST_URL, projectUrl } from '../api/document-api.js';

describe('projectUrl', () => {
    it('strips the project: prefix when present', () => {
        // Regression: a naive concatenation produced URLs like
        // https://project.celbridge/project:packages/foo.png which 404'd
        // because WebView2's virtual host mapping treats the URL path as
        // relative to the project folder.
        const url = projectUrl('project:packages/king-fury/sprites/piece_bishop.png');
        expect(url).toBe('https://project.celbridge/packages/king-fury/sprites/piece_bishop.png');
    });

    it('returns the bare host URL for an empty resource key', () => {
        expect(projectUrl('')).toBe(PROJECT_HOST_URL);
        expect(projectUrl(null)).toBe(PROJECT_HOST_URL);
        expect(projectUrl(undefined)).toBe(PROJECT_HOST_URL);
    });

    it('passes through a key with no project: prefix', () => {
        // The helper is intentionally lenient on its input. A caller that
        // already trimmed the prefix should still get a sane URL.
        expect(projectUrl('packages/foo.png')).toBe('https://project.celbridge/packages/foo.png');
    });
});
