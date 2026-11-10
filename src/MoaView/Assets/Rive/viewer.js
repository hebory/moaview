"use strict";
const canvas = document.getElementById("canvas");
const artboard = document.getElementById("artboard");
const animation = document.getElementById("animation");
const pause = document.getElementById("pause");
const restart = document.getElementById("restart");
const status = document.getElementById("status");
let player = null;
let fileId = 0;
let paused = false;
let missingAssets = false;
rive.RuntimeLoader.setWasmUrl("https://moaview-rive.invalid/vendor/rive.wasm");
rive.RuntimeLoader.setWasmFallbackUrl("https://moaview-rive.invalid/vendor/rive_fallback.wasm");

function report(type, message) {
  status.textContent = message;
  window.chrome.webview.postMessage({ type, id: fileId, message });
}
function options(select, values) {
  select.replaceChildren(...values.map(([value, title]) => {
    const option = document.createElement("option");
    option.value = value;
    option.textContent = title;
    return option;
  }));
}
function playSelection() {
  if (!player) return;
  const selected = animation.value;
  const parameters = { artboard: artboard.value, autoplay: true };
  if (selected.startsWith("s:")) parameters.stateMachine = selected.slice(2);
  else if (selected.startsWith("a:")) parameters.animations = selected.slice(2);
  player.reset(parameters);
  player.resizeDrawingSurfaceToCanvas();
  paused = false;
  updatePlaybackButton();
}
function updatePlaybackButton() {
  const label = paused ? "재생" : "일시정지";
  pause.setAttribute("aria-label", label);
  pause.title = label;
  pause.classList.toggle("paused", paused);
}
function playbackOptions() {
  options(animation, [
    ...player.stateMachineNames.map(name => ["s:" + name, "상태 머신 · " + name]),
    ...player.animationNames.map(name => ["a:" + name, "애니메이션 · " + name]),
  ]);
  if (!animation.options.length) options(animation, [["", "정적 아트보드"]]);
  playSelection();
}
artboard.addEventListener("change", () => {
  player.reset({ artboard: artboard.value, autoplay: false });
  playbackOptions();
});
animation.addEventListener("change", playSelection);
restart.addEventListener("click", playSelection);
pause.addEventListener("click", () => {
  if (!player) return;
  paused = !paused;
  if (paused) player.pause(); else player.play();
  updatePlaybackButton();
});
new ResizeObserver(() => player?.resizeDrawingSurfaceToCanvas()).observe(canvas);
window.addEventListener("keydown", event => {
  if (event.key === "Escape") window.chrome.webview.postMessage({ type: "close", id: fileId });
});
window.chrome.webview.addEventListener("message", event => {
  const payload = event.data;
  if (payload.type === "theme") {
    const colors = ["background", "surface", "text", "muted", "control", "border"];
    if (colors.every(key => typeof payload[key] === "string" && /^#[0-9a-f]{6}$/i.test(payload[key]))) {
      const root = document.documentElement;
      for (const key of colors) root.style.setProperty("--app-" + key, payload[key]);
      root.style.colorScheme = payload.isDark ? "dark" : "light";
    }
    return;
  }
  if (payload.type !== "load" || typeof payload.base64 !== "string" || !Number.isSafeInteger(payload.id)) return;
  player?.cleanup();
  player = null;
  fileId = payload.id;
  const loadingId = fileId;
  missingAssets = false;
  pause.disabled = restart.disabled = true;
  report("loading", "Rive 애니메이션을 읽고 있습니다.");
  try {
    const bytes = Uint8Array.from(atob(payload.base64), c => c.charCodeAt(0));
    player = new rive.Rive({
      buffer: bytes.buffer, canvas, autoplay: false,
      enableRiveAssetCDN: false, automaticallyHandleEvents: false,
      layout: new rive.Layout({ fit: rive.Fit.Contain, alignment: rive.Alignment.Center }),
      assetLoader: (_asset, embeddedBytes) => { if (!embeddedBytes.length) missingAssets = true; return false; },
      onLoad: () => {
        if (loadingId !== fileId) return;
        try {
          options(artboard, player.contents.artboards.map(item => [item.name, item.name]));
          artboard.value = player.activeArtboard;
          if (!artboard.value && artboard.options.length) artboard.selectedIndex = 0;
          playbackOptions();
          pause.disabled = restart.disabled = false;
          report("ready", missingAssets ? "외부 이미지·폰트 리소스는 표시되지 않을 수 있습니다. 파일에 리소스를 포함해 내보내 주세요." : "로컬 재생 · 아트보드와 애니메이션을 선택할 수 있습니다.");
        } catch { report("error", "Rive 아트보드를 표시하지 못했습니다."); }
      },
      onLoadError: () => { if (loadingId === fileId) report("error", "Rive 파일이 손상되었거나 런타임에서 지원하지 않는 형식입니다."); },
    });
  } catch { report("error", "Rive 파일을 읽지 못했습니다."); }
});
window.addEventListener("pagehide", () => { player?.cleanup(); player = null; });
window.chrome.webview.postMessage({ type: "pageReady" });
