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
window.exportSectionsAsHtml = (sectionIds, fileName, chartIds, title) => {
    const container = document.createElement('div');
    container.className = 'v2-root';

    (sectionIds || []).forEach(id => {
        const el = document.getElementById(id);
        if (!el) return;
        const clone = el.cloneNode(true);

        (chartIds || []).forEach(cid => {
            const chart = _charts[cid];
            const canvasInClone = clone.querySelector('#' + cid);
            if (!chart || !canvasInClone) return;
            const img = document.createElement('img');
            img.src = chart.toBase64Image('image/png', 1);
            img.style.maxWidth = '100%';
            canvasInClone.replaceWith(img);
        });

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
