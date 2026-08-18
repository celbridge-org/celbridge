// Renders a parsed report into the page. Talks to the DOM and the localization table only; the host
// is reached through the onOpenResource callback the caller supplies, so the rendering can run
// outside the app.

import { t } from '/assets/celbridge-client/localization.js';
import {
    SectionKind,
    Severity,
    findResourceAction,
    findOtherActions,
    groupItems,
    normalizeSeverity,
    resolveActionPosition
} from './report-model.js';

const SEVERITY_ICONS = Object.freeze({
    [Severity.Info]: 'bi-info-circle',
    [Severity.Warning]: 'bi-exclamation-triangle',
    [Severity.Error]: 'bi-exclamation-octagon'
});

const SEVERITY_LABEL_KEYS = Object.freeze({
    [Severity.Info]: 'Report_Severity_Info',
    [Severity.Warning]: 'Report_Severity_Warning',
    [Severity.Error]: 'Report_Severity_Error'
});

function applySeverityIcon(element, severity) {
    const normalized = normalizeSeverity(severity);

    element.className = `bi ${SEVERITY_ICONS[normalized]} severity-${normalized}`;
    element.setAttribute('title', t(SEVERITY_LABEL_KEYS[normalized]));
}

function createSeverityIcon(severity) {
    const icon = document.createElement('i');
    applySeverityIcon(icon, severity);
    icon.setAttribute('aria-hidden', 'true');

    return icon;
}

function formatGeneratedAt(generatedAt) {
    const parsed = new Date(generatedAt);
    if (Number.isNaN(parsed.getTime())) {
        return generatedAt ?? '';
    }

    // The file stores UTC; the reader is shown their own local time.
    return parsed.toLocaleString();
}

function renderHeader(report) {
    applySeverityIcon(document.getElementById('report-severity'), report.severity);
    document.getElementById('report-title').textContent = report.title ?? '';
    document.getElementById('report-summary').textContent = report.summary ?? '';
    document.getElementById('report-generated').textContent =
        t('Report_Generated', formatGeneratedAt(report.generatedAt));

    const truncatedElement = document.getElementById('report-truncated');
    const omitted = report.truncated?.omitted ?? 0;
    truncatedElement.hidden = omitted <= 0;
    if (omitted > 0) {
        truncatedElement.textContent = t('Report_Truncated', omitted);
    }
}

// The resource an item names is the one thing in a finding the reader can act on, so where an action
// opens it the resource itself is the control rather than a separate button repeating the verb.
function createResourceElement(item, onOpenResource) {
    const action = findResourceAction(item);
    if (action === null) {
        const text = document.createElement('span');
        text.className = 'resource';
        text.textContent = item.resource ?? '';

        return text;
    }

    const position = resolveActionPosition(action);

    const link = document.createElement('button');
    link.type = 'button';
    link.className = 'resource resource-link';
    link.textContent = item.resource;
    link.title = action.label ?? item.resource;
    link.addEventListener('click', () => {
        onOpenResource(action.resource, position.line, position.column);
    });

    return link;
}

// An action naming something other than the item's own resource has no cell to live in, so it keeps
// the button treatment. No producer emits one today.
function renderOtherActions(item, onOpenResource) {
    const actions = findOtherActions(item);
    if (actions.length === 0) {
        return null;
    }

    const container = document.createElement('p');
    container.className = 'item-actions';

    for (const action of actions) {
        const position = resolveActionPosition(action);

        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'item-action';
        button.textContent = action.label ?? action.resource;
        button.addEventListener('click', () => {
            onOpenResource(action.resource, position.line, position.column);
        });
        container.appendChild(button);
    }

    return container;
}

// A fact is a labelled reading rather than prose, so it renders as a label and its value on one row.
function renderFact(item) {
    const row = document.createElement('div');
    row.className = 'fact-row';

    const label = document.createElement('span');
    label.className = 'fact-label';
    label.textContent = item.message ?? '';
    row.appendChild(label);

    const value = document.createElement('span');
    value.className = 'fact-value';
    value.textContent = item.value ?? '';
    row.appendChild(value);

    return row;
}

// A lone finding shares nothing with a neighbour, so there is no column for it to line up with and it
// reads better as a block than as a one-row table.
function renderSingleFinding(item, onOpenResource) {
    const row = document.createElement('div');
    row.className = 'finding-row';

    row.appendChild(createSeverityIcon(item.severity));

    const body = document.createElement('div');
    body.className = 'item-body';

    const message = document.createElement('p');
    message.className = 'item-message';
    message.textContent = item.message ?? '';
    body.appendChild(message);

    if (item.resource || item.target) {
        const resources = document.createElement('p');
        resources.className = 'item-resources';
        resources.appendChild(createResourceElement(item, onOpenResource));

        if (item.target) {
            resources.appendChild(createTargetElement(item.target));
        }

        body.appendChild(resources);
    }

    if (item.detail) {
        const detail = document.createElement('p');
        detail.className = 'item-detail';
        detail.textContent = item.detail;
        body.appendChild(detail);
    }

    const actions = renderOtherActions(item, onOpenResource);
    if (actions !== null) {
        body.appendChild(actions);
    }

    row.appendChild(body);

    return row;
}

function createTargetElement(target) {
    const fragment = document.createDocumentFragment();

    const separator = document.createElement('span');
    separator.className = 'resource-separator';
    separator.setAttribute('aria-hidden', 'true');
    separator.textContent = '→';
    fragment.appendChild(separator);

    const element = document.createElement('span');
    element.className = 'resource';
    element.textContent = target;
    fragment.appendChild(element);

    return fragment;
}

// Which columns a group's table needs, decided by what its rows actually carry. A column every row
// leaves blank is a column that costs width and tells the reader nothing.
function resolveColumns(items) {
    return {
        resource: items.some(item => Boolean(item.resource)),
        target: items.some(item => Boolean(item.target)),
        line: items.some(item => resolveActionPosition(findResourceAction(item)).line > 0),
        detail: items.some(item => Boolean(item.detail))
    };
}

function appendHeaderCell(row, labelKey, className) {
    const cell = document.createElement('th');
    cell.scope = 'col';
    cell.textContent = t(labelKey);
    if (className) {
        cell.className = className;
    }
    row.appendChild(cell);
}

function renderGroupTable(group, columns, onOpenResource) {
    const table = document.createElement('table');
    table.className = 'finding-table';

    const head = document.createElement('thead');
    const headRow = document.createElement('tr');
    if (columns.resource) {
        appendHeaderCell(headRow, 'Report_Column_Resource');
    }
    if (columns.target) {
        appendHeaderCell(headRow, 'Report_Column_Target');
    }
    if (columns.line) {
        appendHeaderCell(headRow, 'Report_Column_Line', 'column-line');
    }
    if (columns.detail) {
        appendHeaderCell(headRow, 'Report_Column_Detail');
    }
    head.appendChild(headRow);
    table.appendChild(head);

    const body = document.createElement('tbody');
    for (const item of group.items) {
        const row = document.createElement('tr');

        if (columns.resource) {
            const cell = document.createElement('td');
            cell.appendChild(createResourceElement(item, onOpenResource));
            row.appendChild(cell);
        }

        if (columns.target) {
            const cell = document.createElement('td');
            cell.className = 'resource';
            cell.textContent = item.target ?? '';
            row.appendChild(cell);
        }

        if (columns.line) {
            const cell = document.createElement('td');
            cell.className = 'column-line';
            const position = resolveActionPosition(findResourceAction(item));
            cell.textContent = position.line > 0 ? String(position.line) : '';
            row.appendChild(cell);
        }

        if (columns.detail) {
            const cell = document.createElement('td');
            cell.className = 'cell-detail';
            cell.textContent = item.detail ?? '';
            row.appendChild(cell);
        }

        body.appendChild(row);
    }
    table.appendChild(body);

    return table;
}

// Occurrences of one finding are parallel rows, which is what makes a table worth its chrome: the
// message is stated once in the heading and the columns carry what differs.
function renderFindingGroup(group, onOpenResource) {
    const element = document.createElement('div');
    element.className = 'finding-group';

    const header = document.createElement('div');
    header.className = 'group-header';
    header.appendChild(createSeverityIcon(group.severity));

    const message = document.createElement('span');
    message.className = 'group-message';
    message.textContent = group.message;
    header.appendChild(message);

    const count = document.createElement('span');
    count.className = 'group-count';
    count.textContent = t('Report_Occurrences_Many', group.items.length);
    header.appendChild(count);

    element.appendChild(header);

    const columns = resolveColumns(group.items);
    const hasColumns = columns.resource || columns.target || columns.line || columns.detail;
    if (hasColumns) {
        element.appendChild(renderGroupTable(group, columns, onOpenResource));
    }

    return element;
}

function renderSection(section, onOpenResource) {
    const element = document.createElement('section');
    element.className = 'report-section';

    // No severity glyph and no count on a section heading. Every item below carries its own glyph, so
    // repeating one on the heading made the heading read as another item rather than as what contains
    // them, and the total is already in the report's summary.
    const title = document.createElement('h2');
    title.className = 'section-title';
    title.textContent = section.title ?? '';
    element.appendChild(title);

    const items = Array.isArray(section.items) ? section.items : [];
    const isFacts = section.kind === SectionKind.Facts;

    const body = document.createElement('div');
    body.className = 'section-body';

    if (isFacts) {
        for (const item of items) {
            body.appendChild(renderFact(item));
        }
    } else {
        for (const group of groupItems(items)) {
            body.appendChild(group.items.length > 1
                ? renderFindingGroup(group, onOpenResource)
                : renderSingleFinding(group.items[0], onOpenResource));
        }
    }

    element.appendChild(body);

    return element;
}

/**
 * Renders a parsed report into the page.
 * @param {Object} report - A report parsed by parseReport.
 * @param {(resource: string, line: number, column: number) => void} onOpenResource - Invoked when the
 *   reader activates a resource that a report action can open.
 */
export function renderReport(report, onOpenResource) {
    renderHeader(report);

    const container = document.getElementById('report-sections');
    container.replaceChildren();

    for (const section of report.sections) {
        container.appendChild(renderSection(section, onOpenResource));
    }

    document.getElementById('report').hidden = false;
    document.getElementById('report-error').hidden = true;
}

/**
 * Replaces the report with the message shown when the file cannot be read.
 */
export function showParseError() {
    document.getElementById('report').hidden = true;
    document.getElementById('report-error').hidden = false;
}
