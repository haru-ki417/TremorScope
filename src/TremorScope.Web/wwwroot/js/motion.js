// TremorScope Web — スマホ・タブレットの加速度センサーを、50 Hz の等間隔の値にして C# に渡す。
// 値はこの端末の中だけで使い、どこにも送らない。
//   ブラウザーの devicemotion は端末によって 60 Hz 前後で、間隔も少し揺れる。
//   解析は等間隔を前提にしているので、届いた値を時刻で直線補間し、20 ms ごとの値を作る。
//   0.2 秒より長く途切れたら（画面が消えた・別のアプリに切りかえた など）、その間は作らずに通し番号だけ進める
//   （C# の RecordingBuffer が「届かなかった点」として数え、品質の確認に使う）。

const RATE = 50, STEP = 1000 / RATE, GAP_MS = 200, PACKET_MS = 500, G = 9.80665;
let dotnet = null, running = false, timer = 0;
let last = null;          // 直前に届いた値 { t, x, y, z }
let nextT = null;         // 次に作る値の時刻
let seq = 0;              // 作った値の通し番号
let out = { x: [], y: [], z: [] }, outSeq = 0;
let eventCount = 0, eventSince = 0, nativeRate = 0;
let wakeLock = null;

export function supported() {
  return typeof DeviceMotionEvent !== 'undefined' && (navigator.maxTouchPoints > 0 || /Android|iPhone|iPad/i.test(navigator.userAgent));
}

export function needsPermission() {
  return typeof DeviceMotionEvent !== 'undefined' && typeof DeviceMotionEvent.requestPermission === 'function';
}

// iPhone・iPad では、ボタンを押したその場で許可を求める必要がある（C# を経由すると間に合わないことがある）
export function bindPermissionButton(id, ref) {
  dotnet = ref;
  const el = document.getElementById(id);
  if (!el || el.dataset.bound) return;
  el.dataset.bound = '1';
  el.addEventListener('click', async () => {
    let state = 'granted';
    if (needsPermission()) {
      try { state = await DeviceMotionEvent.requestPermission(); } catch { state = 'denied'; }
    }
    if (state === 'granted') start();
    dotnet.invokeMethodAsync('OnPermission', state === 'granted');
  });
}

function onMotion(e) {
  const a = e.accelerationIncludingGravity;
  if (!a || a.x === null) return;
  const t = e.timeStamp || performance.now();
  const cur = { t, x: a.x / G, y: a.y / G, z: a.z / G };
  eventCount++;
  if (!eventSince) eventSince = t;
  if (t - eventSince >= 2000) { nativeRate = eventCount * 1000 / (t - eventSince); eventCount = 0; eventSince = t; }
  if (!last) { last = cur; nextT = t; return; }
  if (t <= last.t) return;
  if (t - last.t > GAP_MS) {
    // 途切れ: 作らなかった分だけ通し番号を進める
    const skipped = Math.floor((t - nextT) / STEP);
    if (skipped > 0) { flush(); seq += skipped; outSeq = seq; nextT += skipped * STEP; }
    last = cur; return;
  }
  while (nextT <= t) {
    const f = (nextT - last.t) / (t - last.t);
    out.x.push(last.x + (cur.x - last.x) * f);
    out.y.push(last.y + (cur.y - last.y) * f);
    out.z.push(last.z + (cur.z - last.z) * f);
    seq++; nextT += STEP;
  }
  last = cur;
}

function flush() {
  if (!dotnet || out.x.length === 0) return;
  const p = out, s = outSeq;
  out = { x: [], y: [], z: [] }; outSeq = seq;
  dotnet.invokeMethodAsync('OnPacket', p.x, p.y, p.z, s, Math.round(nativeRate));
}

export function start(ref) {
  if (ref) dotnet = ref;
  if (running) return;
  running = true; last = null; nextT = null; seq = 0; outSeq = 0; out = { x: [], y: [], z: [] };
  eventCount = 0; eventSince = 0; nativeRate = 0;
  window.addEventListener('devicemotion', onMotion);
  timer = setInterval(flush, PACKET_MS);
}

export function stop() {
  running = false;
  window.removeEventListener('devicemotion', onMotion);
  clearInterval(timer);
}

// 測定中は画面を消さない。始まり・終わりを振動で知らせる（対応している端末だけ）
export async function keepAwake(on) {
  try {
    if (on && !wakeLock && 'wakeLock' in navigator) wakeLock = await navigator.wakeLock.request('screen');
    if (!on && wakeLock) { await wakeLock.release(); wakeLock = null; }
  } catch { wakeLock = null; }
}
export function buzz(ms) { try { navigator.vibrate && navigator.vibrate(ms); } catch { } }
