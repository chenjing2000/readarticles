# Generated test audio

The runner generates a shared pool of short PCM WAV files from the fixed article and vocabulary JSON in its isolated temporary workspace. It creates UK and US recordings for every vocabulary stem and sentence SID except `s011`, whose absence is intentional.

These deterministic synthetic recordings exercise filename resolution, media decoding, playback position, pause/resume, and completion. They are not speech samples and must not be used as pronunciation content. No binary audio fixtures are stored in the repository.
