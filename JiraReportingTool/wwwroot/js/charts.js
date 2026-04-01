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

window.destroyChart = (canvasId) => {
    if (_charts[canvasId]) {
        _charts[canvasId].destroy();
        delete _charts[canvasId];
    }
};
