document.addEventListener('click', async (e) => {
  const btn = e.target.closest('.btn-favorito'); if (!btn) return;
  const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
  const r = await fetch('/Favoritos/Alternar', {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded', 'RequestVerificationToken': token ?? '' },
    body: 'id=' + encodeURIComponent(btn.dataset.id)
  });
  if (!r.ok) { alert((await r.json()).mensaje ?? 'No se pudo guardar.'); return; }
  const { favorito } = await r.json();
  btn.textContent = favorito ? '♥' : '♡';
  btn.setAttribute('aria-pressed', favorito);
});