// Starts Phaser. `Phaser` is a global, defined by lib/phaser.min.js which index.html
// loads before this file.

import { SETTINGS } from './settings.js';
import { GameScene } from './game-scene.js';

new Phaser.Game({
  type: Phaser.AUTO,
  width: SETTINGS.WIDTH,
  height: SETTINGS.HEIGHT,
  backgroundColor: '#05060a',
  // Scale the canvas to fit the window, keeping its shape.
  scale: {
    mode: Phaser.Scale.FIT,
    autoCenter: Phaser.Scale.CENTER_BOTH,
  },
  // Arcade Physics moves everything. Set debug to true to see the physics bodies.
  physics: {
    default: 'arcade',
    arcade: { debug: false },
  },
  scene: GameScene,
});
