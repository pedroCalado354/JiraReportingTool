const _charts = {};

window.renderChart = (canvasId, config) => {
    if (_charts[canvasId]) {
        _charts[canvasId].destroy();
        delete _charts[canvasId];
    }
    const canvas = document.getElementById(canvasId);
    if (!canvas) return;
    _charts[canvasId] = new Chart(canvas.getContext('2d'), config);
};

// Bar chart whose bars invoke a .NET method on click.
// dotNetRef: DotNetObjectReference; methodName: [JSInvokable] method receiving (tag, datasetIndex, dataIndex).
// tag: arbitrary string echoed back so a shared handler can tell which chart was clicked.
window.renderClickableBarChart = (canvasId, config, dotNetRef, methodName, tag) => {
    if (_charts[canvasId]) {
        _charts[canvasId].destroy();
        delete _charts[canvasId];
    }
    const canvas = document.getElementById(canvasId);
    if (!canvas) return;
    config.options = config.options || {};
    config.options.onClick = (evt, elements) => {
        if (!elements || elements.length === 0) return;
        const el = elements[0];
        dotNetRef.invokeMethodAsync(methodName, tag, el.datasetIndex, el.index);
    };
    config.options.onHover = (evt, elements) => {
        evt.native.target.style.cursor = (elements && elements.length) ? 'pointer' : 'default';
    };
    _charts[canvasId] = new Chart(canvas.getContext('2d'), config);
};

// SLA tier chart: clickable bars + value labels on top of each bar + a tinted
// "breached" region with a dashed divider after the first (Inside SLA) category.
window.renderSlaBarChart = (canvasId, config, dotNetRef, methodName, tag) => {
    if (_charts[canvasId]) {
        _charts[canvasId].destroy();
        delete _charts[canvasId];
    }
    const canvas = document.getElementById(canvasId);
    if (!canvas) return;

    const txtColor = (getComputedStyle(document.documentElement).getPropertyValue('--txt-1') || '#374151').trim();

    config.options = config.options || {};
    config.options.onClick = (evt, elements) => {
        if (!elements || elements.length === 0) return;
        const el = elements[0];
        dotNetRef.invokeMethodAsync(methodName, tag, el.datasetIndex, el.index);
    };
    config.options.onHover = (evt, elements) => {
        evt.native.target.style.cursor = (elements && elements.length) ? 'pointer' : 'default';
    };

    // Faint red band over the "Outside SLA" categories (everything past category 0) + dashed boundary.
    const insideOutside = {
        id: 'slaInsideOutside',
        beforeDatasetsDraw(chart) {
            const { ctx, chartArea, scales } = chart;
            const x = scales.x;
            if (!chartArea || !x) return;
            const x0 = x.getPixelForValue(0);
            const x1 = x.getPixelForValue(1);
            if (x1 == null || isNaN(x1)) return;
            const boundary = (x0 + x1) / 2;
            ctx.save();
            ctx.fillStyle = 'rgba(220,38,38,0.05)';
            ctx.fillRect(boundary, chartArea.top, chartArea.right - boundary, chartArea.bottom - chartArea.top);
            ctx.strokeStyle = 'rgba(220,38,38,0.45)';
            ctx.lineWidth = 1.5;
            ctx.setLineDash([5, 4]);
            ctx.beginPath();
            ctx.moveTo(boundary, chartArea.top);
            ctx.lineTo(boundary, chartArea.bottom);
            ctx.stroke();
            ctx.setLineDash([]);
            ctx.font = '600 10px system-ui, sans-serif';
            ctx.textBaseline = 'top';
            ctx.fillStyle = 'rgba(22,163,74,0.85)';
            ctx.textAlign = 'right';
            ctx.fillText('IN SLA', boundary - 6, chartArea.top + 2);
            ctx.fillStyle = 'rgba(220,38,38,0.8)';
            ctx.textAlign = 'left';
            ctx.fillText('BREACHED', boundary + 6, chartArea.top + 2);
            ctx.restore();
        }
    };

    // Value label above each non-zero bar.
    const valueLabels = {
        id: 'slaValueLabels',
        afterDatasetsDraw(chart) {
            const { ctx } = chart;
            ctx.save();
            ctx.font = '600 11px system-ui, sans-serif';
            ctx.fillStyle = txtColor || '#374151';
            ctx.textAlign = 'center';
            ctx.textBaseline = 'bottom';
            chart.data.datasets.forEach((ds, di) => {
                const meta = chart.getDatasetMeta(di);
                if (meta.hidden) return;
                meta.data.forEach((bar, i) => {
                    const v = ds.data[i];
                    if (!v) return;
                    ctx.fillText(v, bar.x, bar.y - 2);
                });
            });
            ctx.restore();
        }
    };

    config.plugins = [insideOutside, valueLabels];
    _charts[canvasId] = new Chart(canvas.getContext('2d'), config);
};

// Returns a chart's current render as a base64 PNG data URL, or null if it isn't rendered.
window.getChartImage = (canvasId) => {
    const chart = _charts[canvasId];
    if (!chart) return null;
    return chart.toBase64Image('image/png', 1);
};

window.destroyChart = (canvasId) => {
    if (_charts[canvasId]) {
        _charts[canvasId].destroy();
        delete _charts[canvasId];
    }
};

window.downloadBase64File = (base64, fileName, mimeType) => {
    const link = document.createElement('a');
    link.href = `data:${mimeType};base64,${base64}`;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
};

// Snapshots a specific set of elements (by id) as a standalone .html file, each wrapped in
// its own card: swaps each listed chart canvas for a static PNG (canvas pixels aren't part
// of the DOM so a plain clone would leave them blank), and points at this page's own
// stylesheets so it keeps the same look when opened with network access.
window.exportSectionsAsHtml = (sectionIds, fileName, chartIds, title, notesHtml) => {
    const container = document.createElement('div');
    container.className = 'v2-root';

    if (notesHtml && notesHtml.trim()) {
        const notesCard = document.createElement('div');
        notesCard.className = 'v2-card';
        notesCard.style.marginBottom = '16px';
        notesCard.innerHTML = `<div class="v2-section-title"><i class="bi bi-sticky-fill"></i> Notes</div><div style="margin-top:8px">${notesHtml}</div>`;
        container.appendChild(notesCard);
    }

    (sectionIds || []).forEach(id => {
        const el = document.getElementById(id);
        if (!el) return;
        const clone = el.cloneNode(true);

        // Interactive-only elements (buttons, filter chip rows, modal triggers) are marked
        // dr-export-skip so they never end up baked into the static export.
        clone.querySelectorAll('.dr-export-skip').forEach(skip => skip.remove());

        (chartIds || []).forEach(cid => {
            const chart = _charts[cid];
            const canvasInClone = clone.querySelector('#' + cid);
            if (!chart || !canvasInClone) return;
            const img = document.createElement('img');
            img.src = chart.toBase64Image('image/png', 1);
            img.style.maxWidth = '100%';
            canvasInClone.replaceWith(img);
        });

        // The live page shows Delivery Burndown (hours) before Task Burndown (issue count),
        // but the export wants them the other way round — swap the two cards within this one
        // cloned section only, leaving the on-screen dashboard order untouched.
        if (id === 'html-export-burndown' && clone.children.length === 2) {
            clone.insertBefore(clone.children[1], clone.children[0]);
        }

        // The "By Product" table is deliberately narrow on-screen (it sits beside the donut,
        // priority breakdown, etc.), but the export has far more spare width — widen it there.
        const productTableWrap = clone.querySelector('.dr-product-table-wrap');
        if (productTableWrap) {
            productTableWrap.style.width = '640px';
            productTableWrap.style.maxWidth = 'none';
            const tableScroll = productTableWrap.querySelector('.v2-table-wrap');
            if (tableScroll) tableScroll.style.maxHeight = '320px';
        }

        if (clone.classList.contains('v2-card')) {
            clone.style.marginBottom = '16px';
            container.appendChild(clone);
        } else {
            const card = document.createElement('div');
            card.className = 'v2-card';
            card.style.marginBottom = '16px';
            card.appendChild(clone);
            container.appendChild(card);
        }
    });

    const styleLinks = Array.from(document.querySelectorAll('link[rel="stylesheet"]'))
        .map(l => `<link rel="stylesheet" href="${l.href}">`)
        .join('\n');

    const html = `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>${title || document.title}</title>
${styleLinks}
<style>body{background:#f8fafc;padding:24px;} .v2-root{max-width:1400px;margin:0 auto;}</style>
</head>
<body>
${container.outerHTML}
</body>
</html>`;

    const blob = new Blob([html], { type: 'text/html' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);
};

// Minimal rich-text editing for the "add notes before export" popup — a plain contenteditable
// div plus document.execCommand. Deprecated by the spec but still the simplest way to get
// bold/bullets/color out of a contenteditable without pulling in a full editor library for
// what's meant to be a few lines of report commentary.
window.runNotesCommand = (command) => {
    const el = document.getElementById('export-notes-editor');
    if (!el) return;
    el.focus();
    document.execCommand(command);
};

window.runNotesColor = (color) => {
    const el = document.getElementById('export-notes-editor');
    if (!el) return;
    el.focus();
    document.execCommand('foreColor', false, color);
};

window.insertNotesLink = () => {
    const el = document.getElementById('export-notes-editor');
    if (!el) return;
    const url = window.prompt('Jira link URL:');
    if (!url) return;
    el.focus();

    const selection = window.getSelection();
    const hasSelection = selection && selection.rangeCount > 0 && !selection.isCollapsed && el.contains(selection.anchorNode);
    if (hasSelection) {
        document.execCommand('createLink', false, url);
        return;
    }

    const a = document.createElement('a');
    a.href = url;
    a.target = '_blank';
    a.textContent = url;

    const range = selection && selection.rangeCount > 0 && el.contains(selection.anchorNode)
        ? selection.getRangeAt(0)
        : null;
    if (range) {
        range.deleteContents();
        range.insertNode(a);
        range.setStartAfter(a);
        range.collapse(true);
        selection.removeAllRanges();
        selection.addRange(range);
    } else {
        el.appendChild(a);
    }
};

window.getExportNotesHtml = () => {
    const el = document.getElementById('export-notes-editor');
    return el ? el.innerHTML : '';
};

window.clearNotesEditor = () => {
    const el = document.getElementById('export-notes-editor');
    if (el) el.innerHTML = '';
};
