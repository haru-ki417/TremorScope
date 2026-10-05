// TremorScope Web — 測定中のふるえの波形（直近 6 秒）。値は mg（ふるえの成分だけ）
const views = new Map();
const SECONDS = 6, RATE = 50;

class Wave {
  constructor(host) {
    this.host = host;
    this.canvas = document.createElement('canvas');
    host.appendChild(this.canvas);
    this.ctx = this.canvas.getContext('2d');
    this.data = []; this.color = '#0E8A8A'; this.scale = 20;
    new ResizeObserver(() => this.resize()).observe(host);
    this.resize();
  }
  resize() {
    const r = this.host.getBoundingClientRect(), d = window.devicePixelRatio || 1;
    this.w = Math.max(1, r.width); this.h = Math.max(1, r.height); this.dpr = d;
    this.canvas.width = Math.round(this.w * d); this.canvas.height = Math.round(this.h * d);
    this.canvas.style.width = this.w + 'px'; this.canvas.style.height = this.h + 'px';
    this.draw();
  }
  push(values) {
    for (const v of values) this.data.push(v);
    const max = SECONDS * RATE;
    if (this.data.length > max) this.data.splice(0, this.data.length - max);
    this.draw();
  }
  reset() { this.data = []; this.draw(); }
  draw() { if (!this.raf) this.raf = requestAnimationFrame(() => { this.raf = 0; this.render(); }); }
  render() {
    const c = this.ctx, w = this.w, h = this.h;
    c.setTransform(this.dpr, 0, 0, this.dpr, 0, 0);
    c.clearRect(0, 0, w, h);
    // 目盛り（縦は自動で広げ、ゆっくり戻す）
    let peak = 0; for (const v of this.data) peak = Math.max(peak, Math.abs(v));
    const target = Math.max(10, peak * 1.25);
    this.scale = target > this.scale ? target : this.scale + (target - this.scale) * 0.05;
    const mid = h / 2, k = (h / 2 - 8) / this.scale;
    c.strokeStyle = '#DEDBD3'; c.lineWidth = 1;
    c.beginPath(); c.moveTo(0, mid); c.lineTo(w, mid); c.stroke();
    c.fillStyle = '#8A909A'; c.font = '11px system-ui, sans-serif';
    c.fillText(`±${Math.round(this.scale)} mg`, 8, 14);
    if (this.data.length < 2) return;
    const n = SECONDS * RATE, dx = w / (n - 1), x0 = w - (this.data.length - 1) * dx;
    c.strokeStyle = this.color; c.lineWidth = 1.6; c.lineJoin = 'round';
    c.beginPath();
    this.data.forEach((v, i) => { const x = x0 + i * dx, y = mid - v * k; i ? c.lineTo(x, y) : c.moveTo(x, y); });
    c.stroke();
  }
}

export function create(id) { const el = document.getElementById(id); if (el && !views.has(id)) views.set(id, new Wave(el)); }
export function push(id, values) { views.get(id)?.push(values); }
export function reset(id) { views.get(id)?.reset(); }
export function color(id, c) { const v = views.get(id); if (v) { v.color = c; v.draw(); } }
export function dispose(id) { views.delete(id); }
