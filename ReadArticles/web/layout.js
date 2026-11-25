import { byId } from "./dom.js";
import { hasSession, send } from "./bridge.js";
import { scheduleScriptGeometry } from "./reading.js";

let rightWidth = 300;
let splitting = null;
let splitterDragFrame = 0;
export const getRightWidth = () => rightWidth;

const splitter = byId("reader-splitter");
export function applyPaneWidth(width) {
  // Called only for C# snapshots/acknowledgments, never from pointer input.
  const applied = Number(width);
  if (!Number.isFinite(applied) || applied < 200 || applied > 400) return;
  rightWidth = applied;
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
  if (event.button !== 0 || !hasSession()) return;
  event.preventDefault();
  splitting = { pointerId: event.pointerId,
    width: Number(rightWidth) || 300,
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
