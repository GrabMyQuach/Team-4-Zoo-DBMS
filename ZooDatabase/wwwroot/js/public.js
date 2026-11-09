// Homepage photo banner: changes slide on its own, and the arrows/dots let you move it yourself.
// Clicking restarts the timer so it doesn't jump right after you pick a slide.
document.querySelectorAll('[data-carousel]').forEach(function (root) {
    var slides = root.querySelectorAll('.hero-slide');
    var dots = root.querySelectorAll('.hero-dot');
    if (slides.length < 2) { return; }

    var current = 0;
    var timer = null;
    var delay = parseInt(root.getAttribute('data-interval') || '6000', 10);
    var reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    function show(n) {
        current = (n + slides.length) % slides.length;
        slides.forEach(function (slide, i) {
            var on = i === current;
            slide.classList.toggle('is-active', on);
            slide.setAttribute('aria-hidden', on ? 'false' : 'true');
            slide.querySelectorAll('a').forEach(function (a) { a.tabIndex = on ? 0 : -1; });
        });
        dots.forEach(function (dot, i) {
            dot.classList.toggle('is-active', i === current);
            dot.setAttribute('aria-current', i === current ? 'true' : 'false');
        });
    }

    function start() {
        clearInterval(timer);
        if (!reduceMotion) {
            timer = setInterval(function () { show(current + 1); }, delay);
        }
    }

    root.querySelector('.hero-prev').addEventListener('click', function () { show(current - 1); start(); });
    root.querySelector('.hero-next').addEventListener('click', function () { show(current + 1); start(); });
    dots.forEach(function (dot, i) {
        dot.addEventListener('click', function () { show(i); start(); });
    });

    // Pause while the mouse is over the banner or a button inside it has keyboard focus
    root.addEventListener('mouseenter', function () { clearInterval(timer); });
    root.addEventListener('mouseleave', start);
    root.addEventListener('focusin', function () { clearInterval(timer); });
    root.addEventListener('focusout', start);

    show(0);
    start();
});

// −/+ buttons next to a quantity box
document.addEventListener('click', function (e) {
    var btn = e.target.closest('[data-step]');
    if (!btn) { return; }
    var input = document.getElementById(btn.getAttribute('data-target'));
    if (!input) { return; }
    var min = input.min !== '' ? parseInt(input.min, 10) : 0;
    var max = input.max !== '' ? parseInt(input.max, 10) : 99;
    var next = (parseInt(input.value, 10) || 0) + parseInt(btn.getAttribute('data-step'), 10);
    input.value = Math.max(min, Math.min(max, next));
});

// "When?" calendar on the Tickets page: click a day to pick the visit date
document.querySelectorAll('[data-calendar]').forEach(function (cal) {
    var input = document.getElementById(cal.getAttribute('data-input'));
    var label = document.getElementById(cal.getAttribute('data-label'));
    var fromText = cal.getAttribute('data-from');

    function parse(s) { var p = s.split('-'); return new Date(+p[0], +p[1] - 1, +p[2]); }
    function iso(d) {
        return d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0') + '-' + String(d.getDate()).padStart(2, '0');
    }
    function el(tag, cls, text) {
        var n = document.createElement(tag);
        if (cls) { n.className = cls; }
        if (text != null) { n.textContent = text; }
        return n;
    }

    var min = parse(cal.getAttribute('data-min'));
    var max = parse(cal.getAttribute('data-max'));
    var selected = input.value ? parse(input.value) : null;
    var start = selected || min;
    var view = new Date(start.getFullYear(), start.getMonth(), 1);

    function render() {
        cal.textContent = '';

        var head = el('div', 'cal-head');
        var prev = el('button', 'cal-nav', '‹');
        prev.type = 'button';
        prev.setAttribute('aria-label', 'Previous month');
        prev.disabled = view <= new Date(min.getFullYear(), min.getMonth(), 1);
        prev.addEventListener('click', function () { view.setMonth(view.getMonth() - 1); render(); });
        var title = el('div', 'cal-title', view.toLocaleDateString('en-US', { month: 'long', year: 'numeric' }));
        title.setAttribute('aria-live', 'polite');
        var next = el('button', 'cal-nav', '›');
        next.type = 'button';
        next.setAttribute('aria-label', 'Next month');
        next.disabled = view >= new Date(max.getFullYear(), max.getMonth(), 1);
        next.addEventListener('click', function () { view.setMonth(view.getMonth() + 1); render(); });
        head.append(prev, title, next);

        var grid = el('div', 'cal-grid');
        ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'].forEach(function (d) { grid.append(el('div', 'cal-dow', d)); });
        for (var i = 0; i < view.getDay(); i++) { grid.append(el('div', 'cal-day is-empty')); }

        var days = new Date(view.getFullYear(), view.getMonth() + 1, 0).getDate();
        for (var day = 1; day <= days; day++) {
            (function (d) {
                var btn = el('button', 'cal-day');
                btn.type = 'button';
                var open = d >= min && d <= max;
                btn.disabled = !open;
                var isSel = selected && iso(selected) === iso(d);
                if (isSel) { btn.classList.add('is-selected'); }
                btn.setAttribute('aria-pressed', isSel ? 'true' : 'false');
                btn.setAttribute('aria-label', d.toLocaleDateString('en-US', { weekday: 'long', month: 'long', day: 'numeric', year: 'numeric' }));
                btn.append(el('span', 'cal-num', String(d.getDate())));
                if (open && fromText) { btn.append(el('span', 'cal-from', 'from ' + fromText)); }
                btn.addEventListener('click', function () {
                    selected = d;
                    input.value = iso(d);
                    if (label) { label.textContent = d.toLocaleDateString('en-US', { weekday: 'short', month: 'short', day: 'numeric', year: 'numeric' }); }
                    render();
                });
                grid.append(btn);
            })(new Date(view.getFullYear(), view.getMonth(), day));
        }

        cal.append(head, grid);
    }

    render();
});
