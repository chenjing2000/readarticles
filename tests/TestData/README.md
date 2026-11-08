# Unified test data

This directory is the only checked-in source of fixed test inputs.

- `Library/`: fixed article, vocabulary, and navigation JSON fixtures.
- `Audio/`: documents the generated shared audio pool. The runner creates short PCM WAV files in its isolated temporary workspace from the main article and vocabulary JSON.

The main article has 13 paragraphs, 62 stable SIDs, and 120 vocabulary entries. The `s011_uk` and `s011_us` sentence recordings are intentionally absent. Opening the article must not scan or report missing sentence recordings; only attempting to play the affected sentence should report its own missing recording.

WAV fixtures are synthetic tones used only to exercise media decoding and playback lifecycle. They are not pronunciation audio. MP3-priority and corrupt-MP3 behavior remain covered by focused tests that construct their own temporary files.

Do not edit this directory during a test run. When changing fixtures, update the relevant regression assertions and this inventory together.
