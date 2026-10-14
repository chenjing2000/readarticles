---
name: passage-segment
description: Use when creating ReadArticles passage JSON from a supplied title and English body, including sentence segmentation, SIDs, and blank placeholders.
---

# Passage Segment

## 1. Purpose

Create one complete ReadArticles passage file named:

```text
<title>.json
```

The filename stem is the authoritative Article title. Do not store `title` in the JSON.

This skill is CREATE-only. On successful conversion, output raw JSON only: no Markdown fence, commentary, patch, or reasoning fields. If the source cannot fit the supported format, do not produce a passage file; explain the format limit instead.

ReadArticles has two passage families:

- **Article**: no `[[n]]` placeholders; sentence audio is derived from SID.
- **ArticleBlank**: contains one or more valid `[[n]]` placeholders; sentence audio is disabled.

There is no `tts_enabled` field.

## 2. Input and Output

Input:

- Passage title;
- original Passage body.

The title must be suitable as a filename.

Output structure:

```json
{
  "filetype": "passage",
  "next_sid": 3,
  "paragraphs": [
    {
      "paragraph": [
        {
          "sid": "s001",
          "text": "First complete sentence."
        },
        {
          "sid": "s002",
          "text": "Second complete sentence."
        }
      ]
    }
  ]
}
```

Both families use the same Paragraph/Segment structure and omit `audio` completely from every Segment. Legacy `audio` fields are ignored on input and omitted from this skill's output. ReadArticles does not save passage JSON; removal from existing passage files happens only when an external tool saves or regenerates them.

## 3. Core Rules

### Paragraphs and text

- Natural Paragraph boundaries come from blank lines in the source.
- Formatting-only line wraps inside one natural Paragraph become one ASCII space.
- Preserve spelling, capitalization, punctuation, numbers, quotation marks, word order, and valid `[[n]]` placeholders.
- Remove only program-added leading/trailing whitespace.
- Every Segment must contain lexical content.

### Segments

One Segment represents one complete sentence meaning. Split only when that sentence meaning ends.

Potential endings include `.`, `?`, `!`, `...`, `…`, `。`, `？`, `！` when they actually end the sentence. Do not mechanically split at commas, semicolons, colons, abbreviations, initials, decimals, URLs, or internal quotation punctuation.

A Paragraph boundary always ends its final Segment.

### SIDs

- Use lowercase `s001`, `s002`, ... in reading order across the entire Passage.
- Start at `s001`; never emit `s000`. The supported range is `s001` through `s999` (at most 999 segments). If the source exceeds this limit, report the limitation instead of truncating it or emitting invalid IDs.
- SIDs are Passage-wide unique.
- `next_sid` is one greater than the largest emitted SID number, at most `1000`.

## 4. Article and ArticleBlank Rules

Determine the family from the Passage body only:

- no valid `[[n]]` placeholders → Article;
- one or more valid `[[n]]` placeholders → ArticleBlank.

Do not infer the family from an Exercise type alone.

For **Article**:

- Segment text must contain no `[[` or `]]`;
- no Segment contains an `audio` field;
- sentence recordings are resolved as `audio_sentences/<sid>_uk.mp3` or `.wav`, and `_us.mp3` or `.wav`, relative to the passage directory;
- for each accent, prefer an existing MP3, otherwise WAV; decoding failure does not trigger WAV fallback;
- no old directory, arbitrary JSON path, or audio index is used; no resource migration is performed.

For **ArticleBlank**:

- placeholders are `[[1]]`, `[[2]]`, ...;
- every number is an integer >= 1, appears exactly once Passage-wide, and numbering is continuous through `[[N]]`;
- malformed forms such as `[[0]]`, `[[x]]`, `[[ 1 ]]`, unmatched `[[`, or unmatched `]]` are invalid;
- no Segment may contain an `audio` field.

This skill outputs passage content only; it does not declare audio paths or generate audio, Vocabulary, Exercise, or cache files. Family detection depends on valid body placeholders, never on the presence of legacy `audio` fields.

## 5. Final Validation

Before returning JSON, verify:

- `filetype` is exactly `passage`;
- no `title` or `tts_enabled` field exists;
- Paragraph and Segment boundaries preserve the source meaning and order;
- SIDs are lowercase, unique, sequential in reading order, and `next_sid` is correct;
- Article has no placeholders, no Segment contains `audio`, and SID-based recordings use `audio_sentences/`;
- ArticleBlank has continuous unique `[[1]]..[[N]]` placeholders and no Segment `audio` field.
