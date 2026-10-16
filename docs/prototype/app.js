/* DMX Pro — UI prototype logic.
   Plain JS, no dependencies. The data model mirrors DmxPro.Core (FixtureProfile, Fixture, FixtureGroup, …)
   and a tiny "engine" computes DMX values so every screen reacts like the real app will. */
'use strict';

/* ============================================================== Icons
   Keys are WPF-UI SymbolRegular names, so XAML uses <ui:SymbolIcon Symbol="Key" />. */
const ICONS = {
  DocumentAdd24: 'M6 3h8l4 4v6M14 3v4h4M6 3v18h7M17 15v6M14 18h6',
  FolderOpen24: 'M3 7V5h6l2 2h8v3M3 7v12h15l3-9H6l-3 9',
  Save24: 'M5 4h11l3 3v13H5zM8 4v5h7V4M8 20v-6h8v6',
  Search24: 'M10.5 4a6.5 6.5 0 1 0 0 13 6.5 6.5 0 0 0 0-13zM15.5 15.5 20 20',
  Info24: 'M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18zM12 11v6M12 7.5v.5',
  Lightbulb24: 'M9 18h6M10 21h4M12 3a6 6 0 0 0-3.5 10.9c.6.5 1 1.2 1 2.1h5c0-.9.4-1.6 1-2.1A6 6 0 0 0 12 3z',
  Library24: 'M4 4h3v16H4zM9 4h3v16H9zM14 5l3-1 4 15-3 1z',
  Flash24: 'M13 2 5 13h6l-1 9 8-11h-6z',
  Color24: 'M12 3a9 9 0 0 0 0 18c1.1 0 1.5-.8 1.5-1.5 0-1.2-1-1.4-1-2.5s.9-1.5 2-1.5H17a4 4 0 0 0 4-4c0-4.7-4-8.5-9-8.5zM7.5 11.5h.01M10 7.5h.01M15 7.5h.01',
  Board24: 'M3 4h18v16H3zM7 8v8M12 8v8M17 8v8M5.5 12h3M10.5 10h3M15.5 14h3',
  Timeline24: 'M3 6h8M3 12h14M3 18h10M14 4v4M19 10v4M15 16v4',
  Beaker24: 'M9 3h6M10 3v6l-5 9a2 2 0 0 0 1.8 3h10.4a2 2 0 0 0 1.8-3l-5-9V3M7 15h10',
  PlugConnected24: 'M9 3v4M15 3v4M7 7h10v4a5 5 0 0 1-10 0zM12 16v5',
  Settings24: 'M12 9a3 3 0 1 0 0 6 3 3 0 0 0 0-6zM19.4 13a7.5 7.5 0 0 0 0-2l2-1.5-2-3.4-2.3 1a7.5 7.5 0 0 0-1.7-1L15 3.5h-4l-.4 2.6a7.5 7.5 0 0 0-1.7 1l-2.3-1-2 3.4 2 1.5a7.5 7.5 0 0 0 0 2l-2 1.5 2 3.4 2.3-1a7.5 7.5 0 0 0 1.7 1l.4 2.6h4l.4-2.6a7.5 7.5 0 0 0 1.7-1l2.3 1 2-3.4z',
  Add24: 'M12 5v14M5 12h14',
  Delete24: 'M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3',
  Copy24: 'M8 8h11v13H8zM5 16V3h11',
  Edit24: 'M4 20h4L19 9l-4-4L4 16zM14 6l4 4',
  ArrowImport24: 'M4 4v16M20 12H9M13 8l-4 4 4 4',
  ArrowUndo24: 'M9 14 4 9l5-5M4 9h11a5 5 0 0 1 0 10h-3',
  CheckmarkCircle24: 'M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18zM8 12l3 3 5-6',
  Warning24: 'M12 3 2 20h20zM12 10v5M12 17.5v.5',
  LockClosed24: 'M6 10h12v10H6zM8 10V7a4 4 0 0 1 8 0v3M12 14v2',
  Play24: 'M7 4v16l13-8z',
  Pause24: 'M7 4h3v16H7zM14 4h3v16h-3z',
  Stop24: 'M6 6h12v12H6z',
  MusicNote224: 'M9 18V5l11-2v13M9 18a3 3 0 1 1-6 0 3 3 0 0 1 6 0zM20 16a3 3 0 1 1-6 0 3 3 0 0 1 6 0z',
  Circle24: 'M12 4a8 8 0 1 0 0 16 8 8 0 0 0 0-16z',
  ArrowRepeatAll24: 'M17 2l3 3-3 3M4 11V9a4 4 0 0 1 4-4h12M7 22l-3-3 3-3M20 13v2a4 4 0 0 1-4 4H4',
  DismissCircle24: 'M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18zM9 9l6 6M15 9l-6 6',
  RectangleLandscape24: 'M3 6h18v12H3z',
  Options24: 'M4 7h10M18 7h2M4 17h2M10 17h10M16 5v4M8 15v4',
  TopSpeed24: 'M4 18a8 8 0 1 1 16 0M12 18l4-6',
  TextT24: 'M5 5h14M12 5v15',
  Group24: 'M8 11a3 3 0 1 0 0-6 3 3 0 0 0 0 6zM16 11a3 3 0 1 0 0-6 3 3 0 0 0 0 6zM2 20c0-3 3-5 6-5s6 2 6 5M14 15c3 0 8 1 8 5',
  ChevronDown24: 'M6 9l6 6 6-6',
  Wand24: 'M4 20 15 9M14 4v2M19 9h2M18 5l-1 1M18 13l-1-1',
  ArrowSync24: 'M20 12a8 8 0 0 1-14.3 4.9M4 12A8 8 0 0 1 18.3 7.1M18 3v4h-4M6 21v-4h4',
};

function icon(name, size = 16) {
  const d = ICONS[name] || ICONS.Circle24;
  return `<svg width="${size}" height="${size}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="${d}"/></svg>`;
}
function hydrateIcons(root = document) {
  root.querySelectorAll('i[data-symbol]').forEach(i => {
    if (i.dataset.done) return;
    i.innerHTML = icon(i.dataset.symbol, +(i.dataset.size || 16));
    i.style.display = 'inline-flex';
    i.dataset.done = '1';
  });
}

/* ============================================================== Helpers */
const $ = (s, r = document) => r.querySelector(s);
const $$ = (s, r = document) => [...r.querySelectorAll(s)];
const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
let idSeq = 100;
const newId = p => `${p}${++idSeq}`;
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
const hexToRgb = h => { const n = parseInt(h.slice(1), 16); return [(n >> 16) & 255, (n >> 8) & 255, n & 255]; };
const rgbToHex = (r, g, b) => '#' + [r, g, b].map(v => Math.round(clamp(v, 0, 255)).toString(16).padStart(2, '0')).join('');
const fmtTime = s => { const m = Math.floor(s / 60), r = s - m * 60; return `${String(m).padStart(2, '0')}:${r.toFixed(3).padStart(6, '0')}`; };
const CH_COLORS = { Red: '#ff4040', Green: '#40e060', Blue: '#4080ff', White: '#f0f0f0', Amber: '#ffb000', UV: '#9a4dff', Dimmer: '#bbbbbb', Strobe: '#fce100', ColorMacro: '#ff66cc', Speed: '#60cdff', Program: '#c0a0ff', Mode: '#a0a0a0', Generic: '#666', Fine: '#888' };
const INTENSITY = new Set(['Dimmer', 'Red', 'Green', 'Blue', 'White', 'Amber', 'UV']);

/* ============================================================== Data (mirrors Core) */
const PROFILES = [
  {
    id: 'generic-rgb', manufacturer: 'Generic', model: 'RGB fixture', type: 'Color Changer', verified: false,
    source: 'Generic template. Confirm channel order against your fixture\'s DMX chart.',
    channels: [
      { key: 'dimmer', name: 'Dimmer', type: 'Dimmer', def: 255 },
      { key: 'red', name: 'Red', type: 'Red', def: 0 }, { key: 'green', name: 'Green', type: 'Green', def: 0 }, { key: 'blue', name: 'Blue', type: 'Blue', def: 0 }],
    modes: [{ name: '3ch RGB', channels: ['red', 'green', 'blue'] }, { name: '4ch Dimmer RGB', channels: ['dimmer', 'red', 'green', 'blue'] }],
  },
  {
    id: 'generic-rgbw', manufacturer: 'Generic', model: 'RGBW fixture', type: 'Color Changer', verified: false,
    source: 'Generic template (8ch = illustrative spec layout, NOT universal).',
    channels: [
      { key: 'red', name: 'Red', type: 'Red', def: 0 }, { key: 'green', name: 'Green', type: 'Green', def: 0 },
      { key: 'blue', name: 'Blue', type: 'Blue', def: 0 }, { key: 'white', name: 'White', type: 'White', def: 0 },
      { key: 'dimmer', name: 'Master dimmer', type: 'Dimmer', def: 255 },
      { key: 'strobe', name: 'Strobe', type: 'Strobe', def: 0, caps: [{ min: 0, max: 9, label: 'No strobe', safe: true }, { min: 10, max: 255, label: 'Strobe slow → fast' }] },
      { key: 'macro', name: 'Color macro', type: 'ColorMacro', def: 0, caps: [{ min: 0, max: 9, label: 'Off (RGBW active)', safe: true }, { min: 10, max: 255, label: 'Built-in programs' }] },
      { key: 'speed', name: 'Speed', type: 'Speed', def: 0 }],
    modes: [{ name: '4ch RGBW', channels: ['red', 'green', 'blue', 'white'] }, { name: '5ch Dimmer RGBW', channels: ['dimmer', 'red', 'green', 'blue', 'white'] },
      { name: '8ch Full', channels: ['red', 'green', 'blue', 'white', 'dimmer', 'strobe', 'macro', 'speed'] }],
  },
  {
    id: 'stairville-led-bar-240-8', manufacturer: 'Stairville', model: 'LED Bar 240/8 RGB', type: 'LED Bar (Beams)', verified: true,
    source: 'Imported from QLC+ fixture library (Apache-2.0). Verified on the real fixture 2026-10-16.',
    channels: [
      { key: 'mode', name: 'Mode', type: 'Mode', def: 0, caps: [{ min: 0, max: 63, label: 'RGB control', safe: true }, { min: 64, max: 255, label: 'Auto programs' }] },
      { key: 'red', name: 'Red', type: 'Red', def: 0 }, { key: 'green', name: 'Green', type: 'Green', def: 0 }, { key: 'blue', name: 'Blue', type: 'Blue', def: 0 },
      { key: 'dimmer', name: 'Dimmer', type: 'Dimmer', def: 255 }],
    modes: [{ name: '5 Channel', channels: ['mode', 'red', 'green', 'blue', 'dimmer'] }],
  },
  {
    id: 'cheap-par-7ch', manufacturer: 'Unknown', model: 'PAR 18x1W (user)', type: 'Color Changer', verified: false,
    source: 'User-defined. Channel 6–7 meaning unknown — test before use.',
    channels: [
      { key: 'dimmer', name: 'Dimmer', type: 'Dimmer', def: 255 }, { key: 'red', name: 'Red', type: 'Red', def: 0 },
      { key: 'green', name: 'Green', type: 'Green', def: 0 }, { key: 'blue', name: 'Blue', type: 'Blue', def: 0 },
      { key: 'strobe', name: 'Strobe', type: 'Strobe', def: 0 }, { key: 'ch6', name: 'Channel 6', type: 'Generic', def: 0 }, { key: 'ch7', name: 'Channel 7', type: 'Generic', def: 0 }],
    modes: [{ name: '7ch', channels: ['dimmer', 'red', 'green', 'blue', 'strobe', 'ch6', 'ch7'] }],
  },
];
const profileById = id => PROFILES.find(p => p.id === id);
const modeOf = f => profileById(f.profileId)?.modes.find(m => m.name === f.mode);
const channelsOf = f => { const p = profileById(f.profileId), m = modeOf(f); return m ? m.channels.map(k => p.channels.find(c => c.key === k)) : []; };
const footprint = f => (modeOf(f)?.channels.length) || 1;

const state = {
  project: { name: 'Wedding show', dirty: true },
  page: 'fixtures',
  fixtures: [
    { id: 'f1', name: 'Stage Left PAR', profileId: 'generic-rgbw', mode: '8ch Full', universe: 1, address: 1, enabled: true, notes: 'Front truss, left' },
    { id: 'f2', name: 'Stage Right PAR', profileId: 'generic-rgbw', mode: '8ch Full', universe: 1, address: 9, enabled: true, notes: '' },
    { id: 'f3', name: 'Center PAR', profileId: 'generic-rgbw', mode: '8ch Full', universe: 1, address: 17, enabled: true, notes: '' },
    { id: 'f4', name: 'Back Bar L', profileId: 'stairville-led-bar-240-8', mode: '5 Channel', universe: 1, address: 25, enabled: true, notes: '' },
    { id: 'f5', name: 'Back Bar R', profileId: 'stairville-led-bar-240-8', mode: '5 Channel', universe: 1, address: 28, enabled: true, notes: 'Overlap on purpose: shows warning' },
    { id: 'f6', name: 'DJ booth PAR', profileId: 'cheap-par-7ch', mode: '7ch', universe: 2, address: 1, enabled: true, notes: '' },
  ],
  groups: [
    { id: 'g1', name: 'Front', fixtureIds: ['f1', 'f2', 'f3'] },
    { id: 'g2', name: 'Back bars', fixtureIds: ['f4', 'f5'] },
  ],
  functions: [],
  selFixture: 'f1', selProfile: 'generic-rgbw', selProfileMode: '8ch Full', selFunction: null, selScene: null,
  live: {},            // fixtureId -> { Red:0..255, … } programmer values (scenes/console)
  test: null,          // { fixtureId, values } — Test page override
  running: new Map(),  // functionId -> start time (s)
  master: 1, blackout: false, bpm: 120, speed: 1,
  output: { enabled: false, selUniverse: 1, universes: [
    { u: 1, type: 'Art-Net', ip: '192.168.1.50', port: 0, adapter: 'Ethernet — 192.168.1.10' },
    { u: 2, type: 'Enttec DMX USB Pro', com: 'COM4' },
    { u: 3, type: 'None' }] },
  console: { pageId: 'p1', edit: false, locked: false, sel: null, pages: [] },
  show: { length: 211, bpm: 120, playing: false, t: 0, zoom: 60, sel: null, range: null, tracks: [] },
};

/* Scenes & functions (Function.type mirrors the planned Core function types) */
function scene(name, hex, targets, extra = {}) {
  const [r, g, b] = hexToRgb(hex);
  return { id: newId('s'), type: 'Scene', name, color: { r, g, b, w: extra.w || 0 }, bright: 1, targets, fadeIn: 0.5, fadeOut: 0.5, ...extra };
}
const ALL = ['f1', 'f2', 'f3', 'f4', 'f5', 'f6'];
const S = {
  blackout: scene('Blackout', '#000000', ALL),
  red: scene('Red', '#ff0000', ALL), green: scene('Green', '#00ff00', ALL), blue: scene('Blue', '#0000ff', ALL),
  white: scene('White', '#ffffff', ALL, { w: 255 }), cyan: scene('Cyan', '#00ffff', ALL), magenta: scene('Magenta', '#ff00ff', ALL),
  yellow: scene('Yellow', '#ffff00', ALL), warm: scene('Warm white', '#ff9a40', ['f1', 'f2', 'f3'], { w: 180 }), bluewash: scene('Blue wash', '#1a3cff', ALL),
};
state.functions.push(...Object.values(S));
const FN = {
  party: { id: newId('c'), type: 'Chaser', name: 'Party colors', runOrder: 'Loop', direction: 'Forward', speed: 1,
    steps: [S.red, S.blue, S.green, S.magenta].map(s => ({ fnId: s.id, fadeIn: 0.2, hold: 0.5, fadeOut: 0.2 })) },
  rainbow: { id: newId('cc'), type: 'Color cycle', name: 'Rainbow', colors: ['#ff0000', '#ffff00', '#00ff00', '#00ffff', '#0000ff', '#ff00ff'], duration: 6, targets: ALL },
  pulse: { id: newId('pu'), type: 'Pulse', name: 'Pulse front', period: 0.5, min: 0.1, max: 1, targets: ['f1', 'f2', 'f3'] },
  flash: { id: newId('fl'), type: 'Flash', name: 'Flash white', fnId: S.white.id },
  strobe: { id: newId('st'), type: 'Strobe', name: 'Strobe back', rate: 8, targets: ['f4', 'f5'], hardware: false },
  fade: { id: newId('fd'), type: 'Fade', name: 'Warm to blue', fromId: S.warm.id, toId: S.bluewash.id, duration: 4 },
};
state.functions.push(...Object.values(FN));
state.selFunction = FN.party.id; state.selScene = S.warm.id;
const fnById = id => state.functions.find(f => f.id === id);

state.console.pages = [
  { id: 'p1', name: 'Basic colors', widgets: [
    { id: 'w1', kind: 'button', label: 'BLACKOUT', x: 16, y: 16, w: 160, h: 72, action: 'blackout', color: '#c42b1c' },
    { id: 'w2', kind: 'button', label: 'RELEASE ALL', x: 192, y: 16, w: 160, h: 72, action: 'releaseAll' },
    { id: 'w3', kind: 'flash', label: 'FLASH WHITE', x: 368, y: 16, w: 160, h: 72, fnId: FN.flash.id, color: '#ffffff' },
    ...[S.red, S.green, S.blue, S.white, S.cyan, S.magenta, S.yellow].map((s, i) => ({ id: 'wc' + i, kind: 'button', label: s.name, x: 16 + i * 112, y: 112, w: 96, h: 96, fnId: s.id, color: rgbToHex(s.color.r, s.color.g, s.color.b), solo: 'colors' })),
    { id: 'w4', kind: 'button', label: '▶ Party colors', x: 16, y: 232, w: 176, h: 64, fnId: FN.party.id, color: '#60cdff' },
    { id: 'w5', kind: 'button', label: '▶ Rainbow', x: 208, y: 232, w: 176, h: 64, fnId: FN.rainbow.id, color: '#b44dff' },
    { id: 'w6', kind: 'button', label: '▶ Pulse front', x: 400, y: 232, w: 176, h: 64, fnId: FN.pulse.id, color: '#6ccb5f' },
    { id: 'w7', kind: 'slider', label: 'Master', x: 816, y: 16, w: 72, h: 280, bind: 'master' },
    { id: 'w8', kind: 'slider', label: 'Speed', x: 904, y: 16, w: 72, h: 280, bind: 'speed' },
    { id: 'w9', kind: 'speed', label: 'TAP', x: 592, y: 232, w: 96, h: 64 },
  ] },
  { id: 'p2', name: 'Chases', widgets: [
    { id: 'w20', kind: 'button', label: '▶ Party colors', x: 16, y: 16, w: 200, h: 96, fnId: FN.party.id, color: '#60cdff' },
    { id: 'w21', kind: 'button', label: '▶ Strobe back', x: 232, y: 16, w: 200, h: 96, fnId: FN.strobe.id, color: '#fce100' },
    { id: 'w22', kind: 'button', label: '▶ Warm to blue', x: 448, y: 16, w: 200, h: 96, fnId: FN.fade.id, color: '#ff9a40' },
    { id: 'w23', kind: 'label', label: 'Tip: Edit layout to drag widgets', x: 16, y: 136, w: 632, h: 40 },
  ] },
];

state.show.tracks = [
  { id: 't1', name: 'Track 1', items: [
    { id: 'i1', fnId: S.bluewash.id, start: 0, len: 15, fadeIn: 2, fadeOut: 0.5 },
    { id: 'i2', fnId: FN.party.id, start: 15, len: 16, fadeIn: 0, fadeOut: 0 },
    { id: 'i3', fnId: S.red.id, start: 31, len: 8, fadeIn: 0.5, fadeOut: 0.5 },
    { id: 'i4', fnId: FN.rainbow.id, start: 39, len: 24, fadeIn: 1, fadeOut: 1 }] },
  { id: 't2', name: 'Track 2', items: [
    { id: 'i5', fnId: FN.pulse.id, start: 8, len: 8, fadeIn: 0, fadeOut: 0 },
    { id: 'i6', fnId: FN.flash.id, start: 31, len: 0.5, fadeIn: 0, fadeOut: 0 },
    { id: 'i7', fnId: FN.strobe.id, start: 47, len: 4, fadeIn: 0, fadeOut: 0 }] },
];

/* ============================================================== Pages & menus */
const PAGES = [
  { id: 'fixtures', title: 'Fixtures', symbol: 'Lightbulb24', sub: 'Patch your lights: profile, mode, universe and start address.' },
  { id: 'library', title: 'Fixture Library', symbol: 'Library24', sub: 'Profiles define what every channel of a light does. Import QLC+ (.qxf) files or build your own.' },
  { id: 'functions', title: 'Functions', symbol: 'Flash24', sub: 'Reusable behaviors: chasers, sequences, color cycles, fades, pulses, flash and strobe.' },
  { id: 'scenes', title: 'Scenes', symbol: 'Color24', sub: 'Design static looks with a color picker and profile-driven channel controls.' },
  { id: 'console', title: 'Virtual Console', symbol: 'Board24', sub: 'Your live control surface. Press buttons, move sliders; edit the layout any time.' },
  { id: 'show', title: 'Show Timeline', symbol: 'Timeline24', sub: 'Build shows to music: audio, beats, and functions placed in time.' },
  { id: 'test', title: 'Test & Preview', symbol: 'Beaker24', sub: 'Check every channel of a fixture at a safe intensity before going live.' },
  { id: 'io', title: 'Input / Output', symbol: 'PlugConnected24', sub: 'Connect universes to Art-Net, sACN or USB-DMX interfaces.' },
  { id: 'settings', title: 'Settings', symbol: 'Settings24', sub: 'Theme, safety and file preferences.' },
];

const PAGE_ACTIONS = {
  fixtures: [['primary', 'Add24', 'Add fixture', 'addFixture'], ['', 'Copy24', 'Duplicate', 'dupFixture'], ['', 'Delete24', 'Delete', 'delFixture']],
  library: [['', 'Edit24', 'Edit profile', 'editProfile'], ['', 'CheckmarkCircle24', 'Mark verified', 'verifyProfile']],
  functions: [['primary', 'Add24', 'New function', 'newFunction'], ['', 'Copy24', 'Duplicate', 'dupFunction'], ['', 'Delete24', 'Delete', 'delFunction']],
  scenes: [['primary', 'Add24', 'New scene', 'newScene'], ['', 'Wand24', 'Create starter scenes', 'starterScenes']],
  console: [],
  show: [['primary', 'Add24', 'New function for selection', 'fnForRange']],
  test: [],
  io: [['', 'ArrowSync24', 'Discover Art-Net nodes', 'discover']],
  settings: [],
};

const MENUS = [
  ['File', [['New project', 'Ctrl+N', 'new'], ['Open project…', 'Ctrl+O', 'open'], ['Open recent ▸', '', 'recent'], '-',
    ['Save', 'Ctrl+S', 'save'], ['Save as…', 'Ctrl+Shift+S', 'saveAs'], '-',
    ['Import QLC+ workspace (.qxw)…', '', 'todo'], ['Export patch report (PDF)…', '', 'todo'], '-', ['Exit', 'Alt+F4', 'exit']]],
  ['Edit', [['Undo', 'Ctrl+Z', 'undo'], ['Redo', 'Ctrl+Y', 'redo'], '-', ['Cut', 'Ctrl+X', 'todo'], ['Copy', 'Ctrl+C', 'copy'], ['Paste', 'Ctrl+V', 'paste'],
    ['Duplicate', 'Ctrl+D', 'duplicate'], ['Delete', 'Del', 'delete'], ['Rename', 'F2', 'rename'], '-', ['Select all', 'Ctrl+A', 'todo'], ['Search everything…', 'Ctrl+K', 'palette']]],
  ['View', [...PAGES.map((p, i) => [p.title, `Ctrl+${i + 1}`, 'go:' + p.id]), '-', ['Stage preview panel', 'Ctrl+P', 'go:scenes'], ['DMX monitor', 'Ctrl+M', 'go:test'], '-',
    ['Dark theme', '', 'theme:dark', () => theme() === 'dark'], ['Light theme', '', 'theme:light', () => theme() === 'light'], '-',
    ['Full screen', 'F11', 'fullscreen', () => $('#window').classList.contains('fullscreen')]]],
  ['Fixtures', [['Add fixture…', 'Ctrl+Shift+A', 'addFixture'], ['Add group…', '', 'addGroup'], ['Auto-patch selected', '', 'autoPatch'], '-',
    ['Import fixture profile (.qxf / .json)…', '', 'importProfile'], ['New fixture profile…', '', 'newProfile'], ['Fixture library', '', 'go:library']]],
  ['Functions', [['New scene', '', 'newScene'], ...['Chaser', 'Sequence', 'Color cycle', 'Fade', 'Pulse', 'Flash', 'Strobe'].map(t => ['New ' + t.toLowerCase(), '', 'newFn:' + t]), '-',
    ['Create starter scenes', '', 'starterScenes'], '-', ['Stop all functions', 'Shift+Esc', 'stopAll']]],
  ['Show', [['New show (timeline)…', '', 'go:show'], ['Add audio track…', '', 'tlAddAudio'], ['Add function track', '', 'tlAddTrack'], '-',
    ['Play / Pause', 'Space', 'tlPlay'], ['Stop & return to start', 'Home', 'tlStop'], '-', ['Analyze beats', '', 'analyze']]],
  ['Output', [['Enable DMX output', 'Ctrl+E', 'outOn'], ['Disable DMX output', 'Ctrl+Shift+E', 'outOff'], '-',
    ['Blackout', 'B', 'blackout', () => state.blackout], ['Release all', 'Esc', 'releaseAll'], '-', ['Configure outputs…', '', 'go:io']]],
  ['Tools', [['Test fixture…', '', 'go:test'], ['Profile editor', '', 'newProfile'], ['Operator mode (lock editing)', 'F12', 'vcLock', () => state.console.locked], '-', ['Settings', 'Ctrl+,', 'go:settings']]],
  ['Help', [['Getting started', 'F1', 'help'], ['Keyboard shortcuts', '', 'shortcuts'], ['DMX basics', '', 'dmxBasics'], '-', ['About DMX Pro', '', 'about']]],
];

/* ============================================================== Shell */
function theme() { return document.documentElement.dataset.theme || (matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark'); }
function setTheme(t) { document.documentElement.dataset.theme = t; $('#setTheme').value = t; }

function buildMenus() {
  const bar = $('#menubar');
  bar.innerHTML = MENUS.map(([name, items], mi) => `
    <div class="menu" data-mi="${mi}"><button>${name}</button><div class="menu-dropdown">
      ${items.map(it => it === '-' ? '<div class="menu-sep"></div>' :
        `<button class="menu-item" data-action="${it[2]}"><span class="check">${it[3] && it[3]() ? '✓' : ''}</span><span>${esc(it[0])}</span><span class="gesture">${it[1]}</span></button>`).join('')}
    </div></div>`).join('');
  let open = null;
  const close = () => { open?.classList.remove('open'); open = null; };
  bar.querySelectorAll('.menu').forEach(m => {
    const btn = m.querySelector(':scope > button');
    btn.addEventListener('click', e => { e.stopPropagation(); const was = open === m; close(); if (!was) { buildMenuChecks(m); m.classList.add('open'); open = m; } });
    btn.addEventListener('mouseenter', () => { if (open && open !== m) { close(); buildMenuChecks(m); m.classList.add('open'); open = m; } });
  });
  document.addEventListener('click', close);
}
function buildMenuChecks(menuEl) {
  const items = MENUS[+menuEl.dataset.mi][1].filter(i => i !== '-');
  menuEl.querySelectorAll('.menu-item').forEach((b, i) => { b.querySelector('.check').textContent = items[i][3] && items[i][3]() ? '✓' : ''; });
}

function buildNav() {
  const item = (p, i) => `<button class="nav-item" data-action="go:${p.id}" data-page="${p.id}">${icon(p.symbol, 20)}<span>${p.title}</span><span class="kbd">Ctrl+${i + 1}</span></button>`;
  $('#navList').innerHTML = PAGES.slice(0, 8).map(item).join('');
  $('#navBottom').innerHTML = item(PAGES[8], 8);
}

function go(pageId) {
  state.page = pageId;
  const p = PAGES.find(x => x.id === pageId);
  $$('.nav-item').forEach(n => n.classList.toggle('active', n.dataset.page === pageId));
  $$('.view').forEach(v => v.classList.toggle('active', v.id === 'view-' + pageId));
  $('#pageTitle').textContent = p.title;
  $('#pageSubtitle').textContent = p.sub;
  $('#pageActions').innerHTML = (PAGE_ACTIONS[pageId] || []).map(([cls, sym, label, act]) =>
    `<button class="btn ${cls}" data-action="${act}">${icon(sym)}${label}</button>`).join('');
  RENDER[pageId]?.();
  hydrateIcons();
}

function setDirty() { state.project.dirty = true; updateTitle(); }
function updateTitle() { $('#windowTitle').textContent = `${state.project.name}${state.project.dirty ? ' •' : ''} — DMX Pro`; }

function info(msg, kind = 'info') {
  const bar = $('#infobar');
  bar.className = 'infobar ' + (kind === 'info' ? '' : kind);
  $('#infobarMsg').innerHTML = msg;
  bar.hidden = false;
}
let toastTimer;
function toast(msg) {
  $('#toastMsg').innerHTML = msg;
  $('#toast').classList.add('show');
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => $('#toast').classList.remove('show'), 2600);
}
function dialog(title, bodyHtml, buttons) {
  $('#dialogBody').innerHTML = `<h2>${esc(title)}</h2>${bodyHtml}`;
  $('#dialogFoot').innerHTML = '';
  buttons.forEach(([label, cls, fn]) => {
    const b = document.createElement('button');
    b.className = 'btn ' + (cls || ''); b.textContent = label;
    b.onclick = () => { if (fn?.() !== false) closeDialog(); };
    $('#dialogFoot').appendChild(b);
  });
  $('#dialogHost').classList.add('open');
  hydrateIcons($('#dialogBody'));
  $('#dialogBody input, #dialogBody select')?.focus();
}
const closeDialog = () => $('#dialogHost').classList.remove('open');

/* ============================================================== Validation (mirrors PatchValidator) */
function patchIssues() {
  const issues = [];
  const byU = {};
  state.fixtures.forEach(f => {
    const n = footprint(f), end = f.address + n - 1;
    if (f.address < 1 || f.address > 512) issues.push({ f, code: 'ADDRESS_OUT_OF_RANGE', msg: `'${f.name}': start address ${f.address} is outside 1–512.` });
    else if (end > 512) issues.push({ f, code: 'FOOTPRINT_EXCEEDS_UNIVERSE', msg: `'${f.name}' needs ${n} channels and would end at ${end} (max 512).` });
    if (!profileById(f.profileId).verified) issues.push({ f, code: 'PROFILE_UNVERIFIED', warn: true, msg: `'${f.name}': profile not verified.` });
    if (!f.enabled) return;
    (byU[f.universe] ||= []).forEach(o => {
      const oe = o.address + footprint(o) - 1;
      if (f.address <= oe && o.address <= end) issues.push({ f, code: 'ADDRESS_OVERLAP', overlap: true, msg: `'${f.name}' (${f.address}–${end}) overlaps '${o.name}' (${o.address}–${oe}).` });
    });
    byU[f.universe].push(f);
  });
  return issues;
}
function freeAddress(universe, count, ignoreId) {
  const used = state.fixtures.filter(f => f.enabled && f.universe === universe && f.id !== ignoreId).map(f => [f.address, f.address + footprint(f) - 1]).sort((a, b) => a[0] - b[0]);
  let c = 1;
  for (const [s, e] of used) { if (c + count - 1 < s) break; c = Math.max(c, e + 1); }
  return c + count - 1 <= 512 ? c : null;
}
const universes = () => [...new Set([1, 2, 3, ...state.fixtures.map(f => f.universe)])].sort((a, b) => a - b);

/* ============================================================== Engine (simulated) */
function now() { return performance.now() / 1000; }
function setLive(fid, values) { state.live[fid] = { ...(state.live[fid] || {}), ...values }; }

function sceneValues(s, k = 1) {
  return { Red: s.color.r * s.bright * k, Green: s.color.g * s.bright * k, Blue: s.color.b * s.bright * k, White: (s.color.w || 0) * s.bright * k };
}
function lerpVals(a, b, t) { const o = {}; for (const key of new Set([...Object.keys(a), ...Object.keys(b)])) o[key] = (a[key] || 0) * (1 - t) + (b[key] || 0) * t; return o; }

/* Evaluates a function at local time t (seconds) → { fixtureId: values } */
function evalFn(fn, t) {
  const out = {};
  const put = (targets, v) => targets.forEach(id => { out[id] = { ...(out[id] || {}), ...v }; });
  const sp = state.speed * (fn.speed || 1);
  switch (fn.type) {
    case 'Scene': put(fn.targets, sceneValues(fn)); break;
    case 'Chaser': {
      const steps = fn.steps.map(s => ({ ...s, fn: fnById(s.fnId) })).filter(s => s.fn);
      if (!steps.length) break;
      const lens = steps.map(s => (s.fadeIn + s.hold + s.fadeOut) / sp);
      const total = lens.reduce((a, b) => a + b, 0);
      let tt = (t % total), i = 0;
      while (tt > lens[i]) { tt -= lens[i]; i++; }
      if (fn.direction === 'Backward') i = steps.length - 1 - i;
      const cur = steps[i], prev = steps[(i - 1 + steps.length) % steps.length];
      const fi = cur.fadeIn / sp;
      const k = fi > 0 ? Math.min(1, tt / fi) : 1;
      cur.fn.targets.forEach(id => { out[id] = lerpVals(sceneValues(prev.fn), sceneValues(cur.fn), k); });
      break;
    }
    case 'Color cycle': {
      const n = fn.colors.length, pos = ((t * sp) / fn.duration * n) % n, i = Math.floor(pos), k = pos - i;
      const a = hexToRgb(fn.colors[i]), b = hexToRgb(fn.colors[(i + 1) % n]);
      put(fn.targets, { Red: a[0] + (b[0] - a[0]) * k, Green: a[1] + (b[1] - a[1]) * k, Blue: a[2] + (b[2] - a[2]) * k, White: 0 });
      break;
    }
    case 'Pulse': {
      const v = fn.min + (fn.max - fn.min) * (0.5 + 0.5 * Math.sin(t * sp * Math.PI * 2 / fn.period));
      put(fn.targets, { _scale: v });
      break;
    }
    case 'Flash': { const s = fnById(fn.fnId); if (s) put(s.targets, sceneValues(s)); break; }
    case 'Strobe': {
      const on = Math.floor(t * fn.rate * 2) % 2 === 0;
      put(fn.targets, on ? { Red: 255, Green: 255, Blue: 255, White: 255 } : { Red: 0, Green: 0, Blue: 0, White: 0 });
      break;
    }
    case 'Fade': {
      const a = fnById(fn.fromId), b = fnById(fn.toId); if (!a || !b) break;
      const k = Math.min(1, t * sp / fn.duration);
      [...new Set([...a.targets, ...b.targets])].forEach(id => { out[id] = lerpVals(sceneValues(a), sceneValues(b), k); });
      break;
    }
  }
  return out;
}

/* Final per-fixture logical values (0..255 by channel type), incl. master/blackout */
function fixtureState() {
  const res = {};
  state.fixtures.forEach(f => { res[f.id] = { Red: 0, Green: 0, Blue: 0, White: 0, ...(state.live[f.id] || {}) }; });
  const apply = (o, k = 1) => Object.entries(o).forEach(([fid, v]) => {
    if (!res[fid]) return;
    if ('_scale' in v) { ['Red', 'Green', 'Blue', 'White'].forEach(c => res[fid][c] *= v._scale); return; }
    Object.entries(v).forEach(([c, x]) => { res[fid][c] = x * k; });
  });
  const t = now();
  for (const [id, start] of state.running) { const fn = fnById(id); if (fn) apply(evalFn(fn, t - start)); }
  if (state.show.playing || state.show.t > 0) {
    state.show.tracks.forEach(tr => tr.items.forEach(it => {
      const fn = fnById(it.fnId); const lt = state.show.t - it.start;
      if (!fn || lt < 0 || lt > it.len) return;
      let k = 1;
      if (it.fadeIn > 0 && lt < it.fadeIn) k = lt / it.fadeIn;
      if (it.fadeOut > 0 && it.len - lt < it.fadeOut) k = Math.min(k, (it.len - lt) / it.fadeOut);
      apply(evalFn(fn, lt), k);
    }));
  }
  if (state.test) res[state.test.fixtureId] = { ...res[state.test.fixtureId], ...state.test.values };
  Object.values(res).forEach(v => {
    ['Red', 'Green', 'Blue', 'White', 'Amber', 'UV'].forEach(c => { if (c in v) v[c] = state.blackout ? 0 : clamp(v[c] * state.master, 0, 255); });
  });
  return res;
}

/* DMX universe buffer (1-based address → value), from profiles. */
function universeBuffer(u, fs) {
  const buf = new Uint8Array(513);
  state.fixtures.filter(f => f.universe === u && f.enabled).forEach(f => {
    const v = fs[f.id];
    channelsOf(f).forEach((ch, i) => {
      const addr = f.address + i; if (addr > 512) return;
      let x = ch.type in v ? v[ch.type] : ch.def;
      if (ch.type === 'Dimmer') x = state.blackout ? 0 : (v.Dimmer ?? ch.def) * (state.test?.fixtureId === f.id ? 1 : 1);
      buf[addr] = clamp(Math.round(x), 0, 255);
    });
  });
  return buf;
}
function lampColor(f, v) {
  const chans = channelsOf(f);
  const has = t => chans.some(c => c.type === t);
  const dim = has('Dimmer') ? (state.blackout ? 0 : (v.Dimmer ?? 255) / 255) : 1;
  const w = has('White') ? v.White : 0;
  return [v.Red + w * 0.85, v.Green + w * 0.85, v.Blue + w * 0.8].map(x => clamp(x * dim, 0, 255));
}
function renderStage(el, ids, note) {
  if (!el) return;
  const key = ids.join(',') + '|' + (note || '');
  if (el.dataset.ids !== key) {
    el.dataset.ids = key;
    el.innerHTML = ids.map(id => { const f = state.fixtures.find(x => x.id === id); return f ? `<div class="lamp" data-fid="${id}"><div class="bulb"></div><span>${esc(f.name)}</span></div>` : ''; }).join('') +
      (note ? `<div class="stage-note">${note}</div>` : '');
  }
}
function paintStages(fs) {
  $$('.stage .lamp').forEach(l => {
    const f = state.fixtures.find(x => x.id === l.dataset.fid); if (!f) return;
    const [r, g, b] = lampColor(f, fs[f.id]);
    const c = `rgb(${r | 0},${g | 0},${b | 0})`, lum = (r + g + b) / 765;
    const bulb = l.firstElementChild;
    bulb.style.background = c;
    bulb.style.boxShadow = lum > 0.02 ? `0 0 ${12 + lum * 40}px ${4 + lum * 14}px ${c}` : 'none';
  });
}

let frames = 0, lastFpsT = now(), lastTick = now();
function tick() {
  const t = now(), dt = t - lastTick; lastTick = t;
  if (state.show.playing) {
    state.show.t = Math.min(state.show.length, state.show.t + dt);
    if (state.show.t >= state.show.length) state.show.playing = false;
    if (state.page === 'show') updatePlayhead();
  }
  const fs = fixtureState();
  paintStages(fs);
  if (state.page === 'test') paintMonitor(fs);
  frames++;
  if (t - lastFpsT > 0.5) {
    const fps = state.output.enabled ? 44 : 0;
    $('#fpsText').textContent = `Universe 1 · ${fps} fps`;
    frames = 0; lastFpsT = t;
  }
  requestAnimationFrame(tick);
}

function startFn(id, solo) {
  if (solo) state.console.pages.flatMap(p => p.widgets).filter(w => w.solo === solo && w.fnId !== id).forEach(w => state.running.delete(w.fnId));
  state.running.set(id, now());
}
function stopFn(id) { state.running.delete(id); }
function releaseAll() {
  state.running.clear(); state.live = {}; state.test = null; state.blackout = false;
  $('#blackoutBtn').classList.remove('on');
  if (state.page === 'console') renderConsole();
  toast('Released all functions — channels back to profile defaults.');
}
function toggleBlackout() {
  state.blackout = !state.blackout;
  $('#blackoutBtn').classList.toggle('on', state.blackout);
  if (state.page === 'console') renderConsole();
  toast(state.blackout ? 'Blackout ON — DMX is still sent, intensities are 0.' : 'Blackout off.');
}

/* ============================================================== Page renderers */
const RENDER = {};

/* ---------- Fixtures ---------- */
RENDER.fixtures = () => {
  const uSel = $('#fxUniverseFilter'), mapSel = $('#fxMapUniverse');
  const opts = universes().map(u => `<option value="${u}">Universe ${u}</option>`).join('');
  const keepU = uSel.value, keepMap = mapSel.value || '1';
  uSel.innerHTML = '<option value="">All universes</option>' + opts; uSel.value = keepU;
  mapSel.innerHTML = opts; mapSel.value = keepMap;
  const q = $('#fxSearch').value.toLowerCase();
  const issues = patchIssues();
  const rows = state.fixtures.filter(f => (!uSel.value || f.universe == uSel.value) &&
    (!q || [f.name, profileById(f.profileId).model, ...state.groups.filter(g => g.fixtureIds.includes(f.id)).map(g => g.name)].join(' ').toLowerCase().includes(q)));
  $('#fxTable tbody').innerHTML = rows.map(f => {
    const p = profileById(f.profileId), n = footprint(f);
    const fi = issues.filter(i => i.f === f && !i.warn);
    const groups = state.groups.filter(g => g.fixtureIds.includes(f.id)).map(g => `<span class="chip" style="padding-right:10px">${esc(g.name)}</span>`).join(' ');
    return `<tr data-fid="${f.id}" class="${f.id === state.selFixture ? 'selected' : ''}" style="${f.enabled ? '' : 'opacity:.5'}">
      <td>${fi.length ? `<span title="${esc(fi.map(i => i.msg).join('\n'))}" style="color:var(--SystemFillColorCritical)">${icon('Warning24')}</span>` : `<span class="dot ${f.enabled ? 'ok' : ''}"></span>`}</td>
      <td><b>${esc(f.name)}</b></td><td>${esc(p.model)} ${p.verified ? '' : '<span class="badge warn" title="Unverified profile">!</span>'}</td>
      <td>${esc(f.mode)}</td><td class="num">${f.universe}</td><td class="num">${f.address}–${f.address + n - 1}</td><td>${groups}</td></tr>`;
  }).join('') || `<tr><td colspan="7"><div class="empty">No fixtures yet — click <b>Add fixture</b>.</div></td></tr>`;
  renderAddrMap(+mapSel.value, issues);
  renderFixtureDetails(issues);
  renderGroups();
};

function renderAddrMap(u, issues) {
  const owners = Array.from({ length: 513 }, () => []);
  state.fixtures.filter(f => f.universe === u && f.enabled).forEach(f => { for (let a = f.address; a < f.address + footprint(f) && a <= 512; a++) owners[a].push(f); });
  const palette = ['#60cdff', '#6ccb5f', '#ffb000', '#b44dff', '#ff66cc', '#4dd0c8', '#ff8a4d'];
  const colorOf = f => palette[state.fixtures.indexOf(f) % palette.length];
  let html = '';
  for (let a = 1; a <= 512; a++) {
    const o = owners[a];
    const cls = ['addr-cell', o.length ? 'used' : '', o.length > 1 ? 'overlap' : '', o.some(f => f.id === state.selFixture) ? 'sel' : ''].join(' ');
    html += `<div class="${cls}" data-addr="${a}" title="${a}${o.length ? ' · ' + esc(o.map(f => f.name).join(', ')) : ' · free'}" style="${o.length ? `background:${colorOf(o[0])}` : ''}"></div>`;
  }
  $('#addrMap').innerHTML = html;
  $('#addrMap').dataset.u = u;
}

function renderFixtureDetails(issues) {
  const f = state.fixtures.find(x => x.id === state.selFixture);
  const box = $('#fxDetails');
  if (!f) { box.innerHTML = '<div class="empty">Select a fixture to edit it.</div>'; return; }
  const p = profileById(f.profileId), chans = channelsOf(f), n = chans.length;
  const fi = issues.filter(i => i.f === f);
  box.innerHTML = `
    <div class="row" style="margin-bottom:12px"><span class="h2 grow" style="margin:0">${esc(f.name)}</span>
      <label class="toggle" title="Disabled fixtures send nothing"><input type="checkbox" id="fdEnabled" ${f.enabled ? 'checked' : ''}><span class="track"></span><span>${f.enabled ? 'Enabled' : 'Disabled'}</span></label></div>
    ${fi.map(i => `<div class="infobar ${i.warn ? 'warning' : 'error'}" style="margin-bottom:8px">${icon('Warning24')}<span class="msg">${esc(i.msg)}</span>${i.overlap ? '<button class="btn small" data-action="autoAddress">Fix: next free address</button>' : ''}</div>`).join('')}
    <div class="field"><label>Name</label><input class="input" id="fdName" value="${esc(f.name)}"></div>
    <div class="field"><label>Profile</label><select class="input" id="fdProfile">${PROFILES.map(x => `<option value="${x.id}" ${x.id === f.profileId ? 'selected' : ''}>${esc(x.manufacturer + ' ' + x.model)}${x.verified ? '' : '  (unverified)'}</option>`).join('')}</select></div>
    <div class="row"><div class="field grow"><label>DMX mode</label><select class="input" id="fdMode">${p.modes.map(m => `<option ${m.name === f.mode ? 'selected' : ''}>${esc(m.name)}</option>`).join('')}</select></div>
      <div class="field" style="width:110px"><label>Channels</label><input class="input" value="${n}" disabled></div></div>
    <div class="row"><div class="field" style="width:120px"><label>Universe</label><input class="input" type="number" min="1" id="fdUniverse" value="${f.universe}"></div>
      <div class="field" style="width:120px"><label>Start address</label><input class="input" type="number" min="1" max="512" id="fdAddress" value="${f.address}"></div>
      <div class="field grow"><label>Footprint</label><div class="row"><span class="badge accent">${f.address} – ${f.address + n - 1}</span><button class="btn small" data-action="autoAddress" title="Find the next free address">Auto</button></div></div></div>
    <div class="field"><label>Groups</label><div class="row wrap">
      ${state.groups.filter(g => g.fixtureIds.includes(f.id)).map(g => `<span class="chip">${esc(g.name)}<button data-action="ungroup" data-gid="${g.id}" title="Remove from group">✕</button></span>`).join('')}
      <select class="input" id="fdAddGroup" style="width:150px; min-height:24px; padding:0 8px"><option value="">+ Add to group</option>${state.groups.filter(g => !g.fixtureIds.includes(f.id)).map(g => `<option value="${g.id}">${esc(g.name)}</option>`).join('')}</select></div></div>
    <div class="field"><label>Notes / physical location</label><input class="input" id="fdNotes" value="${esc(f.notes)}"></div>
    <div class="field"><label>Channel map (from profile)</label><div class="channel-chips">
      ${chans.map((c, i) => `<span class="chan" title="DMX ${f.address + i}"><i style="background:${CH_COLORS[c.type]}"></i>${f.address + i} · ${esc(c.name)}${c.type === 'Generic' ? ' ⚠' : ''}</span>`).join('')}</div></div>
    <div class="row" style="margin-top:8px"><button class="btn" data-action="go:test">${icon('Beaker24')}Test this fixture</button><button class="btn" data-action="go:library">${icon('Library24')}View profile</button></div>`;
  const upd = (fn) => e => { fn(e.target); setDirty(); RENDER.fixtures(); hydrateIcons(); };
  $('#fdName').onchange = upd(t => f.name = t.value);
  $('#fdEnabled').onchange = upd(t => f.enabled = t.checked);
  $('#fdProfile').onchange = upd(t => { f.profileId = t.value; f.mode = profileById(t.value).modes.at(-1).name; });
  $('#fdMode').onchange = upd(t => f.mode = t.value);
  $('#fdUniverse').onchange = upd(t => f.universe = Math.max(1, +t.value || 1));
  $('#fdAddress').onchange = upd(t => f.address = +t.value || 1);
  $('#fdNotes').onchange = upd(t => f.notes = t.value);
  $('#fdAddGroup').onchange = upd(t => { const g = state.groups.find(x => x.id === t.value); g?.fixtureIds.push(f.id); });
}

function renderGroups() {
  $('#groupList').innerHTML = state.groups.map(g => `<div class="list-item">${icon('Group24')}<span class="title">${esc(g.name)}</span>
    <span class="caption">${g.fixtureIds.map(id => esc(state.fixtures.find(f => f.id === id)?.name)).join(', ') || 'empty'}</span>
    <span class="meta"><button class="btn subtle icon small" data-action="delGroup" data-gid="${g.id}" title="Delete group">${icon('Delete24', 14)}</button></span></div>`).join('') || '<div class="caption">No groups yet.</div>';
}

function addFixtureDialog() {
  const p0 = PROFILES[1];
  dialog('Add fixture', `
    <div class="field"><label>Profile</label><select class="input" id="afProfile">${PROFILES.map(p => `<option value="${p.id}" ${p === p0 ? 'selected' : ''}>${esc(p.manufacturer + ' ' + p.model)}</option>`).join('')}</select></div>
    <div class="row"><div class="field grow"><label>DMX mode</label><select class="input" id="afMode"></select></div>
      <div class="field" style="width:110px"><label>How many</label><input class="input" type="number" min="1" max="64" id="afCount" value="1"></div></div>
    <div class="row"><div class="field grow"><label>Name</label><input class="input" id="afName" value="PAR"></div>
      <div class="field" style="width:110px"><label>Universe</label><input class="input" type="number" min="1" id="afUniverse" value="1"></div>
      <div class="field" style="width:130px"><label>Start address</label><input class="input" type="number" min="1" max="512" id="afAddr"></div></div>
    <div class="infobar" id="afHint" style="margin:0"></div>
    <p class="caption">Tip: set the same mode and address on the fixture's own display/DIP switches.</p>`,
  [['Add', 'primary', () => {
    const p = profileById($('#afProfile').value), mode = $('#afMode').value, u = +$('#afUniverse').value || 1, count = clamp(+$('#afCount').value || 1, 1, 64);
    const n = p.modes.find(m => m.name === mode).channels.length;
    let addr = +$('#afAddr').value || 1;
    for (let i = 0; i < count; i++) {
      const f = { id: newId('f'), name: count > 1 ? `${$('#afName').value} ${i + 1}` : $('#afName').value, profileId: p.id, mode, universe: u, address: addr, enabled: true, notes: '' };
      state.fixtures.push(f); state.selFixture = f.id; addr += n;
    }
    setDirty(); RENDER.fixtures(); hydrateIcons(); toast(`Added ${count} fixture(s).`);
  }], ['Cancel']]);
  const refresh = () => {
    const p = profileById($('#afProfile').value);
    if (!p.modes.some(m => m.name === $('#afMode').value)) $('#afMode').innerHTML = p.modes.map(m => `<option>${esc(m.name)}</option>`).join('');
    const n = p.modes.find(m => m.name === $('#afMode').value).channels.length, count = +$('#afCount').value || 1, u = +$('#afUniverse').value || 1;
    const free = freeAddress(u, n * count);
    if (document.activeElement !== $('#afAddr')) $('#afAddr').value = free ?? '';
    $('#afHint').innerHTML = free ? `${icon('Info24')}<span class="msg">${count} × ${n} ch = ${n * count} channels → addresses <b>${free}–${free + n * count - 1}</b> (next free in universe ${u}).</span>` :
      `${icon('Warning24')}<span class="msg">Universe ${u} has no room for ${n * count} channels. Pick another universe.</span>`;
  };
  ['#afProfile', '#afMode', '#afCount', '#afUniverse'].forEach(s => $(s).addEventListener('input', refresh));
  refresh();
}

/* ---------- Library ---------- */
RENDER.library = () => {
  const q = $('#libSearch').value.toLowerCase();
  $('#libList').innerHTML = PROFILES.filter(p => !q || `${p.manufacturer} ${p.model} ${p.type}`.toLowerCase().includes(q)).map(p => `
    <div class="list-item ${p.id === state.selProfile ? 'selected' : ''}" data-pid="${p.id}">
      <div><div class="title">${esc(p.model)}</div><div class="caption">${esc(p.manufacturer)} · ${esc(p.type)} · ${p.modes.length} mode(s)</div></div>
      <span class="meta" style="color:var(${p.verified ? '--SystemFillColorSuccess' : '--SystemFillColorCaution'})" title="${p.verified ? 'Verified on the real fixture' : 'Unverified: confirm channels on the real light'}">${icon(p.verified ? 'CheckmarkCircle24' : 'Warning24')}</span></div>`).join('');
  const p = profileById(state.selProfile);
  if (!p) { $('#libDetails').innerHTML = '<div class="empty">Pick a profile or import a QLC+ fixture file.</div>'; return; }
  if (!p.modes.some(m => m.name === state.selProfileMode)) state.selProfileMode = p.modes.at(-1).name;
  const mode = p.modes.find(m => m.name === state.selProfileMode);
  $('#libDetails').innerHTML = `
    <div class="row"><span class="h2" style="margin:0">${esc(p.manufacturer)} ${esc(p.model)}</span><span class="badge">${esc(p.type)}</span>
      <span class="badge ${p.verified ? 'ok' : 'warn'}">${p.verified ? '✓ Verified' : '⚠ Unverified'}</span></div>
    <p class="muted">${esc(p.source)}</p>
    <div class="row" style="margin:16px 0 12px"><span>DMX mode</span><select class="input" id="libMode" style="width:220px">${p.modes.map(m => `<option ${m === mode ? 'selected' : ''}>${esc(m.name)}</option>`).join('')}</select>
      <span class="caption">${mode.channels.length} channels</span><span class="grow"></span><span class="caption">Used by ${state.fixtures.filter(f => f.profileId === p.id).length} fixture(s)</span></div>
    <table class="table"><thead><tr><th class="num">Ch</th><th>Name</th><th>Type</th><th class="num">Default</th><th>Ranges</th></tr></thead><tbody>
      ${mode.channels.map((k, i) => { const c = p.channels.find(x => x.key === k); return `<tr><td class="num">${i + 1}</td>
        <td><span class="chan"><i style="background:${CH_COLORS[c.type]}"></i>${esc(c.name)}</span>${c.type === 'Generic' ? ` <span style="color:var(--SystemFillColorCaution)" title="Unknown meaning — raw 0–255 only">${icon('Warning24', 14)}</span>` : ''}</td>
        <td>${c.type}</td><td class="num">${c.def}</td><td class="caption">${(c.caps || []).map(r => `${r.min}–${r.max} ${esc(r.label)}${r.safe ? ' (safe)' : ''}`).join(' · ') || '0–255'}</td></tr>`; }).join('')}
    </tbody></table>`;
  $('#libMode').onchange = e => { state.selProfileMode = e.target.value; RENDER.library(); };
};

function profileEditor(p) {
  const types = ['Red', 'Green', 'Blue', 'White', 'Amber', 'UV', 'Dimmer', 'Strobe', 'ColorMacro', 'Program', 'Speed', 'Mode', 'Generic'];
  const draft = p ? JSON.parse(JSON.stringify(p)) : { id: 'my-fixture', manufacturer: '', model: 'My fixture', type: 'Color Changer', verified: false, source: 'User-defined', channels: [
    { key: 'red', name: 'Red', type: 'Red', def: 0 }, { key: 'green', name: 'Green', type: 'Green', def: 0 }, { key: 'blue', name: 'Blue', type: 'Blue', def: 0 }], modes: [] };
  const mode0 = draft.modes[draft.modes.length - 1] || { name: `${draft.channels.length}ch`, channels: draft.channels.map(c => c.key) };
  const rows = () => draft.channels.map((c, i) => `<tr><td class="num">${i + 1}</td><td><input class="input pe-name" data-i="${i}" value="${esc(c.name)}"></td>
    <td><select class="input pe-type" data-i="${i}">${types.map(t => `<option ${t === c.type ? 'selected' : ''}>${t}</option>`).join('')}</select></td>
    <td><input class="input pe-def" data-i="${i}" type="number" min="0" max="255" value="${c.def}" style="width:72px"></td>
    <td><button class="btn subtle icon small pe-del" data-i="${i}" title="Remove">${icon('Delete24', 14)}</button></td></tr>`).join('');
  dialog(p ? 'Edit fixture profile' : 'New fixture profile', `
    <div class="row"><div class="field grow"><label>Manufacturer</label><input class="input" id="peMan" value="${esc(draft.manufacturer)}"></div>
      <div class="field grow"><label>Model</label><input class="input" id="peModel" value="${esc(draft.model)}"></div></div>
    <div class="row"><div class="field grow"><label>Mode name</label><input class="input" id="peMode" value="${esc(mode0.name)}"></div>
      <label class="toggle" style="margin-top:8px"><input type="checkbox" id="peVerified" ${draft.verified ? 'checked' : ''}><span class="track"></span>Verified on real fixture</label></div>
    <div class="caption" style="margin-bottom:6px">Channels in DMX order — copy them from the manual's DMX chart. Unknown channels: use <b>Generic</b>.</div>
    <table class="table"><thead><tr><th>Ch</th><th>Name</th><th>Type</th><th>Default</th><th></th></tr></thead><tbody id="peRows">${rows()}</tbody></table>
    <button class="btn small" id="peAdd" style="margin-top:8px">${icon('Add24', 14)}Add channel</button>`,
  [['Save profile', 'primary', () => {
    draft.manufacturer = $('#peMan').value; draft.model = $('#peModel').value; draft.verified = $('#peVerified').checked;
    draft.modes = [{ name: $('#peMode').value || `${draft.channels.length}ch`, channels: draft.channels.map(c => c.key) }];
    if (!p) { draft.id = (draft.manufacturer + '-' + draft.model).toLowerCase().replace(/[^a-z0-9]+/g, '-'); PROFILES.push(draft); }
    else Object.assign(p, draft);
    state.selProfile = draft.id; go('library'); toast('Profile saved.');
  }], ['Cancel']]);
  const body = $('#dialogBody');
  const rebind = () => {
    $('#peRows').innerHTML = rows();
    $$('.pe-name', body).forEach(x => x.oninput = () => draft.channels[x.dataset.i].name = x.value);
    $$('.pe-type', body).forEach(x => x.onchange = () => draft.channels[x.dataset.i].type = x.value);
    $$('.pe-def', body).forEach(x => x.oninput = () => draft.channels[x.dataset.i].def = +x.value);
    $$('.pe-del', body).forEach(x => x.onclick = () => { draft.channels.splice(+x.dataset.i, 1); rebind(); });
  };
  $('#peAdd').onclick = () => { const k = 'ch' + (draft.channels.length + 1); draft.channels.push({ key: k, name: 'Channel ' + (draft.channels.length + 1), type: 'Generic', def: 0 }); rebind(); };
  rebind();
}

/* ---------- Functions ---------- */
const FN_ICON = { Scene: 'Color24', Chaser: 'ArrowRepeatAll24', Sequence: 'Timeline24', 'Color cycle': 'Color24', Fade: 'ArrowSync24', Pulse: 'TopSpeed24', Flash: 'Flash24', Strobe: 'Flash24' };
const fnDuration = fn => fn.type === 'Chaser' ? fn.steps.reduce((a, s) => a + s.fadeIn + s.hold + s.fadeOut, 0) : fn.type === 'Color cycle' ? fn.duration : fn.type === 'Fade' ? fn.duration : fn.type === 'Pulse' ? fn.period : 0;

RENDER.functions = () => {
  const tf = $('#fnTypeFilter');
  if (!tf.options.length) tf.innerHTML = '<option value="">All types</option>' + Object.keys(FN_ICON).map(t => `<option>${t}</option>`).join('');
  const q = $('#fnSearch').value.toLowerCase();
  $('#fnList').innerHTML = state.functions.filter(f => (!tf.value || f.type === tf.value) && (!q || f.name.toLowerCase().includes(q))).map(f => {
    const sw = f.type === 'Scene' ? `<span class="swatch" style="width:20px;height:20px;background:${rgbToHex(f.color.r, f.color.g, f.color.b)}"></span>` : icon(FN_ICON[f.type], 20);
    return `<div class="list-item ${f.id === state.selFunction ? 'selected' : ''}" data-fnid="${f.id}">${sw}<div><div class="title">${esc(f.name)}</div><div class="caption">${f.type}${fnDuration(f) ? ' · ' + fnDuration(f).toFixed(1) + ' s' : ''}</div></div>
      <span class="meta">${state.running.has(f.id) ? '<span class="badge ok">▶ running</span>' : ''}</span></div>`;
  }).join('');
  renderFnEditor();
};

function renderFnEditor() {
  const fn = fnById(state.selFunction), box = $('#fnEditor');
  if (!fn) { box.innerHTML = '<div class="empty">Select a function or create a new one.</div>'; return; }
  const running = state.running.has(fn.id);
  const head = `<div class="row" style="margin-bottom:16px">${icon(FN_ICON[fn.type], 24)}<input class="input" id="feName" value="${esc(fn.name)}" style="max-width:280px;font-weight:600"><span class="badge">${fn.type}</span>
    <span class="grow"></span><button class="btn ${running ? '' : 'primary'}" data-action="previewFn">${icon(running ? 'Stop24' : 'Play24')}${running ? 'Stop preview' : 'Preview'}</button>
    <button class="btn" data-action="fnToConsole">${icon('Board24')}Add to console</button></div>`;
  const sceneOpts = sel => state.functions.filter(f => f.type === 'Scene').map(s => `<option value="${s.id}" ${s.id === sel ? 'selected' : ''}>${esc(s.name)}</option>`).join('');
  const targets = ids => `<div class="field"><label>Targets</label><div class="row wrap">${state.groups.map(g => `<span class="chip" style="padding-right:10px">${icon('Group24', 12)} ${esc(g.name)}</span>`).join('')}
    ${ids.map(id => `<span class="chip" style="padding-right:10px">${esc(state.fixtures.find(f => f.id === id)?.name)}</span>`).join('')}</div></div>`;
  let body = '';
  switch (fn.type) {
    case 'Scene':
      body = `<p class="muted">Scenes are edited in the Scene Creator.</p><button class="btn primary" data-action="editScene" data-sid="${fn.id}">${icon('Color24')}Open in Scene Creator</button>`; break;
    case 'Chaser':
      body = `<div class="row wrap" style="margin-bottom:12px"><div class="field" style="width:160px;margin:0"><label>Run order</label><select class="input" data-k="runOrder">${['Loop', 'Single shot', 'Ping-pong', 'Random'].map(o => `<option ${o === fn.runOrder ? 'selected' : ''}>${o}</option>`).join('')}</select></div>
        <div class="field" style="width:160px;margin:0"><label>Direction</label><select class="input" data-k="direction">${['Forward', 'Backward'].map(o => `<option ${o === fn.direction ? 'selected' : ''}>${o}</option>`).join('')}</select></div>
        <div class="field" style="width:220px;margin:0"><label>Speed ${fn.speed.toFixed(2)}×</label><input type="range" min="0.25" max="4" step="0.05" value="${fn.speed}" data-k="speed" data-num="1"></div></div>
        <table class="table"><thead><tr><th>#</th><th>Step</th><th>Fade in (s)</th><th>Hold (s)</th><th>Fade out (s)</th><th></th></tr></thead><tbody>
        ${fn.steps.map((s, i) => `<tr class="step-row"><td>${i + 1}</td><td><select class="input st-fn" data-i="${i}">${sceneOpts(s.fnId)}</select></td>
          ${['fadeIn', 'hold', 'fadeOut'].map(k => `<td><input class="input st-num" data-i="${i}" data-k="${k}" type="number" step="0.1" min="0" value="${s[k]}"></td>`).join('')}
          <td class="row"><button class="btn subtle icon small" data-action="stepUp" data-i="${i}" title="Move up">↑</button><button class="btn subtle icon small" data-action="stepDel" data-i="${i}" title="Remove">${icon('Delete24', 14)}</button></td></tr>`).join('')}
        </tbody></table><button class="btn small" data-action="stepAdd" style="margin-top:8px">${icon('Add24', 14)}Add step</button>
        <span class="caption" style="margin-left:12px">Total ${fnDuration(fn).toFixed(1)} s per loop</span>`; break;
    case 'Color cycle':
      body = `<div class="field"><label>Colors (click to change)</label><div class="color-stops">${fn.colors.map((c, i) => `<label class="color-stop" style="background:${c}"><input type="color" class="cc-color" data-i="${i}" value="${c}"></label>`).join('')}
        <button class="btn icon" data-action="ccAdd" style="width:44px;height:44px" title="Add color">${icon('Add24')}</button></div>
        <div class="preview-bar" style="background:linear-gradient(90deg, ${[...fn.colors, fn.colors[0]].join(',')})"></div></div>
        <div class="field" style="max-width:300px"><label>One full cycle: ${fn.duration} s</label><input type="range" min="1" max="30" value="${fn.duration}" data-k="duration" data-num="1"></div>${targets(fn.targets)}
        <p class="caption">Interpolates R, G, B (and W) from each fixture's profile — no color-wheel channel needed.</p>`; break;
    case 'Pulse':
      body = `<div class="row wrap"><div class="field" style="width:220px"><label>Period ${fn.period} s</label><input type="range" min="0.1" max="4" step="0.1" value="${fn.period}" data-k="period" data-num="1"></div>
        <div class="field" style="width:180px"><label>Min ${(fn.min * 100) | 0}%</label><input type="range" min="0" max="1" step="0.05" value="${fn.min}" data-k="min" data-num="1"></div>
        <div class="field" style="width:180px"><label>Max ${(fn.max * 100) | 0}%</label><input type="range" min="0" max="1" step="0.05" value="${fn.max}" data-k="max" data-num="1"></div></div>${targets(fn.targets)}
        <p class="caption">Uses the fixture's Dimmer channel when the profile has one, otherwise scales RGB(W).</p>`; break;
    case 'Flash':
      body = `<div class="field" style="max-width:320px"><label>Scene shown while held</label><select class="input" data-k="fnId">${sceneOpts(fn.fnId)}</select></div>
        <p class="caption">Releases when the button is released, the window loses focus, or the input disconnects.</p>`; break;
    case 'Strobe':
      body = `<div class="infobar warning">${icon('Warning24')}<span class="msg">Strobe can trigger photosensitive epilepsy. Limited to the max rate in Settings.</span></div>
        <div class="field" style="max-width:300px"><label>Rate ${fn.rate} Hz</label><input type="range" min="1" max="10" value="${fn.rate}" data-k="rate" data-num="1"></div>
        <label class="toggle"><input type="checkbox" ${fn.hardware ? 'checked' : ''} data-k="hardware" data-bool="1"><span class="track"></span>Use the fixture's Strobe channel (only offered when the profile has one)</label>${targets(fn.targets)}`; break;
    case 'Fade':
      body = `<div class="row wrap"><div class="field" style="width:220px"><label>From</label><select class="input" data-k="fromId">${sceneOpts(fn.fromId)}</select></div>
        <div class="field" style="width:220px"><label>To</label><select class="input" data-k="toId">${sceneOpts(fn.toId)}</select></div>
        <div class="field" style="width:220px"><label>Duration ${fn.duration} s</label><input type="range" min="0.5" max="30" step="0.5" value="${fn.duration}" data-k="duration" data-num="1"></div></div>`; break;
    default: body = `<p class="muted">${fn.type} editor.</p>`;
  }
  box.innerHTML = head + body;
  $('#feName').onchange = e => { fn.name = e.target.value; setDirty(); RENDER.functions(); };
  $$('[data-k]', box).forEach(el => el.addEventListener(el.type === 'range' ? 'input' : 'change', () => {
    fn[el.dataset.k] = el.dataset.bool ? el.checked : el.dataset.num ? +el.value : el.value; setDirty();
    if (el.type !== 'range') renderFnEditor(); else { const lab = el.previousElementSibling; if (lab) lab.textContent = lab.textContent.replace(/[\d.]+/, (+el.value).toString()); }
  }));
  $$('.st-fn', box).forEach(el => el.onchange = () => { fn.steps[el.dataset.i].fnId = el.value; setDirty(); });
  $$('.st-num', box).forEach(el => el.onchange = () => { fn.steps[el.dataset.i][el.dataset.k] = +el.value; setDirty(); renderFnEditor(); });
  $$('.cc-color', box).forEach(el => el.oninput = () => { fn.colors[el.dataset.i] = el.value; el.parentElement.style.background = el.value; });
  const ids = fn.targets || (fn.steps ? [...new Set(fn.steps.flatMap(s => fnById(s.fnId)?.targets || []))] : fn.fnId ? fnById(fn.fnId)?.targets : ALL) || [];
  renderStage($('#fnStage'), ids, running ? 'Preview running' : 'Press Preview to see it');
}

function newFunctionDialog() {
  const types = ['Chaser', 'Sequence', 'Color cycle', 'Fade', 'Pulse', 'Flash', 'Strobe'];
  const desc = { Chaser: 'Plays scenes one after another', Sequence: 'Steps with their own values', 'Color cycle': 'Smoothly cycles through colors', Fade: 'Crossfades one scene into another',
    Pulse: 'Brightness goes up and down', Flash: 'Shows a scene while held', Strobe: 'Fast on/off flashing' };
  dialog('New function', `<div class="list">${types.map(t => `<div class="list-item" data-newfn="${t}">${icon(FN_ICON[t], 20)}<div><div class="title">${t}</div><div class="caption">${desc[t]}</div></div></div>`).join('')}
    <div class="list-item" data-newfn="Scene">${icon('Color24', 20)}<div><div class="title">Scene</div><div class="caption">A static look (opens Scene Creator)</div></div></div></div>`, [['Cancel']]);
  $$('[data-newfn]', $('#dialogBody')).forEach(el => el.onclick = () => { closeDialog(); createFunction(el.dataset.newfn); });
}
function createFunction(type, opts = {}) {
  if (type === 'Scene') { const s = scene('New scene', '#ffffff', ALL); state.functions.push(s); state.selScene = s.id; go('scenes'); return s; }
  const base = { id: newId('fn'), type, name: `New ${type.toLowerCase()}`, ...opts };
  const defs = {
    Chaser: { runOrder: 'Loop', direction: 'Forward', speed: 1, steps: [S.red, S.blue].map(s => ({ fnId: s.id, fadeIn: 0.2, hold: 0.5, fadeOut: 0.2 })) },
    Sequence: { type: 'Chaser', runOrder: 'Loop', direction: 'Forward', speed: 1, steps: [] },
    'Color cycle': { colors: ['#ff0000', '#00ff00', '#0000ff'], duration: 6, targets: ALL },
    Fade: { fromId: S.blackout.id, toId: S.warm.id, duration: 4 },
    Pulse: { period: 1, min: 0, max: 1, targets: ALL }, Flash: { fnId: S.white.id }, Strobe: { rate: 6, targets: ALL, hardware: false },
  };
  const fn = { ...base, ...defs[type], ...opts };
  state.functions.push(fn); state.selFunction = fn.id; setDirty();
  if (state.page !== 'show') go('functions');
  return fn;
}

/* ---------- Scenes ---------- */
let wheelState = { h: 30, s: 0.75 };
RENDER.scenes = () => {
  const scenes = state.functions.filter(f => f.type === 'Scene');
  if (!fnById(state.selScene)) state.selScene = scenes[0]?.id;
  const s = fnById(state.selScene);
  $('#sceneList').innerHTML = scenes.map(x => `<div class="list-item ${x.id === state.selScene ? 'selected' : ''}" data-sid="${x.id}"><span class="swatch" style="width:20px;height:20px;background:${rgbToHex(x.color.r, x.color.g, x.color.b)}"></span><span>${esc(x.name)}</span></div>`).join('');
  if (!s) return;
  $('#sceneName').value = s.name;
  $('#sceneTargets').innerHTML = state.groups.map(g => `<label class="row" style="padding:4px 0"><input type="checkbox" data-group="${g.id}" ${g.fixtureIds.every(id => s.targets.includes(id)) ? 'checked' : ''}>${icon('Group24', 14)} ${esc(g.name)}</label>`).join('') +
    '<div style="height:8px"></div>' + state.fixtures.map(f => `<label class="row" style="padding:4px 0"><input type="checkbox" data-target="${f.id}" ${s.targets.includes(f.id) ? 'checked' : ''}>${esc(f.name)}</label>`).join('');
  $('#sceneTargetCount').textContent = `${s.targets.length} fixture(s)`;
  // Only show channel controls the targeted profiles actually have (profile-driven UI).
  const types = new Set(s.targets.flatMap(id => channelsOf(state.fixtures.find(f => f.id === id)).map(c => c.type)));
  const slider = (lab, key, val) => `<div class="slider-row"><label><i style="background:${CH_COLORS[lab]}"></i>${lab}</label><input type="range" min="0" max="255" value="${val}" data-ch="${key}"><span class="val">${val}</span></div>`;
  $('#sceneSliders').innerHTML = [types.has('Red') && slider('Red', 'r', s.color.r), types.has('Green') && slider('Green', 'g', s.color.g), types.has('Blue') && slider('Blue', 'b', s.color.b),
    types.has('White') && slider('White', 'w', s.color.w)].filter(Boolean).join('') +
    (types.has('White') ? '' : '<div class="caption">No selected fixture has a White channel.</div>');
  const adv = ['Strobe', 'ColorMacro', 'Speed', 'Mode', 'Generic'].filter(t => types.has(t));
  $('#sceneAdvanced').innerHTML = adv.length ? adv.map(t => `<div class="slider-row"><label><i style="background:${CH_COLORS[t]}"></i>${t}</label><input type="range" min="0" max="255" value="0"><span class="val">0</span></div>`).join('') +
    '<div class="caption">Ranges and meanings come from each profile (e.g. 0–9 = no strobe).</div>' : '<div class="caption">The selected fixtures have no extra channels.</div>';
  $('#sceneBright').value = Math.round(s.bright * 100); $('#sceneBrightVal').textContent = Math.round(s.bright * 100) + '%';
  placeKnob(s);
  renderStage($('#sceneStage'), state.fixtures.map(f => f.id), state.output.enabled ? 'Live — output is enabled' : 'Preview only — output is disabled');
  previewScene(s);
};
function previewScene(s) { state.live = {}; s.targets.forEach(id => setLive(id, sceneValues(s))); }
function drawWheel() {
  const c = $('#wheel'), ctx = c.getContext('2d'), r = c.width / 2;
  const img = ctx.createImageData(c.width, c.height);
  for (let y = 0; y < c.height; y++) for (let x = 0; x < c.width; x++) {
    const dx = x - r, dy = y - r, d = Math.sqrt(dx * dx + dy * dy) / r, i = (y * c.width + x) * 4;
    if (d > 1) { img.data[i + 3] = 0; continue; }
    const [R, G, B] = hsv((Math.atan2(dy, dx) * 180 / Math.PI + 360) % 360, d, 1);
    img.data[i] = R; img.data[i + 1] = G; img.data[i + 2] = B; img.data[i + 3] = 255;
  }
  ctx.putImageData(img, 0, 0);
}
function hsv(h, s, v) { const f = n => { const k = (n + h / 60) % 6; return v - v * s * Math.max(0, Math.min(k, 4 - k, 1)); }; return [f(5) * 255, f(3) * 255, f(1) * 255]; }
function placeKnob(s) {
  const { r, g, b } = s.color, mx = Math.max(r, g, b), mn = Math.min(r, g, b);
  const sat = mx ? (mx - mn) / mx : 0; let h = 0;
  if (mx !== mn) { h = mx === r ? (g - b) / (mx - mn) : mx === g ? 2 + (b - r) / (mx - mn) : 4 + (r - g) / (mx - mn); h = (h * 60 + 360) % 360; }
  const k = $('#wheelKnob'); k.style.left = (110 + Math.cos(h * Math.PI / 180) * sat * 110) + 'px'; k.style.top = (110 + Math.sin(h * Math.PI / 180) * sat * 110) + 'px';
  k.style.background = rgbToHex(r, g, b);
}
function setSceneColor(r, g, b) {
  const s = fnById(state.selScene); if (!s) return;
  const policy = $('#sceneWhitePolicy').selectedIndex;
  [r, g, b] = [r, g, b].map(Math.round);
  if (policy === 0) { const w = Math.min(r, g, b); s.color = { r: r - w, g: g - w, b: b - w, w }; } else s.color = { r, g, b, w: policy === 1 ? 0 : s.color.w };
  setDirty(); RENDER.scenes();
}

/* ---------- Virtual console ---------- */
RENDER.console = renderConsole;
function renderConsole() {
  const c = state.console, page = c.pages.find(p => p.id === c.pageId);
  $('#vcPages').innerHTML = c.pages.map(p => `<button class="tab ${p.id === c.pageId ? 'active' : ''}" data-vcpage="${p.id}">${esc(p.name)}</button>`).join('');
  $('#vcArea').classList.toggle('editing', c.edit);
  $('#view-console').classList.toggle('vc-locked', c.locked);
  $('#vcPalette').style.display = c.edit ? '' : 'none';
  $('#vcEditBtn').disabled = c.locked;
  $('#vcEditBtn').innerHTML = `${icon(c.edit ? 'CheckmarkCircle24' : 'Edit24')}${c.edit ? 'Done editing' : 'Edit layout'}`;
  $('#vcLockBtn').classList.toggle('primary', c.locked);
  $('#vcCanvas').innerHTML = page.widgets.map(w => {
    const style = `left:${w.x}px;top:${w.y}px;width:${w.w}px;height:${w.h}px;--wc:${w.color || 'var(--AccentFillColorDefault)'}`;
    const on = (w.fnId && state.running.has(w.fnId)) || (w.action === 'blackout' && state.blackout);
    const sel = c.sel === w.id ? 'sel' : '';
    if (w.kind === 'slider') {
      const v = w.bind === 'master' ? state.master * 100 : state.speed * 50;
      return `<div class="widget sliderw ${sel}" data-wid="${w.id}" style="${style}"><span class="caption" style="text-align:center">${esc(w.label)}</span>
        <input type="range" min="0" max="100" value="${v}" data-bind="${w.bind}" ${c.edit ? 'disabled' : ''}><span class="caption" style="text-align:center">${w.bind === 'master' ? Math.round(v) + '%' : state.speed.toFixed(2) + '×'}</span></div>`;
    }
    if (w.kind === 'label') return `<div class="widget ${sel}" data-wid="${w.id}" style="${style};background:none;border-style:dashed;font-weight:400;color:var(--TextFillColorSecondary)">${esc(w.label)}</div>`;
    if (w.kind === 'speed') return `<div class="widget btnw ${sel}" data-wid="${w.id}" data-tap="1" style="${style}">${esc(w.label)}<span class="wsub">${state.bpm} BPM</span></div>`;
    const sub = w.kind === 'flash' ? 'hold' : w.fnId ? fnById(w.fnId)?.type : '';
    return `<div class="widget btnw ${on ? 'on' : ''} ${sel}" data-wid="${w.id}" style="${style}">${esc(w.label)}${sub ? `<span class="wsub">${esc(sub)}</span>` : ''}${w.color ? `<span class="wcolor" style="background:${w.color}"></span>` : ''}</div>`;
  }).join('');
  renderVcProps();
}
function renderVcProps() {
  const c = state.console, w = c.pages.flatMap(p => p.widgets).find(x => x.id === c.sel), box = $('#vcProps');
  if (!w || !c.edit) { box.innerHTML = '<span class="caption">Select a widget.</span>'; return; }
  box.innerHTML = `<div class="field"><label>Label</label><input class="input" id="vpLabel" value="${esc(w.label)}"></div>
    ${w.kind === 'button' || w.kind === 'flash' ? `<div class="field"><label>Function</label><select class="input" id="vpFn"><option value="">—</option>${state.functions.map(f => `<option value="${f.id}" ${f.id === w.fnId ? 'selected' : ''}>${esc(f.name)} (${f.type})</option>`).join('')}</select></div>
    <div class="field"><label>Color</label><input type="color" id="vpColor" value="${w.color && w.color.startsWith('#') ? w.color : '#60cdff'}"></div>
    <div class="field"><label>Behavior</label><select class="input" id="vpKind"><option value="button" ${w.kind === 'button' ? 'selected' : ''}>Toggle</option><option value="flash" ${w.kind === 'flash' ? 'selected' : ''}>Flash (while held)</option></select></div>
    <div class="field"><label>Keyboard key</label><input class="input" placeholder="e.g. 1" maxlength="1"></div>` : ''}
    <div class="caption">Size ${w.w}×${w.h} · drag to move (snaps to 16 px)</div>
    <button class="btn small" id="vpDel" style="margin-top:8px">${icon('Delete24', 14)}Delete widget</button>`;
  $('#vpLabel').oninput = e => { w.label = e.target.value; renderConsoleKeepProps(); };
  $('#vpFn') && ($('#vpFn').onchange = e => { w.fnId = e.target.value; renderConsoleKeepProps(); });
  $('#vpColor') && ($('#vpColor').oninput = e => { w.color = e.target.value; renderConsoleKeepProps(); });
  $('#vpKind') && ($('#vpKind').onchange = e => { w.kind = e.target.value; renderConsoleKeepProps(); });
  $('#vpDel').onclick = () => { const p = c.pages.find(p => p.id === c.pageId); p.widgets = p.widgets.filter(x => x !== w); c.sel = null; renderConsole(); };
}
function renderConsoleKeepProps() {
  const focused = document.activeElement?.id;
  const page = state.console.pages.find(p => p.id === state.console.pageId);
  $('#vcCanvas').innerHTML = ''; renderConsole();
  if (focused) $('#' + focused)?.focus();
  void page;
}
let taps = [];
function vcPress(w, down) {
  const c = state.console;
  if (c.edit) return;
  if (w.kind === 'flash') { if (down) startFn(w.fnId); else stopFn(w.fnId); renderConsole(); return; }
  if (!down) return;
  if (w.action === 'blackout') return toggleBlackout();
  if (w.action === 'releaseAll') return releaseAll();
  if (w.kind === 'speed') {
    const t = now(); taps = taps.filter(x => t - x < 3); taps.push(t);
    if (taps.length > 1) { const avg = (taps.at(-1) - taps[0]) / (taps.length - 1); state.bpm = Math.round(60 / avg); $('#bpmText').textContent = `♪ ${state.bpm} BPM`; }
    renderConsole(); return;
  }
  if (w.fnId) { if (state.running.has(w.fnId)) stopFn(w.fnId); else startFn(w.fnId, w.solo); renderConsole(); }
}

/* ---------- Show timeline ---------- */
const beatT = () => 60 / state.show.bpm;
RENDER.show = renderTimeline;
function renderTimeline() {
  const sh = state.show, pps = sh.zoom, width = sh.length * pps;
  const lane = (cls, label, inner) => `<div class="tl-row ${cls}"><div class="tl-label">${label}</div><div class="tl-lane" style="width:${width}px">${inner}</div></div>`;
  let ticks = ''; for (let s = 0; s <= sh.length; s += 5) ticks += `<div class="tick" style="left:${s * pps}px">${fmtTime(s).slice(0, 5)}</div>`;
  let beats = ''; for (let i = 0, t = 0; t <= sh.length; i++, t = i * beatT()) beats += `<div class="beat ${i % 4 === 0 ? 'bar' : ''}" style="left:${t * pps}px"></div>`;
  const tracks = sh.tracks.map(tr => lane('track', `${icon('Flash24', 14)} ${esc(tr.name)}`, tr.items.map(it => {
    const fn = fnById(it.fnId); const col = fn?.type === 'Scene' ? rgbToHex(fn.color.r, fn.color.g, fn.color.b) : { Chaser: '#60cdff', 'Color cycle': '#c58bff', Pulse: '#6ccb5f', Flash: '#ffffff', Strobe: '#fce100', Fade: '#ff9a40' }[fn?.type] || '#aaa';
    const [cr, cg, cb] = hexToRgb(col), lum = (0.299 * cr + 0.587 * cg + 0.114 * cb) / 255;
    return `<div class="tl-item ${sh.sel === it.id ? 'sel' : ''}" data-item="${it.id}" data-track="${tr.id}" style="left:${it.start * pps}px;width:${Math.max(6, it.len * pps)}px;background:${col};color:${lum < 0.5 ? '#fff' : '#000'}">
      ${it.fadeIn ? '<span class="fade in"></span>' : ''}${esc(fn?.name || '?')}${it.fadeOut ? '<span class="fade out"></span>' : ''}</div>`;
  }).join('') + (sh.range && sh.range.track === tr.id ? `<div class="tl-sel-range" style="left:${sh.range.a * pps}px;width:${(sh.range.b - sh.range.a) * pps}px"></div>` : ''))).join('');
  $('#tlGrid').innerHTML = lane('ruler', 'Time', ticks) +
    lane('audio', `${icon('MusicNote224', 14)} first-dance.mp3`, `<canvas class="waveform" id="wave" width="${Math.min(width, 16000)}" height="72"></canvas>`) +
    lane('beats', `♪ ${sh.bpm} BPM`, beats) + tracks + `<div class="playhead" id="playhead"></div>`;
  $('#tlGrid').style.width = (width + 120) + 'px';
  drawWave(); updatePlayhead(); renderTlProps(); renderTlLibrary();
  $('#tlLen').textContent = '/ ' + fmtTime(sh.length).slice(0, 5);
}
function drawWave() {
  const c = $('#wave'); if (!c) return; const ctx = c.getContext('2d'), h = c.height, mid = h / 2;
  ctx.clearRect(0, 0, c.width, h);
  ctx.fillStyle = getComputedStyle(document.documentElement).getPropertyValue('--AccentFillColorDefault').trim() || '#60cdff';
  const pps = state.show.zoom * (c.width / (state.show.length * state.show.zoom));
  for (let x = 0; x < c.width; x += 2) {
    const t = x / pps, beatPhase = (t % beatT()) / beatT();
    const section = t < 15 ? 0.35 : t < 31 ? 0.7 : t < 39 ? 0.5 : t < 63 ? 0.95 : 0.6;
    const v = section * (0.45 + 0.55 * Math.exp(-beatPhase * 5)) * (0.75 + 0.25 * Math.sin(t * 13.7) * Math.sin(t * 3.1));
    ctx.globalAlpha = 0.85; ctx.fillRect(x, mid - v * mid * 0.95, 1.5, v * mid * 1.9);
  }
}
function updatePlayhead() {
  const ph = $('#playhead'); if (!ph) return;
  ph.style.left = (120 + state.show.t * state.show.zoom) + 'px';
  $('#tlTime').textContent = fmtTime(state.show.t);
  const sc = $('#tlScroll'), x = 120 + state.show.t * state.show.zoom;
  if (state.show.playing && (x > sc.scrollLeft + sc.clientWidth - 80 || x < sc.scrollLeft)) sc.scrollLeft = x - 200;
}
function renderTlProps() {
  const sh = state.show, box = $('#tlProps');
  const it = sh.tracks.flatMap(t => t.items).find(i => i.id === sh.sel);
  if (sh.range) {
    box.innerHTML = `<div class="h3">Selected range</div><p class="muted">${fmtTime(sh.range.a)} → ${fmtTime(sh.range.b)} (${(sh.range.b - sh.range.a).toFixed(2)} s)</p>
      <div class="field"><label>New function for this range</label><select class="input" id="rgType">${['Scene', 'Chaser', 'Color cycle', 'Pulse', 'Strobe', 'Fade'].map(t => `<option>${t}</option>`).join('')}</select></div>
      <button class="btn primary" data-action="fnForRange">${icon('Add24')}Create &amp; place</button>
      <div class="caption" style="margin-top:8px">The function is saved to the library and can be reused anywhere.</div>`;
    return;
  }
  if (!it) { box.innerHTML = '<div class="h3">Properties</div><div class="caption">Select an item, or drag across an empty part of a track to select a time range.</div>'; return; }
  const fn = fnById(it.fnId);
  box.innerHTML = `<div class="h3">${esc(fn?.name)} <span class="badge">${fn?.type}</span></div>
    ${[['start', 'Start (s)'], ['len', 'Length (s)'], ['fadeIn', 'Fade in (s)'], ['fadeOut', 'Fade out (s)']].map(([k, l]) => `<div class="row" style="margin-bottom:6px"><span class="grow caption">${l}</span><input class="input tlp" data-k="${k}" type="number" step="0.1" min="0" value="${it[k]}" style="width:110px"></div>`).join('')}
    <div class="row wrap" style="margin-top:8px"><button class="btn small" data-action="tlEditFn">${icon('Edit24', 14)}Edit function</button><button class="btn small" data-action="tlDup">${icon('Copy24', 14)}Duplicate</button><button class="btn small" data-action="tlDel">${icon('Delete24', 14)}Delete</button></div>`;
  $$('.tlp', box).forEach(el => el.onchange = () => { it[el.dataset.k] = Math.max(0, +el.value); setDirty(); renderTimeline(); });
}
function renderTlLibrary() {
  $('#tlLibrary').innerHTML = state.functions.map(f => `<div class="list-item" draggable="true" data-dragfn="${f.id}">${f.type === 'Scene' ? `<span class="swatch" style="width:16px;height:16px;background:${rgbToHex(f.color.r, f.color.g, f.color.b)}"></span>` : icon(FN_ICON[f.type])}<span>${esc(f.name)}</span><span class="meta caption">${f.type}</span></div>`).join('');
}
const snap = t => { const m = $('#tlSnap').value; const q = m === 'Beat' ? beatT() : m === 'Bar' ? beatT() * 4 : m === '0.1 s' ? 0.1 : 0; return q ? Math.round(t / q) * q : t; };

/* ---------- Test & preview ---------- */
RENDER.test = () => {
  const sel = $('#testFixture');
  sel.innerHTML = state.fixtures.map(f => `<option value="${f.id}" ${f.id === state.selFixture ? 'selected' : ''}>${esc(f.name)} — U${f.universe} @${f.address}</option>`).join('');
  const f = state.fixtures.find(x => x.id === sel.value);
  const p = profileById(f.profileId);
  const issues = patchIssues().filter(i => i.f === f);
  $('#testWarnings').innerHTML = [!p.verified && `<span class="badge warn">⚠ Unverified profile</span>`, ...issues.filter(i => i.overlap).map(() => `<span class="badge err">⚠ Address overlap</span>`),
    !state.output.enabled && `<span class="badge">● Output disabled — preview only</span>`].filter(Boolean).join('');
  const vals = state.test?.fixtureId === f.id ? state.test.values : {};
  $('#testFaders').innerHTML = channelsOf(f).map((c, i) => {
    const v = Math.round(vals[c.type] ?? c.def);
    return `<div class="slider-row" style="grid-template-columns:130px 1fr 40px"><label title="DMX ${f.address + i}"><i style="background:${CH_COLORS[c.type]}"></i>${i + 1} ${esc(c.name)}${c.type === 'Generic' ? ' ⚠' : ''}</label>
      <input type="range" min="0" max="255" value="${v}" data-testch="${c.type}"><span class="val">${v}</span></div>`;
  }).join('');
  const mu = $('#monUniverse'); const keep = mu.value || f.universe;
  mu.innerHTML = universes().map(u => `<option value="${u}">Universe ${u}</option>`).join(''); mu.value = keep;
  renderStage($('#testStage'), [f.id], 'Software preview');
  buildMonitor();
};
function buildMonitor() {
  let h = ''; for (let a = 1; a <= 512; a++) h += `<div class="mcell" data-a="${a}"><span class="a">${String(a).padStart(3, '0')}</span><span class="v">0</span></div>`;
  $('#monitor').innerHTML = h;
}
function paintMonitor(fs) {
  const u = +$('#monUniverse').value || 1, buf = universeBuffer(u, fs);
  const f = state.fixtures.find(x => x.id === $('#testFixture').value);
  const cells = $('#monitor').children;
  for (let a = 1; a <= 512; a++) {
    const c = cells[a - 1]; if (!c) return;
    const v = buf[a]; c.lastChild.textContent = v;
    c.classList.toggle('hot', v > 0);
    c.classList.toggle('fp', !!f && f.universe === u && a >= f.address && a < f.address + footprint(f));
  }
}
function testSet(fid, values) { state.test = { fixtureId: fid, values }; RENDER.test(); hydrateIcons(); }

/* ---------- Input / Output ---------- */
const OUT_TYPES = ['None', 'Art-Net', 'sACN (E1.31)', 'Enttec DMX USB Pro', 'Open DMX (FTDI)'];
const outValid = o => o.type === 'Art-Net' ? !!o.ip : o.type === 'sACN (E1.31)' ? true : o.type === 'Enttec DMX USB Pro' ? !!o.com : o.type === 'Open DMX (FTDI)' ? true : false;
RENDER.io = () => {
  const out = state.output;
  $('#ioTable tbody').innerHTML = out.universes.map(o => {
    const valid = outValid(o);
    const st = o.type === 'None' ? '<span class="caption">— not connected —</span>' : !valid ? '<span class="badge warn">⚠ Needs settings</span>' :
      out.enabled ? (o.error ? `<span class="badge err"><span class="dot err"></span>${o.error}</span>` : '<span class="badge ok"><span class="dot ok"></span>Sending · 44 fps</span>') : '<span class="badge"><span class="dot accent"></span>Ready</span>';
    const desc = o.type === 'Art-Net' ? `Art-Net → ${o.ip || '?'} · port-address ${o.port}` : o.type === 'sACN (E1.31)' ? `sACN universe ${o.sacnU || o.u} · ${o.unicast ? 'unicast ' + o.unicast : 'multicast'}` :
      o.type === 'Enttec DMX USB Pro' ? `Enttec DMX USB Pro (${o.com || '?'})` : o.type === 'Open DMX (FTDI)' ? 'Open DMX (FTDI) — best effort timing' : '';
    return `<tr data-u="${o.u}" class="${o.u === out.selUniverse ? 'selected' : ''}"><td><b>Universe ${o.u}</b><div class="caption">${state.fixtures.filter(f => f.universe === o.u).length} fixtures</div></td><td>${desc || '<span class="caption">None</span>'}</td><td>${st}</td>
      <td><button class="btn small" data-action="ioTest" data-u="${o.u}" ${valid ? '' : 'disabled'}>Test</button></td></tr>`;
  }).join('') + `<tr><td colspan="4"><button class="btn small" data-action="ioAddUniverse">${icon('Add24', 14)}Add universe</button></td></tr>`;
  const anyValid = out.universes.some(outValid);
  $('#outSwitch').disabled = !anyValid; $('#outSwitch').checked = out.enabled;
  $('#outSwitchLabel').textContent = out.enabled ? 'On' : 'Off';
  $('#outSwitchCard').classList.toggle('on', out.enabled);
  $('#outSwitchHint').textContent = !anyValid ? 'Configure at least one universe before enabling output.' : out.enabled ? 'Sending DMX to all configured universes.' : 'Output starts disabled every time the app opens.';
  const o = out.universes.find(x => x.u === out.selUniverse);
  const box = $('#ioConfig');
  let cfg = '';
  if (o.type === 'Art-Net') cfg = `<div class="field"><label>Network adapter</label><select class="input" data-o="adapter"><option>Ethernet — 192.168.1.10</option><option>Wi-Fi — 192.168.0.23</option></select></div>
    <div class="row"><div class="field grow"><label>Node IP (or broadcast)</label><input class="input" data-o="ip" value="${esc(o.ip || '')}" placeholder="192.168.1.50"></div>
    <div class="field" style="width:140px"><label>Art-Net universe</label><input class="input" type="number" min="0" max="32767" data-o="port" value="${o.port ?? 0}"></div></div>
    <div class="caption">Art-Net counts universes from 0. Project universe ${o.u} → Art-Net ${o.port ?? 0}.</div>
    <button class="btn" data-action="discover" style="margin-top:12px">${icon('ArrowSync24')}Discover nodes</button><div id="discoverResults" class="list" style="margin-top:8px"></div>`;
  else if (o.type === 'sACN (E1.31)') cfg = `<div class="field"><label>Network adapter</label><select class="input"><option>Ethernet — 192.168.1.10</option></select></div>
    <div class="row"><div class="field" style="width:140px"><label>sACN universe</label><input class="input" type="number" min="1" max="63999" data-o="sacnU" value="${o.sacnU || o.u}"></div>
    <div class="field" style="width:140px"><label>Priority</label><input class="input" type="number" min="0" max="200" value="100"></div></div>
    <div class="field"><label>Unicast IP (empty = multicast 239.255.0.${o.sacnU || o.u})</label><input class="input" data-o="unicast" value="${esc(o.unicast || '')}"></div>
    <div class="caption">Multicast is often unreliable on Wi-Fi — use unicast or wired Ethernet.</div>`;
  else if (o.type === 'Enttec DMX USB Pro') cfg = `<div class="field"><label>Device</label><select class="input" data-o="com"><option value="">— select —</option>${['COM3', 'COM4'].map(cm => `<option ${cm === o.com ? 'selected' : ''}>${cm}</option>`).join('')}</select></div>
    <div class="caption">Serial number EN123456 · firmware 1.44 (read from the device).</div>`;
  else if (o.type === 'Open DMX (FTDI)') cfg = `<div class="infobar warning">${icon('Warning24')}<span class="msg">Open DMX relies on the PC for timing. Lights may flicker under heavy CPU load. Prefer an Enttec Pro or Art-Net node for shows.</span></div>
    <div class="field"><label>Device</label><select class="input"><option>FT232R USB UART (A10K5JX)</option></select></div>`;
  box.innerHTML = `<div class="h2">Universe ${o.u}</div><div class="field"><label>Output type</label><select class="input" id="ioType">${OUT_TYPES.map(t => `<option ${t === o.type ? 'selected' : ''}>${t}</option>`).join('')}</select></div>${cfg}`;
  $('#ioType').onchange = e => { o.type = e.target.value; RENDER.io(); hydrateIcons(); };
  $$('[data-o]', box).forEach(el => el.onchange = () => { o[el.dataset.o] = el.type === 'number' ? +el.value : el.value; RENDER.io(); hydrateIcons(); });
};
function setOutput(on) {
  const out = state.output;
  if (on && !out.universes.some(outValid)) { info('Output cannot be enabled: no universe has a valid output configuration. Open <b>Input / Output</b>.', 'warning'); return; }
  out.enabled = on;
  $('#outDot').className = 'dot ' + (on ? 'ok' : '');
  $('#outText').textContent = on ? 'Output: sending' : 'Output disabled';
  $('#outPill').className = 'badge ' + (on ? 'ok' : '');
  if (state.page === 'io') RENDER.io();
  toast(on ? 'DMX output enabled.' : 'DMX output disabled — lights may hold their last state.');
  hydrateIcons();
}

/* ============================================================== Actions */
function act(a, el) {
  const [name, arg] = a.split(':');
  const f = state.fixtures.find(x => x.id === state.selFixture);
  switch (name) {
    case 'go': return go(arg);
    case 'theme': return setTheme(arg);
    case 'new': return dialog('Unsaved changes', '<p>Save changes to "Wedding show"?</p>', [['Save', 'primary', () => { act('save'); }], ["Don't save", '', () => toast('New empty project (prototype keeps the demo data).')], ['Cancel']]);
    case 'open': return toast('Opens the Windows file picker for *.dmxpro.');
    case 'recent': return dialog('Open recent', '<div class="list">' + ['Wedding show.dmxpro', 'Club Friday.dmxpro', 'School play.dmxpro'].map(n => `<div class="list-item">${icon('FolderOpen24')}<span>${n}</span><span class="meta caption">Documents\\DmxPro</span></div>`).join('') + '</div>', [['Close']]);
    case 'save': state.project.dirty = false; updateTitle(); info(`Saved <b>${state.project.name}.dmxpro</b>.`, 'success'); return;
    case 'saveAs': return toast('Opens the Windows save dialog.');
    case 'exit': return dialog('Exit DMX Pro', '<p>Stop all functions, send the safe frame (blackout), stop output and close?</p>', [['Exit', 'primary', () => toast('Closed (prototype).')], ['Cancel']]);
    case 'undo': case 'redo': return toast(`${name === 'undo' ? 'Undo' : 'Redo'} — every edit is undoable.`);
    case 'copy': return toast('Copied to clipboard (fixtures, functions, scenes, widgets, timeline items).');
    case 'paste': return toast('Pasted as a copy with a new name.');
    case 'duplicate': return state.page === 'functions' ? act('dupFunction') : act('dupFixture');
    case 'delete': return state.page === 'functions' ? act('delFunction') : act('delFixture');
    case 'rename': return toast('F2 renames the selected item in place.');
    case 'todo': return toast('Planned feature.');
    case 'palette': return openPalette();
    case 'fullscreen': $('#window').classList.toggle('fullscreen'); if (!document.fullscreenElement) document.documentElement.requestFullscreen?.().catch(() => {}); else document.exitFullscreen?.(); return;
    case 'closeInfobar': $('#infobar').hidden = true; return;
    case 'blackout': return toggleBlackout();
    case 'releaseAll': return releaseAll();
    case 'stopAll': state.running.clear(); toast('All functions stopped.'); return state.page === 'console' && renderConsole();
    case 'outOn': return setOutput(true);
    case 'outOff': return setOutput(false);

    case 'addFixture': go('fixtures'); return addFixtureDialog();
    case 'dupFixture': if (!f) return; { const d = { ...f, id: newId('f'), name: f.name + ' (copy)' }; d.address = freeAddress(d.universe, footprint(d)) ?? d.address; state.fixtures.push(d); state.selFixture = d.id; setDirty(); go('fixtures'); toast(`Duplicated to address ${d.address}.`); } return;
    case 'delFixture': if (!f) return; return dialog('Delete fixture', `<p>Delete <b>${esc(f.name)}</b>? It is also removed from its groups.</p>`, [['Delete', 'primary', () => {
      state.fixtures = state.fixtures.filter(x => x !== f); state.groups.forEach(g => g.fixtureIds = g.fixtureIds.filter(id => id !== f.id)); state.selFixture = state.fixtures[0]?.id; setDirty(); go('fixtures'); toast('Deleted · Undo (Ctrl+Z)'); }], ['Cancel']]);
    case 'autoAddress': if (!f) return; { const a2 = freeAddress(f.universe, footprint(f), f.id); if (a2) { f.address = a2; setDirty(); RENDER.fixtures(); hydrateIcons(); toast(`Moved to free address ${a2}.`); } } return;
    case 'autoPatch': { let a2 = 1; state.fixtures.filter(x => x.universe === (f?.universe || 1)).forEach(x => { x.address = a2; a2 += footprint(x); }); setDirty(); go('fixtures'); toast('Re-addressed one after another.'); } return;
    case 'addGroup': go('fixtures'); return dialog('Add group', '<div class="field"><label>Group name</label><input class="input" id="gName" value="New group"></div><label class="row"><input type="checkbox" id="gWithSel" checked>Add the selected fixture</label>',
      [['Add', 'primary', () => { state.groups.push({ id: newId('g'), name: $('#gName').value, fixtureIds: $('#gWithSel').checked && f ? [f.id] : [] }); setDirty(); RENDER.fixtures(); hydrateIcons(); }], ['Cancel']]);
    case 'delGroup': state.groups = state.groups.filter(g => g.id !== el.dataset.gid); setDirty(); RENDER.fixtures(); hydrateIcons(); return;
    case 'ungroup': { const g = state.groups.find(x => x.id === el.dataset.gid); g.fixtureIds = g.fixtureIds.filter(id => id !== f.id); setDirty(); RENDER.fixtures(); hydrateIcons(); } return;

    case 'importProfile': go('library'); return dialog('Import fixture profile', `<p>Choose <b>.qxf</b> (QLC+) or <b>.dmxprofile.json</b> files.</p><div class="infobar">${icon('Info24')}<span class="msg">Imported profiles are marked <b>unverified</b>. Test each channel on the real light, then mark it verified.</span></div>`,
      [['Choose files…', 'primary', () => toast('Imported 1 profile: “Stairville LED Par 56” (unverified).')], ['Cancel']]);
    case 'newProfile': go('library'); return profileEditor(null);
    case 'editProfile': return profileEditor(profileById(state.selProfile));
    case 'verifyProfile': { const p = profileById(state.selProfile); p.verified = !p.verified; RENDER.library(); hydrateIcons(); toast(p.verified ? 'Marked as verified.' : 'Marked as unverified.'); } return;

    case 'newFunction': return newFunctionDialog();
    case 'newFn': return createFunction(arg);
    case 'dupFunction': { const fn = fnById(state.selFunction); if (!fn) return; const d = JSON.parse(JSON.stringify(fn)); d.id = newId('fn'); d.name += ' (copy)'; state.functions.push(d); state.selFunction = d.id; setDirty(); go('functions'); toast('Function duplicated.'); } return;
    case 'delFunction': { const fn = fnById(state.selFunction); if (!fn) return; return dialog('Delete function', `<p>Delete <b>${esc(fn.name)}</b>? Console buttons and timeline items using it will show a warning.</p>`, [['Delete', 'primary', () => {
      state.functions = state.functions.filter(x => x !== fn); state.running.delete(fn.id); state.selFunction = state.functions[0]?.id; setDirty(); go('functions'); }], ['Cancel']]); }
    case 'previewFn': { const id = state.selFunction; state.running.has(id) ? stopFn(id) : startFn(id); RENDER.functions(); hydrateIcons(); } return;
    case 'fnToConsole': case 'sceneToConsole': {
      const fn = fnById(name === 'sceneToConsole' ? state.selScene : state.selFunction); const page = state.console.pages[0];
      const y = Math.max(...page.widgets.map(w => w.y + w.h)) + 16;
      page.widgets.push({ id: newId('w'), kind: 'button', label: fn.name, x: 16, y, w: 160, h: 72, fnId: fn.id, color: fn.color ? rgbToHex(fn.color.r, fn.color.g, fn.color.b) : '#60cdff' });
      setDirty(); toast(`Added “${esc(fn.name)}” button to console page “${page.name}”.`); return;
    }
    case 'stepAdd': fnById(state.selFunction).steps.push({ fnId: S.white.id, fadeIn: 0.2, hold: 0.5, fadeOut: 0.2 }); setDirty(); return renderFnEditor();
    case 'stepDel': fnById(state.selFunction).steps.splice(+el.dataset.i, 1); setDirty(); return renderFnEditor();
    case 'stepUp': { const st = fnById(state.selFunction).steps, i = +el.dataset.i; if (i > 0) [st[i - 1], st[i]] = [st[i], st[i - 1]]; setDirty(); return renderFnEditor(); }
    case 'ccAdd': fnById(state.selFunction).colors.push('#ffffff'); return renderFnEditor();
    case 'editScene': state.selScene = el.dataset.sid; return go('scenes');

    case 'newScene': return createFunction('Scene');
    case 'saveScene': state.project.dirty = false; updateTitle(); return toast('Scene saved.');
    case 'saveSceneAs': { const s = fnById(state.selScene); const d = JSON.parse(JSON.stringify(s)); d.id = newId('s'); d.name += ' (copy)'; state.functions.push(d); state.selScene = d.id; RENDER.scenes(); hydrateIcons(); return toast('Saved as a new scene.'); }
    case 'revertScene': return toast('Reverted to the last saved version.');
    case 'starterScenes': return toast('Starter scenes exist: Blackout, Red, Green, Blue, White, Cyan, Magenta, Yellow.');

    case 'vcEdit': state.console.edit = !state.console.edit; state.console.sel = null; renderConsole(); return hydrateIcons();
    case 'vcLock': state.console.locked = !state.console.locked; if (state.console.locked) state.console.edit = false; go('console'); return toast(state.console.locked ? 'Operator mode: editing is locked (F12 to unlock).' : 'Editing unlocked.');
    case 'vcAddPage': state.console.pages.push({ id: newId('p'), name: 'Page ' + (state.console.pages.length + 1), widgets: [] }); state.console.pageId = state.console.pages.at(-1).id; return renderConsole();
    case 'vcAdd': { const page = state.console.pages.find(p => p.id === state.console.pageId); const k = el.dataset.kind;
      const w = { id: newId('w'), kind: k, label: { button: 'Button', flash: 'Flash', slider: 'Level', speed: 'TAP', label: 'Label' }[k], x: 16, y: 320, w: k === 'slider' ? 72 : k === 'label' ? 240 : 128, h: k === 'slider' ? 200 : k === 'label' ? 40 : 72, bind: k === 'slider' ? 'master' : undefined };
      page.widgets.push(w); state.console.sel = w.id; return renderConsole(); }

    case 'tlPlay': { state.show.playing = !state.show.playing; lastTick = now(); $('#tlPlayBtn').innerHTML = icon(state.show.playing ? 'Pause24' : 'Play24'); return; }
    case 'tlStop': state.show.playing = false; state.show.t = 0; $('#tlPlayBtn').innerHTML = icon('Play24'); return updatePlayhead();
    case 'tlAddAudio': return dialog('Add audio track', '<p>Choose an <b>MP3</b> or <b>WAV</b> file. DMX Pro shows its waveform and detects beats for snapping.</p>', [['Choose file…', 'primary', () => toast('Loaded first-dance.mp3 · 03:31 · 120 BPM detected.')], ['Cancel']]);
    case 'tlAddTrack': state.show.tracks.push({ id: newId('t'), name: 'Track ' + (state.show.tracks.length + 1), items: [] }); go('show'); return;
    case 'analyze': return toast('Beats analyzed: 120 BPM, 4/4. Markers added.');
    case 'fnForRange': {
      const r = state.show.range; if (!r) return toast('First drag across a track to select a time range.');
      const type = $('#rgType')?.value || 'Chaser';
      const fn = type === 'Scene' ? (() => { const s = scene('Scene @' + fmtTime(r.a).slice(0, 5), '#ff66cc', ALL); state.functions.push(s); return s; })() : createFunction(type, { name: `${type} @${fmtTime(r.a).slice(0, 5)}` });
      const tr = state.show.tracks.find(t => t.id === r.track);
      const it = { id: newId('i'), fnId: fn.id, start: r.a, len: r.b - r.a, fadeIn: 0, fadeOut: 0 };
      tr.items.push(it); state.show.range = null; state.show.sel = it.id; setDirty(); go('show');
      return toast(`Created “${esc(fn.name)}” (${type}) for ${(it.len).toFixed(2)} s and placed it on ${tr.name}.`);
    }
    case 'tlDel': state.show.tracks.forEach(t => t.items = t.items.filter(i => i.id !== state.show.sel)); state.show.sel = null; setDirty(); return renderTimeline();
    case 'tlDup': { const tr = state.show.tracks.find(t => t.items.some(i => i.id === state.show.sel)); const it = tr.items.find(i => i.id === state.show.sel);
      const d = { ...it, id: newId('i'), start: it.start + it.len }; tr.items.push(d); state.show.sel = d.id; setDirty(); return renderTimeline(); }
    case 'tlEditFn': { const it = state.show.tracks.flatMap(t => t.items).find(i => i.id === state.show.sel); const fn = fnById(it.fnId);
      if (fn.type === 'Scene') { state.selScene = fn.id; return go('scenes'); } state.selFunction = fn.id; return go('functions'); }

    case 'testColor': { const fx = state.fixtures.find(x => x.id === $('#testFixture').value), k = +$('#testIntensity').value * 255;
      const v = { Red: 0, Green: 0, Blue: 0, White: 0, Dimmer: 255, [{ red: 'Red', green: 'Green', blue: 'Blue', white: 'White' }[el.dataset.c]]: k };
      if (el.dataset.c === 'white' && !channelsOf(fx).some(c => c.type === 'White')) { Object.assign(v, { Red: k, Green: k, Blue: k, White: 0 }); toast('No White channel in this profile — testing white via RGB.'); }
      return testSet(fx.id, v); }
    case 'testSequence': { const fx = $('#testFixture').value, k = +$('#testIntensity').value * 255, seq = [[k, 0, 0], [0, k, 0], [0, 0, k], [k, k, k], [0, 0, 0]]; let i = 0;
      const step = () => { if (i >= seq.length) return; const [r, g, b] = seq[i++]; testSet(fx, { Red: r, Green: g, Blue: b, White: 0, Dimmer: 255 }); setTimeout(step, 700); }; return step(); }
    case 'testOff': return testSet($('#testFixture').value, { Red: 0, Green: 0, Blue: 0, White: 0, Dimmer: 0, Strobe: 0 });

    case 'discover': { const box = $('#discoverResults'); if (!box) { go('io'); return; } box.innerHTML = '<span class="caption">Searching (ArtPoll)…</span>';
      setTimeout(() => { box.innerHTML = [['192.168.1.50', 'DMXking eDMX2 PRO', '2 ports'], ['192.168.1.61', 'Generic Art-Net node', '1 port']].map(([ip, n, p]) => `<div class="list-item" data-useip="${ip}">${icon('PlugConnected24')}<div><div class="title">${n}</div><div class="caption">${ip} · ${p}</div></div><span class="meta"><button class="btn small">Use</button></span></div>`).join(''); hydrateIcons(box); }, 700); return; }
    case 'ioTest': return toast(`Universe ${el.dataset.u}: sent a 2-second test frame (all channels 25 %).`);
    case 'ioAddUniverse': { const u = Math.max(...state.output.universes.map(o => o.u)) + 1; state.output.universes.push({ u, type: 'None' }); state.output.selUniverse = u; return RENDER.io(); }

    case 'help': return dialog('Getting started', `<ol style="padding-left:18px; line-height:28px"><li>Add your lights in <b>Fixtures</b> (pick or import a profile).</li><li>Check each light in <b>Test &amp; Preview</b> at low intensity.</li>
      <li>Make looks in <b>Scenes</b>, effects in <b>Functions</b>.</li><li>Put buttons on the <b>Virtual Console</b>, or build a <b>Show Timeline</b> to music.</li><li>Configure <b>Input / Output</b>, then enable output.</li></ol>`, [['Got it', 'primary']]);
    case 'shortcuts': return dialog('Keyboard shortcuts', `<table class="table">${[['Ctrl+N / O / S', 'New / Open / Save'], ['Ctrl+1 … Ctrl+9', 'Go to page'], ['Ctrl+K', 'Search everything'], ['Ctrl+Z / Y', 'Undo / Redo'],
      ['Ctrl+C / V / D', 'Copy / Paste / Duplicate'], ['F2 / Del', 'Rename / Delete'], ['B', 'Blackout'], ['Esc', 'Release all'], ['Space', 'Play / Pause timeline'], ['F11', 'Full screen'], ['F12', 'Operator mode']]
      .map(([k, d]) => `<tr><td><kbd>${k}</kbd></td><td>${d}</td></tr>`).join('')}</table>`, [['Close']]);
    case 'dmxBasics': return dialog('DMX basics', `<p>A <b>universe</b> has 512 channels (addresses 1–512), each 0–255. A light uses a few channels starting at its <b>start address</b>; its <b>profile</b> says what each channel does.</p>
      <p>Network nodes (<b>Art-Net</b>/<b>sACN</b>) or <b>USB interfaces</b> turn the data into the DMX signal on the XLR cable. Daisy-chain lights and put a 120 Ω terminator on the last one.</p>`, [['Close']]);
    case 'about': return dialog('About DMX Pro', '<p><b>DMX Pro 0.1</b> (prototype)<br>A modern DMX lighting controller for RGB/RGBW fixtures.</p><p class="caption">QLC+ fixture definitions: Apache License 2.0. UI: WPF-UI (MIT), CommunityToolkit.Mvvm (MIT).</p>', [['Close']]);
  }
}

/* ============================================================== Command palette */
function paletteEntries() {
  return [
    ...PAGES.map(p => ({ label: p.title, hint: 'Page', sym: p.symbol, run: () => go(p.id) })),
    ...state.fixtures.map(f => ({ label: f.name, hint: `Fixture · U${f.universe} @${f.address}`, sym: 'Lightbulb24', run: () => { state.selFixture = f.id; go('fixtures'); } })),
    ...state.functions.map(f => ({ label: f.name, hint: f.type, sym: FN_ICON[f.type], run: () => { if (f.type === 'Scene') { state.selScene = f.id; go('scenes'); } else { state.selFunction = f.id; go('functions'); } } })),
    ...[['Add fixture', 'addFixture'], ['New function', 'newFunction'], ['Import fixture profile', 'importProfile'], ['Enable DMX output', 'outOn'], ['Blackout', 'blackout'], ['Release all', 'releaseAll'], ['Toggle theme', 'theme:' + (theme() === 'dark' ? 'light' : 'dark')]]
      .map(([l, a]) => ({ label: l, hint: 'Action', sym: 'Flash24', run: () => act(a) })),
  ];
}
let paletteSel = 0;
function openPalette() { $('#paletteHost').classList.add('open'); $('#paletteInput').value = ''; paletteSel = 0; renderPalette(); $('#paletteInput').focus(); }
function renderPalette() {
  const q = $('#paletteInput').value.toLowerCase();
  const res = paletteEntries().filter(e => !q || e.label.toLowerCase().includes(q) || e.hint.toLowerCase().includes(q)).slice(0, 12);
  paletteSel = clamp(paletteSel, 0, res.length - 1);
  $('#paletteResults').innerHTML = res.map((e, i) => `<div class="list-item ${i === paletteSel ? 'selected' : ''}" data-pi="${i}">${icon(e.sym)}<span>${esc(e.label)}</span><span class="meta caption">${esc(e.hint)}</span></div>`).join('') || '<div class="caption">No results</div>';
  $('#paletteResults').onclick = ev => { const it = ev.target.closest('[data-pi]'); if (it) { $('#paletteHost').classList.remove('open'); res[+it.dataset.pi].run(); } };
  $('#paletteInput').onkeydown = ev => {
    if (ev.key === 'ArrowDown') { paletteSel++; renderPalette(); ev.preventDefault(); }
    if (ev.key === 'ArrowUp') { paletteSel--; renderPalette(); ev.preventDefault(); }
    if (ev.key === 'Enter' && res[paletteSel]) { $('#paletteHost').classList.remove('open'); res[paletteSel].run(); }
  };
}

/* ============================================================== Events */
function wire() {
  document.addEventListener('click', e => {
    const a = e.target.closest('[data-action]');
    if (a && !a.disabled) { act(a.dataset.action, a); hydrateIcons(); return; }
    const row = e.target.closest('#fxTable tr[data-fid]'); if (row) { state.selFixture = row.dataset.fid; RENDER.fixtures(); hydrateIcons(); return; }
    const cell = e.target.closest('.addr-cell'); if (cell) { const u = +$('#addrMap').dataset.u, ad = +cell.dataset.addr; const f = state.fixtures.find(x => x.universe === u && ad >= x.address && ad < x.address + footprint(x)); if (f) { state.selFixture = f.id; RENDER.fixtures(); hydrateIcons(); } return; }
    const lp = e.target.closest('[data-pid]'); if (lp) { state.selProfile = lp.dataset.pid; RENDER.library(); hydrateIcons(); return; }
    const fi = e.target.closest('[data-fnid]'); if (fi) { state.selFunction = fi.dataset.fnid; RENDER.functions(); hydrateIcons(); return; }
    const si = e.target.closest('[data-sid]'); if (si && si.closest('#sceneList')) { state.selScene = si.dataset.sid; RENDER.scenes(); hydrateIcons(); return; }
    const vp = e.target.closest('[data-vcpage]'); if (vp) { state.console.pageId = vp.dataset.vcpage; state.console.sel = null; return renderConsole(); }
    const io = e.target.closest('#ioTable tr[data-u]'); if (io) { state.output.selUniverse = +io.dataset.u; RENDER.io(); hydrateIcons(); return; }
    const ip = e.target.closest('[data-useip]'); if (ip) { const o = state.output.universes.find(x => x.u === state.output.selUniverse); o.ip = ip.dataset.useip; RENDER.io(); hydrateIcons(); return toast(`Universe ${o.u} → ${o.ip}`); }
    if (e.target === $('#dialogHost')) closeDialog();
    if (e.target === $('#paletteHost')) $('#paletteHost').classList.remove('open');
  });

  $('#fxSearch').oninput = () => { RENDER.fixtures(); hydrateIcons(); };
  $('#fxUniverseFilter').onchange = () => { RENDER.fixtures(); hydrateIcons(); };
  $('#fxMapUniverse').onchange = () => renderAddrMap(+$('#fxMapUniverse').value, patchIssues());
  $('#libSearch').oninput = () => { RENDER.library(); hydrateIcons(); };
  $('#fnSearch').oninput = () => { RENDER.functions(); hydrateIcons(); };
  $('#fnTypeFilter').onchange = () => { RENDER.functions(); hydrateIcons(); };

  // Scenes
  $('#sceneName').onchange = e => { fnById(state.selScene).name = e.target.value; setDirty(); RENDER.scenes(); };
  $('#sceneTargets').addEventListener('change', e => {
    const s = fnById(state.selScene);
    if (e.target.dataset.target) { const id = e.target.dataset.target; s.targets = e.target.checked ? [...new Set([...s.targets, id])] : s.targets.filter(x => x !== id); }
    if (e.target.dataset.group) { const g = state.groups.find(x => x.id === e.target.dataset.group); s.targets = e.target.checked ? [...new Set([...s.targets, ...g.fixtureIds])] : s.targets.filter(x => !g.fixtureIds.includes(x)); }
    setDirty(); RENDER.scenes(); hydrateIcons();
  });
  $('#sceneSliders').addEventListener('input', e => {
    const s = fnById(state.selScene), k = e.target.dataset.ch; if (!k) return;
    s.color[k] = +e.target.value; e.target.nextElementSibling.textContent = e.target.value; placeKnob(s); previewScene(s); setDirty();
    $$('#sceneList [data-sid]').find(x => x.dataset.sid === s.id).firstElementChild.style.background = rgbToHex(s.color.r, s.color.g, s.color.b);
  });
  $('#sceneAdvanced').addEventListener('input', e => { if (e.target.type === 'range') e.target.nextElementSibling.textContent = e.target.value; });
  $('#sceneBright').oninput = e => { const s = fnById(state.selScene); s.bright = e.target.value / 100; $('#sceneBrightVal').textContent = e.target.value + '%'; previewScene(s); };
  const wheelPick = e => { const r = $('#wheel').getBoundingClientRect(), x = e.clientX - r.left - r.width / 2, y = e.clientY - r.top - r.height / 2;
    const d = Math.min(1, Math.sqrt(x * x + y * y) / (r.width / 2)), h = (Math.atan2(y, x) * 180 / Math.PI + 360) % 360; const [R, G, B] = hsv(h, d, 1); setSceneColor(R, G, B); };
  let wheelDown = false;
  $('#wheel').addEventListener('pointerdown', e => { wheelDown = true; wheelPick(e); });
  window.addEventListener('pointermove', e => { if (wheelDown) wheelPick(e); });
  window.addEventListener('pointerup', () => { wheelDown = false; });
  $('#swatches').innerHTML = ['#ff0000', '#ff8000', '#ffff00', '#00ff00', '#00ffff', '#0000ff', '#ff00ff', '#ffffff', '#ff9a40', '#ffd0a0', '#80ff80', '#40c0ff', '#8040ff', '#ff4080', '#ffe0f0', '#000000']
    .map(c => `<button class="swatch" style="background:${c}" data-swatch="${c}" title="${c}"></button>`).join('');
  $('#swatches').onclick = e => { const c = e.target.dataset.swatch; if (c) setSceneColor(...hexToRgb(c)); };

  // Console: press/hold, drag in edit mode, sliders
  let drag = null;
  $('#vcCanvas').addEventListener('pointerdown', e => {
    const wEl = e.target.closest('.widget'); const c = state.console;
    if (!wEl) { if (c.edit) { c.sel = null; renderConsole(); } return; }
    const w = c.pages.find(p => p.id === c.pageId).widgets.find(x => x.id === wEl.dataset.wid);
    if (c.edit) { c.sel = w.id; drag = { w, dx: e.clientX - w.x, dy: e.clientY - w.y }; renderConsole(); return; }
    if (e.target.type === 'range') return;
    vcPress(w, true);
    const up = () => { vcPress(w, false); window.removeEventListener('pointerup', up); window.removeEventListener('blur', up); };
    window.addEventListener('pointerup', up); window.addEventListener('blur', up); // flash releases on focus loss too
  });
  window.addEventListener('pointermove', e => { if (!drag) return; drag.w.x = Math.max(0, Math.round((e.clientX - drag.dx) / 16) * 16); drag.w.y = Math.max(0, Math.round((e.clientY - drag.dy) / 16) * 16);
    const el = $(`[data-wid="${drag.w.id}"]`); el.style.left = drag.w.x + 'px'; el.style.top = drag.w.y + 'px'; });
  window.addEventListener('pointerup', () => { if (drag) { drag = null; setDirty(); renderVcProps(); } });
  $('#vcCanvas').addEventListener('input', e => {
    if (e.target.dataset.bind === 'master') { state.master = e.target.value / 100; $('#master').value = e.target.value; $('#masterVal').textContent = e.target.value + '%'; e.target.nextElementSibling.textContent = e.target.value + '%'; }
    if (e.target.dataset.bind === 'speed') { state.speed = Math.max(0.1, e.target.value / 50); e.target.nextElementSibling.textContent = state.speed.toFixed(2) + '×'; }
  });

  // Timeline: select range / move items / drop functions
  let tlDrag = null;
  $('#tlGrid').addEventListener('pointerdown', e => {
    const sh = state.show, pps = sh.zoom;
    const item = e.target.closest('.tl-item');
    const lane = e.target.closest('.tl-row.track .tl-lane');
    if (item) {
      const it = sh.tracks.flatMap(t => t.items).find(i => i.id === item.dataset.item);
      sh.sel = it.id; sh.range = null; tlDrag = { mode: 'move', it, x0: e.clientX, s0: it.start }; renderTimeline(); hydrateIcons(); return;
    }
    if (lane) {
      const trId = $$('.tl-row.track').indexOf(lane.parentElement), tr = sh.tracks[trId];
      const t = snap((e.clientX - lane.getBoundingClientRect().left) / pps);
      sh.sel = null; sh.range = { track: tr.id, a: t, b: t }; tlDrag = { mode: 'range', t0: t, left: lane.getBoundingClientRect().left }; return;
    }
    const ruler = e.target.closest('.tl-row.ruler .tl-lane, .tl-row.beats .tl-lane, .tl-row.audio .tl-lane');
    if (ruler) { sh.t = clamp((e.clientX - ruler.getBoundingClientRect().left) / pps, 0, sh.length); updatePlayhead(); }
  });
  window.addEventListener('pointermove', e => {
    if (!tlDrag) return; const sh = state.show, pps = sh.zoom;
    if (tlDrag.mode === 'move') { tlDrag.it.start = Math.max(0, snap(tlDrag.s0 + (e.clientX - tlDrag.x0) / pps)); const el = $(`[data-item="${tlDrag.it.id}"]`); if (el) el.style.left = tlDrag.it.start * pps + 'px'; }
    if (tlDrag.mode === 'range') { const t = clamp(snap((e.clientX - tlDrag.left) / pps), 0, sh.length); sh.range.a = Math.min(tlDrag.t0, t); sh.range.b = Math.max(tlDrag.t0, t); renderTimeline(); hydrateIcons(); }
  });
  window.addEventListener('pointerup', () => {
    if (!tlDrag) return; const sh = state.show;
    if (tlDrag.mode === 'range' && sh.range.b - sh.range.a < 0.05) sh.range = null;
    if (tlDrag.mode === 'move') setDirty();
    tlDrag = null; renderTimeline(); hydrateIcons();
  });
  $('#tlLibrary').addEventListener('dragstart', e => { const id = e.target.closest('[data-dragfn]')?.dataset.dragfn; if (id) e.dataTransfer.setData('text/plain', id); });
  $('#tlGrid').addEventListener('dragover', e => { if (e.target.closest('.tl-row.track')) e.preventDefault(); });
  $('#tlGrid').addEventListener('drop', e => {
    const lane = e.target.closest('.tl-row.track')?.querySelector('.tl-lane'); if (!lane) return; e.preventDefault();
    const tr = state.show.tracks[$$('.tl-row.track').indexOf(lane.parentElement)], fn = fnById(e.dataTransfer.getData('text/plain'));
    const it = { id: newId('i'), fnId: fn.id, start: snap((e.clientX - lane.getBoundingClientRect().left) / state.show.zoom), len: Math.max(2, fnDuration(fn) || 4), fadeIn: 0, fadeOut: 0 };
    tr.items.push(it); state.show.sel = it.id; setDirty(); renderTimeline(); hydrateIcons(); toast(`Placed “${esc(fn.name)}” at ${fmtTime(it.start)}.`);
  });
  $('#tlZoom').oninput = e => { state.show.zoom = +e.target.value; renderTimeline(); hydrateIcons(); };

  // Test page
  $('#testFixture').onchange = e => { state.selFixture = e.target.value; state.test = null; RENDER.test(); hydrateIcons(); };
  $('#testFaders').addEventListener('input', e => {
    const fid = $('#testFixture').value; const t = e.target.dataset.testch; if (!t) return;
    state.test = state.test?.fixtureId === fid ? state.test : { fixtureId: fid, values: {} };
    state.test.values[t] = +e.target.value; e.target.nextElementSibling.textContent = e.target.value;
  });
  $('#monUniverse').onchange = () => {};

  // Output, master, settings
  $('#outSwitch').onchange = e => setOutput(e.target.checked);
  $('#master').oninput = e => { state.master = e.target.value / 100; $('#masterVal').textContent = e.target.value + '%'; };
  $('#setTheme').onchange = e => setTheme(e.target.value);

  // Keyboard
  document.addEventListener('keydown', e => {
    const typing = /INPUT|SELECT|TEXTAREA/.test(document.activeElement?.tagName) && document.activeElement.type !== 'range' && document.activeElement.type !== 'checkbox';
    if (e.key === 'Escape') {
      if ($('#paletteHost').classList.contains('open')) return $('#paletteHost').classList.remove('open');
      if ($('#dialogHost').classList.contains('open')) return closeDialog();
      if (!typing) { if (e.shiftKey) act('stopAll'); else releaseAll(); }
      return;
    }
    if (e.ctrlKey && /^[1-9]$/.test(e.key)) { e.preventDefault(); return go(PAGES[+e.key - 1].id); }
    if (e.ctrlKey && e.key.toLowerCase() === 'k') { e.preventDefault(); return openPalette(); }
    if (e.ctrlKey && e.key.toLowerCase() === 's') { e.preventDefault(); return act(e.shiftKey ? 'saveAs' : 'save'); }
    if (e.ctrlKey && e.key.toLowerCase() === 'n') { e.preventDefault(); return act('new'); }
    if (e.ctrlKey && e.key.toLowerCase() === 'o') { e.preventDefault(); return act('open'); }
    if (e.ctrlKey && e.key.toLowerCase() === 'e') { e.preventDefault(); return setOutput(!e.shiftKey); }
    if (e.ctrlKey && e.key === ',') { e.preventDefault(); return go('settings'); }
    if (e.key === 'F11') { e.preventDefault(); return act('fullscreen'); }
    if (e.key === 'F12') { e.preventDefault(); return act('vcLock'); }
    if (e.key === 'F1') { e.preventDefault(); return act('help'); }
    if (typing) return;
    if (e.key === 'b' || e.key === 'B') return toggleBlackout();
    if (e.key === ' ' && state.page === 'show') { e.preventDefault(); return act('tlPlay'); }
    if (e.key === 'Home' && state.page === 'show') return act('tlStop');
    if (e.key === 'Delete' && state.page === 'show' && state.show.sel) return act('tlDel');
  });
}

/* ============================================================== Boot */
buildMenus();
buildNav();
drawWheel();
wire();
$('#setTheme').value = theme();
updateTitle();
go('fixtures');
hydrateIcons();
requestAnimationFrame(tick);
