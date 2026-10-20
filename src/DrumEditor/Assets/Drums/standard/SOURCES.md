# 13-track drum sources

Clap: VCSL Clap_rr1 (CC0), replacing SoloClap_vl3.

The four tom sounds are derived from two recorded Virtuosity Drums toms (CC0), not four separately recorded drums. LowTom/LowMiddleTom use the low recording shifted +4/+6 semitones; HighMiddleTom/HighTom use the high recording at +0/+2 semitones. Bandlimited sinc resampling avoids high-frequency aliasing. FloorTom retains the original low recording. Peak normalization and edge fades are applied before PCM16 conversion. Gains are in manifest.json. Existing linear master volume (50% default) and peak protection remain.

All exact URLs, source/output hashes, pitch shifts and authors are in provenance.json; license files accompany the kit. Reproduce with import_web_drum_assets.py, replace_natural_drum_assets.py, then expand_toms.py. Backup: tools/kit-before-13tracks.zip.

| Instrument | Source | License |
|---|---|---|
| Kick | [Recorded sample](https://raw.githubusercontent.com/tidalcycles/uzu-drumkit/2f3e05c70ab4d73ad053a1467adec89bd27377a0/bd/10_bd_switchangel.wav) | Unlicense |
| Snare | [Recorded sample](https://raw.githubusercontent.com/tidalcycles/uzu-drumkit/2f3e05c70ab4d73ad053a1467adec89bd27377a0/sd/10_sd_switchangel-bounce-2.wav) | Unlicense |
| HiHat | [Recorded sample](https://raw.githubusercontent.com/tidalcycles/uzu-drumkit/2f3e05c70ab4d73ad053a1467adec89bd27377a0/hh/10_hh_switchangel.wav) | Unlicense |
| OpenHH | [CM Music](https://ccmixter.org/files/carbonmonoxidemusic/23425) | CC0-1.0 |
| Clap | [Versilian Studios / VCSL](https://github.com/sgossner/VCSL) | CC0-1.0 |
| Perc | [Recorded sample](https://raw.githubusercontent.com/sgossner/VSCO-2-CE/440300901dfe9275fd84e0b7763af1f8443ae62e/VSCO%201%20Percussion/varWood/claves_mf.wav) | CC0-1.0 |
| LowTom | [Versilian Studios / Karoryfer Samples; Austin McMahon](https://versilian-studios.com/virtuosity-drums/) | CC0-1.0 |
| LowMiddleTom | [Versilian Studios / Karoryfer Samples; Austin McMahon](https://versilian-studios.com/virtuosity-drums/) | CC0-1.0 |
| HighMiddleTom | [Versilian Studios / Karoryfer Samples; Austin McMahon](https://versilian-studios.com/virtuosity-drums/) | CC0-1.0 |
| HighTom | [Versilian Studios / Karoryfer Samples; Austin McMahon](https://versilian-studios.com/virtuosity-drums/) | CC0-1.0 |
| FloorTom | [Versilian Studios / Karoryfer Samples; performance Austin McMahon](https://versilian-studios.com/virtuosity-drums/) | CC0-1.0 |
| Crash | [Recorded sample](https://raw.githubusercontent.com/tidalcycles/uzu-drumkit/2f3e05c70ab4d73ad053a1467adec89bd27377a0/cr/10_cr_switchangel.wav) | Unlicense |
| Ride | [Recorded sample](https://raw.githubusercontent.com/tidalcycles/uzu-drumkit/2f3e05c70ab4d73ad053a1467adec89bd27377a0/rd/10_rd_switchangel.wav) | Unlicense |
