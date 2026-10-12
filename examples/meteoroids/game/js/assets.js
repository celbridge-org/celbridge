// Every image and sound the game loads, by key. The rest of the code only ever uses
// the key ('ship', 'shoot'), so to reskin the game, point a key at a different file.
//
// Sprites come from the Emoji Sprite utility; sounds are made with the Sound FX Editor.
// Paths are relative to index.html.

export const IMAGES = {
  ship:      'sprites/rocket.png',
  flame:     'sprites/fire.png',
  rock:      'sprites/rock.png',
  bullet:    'sprites/yellow-circle.png',
  explosion: 'sprites/collision.png',
  life:      'sprites/red-heart.png',
};

export const SOUNDS = {
  shoot:     'sounds/shoot.wav',
  thrust:    'sounds/thrust.wav',
  bang:      'sounds/bang.wav',
  die:       'sounds/die.wav',
  'level-up': 'sounds/level-up.wav',
};
