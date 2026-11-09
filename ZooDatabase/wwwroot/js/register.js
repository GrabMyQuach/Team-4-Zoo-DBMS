// Register: tap item buttons to build a receipt, fix quantities, then Complete Sale.
// The page only sends item ids and quantities; the server looks up prices.
document.querySelectorAll('[data-pos]').forEach(function (form) {
    var lines = form.querySelector('[data-pos-lines]');
    var empty = form.querySelector('[data-pos-empty]');
    var inputs = form.querySelector('[data-pos-inputs]');
    var totalEl = form.querySelector('[data-pos-total]');
    var submit = form.querySelector('[data-pos-submit]');
    var money = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' });
    var items = {};   // id -> { name, price, max, qty }

    form.querySelectorAll('.pos-item').forEach(function (btn) {
        items[btn.dataset.id] = { name: btn.dataset.name, price: parseFloat(btn.dataset.price), max: parseInt(btn.dataset.max, 10), qty: parseInt(btn.dataset.start || '0', 10) || 0 };
        btn.addEventListener('click', function () { change(btn.dataset.id, 1); });
    });

    function change(id, by) {
        var it = items[id];
        it.qty = Math.max(0, Math.min(it.max, it.qty + by));
        render();
    }

    function render() {
        lines.textContent = '';
        inputs.textContent = '';
        var total = 0, count = 0;
        Object.keys(items).forEach(function (id) {
            var it = items[id];
            if (it.qty === 0) { return; }
            total += it.qty * it.price;
            count += it.qty;

            var row = document.createElement('div');
            row.className = 'pos-line';
            var name = document.createElement('span');
            name.className = 'pos-line-name';
            name.textContent = it.name;
            var minus = document.createElement('button');
            minus.type = 'button';
            minus.textContent = '−';
            minus.setAttribute('aria-label', 'One less ' + it.name);
            minus.addEventListener('click', function () { change(id, -1); });
            var qty = document.createElement('span');
            qty.className = 'pos-line-qty';
            qty.textContent = it.qty;
            var plus = document.createElement('button');
            plus.type = 'button';
            plus.textContent = '+';
            plus.setAttribute('aria-label', 'One more ' + it.name);
            plus.addEventListener('click', function () { change(id, 1); });
            var amount = document.createElement('span');
            amount.className = 'pos-line-amount';
            amount.textContent = money.format(it.qty * it.price);
            row.append(name, minus, qty, plus, amount);
            lines.append(row);

            var hidden = document.createElement('input');
            hidden.type = 'hidden';
            hidden.name = 'Qty[' + id + ']';
            hidden.value = it.qty;
            inputs.append(hidden);
        });
        totalEl.textContent = money.format(total);
        empty.hidden = count > 0;
        submit.disabled = count === 0;
    }

    form.querySelector('[data-pos-clear]').addEventListener('click', function () {
        Object.keys(items).forEach(function (id) { items[id].qty = 0; });
        render();
    });

    // Stop a double tap from ringing the same sale up twice
    form.addEventListener('submit', function () { submit.disabled = true; submit.textContent = 'Saving…'; });

    render();
});
