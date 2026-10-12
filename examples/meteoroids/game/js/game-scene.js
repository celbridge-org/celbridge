// The whole game: one Phaser scene.
//
// The scene is always in one of four states:
//   'ready'      title screen, rocks drifting, waiting for Space
//   'playing'    flying and shooting
//   'respawning' the ship blew up and is waiting to come back
//   'gameover'   out of lives, waiting for Space
//
// Pressing Space on the title or game-over screen restarts the scene, so a new game
// always starts from a clean slate.

import { SETTINGS } from './settings.js';
import { IMAGES, SOUNDS } from './assets.js';

const S = SETTINGS;
const HUD_FONT = { fontFamily: 'ui-monospace, Consolas, monospace', fontSize: '28px', color: '#ffffff' };

export class GameScene extends Phaser.Scene {
  constructor() {
    super('game');
  }

  // Called before create(), with whatever was passed to scene.restart().
  init(data) {
    this.startPlaying = data.startPlaying ?? false;
  }

  preload() {
    for (const [key, file] of Object.entries(IMAGES)) this.load.image(key, file);
    for (const [key, file] of Object.entries(SOUNDS)) this.load.audio(key, file);

    this.load.on('loaderror', (file) => {
      console.warn(`Could not load ${file.src}. Sprites come from the Emoji Sprite utility; ` +
        'sounds are made with the Sound FX Editor.');
    });
  }

  create() {
    this.score = 0;
    this.lives = S.START_LIVES;
    this.wave = 0;
    this.heading = -90;           // the direction the ship faces; starts pointing up
    this.invulnerableUntil = 0;
    this.waveClearing = false;

    // Physics groups. The bullet group is a pool: get() reuses a spent bullet, or
    // returns null once MAX_BULLETS are already flying.
    this.rocks = this.physics.add.group();
    this.bullets = this.physics.add.group({ maxSize: S.MAX_BULLETS });

    // The flame is created before the ship so it is drawn underneath it.
    this.flame = this.add.image(0, 0, 'flame').setVisible(false);
    this.ship = this.physics.add.sprite(S.WIDTH / 2, S.HEIGHT / 2, 'ship');
    this.ship.setDisplaySize(S.SHIP_SIZE, S.SHIP_SIZE);
    this.ship.setDrag(S.SHIP_DRAG);
    this.ship.body.setMaxSpeed(S.SHIP_MAX_SPEED);

    if (this.cache.audio.exists('thrust')) {
      this.thrustSound = this.sound.add('thrust', { loop: true, volume: 0.5 });
      // Sounds outlive the scene, so stop the loop when the scene restarts.
      this.events.once('shutdown', () => this.thrustSound.destroy());
    }

    // Arcade Physics checks for overlaps every frame. Its bodies are rectangles, so
    // circlesTouch() double-checks with circles before the hit counts.
    this.physics.add.overlap(this.bullets, this.rocks, this.bulletHitRock, this.circlesTouch, this);
    this.physics.add.overlap(this.ship, this.rocks, this.shipHitRock, this.circlesTouch, this);

    this.cursors = this.input.keyboard.createCursorKeys();
    this.wasd = this.input.keyboard.addKeys('W,A,D');

    this.createHud();

    if (this.startPlaying) {
      this.state = 'playing';
      this.startWave();
    } else {
      this.state = 'ready';
      this.ship.disableBody(true, true);
      this.spawnLargeRocks(S.START_ROCKS);
      this.showMessage('METEOROIDS\n\npress SPACE to start');
    }
  }

  update(time, delta) {
    // Anything that flies off one edge comes back on the opposite edge.
    this.physics.world.wrap(this.ship, S.SHIP_SIZE / 2);
    this.physics.world.wrap(this.rocks, S.ROCKS.large.size / 2);
    this.physics.world.wrap(this.bullets, S.BULLET_SIZE);

    this.expireBullets(time);

    if (this.state === 'ready' || this.state === 'gameover') {
      if (Phaser.Input.Keyboard.JustDown(this.cursors.space)) {
        this.scene.restart({ startPlaying: true });
      }
      return;
    }

    if (this.state === 'playing') this.updateShip(delta / 1000);

    if (!this.waveClearing && this.rocks.countActive() === 0) {
      this.waveClearing = true;
      this.playSound('level-up');
      this.time.delayedCall(S.WAVE_DELAY, () => this.startWave());
    }
  }

  // ---------------------------------------------------------------- ship

  updateShip(seconds) {
    const left = this.cursors.left.isDown || this.wasd.A.isDown;
    const right = this.cursors.right.isDown || this.wasd.D.isDown;
    const thrust = this.cursors.up.isDown || this.wasd.W.isDown;

    if (left) this.heading -= S.SHIP_TURN_SPEED * seconds;
    if (right) this.heading += S.SHIP_TURN_SPEED * seconds;

    // The artwork doesn't face right, so turn the sprite to make up the difference.
    this.ship.angle = this.heading - S.SHIP_ART_ANGLE;

    if (thrust) {
      // Push in the direction the ship is facing.
      this.physics.velocityFromAngle(this.heading, S.SHIP_THRUST, this.ship.body.acceleration);
    } else {
      this.ship.setAcceleration(0);
    }

    this.updateFlame(thrust);

    if (Phaser.Input.Keyboard.JustDown(this.cursors.space)) this.fireBullet();
  }

  // The 🔥 sits behind the ship, pointing backwards, and flickers in length.
  updateFlame(thrusting) {
    this.flame.setVisible(thrusting);

    if (thrusting) {
      const behind = this.physics.velocityFromAngle(this.heading + 180, S.FLAME_OFFSET);
      this.flame.setPosition(this.ship.x + behind.x, this.ship.y + behind.y);
      this.flame.angle = this.heading + 180 - S.FLAME_ART_ANGLE;
      this.flame.setDisplaySize(S.FLAME_SIZE, S.FLAME_SIZE * Phaser.Math.FloatBetween(0.8, 1.2));
    }

    if (!this.thrustSound) return;
    if (thrusting && !this.thrustSound.isPlaying) this.thrustSound.play();
    if (!thrusting && this.thrustSound.isPlaying) this.thrustSound.stop();
  }

  fireBullet() {
    const nose = this.physics.velocityFromAngle(this.heading, S.SHIP_SIZE / 2);
    const x = this.ship.x + nose.x;
    const y = this.ship.y + nose.y;

    const bullet = this.bullets.get(x, y, 'bullet');
    if (!bullet) return; // too many bullets already flying

    bullet.enableBody(true, x, y, true, true);
    bullet.setDisplaySize(S.BULLET_SIZE, S.BULLET_SIZE);
    // Bullet speed, plus the ship's own speed so you can't outrun your bullets.
    this.physics.velocityFromAngle(this.heading, S.BULLET_SPEED, bullet.body.velocity);
    bullet.body.velocity.add(this.ship.body.velocity);
    bullet.setData('expiresAt', this.time.now + S.BULLET_LIFETIME);

    this.playSound('shoot');
  }

  expireBullets(time) {
    for (const bullet of this.bullets.getChildren()) {
      if (bullet.active && time > bullet.getData('expiresAt')) bullet.disableBody(true, true);
    }
  }

  shipHitRock(ship, rock) {
    if (this.state !== 'playing' || this.time.now < this.invulnerableUntil) return;
    if (!rock.active) return;

    this.breakRock(rock);
    this.playSound('die');
    this.showExplosion(ship.x, ship.y, S.SHIP_SIZE * 2.5);

    ship.disableBody(true, true);
    this.updateFlame(false);
    this.lives -= 1;
    this.updateLives();

    this.state = 'respawning';
    this.time.delayedCall(S.RESPAWN_DELAY, () => {
      if (this.lives > 0) this.respawnShip();
      else this.gameOver();
    });
  }

  respawnShip() {
    this.heading = -90;
    this.ship.enableBody(true, S.WIDTH / 2, S.HEIGHT / 2, true, true);
    this.ship.setVelocity(0);
    this.ship.setAcceleration(0);
    this.ship.angle = this.heading - S.SHIP_ART_ANGLE;

    // Blink while invulnerable, so it's obvious rocks can't hurt you yet.
    this.invulnerableUntil = this.time.now + S.INVULNERABLE_TIME;
    this.tweens.add({
      targets: this.ship,
      alpha: 0.2,
      duration: 100,
      yoyo: true,
      repeat: S.INVULNERABLE_TIME / 200 - 1,
      onComplete: () => this.ship.setAlpha(1),
    });

    this.state = 'playing';
  }

  gameOver() {
    this.state = 'gameover';
    this.showMessage('GAME OVER\n\npress SPACE to play again');
  }

  // ---------------------------------------------------------------- rocks

  startWave() {
    this.wave += 1;
    this.waveClearing = false;
    this.waveText.setText(`WAVE ${this.wave}`);
    this.spawnLargeRocks(Math.min(S.START_ROCKS + this.wave - 1, S.MAX_ROCKS));
  }

  // New rocks appear along the screen edges, but never right on top of the ship.
  spawnLargeRocks(count) {
    for (let i = 0; i < count; i++) {
      let x, y;
      do {
        if (Math.random() < 0.5) {
          x = Phaser.Math.Between(0, S.WIDTH);
          y = 0;
        } else {
          x = 0;
          y = Phaser.Math.Between(0, S.HEIGHT);
        }
      } while (Phaser.Math.Distance.Between(x, y, this.ship.x, this.ship.y) < S.SAFE_RADIUS);

      this.spawnRock(x, y, 'large');
    }
  }

  spawnRock(x, y, sizeName) {
    const type = S.ROCKS[sizeName];
    const rock = this.rocks.create(x, y, 'rock');
    rock.setDisplaySize(type.size, type.size);
    rock.setData('size', sizeName);

    // Fly off in a random direction, spinning slowly.
    const direction = Phaser.Math.Between(0, 359);
    const speed = Phaser.Math.Between(type.minSpeed, type.maxSpeed);
    this.physics.velocityFromAngle(direction, speed, rock.body.velocity);
    rock.setAngularVelocity(Phaser.Math.Between(-S.ROCK_SPIN, S.ROCK_SPIN));
  }

  bulletHitRock(bullet, rock) {
    // Two bullets can hit the same rock in one frame; only the first one counts.
    if (!bullet.active || !rock.active) return;

    bullet.disableBody(true, true);
    this.breakRock(rock);
  }

  breakRock(rock) {
    const type = S.ROCKS[rock.getData('size')];

    this.addScore(type.score);
    this.playSound('bang', { rate: type.soundRate });
    this.showExplosion(rock.x, rock.y, type.size);

    if (type.splitsInto) {
      this.spawnRock(rock.x, rock.y, type.splitsInto);
      this.spawnRock(rock.x, rock.y, type.splitsInto);
    }
    rock.destroy();
  }

  // Overlap check: do the two objects' circles touch?
  circlesTouch(a, b) {
    const distance = Phaser.Math.Distance.Between(a.x, a.y, b.x, b.y);
    return distance < (a.displayWidth + b.displayWidth) / 2 * S.HITBOX_SCALE;
  }

  // ---------------------------------------------------------------- effects

  // A 💥 that grows and fades away, then removes itself.
  showExplosion(x, y, size) {
    const boom = this.add.image(x, y, 'explosion').setDisplaySize(size, size);
    this.tweens.add({
      targets: boom,
      scale: boom.scale * 1.8,
      alpha: 0,
      duration: 400,
      onComplete: () => boom.destroy(),
    });
  }

  // Plays a sound if it has been generated, so a missing file doesn't crash the game.
  playSound(key, config) {
    if (this.cache.audio.exists(key)) this.sound.play(key, config);
  }

  // ---------------------------------------------------------------- HUD

  createHud() {
    this.scoreText = this.add.text(S.WIDTH - 16, 12, '0', HUD_FONT).setOrigin(1, 0).setDepth(10);
    this.waveText = this.add.text(S.WIDTH / 2, 12, '', HUD_FONT).setOrigin(0.5, 0).setDepth(10);
    this.message = this.add.text(S.WIDTH / 2, S.HEIGHT / 2, '', { ...HUD_FONT, align: 'center' })
      .setOrigin(0.5)
      .setDepth(10);

    // One ❤️ per life; lost lives are hidden.
    this.lifeIcons = [];
    for (let i = 0; i < S.START_LIVES; i++) {
      const icon = this.add.image(28 + i * 36, 30, 'life').setDisplaySize(28, 28).setDepth(10);
      this.lifeIcons.push(icon);
    }
    this.updateLives();
  }

  updateLives() {
    this.lifeIcons.forEach((icon, i) => icon.setVisible(i < this.lives));
  }

  addScore(points) {
    this.score += points;
    this.scoreText.setText(String(this.score));
  }

  showMessage(text) {
    this.message.setText(text);
  }
}
