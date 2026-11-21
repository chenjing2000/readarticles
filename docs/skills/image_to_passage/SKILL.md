---
name: image-to-passage
description: Use when converting English textbook or page images into ReadArticles passage JSON and optional external exercise-authoring JSON.
---

# Image to Passage

## 1. Purpose

Convert English reading images into reader-compatible passage content and optional external authoring companions:

- required `<filename>.json` with `filetype: "passage"`;
- optional `<filename>.exercise.json` with `filetype: "exercise"`, for external authoring only.

The current ReadArticles reader skips `.exercise.json` files during navigation and does not load, display, answer, or grade them. The conventions below describe optional preparation output, not implemented exercise support.

Use the images as the source of truth. Preserve article and question text faithfully. Do not mix Exercise text into the Passage body.

On successful conversion, output valid raw JSON files only; do not wrap JSON in Markdown or add commentary inside the files. If the passage cannot fit the supported format, report the limitation without producing invalid or truncated JSON.

## 2. Input and Filenames

Input may include:

- one or more images in reading order;
- an optional explicit Passage title;
- an optional output directory.

When images overlap, keep duplicated source text only once.

Resolve the JSON display `title` in this order:

1. use an explicit user-supplied title;
2. otherwise transcribe the genuine article title shown in the source;
3. otherwise use an empty string; do not invent a title from the filename or article body.

Do not use page headers, unit labels, Exercise headings, captions, or other textbook metadata as the title.

The Passage JSON `title` is the authoritative display title, independently provided or inferred from the genuine article heading. The filename stem only identifies the source and related companion files. Write:

```text
<title>.json
<title>.exercise.json
```

Always store the chosen title in Passage JSON. Choose a valid filename stem separately if necessary.

## 3. Passage Extraction

Read the Passage in normal reading order and keep only its English body.

Exclude non-body material such as:

- separate questions and answer options;
- page numbers, unit labels, running headers/footers, QR-code text;
- decorative image text or unrelated captions;
- printed Chinese vocabulary glosses not belonging to the English body.

Preserve spelling, capitalization, punctuation, numbers, quotation marks, and word order. Do not paraphrase or translate the Passage.

Restore natural Paragraphs instead of copying visual line wraps. Join formatting-only line breaks with one ASCII space. Restore a word split only by line-end layout/hyphenation when the original word is clear.

If genuine blanks occur inside the Passage body, normalize them in reading order as:

```text
[[1]], [[2]], [[3]], ...
```

Do not create placeholders for separate questions outside the Passage. Each placeholder number must occur exactly once and numbering must be continuous from 1.

The Passage JSON uses exactly `filetype`, `title`, and `paragraphs`, with an array of nonempty sentence strings for each paragraph:

```json
{
  "filetype": "passage",
  "title": "Example",
  "paragraphs": [
    ["First sentence.", "Second sentence."]
  ]
}
```

Follow [Passage Segment](../passage_segment/SKILL.md) for segmentation and valid `[[n]]` placeholders. Passage has no SID, `text` object, `paragraph` wrapper, `next_sid` counter or sentence audio. No old-format compatibility is provided.

## 4. Exercise Extraction

Inspect the same images for an Exercise belonging to the Passage. If none of the authoring types below is present, create only `<filename>.json`.

For an optional external exercise companion, use one of these authoring types:

| `type` | Use | Main payload |
|---|---|---|
| `article_choice` | multiple-choice questions on a complete Article | `questions` with `prompt`, `options` |
| `article_answer` | free-response questions on a complete Article | `questions` with `prompt` |
| `article_cloze` | Passage blanks, each with its own option set | `items` with `options` |
| `article_cloze_words` | Passage blanks answered by typed word/phrase | `items` with `cue` |
| `article_cloze_sentences` | Passage blanks using one shared sentence pool | top-level `options` + `items` |

Do not emit legacy or other Exercise types outside these authoring conventions. Do not merge unrelated Exercise families into one file.

Every Exercise file starts with:

```json
{
  "filetype": "exercise",
  "type": "article_choice"
}
```

Use the matching authoring `type` and required payload.

### Common answer-unit rules

- `number` is a unique positive integer;
- never store user state such as `answer`, `user_answer`, `user_note`, `username`, `userdata`, or `correct`.

Keep the questions, answer options, and printed word cues faithfully. Do not extract answer keys, worked solutions, correctness annotations, or grading information into Exercise files.

### `article_choice`

```json
{
  "filetype": "exercise",
  "type": "article_choice",
  "questions": [
    {
      "number": 1,
      "prompt": "Why did this happen?",
      "options": [
        {"key": "A", "text": "Option A"},
        {"key": "B", "text": "Option B"}
      ]
    }
  ]
}
```

`questions` is non-empty; `prompt` is non-empty and contains no `[[n]]`; there are at least two options and their keys are unique.

### `article_answer`

Uses non-empty `questions`; each question has `number` and non-empty `prompt`. `prompt` contains no `[[n]]`.

### `article_cloze`

Uses non-empty `items`; each item has `number` and at least two keyed `options`. Each item number matches exactly one Passage placeholder.

### `article_cloze_words`

Uses non-empty `items`; each item has `number` and string `cue` (empty string allowed). A cue is part of the printed question, not an answer key. Item numbers match Passage placeholders exactly.

### `article_cloze_sentences`

Uses one shared non-empty `options` array with at least two options plus non-empty `items`. Shared option keys are unique single uppercase characters. Each item has `number`; item numbers match Passage placeholders exactly.

## 5. Final Validation

Before returning/writing files, verify:

- Passage and Exercise text are separated correctly and source order is preserved;
- `<filename>.json` satisfies `../passage_segment/SKILL.md`;
- Passage has `filetype="passage"`, a required string `title` (empty allowed), and two-dimensional sentence-string `paragraphs`;
- no sentence-level audio, SID or `text` object exists; fill-in-the-blank placeholders are continuous `[[1]]..[[N]]` when present;
- optional Exercise has `filetype="exercise"` and one of the listed authoring `article_*` types;
- all answer-unit numbers are unique positive integers;
- all cloze-family item numbers match Passage placeholder numbers exactly;
- no Exercise object contains user-answer state;
- no Exercise object contains answer keys, solutions, or grading information;
- all produced files are valid JSON.
