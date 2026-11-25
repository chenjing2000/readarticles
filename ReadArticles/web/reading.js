import { byId, element } from "./dom.js";
import { pageId, hasSession, send } from "./bridge.js";

let currentMode = "passage";
let pendingModeRequest = null;
const readingScroll = { passage: 0, script: 0 };
let scriptGeometry = [];
let scriptGeometryFrame = 0;
const svgNamespace = "http://www.w3.org/2000/svg";
let beforeRender, refreshHighlights, isMutationPending, getPlayback;

export function configureReading(callbacks) {
  ({ beforeRender, refreshHighlights, isMutationPending, getPlayback } = callbacks);
}
export const getMode = () => currentMode;

export function initializeReading(payload) {
  pendingModeRequest = null;
  readingScroll.passage = 0;
  readingScroll.script = 0;
  renderArticle(payload.article, payload.mode === "script" ? "script" : "passage");
  byId("script-toggle").disabled = payload.article == null;
  updateReadingModeButton();
}

export function handleModeMessage(message) {
  if (!pendingModeRequest || message.requestId !== pendingModeRequest) return;
  try {
    updateReadingMode(message.payload?.mode, message.payload?.article);
    send("modeRendered", { requestId: message.requestId });
  } catch {
    send("modeRenderFailed", { requestId: message.requestId });
  }
}

export function completeModeRequest(message) {
  if (!pendingModeRequest || message.requestId !== pendingModeRequest) return false;
  pendingModeRequest = null;
  updateReadingModeButton();
  return true;
}

function renderArticle(article, mode = "passage", restoreScroll = false) {
  const body = byId("article-body");
  const title = byId("article-title");
  beforeRender();
  body.replaceChildren();
  scriptGeometry = [];
  if (scriptGeometryFrame) cancelAnimationFrame(scriptGeometryFrame);
  scriptGeometryFrame = 0;
  byId("article-scroll").scrollTop = 0;
  title.textContent = article?.title ?? "";
  currentMode = mode;
  updateReadingModeButton();
  if (!article) {
    body.append(element("p", "empty", "请在左侧图书馆中选择文章。"));
    return;
  }
  if (mode === "script") {
    renderScriptArticle(article, body);
    scheduleScriptGeometry();
    if (restoreScroll) byId("article-scroll").scrollTop = readingScroll.script;
    return;
  }
  for (const [paragraphIndex, paragraph] of (article.paragraphs ?? []).entries()) {
    const row = element("p", "article-paragraph");
    paragraph.forEach((sentence, index) => {
      if (index > 0) row.append(document.createTextNode(" "));
      const span = element("span", "sentence");
      span.dataset.paragraphIndex = String(paragraphIndex);
      span.dataset.sentenceIndex = String(index);

      // Passage blank tokens are display markers, not regular source words.
      const raw = String(sentence ?? "");
      const tokens = /\[\[(\d+)\]\]/g;
      let offset = 0;
      for (const found of raw.matchAll(tokens)) {
        if (found.index > offset)
          span.append(document.createTextNode(raw.slice(offset, found.index)));
        const blank = element("span", "blank", "____" + found[1] + "____");
        span.append(blank);
        offset = found.index + found[0].length;
      }
      if (offset < raw.length) span.append(document.createTextNode(raw.slice(offset)));
      row.append(span);
    });
    body.append(row);
  }
  if (restoreScroll) byId("article-scroll").scrollTop = readingScroll.passage;
}

// One Script timed unit retains one interval, even when it visually wraps.
function renderScriptArticle(article, host) {
  (article.paragraphs ?? []).forEach((paragraph, paragraphIndex) => {
    const row = element("p", "article-paragraph script-paragraph");
    row.dataset.paragraph = String(paragraphIndex);
    paragraph.forEach((sentence, sentenceIndex) => {
      const raw = String(sentence.text ?? "");
      if (sentenceIndex > 0) {
        const previous = String(paragraph[sentenceIndex - 1].text ?? "");
        if (previous && raw && !/\s$/u.test(previous) && !/^\s/u.test(raw))
          row.append(document.createTextNode(" "));
      }
      const span = element("span", "script-sentence");
      span.dataset.paragraphIndex = String(paragraphIndex);
      span.dataset.sentenceIndex = String(sentenceIndex);
      let cursor = 0;
      for (const unit of sentence.timedUnits ?? []) {
        const offset = Number(unit.offset), length = Number(unit.length);
        if (!unit.canColor || !Number.isInteger(offset) || !Number.isInteger(length) ||
            offset < cursor || length <= 0 || offset + length > raw.length ||
            raw.slice(offset, offset + length) !== unit.text) continue;
        if (offset > cursor) span.append(document.createTextNode(raw.slice(cursor, offset)));
        const timed = element("span", "script-timed-unit", raw.slice(offset, offset + length));
        timed.dataset.start = String(unit.start);
        timed.dataset.end = String(unit.end);
        span.append(timed);
        cursor = offset + length;
      }
      if (cursor < raw.length) span.append(document.createTextNode(raw.slice(cursor)));
      row.append(span);
    });

    // The red copy has identical text layout to the original and never
    // receives pointer/selection events; only its foreground is revealed.
    const paint = element("span", "script-paint");
    paint.setAttribute("aria-hidden", "true");
    for (const child of [...row.childNodes]) paint.append(child.cloneNode(true));
    const clipId = "script-clip-" + pageId.replace(/-/g, "") + "-" + paragraphIndex;
    const svg = document.createElementNS(svgNamespace, "svg");
    svg.setAttribute("class", "script-clips");
    svg.setAttribute("aria-hidden", "true");
    const clip = document.createElementNS(svgNamespace, "clipPath");
    clip.setAttribute("id", clipId);
    clip.setAttribute("clipPathUnits", "userSpaceOnUse");
    svg.append(clip);
    paint.style.clipPath = "url(#" + clipId + ")";
    row.append(paint, svg);
    host.append(row);
  });
}

export function scheduleScriptGeometry() {
  if (currentMode !== "script" || scriptGeometryFrame) return;
  scriptGeometryFrame = requestAnimationFrame(() => {
    scriptGeometryFrame = 0;
    measureScriptGeometry();
  });
}

function measureScriptGeometry() {
  if (currentMode !== "script") return;
  scriptGeometry = [];
  for (const row of byId("article-body").querySelectorAll(".script-paragraph")) {
    const paint = row.querySelector(":scope > .script-paint");
    const clip = row.querySelector(":scope > .script-clips clipPath");
    if (!paint || !clip) continue;
    clip.replaceChildren();
    const base = paint.getBoundingClientRect();
    for (const unit of row.querySelectorAll(":scope > .script-sentence .script-timed-unit")) {
      const start = Number(unit.dataset.start), end = Number(unit.dataset.end);
      if (!Number.isFinite(start) || !Number.isFinite(end) || end <= start) continue;
      const range = document.createRange();
      range.selectNodeContents(unit);
      const lineRects = [...range.getClientRects()]
        .filter(rect => rect.width > 0.25 && rect.height > 0.25)
        .sort((a, b) => a.top - b.top || a.left - b.left);
      const segments = [];
      for (const line of lineRects) {
        const rect = document.createElementNS(svgNamespace, "rect");
        rect.setAttribute("x", String(line.left - base.left));
        rect.setAttribute("y", String(line.top - base.top - 1));
        rect.setAttribute("width", "0");
        rect.setAttribute("height", String(line.height + 2));
        clip.append(rect);
        segments.push({ element: rect, width: line.width, painted: 0 });
      }
      if (segments.length) scriptGeometry.push({
        start, end, segments, width: segments.reduce((total, line) => total + line.width, 0)
      });
    }
  }
  paintScriptProgress(getPlayback());
}

export function paintScriptProgress(playback) {
  if (currentMode !== "script") return;
  const active = playback?.isArticleActive === true && playback?.canSeek === true;
  const seconds = Number(playback?.position);
  for (const unit of scriptGeometry) {
    const progress = active && Number.isFinite(seconds)
      ? Math.max(0, Math.min(1, (seconds - unit.start) / (unit.end - unit.start))) : 0;
    let remaining = progress * unit.width;
    for (const segment of unit.segments) {
      const painted = Math.max(0, Math.min(segment.width, remaining));
      if (segment.painted !== painted) {
        segment.element.setAttribute("width", String(painted));
        segment.painted = painted;
      }
      remaining -= segment.width;
    }
  }
}

export function updateReadingModeButton() {
  const button = byId("script-toggle");
  button.setAttribute("aria-pressed", String(currentMode === "script"));
  button.querySelector("img").src = currentMode === "script"
    ? "icons/show-passage.svg" : "icons/show-script.svg";
  button.disabled = !hasSession() || !byId("article-body").querySelector(".sentence, .script-sentence") ||
    !!pendingModeRequest || isMutationPending();
}

function updateReadingMode(next, content) {
  if (next !== "passage" && next !== "script") throw Error("Unknown reading mode");
  const body = byId("article-body");
  const previousNodes = [...body.childNodes];
  const previousTitle = byId("article-title").textContent;
  const previousMode = currentMode;
  const previousScroll = byId("article-scroll").scrollTop;
  readingScroll[previousMode] = previousScroll;
  try {
    renderArticle(content, next, true);
    refreshHighlights(true);
    if (next === "script") scheduleScriptGeometry();
  } catch (error) {
    if (scriptGeometryFrame) cancelAnimationFrame(scriptGeometryFrame);
    scriptGeometryFrame = 0;
    body.replaceChildren(...previousNodes);
    byId("article-title").textContent = previousTitle;
    currentMode = previousMode;
    byId("article-scroll").scrollTop = previousScroll;
    updateReadingModeButton();
    refreshHighlights(true);
    if (previousMode === "script") scheduleScriptGeometry();
    throw error;
  }
}

byId("script-toggle").addEventListener("click", () => {
  if (byId("script-toggle").disabled || !hasSession()) return;
  pendingModeRequest = send("switchReadingMode");
  updateReadingModeButton();
});
byId("article-scroll").addEventListener("scroll", () => {
  readingScroll[currentMode] = byId("article-scroll").scrollTop;
});
window.addEventListener("resize", scheduleScriptGeometry);
const scriptSizeObserver = new ResizeObserver(scheduleScriptGeometry);
scriptSizeObserver.observe(byId("article-scroll"));
