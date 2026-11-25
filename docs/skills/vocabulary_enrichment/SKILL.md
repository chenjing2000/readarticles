---
name: vocabulary-enrichment
description: Use when enriching or normalizing ReadArticles vocabulary JSON, optionally with a matching passage for context, phrase extraction, and ordering.
---

# Vocabulary Enrichment

## 1. Purpose

Update `<title>.vocabulary.json` by improving meanings, normalizing lexical forms, deduplicating entries, and—when a source Passage is provided—extracting useful phrases and reordering the final vocabulary by Passage occurrence.

Required input: a structured vocabulary JSON document (`<title>.vocabulary.json`, standalone `vocabulary.json`, or pasted JSON). For plain lists, first follow [Vocabulary Preparation](../../Vocabulary-Preparation.md).

Optional context:

```text
<title>.json
```

The Passage, when provided, is authoritative context and must never be modified.

A source Passage uses the current three-key format: `filetype: "passage"`, independent string `title`, and `paragraphs` as arrays of sentence strings (`string[][]`). Read its sentences in paragraph order and join adjacent sentences with spaces only when establishing natural reading order or locating phrases. Do not expect sentence objects, `sid`, `text`, `next_sid`, or audio metadata. Use the Passage text, not its filename or display title, to identify contextual meanings and first occurrences.

Return the **complete final `<title>.vocabulary.json`** as raw JSON only. Do not return Markdown fences, commentary, diffs, reasoning, confidence, or temporary analysis fields. This skill does not generate audio files.

## 2. Schema and Editing Permissions

Top level:

```json
{
  "filetype": "vocabulary",
  "words": []
}
```

Each word or phrase uses:

```json
{
  "word": "example",
  "family": [],
  "phonetic_uk": "...",
  "phonetic_us": "...",
  "meanings": [{"pos": "n.", "meaning": "例子；实例"}]
}
```

Do not create a separate `phrases` array and do not add `wid`.

An existing phrase is an entry whose trimmed `word` contains at least two whitespace-separated lexical tokens. Hyphenated forms such as `state-of-the-art` remain single-word entries for this permission rule.

| Entry | Allowed changes |
|---|---|
| Existing single word | Update `meanings` and Passage-attested `family`; change `word` only when lexical normalization requires it |
| Existing phrase | Canonicalize `word`, update meanings, and fix phrase fields when needed |
| New phrase | Create the complete phrase entry |

Remove legacy `audio` fields from all output entries. Apart from this removal and passage-supported `family` updates, if an existing single-word `word` is unchanged, modify only `meanings`. If normalization changes `word`, also:

- clear `phonetic_uk` and `phonetic_us` to `""`;
- never reuse or invent IPA from the old inflected form.

For an existing phrase, preserve phonetics when `word` is unchanged; if canonicalization changes `word`, clear both phonetics.

Single-word POS labels may include `n.`, `v.`, `adj.`, `adv.`, `prep.`, `conj.`, `pron.`, `det.`, `interj.`, `num.`. Phrase meanings use exactly `"pos": "phrase"`.

New meanings contain `pos` and a concise `meaning` in Simplified Chinese by default, or another language explicitly requested by the user. Do not invent examples or separate definition fields. Preserve existing unrelated extension metadata at root, entry and meaning levels; repair missing required fields when necessary.

## 3. Meaning Rules

When a Passage is available, determine the lexical role and current-context meaning **before normalization**. Normalization changes the headword, not the semantic context.

For an entry found in the Passage:

1. represent the current-context meaning and place it first when practical;
2. add only genuinely useful high-frequency/common meanings with independent learning value.

Preserve existing meanings that are already correct and useful. Revise only for a substantive reason: missing context meaning, wrong POS/sense, seriously unnatural translation, or a missing important common sense.

Avoid rare, archaic, obscure, overly technical, or excessively fine-grained senses unless the Passage actually uses them. Closely related Chinese glosses may be combined in one meaning object.

If no Passage is provided, do not invent passage-specific senses; make only clearly justified meaning corrections/additions.

## 4. Lexical Normalization

Normalization is context-sensitive. Determine the lexical role first; do not apply suffix/string rules mechanically.

| Case | Rule | Examples |
|---|---|---|
| Verb inflection | Normalize to dictionary/base form | `went/goes/going -> go`; `has/had -> have`; `does/did -> do` |
| `be` forms | Normalize all verbal forms to `be` | `am/is/are/was/were/been/being -> be` |
| Ordinary plural noun | Normalize to singular lemma | `children -> child`; `women -> woman`; `analyses -> analysis` |
| Lexicalized adjective/other POS | Preserve the contextual lexical form | do not mechanically reduce `interested`, `advanced`, etc. |
| Invariant/plural-like lexeme | Preserve the real lemma | `news`, `species`, `series` |
| Fixed phrase | Use the conventional whole-phrase form | keep `make ends meet`, `in terms of`, `had better` |

**Phrase canonicalization overrides word-by-word normalization.** Do not turn accepted expressions into forms such as `make end meet`, `in term of`, or `have better`.

### Person names in phrases

Normalize names only when they are part of a learnable phrase pattern:

```text
ask Tom for help        -> ask somebody for help
prevent Mary from doing -> prevent somebody from doing
Tom changed his mind    -> change one's mind
Tom enjoyed himself     -> enjoy oneself
Tom was eager to leave  -> be eager to do something
```

Use the conventional placeholder: normally `somebody`, or `one` / `one's` / `oneself` when required. If the name is only the sentence subject, omit it rather than inserting `somebody`.

Do not modify the Passage, automatically replace standalone proper-name entries, or remove lexicalized proper names that genuinely belong to an established expression.

### Deduplication after normalization

If several entries normalize to the same lexical item:

1. keep an existing entry already in canonical form if one exists; otherwise keep the earliest original entry;
2. merge only useful non-duplicate meanings;
3. keep the context meaning first when identifiable;
4. do not overwrite canonical phonetics with data from an inflected form;
5. inherit the earliest relevant Passage occurrence for ordering.

Compare lexical identity case-insensitively after trimming and normalizing whitespace. Combine supported inflected surface forms under the surviving canonical headword.

### Family forms from the source Passage

`family` is an optional list of strings. It may be omitted or `[]`, but must not be `null` or a scalar. It records **only the actual forms in the supplied Passage body** that differ from the canonical `word` ignoring case. For example, when the source contains `went` and `goes`, output `"word": "go", "family": ["went", "goes"]`; do not add `gone` unless it is present in the Passage.

- Determine grammatical identity in context: include inflected verbs, plural nouns, comparatives/superlatives, and grammatical variants of fixed expressions (e.g. `took off` under `take off`). Do not group derived words (e.g. `decision` under `decide`), synonyms or unrelated homographs.
- Preserve the casing and spelling of the first source occurrence; order distinct forms by first occurrence. Omit strings identical to `word` ignoring case, duplicate forms ignoring case, empty strings, leading/trailing spaces and repeated whitespace.
- For expressions containing placeholders, include a realized phrase only if it is unambiguously the same phrase in the source, never a hypothetical substitution.
- After normalization or deduplication, combine eligible original surface forms under the surviving headword. Remove incoming `family` forms that are not attested in the supplied Passage.
- Without a source Passage, preserve existing valid `family` values (including an existing empty array) but do not invent potential forms. New entries without attested forms may omit the field.

ReadArticles matches both the canonical `word` and explicit `family` strings literally and case-insensitively, attributing matches to the canonical word for highlights and navigation. Direct headwords take priority over another entry's family. The reader does no automatic morphology or contextual disambiguation. Family does not change audio lookup or appear in the right-side word list.

## 5. Phrase Extraction

When a Passage is provided, extract only **useful multi-word expressions worth learning as a unit**. Precision is more important than recall.

Good candidates include phrasal verbs, fixed/semi-fixed expressions, linking/prepositional expressions, and common academic expressions such as `carry out`, `be responsible for`, `in contrast to`, and `a wide range of`.

Do not extract ordinary free combinations (`large population`, `beautiful city`) or arbitrary sentence fragments unless they clearly function as a lexical unit. Every new phrase must be supported by a grammatical surface form in the Passage. If no Passage is provided, do not invent passage-derived phrases.

Canonicalize the whole expression while preserving lexical identity:

```text
resulted in              -> result in
was responsible for      -> be responsible for
played a role in         -> play a role in
tried my best to         -> try one's best to
```

Do not replace phrases with synonyms or unnecessarily simplify them. For example, keep `in spite of` rather than replacing it with `despite`.

Before adding a phrase, compare its canonical `word` case-insensitively with existing entries and merge/update rather than duplicate. New phrase phonetics are `""`; phrase meanings use `"pos": "phrase"`.

## 6. Audio Naming, Collision Eligibility, and Ordering

Do not write `audio` path fields. Legacy `audio` fields are ignored on input and removed from output. ReadArticles derives filenames from `word`; it does not search old directories or indexes and does not migrate or rename existing resources.

Derive each stem by trimming outer whitespace, lowercasing with invariant-culture rules, replacing every character outside ASCII `a-z` and `0-9` with `_`, collapsing consecutive `_`, then trimming leading/trailing `_`.

```text
One's Own        -> one_s_own
state-of-the-art -> state_of_the_art
 A / B           -> a_b
café             -> caf
```

Word recordings belong beside the passage in `audio/<stem>_uk.mp3` or `.wav`, and `audio/<stem>_us.mp3` or `.wav`. For each accent, MP3 wins when present; WAV is selected only when MP3 is absent. An empty MP3 disables pronunciation; a nonempty undecodable MP3 reports failure without a WAV retry.

Lexical identities remain unique under the existing spelling rules. Distinct entries with the same stem must remain in the vocabulary: do not merge or delete them merely because their filenames collide, and do not add filename suffixes. In the final `words` order, the first entry immediately reserves its nonempty stem regardless of file existence or generation success. Later collisions retain all text but have no audio eligibility. Empty stems retain their entries with all audio disabled. Establish ownership after any passage-order sorting; reordering may change ownership. This skill does not generate audio.

### With a source Passage

Reorder the **entire final `words` array** by first meaningful occurrence in the Passage.

- Sort by the **original surface form**, not the normalized spelling (`convicted -> convict` keeps the position of `convicted`).
- If several surface forms normalize to one entry, use their earliest occurrence.
- Entries not locatable in the Passage go after all locatable entries and keep their original relative order.
- Never delete an entry merely because it is absent from the Passage.

### Without a source Passage

Do not reorder surviving entries. Preserve their original relative order. If normalization merges entries, the survivor occupies the earliest original position among them.

## 7. Final Validation

Verify that:

- output is the complete valid `<title>.vocabulary.json` with `filetype="vocabulary"` and `words`;
- the source Passage, when provided, was not modified;
- lexical role was determined before normalization;
- verbs, plurals, person placeholders, and fixed phrases follow the rules above;
- phrase-level canonical form takes priority over mechanical token normalization;
- normalization-created duplicates were merged safely;
- unchanged existing single words changed only in `meanings` and passage-supported `family`, apart from removing legacy `audio`;
- whenever `word` changes, phonetics are cleared;
- current-context meanings remain first when identifiable;
- new phrases are conservative, Passage-supported, canonicalized, and use `pos="phrase"`;
- with a Passage, `family` contains only distinct attested forms in first-occurrence order; without a Passage, no family forms were invented;
- with a Passage, ordering follows earliest original Passage occurrence and unlocated entries remain at the end in original relative order;
- without a Passage, surviving entries retain their original relative order;
- no `audio` fields remain; distinct stem collisions and empty stems retain their text, with audio eligibility determined by final list order;
- no audio file or extra analysis/user-state field was generated;
- output file contains raw JSON only; if not paired with a Passage or requested filename, use standalone `vocabulary.json`.
