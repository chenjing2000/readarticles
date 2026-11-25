import { byId } from "./dom.js";
import { hasSession, send } from "./bridge.js";
import { paintScriptProgress } from "./reading.js";

let currentPlayback = {};
let seekDragging = false;
export const getPlayback = () => ({ ...currentPlayback });
export const isSeekDragging = () => seekDragging;

export function formatSeconds(seconds) {
  const value = Math.max(0, Math.floor(Number(seconds) || 0));
  const minutes = Math.floor(value / 60);
  const secondsPart = String(value % 60).padStart(2, "0");
  if (value <= 3600) return String(minutes).padStart(2, "0") + ":" + secondsPart;
  return String(Math.floor(value / 3600)).padStart(2, "0") + ":" +
    String(minutes % 60).padStart(2, "0") + ":" + secondsPart;
}

export function renderPlayback(playback) {
  currentPlayback = playback;
  const position = Number(playback.position) || 0;
  const duration = Number(playback.duration) || 0;
  const progress = byId("progress");
  progress.disabled = playback.canSeek !== true;
  if (progress.disabled) seekDragging = false;
  progress.max = String(Math.max(duration, 1));
  if (!seekDragging) progress.value = String(Math.max(0, Math.min(position, Math.max(duration, 1))));
  byId("playback-time").textContent =
    formatSeconds(seekDragging ? progress.value : position) + " / " + formatSeconds(duration);
  byId("article-play").disabled = playback.canPlay !== true;
  const playButton = byId("article-play");
  playButton.querySelector("img").src = playback.state === "playing"
    ? "icons/pause.svg" : "icons/play.svg";
  playButton.setAttribute("aria-label", playback.state === "playing" ? "暂停文章音频" : "播放文章音频");
  byId("article-stop").disabled = playback.isArticleActive !== true;
  paintScriptProgress(playback);
}

// Reserve playback keys before focused controls receive them.
function playbackShortcut(event) {
  if (event.ctrlKey || event.metaKey || event.altKey || event.shiftKey ||
      ![" ", "Spacebar", "ArrowLeft", "ArrowRight"].includes(event.key)) return;
  event.preventDefault();
  event.stopImmediatePropagation();
  if (event.type !== "keydown" || !hasSession()) return;
  if (event.key === " " || event.key === "Spacebar") {
    if (!event.repeat && currentPlayback.canPlay === true) send("playArticle");
  } else if (currentPlayback.canSeek === true) {
    send("seekArticle", { payload: { direction: event.key === "ArrowLeft" ? "backward" : "forward" } });
  }
}
document.addEventListener("keydown", playbackShortcut, true);
document.addEventListener("keyup", playbackShortcut, true);

byId("article-play").addEventListener("click", () => {
  if (!byId("article-play").disabled) send("playArticle");
});
byId("article-stop").addEventListener("click", () => {
  if (!byId("article-stop").disabled) send("stopArticle");
});
byId("progress").addEventListener("pointerdown", () => { seekDragging = true; });
// A click without a value change does not emit "change". Never retain dragging
// after release, cancellation, lost focus, or stopping the current audio.
document.addEventListener("pointerup", () => { seekDragging = false; });
document.addEventListener("pointercancel", () => { seekDragging = false; });
byId("progress").addEventListener("blur", () => { seekDragging = false; });
byId("progress").addEventListener("input", () => {
  seekDragging = true;
  renderPlayback(currentPlayback);
});
byId("progress").addEventListener("change", () => {
  const seconds = Number(byId("progress").value);
  seekDragging = false;
  if (Number.isFinite(seconds) && currentPlayback.canSeek === true)
    send("seekArticle", { payload: { position: seconds } });
});
