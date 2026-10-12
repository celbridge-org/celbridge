# Meteoroids

An Asteroids clone where everything on screen is an emoji. Fly a 🚀 around a wrapping screen, shoot 🪨 rocks that split into smaller rocks, and clear each wave without getting hit.

The game is plain JavaScript on [Phaser 4](https://phaser.io), with no build step. The project also includes the tools used to make the game's art and sound, as Celbridge packages you can open, use and change.

## Play

Open `game/index.html`. Celbridge shows it in the HTML editor, which previews the page. Click the game to give it keyboard focus, then press Space.

| Key            | Action                           |
|----------------|----------------------------------|
| ← / → or A / D | Rotate                           |
| ↑ or W         | Thrust                           |
| Space          | Fire, and start a new game       |

The preview doesn't reload when files change. After an edit, press F5 or the preview's Reload button.

## Project layout

```
game/
  index.html          loads Phaser, then js/main.js
  js/
    main.js           Phaser game config
    settings.js       every number worth tweaking
    assets.js         image and sound files, by key
    game-scene.js     the whole game, in one scene
  lib/phaser.min.js   Phaser 4.2.1, vendored so the game runs offline
  sprites/            PNGs made with the Emoji Sprite Generator
  sounds/             WAVs made with the Sound FX Editor
  package.toml        makes the folder a Celbridge package
  pages.toml          publishes the folder as a static page on the workshop
tools/
  emoji-sprite/       Emoji Sprite Generator
  image-editor/       Image Editor
  sound-fx/           Sound FX Editor
python.console        the Python console that opens on load
```

## How the game works

`index.html` loads Phaser with a classic `<script>` tag, which defines the global `Phaser`, and then loads `js/main.js` as an ES module. There's no bundler and no npm.

- **`main.js`** sets up a 960 × 720 game that scales to fit the panel, with Arcade Physics. Set `arcade.debug` to `true` to see the physics bodies.
- **`settings.js`** holds the speeds, sizes, scores, timings and wave sizes. Change a number and reload.
- **`assets.js`** maps short keys (`'ship'`, `'bang'`) to files. The rest of the code only uses the keys, so you can reskin the game by pointing a key at a different file.
- **`game-scene.js`** is the whole game: one `Phaser.Scene` with a `state` string (`ready`, `playing`, `respawning`, `gameover`). Starting a new game calls `scene.restart()`, so there's no reset code.

A few details worth knowing:

- Arcade Physics bodies are rectangles, so the overlap checks pass a `circlesTouch` callback that confirms each hit by distance. `HITBOX_SCALE` makes hitboxes a little smaller than the art, so near misses feel fair.
- Phaser measures angles from pointing right, but the 🚀 artwork points up and to the right. `SHIP_ART_ANGLE` (and `FLAME_ART_ANGLE` for the 🔥) corrects for this. If you swap in art that faces another way, change that number.
- Bullets come from a pooled group whose size is `MAX_BULLETS`, so firing does nothing while the pool is full.
- Every rock size plays the same `bang` sound at a different playback rate.
- A missing sound is skipped, and a missing image shows Phaser's placeholder, so the game still runs while you replace assets.

## The asset tools

The three tools in `tools/` are Celbridge packages, which load with the project. Each one is a plain HTML and JavaScript page, so they're also worked examples of building your own Celbridge tools.

### Emoji Sprite Generator

This tool turns emoji into PNG sprites. Open it from its button (a smiley face) on the left rail.

A **sprite set** lists the emoji to render, the resolution, and the output folder. The `meteoroids` set renders 🚀🔥🪨🟡💥❤️ at 128 × 128 into `game/sprites`. **Generate sprites** downloads the [Noto Color Emoji](https://github.com/googlefonts/noto-emoji) artwork, so it needs an internet connection. The game itself runs offline.

Each file is named after its emoji (🚀 becomes `rocket.png`). Names come from Unicode's emoji annotations, so check what was written and add each new file to `IMAGES` in `assets.js`.

### Image Editor

This tool edits PNG, JPEG, WebP, GIF and BMP files, and opens ICO files read-only. Its toolbar crops, resizes, rotates and flips the image, with undo and redo. For pixel art, choose **Pixelated** resampling when resizing. Each edit is saved straight back to the file, in its original format.

### Sound FX Editor

This tool opens `.wav` files and synthesises retro sound effects with [jsfxr](https://github.com/chr15m/jsfxr). Open one of the files in `game/sounds/` to hear it, then shape it:

- **Presets** (Laser, Explosion, Coin and so on) make a new random sound of that kind.
- **Mutate** nudges every setting a little, and **Random** makes something new.
- The sliders control the envelope, frequency, filters and other settings. Double-click a slider to reset it.
- Space plays the sound.

Every change is saved into the `.wav`, so reloading the game plays the new version. The synth settings are stored inside the file, so you can reopen a sound and keep tweaking it. A WAV made elsewhere opens read-only.

To add a sound, use **New File** in the Explorer and choose one of the Sound FX presets as the starting point. Add the file to `SOUNDS` in `assets.js`, and play it with `this.playSound('key')`.

## Things to try

- Reskin the game: 🐝 in a field of 🌸, 🐟 dodging 🦈, or a 🧙 casting ✨ at 👻. Edit the sprite set, generate the sprites, and update `assets.js`.
- Add a 🛸 that crosses the screen and shoots back.
- Add hyperspace on a key press, with the risk of landing on a rock.
- Award an extra life every 10,000 points.
- Save a high score in `localStorage`.
- Have rocks drop power-ups, such as a 🛡️ shield or ⚡ rapid fire.
- Shake the screen when the ship dies (`cameras.main.shake`).
- Add background music, or make new sounds in the Sound FX Editor.
- Add touch controls for phones.

## Credits

- **Emoji artwork** in `game/sprites/` is from [Noto Emoji](https://github.com/googlefonts/noto-emoji) by Google, licensed under the Apache License 2.0 (see `game/sprites/LICENSE-noto-emoji.txt`). It was generated with the Emoji Sprite Generator. If you ship generated sprites somewhere else, take the license file with them.
- **[Phaser](https://phaser.io)** (`game/lib/phaser.min.js`) is by Richard Davey and Phaser Studio Inc., licensed under the MIT License (see `game/lib/phaser-LICENSE.md`).
- **Sound effects** in `game/sounds/` are made with the Sound FX Editor, which uses [jsfxr](https://github.com/chr15m/jsfxr) by Eric Fredricksen and Chris McCormick. jsfxr is a port of sfxr by Dr Petter (Tomas Pettersson), and is public domain (Unlicense).
