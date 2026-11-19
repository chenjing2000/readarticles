"use strict";

// WebView2 DOM pilot. C# owns vocabulary files and audio; UI matching stays local.
const pageId = crypto.randomUUID();
let sessionId = null;
let vocabularyRevision = 0;
let vocabulary = [];
let highlightsVisible = false;
let selectedWord = null;
let selectedIndex = -1;
let canWriteVocabulary = false;
let matches = [];
let selectedPassage = null;
let pendingMutation = null;
let currentSettings = {};
const toastEntries = new Map();
let splitting = null;
let splitterDragFrame = 0;
let currentMode = "passage";
let pendingModeRequest = null;
let currentPlayback = {};
let seekDragging = false;
const readingScroll = { passage: 0, script: 0 };
let scriptGeometry = [];
let scriptGeometryFrame = 0;
const svgNamespace = "http://www.w3.org/2000/svg";

const byId = id => document.getElementById(id);

function element(tag, className, value) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (value !== undefined && value !== null) node.textContent = String(value);
  return node;
}

// Match legacy VocabularyMatcher. Boundary letters deliberately exclude digits.
function englishBoundary(c) {
  return /[A-Za-z'’\-]/.test(c ?? "");
}

function ordinalFold(value) {
  // Avoid code-unit shifts caused by multi-character Unicode case expansions.
  return Array.from(value, c => {
    const lower = c.toLowerCase();
    return lower.length === c.length ? lower : c;
  }).join("");
}

function findWholeWord(text, word, from) {
  const source = ordinalFold(text);
  const target = ordinalFold(word);
  let start = source.indexOf(target, from);
  while (start >= 0) {
    const end = start + word.length;
    if ((start === 0 || !englishBoundary(text[start - 1])) &&
        (end === text.length || !englishBoundary(text[end]))) return start;
    start = source.indexOf(target, start + 1);
  }
  return -1;
}

function matchVocabulary(text, entries) {
  const seen = new Set();
  const words = [];
  for (const entry of entries) {
    const word = String(entry).trim();
    const key = ordinalFold(word);
    if (!word || seen.has(key)) continue;
    seen.add(key);
    words.push(word);
  }
  const result = [];
  let cursor = 0;
  while (cursor < text.length) {
    let bestStart = -1;
    let bestWord = null;
    for (const word of words) {
      const start = findWholeWord(text, word, cursor);
      if (start >= 0 && (bestStart < 0 || start < bestStart ||
          (start === bestStart && word.length > bestWord.length))) {
        bestStart = start;
        bestWord = word;
      }
    }
    if (bestWord === null) break;
    result.push({ start: bestStart, length: bestWord.length, key: bestWord });
    cursor = bestStart + bestWord.length;
  }
  return result;
}

function renderArticle(article, mode = "passage", restoreScroll = false) {
  const body = byId("article-body");
  const title = byId("article-title");
  hideSelectionAdd();
  pendingMutation = null;
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
  for (const paragraph of article.paragraphs ?? []) {
    const row = element("p", "article-paragraph");
    paragraph.forEach((sentence, index) => {
      if (index > 0) row.append(document.createTextNode(" "));
      const span = element("span", "sentence");
      if (sentence.sid) span.dataset.sid = sentence.sid;
      if (sentence.hasAudio === true) {
        span.dataset.canAudio = "true";
        span.addEventListener("contextmenu", event => {
          event.preventDefault();
          window.getSelection()?.removeAllRanges();
          send("playSegment", { payload: { sid: sentence.sid } });
        });
      }

      // Passage blank tokens are display markers, not regular source words.
      const raw = String(sentence.text ?? "");
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

function scheduleScriptGeometry() {
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
        segments.push({ element: rect, width: line.width });
      }
      if (segments.length) scriptGeometry.push({ start, end, segments });
    }
  }
  paintScriptProgress(currentPlayback);
}

function paintScriptProgress(playback) {
  if (currentMode !== "script") return;
  const active = playback?.isArticleActive === true && playback?.canSeek === true;
  const seconds = Number(playback?.position);
  for (const unit of scriptGeometry) {
    const progress = active && Number.isFinite(seconds)
      ? Math.max(0, Math.min(1, (seconds - unit.start) / (unit.end - unit.start))) : 0;
    let remaining = progress * unit.segments.reduce((total, line) => total + line.width, 0);
    for (const segment of unit.segments) {
      const painted = Math.max(0, Math.min(segment.width, remaining));
      segment.element.setAttribute("width", String(painted));
      remaining -= segment.width;
    }
  }
}

function renderVocabulary(words) {
  const scroller = byId("vocabulary-scroll");
  const previousOffset = scroller.scrollTop;
  const list = byId("vocabulary-list");
  list.replaceChildren();
  byId("vocabulary-count").textContent = words.length ? "(" + words.length + ")" : "";
  words.forEach((word, index) => {
    const row = element("article", "word-row");
    row.dataset.word = word.word;
    if (selectedIndex === index)
      row.classList.add("selected");
    row.tabIndex = 0;
    row.setAttribute("role", "button");
    row.setAttribute("aria-label", "定位词汇 " + word.word);
    row.append(element("div", "word-head", word.word));
    row.append(element("div", "phonetics",
      "UK " + (word.phoneticUk ?? "—") + "  |  US " + (word.phoneticUs ?? "—")));
    const pronunciations = element("div", "word-pronunciations");
    for (const accent of ["uk", "us"]) {
      const button = element("button", "", accent === "uk" ? "▶ 英音" : "▶ 美音");
      button.type = "button";
      button.disabled = accent === "uk" ? !word.canPlayUk : !word.canPlayUs;
      button.title = accent === "uk" ? "播放英音" : "播放美音";
      button.addEventListener("click", event => {
        event.stopPropagation();
        if (!button.disabled && sessionId) {
          send("playWord", {
            payload: { index, word: word.word, accent }
          });
        }
      });
      pronunciations.append(button);
    }
    row.append(pronunciations);
    for (const meaning of word.meanings ?? []) {
      const line = element("p", "meaning");
      line.append(element("span", "part-of-speech", meaning.pos));
      line.append(document.createTextNode(meaning.text ?? ""));
      row.append(line);
    }
    row.addEventListener("click", () => focusVocabulary(word.word, index));
    row.addEventListener("keydown", event => {
      if (event.key === "Enter" || event.key === " " || event.key === "Spacebar") {
        event.preventDefault();
        event.stopPropagation();
        focusVocabulary(word.word, index);
      }
    });
    list.append(row);
  });
  scroller.scrollTop = previousOffset;
  updateEditControls();
}

function clearHighlights() {
  if (CSS.highlights) {
    CSS.highlights.delete("reader-vocabulary");
    CSS.highlights.delete("reader-focused");
  }
}

function refreshHighlights(recalculate) {
  if (recalculate) {
    matches = [];
    const words = vocabulary.map(entry => entry.word);
    if (currentMode === "script") {
      // Match the complete sentence, even when a vocabulary phrase crosses
      // two timed units. Range endpoints may sit in different text nodes.
      for (const sentence of byId("article-body").querySelectorAll(
        ".script-paragraph > .script-sentence")) {
        const parts = [];
        let source = "";
        const walker = document.createTreeWalker(sentence, NodeFilter.SHOW_TEXT);
        while (walker.nextNode()) {
          const node = walker.currentNode;
          const length = node.textContent?.length ?? 0;
          if (!length) continue;
          parts.push({ node, start: source.length, length });
          source += node.textContent;
        }
        for (const hit of matchVocabulary(source, words)) {
          const begin = parts.find(p => hit.start >= p.start && hit.start < p.start + p.length);
          const endOffset = hit.start + hit.length;
          const end = parts.find(p => endOffset > p.start && endOffset <= p.start + p.length);
          if (!begin || !end) continue;
          const range = document.createRange();
          range.setStart(begin.node, hit.start - begin.start);
          range.setEnd(end.node, endOffset - end.start);
          matches.push({ word: hit.key, range });
        }
      }
    } else {
      // Match each Passage text fragment separately, preserving blank boundaries.
      for (const sentence of byId("article-body").querySelectorAll(".sentence")) {
        for (const child of sentence.childNodes) {
          if (child.nodeType !== Node.TEXT_NODE || !child.textContent) continue;
          for (const hit of matchVocabulary(child.textContent, words)) {
            const range = document.createRange();
            range.setStart(child, hit.start);
            range.setEnd(child, hit.start + hit.length);
            matches.push({ word: hit.key, range });
          }
        }
      }
    }
  }
  clearHighlights();
  if (!highlightsVisible || !CSS.highlights || typeof Highlight === "undefined") return;
  if (matches.length)
    CSS.highlights.set("reader-vocabulary", new Highlight(...matches.map(match => match.range)));
  if (selectedWord !== null) {
    const selected = matches.filter(match => ordinalFold(match.word) === ordinalFold(selectedWord));
    if (selected.length)
      CSS.highlights.set("reader-focused", new Highlight(...selected.map(match => match.range)));
  }
}

function focusVocabulary(word, index) {
  selectedWord = word;
  selectedIndex = index;
  for (const row of byId("vocabulary-list").querySelectorAll(".word-row")) {
    const selected = ordinalFold(row.dataset.word ?? "") === ordinalFold(word);
    row.classList.toggle("selected", selected);
    row.setAttribute("aria-pressed", String(selected));
  }
  refreshHighlights(false);
  updateEditControls();
  const first = matches.find(match => ordinalFold(match.word) === ordinalFold(word));
  if (!first) return;
  const box = first.range.getBoundingClientRect();
  const scroller = byId("article-scroll");
  const view = scroller.getBoundingClientRect();
  if (box.top < view.top + 20 || box.bottom > view.bottom - 20)
    scroller.scrollTop += box.top - view.top - Math.round(view.height / 3);
}

function sentenceFor(node) {
  const parent = node.nodeType === Node.ELEMENT_NODE ? node : node.parentElement;
  const sentence = parent?.closest(".sentence, .script-sentence") ?? null;
  return sentence?.closest(".script-paint") ? null : sentence;
}

function hideSelectionAdd() {
  selectedPassage = null;
  byId("selection-add").hidden = true;
}

function updateSelectionAdd() {
  const selection = window.getSelection();
  if (!selection || selection.isCollapsed || selection.rangeCount !== 1) {
    hideSelectionAdd();
    return;
  }
  const range = selection.getRangeAt(0);
  const sentence = sentenceFor(range.startContainer);
  if (!sentence || sentence !== sentenceFor(range.endContainer) ||
      !byId("article-body").contains(sentence) ||
      (currentMode === "passage" && !sentence.dataset.sid) ||
      (currentMode === "script" && !sentence.classList.contains("script-sentence"))) {
    hideSelectionAdd();
    return;
  }
  const raw = selection.toString();
  const text = raw.replace(/\s+/gu, " ").trim();
  if (!text) { hideSelectionAdd(); return; }
  const prefix = document.createRange();
  prefix.selectNodeContents(sentence);
  prefix.setEnd(range.startContainer, range.startOffset);
  const offset = prefix.toString().length;
  if (offset < 0 || offset + raw.length > sentence.textContent.length) {
    hideSelectionAdd();
    return;
  }
  selectedPassage = currentMode === "script"
    ? {
        mode: "script", paragraphIndex: Number(sentence.dataset.paragraphIndex),
        sentenceIndex: Number(sentence.dataset.sentenceIndex),
        text, offset, length: raw.length
      }
    : { mode: "passage", sid: sentence.dataset.sid, text, offset, length: raw.length };
  const rect = range.getBoundingClientRect();
  const button = byId("selection-add");
  button.style.left = Math.round(Math.max(8, Math.min(window.innerWidth - 38, rect.right + 8))) + "px";
  button.style.top = Math.round(Math.max(8, Math.min(window.innerHeight - 40, rect.top - 14))) + "px";
  button.hidden = false;
  button.disabled = !canWriteVocabulary || !!pendingMutation;
}

function formatSeconds(seconds) {
  const value = Math.max(0, Math.floor(Number(seconds) || 0));
  return String(Math.floor(value / 60)).padStart(2, "0") + ":" +
    String(value % 60).padStart(2, "0");
}

function renderPlayback(playback) {
  currentPlayback = playback;
  const position = Number(playback.position) || 0;
  const duration = Number(playback.duration) || 0;
  const progress = byId("progress");
  progress.disabled = playback.canSeek !== true;
  progress.max = String(Math.max(duration, 1));
  if (!seekDragging) progress.value = String(Math.max(0, Math.min(position, Math.max(duration, 1))));
  byId("playback-time").textContent =
    formatSeconds(seekDragging ? progress.value : position) + " / " + formatSeconds(duration);
  byId("article-play").disabled = playback.canPlay !== true;
  byId("article-play").textContent = playback.state === "playing" ? "暂停" : "播放";
  byId("article-stop").disabled = playback.isArticleActive !== true;
  paintScriptProgress(playback);
}

function dismissNotification(id) {
  if (sessionId) send("notificationDismiss", { payload: { id } });
}

// Shared visual lifetime for all severities. Unacknowledged error records
// remain in the C# notification service after their cards disappear.
const toastDisplayMs = 4650;
const toastFadeMs = 1000;

function noticePrefix(severity) {
  const kind = String(severity ?? "").toLowerCase();
  return kind === "error" ? "错误：" : kind === "warning" ? "警告：" : "普通：";
}

function noticeText(message, severity) {
  const body = element("div", "notice-message clamped");
  body.append(element("strong", "notice-prefix", noticePrefix(severity)));
  body.append(document.createTextNode(String(message ?? "")));
  return body;
}

function attachNoticeExpansion(body, actions) {
  const expand = element("button", "notice-expand", "...>>展开");
  expand.type = "button";
  expand.hidden = true;
  expand.setAttribute("aria-expanded", "false");
  const update = () => {
    if (body.classList.contains("expanded")) return;
    expand.hidden = body.scrollHeight <= body.clientHeight + 1;
  };
  expand.addEventListener("click", () => {
    const opened = !body.classList.contains("expanded");
    body.classList.toggle("expanded", opened);
    body.classList.toggle("clamped", !opened);
    expand.textContent = opened ? "<<收起" : "...>>展开";
    expand.setAttribute("aria-expanded", String(opened));
    expand.hidden = false;
  });
  actions.append(expand);
  return update;
}

function createToast(item) {
  const key = String(item.id);
  const card = element("div", "notice " + String(item.severity ?? "").toLowerCase());
  card.dataset.id = key;
  card.classList.add("entering");
  const message = noticeText(item.message, item.severity);
  const actions = element("div", "notice-actions");
  const checkOverflow = attachNoticeExpansion(message, actions);
  const close = element("button", "notice-close", "×");
  close.type = "button";
  close.setAttribute("aria-label", "关闭通知");
  actions.append(close);
  card.append(message, actions);
  const isError = String(item.severity ?? "").toLowerCase() === "error";
  const entry = {
    card, item, remaining: toastDisplayMs, started: null, timeout: null,
    exiting: false, hovered: false, checkOverflow
  };
  const pause = () => {
    if (entry.exiting) return;
    if (entry.timeout !== null) window.clearTimeout(entry.timeout);
    entry.timeout = null;
    if (entry.started !== null) entry.remaining = Math.max(0,
      entry.remaining - (performance.now() - entry.started));
    entry.started = null;
  };
  const fadeOut = () => {
    if (entry.exiting) return;
    pause();
    entry.exiting = true;
    card.classList.remove("entering");
    card.classList.add("exiting");
    entry.timeout = window.setTimeout(() => {
      entry.timeout = null;
      // A queue slot is freed only after the fade completes and the C#
      // NotificationService has acknowledged the dismissal.
      dismissNotification(item.id);
    }, toastFadeMs);
  };
  const resume = () => {
    // A stable DOM card keeps its own remaining time when hover ends.
    // If the pointer is still physically over the card, even after a
    // layout shift or mouse event ordering race, do not restart its timer.
    if (entry.exiting || isError || entry.hovered || card.matches(":hover")) return;
    if (entry.timeout !== null) window.clearTimeout(entry.timeout);
    entry.started = performance.now();
    entry.timeout = window.setTimeout(() => {
      entry.timeout = null;
      // Check the actual pointer position again at the expiry boundary.
      // This prevents an already-queued timeout from hiding hovered text.
      if (entry.hovered || card.matches(":hover")) {
        pause();
        return;
      }
      fadeOut();
    }, Math.max(0, entry.remaining));
  };
  entry.dispose = () => {
    if (entry.timeout !== null) window.clearTimeout(entry.timeout);
    entry.timeout = null;
    entry.started = null;
    card.remove();
  };
  const hoverEnter = () => {
    entry.hovered = true;
    pause();
  };
  const hoverLeave = () => {
    // A card can move under the pointer when a different message leaves.
    // Treat the card as hovered while the browser still reports :hover.
    if (card.matches(":hover")) return;
    entry.hovered = false;
    resume();
  };
  card.addEventListener("pointerenter", hoverEnter);
  card.addEventListener("pointerleave", hoverLeave);
  card.addEventListener("mouseenter", hoverEnter);
  card.addEventListener("mouseleave", hoverLeave);
  close.addEventListener("click", fadeOut);
  resume();
  return entry;
}

function renderNotifications(notifications) {
  const host = byId("notification-cards");
  const visible = (notifications.visible ?? []).slice(0, 3);
  const wanted = new Set(visible.map(item => String(item.id)));
  for (const [key, entry] of toastEntries) {
    if (!wanted.has(key)) {
      entry.dispose();
      toastEntries.delete(key);
    }
  }
  // C# owns the FIFO. Only three admitted cards have active DOM/timers.
  // Create oldest-to-newest, then place newest above the older messages.
  for (const item of [...visible].reverse()) {
    const key = String(item.id);
    if (!toastEntries.has(key)) toastEntries.set(key, createToast(item));
  }
  for (let index = 0; index < visible.length; index++) {
    const entry = toastEntries.get(String(visible[index].id));
    if (!entry) continue;
    // append() unconditionally moves an existing DOM node. During ordinary
    // C# state refreshes that generates artificial mouseleave events, which
    // restart an otherwise hovered toast's expiry timer.
    // Only insert when the order truly changes; leave stable cards untouched.
    if (host.children[index] !== entry.card)
      host.insertBefore(entry.card, host.children[index] ?? null);
    window.requestAnimationFrame(entry.checkOverflow);
  }
}
function renderSettings(settings) {
  const fontSize = Number(settings.fontSize);
  document.documentElement.style.setProperty("--reader-font",
    (Number.isFinite(fontSize) && fontSize >= 10 && fontSize <= 30 ? fontSize : 15) + "px");
  currentSettings = { ...settings };
  applyPaneWidth(settings.rightWidth);
  for (const [id, value] of [
    ["reader-font-size", settings.fontSize],
    ["reader-accent", settings.accent],
    ["reader-forward-step", settings.forwardStep],
    ["reader-backward-step", settings.backwardStep]
  ]) {
    if (value !== undefined) byId(id).value = String(value);
  }
  highlightsVisible = settings.highlightsVisible === true;
  byId("highlight-toggle").setAttribute("aria-pressed", String(highlightsVisible));
  refreshHighlights(false);
  if (currentMode === "script") scheduleScriptGeometry();
}

function renderInitialize(payload) {
  pendingModeRequest = null;
  readingScroll.passage = 0;
  readingScroll.script = 0;
  renderArticle(payload.article, payload.mode === "script" ? "script" : "passage");
  selectedWord = null;
  selectedIndex = -1;
  canWriteVocabulary = payload.canWriteVocabulary === true;
  vocabulary = payload.vocabulary ?? [];
  byId("script-toggle").disabled = payload.article == null;
  updateReadingModeButton();
  renderVocabulary(vocabulary);
  byId("vocabulary-scroll").scrollTop = 0;
  refreshHighlights(true);
  renderSettings(payload.settings ?? {});
  renderPlayback(payload.playback ?? {});
  renderNotifications(payload.notifications ?? {});
}

function send(type, additional = {}) {
  if (!window.chrome?.webview) return null;
  const requestId = additional.requestId ?? crypto.randomUUID();
  window.chrome.webview.postMessage({
    version: 1, type, pageId, sessionId, vocabularyRevision,
    ...additional, requestId
  });
  return requestId;
}

function updateReadingModeButton() {
  const button = byId("script-toggle");
  button.setAttribute("aria-pressed", String(currentMode === "script"));
  button.disabled = !sessionId || !byId("article-body").querySelector(".sentence, .script-sentence") ||
    !!pendingModeRequest || !!pendingMutation;
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

function updateEditControls() {
  const canEdit = !!sessionId && canWriteVocabulary && !pendingMutation;
  const valid = selectedIndex >= 0 && selectedIndex < vocabulary.length;
  byId("word-up").disabled = !canEdit || !valid || selectedIndex === 0;
  byId("word-down").disabled = !canEdit || !valid || selectedIndex === vocabulary.length - 1;
  byId("word-delete").disabled = !canEdit || !valid;
  byId("word-import").disabled = !canEdit;
  byId("selection-add").disabled = !canEdit;
  updateReadingModeButton();
}

function requestVocabularyEdit(type, payload) {
  if (!sessionId || !canWriteVocabulary || pendingMutation) return;
  const requestId = send(type, { payload });
  if (!requestId) return;
  pendingMutation = { type, requestId };
  updateEditControls();
}

function selectedWordPayload() {
  if (selectedIndex < 0 || selectedIndex >= vocabulary.length) return null;
  return { index: selectedIndex, word: vocabulary[selectedIndex].word };
}

function setSettingsOpen(open) {
  byId("reader-settings-panel").hidden = !open;
  byId("reader-settings-toggle").setAttribute("aria-expanded", String(open));
}
byId("reader-settings-toggle").addEventListener("click", () =>
  setSettingsOpen(byId("reader-settings-panel").hidden));
document.addEventListener("pointerdown", event => {
  if (!byId("reader-settings-panel").hidden &&
      !byId("reader-settings-panel").contains(event.target) &&
      !byId("reader-settings-toggle").contains(event.target))
    setSettingsOpen(false);
});

for (const [id, type, field, convert] of [
  ["reader-font-size", "setFontSize", "size", Number],
  ["reader-accent", "setAccent", "accent", String],
  ["reader-forward-step", "setForwardStep", "seconds", Number],
  ["reader-backward-step", "setBackwardStep", "seconds", Number]
]) {
  byId(id).addEventListener("change", () => {
    if (!sessionId) return;
    send(type, { payload: { [field]: convert(byId(id).value) } });
  });
}

const splitter = byId("reader-splitter");
function applyPaneWidth(width) {
  // Called only for C# snapshots/acknowledgments, never from pointer input.
  const applied = Number(width);
  if (!Number.isFinite(applied) || applied < 200 || applied > 400) return;
  currentSettings.rightWidth = applied;
  document.documentElement.style.setProperty("--word-width", applied + "px");
  scheduleScriptGeometry();
}
function queueRightDrag() {
  if (splitterDragFrame) return;
  splitterDragFrame = requestAnimationFrame(() => {
    splitterDragFrame = 0;
    if (splitting?.targetWidth != null)
      send("dragPanelWidth", { payload: { width: splitting.targetWidth } });
  });
}
splitter.addEventListener("pointerdown", event => {
  if (event.button !== 0 || !sessionId) return;
  event.preventDefault();
  splitting = { pointerId: event.pointerId,
    width: Number(currentSettings.rightWidth) || 300,
    // The WebView2 host can move when its native sibling shrinks.
    screenX: event.screenX, targetWidth: null };
  splitter.setPointerCapture(event.pointerId);
  splitter.classList.add("dragging");
  document.body.classList.add("splitter-dragging");
});
splitter.addEventListener("pointermove", event => {
  if (!splitting || event.pointerId !== splitting.pointerId) return;
  // Only bound the input request; C# owns all three rendered pane widths.
  splitting.targetWidth = Math.round(Math.max(200, Math.min(400,
    splitting.width + splitting.screenX - event.screenX)));
  queueRightDrag();
});
function finishDivider(event, save) {
  if (!splitting || event.pointerId !== splitting.pointerId) return;
  const targetWidth = splitting.targetWidth;
  const splittingStartWidth = splitting.width;
  if (splitterDragFrame) cancelAnimationFrame(splitterDragFrame);
  splitterDragFrame = 0;
  splitting = null;
  splitter.classList.remove("dragging");
  document.body.classList.remove("splitter-dragging");
  if (splitter.hasPointerCapture(event.pointerId)) splitter.releasePointerCapture(event.pointerId);
  if (save) {
    send("setPanelWidth", { payload: { width: targetWidth ?? splittingStartWidth } });
  } else {
    send("cancelPanelDrag");
  }
}
splitter.addEventListener("pointerup", event => finishDivider(event, true));
splitter.addEventListener("pointercancel", event => finishDivider(event, false));
splitter.addEventListener("keydown", event => {
  if (event.key !== "ArrowLeft" && event.key !== "ArrowRight") return;
  event.preventDefault();
  const current = Number(currentSettings.rightWidth) || 300;
  const next = Math.round(Math.max(200, Math.min(400,
    current + (event.key === "ArrowLeft" ? 16 : -16))));
  send("setPanelWidth", { payload: { width: next } });
});

document.addEventListener("keydown", event => {
  if (!sessionId || event.ctrlKey || event.metaKey || event.altKey ||
      event.target?.closest("input, select, textarea, button, [role='button'], [contenteditable='true'], [role='separator']"))
    return;
  if (event.key === " " || event.key === "Spacebar") {
    if (currentPlayback.canPlay !== true || event.repeat) return;
    event.preventDefault();
    send("playArticle");
  } else if (event.key === "ArrowLeft" || event.key === "ArrowRight") {
    if (currentPlayback.canSeek !== true) return;
    event.preventDefault();
    const step = event.key === "ArrowLeft"
      ? -(Number(currentSettings.backwardStep) || 5)
      : Number(currentSettings.forwardStep) || 4;
    const target = Math.max(0, Math.min(Number(currentPlayback.duration) || 0,
      (Number(currentPlayback.position) || 0) + step));
    send("seekArticle", { payload: { position: target } });
  } else if (event.key === "Escape") {
    setSettingsOpen(false);
  }
});

byId("article-play").addEventListener("click", () => {
  if (!byId("article-play").disabled) send("playArticle");
});
byId("article-stop").addEventListener("click", () => {
  if (!byId("article-stop").disabled) send("stopArticle");
});
byId("progress").addEventListener("pointerdown", () => { seekDragging = true; });
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
byId("script-toggle").addEventListener("click", () => {
  if (byId("script-toggle").disabled || !sessionId) return;
  pendingModeRequest = send("switchReadingMode");
  updateReadingModeButton();
});
byId("article-scroll").addEventListener("scroll", () => {
  readingScroll[currentMode] = byId("article-scroll").scrollTop;
});
window.addEventListener("resize", scheduleScriptGeometry);
const scriptSizeObserver = new ResizeObserver(scheduleScriptGeometry);
scriptSizeObserver.observe(byId("article-scroll"));

byId("word-up").addEventListener("click", () => {
  const payload = selectedWordPayload();
  if (payload) requestVocabularyEdit("moveWord", { ...payload, direction: -1 });
});
byId("word-down").addEventListener("click", () => {
  const payload = selectedWordPayload();
  if (payload) requestVocabularyEdit("moveWord", { ...payload, direction: 1 });
});
byId("word-delete").addEventListener("click", () => {
  const payload = selectedWordPayload();
  if (payload) requestVocabularyEdit("deleteWord", payload);
});
byId("word-import").addEventListener("click", () => {
  requestVocabularyEdit("importVocabulary", {});
});

byId("selection-add").addEventListener("pointerdown", event => event.preventDefault());
byId("selection-add").addEventListener("click", () => {
  if (!selectedPassage || !sessionId) return;
  requestVocabularyEdit("addWord", { ...selectedPassage });
});
byId("highlight-toggle").addEventListener("click", () => {
  if (!sessionId) return;
  send("setHighlight", { payload: { enabled: !highlightsVisible } });
});
document.addEventListener("selectionchange", updateSelectionAdd);

if (window.chrome?.webview) {
  window.chrome.webview.addEventListener("message", event => {
    const message = event.data;
    if (message?.version !== 1 || message.pageId !== pageId) return;
    if (message.type === "initialize") {
      if (typeof message.sessionId !== "string" || typeof message.requestId !== "string") return;
      sessionId = message.sessionId;
      vocabularyRevision = Number(message.vocabularyRevision) || 0;
      try {
        renderInitialize(message.payload ?? {});
        send("rendered", { sessionId, requestId: message.requestId });
      } catch {
        send("renderFailed", { sessionId, requestId: message.requestId });
      }
      return;
    }
    if (message.sessionId !== sessionId) return;
    if (message.type === "setReadingMode") {
      if (!pendingModeRequest || message.requestId !== pendingModeRequest) return;
      try {
        updateReadingMode(message.payload?.mode, message.payload?.article);
        send("modeRendered", { requestId: message.requestId });
      } catch {
        send("modeRenderFailed", { requestId: message.requestId });
      }
      return;
    }
    if (message.type === "setVocabulary") {
      const nextRevision = Number(message.vocabularyRevision);
      if (!Number.isInteger(nextRevision) || nextRevision < vocabularyRevision) return;
      vocabularyRevision = nextRevision;
      vocabulary = message.payload?.vocabulary ?? [];
      canWriteVocabulary = message.payload?.canWriteVocabulary === true;
      if (selectedWord !== null) {
        selectedIndex = vocabulary.findIndex(word =>
          ordinalFold(word.word) === ordinalFold(selectedWord));
        if (selectedIndex < 0) selectedWord = null;
      } else selectedIndex = -1;
      renderVocabulary(vocabulary);
      refreshHighlights(true);
    } else if (message.type === "setSettings") {
      renderSettings(message.payload ?? {});
    } else if (message.type === "setPaneWidth") {
      const width = Number(message.payload?.rightWidth);
      applyPaneWidth(width);
    } else if (message.type === "setPlayback") {
      renderPlayback(message.payload ?? {});
    } else if (message.type === "setNotifications") {
      renderNotifications(message.payload ?? {});
    } else if (message.type === "actionResult") {
      if (pendingModeRequest && message.requestId === pendingModeRequest) {
        pendingModeRequest = null;
        updateReadingModeButton();
        // The host publishes Script failures to NotificationService.
        // Never create a second independent message outside the Toast stack.
        return;
      }
      if (!pendingMutation || message.requestId !== pendingMutation.requestId) return;
      const operation = pendingMutation.type;
      const result = message.payload?.status;
      pendingMutation = null;
      if (result === "success") {
        if (operation === "deleteWord" || operation === "importVocabulary") {
          selectedWord = null;
          selectedIndex = -1;
        } else if (operation === "addWord" || operation === "moveWord") {
          const index = Number(message.payload?.index);
          if (Number.isInteger(index) && index >= 0 && index < vocabulary.length) {
            selectedIndex = index;
            selectedWord = vocabulary[index].word;
          }
        }
        renderVocabulary(vocabulary);
        refreshHighlights(false);
      }
      // Operation results are published by C# to its single FIFO notification
      // service; the browser never starts a competing message queue.
      updateEditControls();
    }
  });
  send("ready");
} else {
  byId("article-body").replaceChildren(
    element("p", "empty", "此页面需由 ReadArticles 桌面程序加载。"));
}
