"use strict";

// Initial WebView2 pilot: authoritative C# snapshots only, no user-data mutations.
const pageId = crypto.randomUUID();
let sessionId = null;
let vocabularyRevision = 0;

const byId = id => document.getElementById(id);
function element(tag, className, value) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (value !== undefined && value !== null) node.textContent = String(value);
  return node;
}

function renderArticle(article) {
  const body = byId("article-body");
  const title = byId("article-title");
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
      const span = element("span", "sentence", sentence.text);
      if (sentence.sid) span.dataset.sid = sentence.sid;
      row.append(span);
    });
    body.append(row);
  }
}

function renderVocabulary(words) {
  const scroll = byId("vocabulary-scroll");
  const previousOffset = scroll.scrollTop;
  const list = byId("vocabulary-list");
  list.replaceChildren();
  byId("vocabulary-count").textContent = words.length ? `(${words.length})` : "";
  for (const word of words) {
    const row = element("article", "word-row");
    row.append(element("div", "word-head", word.word));
    row.append(element("div", "phonetics", `UK ${word.phoneticUk ?? "—"}  |  US ${word.phoneticUs ?? "—"}`));
    for (const meaning of word.meanings ?? []) {
      const line = element("p", "meaning");
      line.append(element("span", "part-of-speech", meaning.pos));
      line.append(document.createTextNode(meaning.text ?? ""));
      row.append(line);
    }
    list.append(row);
  }
  scroll.scrollTop = previousOffset;
}

function formatSeconds(seconds) {
  const value = Math.max(0, Math.floor(Number(seconds) || 0));
  return `${String(Math.floor(value / 60)).padStart(2, "0")}:${String(value % 60).padStart(2, "0")}`;
}

function renderPlayback(playback) {
  const position = Number(playback.position) || 0;
  const duration = Number(playback.duration) || 0;
  const progress = byId("progress");
  progress.max = String(Math.max(duration, 1));
  progress.value = String(Math.max(0, Math.min(position, Math.max(duration, 1))));
  byId("playback-time").textContent = `${formatSeconds(position)} / ${formatSeconds(duration)}`;
}

function renderNotifications(notifications) {
  const host = byId("notifications");
  host.replaceChildren();
  for (const item of notifications.visible ?? []) {
    host.append(element("div", "notice " + (item.severity ?? "").toLowerCase(), item.message));
  }
}

function renderSettings(settings) {
  const fontSize = Number(settings.fontSize);
  const rightWidth = Number(settings.rightWidth);
  document.documentElement.style.setProperty("--reader-font",
    (Number.isFinite(fontSize) && fontSize >= 10 && fontSize <= 30 ? fontSize : 15) + "px");
  document.documentElement.style.setProperty("--word-width",
    (Number.isFinite(rightWidth) && rightWidth >= 200 && rightWidth <= 1000 ? rightWidth : 390) + "px");
}

function renderInitialize(payload) {
  renderSettings(payload.settings ?? {});
  renderArticle(payload.article);
  byId("vocabulary-scroll").scrollTop = 0;
  renderVocabulary(payload.vocabulary ?? []);
  renderPlayback(payload.playback ?? {});
  renderNotifications(payload.notifications ?? {});
}

function send(type, additional = {}) {
  if (window.chrome?.webview) {
    window.chrome.webview.postMessage({ version: 1, type, pageId, ...additional });
  }
}

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
      renderVocabulary(message.payload?.vocabulary ?? []);
    } else if (message.type === "setSettings") {
      renderSettings(message.payload ?? {});
    } else if (message.type === "setPlayback") {
      renderPlayback(message.payload ?? {});
    } else if (message.type === "setNotifications") {
      renderNotifications(message.payload ?? {});
    }
    // All user-data action controls remain disabled during the pilot.
  });
  send("ready");
} else {
  byId("article-body").replaceChildren(element("p", "empty", "此页面需由 ReadArticles 桌面程序加载。"));
}
