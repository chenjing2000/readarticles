# ReadArticles documentation

This index links to the **current** application contracts, developer guides, and content-preparation skills. Completed implementation logs and dated audits remain accessible in Git history and merged pull requests, not as competing instructions.

## Application and development

| Document | Purpose |
| --- | --- |
| [Project contract](project-contract.md) | Authoritative technical, data-protection and behavior invariants |
| [Development handoff](HANDOFF.md) | Current GitHub baseline, review/acceptance evidence and next steps |
| [User guide](User-Guide.md) | Reading, vocabulary and audio operation |
| [Application overview](Application-Overview.md) | Architecture, modules and responsibilities |
| [Library layout](Library-Layout.md) | JSON formats, directory rules, Script and audio naming |
| [Configuration](Configuration.md) | Settings, appearance, persistence and recovery |
| [Vocabulary preparation](Vocabulary-Preparation.md) | Vocabulary JSON authoring and validation |
| [Runtime logging](Logging.md) | Diagnostic events, retention and privacy |
| [Testing and acceptance](Testing.md) | Core/UI/Full, fixtures and manual/published checks |
| [Publishing](Publishing.md) | Portable release and protected replacement |
| [Test fixtures](../tests/TestData/README.md) | Fixed article and vocabulary fixtures |

## Skills

- [Project code review](skills/project-code-review/SKILL.md) — approved change scope and regression invariants
- [Image to passage](skills/image_to_passage/SKILL.md) — externally prepare passage JSON from images
- [Passage segmentation](skills/passage_segment/SKILL.md) — segment and validate passage JSON
- [Vocabulary enrichment](skills/vocabulary_enrichment/SKILL.md) — enrich and normalize vocabulary JSON

The project contract owns invariants; Library Layout owns file discovery and formats; Configuration owns settings values; User Guide owns operation; Application Overview owns implementation responsibilities; Testing and Publishing own verification and release instructions. HANDOFF records verified baselines and outstanding work only. Keep historical implementation details in Git/PRs. External authoring Skills define producer rules; they must not imply runtime support. Avoid duplicating changing settings, test counts or release instructions across documents.
