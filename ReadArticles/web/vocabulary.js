import { byId, element } from "./dom.js";
import { hasSession, send } from "./bridge.js";
import { getMode, updateReadingModeButton } from "./reading.js";

let vocabulary = [];
let highlightsVisible = false;
let selectedWord = null;
let selectedIndex = -1;
let canWriteVocabulary = false;
let matches = [];
let selectedPassage = null;
const selectionAddRefreshHz = 60;
let selectionAddPositionFrame = 0;
let selectionAddLastFrameAt = -Infinity;
let pendingMutation = null;

export const isMutationPending = () => !!pendingMutation;
export const matchedTexts = word => matches.filter(m => m.word === word).map(m => m.range.toString());
export const selectionTracking = () => ({
  tracking: selectionAddPositionFrame !== 0, refreshHz: selectionAddRefreshHz,
  hasSelection: selectedPassage !== null
});

export function beforeArticleRender() {
  hideSelectionAdd();
  pendingMutation = null;
}

export function initializeVocabulary(payload) {
  selectedWord = null;
  selectedIndex = -1;
  canWriteVocabulary = payload.canWriteVocabulary === true;
  vocabulary = payload.vocabulary ?? [];
  renderVocabulary(vocabulary);
  byId("vocabulary-scroll").scrollTop = 0;
  refreshHighlights(true);
}

export function updateVocabulary(payload) {
  vocabulary = payload?.vocabulary ?? [];
  canWriteVocabulary = payload?.canWriteVocabulary === true;
  if (selectedWord !== null) {
    selectedIndex = vocabulary.findIndex(word => ordinalFold(word.word) === ordinalFold(selectedWord));
    if (selectedIndex < 0) selectedWord = null;
  } else selectedIndex = -1;
  renderVocabulary(vocabulary);
  refreshHighlights(true);
}

export function completeVocabularyRequest(message) {
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
  // C# owns all user-visible operation notifications.
  updateEditControls();
}

export function applyHighlightSetting(enabled) {
  highlightsVisible = enabled === true;
  byId("highlight-toggle").setAttribute("aria-pressed", String(highlightsVisible));
  byId("highlight-toggle").querySelector("img").src = highlightsVisible
    ? "icons/show-vocabulary.svg" : "icons/not-show-vocabulary.svg";
  refreshHighlights(false);
  scheduleHighlightGeometry();
  if (selectedPassage) refreshSelectionAddPosition();
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

function findWholeWord(text, source, target, from) {
  let start = source.indexOf(target, from);
  while (start >= 0) {
    const end = start + target.length;
    if ((start === 0 || !englishBoundary(text[start - 1])) &&
        (end === text.length || !englishBoundary(text[end]))) return start;
    start = source.indexOf(target, start + 1);
  }
  return -1;
}

export function matchVocabulary(text, entries) {
  const seen = new Set();
  const words = [];
  const add = (surface, owner) => {
    const word = String(surface ?? "").trim();
    const key = ordinalFold(word);
    if (!word || seen.has(key)) return;
    seen.add(key);
    words.push({ surface: word, folded: key, owner });
  };
  // Headwords always own exact matches, even when an earlier entry lists
  // the same surface form as a family member.
  for (const entry of entries) {
    const word = typeof entry === "string" ? entry : entry?.word;
    add(word, String(word ?? "").trim());
  }
  for (const entry of entries) {
    if (!entry || typeof entry === "string" || !Array.isArray(entry.family)) continue;
    for (const form of entry.family) add(form, entry.word);
  }
  const result = [];
  const source = ordinalFold(text);
  let cursor = 0;
  while (cursor < text.length) {
    let bestStart = -1;
    let bestWord = null;
    for (const word of words) {
      const start = findWholeWord(text, source, word.folded, cursor);
      if (start >= 0 && (bestStart < 0 || start < bestStart ||
          (start === bestStart && word.surface.length > bestWord.surface.length))) {
        bestStart = start;
        bestWord = word;
      }
    }
    if (bestWord === null) break;
    result.push({ start: bestStart, length: bestWord.surface.length, key: bestWord.owner });
    cursor = bestStart + bestWord.surface.length;
  }
  return result;
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
    const phonetics = element("div", "phonetic-line");
    for (const accent of ["uk", "us"]) {
      // The break opportunity belongs between the two pronunciation groups.
      const unit = element("span", "phonetic-unit");
      unit.append(element("span", "phonetic-text",
        (accent === "uk" ? "UK " : "US ") +
        (accent === "uk" ? word.phoneticUk ?? "—" : word.phoneticUs ?? "—")));
      const button = element("button", "phonetic-audio");
      button.type = "button";
      button.disabled = accent === "uk" ? !word.canPlayUk : !word.canPlayUs;
      button.title = accent === "uk" ? "播放英音" : "播放美音";
      button.setAttribute("aria-label", button.title);
      const speaker = element("img");
      speaker.src = "icons/sound.svg";
      speaker.alt = "";
      button.append(speaker);
      button.addEventListener("click", event => {
        event.stopPropagation();
        if (!button.disabled && hasSession())
          send("playWord", { payload: { index, word: word.word, accent } });
      });
      unit.append(button);
      phonetics.append(unit);
    }
    row.append(phonetics);
    for (const meaning of word.meanings ?? []) {
      const line = element("p", "meaning");
      line.append(element("span", "part-of-speech", meaning.pos));
      line.append(document.createTextNode(meaning.text ?? ""));
      row.append(line);
    }
    row.addEventListener("click", () => focusVocabulary(word.word, index));
    row.addEventListener("keydown", event => {
      if (event.key === "Enter") {
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

// Range geometry, not nested text spans, owns the rounded background.
let highlightFrame = 0;
function clearHighlights() {
  if (highlightFrame) cancelAnimationFrame(highlightFrame);
  highlightFrame = 0;
  byId("word-highlight-layer")?.remove();
}

function scheduleHighlightGeometry() {
  if (!highlightsVisible || highlightFrame) return;
  highlightFrame = requestAnimationFrame(() => {
    highlightFrame = 0;
    paintRoundedHighlights();
  });
}

function paintRoundedHighlights() {
  const body = byId("article-body");
  let layer = byId("word-highlight-layer");
  if (!highlightsVisible || !matches.length) {
    layer?.remove();
    return;
  }
  if (!layer) {
    layer = element("div", "word-highlight-layer");
    layer.id = "word-highlight-layer";
    layer.setAttribute("aria-hidden", "true");
    body.append(layer);
  }
  const base = body.getBoundingClientRect();
  const fragments = document.createDocumentFragment();
  const focused = selectedWord === null ? null : ordinalFold(selectedWord);

  for (const match of matches) {
    const selected = focused !== null && ordinalFold(match.word) === focused;
    const lineRects = [...match.range.getClientRects()]
      .filter(rect => rect.width > 0.25 && rect.height > 0.25)
      .sort((a, b) => a.top - b.top || a.left - b.left);

    // Adjacent fragments of a multi-unit word share one rounded capsule.
    const rows = [];
    for (const rect of lineRects) {
      const last = rows[rows.length - 1];
      if (last && Math.abs(rect.top - last.top) < 2 &&
          rect.left <= last.right + 2) {
        last.right = Math.max(last.right, rect.right);
        last.bottom = Math.max(last.bottom, rect.bottom);
      } else {
        rows.push({ left: rect.left, right: rect.right, top: rect.top, bottom: rect.bottom });
      }
    }
    for (const rect of rows) {
      const capsule = element("div", "word-highlight-rect");
      capsule.style.left = (rect.left - base.left - 1) + "px";
      capsule.style.top = (rect.top - base.top - 1) + "px";
      capsule.style.width = (rect.right - rect.left + 2) + "px";
      capsule.style.height = (rect.bottom - rect.top + 2) + "px";
      capsule.style.backgroundColor = selected ? "#f6bec8" : "#f9d3e3";
      fragments.append(capsule);
    }
  }
  layer.replaceChildren(fragments);
}

export function refreshHighlights(recalculate) {
  if (recalculate) {
    matches = [];
    const words = vocabulary;
    if (getMode() === "script") {
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
  if (!highlightsVisible) {
    clearHighlights();
    return;
  }
  scheduleHighlightGeometry();
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
  return parent?.closest(".sentence, .script-sentence") ?? null;
}

function stopSelectionAddPositionTimer() {
  if (selectionAddPositionFrame) cancelAnimationFrame(selectionAddPositionFrame);
  selectionAddPositionFrame = 0;
  selectionAddLastFrameAt = -Infinity;
}

function scheduleSelectionAddFrame() {
  if (selectedPassage && !document.hidden && !selectionAddPositionFrame)
    selectionAddPositionFrame = requestAnimationFrame(selectionAddFrame);
}

// RequestAnimationFrame tracks the browser repaint; at faster refresh rates
// the geometry check is capped at approximately 60 Hz. There is no timer.
function selectionAddFrame(timestamp) {
  selectionAddPositionFrame = 0;
  if (!selectedPassage || document.hidden) {
    stopSelectionAddPositionTimer();
    return;
  }
  if (timestamp - selectionAddLastFrameAt >= 1000 / selectionAddRefreshHz - 1) {
    selectionAddLastFrameAt = timestamp;
    refreshSelectionAddPosition();
  } else {
    scheduleSelectionAddFrame();
  }
}

function hideSelectionAdd() {
  stopSelectionAddPositionTimer();
  selectedPassage = null;
  byId("selection-add").hidden = true;
}

// A fixed-position browser button uses CSS px, matching Range client rects.
// Anchor after the final selected text fragment, 1px right and 0px below.
// Flip left/above near viewport edges; the visible em-sized glyph occupies
// the click target's upper-left corner.
export function positionSelectionAddButton(range, button) {
  const size = 24;
  const horizontalGap = 1;
  const verticalGap = 0;
  const fragments = [...range.getClientRects()].filter(rect => rect.width > 0 && rect.height > 0);
  const anchor = fragments.at(-1) ?? range.getBoundingClientRect();
  const toRight = anchor.right + horizontalGap;
  const toLeft = anchor.left - horizontalGap - size;
  const below = anchor.bottom + verticalGap;
  const above = anchor.top - verticalGap - size;
  const left = toRight + size <= window.innerWidth - horizontalGap ? toRight : toLeft;
  const top = below + size <= window.innerHeight - verticalGap ? below : above;
  const x = Math.round(Math.max(horizontalGap,
    Math.min(window.innerWidth - horizontalGap - size, left))) + "px";
  const y = Math.round(Math.max(verticalGap,
    Math.min(window.innerHeight - verticalGap - size, top))) + "px";
  if (button.style.left !== x) button.style.left = x;
  if (button.style.top !== y) button.style.top = y;
}

// The repaint loop does geometry only, never word extraction or saving.
// Keep it only while the selected text is visible; scrolling can reactivate it.
function refreshSelectionAddPosition() {
  if (!selectedPassage || document.hidden) {
    stopSelectionAddPositionTimer();
    byId("selection-add").hidden = true;
    return;
  }
  const selection = window.getSelection();
  if (!selection || selection.isCollapsed || selection.rangeCount !== 1) {
    hideSelectionAdd();
    return;
  }
  const range = selection.getRangeAt(0);
  const body = byId("article-body");
  if (!body.contains(range.startContainer) || !body.contains(range.endContainer)) {
    hideSelectionAdd();
    return;
  }
  const fragments = [...range.getClientRects()].filter(rect => rect.width > 0 && rect.height > 0);
  const anchor = fragments.at(-1);
  const viewport = byId("article-scroll").getBoundingClientRect();
  const button = byId("selection-add");
  if (!anchor || anchor.right <= viewport.left || anchor.left >= viewport.right ||
      anchor.bottom <= viewport.top || anchor.top >= viewport.bottom) {
    button.hidden = true;
    stopSelectionAddPositionTimer();
    return;
  }
  positionSelectionAddButton(range, button);
  button.hidden = false;
  scheduleSelectionAddFrame();
}

export function updateSelectionAdd() {
  const selection = window.getSelection();
  if (!selection || selection.isCollapsed || selection.rangeCount !== 1) {
    hideSelectionAdd();
    return;
  }
  const range = selection.getRangeAt(0);
  const sentence = sentenceFor(range.startContainer);
  if (!sentence || sentence !== sentenceFor(range.endContainer) ||
      !byId("article-body").contains(sentence) ||
      (getMode() === "passage" && sentence.dataset.paragraphIndex === undefined) ||
      (getMode() === "script" && !sentence.classList.contains("script-sentence"))) {
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
  selectedPassage = {
    mode: getMode(),
    paragraphIndex: Number(sentence.dataset.paragraphIndex),
    sentenceIndex: Number(sentence.dataset.sentenceIndex),
    text, offset, length: raw.length
  };
  const button = byId("selection-add");
  button.disabled = !canWriteVocabulary || !!pendingMutation;
  refreshSelectionAddPosition();
}

function updateEditControls() {
  const canEdit = !!hasSession() && canWriteVocabulary && !pendingMutation;
  const valid = selectedIndex >= 0 && selectedIndex < vocabulary.length;
  byId("word-up").disabled = !canEdit || !valid || selectedIndex === 0;
  byId("word-down").disabled = !canEdit || !valid || selectedIndex === vocabulary.length - 1;
  byId("word-delete").disabled = !canEdit || !valid;
  byId("word-import").disabled = !canEdit;
  byId("selection-add").disabled = !canEdit;
  updateReadingModeButton();
}

function requestVocabularyEdit(type, payload) {
  if (!hasSession() || !canWriteVocabulary || pendingMutation) return;
  const requestId = send(type, { payload });
  if (!requestId) return;
  pendingMutation = { type, requestId };
  updateEditControls();
}

function selectedWordPayload() {
  if (selectedIndex < 0 || selectedIndex >= vocabulary.length) return null;
  return { index: selectedIndex, word: vocabulary[selectedIndex].word };
}

const highlightResizeObserver = new ResizeObserver(() => scheduleHighlightGeometry());
highlightResizeObserver.observe(byId("article-body"));
highlightResizeObserver.observe(byId("article-scroll"));

window.addEventListener("resize", () => scheduleHighlightGeometry());
document.fonts?.addEventListener?.("loadingdone", () => scheduleHighlightGeometry());
byId("article-scroll").addEventListener("scroll", () => {
  if (selectedPassage && !selectionAddPositionFrame)
    refreshSelectionAddPosition();
});
window.addEventListener("resize", () => {
  if (selectedPassage) refreshSelectionAddPosition();
});
document.addEventListener("visibilitychange", () => {
  if (document.hidden) {
    stopSelectionAddPositionTimer();
    byId("selection-add").hidden = true;
  } else if (selectedPassage) {
    updateSelectionAdd();
  }
});
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
  if (!selectedPassage || !hasSession()) return;
  requestVocabularyEdit("addWord", { ...selectedPassage });
});
byId("highlight-toggle").addEventListener("click", () => {
  if (!hasSession()) return;
  send("setHighlight", { payload: { enabled: !highlightsVisible } });
});
document.addEventListener("selectionchange", updateSelectionAdd);

