namespace Lumeshow.Control.Remote;

/// <summary>The one web page of the phone remote. It has everything inline, so it needs no internet.</summary>
public static class RemotePage
{
    public const string Html = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover, user-scalable=no">
<meta name="theme-color" content="#111318">
<title>Lumeshow remote</title>
<style>
  :root { --bg:#111318; --card:#1b1e25; --line:#2c313c; --text:#f2f4f8; --dim:#9aa3b2; --accent:#3b82f6; --danger:#e5484d; }
  * { box-sizing:border-box; -webkit-tap-highlight-color:transparent; }
  html, body { margin:0; background:var(--bg); color:var(--text); font:16px/1.4 system-ui, "Segoe UI", sans-serif; }
  body { padding:12px 16px calc(16px + env(safe-area-inset-bottom)); max-width:720px; margin:0 auto; user-select:none; -webkit-user-select:none; }
  header { display:flex; align-items:center; justify-content:space-between; gap:12px; margin-bottom:10px; }
  h1 { font-size:17px; margin:0; font-weight:600; }
  #link { font-size:13px; color:var(--dim); }
  #link.bad { color:var(--danger); }
  .card { background:var(--card); border:1px solid var(--line); border-radius:14px; padding:14px; margin-bottom:12px; }
  .label { font-size:12px; color:var(--dim); text-transform:uppercase; letter-spacing:.06em; }
  #cue { font-size:20px; font-weight:600; min-height:28px; overflow-wrap:anywhere; }
  #next { color:var(--dim); font-size:14px; min-height:20px; overflow-wrap:anywhere; }
  #bar { height:4px; background:var(--line); border-radius:2px; margin-top:10px; overflow:hidden; }
  #fill { height:100%; width:0; background:var(--accent); transition:width .25s linear; }
  button { font:inherit; color:var(--text); background:#262a33; border:1px solid var(--line); border-radius:12px; padding:14px 8px; min-height:52px; touch-action:manipulation; }
  button:active { filter:brightness(1.35); }
  #go { width:100%; min-height:84px; font-size:28px; font-weight:700; background:var(--accent); border-color:var(--accent); margin-bottom:10px; }
  .row { display:grid; grid-template-columns:repeat(3, 1fr); gap:10px; }
  .row2 { display:grid; grid-template-columns:1fr 1fr; gap:10px; }
  #blackout.on { background:var(--danger); border-color:var(--danger); font-weight:700; }
  #master { width:100%; margin:14px 0 4px; accent-color:var(--accent); height:32px; touch-action:pan-y; }
  .between { display:flex; justify-content:space-between; align-items:baseline; }
  #buttons { display:grid; grid-template-columns:repeat(auto-fill, minmax(96px, 1fr)); gap:10px; }
  #buttons button { min-height:72px; border-width:2px; border-color:var(--c); background:color-mix(in srgb, var(--c) 18%, #1b1e25); overflow-wrap:anywhere; }
  #buttons button.on { background:var(--c); color:#fff; font-weight:700; box-shadow:0 0 14px var(--c); }
  #empty { color:var(--dim); font-size:14px; }
  dialog { background:var(--card); color:var(--text); border:1px solid var(--line); border-radius:14px; padding:18px; width:min(320px, 90vw); }
  dialog::backdrop { background:rgba(0,0,0,.6); }
  input[type=password], input[type=text] { width:100%; font:inherit; font-size:22px; letter-spacing:.2em; text-align:center; padding:10px; border-radius:10px; border:1px solid var(--line); background:var(--bg); color:var(--text); margin:12px 0; }
  #pinMsg { color:var(--danger); font-size:14px; min-height:20px; }
</style>
</head>
<body>
<header><h1>Lumeshow remote</h1><span id="link">Connecting…</span></header>

<div class="card">
  <div class="label">Now</div>
  <div id="cue">–</div>
  <div id="next"></div>
  <div id="bar"><div id="fill"></div></div>
</div>

<button id="go">GO</button>
<div class="row" style="margin-bottom:12px">
  <button id="back">Back</button>
  <button id="pause">Pause</button>
  <button id="release">Release</button>
</div>

<div class="card">
  <div class="between"><span class="label">Master</span><span id="masterText">100%</span></div>
  <input id="master" type="range" min="0" max="100" value="100" aria-label="Master">
  <div class="row2">
    <button id="blackout">Blackout</button>
    <button id="tap">Tap · <span id="bpm">120</span> BPM</button>
  </div>
</div>

<div class="card">
  <div class="label" style="margin-bottom:10px">Cue buttons</div>
  <div id="buttons"></div>
  <div id="empty" hidden>This show has no cue buttons yet. Add them in Lumeshow under Cue → Buttons.</div>
</div>

<dialog id="pinDialog">
  <form method="dialog" id="pinForm">
    <div>Enter the PIN shown in Lumeshow<br><span class="label">Output → Phone remote</span></div>
    <input id="pin" type="password" inputmode="numeric" autocomplete="off" maxlength="12" aria-label="PIN">
    <div id="pinMsg"></div>
    <button style="width:100%">OK</button>
  </form>
</dialog>

<script>
(() => {
  const $ = id => document.getElementById(id);
  let pin = localStorage.getItem("lumePin") || "";
  let masterBusyUntil = 0, asking = false, shown = "";

  async function call(command, params, method) {
    const q = new URLSearchParams(params || {});
    if (pin) q.set("pin", pin);
    let r;
    try { r = await fetch("/api/" + command + "?" + q, { method: method || "POST", cache: "no-store" }); }
    catch { link("No connection", true); return null; }
    if (r.status === 401 || r.status === 429) { askPin(r.status === 429 ? "Too many wrong PINs. Wait half a minute." : (pin ? "Wrong PIN" : "")); return null; }
    link("Connected", false);
    if (!r.ok) return null;
    const state = await r.json();
    show(state);
    return state;
  }

  function link(text, bad) { $("link").textContent = text; $("link").className = bad ? "bad" : ""; }

  function askPin(message) {
    link("PIN needed", true);
    $("pinMsg").textContent = message;
    if (asking) return;
    asking = true;
    $("pin").value = "";
    $("pinDialog").showModal();
  }
  $("pinForm").addEventListener("submit", () => {
    pin = $("pin").value.trim();
    localStorage.setItem("lumePin", pin);
    asking = false;
    refresh();
  });

  function show(s) {
    $("cue").textContent = s.cue ? s.cue + (s.paused ? " (paused)" : "") : "No cue running";
    $("next").textContent = s.next ? "Next: " + s.next : "";
    $("fill").style.width = (s.fading ? s.progress * 100 : 0) + "%";
    $("pause").textContent = s.paused ? "Resume" : "Pause";
    $("blackout").classList.toggle("on", s.blackout);
    $("bpm").textContent = s.bpm;
    if (Date.now() > masterBusyUntil) { $("master").value = s.master; $("masterText").textContent = s.master + "%"; }

    // Rebuild the buttons only when the set changes, so a held flash button is not replaced under the finger.
    const key = JSON.stringify(s.buttons.map(b => [b.id, b.name, b.color, b.flash]));
    if (key !== shown) {
      shown = key;
      const box = $("buttons");
      box.replaceChildren();
      for (const b of s.buttons) {
        const el = document.createElement("button");
        el.textContent = b.name || "Button";
        el.dataset.id = b.id;
        el.style.setProperty("--c", /^#[0-9a-f]{6}$/i.test(b.color) ? b.color : "#3b82f6");
        if (b.flash) {
          const set = on => call("button", { id: b.id, on: on ? 1 : 0 });
          el.addEventListener("pointerdown", e => { el.setPointerCapture(e.pointerId); set(true); });
          el.addEventListener("pointerup", () => set(false));
          el.addEventListener("pointercancel", () => set(false));
          el.addEventListener("contextmenu", e => e.preventDefault());
        } else {
          el.addEventListener("click", () => call("button", { id: b.id }));
        }
        box.appendChild(el);
      }
      $("empty").hidden = s.buttons.length > 0;
    }
    for (const el of $("buttons").children)
      el.classList.toggle("on", !!s.buttons.find(b => b.id === el.dataset.id && b.on));
  }

  for (const id of ["go", "back", "pause", "tap"]) $(id).addEventListener("click", () => call(id));
  $("blackout").addEventListener("click", () => call("blackout"));
  $("release").addEventListener("click", () => { if (confirm("Release the cue list and all cue buttons?")) call("release"); });
  $("master").addEventListener("input", () => {
    masterBusyUntil = Date.now() + 1500;
    $("masterText").textContent = $("master").value + "%";
    call("master", { value: $("master").value });
  });

  const refresh = () => { if (!asking && !document.hidden) call("state", null, "GET"); };
  setInterval(refresh, 700);
  document.addEventListener("visibilitychange", refresh);
  refresh();
})();
</script>
</body>
</html>
""";
}
