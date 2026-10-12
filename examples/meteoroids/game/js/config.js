// Loads the gameplay settings from config/gameplay.csv over the defaults in
// settings.js. The CSV is made from config/gameplay.xlsx by
// config/export_config.py, so edit the spreadsheet, not the CSV.
//
// Each line is a setting name and a number. A name is lower case, with dots
// between levels and underscores between words, and maps onto the camelCase path
// in settings.js, so ship.turn_speed sets SETTINGS.ship.turnSpeed.

import { SETTINGS } from './settings.js';

const CONFIG_FILE = 'config/gameplay.csv';

export async function loadConfig() {
  let text;
  try {
    const response = await fetch(CONFIG_FILE, { cache: 'no-store' });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    text = await response.text();
  } catch (error) {
    // The game still runs without the file, using the defaults in settings.js.
    console.warn(`Could not load ${CONFIG_FILE} (${error.message}), so the game uses the ` +
      'defaults in settings.js. Run config/export_config.py in the Python console to make it.');
    return;
  }

  // Skip the header line and any blank lines.
  const lines = text.split(/\r?\n/).slice(1).filter((line) => line.trim() !== '');
  for (const line of lines) {
    const [name, value] = line.split(',').map((field) => field.trim());
    applySetting(name, Number(value));
  }
}

function applySetting(name, value) {
  const path = name.split('.').map(toCamelCase);
  const key = path.pop();
  let target = SETTINGS;
  for (const part of path) target = target?.[part];

  // Only numbers that settings.js already has, so a typo can't break the game.
  if (typeof target?.[key] !== 'number') {
    console.warn(`${CONFIG_FILE}: ignoring ${name}, which is not a setting in settings.js.`);
    return;
  }
  if (!Number.isFinite(value)) {
    console.warn(`${CONFIG_FILE}: ignoring ${name}, whose value is not a number.`);
    return;
  }
  target[key] = value;
}

// 'turn_speed' becomes 'turnSpeed'.
function toCamelCase(word) {
  return word.replace(/_([a-z])/g, (match, letter) => letter.toUpperCase());
}
