// Composition and cross-module snapshot dispatch. No business or DOM state.
import { byId, element } from "./dom.js";
import { connect } from "./bridge.js";
import { configureReading, initializeReading, handleModeMessage, completeModeRequest } from "./reading.js";
import { beforeArticleRender, initializeVocabulary, updateVocabulary, completeVocabularyRequest,
  isMutationPending, refreshHighlights } from "./vocabulary.js";
import { getPlayback, renderPlayback } from "./playback.js";
import { applyPaneWidth } from "./layout.js";
import { renderSettings } from "./settings.js";
import { renderNotifications } from "./notifications.js";

configureReading({ beforeRender: beforeArticleRender, refreshHighlights, isMutationPending, getPlayback });

function initialize(payload) {
  initializeReading(payload);
  initializeVocabulary(payload);
  renderSettings(payload.settings ?? {});
  renderPlayback(payload.playback ?? {});
  renderNotifications(payload.notifications ?? {});
}

function message(message) {
  switch (message.type) {
    case "setReadingMode": handleModeMessage(message); break;
    case "setVocabulary": updateVocabulary(message.payload); break;
    case "setSettings": renderSettings(message.payload ?? {}); break;
    case "setPaneWidth": applyPaneWidth(Number(message.payload?.rightWidth)); break;
    case "setPlayback": renderPlayback(message.payload ?? {}); break;
    case "setNotifications": renderNotifications(message.payload ?? {}); break;
    case "actionResult":
      if (!completeModeRequest(message)) completeVocabularyRequest(message);
      break;
  }
}

if (!connect({ initialize, message })) {
  byId("article-body").replaceChildren(
    element("p", "empty", "此页面需由 ReadArticles 桌面程序加载。"));
}
