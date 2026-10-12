// Every number you might want to tweak lives here. Change one, reload the page, and
// see what happens.
//
// The main gameplay numbers are set from config/gameplay.xlsx instead, which
// overrides the values below when the game starts (see config.js). The values here
// are the defaults the game falls back on without config/gameplay.csv. A setting's
// config name is its path here in lower case, with underscores between words, so
// ship.turn_speed sets SETTINGS.ship.turnSpeed.
//
// Angles are in degrees, the way Phaser measures them: 0 points right, 90 points
// down, -90 points up. Speeds are in pixels per second.

export const SETTINGS = {
  // The play area. The canvas scales to fit the window, but the game always
  // thinks it is this big.
  screen: {
    width: 960,
    height: 720,
  },

  ship: {
    size: 48,               // drawn size in pixels
    artAngle: -45,          // which way the emoji artwork faces (🚀 points up-right)
    turnSpeed: 270,         // degrees per second
    thrust: 300,            // acceleration while thrusting, pixels per second²
    drag: 60,               // slowdown while not thrusting, pixels per second²
    maxSpeed: 400,
  },

  // Thrust flame, drawn behind the ship
  flame: {
    size: 28,
    artAngle: -90,          // 🔥 points up
    offset: 28,             // distance behind the ship's centre
  },

  bullet: {
    size: 12,
    speed: 600,
    lifetime: 1000,         // milliseconds
    maxCount: 4,            // on screen at once
  },

  // Each size splits into two of the next size down when shot.
  // soundRate plays the bang sound faster (higher) or slower (lower).
  rocks: {
    spin: 60,               // maximum spin, degrees per second
    large:  { size: 96, minSpeed: 30, maxSpeed: 70,  score: 20,  splitsInto: 'medium', soundRate: 0.7 },
    medium: { size: 56, minSpeed: 50, maxSpeed: 110, score: 50,  splitsInto: 'small',  soundRate: 1.0 },
    small:  { size: 32, minSpeed: 80, maxSpeed: 150, score: 100, splitsInto: null,     soundRate: 1.4 },
  },

  waves: {
    startRocks: 4,          // large rocks in wave 1; each wave adds one more
    maxRocks: 10,           // large rocks never exceed this
    safeRadius: 200,        // new rocks never appear this close to the ship
    delay: 2000,            // pause between waves, milliseconds
  },

  lives: {
    start: 3,
    respawnDelay: 1500,     // milliseconds before the ship comes back
    invulnerableTime: 2000, // milliseconds of safety after respawning
  },

  // Collisions count when circles overlap. Below 1 makes hitboxes a little smaller
  // than the emoji, so near misses feel fair.
  collision: {
    hitboxScale: 0.8,
  },
};
