---
name: passage-segment
description: Create a ReadArticles Passage JSON from a user-supplied title and English body, preserving natural paragraphs and complete sentence boundaries.
---

# Passage Segment

## Purpose

Create one UTF-8 `<filename>.json` Passage document from the supplied article text and title. The `title` value comes from the user-provided title, not the filename; it is required but may be an empty string. The filename remains responsible only for identifying the article and matching the Script and Vocabulary companion filenames.

Output raw JSON only; do not wrap it in Markdown or add explanation fields. This is a breaking schema: no legacy Passage format or migration is supported.

## Format

```json
{
  "filetype": "passage",
  "title": "Example",
  "paragraphs": [
    ["First complete sentence.", "Second complete sentence."],
    ["A sentence in the next paragraph."]
  ]
}
```

The only keys in generated Passage files are `filetype`, `title` and `paragraphs`. Paragraphs are nonempty arrays of complete sentence strings. There are no sentence objects, SIDs, `text` keys, `next_sid`, `paragraph` wrappers, audio paths or `HasAudio` indicators.

## Segmentation

- Preserve natural paragraph boundaries from the source; do not create an extra paragraph for a line wrap.
- Preserve original spelling, punctuation, capitalization, numbers, quotation marks and word order.
- Split each paragraph at complete sentence boundaries, respecting abbreviations, decimals, initials, URLs, quotations and ellipses. Do not split mechanically at commas, colons or semicolons.
- Keep sentence order and paragraph order unchanged. Remove only formatting whitespace at the start/end of strings, and join soft line breaks inside natural sentences with a single ASCII space.
- Every paragraph has at least one sentence and every sentence contains non-whitespace lexical content.
- Preserve valid `[[n]]` fill-in-the-blank placeholders. They must appear exactly once for each integer 1 through N; do not create malformed `[[` / `]]` markers.
- Passage preserves sentence boundaries for reference and Script/WhisperX alignment; it does not declare or require sentence-level audio.

## Final checks

Validate exact `filetype`, string `title` (empty is allowed), nonempty `paragraphs` array, nonempty sentence arrays and sentence strings, and valid blank numbering. Do not create any additional fields. This skill does not generate Script, Vocabulary, exercises or audio files.
