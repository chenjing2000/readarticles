# Vocabulary Preparation

## Purpose and scope

Produce a complete ReadArticles vocabulary file from either an existing structured vocabulary document or a plain list of words/phrases. Improve useful meanings, normalize lexical forms where justified, remove duplicates, and use the current derived audio naming rules without storing paths.

Use the supplied [Vocabulary Enrichment skill](skills/vocabulary_enrichment/SKILL.md) for structured input. This guide supplies the plain-list preparation step and the reader-specific compatibility checks. It does not modify the source passage, generate speech files, look for arbitrary files without a supplied context, or install anything in the application.

## Inputs

**Structured input:** accept `<title>.vocabulary.json`, a standalone `vocabulary.json`, or pasted vocabulary JSON. Preserve the original entries and relevant extension metadata. Remove legacy `audio` fields and repair missing required fields before emitting a reader-compatible result; do not discard a document merely because it is incomplete.

**Unstructured input:** accept a text document or pasted word/phrase list. Prefer one entry per line. Remove explicit bullet markers and list numbering, not meaningful punctuation. Preserve each multi-word expression as a unit. For a declared comma/tab-separated format, use that delimiter; do not blindly split phrases at spaces, commas, slashes, or apostrophes. A JSON array of strings is also a plain-list input rather than the final application schema. Ask only if the source does not establish entry boundaries.

Optional inputs are a matching passage JSON, a desired filename/destination, and a translation language. Use the passage's segment text in reading order as authoritative context, without editing it.

The default gloss language is Simplified Chinese. A user can request another language; the runtime schema accepts strings in any language. These instructions and their structural examples are English-only. Do not treat an English example below as a requirement to change the requested output language.

## Output and placement

Return or write the complete UTF-8 JSON document, not a diff, list of changes, or partial entry array. The data file contains raw JSON only, with no Markdown fences, comments, reasoning, or confidence fields.

When pairing with a passage, use its exact filename stem: `Example.json` pairs with `Example.vocabulary.json` in the same directory. A file named `vocabulary.json` is valid as an explicit import input but is not automatically loaded as a companion. Without a passage or requested title, use `vocabulary.json` as a standalone deliverable and explain its placement/import role outside the data file. See [Library Layout](Library-Layout.md).

## Reader-compatible schema

```json
{
  "filetype": "vocabulary",
  "words": [
    {
      "word": "example",
      "family": [],
      "phonetic_uk": "",
      "phonetic_us": "",
      "meanings": [
        {"pos": "n.", "meaning": "an instance used to explain an idea"}
      ]
    },
    {
      "word": "carry out",
      "phonetic_uk": "",
      "phonetic_us": "",
      "meanings": [
        {"pos": "phrase", "meaning": "perform or complete a task"}
      ]
    }
  ]
}
```

The English glosses demonstrate the structure; actual glosses use the chosen target language.

| Field | Contract |
| --- | --- |
| `filetype` | Exactly `"vocabulary"` |
| `words` | Array of entries; `[]` is a valid empty vocabulary |
| `word` | Non-empty string; normalized lexical identities are unique case-insensitively |
| `family` | Optional array of strings; it may be omitted or `[]`, never `null`. Author only forms attested in a supplied article; the runtime validates normalized nonempty strings distinct from the headword and from one another, not article attestation |
| `phonetic_uk`, `phonetic_us` | Strings in generated files; use `""` when pronunciation is unavailable or not verified, never `null`. The reader also tolerates omitted phonetic keys |
| `meanings` | Array of objects with string `pos` and `meaning`; `[]` is technically readable, but supply useful meanings when enrichment can be supported |
| Legacy `audio` | Ignored while reading; removed when saving or regenerating JSON; never emitted in new files |

Never emit null word entries or null meanings. Do not introduce a separate `phrases` array, `wid`, examples, English-definition fields, user answers, or analysis metadata. The `meaning` string holds the gloss in the chosen language. Preserve existing unrelated extension metadata rather than deleting it during enrichment; do not invent new metadata fields.

Use conventional single-word POS labels such as `n.`, `v.`, `adj.`, `adv.`, `prep.`, `conj.`, `pron.`, `det.`, `interj.`, or `num.`. Use `phrase` for phrase meanings. These labels are authoring conventions; the reader itself accepts string labels.

## Plain-list preparation and enrichment

Convert plain-list entries into the schema above, then apply the shared [Vocabulary Enrichment skill](skills/vocabulary_enrichment/SKILL.md). The following summarizes the reader-facing rules; the skill owns detailed linguistic decisions.

Determine the lexical role and passage-context sense before normalizing. Do not apply suffix removal mechanically.

| Situation | Rule | Example |
| --- | --- | --- |
| Verb inflection | Use the dictionary/base form | `went`, `goes`, `going` -> `go` |
| Verbal forms of `be` | Normalize to `be` | `was`, `were`, `been` -> `be` |
| Ordinary plural noun | Use its singular lemma | `children` -> `child`; `analyses` -> `analysis` |
| Lexicalized adjective | Keep the real contextual lexical form | Do not automatically reduce `interested` or `advanced` |
| Invariant lexeme | Keep the actual lemma | `news`, `species`, `series` |
| Fixed expression | Canonicalize the whole phrase | Preserve `make ends meet`, `in terms of`, and `had better` |

Use conventional phrase placeholders only where they express a learnable pattern: `ask Tom for help` -> `ask somebody for help`; `Tom changed his mind` -> `change one's mind`. Do not automatically rewrite standalone proper names or replace names inside established expressions.

For a passage-supported entry, put the contextual meaning first when practical, then add only useful common senses. Keep existing correct, useful meanings. Correct wrong POS/senses, missing context meanings, and seriously unnatural translations; do not expand into rare or highly technical senses unless context calls for them. Without a passage, do not invent a passage-specific sense.

Always remove legacy `audio` fields from output. Apart from this removal and passage-supported `family` updates, if the input is already structured and an existing single-word spelling stays unchanged, normally change only its meanings. Preserve verified phonetics. Repair missing or invalid schema fields when necessary for readability. If a word or phrase is renamed, clear both phonetic fields; pronunciation of an inflected form is not pronunciation evidence for its lemma.

For new entries, leave phonetics empty unless trustworthy pronunciation information is available. Do not guess IPA. Missing or uncertain meanings must not be fabricated; retain a readable partial entry and report unresolved enrichment outside the JSON if clarification is needed.

## Deduplication and phrases

Apply the [skill's normalization, deduplication and phrase extraction rules](skills/vocabulary_enrichment/SKILL.md). Compare lexical identities case-insensitively after whitespace normalization; keep canonical metadata and phonetics, merge useful meanings, and add only Passage-supported phrases. Use empty phonetics for renamed or new unverified entries.

## Audio stem algorithm and eligibility

For every entry, derive the stem exactly as the reader does:

1. Trim outer whitespace and lowercase using invariant-culture rules.
2. Replace each character outside ASCII `a-z` and `0-9` with `_`.
3. Collapse consecutive underscores and trim leading/trailing underscores.

```text
be responsible for -> be_responsible_for
 A / B              -> a_b
state-of-the-art    -> state_of_the_art
One's Own           -> one_s_own
café                -> caf
```

Word recordings use `audio/<stem>_uk.mp3` or `.wav` and `audio/<stem>_us.mp3` or `.wav`, relative to the passage directory. Prefer an existing MP3 for each accent, otherwise WAV; an empty MP3 disables the pronunciation action, and a nonempty undecodable MP3 fails without a WAV retry. These paths are inferred, never stored in JSON.

Distinct words with the same stem remain in the final list. Reserve each nonempty stem immediately for its first entry in the final `words` array, irrespective of file existence or generation success. All later collisions retain their text but lose all audio eligibility; no suffixes or fallback ownership. Empty stems retain their entries with audio disabled. Spelling-duplicate validation is unchanged. Establish ownership after ordering, because moving entries changes the first owner.

Do not create dummy audio, claim pronunciation is playable, delete old files, or rename existing recordings. Old directories (`audio_vocabulary/`, `audio_segments/`, `audio_sentences/`), old names, SIDs and audio indexes are not fallback sources and are not migrated. The reader does not support sentence recordings; see [Library Layout](Library-Layout.md).

## Ordering and literal matching

With a passage, sort the complete final list by first meaningful occurrence of the original surface forms, not the normalized spellings. A merged entry inherits the earliest relevant occurrence. Keep unlocated entries after located entries, preserving their original relative order; do not delete them merely because they are absent from the passage.

Without a passage, preserve surviving input order. When entries merge, the survivor occupies the earliest original position of the merged group.

ReadArticles matches each `word` plus its explicitly listed `family` forms case-insensitively. Each match belongs to the canonical headword for highlighting and navigation; a direct `word` match has priority over another entry's `family` match. It does not infer inflections or disambiguate homographs automatically. For example, `convict` highlights `convicted` only if its `family` includes `"convicted"`.

With a source Passage, populate `family` only from forms actually present in its body: attested verb inflections, plural nouns, comparatives/superlatives and inflected fixed expressions when the grammatical relationship is clear. Do not include hypothetical forms, unrelated homographs, or derived words such as `decision` under `decide`. Preserve the first-occurrence spelling and order. Exclude duplicates, the canonical headword itself, empty forms, leading/trailing whitespace and repeated whitespace. With no source Passage, retain existing valid `family` metadata but never invent forms. Audio filenames still derive only from `word`; the right-side vocabulary list does not display `family`.

## Final checks

- Parse the complete JSON and check all required types, exact `filetype`, non-null fields, and preserved metadata.
- Confirm contextual meaning/POS decisions and conservative normalization; preserve fixed phrases and supported input entries.
- Confirm case-insensitive lexical uniqueness, valid optional `family` forms, absence of `audio` fields, and first-entry audio ownership for each nonempty stem; preserve distinct colliding entries and entries with empty stems.
- Confirm verified phonetics were preserved only for unchanged forms and cleared for renamed forms.
- Confirm ordering follows the supplied context or original input order, as appropriate.
- Confirm the source passage and physical audio remain unchanged. Do not generate exercises or user state.
- Confirm the destination name matches the intended passage, or that standalone import use is explicit.
