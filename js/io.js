// TremorScope Web — 保存・印刷・この端末への記録の保存（どこにも送らない）

export function download(name, mime, bytes) {
  const blob = new Blob([bytes], { type: mime });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url; a.download = name; a.rel = 'noopener';
  document.body.appendChild(a); a.click(); a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 30000);
}

export function print() { window.print(); }

export function load(key) { try { return localStorage.getItem(key); } catch { return null; } }
export function save(key, value) {
  try { localStorage.setItem(key, value); return true; } catch { return false; }
}
export function remove(key) { try { localStorage.removeItem(key); } catch { } }

export function scrollTop() { window.scrollTo({ top: 0 }); document.querySelector('.main')?.scrollTo({ top: 0 }); }
