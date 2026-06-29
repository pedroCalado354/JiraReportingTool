// Theme + sidebar chrome — plain global helpers so they work without a
// Blazor round-trip (the inline <head> snippet applies the saved theme
// before first paint to avoid a flash).
(function () {
  window.jrtToggleTheme = function () {
    var cur = document.documentElement.getAttribute('data-theme') === 'dark' ? 'dark' : 'light';
    var next = cur === 'dark' ? 'light' : 'dark';
    document.documentElement.setAttribute('data-theme', next);
    try { localStorage.setItem('jrt-theme', next); } catch (e) { /* ignore */ }
  };

  window.jrtToggleSidebar = function () {
    document.body.classList.toggle('sidebar-open');
  };
  window.jrtCloseSidebar = function () {
    document.body.classList.remove('sidebar-open');
  };

  // Close the off-canvas sidebar after navigating to a new page.
  document.addEventListener('click', function (e) {
    var link = e.target.closest && e.target.closest('.app-sidebar a');
    if (link) { document.body.classList.remove('sidebar-open'); }
  });
})();
