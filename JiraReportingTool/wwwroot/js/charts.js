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
