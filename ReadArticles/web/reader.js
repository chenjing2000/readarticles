"use strict";

// WebView2 DOM pilot. C# owns vocabulary files and audio; UI matching stays local.
const pageId = crypto.randomUUID();
let sessionId = null;
let vocabularyRevision = 0;
let vocabulary = [];
let highlightsVisible = false;
let selectedWord = null;
let matches = [];
let selectedPassage = null;
let pendingAdd = null;

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

function renderArticle(article) {
  const body = byId("article-body");
  const title = byId("article-title");
  hideSelectionAdd();
  pendingAdd = null;
  body.replaceChildren();
  byId("article-scroll").scrollTop = 0;
  title.textContent = article?.title ?? "";
  if (!article) {
    body.append(element("p", "empty", "请在左侧图书馆中选择文章。"));
    return;
  }
  for (const paragraph of article.paragraphs ?? []) {
    const row = element("p", "article-paragraph");
    paragraph.forEach((sentence, index) => {
      if (index > 0) row.append(document.createTextNode(" "));
      const span = element("span", "sentence");
      if (sentence.sid) span.dataset.sid = sentence.sid;

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
}

function renderVocabulary(words) {
  const scroller = byId("vocabulary-scroll");
  const previousOffset = scroller.scrollTop;
  const list = byId("vocabulary-list");
  list.replaceChildren();
  byId("vocabulary-count").textContent = words.length ? "(" + words.length + ")" : "";
  for (const word of words) {
    const row = element("article", "word-row");
    row.dataset.word = word.word;
    if (selectedWord !== null && ordinalFold(selectedWord) === ordinalFold(word.word))
      row.classList.add("selected");
    row.tabIndex = 0;
    row.setAttribute("role", "button");
    row.setAttribute("aria-label", "定位词汇 " + word.word);
    row.append(element("div", "word-head", word.word));
    row.append(element("div", "phonetics",
      "UK " + (word.phoneticUk ?? "—") + "  |  US " + (word.phoneticUs ?? "—")));
    for (const meaning of word.meanings ?? []) {
      const line = element("p", "meaning");
      line.append(element("span", "part-of-speech", meaning.pos));
      line.append(document.createTextNode(meaning.text ?? ""));
      row.append(line);
    }
    row.addEventListener("click", () => focusVocabulary(word.word));
    row.addEventListener("keydown", event => {
      if (event.key === "Enter") {
        event.preventDefault();
        focusVocabulary(word.word);
      }
    });
    list.append(row);
  }
  scroller.scrollTop = previousOffset;
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
    // Match each text fragment separately, preserving legacy blank boundaries.
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

function focusVocabulary(word) {
  selectedWord = word;
  for (const row of byId("vocabulary-list").querySelectorAll(".word-row")) {
    const selected = ordinalFold(row.dataset.word ?? "") === ordinalFold(word);
    row.classList.toggle("selected", selected);
    row.setAttribute("aria-pressed", String(selected));
  }
  refreshHighlights(false);
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
  return parent?.closest(".sentence") ?? null;
}

function hideSelectionAdd() {
  selectedPassage = null;
  byId("selection-add").hidden = true;
  byId("selection-status").hidden = true;
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
      !byId("article-body").contains(sentence) || !sentence.dataset.sid) {
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
  selectedPassage = { sid: sentence.dataset.sid, text, offset, length: raw.length };
  const rect = range.getBoundingClientRect();
  const button = byId("selection-add");
  button.style.left = Math.round(Math.max(8, Math.min(window.innerWidth - 38, rect.right + 8))) + "px";
  button.style.top = Math.round(Math.max(8, Math.min(window.innerHeight - 40, rect.top - 14))) + "px";
  button.hidden = false;
  byId("selection-status").hidden = true;
}

function formatSeconds(seconds) {
  const value = Math.max(0, Math.floor(Number(seconds) || 0));
  return String(Math.floor(value / 60)).padStart(2, "0") + ":" +
    String(value % 60).padStart(2, "0");
}

function renderPlayback(playback) {
  const position = Number(playback.position) || 0;
  const duration = Number(playback.duration) || 0;
  const progress = byId("progress");
  progress.max = String(Math.max(duration, 1));
  progress.value = String(Math.max(0, Math.min(position, Math.max(duration, 1))));
  byId("playback-time").textContent =
    formatSeconds(position) + " / " + formatSeconds(duration);
}

function renderNotifications(notifications) {
  const host = byId("notifications");
  host.replaceChildren();
  for (const item of notifications.visible ?? [])
    host.append(element("div", "notice " + (item.severity ?? "").toLowerCase(), item.message));
}

function renderSettings(settings) {
  const fontSize = Number(settings.fontSize);
  const rightWidth = Number(settings.rightWidth);
  document.documentElement.style.setProperty("--reader-font",
    (Number.isFinite(fontSize) && fontSize >= 10 && fontSize <= 30 ? fontSize : 15) + "px");
  document.documentElement.style.setProperty("--word-width",
    (Number.isFinite(rightWidth) && rightWidth >= 200 && rightWidth <= 1000 ? rightWidth : 390) + "px");
  highlightsVisible = settings.highlightsVisible === true;
  byId("highlight-toggle").setAttribute("aria-pressed", String(highlightsVisible));
  refreshHighlights(false);
}

function renderInitialize(payload) {
  renderArticle(payload.article);
  selectedWord = null;
  vocabulary = payload.vocabulary ?? [];
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

byId("selection-add").addEventListener("pointerdown", event => event.preventDefault());
byId("selection-add").addEventListener("click", () => {
  if (!selectedPassage || !sessionId) return;
  pendingAdd = send("addWord", { payload: { ...selectedPassage } });
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
    if (message.type === "setVocabulary") {
      const nextRevision = Number(message.vocabularyRevision);
      if (!Number.isInteger(nextRevision) || nextRevision < vocabularyRevision) return;
      vocabularyRevision = nextRevision;
      vocabulary = message.payload?.vocabulary ?? [];
      if (selectedWord && !vocabulary.some(word => ordinalFold(word.word) === ordinalFold(selectedWord)))
        selectedWord = null;
      renderVocabulary(vocabulary);
      refreshHighlights(true);
    } else if (message.type === "setSettings") {
      renderSettings(message.payload ?? {});
    } else if (message.type === "setPlayback") {
      renderPlayback(message.payload ?? {});
    } else if (message.type === "setNotifications") {
      renderNotifications(message.payload ?? {});
    } else if (message.type === "actionResult") {
      // The current pilot never writes a vocabulary file.
      if (pendingAdd && message.requestId === pendingAdd) {
        const status = byId("selection-status");
        status.textContent = message.payload?.status === "stale"
          ? "选区已过期，请重新选择" : "只读试验版：尚未保存";
        status.hidden = false;
        pendingAdd = null;
      }
    }
  });
  send("ready");
} else {
  byId("article-body").replaceChildren(
    element("p", "empty", "此页面需由 ReadArticles 桌面程序加载。"));
}
