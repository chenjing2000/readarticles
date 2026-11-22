---
name: script-generation
description: Convert a WhisperX JSON transcript directly into a ReadArticles filetype "script" companion, preserving source text and real word timestamps without requiring a reference Passage.
---

# WhisperX to ReadArticles Script

## Purpose

Convert one original WhisperX JSON transcript into one UTF-8 `<name>.script.json` file accepted by the ReadArticles Script reader. A matching Passage is **not required to perform conversion**; ReadArticles requires a `<name>.json` Passage beside the resulting companion before the Script view can be opened.

This skill converts transcript content only. Do not generate or rewrite Passage, Vocabulary, audio, speaker labels or other companion files. Do not perform a second transcription, translate the speech, invent missing words, or infer word timing.

## Input and output

**Required input:** the original WhisperX JSON result containing a nonempty `segments` array. Each segment normally has `text`, `start`, `end`, and optionally `words`. Aligned words typically contain `word`, `start`, and `end`; `score`, `speaker`, `language`, `chars`, `word_segments`, and other metadata are not output. The segment `text` is the textual authority, even when its `words` disagree with it.

An input with only SRT/VTT/TXT, or only a flat `word_segments` list but no `segments[].text`, is **not** the supported direct-input format. Ask for the original WhisperX JSON rather than silently fabricating full sentence text.

**Optional inputs:** an existing Passage JSON or its filename stem (for companion naming), an explicit Script title, and an output location. A Passage is only a naming/context reference in this skill; it is not used to silently correct the transcript or assign timestamps.

Write a JSON object with exactly these root keys:

```json
{
  "filetype": "script",
  "title": "",
  "paragraphs": [
    [
      {
        "text": "Hello, world.",
        "timed_units": [
          ["Hello,", 0.2, 0.6],
          ["world.", 0.65, 1.1]
        ]
      }
    ]
  ]
}
```

- `filetype` is exactly `"script"`.
- `title` is a required string; use the user's explicit title when given, otherwise `""`. Do not invent a title from a filename or transcript.
- `paragraphs` is a **nonempty** array of **nonempty** sentence arrays. Each sentence is an object containing a nonblank `text` string and a `timed_units` array (which may be empty).
- Each timed unit is exactly `[displayText, startSeconds, endSeconds]`. `displayText` must be a nonempty substring of the sentence's `text` in occurrence order. Both times must be finite nonnegative numbers with `start < end`, or **both** must be `null`. Never mix a number with `null`.

## Conversion procedure

### 1. Recover the spoken text

1. Read `segments` in their original order; reject a missing, non-array, or empty `segments` value. Require a string `text` on every segment. An empty/whitespace-only segment contributes no sentence, but must not make other segments disappear.
2. Build the reading text from the **segment `text` fields**, not by joining the word list. Preserve all actual words, repetitions, spelling, capitalization and punctuation. Normalize only incidental leading/trailing or duplicated formatting whitespace and join adjacent fragments with appropriate spacing; do not insert or remove lexical content.
3. Retain repetitions even when segments overlap in time. Do not silently delete speech because two phrases are alike, or because timestamps overlap. Ignore metadata such as confidence, language and diarization unless the user separately asks to display it.
4. If no nonblank transcription remains, report that a valid Script cannot be generated; do not output dummy paragraphs.

### 2. Make readable sentences and paragraphs

1. Recover complete sentences using actual punctuation and language context. WhisperX segments are timing/transcription chunks, **not** necessarily sentence or paragraph boundaries. A sentence may cross several segments; a segment may contain several sentences.
2. Respect abbreviations, initials, decimal numbers and quoted punctuation. Do not mechanically split at commas, colons, semicolons, every time gap or every WhisperX segment. When punctuation is absent or a boundary is uncertain, keep the text together instead of inventing sentence-ending punctuation.
3. Make natural paragraphs at clear topic/argument shifts or substantial speech breaks supported by context. When uncertain, keep sentences in one paragraph. Preserve the original reading order and every spoken word.
4. Each resulting sentence `text` is the exact resulting display span, trimmed at its ends. Adjacent sentences in the same paragraph remain in reading order. Keep the source's words and punctuation even if transcription contains errors; correction needs an explicitly requested separate workflow.

### 3. Attach authentic word timings

1. First identify each word's occurrence in the **original segment's text**, then carry that anchored occurrence into the assembled sentences. Use increasing character offsets so repeated words map to their proper occurrences. Segment-local matching prevents identical words elsewhere in the transcript from stealing a timestamp.
2. For each `segments[i].words[j]` with nonempty `word`, remove incidental outer whitespace solely for matching. Prefer exact text matching; a unique, unambiguous case-insensitive spelling match is also allowed, but always emit the **exact substring from the final sentence** as `displayText`. Do not normalize apostrophes, punctuation or spelling merely to force a match.
3. Emit units in the order their display spans occur within each sentence. Never duplicate a matched occurrence. Do not split a timed word between two sentences; adjust a sentence boundary to a real word boundary when necessary. A candidate that cannot be reliably located is omitted from `timed_units`; its text must still remain in `text`.
4. When a matched word has a valid finite nonnegative numeric `start` and `end` with `end > start`, copy those seconds **unchanged**. When either timestamp is absent, null, nonnumeric or invalid, emit `[displayText, null, null]`. Ignore `score` and other alignment metadata. Never interpolate, estimate, shift, round, or use segment timestamps to invent word-level timing.
5. Partially aligned transcripts are valid: matched words can have null intervals; unmatched text stays visible; a sentence may have an empty `timed_units` array.

ReadArticles finds timed-unit text with a case-sensitive, forward-only search within `sentence.text`. Therefore, final emitted text **must match exactly** and occur in order. A structurally valid but unlocatable unit will not be colored by the reader.

### 4. When WhisperX has only segment-level timing

If a segment has **no usable `words` entries**, has a valid numeric `start/end` interval, and its complete trimmed `text` occurs as one contiguous substring **entirely inside one output sentence**, emit one segment-level unit `[exactSegmentSubstring, segment.start, segment.end]`. This is coarse **segment-level** progress, **not** fabricated word alignment.

If the segment crosses output sentence boundaries, cannot be located exactly, or has invalid timing, leave its speech in `text` but do not create a timed unit for it. Do not apply this fallback to partly aligned segments with usable word entries. Never overlap a fallback unit with a word-timed unit in the same sentence.

## Example: original WhisperX input

```json
{
  "language": "en",
  "segments": [
    {
      "start": 0.2,
      "end": 1.1,
      "text": " Hello, world.",
      "words": [
        {"word": "Hello,", "start": 0.2, "end": 0.6, "score": 0.98},
        {"word": "world.", "start": 0.65, "end": 1.1}
      ]
    },
    {
      "start": 1.4,
      "end": 2.9,
      "text": " We went home.",
      "words": [
        {"word": "We", "start": 1.4, "end": 1.55},
        {"word": "went", "start": 1.6, "end": 1.9},
        {"word": "home."}
      ]
    }
  ]
}
```

Corresponding output:

```json
{
  "filetype": "script",
  "title": "",
  "paragraphs": [
    [
      {
        "text": "Hello, world.",
        "timed_units": [
          ["Hello,", 0.2, 0.6],
          ["world.", 0.65, 1.1]
        ]
      },
      {
        "text": "We went home.",
        "timed_units": [
          ["We", 1.4, 1.55],
          ["went", 1.6, 1.9],
          ["home.", null, null]
        ]
      }
    ]
  ]
}
```

The two source segments become two sentences in one paragraph; a word with no timestamps remains visible and has a null timing pair. The reader does not color that word from the audio clock.

## Filename, delivery and safeguards

- If given a matching Passage such as `Example.json`, name the output **`Example.script.json`** and place it beside the Passage. The Script's internal `title` is independent of the filename or Passage title.
- Without a Passage, derive a safe stem from the supplied WhisperX filename and create `<stem>.script.json` as a standalone deliverable. Explain that the reader will not load it until a matching `<stem>.json` Passage exists in the same directory. Do not rename or overwrite the original WhisperX source.
- Keep raw WhisperX input outside the scanned book tree when possible; unrelated JSON can otherwise appear as a passage candidate.
- Return the complete generated JSON file, not a patch, analysis report, or renamed source. Use UTF-8 JSON with no comments, markdown fences, speaker/score/offset fields, or fabricated `sid`/audio metadata.

## Final checks

Before delivery, verify:

1. Exactly `filetype`, `title` and `paragraphs` at the root; `filetype === "script"`; `title` is a string (empty allowed).
2. Every paragraph has at least one sentence; every sentence has nonblank `text` and `timed_units` as an array. No original nonblank WhisperX speech has been dropped, rewritten or duplicated by text assembly.
3. Every timed unit is a three-item array with an exact nonempty substring located in its sentence from the previous unit's end onward. Repeated surface words use distinct ordered occurrences; no unit crosses a sentence/paragraph boundary.
4. Time pairs are either `[number, number]` with finite `0 <= start < end`, or `[null, null]`. Timestamps are sourced unchanged from aligned WhisperX words or, under the explicit fallback rule, from a matching whole segment.
5. Optional score, speaker and source `word_segments` metadata are not carried into Script JSON. Output only the named Script companion, leaving Passage, Vocabulary and media untouched.

If validation fails, repair only the transformation. If the source is insufficient for a trustworthy conversion, clearly report the limitation instead of inventing missing transcript content or timings.
