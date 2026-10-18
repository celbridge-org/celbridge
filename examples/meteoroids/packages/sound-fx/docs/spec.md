# Sound FX Editor — Specification

## Overview

The Sound FX Editor is a Celbridge package for making retro game sound effects. It is a
document editor for `.wav` files: a student adds a new sound with **New File →
Explosion**, opens it, and shapes it with sliders on a full-size editor surface. Every
change is saved straight back into the `.wav`, which a game (such as Meteoroids) plays
directly.

Sounds are synthesised by [jsfxr](https://github.com/chr15m/jsfxr), the JavaScript port of
Dr Petter's classic **sfxr**. We use jsfxr only as a synthesis library, with our own
front end on it. The synth settings are stored inside the `.wav` itself, so a sound can be
reopened and tweaked at any time, and copying or renaming the file keeps them.

## Goals

- **Fun first.** Presets, Mutate and Random get a student to a usable sound in a few clicks. The sliders are there for when they want to shape it.
- **Instant feedback.** Every change re-renders the sound, redraws the waveform and plays it.
- **The file is the sound.** A `.wav` made here plays in any game, browser or audio app. The synth settings travel inside it; there is no sidecar or separate project file.
- **Editable forever.** Reopening a sound shows exactly the settings it was made with.
- **Untouched unless edited.** Opening and closing a sound without changing it never rewrites the file.
- **Safe with other WAVs.** Recordings and downloaded sounds open without harm. The editor never overwrites audio it didn't make unless the user confirms.
- **Native look and feel.** The editor uses the Celbridge client's shared styles, so it matches the built-in editors in every theme.

## Non-goals

- Editing recorded audio: trimming, fades, effects, or mixing on arbitrary WAV files.
- Other audio formats (`.ogg`, `.mp3`, `.flac`). The editor claims `.wav` only.
- Music, sequencing, or multi-sound "packs". One file is one sound.
- Stereo or sample rates other than 44.1 kHz.
- Exporting or importing sfxr.me share links or `.bfxr` files. (Possible later; see *Deferred*.)

## Package

- A Celbridge package named `sound-fx` (title **Sound FX**) in `packages/sound-fx/`. It contributes one document editor, id `sound-document`, display name **Sound FX Editor**.
- One `[[file-types]]` entry for `.wav`. It's an established type in the host's file type catalog, which supplies its icon, so the package declares no icon.
- It becomes the default editor for `.wav` with no extra configuration, because the host picks the first contribution that supports an extension ahead of the built-in editors. The built-in viewer stays available in the Open With picker.
- **New File templates**, one per preset (see *Templates*).
- The editor is a WebView page that uses the Celbridge JS client for the document lifecycle, view state, toasts and localization. The editor manifest declares `binary = true`, so the host passes the document to the editor as base64 and saves the base64 the editor gives it through `document.save`.
- Plain ES modules, no build step. jsfxr is vendored (see *jsfxr*).
- All user-facing strings are localized (`localization/en.json`).

### Layout

```
packages/sound-fx/
  package.toml
  sound.editor.toml
  index.html
  css/sound-fx.css
  js/
    sound-fx.js         document lifecycle, UI wiring, undo, auto-save
    wav-file.js         RIFF parse/write, including the settings chunk
    waveform.js         waveform canvas drawing
    params.js           slider definitions: groups, labels, ranges, unit formatting
  lib/
    sfxr.js             jsfxr, unmodified
    riffwave.js         jsfxr's WAV writer, unmodified
    UNLICENSE           jsfxr's licence
  templates/            one .wav per preset (see Templates)
  scripts/
    make-templates.mjs  regenerates templates/ (maintainer tool, Node)
  localization/en.json
  docs/spec.md
```

## jsfxr

- Vendor **jsfxr 1.4.1** from npm: `sfxr.js`, `riffwave.js` and `UNLICENSE`, byte-for-byte. jsfxr is public domain (Unlicense). The vendored version is recorded here, so the library files stay unmodified.
- Load both as classic `<script>` tags (`riffwave.js` first), which define `window.jsfxr` and `window.RIFFWAVE`. jsfxr's ESM wrapper (`sfxr.mjs`) imports a UMD file as a module, which only works through a bundler, so it isn't used.
- The APIs we rely on:
  - `new jsfxr.Params()`, its preset methods (`pickupCoin`, `laserShoot`, `explosion`, `powerUp`, `hitHurt`, `jump`, `blipSelect`, `random`), `mutate()` and `fromJSON()`.
  - `jsfxr.sfxr.toWave(params)`, which returns a `RIFFWAVE` whose `.wav` is the finished file as a byte array.
  - `jsfxr.convert.units` for human-readable slider values (e.g. `"0.5808Hz"`), and `jsfxr.parameters.order` / `.signed` for the parameter list and which parameters are signed.
- Every sound is rendered at **44,100 Hz, 16-bit, mono** (`sample_rate = 44100`, `sample_size = 16`). jsfxr's own default is 8-bit, which is audibly noisier.

## The file

A Sound FX file is a standard PCM WAV with one extra chunk:

```
RIFF  WAVE
  fmt   PCM, 1 channel, 44100 Hz, 16-bit
  data  the rendered samples
  sfxr  the synth settings, as UTF-8 JSON
```

- The `sfxr` chunk comes **after** `data`, so naive readers that expect audio data at byte 44 still work. The RIFF size field covers it, and it's padded to an even length as RIFF requires.
- Readers skip chunks they don't recognise. This was checked in the WebView: `decodeAudioData` (which Phaser uses) and an `<audio>` element both play a WAV carrying a trailing custom chunk normally.
- The chunk's JSON:

  ```json
  {
    "format": "celbridge-sound-fx",
    "version": 1,
    "generator": "jsfxr 1.4.1",
    "params": { "wave_type": 3, "p_env_attack": 0, "...": "...", "sound_vol": 0.25 }
  }
  ```

  `params` is jsfxr's own parameter object (`Params` fields), so it round-trips through `Params.fromJSON()` and could be pasted into sfxr.me's "serialize" box.
- **Sound FX sound:** a WAV with a valid `sfxr` chunk whose `format` and `version` the editor understands. **Plain WAV:** anything else, including a WAV whose `sfxr` chunk can't be parsed.
- A chunk with a newer `version` than the editor knows opens read-only with a notice, rather than being silently downgraded.

## Templates

One `[[templates]]` entry per preset, so **New File** offers them by name:

| Template | jsfxr preset | Default |
|---|---|---|
| Laser | `laserShoot` | |
| Explosion | `explosion` | |
| Coin | `pickupCoin` | ✓ |
| Power up | `powerUp` | |
| Hit | `hitHurt` | |
| Jump | `jump` | |
| Blip | `blipSelect` | |

- Each template is a complete Sound FX `.wav`, so a new file plays in a game immediately, before it's ever opened in the editor.
- The templates are committed. `scripts/make-templates.mjs` regenerates them from the vendored jsfxr with a fixed choice of settings; it's a maintainer tool, run with Node, and isn't needed to use the editor.

## Edit model

The document in memory is:

- **kind**: `sound-fx` or `plain`.
- **params**: the current jsfxr parameters (Sound FX sounds only).
- **original**: the file's bytes as loaded, kept so an unedited document is never rewritten.
- **history**: an undo/redo stack of `params` snapshots.

Rules:

- Any change to `params` (slider, wave type, preset, Mutate, Random, undo, redo) re-renders the sound with `sfxr.toWave`, redraws the waveform, and schedules a save.
- A slider drag is one undo step, recorded on release, not per pixel. Presets, Mutate and Random are one step each. New edits after an undo discard the redo tail.
- Undo and redo are wired to the host's edit verbs (the `input/performEdit` notification, with availability reported through `input.notifyEditAvailability`) and to Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z.
- History lives in memory only and is lost when the document closes. Source control is the way back after that.
- jsfxr's noise generator uses `Math.random()`, so re-rendering the same `params` gives slightly different noise. This is accepted: the sound is recognisably the same, it's only re-rendered after an edit, and the unedited file is never rewritten.

## User experience

The editor fills the tab. From top to bottom: a **toolbar**, the **waveform**, a **preset bar**, and the **settings**, which scroll if the tab is short.

### Toolbar

- **Play** (also Space, when focus isn't in a text field). Plays the current sound from the start, cutting off a sound already playing.
- **Play on change** toggle, on by default: every edit plays the result. Remembered per view through `onRequestState` / `onRestoreState`.
- **Undo / Redo.**
- Icon buttons with tooltips, Bootstrap Icons, as in the Image Editor.

### Waveform

- A wide canvas showing the rendered waveform (min/max per pixel column), with the duration in seconds and the file size.
- While the sound plays, a playhead sweeps across it.
- Drawn in theme colours, redrawn on resize and when the theme changes.

### Preset bar

- One button per preset (Laser, Explosion, Coin, Power up, Hit, Jump, Blip). Each click rolls a **new random variation** of that preset, as sfxr does, replacing the current settings.
- **Mutate**: a small random nudge to the current settings (`Params.mutate()`).
- **Random**: a completely random sound.
- Each click is one undo step, so a student can click Explosion five times and undo back to the one they liked.

### Settings

- **Wave type**: a segmented control with Square, Sawtooth, Sine, Noise.
- **Volume**: master volume (`sound_vol`).
- Every other parameter is a slider, grouped under headings the way sfxr.me groups them. There's enough room on a document surface to show them all, so nothing is hidden behind an "Advanced" toggle:

  | Group | Sliders (jsfxr parameter) |
  |---|---|
  | Envelope | Attack (`p_env_attack`), Sustain (`p_env_sustain`), Punch (`p_env_punch`), Decay (`p_env_decay`) |
  | Frequency | Start frequency (`p_base_freq`), Minimum frequency (`p_freq_limit`), Slide (`p_freq_ramp`), Delta slide (`p_freq_dramp`) |
  | Vibrato | Depth (`p_vib_strength`), Speed (`p_vib_speed`) |
  | Arpeggio | Frequency change (`p_arp_mod`), Change speed (`p_arp_speed`) |
  | Duty cycle | Duty (`p_duty`), Sweep (`p_duty_ramp`) |
  | Retrigger | Rate (`p_repeat_speed`) |
  | Flanger | Offset (`p_pha_offset`), Sweep (`p_pha_ramp`) |
  | Low-pass filter | Cutoff (`p_lpf_freq`), Cutoff sweep (`p_lpf_ramp`), Resonance (`p_lpf_resonance`) |
  | High-pass filter | Cutoff (`p_hpf_freq`), Cutoff sweep (`p_hpf_ramp`) |

- Signed parameters (`jsfxr.parameters.signed`) range from −1 to 1, with the centre marked; the rest range from 0 to 1. Double-clicking a slider resets it to its default.
- Each slider shows its value in real units from `jsfxr.convert.units` (Hz, seconds, %, dB), not the raw 0–1 number.
- Duty cycle only affects Square and Sawtooth waves. Its group is dimmed (still editable) for Sine and Noise, with a tooltip saying why.
- Groups flow into columns as the tab widens, and stack into one column when it's narrow.

### Plain WAV files

A WAV without Sound FX settings (a recording, a downloaded sound, a WAV from another tool):

- Opens with the waveform, Play, the duration and format, and a notice: *"This sound wasn't made with Sound FX, so it can't be edited here."*
- The settings and undo controls are hidden. The preset bar is replaced by **Start from a preset…**, which asks for confirmation ("This replaces the audio in *name*.wav") before turning the file into a Sound FX sound from the chosen preset.
- A WAV the WebView can't decode shows the notice and the reason, with nothing to play.
- The file is never written unless the user confirms a replacement.

### Read-only files

If `viewState.writable` isn't `"Writable"`, or the `sfxr` chunk is from a newer version, all editing controls are disabled and a notice says why. Play and the waveform still work.

## Saving

- There is no explicit save. Each change calls `document.notifyChanged`, and the host's auto-save calls `onRequestSave` about a second after the last change, and when the tab closes. The editor then saves the current WAV bytes (rendered audio plus the `sfxr` chunk) as base64 with `document.save`.
- If the user undoes back to the settings the file was opened with, the original bytes are saved, so the file ends byte-identical to how it started.
- **External changes** (`onExternalChange`): read the new content with `document.load`. If there are no unsaved edits, reload and clear history. If there are, the file on disk wins, as with every Celbridge editor: reload, and show a notice that the edits were replaced.
- If writing fails, show an error toast, keep the in-memory state, and retry on the next change.

## Meteoroids

Meteoroids is the first user of the editor. Its `sounds/` folder holds five sounds made in the editor: `shoot.wav` (from Laser), `bang.wav` (Explosion), `die.wav` (Hit, lengthened), `level-up.wav` (Power up) and `thrust.wav` (Explosion with a long sustain, low start frequency, no attack or decay, so it loops without an obvious click).

## Deferred

- Import and export of sfxr.me share links (jsfxr's `b58encode` / `b58decode`), so students can bring in a sound they found online.
- A seeded random number source, so noise renders identically every time.
- Sample-rate options (22,050 / 11,025 Hz) for an even crunchier sound and smaller files.
- A "loop" preview mode for looping sounds like thrust.
- A bfxr-style lock on individual sliders, so Mutate and Random leave them alone.

## Attribution

- jsfxr by Eric Fredricksen and Chris McCormick, a port of sfxr by Dr Petter (Tomas Pettersson). Public domain (Unlicense), shipped as `lib/UNLICENSE`.
- The toolbar's About button opens a popover crediting sfxr and jsfxr.
