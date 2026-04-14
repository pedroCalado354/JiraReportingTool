window.modalHelper = {
    patchDialog: function (element) {
        if (!element) return;
        // Remove any previously attached handler to avoid duplicates on re-render
        if (element.__modalStopProp) {
            element.removeEventListener('click', element.__modalStopProp);
        }
        element.__modalStopProp = function (e) {
            e.stopPropagation();
        };
        element.addEventListener('click', element.__modalStopProp);
    }
};
