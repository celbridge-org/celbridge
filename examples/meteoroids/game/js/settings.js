// Every number you might want to tweak lives here. Change one, reload the page, and
// see what happens.
//
// Angles are in degrees, the way Phaser measures them: 0 points right, 90 points
// down, -90 points up. Speeds are in pixels per second.

export const SETTINGS = {
  // The play area. The canvas scales to fit the window, but the game always
  // thinks it is this big.
  WIDTH: 960,
  HEIGHT: 720,

  // Ship
  SHIP_SIZE: 48,            // drawn size in pixels
  SHIP_ART_ANGLE: -45,      // which way the emoji artwork faces (🚀 points up-right)
  SHIP_TURN_SPEED: 270,     // degrees per second
  SHIP_THRUST: 300,         // acceleration while thrusting, pixels per second²
  SHIP_DRAG: 60,            // slowdown while not thrusting, pixels per second²
  SHIP_MAX_SPEED: 400,

  // Thrust flame, drawn behind the ship
  FLAME_SIZE: 28,
  FLAME_ART_ANGLE: -90,     // 🔥 points up
  FLAME_OFFSET: 28,         // distance behind the ship's centre

  // Bullets
  BULLET_SIZE: 12,
  BULLET_SPEED: 600,
  BULLET_LIFETIME: 1000,    // milliseconds
  MAX_BULLETS: 4,           // on screen at once

  // Rocks. Each size splits into two of the next size down when shot.
  // soundRate plays the bang sound faster (higher) or slower (lower).
  ROCKS: {
    large:  { size: 96, minSpeed: 30, maxSpeed: 70,  score: 20,  splitsInto: 'medium', soundRate: 0.7 },
    medium: { size: 56, minSpeed: 50, maxSpeed: 110, score: 50,  splitsInto: 'small',  soundRate: 1.0 },
    small:  { size: 32, minSpeed: 80, maxSpeed: 150, score: 100, splitsInto: null,     soundRate: 1.4 },
  },
  ROCK_SPIN: 60,            // maximum spin, degrees per second

  // Waves
  START_ROCKS: 4,           // large rocks in wave 1; each wave adds one more
  MAX_ROCKS: 10,            // large rocks never exceed this
  SAFE_RADIUS: 200,         // new rocks never appear this close to the ship
  WAVE_DELAY: 2000,         // pause between waves, milliseconds

  // Lives
  START_LIVES: 3,
  RESPAWN_DELAY: 1500,      // milliseconds before the ship comes back
  INVULNERABLE_TIME: 2000,  // milliseconds of safety after respawning

  // Collisions count when circles overlap. Below 1 makes hitboxes a little smaller
  // than the emoji, so near misses feel fair.
  HITBOX_SCALE: 0.8,
};
