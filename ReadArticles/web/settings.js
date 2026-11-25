import { applyPaneWidth } from "./layout.js";
import { applyHighlightSetting } from "./vocabulary.js";
import { scheduleScriptGeometry } from "./reading.js";

function fontStack(selected, choices) {
  const names = [selected, ...choices.filter(name => name !== selected)];
  return names.filter(name => choices.includes(name))
    .map(name => '"' + name + '"').join(", ") + ", sans-serif";
}

export function renderSettings(settings) {
  const a = settings.appearance ?? {};
  const title = a.title ?? {};
  const body = a.body ?? {};
  const word = a.vocabulary ?? {};
  const phonetic = a.phonetics ?? {};
  const meaning = a.meanings ?? {};
  const english = ["Georgia", "Microsoft YaHei UI", "Nunito", "Segoe UI"];
  const chinese = ["Microsoft YaHei", "SimSun", "KaiTi"];
  const variables = {
    "--title-font": fontStack(title.font ?? "Georgia", english),
    "--title-size": (title.size ?? 18) + "px",
    "--title-line": String(title.lineHeight ?? 1.7),
    "--title-after": (title.paragraphAfter ?? 16) + "px",
    "--title-color": title.color ?? "#283747",
    "--body-font": fontStack(body.font ?? "Georgia", english),
    "--body-size": (body.size ?? 15) + "px",
    "--body-line": String(body.lineHeight ?? 1.7),
    "--body-after": (body.paragraphAfter ?? 16) + "px",
    "--body-indent": (body.firstLineIndent ?? 2) + "em",
    "--body-color": body.color ?? "#283747",
    "--reading-bg": body.background ?? "#F8F8F6",
    "--script-color": body.scriptColor ?? "#E02020",
    "--selection-add-size": (body.selectionAddSize ?? 0.65) + "em",
    "--selection-background": body.selectionBackground ?? "#DDE8F2",
    "--word-font": fontStack(word.font ?? "Georgia", english),
    "--word-size": (word.size ?? 16) + "px",
    "--word-color": word.color ?? "#B7394D",
    "--phonetic-size": (phonetic.size ?? 14) + "px",
    "--phonetic-color": phonetic.color ?? "#0A7785",
    "--meaning-font": fontStack(meaning.font ?? "Microsoft YaHei", chinese),
    "--meaning-size": (meaning.size ?? 15) + "px",
    "--meaning-line": String(meaning.lineHeight ?? 1.5),
    "--meaning-color": meaning.color ?? "#283747"
  };
  for (const [name, value] of Object.entries(variables))
    document.documentElement.style.setProperty(name, value);
  applyPaneWidth(settings.rightWidth);
  applyHighlightSetting(settings.highlightsVisible);
  scheduleScriptGeometry();
}
