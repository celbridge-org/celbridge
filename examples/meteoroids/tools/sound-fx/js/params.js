// The editor's view of jsfxr's parameters: the slider groups and labels, their ranges and unit formatting,
// and the preset, mutate and random rolls. jsfxr is loaded as a classic script that defines globalThis.jsfxr,
// in the editor page and in scripts/make-templates.mjs alike, so this module reads it from there.

export const GENERATOR = 'jsfxr 1.4.1';

// Every sound is rendered at 44.1 kHz, 16-bit, mono. jsfxr's own default is 8-bit, which is audibly noisier.
export const SAMPLE_RATE = 44100;
export const SAMPLE_SIZE = 16;

// sfxr.generate's volume, and the value the Volume slider resets to.
export const DEFAULT_VOLUME = 0.25;

// Each preset is a method on jsfxr.Params that rolls a random variation of its kind of sound.
export const PRESETS = [
    { id: 'laser', method: 'laserShoot', key: 'SFX_Preset_Laser' },
    { id: 'explosion', method: 'explosion', key: 'SFX_Preset_Explosion' },
    { id: 'coin', method: 'pickupCoin', key: 'SFX_Preset_Coin' },
    { id: 'power-up', method: 'powerUp', key: 'SFX_Preset_PowerUp' },
    { id: 'hit', method: 'hitHurt', key: 'SFX_Preset_Hit' },
    { id: 'jump', method: 'jump', key: 'SFX_Preset_Jump' },
    { id: 'blip', method: 'blipSelect', key: 'SFX_Preset_Blip' },
];

export const WAVE_TYPES = [
    { value: 0, key: 'SFX_Wave_Square' },
    { value: 1, key: 'SFX_Wave_Sawtooth' },
    { value: 2, key: 'SFX_Wave_Sine' },
    { value: 3, key: 'SFX_Wave_Noise' },
];

// Duty cycle only shapes these wave types.
export const DUTY_WAVE_TYPES = [0, 1];

// Grouped as sfxr.me groups them.
export const GROUPS = [
    {
        id: 'envelope', key: 'SFX_Group_Envelope', sliders: [
            { param: 'p_env_attack', key: 'SFX_Param_Attack' },
            { param: 'p_env_sustain', key: 'SFX_Param_Sustain' },
            { param: 'p_env_punch', key: 'SFX_Param_Punch' },
            { param: 'p_env_decay', key: 'SFX_Param_Decay' },
        ],
    },
    {
        id: 'frequency', key: 'SFX_Group_Frequency', sliders: [
            { param: 'p_base_freq', key: 'SFX_Param_StartFrequency' },
            { param: 'p_freq_limit', key: 'SFX_Param_MinFrequency' },
            { param: 'p_freq_ramp', key: 'SFX_Param_Slide' },
            { param: 'p_freq_dramp', key: 'SFX_Param_DeltaSlide' },
        ],
    },
    {
        id: 'vibrato', key: 'SFX_Group_Vibrato', sliders: [
            { param: 'p_vib_strength', key: 'SFX_Param_Depth' },
            { param: 'p_vib_speed', key: 'SFX_Param_Speed' },
        ],
    },
    {
        id: 'arpeggio', key: 'SFX_Group_Arpeggio', sliders: [
            { param: 'p_arp_mod', key: 'SFX_Param_FrequencyChange' },
            { param: 'p_arp_speed', key: 'SFX_Param_ChangeSpeed' },
        ],
    },
    {
        id: 'duty', key: 'SFX_Group_Duty', sliders: [
            { param: 'p_duty', key: 'SFX_Param_Duty' },
            { param: 'p_duty_ramp', key: 'SFX_Param_Sweep' },
        ],
    },
    {
        id: 'retrigger', key: 'SFX_Group_Retrigger', sliders: [
            { param: 'p_repeat_speed', key: 'SFX_Param_Rate' },
        ],
    },
    {
        id: 'flanger', key: 'SFX_Group_Flanger', sliders: [
            { param: 'p_pha_offset', key: 'SFX_Param_Offset' },
            { param: 'p_pha_ramp', key: 'SFX_Param_Sweep' },
        ],
    },
    {
        id: 'low-pass', key: 'SFX_Group_LowPass', sliders: [
            { param: 'p_lpf_freq', key: 'SFX_Param_Cutoff' },
            { param: 'p_lpf_ramp', key: 'SFX_Param_CutoffSweep' },
            { param: 'p_lpf_resonance', key: 'SFX_Param_Resonance' },
        ],
    },
    {
        id: 'high-pass', key: 'SFX_Group_HighPass', sliders: [
            { param: 'p_hpf_freq', key: 'SFX_Param_Cutoff' },
            { param: 'p_hpf_ramp', key: 'SFX_Param_CutoffSweep' },
        ],
    },
];

function jsfxr() {
    return globalThis.jsfxr;
}

export function isSigned(name) {
    return jsfxr().parameters.signed.includes(name);
}

export function rangeOf(name) {
    return isSigned(name) ? { min: -1, max: 1 } : { min: 0, max: 1 };
}

function clamp(value, min, max) {
    return Number.isFinite(value) ? Math.min(max, Math.max(min, value)) : min;
}

// A plain parameter object holding exactly the fields the sfxr chunk stores, each in its slider's range.
// jsfxr's rolls can step outside the range (Random and Mutate especially), and a value the slider cannot
// show would make the settings disagree with the sound, so everything is clamped here.
export function normalizeParams(source) {
    const defaults = new (jsfxr().Params)();
    const params = {};

    const waveType = Math.round(Number(source?.wave_type ?? defaults.wave_type));
    params.wave_type = clamp(waveType, 0, WAVE_TYPES.length - 1);

    for (const name of jsfxr().parameters.order) {
        if (name === 'wave_type') {
            continue;
        }
        const { min, max } = rangeOf(name);
        params[name] = clamp(Number(source?.[name] ?? defaults[name]), min, max);
    }

    params.sound_vol = clamp(Number(source?.sound_vol ?? DEFAULT_VOLUME), 0, 1);
    params.sample_rate = SAMPLE_RATE;
    params.sample_size = SAMPLE_SIZE;

    return params;
}

export function defaultValue(name) {
    if (name === 'sound_vol') {
        return DEFAULT_VOLUME;
    }

    return new (jsfxr().Params)()[name];
}

function toJsfxr(params) {
    return new (jsfxr().Params)().fromJSON(params);
}

// A new random variation of a preset. Like sfxr, the roll starts from default settings but keeps the
// master volume.
export function rollPreset(method, volume = DEFAULT_VOLUME) {
    const params = new (jsfxr().Params)();
    params[method]();
    params.sound_vol = volume;
    return normalizeParams(params);
}

export function rollRandom(volume = DEFAULT_VOLUME) {
    return rollPreset('random', volume);
}

export function mutate(params) {
    return normalizeParams(toJsfxr(params).mutate());
}

export function paramsEqual(a, b) {
    if (!a || !b) {
        return false;
    }

    return Object.keys(a).length === Object.keys(b).length && Object.keys(a).every((key) => a[key] === b[key]);
}

// Renders a sound. Returns jsfxr's RIFFWAVE: .wav is the file as a byte array, .buffer the samples as
// floats before quantisation.
export function render(params) {
    return jsfxr().sfxr.toWave(toJsfxr(params));
}

// A slider value in real units, via jsfxr's own conversions (slider -> internal -> units).
export function formatValue(name, value) {
    const convert = jsfxr().convert;
    const text = convert.units[name](convert.sliders[name](value));
    return String(text).replace('&plusmn;', '±').replace('Infinity', '∞');
}
