// The only browser-side owner of protocol identity.
export const pageId = crypto.randomUUID();
let sessionId = null;
let vocabularyRevision = 0;
export const hasSession = () => !!sessionId;

export function send(type, additional = {}) {
  if (!window.chrome?.webview) return null;
  const requestId = additional.requestId ?? crypto.randomUUID();
  window.chrome.webview.postMessage({
    version: 1, type, pageId, sessionId, vocabularyRevision,
    ...additional, requestId
  });
  return requestId;
}

export function connect(handlers) {
  if (!window.chrome?.webview) return false;
  window.chrome.webview.addEventListener("message", event => {
    const message = event.data;
    if (message?.version !== 1 || message.pageId !== pageId) return;
    if (message.type === "initialize") {
      if (typeof message.sessionId !== "string" || typeof message.requestId !== "string") return;
      sessionId = message.sessionId;
      vocabularyRevision = Number(message.vocabularyRevision) || 0;
      try {
        handlers.initialize(message.payload ?? {});
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
    }
    handlers.message(message);
  });
  send("ready");
  return true;
}
