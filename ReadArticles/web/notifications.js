import { byId, element } from "./dom.js";
import { hasSession, send } from "./bridge.js";

const toastEntries = new Map();

function dismissNotification(id) {
  if (hasSession()) send("notificationDismiss", { payload: { id } });
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

export function renderNotifications(notifications) {
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
