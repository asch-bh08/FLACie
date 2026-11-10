// design-shots.mjs on the unfolded Fold's screen size (1840x2208), so the wide layouts (rail, grids, two panes) are checked too.
// The emulator's size goes back to its own when the run ends. Same arguments as design-shots.mjs; set ANDROID_SERIAL to the emulator.
import { requireEmulator, sh, setAdb } from './adbui.mjs';

const arg = (n, d) => { const i = process.argv.indexOf('--' + n); return i > 0 ? process.argv[i + 1] : d; };
setAdb(arg('adb', ''));
requireEmulator();
sh('wm size 1840x2208');
process.on('exit', () => { try { sh('wm size reset'); } catch { } });
await import('./design-shots.mjs');
