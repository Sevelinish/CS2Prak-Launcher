'use strict';


(function () {
    const SNIPERS = new Set(['AWP', 'SSG 08', 'SCAR-20', 'G3SG1']);
    const GRID = 64;                 
    const MIN_FRAMES = 60;           
    const MOVING_PX_S = 40;          
    
    
    const MIN_ROUNDS_PER_CELL = 2;
    
    
    
    const SKIP_OPENING_SECONDS = 5;

    const CT = 1, T = 0;

    

    function median(a) {
        if (!a.length) return 0;
        const b = Float64Array.from(a).sort();
        return b[b.length >> 1];
    }

    
    function ranks(obj, key) {
        const ids = Object.keys(obj);
        const sorted = ids.slice().sort((a, b) => obj[a][key] - obj[b][key]);
        const out = {};
        sorted.forEach((id, i) => {
            out[id] = sorted.length < 2 ? 0.5 : i / (sorted.length - 1);
        });
        return out;
    }

    
    function filterOneOffs(grid, cellRounds) {
        const out = new Float64Array(grid.length);
        for (let i = 0; i < grid.length; i++) {
            if (cellRounds[i] >= MIN_ROUNDS_PER_CELL) out[i] = grid[i];
        }
        return out;
    }

    
    function blur(grid, n, passes) {
        let src = grid;
        for (let p = 0; p < (passes || 3); p++) {
            const tmp = new Float64Array(n * n);
            for (let y = 0; y < n; y++) {
                for (let x = 0; x < n; x++) {
                    let s = 0, c = 0;
                    for (let d = -1; d <= 1; d++) {
                        const xx = x + d;
                        if (xx >= 0 && xx < n) { s += src[y * n + xx]; c++; }
                    }
                    tmp[y * n + x] = s / c;
                }
            }
            const dst = new Float64Array(n * n);
            for (let y = 0; y < n; y++) {
                for (let x = 0; x < n; x++) {
                    let s = 0, c = 0;
                    for (let d = -1; d <= 1; d++) {
                        const yy = y + d;
                        if (yy >= 0 && yy < n) { s += tmp[yy * n + x]; c++; }
                    }
                    dst[y * n + x] = s / c;
                }
            }
            src = dst;
        }
        return src;
    }

    

    
    
    function halftimeRound(D) {
        const RD = D.rounds || [], FR = D.frames;
        const probe = (D.teamA || [])[0];
        if (probe == null || !RD.length) return Infinity;
        const sideAt = ri => {
            const row = FR[RD[ri].freeze];
            const e = row && row[probe];
            return e ? e[4] : null;
        };
        const first = sideAt(0);
        if (first == null) return Infinity;
        for (let ri = 1; ri < RD.length; ri++) {
            const sd = sideAt(ri);
            if (sd != null && sd !== first) return ri;
        }
        return Infinity;
    }

    
    function makeKeep(D, filter) {
        const f = filter || {};
        if (!f.half && !f.buy && !f.outcome) return null;   
        const half = halftimeRound(D);
        const econ = D.econ || {};
        return function keep(ri, r, idx, side) {
            if (f.half === 1 && ri >= half) return false;
            if (f.half === 2 && ri < half) return false;
            if (f.outcome) {
                const won = (r.wside === 'CT') === (side === CT);
                if (f.outcome === 'won' && !won) return false;
                if (f.outcome === 'lost' && won) return false;
            }
            if (f.buy) {
                const ec = econ[ri] || econ[String(ri)];
                const row = ec && (ec[idx] || ec[String(idx)]);
                if (!row) return false;
                const spent = row[4] || 0;
                const kind = spent < 2000 ? 'eco' : (spent < 3900 ? 'force' : 'full');
                if (kind !== f.buy) return false;
            }
            return true;
        };
    }

    function compute(D, filter) {
        const keep = makeKeep(D, filter);
        const N = D.nFrames, FR = D.frames, RD = D.rounds || [], FPS = D.fps || 8;
        const size = D.radarSize || 1024;
        const cell = size / GRID;
        const sniperIdx = new Set();
        (D.weapons || []).forEach((w, i) => { if (SNIPERS.has(w)) sniperIdx.add(i); });

        const acc = { 1: {}, 0: {} };
        const bucket = (side, i) => {
            let b = acc[side][i];
            if (!b) {
                b = acc[side][i] = {
                    n: 0, sx: 0, sy: 0, sniper: 0,
                    pts: [], speeds: [], iso: [], grid: new Float64Array(GRID * GRID),
                    cellRounds: new Uint16Array(GRID * GRID),
                    cellLast: new Int16Array(GRID * GRID).fill(-1),
                    roundCent: [], roundDepth: [], openK: 0, openD: 0, roundsSeen: 0,
                    deaths: 0, kills: 0,
                };
            }
            return b;
        };

        for (let ri = 0; ri < RD.length; ri++) {
            const r = RD[ri];
            const f0 = r.freeze, f1 = Math.min(r.end, N - 1);
            if (f1 <= f0) continue;

            
            const anchor = {};
            for (const side of [CT, T]) {
                const base = [];
                for (let i = 0; i < FR[f0].length; i++) {
                    const e = FR[f0][i];
                    if (e && e[4] === side) base.push(e);
                }
                anchor[side] = base.length
                    ? [base.reduce((s, e) => s + e[0], 0) / base.length,
                       base.reduce((s, e) => s + e[1], 0) / base.length]
                    : null;
            }

            
            
            const fLive = Math.min(f1, f0 + SKIP_OPENING_SECONDS * FPS);

            const rc = {};   
            for (let f = fLive; f < f1; f++) {
                const row = FR[f];
                if (!row) continue;
                const live = [];
                for (let i = 0; i < row.length; i++) {
                    const e = row[i];
                    if (e && e[5]) live.push(i);
                }
                for (const i of live) {
                    const e = row[i], side = e[4];
                    if (side !== CT && side !== T) continue;
                    if (keep && !keep(ri, r, i, side)) continue;
                    const b = bucket(side, i);
                    b.n++; b.sx += e[0]; b.sy += e[1];
                    if (sniperIdx.has(e[7])) b.sniper++;

                    const gx = Math.max(0, Math.min(GRID - 1, (e[0] / cell) | 0));
                    const gy = Math.max(0, Math.min(GRID - 1, (e[1] / cell) | 0));
                    const ci = gy * GRID + gx;
                    b.grid[ci]++;
                    if (b.cellLast[ci] !== r.n) { b.cellLast[ci] = r.n; b.cellRounds[ci]++; }

                    b.pts.push(f, e[0], e[1]);

                    
                    let best = Infinity;
                    for (const j of live) {
                        if (j === i) continue;
                        const o = row[j];
                        if (!o || o[4] !== side) continue;
                        const d = Math.hypot(e[0] - o[0], e[1] - o[1]);
                        if (d < best) best = d;
                    }
                    if (best < Infinity) b.iso.push(best);

                    const a = anchor[side];
                    if (a) {
                        const d = Math.hypot(e[0] - a[0], e[1] - a[1]);
                        const k = side + ':' + i;
                        if (!rc[k]) rc[k] = { side, i, n: 0, sx: 0, sy: 0, maxD: 0 };
                        const c = rc[k];
                        c.n++; c.sx += e[0]; c.sy += e[1];
                        if (d > c.maxD) c.maxD = d;
                    }
                }
            }
            for (const k in rc) {
                const c = rc[k];
                if (c.n < 8) continue;
                const b = bucket(c.side, c.i);
                b.roundCent.push([c.sx / c.n, c.sy / c.n]);
                b.roundDepth.push(c.maxD);       
                b.roundsSeen++;
            }

            
            const ks = (D.kills || []).filter(k => k.f >= f0 && k.f <= r.end);
            if (ks.length) {
                const k = ks.reduce((m, x) => (x.f < m.f ? x : m), ks[0]);
                const ea = k.a != null && FR[k.f] ? FR[k.f][k.a] : null;
                const ev = k.v != null && FR[k.f] ? FR[k.f][k.v] : null;
                if (ea && (ea[4] === CT || ea[4] === T) && (!keep || keep(ri, r, k.a, ea[4])))
                    bucket(ea[4], k.a).openK++;
                if (ev && (ev[4] === CT || ev[4] === T) && (!keep || keep(ri, r, k.v, ev[4])))
                    bucket(ev[4], k.v).openD++;
            }
        }

        
        const roundOf = f => {
            for (let ri2 = 0; ri2 < RD.length; ri2++)
                if (f >= RD[ri2].start && f <= RD[ri2].end) return ri2;
            return -1;
        };
        for (const k of (D.kills || [])) {
            const row = FR[k.f];
            if (!row) continue;
            const ri2 = keep ? roundOf(k.f) : 0;
            if (keep && ri2 < 0) continue;
            const r2 = keep ? RD[ri2] : null;
            const ea = k.a != null ? row[k.a] : null;
            const ev = k.v != null ? row[k.v] : null;
            if (ea && (ea[4] === CT || ea[4] === T) && (!keep || keep(ri2, r2, k.a, ea[4])))
                bucket(ea[4], k.a).kills++;
            if (ev && (ev[4] === CT || ev[4] === T) && (!keep || keep(ri2, r2, k.v, ev[4])))
                bucket(ev[4], k.v).deaths++;
        }

        const out = { 1: {}, 0: {} };
        for (const side of [CT, T]) {
            for (const idx in acc[side]) {
                const b = acc[side][idx];
                if (b.n < MIN_FRAMES) continue;
                const mx = b.sx / b.n, my = b.sy / b.n;

                const dist = [];
                let vxx = 0, vyy = 0, vxy = 0;
                for (let p = 0; p < b.pts.length; p += 3) {
                    const dx = b.pts[p + 1] - mx, dy = b.pts[p + 2] - my;
                    dist.push(Math.hypot(dx, dy));
                    vxx += dx * dx; vyy += dy * dy; vxy += dx * dy;
                }
                vxx /= b.n; vyy /= b.n; vxy /= b.n;

                const sp = [];
                for (let p = 3; p < b.pts.length; p += 3) {
                    if (b.pts[p] === b.pts[p - 3] + 1) {
                        sp.push(Math.hypot(b.pts[p + 1] - b.pts[p - 2],
                                           b.pts[p + 2] - b.pts[p - 1]) * FPS);
                    }
                }

                const cd = [];
                for (let a = 0; a < b.roundCent.length; a++) {
                    for (let c = a + 1; c < b.roundCent.length; c++) {
                        cd.push(Math.hypot(b.roundCent[a][0] - b.roundCent[c][0],
                                           b.roundCent[a][1] - b.roundCent[c][1]));
                    }
                }

                out[side][idx] = {
                    idx: +idx, side, frames: b.n, seconds: b.n / FPS,
                    mx, my, cov: [vxx, vyy, vxy],
                    roam: median(dist),
                    speed: median(sp),
                    moving: sp.length ? sp.filter(v => v > MOVING_PX_S).length / sp.length : 0,
                    spotHold: cd.length ? cd.reduce((s, v) => s + v, 0) / cd.length : 0,
                    sniper: b.n ? b.sniper / b.n : 0,
                    iso: median(b.iso),
                    depth: median(b.roundDepth),
                    openK: b.openK, openD: b.openD, rounds: b.roundsSeen,
                    kills: b.kills, deaths: b.deaths,
                    grid: blur(filterOneOffs(b.grid, b.cellRounds), GRID, 4),
                    cellRounds: b.cellRounds,
                    gridN: GRID,
                };
            }
        }

        for (const side of [CT, T]) assignRoles(out[side], side);
        return out;
    }

    

    
    function assignRoles(m, side) {
        const ids = Object.keys(m);
        if (!ids.length) return;
        const rRoam = ranks(m, 'roam'), rIso = ranks(m, 'iso'),
              rHold = ranks(m, 'spotHold'), rDepth = ranks(m, 'depth'),
              rMov = ranks(m, 'moving'), rSpeed = ranks(m, 'speed');

        for (const id of ids) {
            const v = m[id];
            const openRate = v.rounds ? (v.openK + v.openD) / v.rounds : 0;
            v.rank = {
                roam: rRoam[id], iso: rIso[id], hold: rHold[id],
                depth: rDepth[id], moving: rMov[id], speed: rSpeed[id], openRate,
            };

            
            
            let role, why;
            if (v.sniper >= 0.12) {
                role = 'SNIPER';
                why = [['impact.why.awp', { p: Math.round(v.sniper * 100) }]];
                if (rIso[id] > 0.6) why.push(['impact.why.awpAngles']);
            } else if (side === CT) {
                if (rRoam[id] <= 0.34 && rHold[id] <= 0.34) {
                    role = 'ANCHOR';
                    why = [['impact.why.anchor1'], ['impact.why.anchor2']];
                } else if (rDepth[id] >= 0.66 && rMov[id] >= 0.5) {
                    role = 'AGGRESSOR';
                    why = [['impact.why.aggr1'], ['impact.why.aggr2']];
                } else if (rRoam[id] >= 0.66) {
                    role = 'ROTATOR';
                    why = [['impact.why.rotator']];
                } else {
                    role = 'RIFLER';
                    why = [['impact.why.balanced']];
                }
            } else {
                if (openRate >= 0.28) {
                    role = 'ENTRY';
                    why = [['impact.why.entry', { n: v.openK + v.openD, r: v.rounds }]];
                    if (v.openK > v.openD) {
                        why.push(['impact.why.entryWins', { w: v.openK, l: v.openD }]);
                    }
                } else if (rIso[id] >= 0.75) {
                    role = 'LURKER';
                    why = [['impact.why.lurker']];
                } else if (rIso[id] <= 0.3) {
                    role = 'SUPPORT';
                    why = [['impact.why.support']];
                } else {
                    role = 'RIFLER';
                    why = [['impact.why.balanced']];
                }
            }
            v.role = role;
            v.why = why;
        }
    }

    

    
    function hotspots(v, n) {
        const g = v.grid, G = v.gridN, cells = [];
        let total = 0;
        for (let i = 0; i < g.length; i++) total += g[i];
        if (!total) return [];
        for (let i = 0; i < g.length; i++) if (g[i] > 0) cells.push(i);
        cells.sort((a, b) => g[b] - g[a]);
        const picked = [];
        const minSep = G * 0.14;
        for (const i of cells) {
            const x = i % G, y = (i / G) | 0;
            if (picked.some(p => Math.hypot(p.gx - x, p.gy - y) < minSep)) continue;
            
            let mass = 0;
            const R = Math.max(1, Math.round(G * 0.06));
            for (let dy = -R; dy <= R; dy++) {
                for (let dx = -R; dx <= R; dx++) {
                    const xx = x + dx, yy = y + dy;
                    if (xx < 0 || yy < 0 || xx >= G || yy >= G) continue;
                    if (Math.hypot(dx, dy) <= R) mass += g[yy * G + xx];
                }
            }
            let rounds = 0;
            if (v.cellRounds) {
                for (let dy = -R; dy <= R; dy++) {
                    for (let dx = -R; dx <= R; dx++) {
                        const xx = x + dx, yy = y + dy;
                        if (xx < 0 || yy < 0 || xx >= G || yy >= G) continue;
                        const c = v.cellRounds[yy * G + xx];
                        if (c > rounds) rounds = c;
                    }
                }
            }
            picked.push({ gx: x, gy: y, share: mass / total, rounds });
            if (picked.length >= (n || 3)) break;
        }
        return picked;
    }

    

    
    let CALIB = null;
    fetch('/static/radars/calibration.json')
        .then(r => r.json()).then(j => { CALIB = j; })
        .catch(() => { CALIB = {}; });

    const LANDMARKS = [
        ['bomb_a', 'impact.lm.a'], ['bomb_b', 'impact.lm.b'],
        ['ct_spawn', 'impact.lm.ctSpawn'], ['t_spawn', 'impact.lm.tSpawn'],
    ];
    const NAME_RADIUS = 115;          

    
    function nameSpots(map, spots, cellPx) {
        const out = spots.map(() => null);
        const c = CALIB && CALIB[map];
        if (!c) return out;
        const cand = [];
        spots.forEach((s, i) => {
            const px = (s.gx + 0.5) * cellPx, py = (s.gy + 0.5) * cellPx;
            for (const [key, label] of LANDMARKS) {
                const w = c[key];
                if (!w || typeof w.x !== 'number') continue;
                const lx = (w.x - c.pos_x) / c.scale;
                const ly = (c.pos_y - w.y) / c.scale;
                const d = Math.hypot(px - lx, py - ly);
                if (d < NAME_RADIUS) cand.push({ i, label, d });
            }
        });
        cand.sort((a, b) => a.d - b.d);
        const usedLabel = new Set(), usedSpot = new Set();
        for (const k of cand) {
            if (usedLabel.has(k.label) || usedSpot.has(k.i)) continue;
            usedLabel.add(k.label);
            usedSpot.add(k.i);
            out[k.i] = k.label;
        }
        return out;
    }

    window.Impact = {
        compute, hotspots, nameSpots, LANDMARKS, GRID, CT, T,
        calib: map => (CALIB && CALIB[map]) || null,
    };
})();


(function () {
    const $ = id => document.getElementById(id);
    const panel = $('impPanel');
    if (!panel) return;

    const btn = $('dvImpact'), closeBtn = $('impClose'), seg = $('impSide'),
          roster = $('impRoster'), sub = $('impSub'),
          canvas = $('impCanvas'), scaleBar = $('impScaleBar');
    const ctx = canvas.getContext('2d');

    const SIDE_COL = { 1: [132, 180, 222], 0: [255, 154, 92] };

    
    const SVG = (d, extra) =>
        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" ' +
        'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' + (extra || '') + d + '</svg>';
    
    const FILE_ICON = n =>
        '<i class="imp-ico" style="-webkit-mask-image:url(/static/cs2_icons/' + n +
        '.svg);mask-image:url(/static/cs2_icons/' + n + '.svg)"></i>';

    const ICON = {
        ct: FILE_ICON('ct'),
        t:  FILE_ICON('t'),
        kill:  FILE_ICON('kill'),
        death: FILE_ICON('death'),
        positioning: SVG('<circle cx="12" cy="12" r="7"/><path d="M12 2v3M12 19v3M2 12h3M19 12h3"/>'),
        movement:    SVG('<path d="M4 17h9M4 17l3-3M4 17l3 3"/><path d="M20 7h-9M20 7l-3-3M20 7l-3 3"/>'),
        role:        SVG('<path d="M12 3l2.6 5.4 5.9.8-4.3 4.1 1.1 5.9L12 16.4 6.7 19.2l1.1-5.9L3.5 9.2l5.9-.8z"/>'),
        zones:       SVG('<path d="M3 6l6-3 6 3 6-3v15l-6 3-6-3-6 3z"/><path d="M9 3v15M15 6v15"/>'),
        team:        SVG('<path d="M16 20v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="3.2"/><path d="M22 20v-2a4 4 0 0 0-3-3.8"/>'),
    };

    
    const LVL_COL = { 1: '#eeeeee', 2: '#1ce400', 3: '#1ce400', 4: '#ffc800',
                      5: '#ffc800', 6: '#ffc800', 7: '#ffc800', 8: '#ff6309',
                      9: '#ff6309', 10: '#fe1f00' };

    const T = (k, params) => {
        let s = (window.t ? window.t(k) : k);
        if (params) for (const p in params) s = s.split('{' + p + '}').join(params[p]);
        return s;
    };

    
    function plural(n, key) {
        const f = T(key).split('|');
        if (f.length < 3) return f[0] || '';
        const n10 = n % 10, n100 = n % 100;
        if (n10 === 1 && n100 !== 11) return f[0];
        if (n10 >= 2 && n10 <= 4 && (n100 < 12 || n100 > 14)) return f[1];
        return f[2];
    }
    
    
    
    const RAMP = {
        1: [[0.00, [30, 62, 104], 0.00], [0.20, [46, 96, 156], 0.26],
            [0.45, [96, 158, 214], 0.52], [0.72, [158, 206, 240], 0.74],
            [1.00, [226, 243, 255], 0.90]],
        0: [[0.00, [92, 44, 16], 0.00], [0.20, [148, 72, 24], 0.26],
            [0.45, [216, 118, 44], 0.52], [0.72, [248, 172, 84], 0.74],
            [1.00, [255, 231, 166], 0.90]],
    };

    function rampAt(side, t) {
        const st = RAMP[side];
        for (let i = 1; i < st.length; i++) {
            if (t <= st[i][0]) {
                const [p0, c0, a0] = st[i - 1], [p1, c1, a1] = st[i];
                const k = (t - p0) / (p1 - p0 || 1);
                return [
                    c0[0] + (c1[0] - c0[0]) * k,
                    c0[1] + (c1[1] - c0[1]) * k,
                    c0[2] + (c1[2] - c0[2]) * k,
                    (a0 + (a1 - a0) * k) * 255,
                ];
            }
        }
        const l = st[st.length - 1];
        return [l[1][0], l[1][1], l[1][2], l[2] * 255];
    }
    let D = null, model = null, side = 1, sel = null, radar = null, raf = 0, computeMs = 0;

    function renderSub() {
        const n = (D.rounds || []).length;
        sub.textContent = T('impact.sub') + ' · ' + n + ' ' +
            plural(n, 'impact.roundsF') + ' · ' + computeMs + 'MS';
    }

    
    
    document.addEventListener('langchange', () => {
        const scale = document.querySelector('.imp-scale-lab');
        if (scale) scale.textContent = T('impact.timeSpent');
        const hi = document.querySelectorAll('.imp-scale-lab')[1];
        if (hi) hi.textContent = T('impact.high');
        if (!panel.hidden && model) { renderSub(); setSide(side); }
    });

    function attach(data) {
        D = data; model = null; sel = null; side = 1;
        close();
    }

    
    function sizeCanvas() {
        const stage = canvas.parentElement;
        if (!stage || panel.hidden) return false;
        const cs = getComputedStyle(stage);
        const padX = parseFloat(cs.paddingLeft) + parseFloat(cs.paddingRight);
        const padY = parseFloat(cs.paddingTop) + parseFloat(cs.paddingBottom);
        const legend = stage.querySelector('.imp-scale');
        
        
        
        const gap = parseFloat(cs.rowGap);
        const legendH = legend ? legend.getBoundingClientRect().height + (isFinite(gap) ? gap : 12) : 0;
        const box = stage.getBoundingClientRect();
        
        
        const s = Math.round(Math.max(320, Math.min(920,
            Math.min(box.width - padX, box.height - padY - legendH))));
        if (s === canvas.width) return false;
        canvas.width = s; canvas.height = s;
        heatKey = '';
        return true;
    }

    let stageRO = null;

    

    
    const BANDS = {
        spread:     ['impact.b.tight', 'impact.b.mid', 'impact.b.wide'],
        discipline: ['impact.b.low', 'impact.b.mid', 'impact.b.high'],
        isolation:  ['impact.b.paired', 'impact.b.mid', 'impact.b.lone'],
        depth:      ['impact.b.shallow', 'impact.b.mid', 'impact.b.deep'],
        repos:      ['impact.b.low', 'impact.b.mid', 'impact.b.high'],
        pace:       ['impact.b.slow', 'impact.b.mid', 'impact.b.fast'],
        duels:      ['impact.b.rare', 'impact.b.some', 'impact.b.often'],
    };
    
    const METRIC_FRAC = {
        spread:     v => v.rank.roam,
        discipline: v => 1 - v.rank.hold,
        isolation:  v => v.rank.iso,
        depth:      v => v.rank.depth,
        repos:      v => v.moving,
        
        
        
        
        
        pace:       v => v.rank.speed,
        sniper:     v => Math.min(1, v.sniper * 4),
        duels:      v => Math.min(1, v.rank.openRate),
    };

    const bandOf = (kind, frac) =>
        T(BANDS[kind][frac < 0.34 ? 0 : (frac < 0.67 ? 1 : 2)]);
    const sniperBand = share =>
        T(share >= 0.12 ? 'impact.b.awpMain'
          : (share > 0.02 ? 'impact.b.awpSome' : 'impact.b.awpNo'));

    let heatLayer = null, heatKey = '';
    const view = { z: 1, x: 0, y: 0 };
    const ZMIN = 1, ZMAX = 6;

    function clampView() {
        const W = canvas.width;
        view.z = Math.max(ZMIN, Math.min(ZMAX, view.z));
        const span = W * view.z - W;          
        view.x = Math.min(0, Math.max(-span, view.x));
        view.y = Math.min(0, Math.max(-span, view.y));
        if (view.z === 1) { view.x = 0; view.y = 0; }
    }

    function resetView() { view.z = 1; view.x = 0; view.y = 0; }

    
    let playMask = null, playMaskMap = '';
    const PM = 96;

    function buildPlayMask() {
        if (playMaskMap === D.map && playMask) return;
        playMaskMap = D.map; playMask = null;
        if (!radar || !radar.complete || !radar.naturalWidth) return;
        try {
            const c = document.createElement('canvas');
            c.width = PM; c.height = PM;
            const g = c.getContext('2d');
            g.drawImage(radar, 0, 0, PM, PM);
            const d = g.getImageData(0, 0, PM, PM).data;
            const m = new Uint8Array(PM * PM);
            for (let i = 0; i < PM * PM; i++) m[i] = d[i * 4 + 3] > 40 ? 1 : 0;
            playMask = m;
        } catch (e) {
            playMask = null;      
        }
    }

    
    function onMap(x, y, W) {
        if (!playMask) return true;
        const gx = Math.floor(x / W * PM), gy = Math.floor(y / W * PM);
        if (gx < 0 || gy < 0 || gx >= PM || gy >= PM) return false;
        return playMask[gy * PM + gx] === 1;
    }

    function zoomAt(cx, cy, factor) {
        const before = view.z;
        view.z = Math.max(ZMIN, Math.min(ZMAX, view.z * factor));
        if (view.z === before) return false;
        
        const k = view.z / before;
        view.x = cx - (cx - view.x) * k;
        view.y = cy - (cy - view.y) * k;
        clampView();
        return true;
    }

    function wireZoom() {
        if (canvas.dataset.zoomWired) return;
        canvas.dataset.zoomWired = '1';
        canvas.addEventListener('wheel', e => {
            e.preventDefault();
            const r = canvas.getBoundingClientRect();
            const sx = (e.clientX - r.left) / r.width * canvas.width;
            const sy = (e.clientY - r.top) / r.height * canvas.height;
            if (zoomAt(sx, sy, e.deltaY < 0 ? 1.18 : 1 / 1.18)) draw();
        }, { passive: false });

        let drag = null;
        canvas.addEventListener('mousedown', e => {
            if (view.z <= 1) return;
            drag = { x: e.clientX, y: e.clientY, vx: view.x, vy: view.y };
            canvas.style.cursor = 'grabbing';
        });
        window.addEventListener('mousemove', e => {
            if (!drag) return;
            const r = canvas.getBoundingClientRect();
            const k = canvas.width / r.width;
            view.x = drag.vx + (e.clientX - drag.x) * k;
            view.y = drag.vy + (e.clientY - drag.y) * k;
            clampView(); draw();
        });
        window.addEventListener('mouseup', () => {
            drag = null;
            canvas.style.cursor = view.z > 1 ? 'grab' : '';
        });
        canvas.addEventListener('dblclick', () => { resetView(); draw(); });
    }

    
    const cut = { half: 0, buy: '', outcome: '' };
    const cutCache = {};

    function cutKey() { return cut.half + '|' + cut.buy + '|' + cut.outcome; }

    function modelForCut() {
        const k = cutKey();
        if (!cutCache[k]) cutCache[k] = window.Impact.compute(D, cut);
        return cutCache[k];
    }

    const CUTS = [
        { g: 'half',    opts: [[0, 'impact.cut.all'], [1, 'impact.cut.h1'], [2, 'impact.cut.h2']] },
        { g: 'buy',     opts: [['', 'impact.cut.anyBuy'], ['eco', 'impact.cut.eco'],
                               ['force', 'impact.cut.force'], ['full', 'impact.cut.full']] },
        { g: 'outcome', opts: [['', 'impact.cut.anyRes'], ['won', 'impact.cut.won'],
                               ['lost', 'impact.cut.lost']] },
    ];

    function renderCuts() {
        const bar = document.getElementById('impCuts');
        if (!bar) return;
        bar.innerHTML = CUTS.map(c =>
            '<div class="imp-cut-grp" data-g="' + c.g + '">' +
            c.opts.map(([val, key]) =>
                '<button class="imp-cut' + (cut[c.g] === val ? ' on' : '') + '" type="button" ' +
                'data-g="' + c.g + '" data-v="' + val + '">' + T(key) + '</button>').join('') +
            '</div>').join('') +
            '<span class="imp-cut-n" id="impCutN"></span>';
        bar.querySelectorAll('.imp-cut').forEach(b => {
            b.addEventListener('click', () => {
                const g = b.dataset.g;
                const raw = b.dataset.v;
                cut[g] = g === 'half' ? +raw : raw;
                applyCut();
            });
        });
    }

    
    function applyCut() {
        model = modelForCut();
        renderCuts();
        renderSub();
        setSide(side);
    }

    function open() {
        if (!D) return;
        if (!model) {
            const t0 = performance.now();
            model = modelForCut();
            computeMs = Math.round(performance.now() - t0);
        }
        renderCuts();
        renderSub();
        radar = new Image();
        radar.onload = draw;
        radar.onerror = () => { radar = null; draw(); };
        radar.src = '/static/radars/' + D.map + '.png';
        panel.classList.remove('is-closing');
        panel.hidden = false;
        
        sizeCanvas();
        resetView();
        wireZoom();
        setSide(side);
        
        
        
        if (!stageRO && window.ResizeObserver) {
            stageRO = new ResizeObserver(() => { if (!panel.hidden && sizeCanvas()) draw(); });
            stageRO.observe(canvas.parentElement);
        }
        
        
        
        requestAnimationFrame(() => { if (sizeCanvas()) draw(); });
        setTimeout(() => { if (!panel.hidden && sizeCanvas()) draw(); }, 80);
        document.addEventListener('keydown', onKey);
    }

    window.addEventListener('resize', () => { if (!panel.hidden && sizeCanvas()) draw(); });

    function close() {
        
        panel.classList.add('is-closing');
        setTimeout(() => { panel.hidden = true; panel.classList.remove('is-closing'); }, 160);
        document.removeEventListener('keydown', onKey);
    }

    function onKey(e) {
        if (e.key === 'Escape' && canvas.parentElement &&
            canvas.parentElement.querySelector('.imp-rank.is-open')) { closeRank(); return; }
        if (e.key === 'Escape') { e.stopPropagation(); close(); }
    }

    function setSide(s) {
        side = s;
        seg.querySelectorAll('.imp-seg-btn').forEach(b => {
            const on = +b.dataset.side === s;
            b.classList.toggle('on', on);
            b.setAttribute('aria-checked', on ? 'true' : 'false');
        });
        const m = model[side] || {};
        const ids = Object.keys(m).sort((a, b) => m[b].frames - m[a].frames);
        if (sel == null || !m[sel]) sel = ids[0] != null ? +ids[0] : null;
        scaleBar.style.background = 'linear-gradient(90deg,' + RAMP[side]
            .map(([p, c, a]) => `rgba(${c[0]},${c[1]},${c[2]},${a}) ${(p * 100).toFixed(0)}%`)
            .join(',') + ')';
        renderRoster(ids, m);
        renderProfile();
        draw();
    }

    const nameOf = i => (D.players[i] && D.players[i].name) || '?';

    
    
    const profiles = {};
    const pending = {};
    function faceitProfile(steamid) {
        if (!steamid) return Promise.resolve(null);
        if (profiles[steamid] !== undefined) return Promise.resolve(profiles[steamid]);
        if (pending[steamid]) return pending[steamid];
        pending[steamid] = fetch('/api/faceit/avatar?steamid=' + encodeURIComponent(steamid))
            .then(r => r.json())
            .then(j => (profiles[steamid] = j && j.ok ? j : null))
            .catch(() => (profiles[steamid] = null))
            .finally(() => { delete pending[steamid]; });
        return pending[steamid];
    }
    window.FaceitProfile = { get: faceitProfile, peek: sid => profiles[sid] || null };

    function initials(name) {
        const s = (name || '?').replace(/[^\p{L}\p{N} ]/gu, '').trim();
        if (!s) return '?';
        const parts = s.split(/\s+/);
        return (parts.length > 1 ? parts[0][0] + parts[1][0] : s.slice(0, 2)).toUpperCase();
    }

    
    function byTeam(ids) {
        const a = new Set((D.teamA || []).map(Number));
        const groups = [
            { name: D.teamAName, ids: [] },
            { name: D.teamBName, ids: [] },
        ];
        for (const id of ids) groups[a.has(+id) ? 0 : 1].ids.push(id);
        return groups.filter(g => g.ids.length);
    }

    function addRow(id, v) {
        const p = D.players[+id] || {};
        const row = document.createElement('button');
        row.className = 'imp-row' + (+id === sel ? ' on' : '');
        row.type = 'button';
        row.innerHTML =
            '<span class="imp-av" data-initials="' + initials(p.name) + '"></span>' +
            '<span class="imp-row-name"></span>' +
            '<span class="imp-row-role"></span>' +
            '<span class="imp-row-meta"><b class="imp-lvl" hidden></b>' +
            '<span class="imp-row-time"></span></span>';
        row.querySelector('.imp-row-name').textContent = p.name || '?';
        row.querySelector('.imp-row-role').textContent = T('impact.role.' + v.role);
        
        
        row.querySelector('.imp-row-time').textContent =
            (v.rounds ? Math.round(v.seconds / v.rounds) : 0) + 's';
        row.addEventListener('click', () => {
            sel = +id;
            roster.querySelectorAll('.imp-row').forEach(r => r.classList.remove('on'));
            row.classList.add('on');
            renderProfile(); draw();
        });
        roster.appendChild(row);

        faceitProfile(p.steamid).then(fp => {
            if (!fp || !row.isConnected) return;
            const av = row.querySelector('.imp-av');
            if (fp.url) {
                const im = new Image();
                im.alt = '';
                im.onload = () => { av.classList.add('has-img'); av.appendChild(im); };
                im.src = fp.url;
            }
            if (+id === sel) renderProfile();
            if (fp.lvl) {
                const b = row.querySelector('.imp-lvl');
                b.innerHTML = '<img src="/static/faceit_levels/' + fp.lvl + '.png" alt="">';
                b.title = 'FACEIT ' + fp.lvl + (fp.elo ? ' · ' + fp.elo + ' elo' : '');
                b.hidden = false;
            }
        });
    }

    function renderRoster(ids, m) {
        roster.innerHTML = '';
        const head = document.createElement('div');
        head.className = 'imp-roster-head ' + (side === 1 ? 'is-ct' : 'is-t');
        head.innerHTML = '<i class="imp-side-ico">' + (side === 1 ? ICON.ct : ICON.t) + '</i>' +
            '<span>' + T(side === 1 ? 'impact.ct' : 'impact.t') + '</span>' +
            '<em>' + ids.length + '</em>';
        roster.appendChild(head);

        byTeam(ids).forEach((g, gi) => {
            const th = document.createElement('div');
            th.className = 'imp-team-head';
            th.innerHTML = '<i>' + ICON.team + '</i><span></span><em>' + g.ids.length + '</em>';
            th.querySelector('span').textContent =
                g.name || (T('impact.team') + ' ' + (gi + 1));
            roster.appendChild(th);
            for (const id of g.ids) addRow(id, m[id]);
        });
    }

    function bar(label, value, text, hint) {
        const pct = Math.max(0, Math.min(1, value)) * 100;
        return '<div class="imp-metric" title="' + (hint || '') + '">' +
               '<span class="imp-metric-lab">' + label + '</span>' +
               '<span class="imp-metric-track"><i style="width:' + pct.toFixed(0) + '%"></i></span>' +
               '<span class="imp-metric-val">' + text + '</span></div>';
    }

    
    function group(icon, titleKey, rows) {
        return '<section class="imp-grp">' +
               '<h4 class="imp-grp-t"><i>' + icon + '</i>' + T(titleKey) + '</h4>' +
               '<div class="imp-grp-body">' + rows + '</div></section>';
    }

    
    
    
    function openRank(key) {
        const stage = canvas.parentElement;
        if (!stage || !model || !model[side]) return;
        const get = METRIC_FRAC[key];
        if (!get) return;

        const rows = Object.keys(model[side]).map(id => {
            const v = model[side][id];
            return { id: +id, v, frac: Math.max(0, Math.min(1, get(v) || 0)) };
        }).sort((a, b) => b.frac - a.frac);

        let box = stage.querySelector('.imp-rank');
        if (!box) {
            box = document.createElement('div');
            box.className = 'imp-rank';
            stage.appendChild(box);
            box.addEventListener('click', e => {
                if (e.target.closest('.imp-rank-x') || e.target === box) closeRank();
            });
        }
        box.innerHTML =
            '<div class="imp-rank-head">' +
                '<span class="imp-rank-t">' + T('impact.m.' + key) + '</span>' +
                '<span class="imp-rank-sub">' + T('impact.rank.sub') + '</span>' +
                '<button class="imp-rank-x" type="button" aria-label="close">&#10005;</button>' +
            '</div>' +
            '<div class="imp-rank-list">' +
            rows.map((r, i) => {
                const band = key === 'sniper' ? sniperBand(r.v.sniper) : bandOf(key, r.frac);
                return '<div class="imp-rank-row' + (r.id === sel ? ' is-me' : '') + '" data-id="' + r.id + '">' +
                    '<span class="imp-rank-n">' + (i + 1) + '</span>' +
                    '<span class="imp-rank-name">' + nameOf(r.id) + '</span>' +
                    '<span class="imp-rank-band">' + band + '</span>' +
                    '<span class="imp-rank-bar"><i style="left:' + (r.frac * 100).toFixed(1) + '%"></i></span>' +
                '</div>';
            }).join('') +
            '</div>';

        box.querySelectorAll('.imp-rank-row').forEach(rw => {
            rw.addEventListener('click', () => {
                sel = +rw.dataset.id;
                roster.querySelectorAll('.imp-row').forEach(x => x.classList.remove('on'));
                closeRank();
                renderProfile(); draw();
            });
        });
        
        
        box.classList.remove('is-open');
        void box.offsetWidth;
        box.classList.add('is-open');
    }

    function closeRank() {
        const box = canvas.parentElement && canvas.parentElement.querySelector('.imp-rank');
        if (!box) return;
        box.classList.remove('is-open');
        setTimeout(() => { if (box && !box.classList.contains('is-open')) box.remove(); }, 160);
    }

    function renderProfile() {
        const v = model[side][sel];
        const hud = document.getElementById('impHud');
        const strip = document.getElementById('impMetrics');
        const n = document.getElementById('impCutN');

        if (n) {
            const rounds = v ? v.rounds : 0;
            n.textContent = rounds
                ? rounds + ' ' + plural(rounds, 'impact.roundsLcF')
                : T('impact.cut.empty');
            n.classList.toggle('is-empty', !rounds);
        }
        if (!v) {
            if (hud) hud.innerHTML = '<div class="imp-hud-empty">' + T('impact.cut.empty') + '</div>';
            if (strip) strip.innerHTML = '';
            return;
        }

        const r = v.rank;
        const kd = v.deaths ? (v.kills / v.deaths).toFixed(2) : v.kills.toFixed(2);
        const fp = profiles[(D.players[v.idx] || {}).steamid] || null;
        const av = fp && fp.url
            ? '<span class="imp-hud-av has-img"><img src="' + fp.url + '" alt=""></span>'
            : '<span class="imp-hud-av">' + initials(nameOf(v.idx)) + '</span>';
        const lvl = fp && fp.lvl
            ? '<img class="imp-hud-lvl" src="/static/faceit_levels/' + fp.lvl + '.png" alt="" title="FACEIT ' + fp.lvl + '">'
            : '';
        const elo = fp && fp.elo
            ? '<span class="imp-hud-elo">' + fp.elo.toLocaleString('en-US') + ' ELO</span>' : '';

        if (hud) hud.innerHTML =
            '<div class="imp-hud-id">' + av +
              '<span class="imp-hud-txt">' +
                '<span class="imp-hud-name">' + nameOf(v.idx) + lvl + '</span>' + elo +
                '<span class="imp-hud-role">' + T('impact.role.' + v.role) + '</span>' +
              '</span>' +
            '</div>' +
            '<ul class="imp-hud-why">' +
              v.why.map(w => '<li>' + T(w[0], w[1]) + '</li>').join('') + '</ul>' +
            '<div class="imp-hud-foot">' +
              
              
              '<span>' + T('impact.avgLife', { s: v.rounds ? Math.round(v.seconds / v.rounds) : 0 }) + '</span>' +
              '<span class="imp-kd"><b>' + ICON.kill + v.kills + '</b>' +
                '<b>' + ICON.death + v.deaths + '</b><u>' + kd + '</u></span>' +
            '</div>';

        
        const n5 = Object.keys(model[side]).length;
        const place = frac => Math.max(1, Math.round((1 - frac) * (n5 - 1)) + 1);
        
        
        const cell = (label, frac, verdict, hint, key) => {
            const pos = place(frac);
            return '<div class="imp-cellm" role="button" tabindex="0" data-metric="' + key +
                   '" title="' + (hint || '') + '">' +
                '<span class="imp-cellm-l">' + label + '</span>' +
                '<span class="imp-cellm-v">' + verdict + '</span>' +
                '<span class="imp-cellm-r' + (pos === 1 ? ' is-top' : '') + '">#' + pos + '</span>' +
                '<span class="imp-cellm-bar"><i style="left:' +
                    (Math.max(0, Math.min(1, frac)) * 100).toFixed(1) + '%"></i></span>' +
            '</div>';
        };

        if (strip) strip.innerHTML =
            cell(T('impact.m.spread'), r.roam,
                 bandOf('spread', r.roam), T('impact.m.spreadH'), 'spread') +
            cell(T('impact.m.discipline'), 1 - r.hold,
                 bandOf('discipline', 1 - r.hold), T('impact.m.disciplineH'), 'discipline') +
            cell(T('impact.m.isolation'), r.iso,
                 bandOf('isolation', r.iso), T('impact.m.isolationH'), 'isolation') +
            cell(T('impact.m.depth'), r.depth,
                 bandOf('depth', r.depth), T('impact.m.depthH'), 'depth') +
            cell(T('impact.m.repos'), v.moving,
                 bandOf('repos', v.moving), T('impact.m.reposH'), 'repos') +
            cell(T('impact.m.pace'), r.moving,
                 bandOf('pace', r.moving), T('impact.m.paceH'), 'pace') +
            cell(T('impact.m.sniper'), Math.min(1, v.sniper * 4),
                 sniperBand(v.sniper), T('impact.m.sniperH'), 'sniper') +
            cell(T('impact.m.duels'), Math.min(1, r.openRate),
                 bandOf('duels', Math.min(1, r.openRate)), T('impact.m.duelsH'), 'duels');

        strip.querySelectorAll('.imp-cellm[data-metric]').forEach(el => {
            const go = () => openRank(el.dataset.metric);
            el.addEventListener('click', go);
            el.addEventListener('keydown', e => {
                if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); go(); }
            });
        });
    }

    

    function draw() {
        
        
        if (document.hidden) { paint(); return; }
        if (raf) cancelAnimationFrame(raf);
        raf = requestAnimationFrame(paint);
    }

    
    function token(name, fallback) {
        const v = getComputedStyle(document.documentElement)
            .getPropertyValue(name).trim();
        return v || fallback;
    }
    function tokenRGBA(name, alpha, fallback) {
        const hex = token(name, fallback).replace('#', '');
        if (!/^[0-9a-f]{6}$/i.test(hex)) return fallback;
        return 'rgba(' + parseInt(hex.slice(0, 2), 16) + ','
                       + parseInt(hex.slice(2, 4), 16) + ','
                       + parseInt(hex.slice(4, 6), 16) + ',' + alpha + ')';
    }

    function paint() {
        raf = 0;
        const v = model && model[side] ? model[side][sel] : null;
        const W = canvas.width, H = canvas.height;
        ctx.clearRect(0, 0, W, H);
        ctx.fillStyle = token('--radar-bg', '#14130f');
        ctx.fillRect(0, 0, W, H);

        ctx.save();
        ctx.translate(view.x, view.y);
        ctx.scale(view.z, view.z);

        if (radar) {
            ctx.globalAlpha = 0.78;
            ctx.drawImage(radar, 0, 0, W, H);
            ctx.globalAlpha = 1;
            
            
            ctx.fillStyle = tokenRGBA('--bg', 0.34, 'rgba(14,13,11,0.34)');
            ctx.fillRect(0, 0, W, H);
        }
        if (!v) { ctx.restore(); return; }

        buildPlayMask();
        const S = W / (D.radarSize || 1024);
        const G = v.gridN;
        const c = SIDE_COL[side];

        
        const hk = side + ':' + sel + ':' + cutKey() + ':' + W;
        if (heatKey !== hk) {
            heatKey = hk;
            heatLayer = null;
            const g = v.grid;
            let peak = 0;
            for (let q = 0; q < g.length; q++) if (g[q] > peak) peak = g[q];
            if (peak) {
                const off = document.createElement('canvas');
                off.width = G; off.height = G;
                const octx = off.getContext('2d');
                const img = octx.createImageData(G, G);
                for (let q = 0; q < g.length; q++) {
                    const t = g[q] / peak;
                    if (t <= 0.07) continue;
                    const band = Math.ceil(Math.pow(t, 0.62) * 5) / 5;
                    const [rr, gg, bb, aa] = rampAt(side, band);
                    const p = q * 4;
                    img.data[p] = rr; img.data[p + 1] = gg; img.data[p + 2] = bb; img.data[p + 3] = aa;
                }
                octx.putImageData(img, 0, 0);

                const layer = document.createElement('canvas');
                layer.width = W; layer.height = H;
                const lctx = layer.getContext('2d');
                lctx.imageSmoothingEnabled = true;
                lctx.imageSmoothingQuality = 'high';
                lctx.drawImage(off, 0, 0, W, H);
                if (radar) {
                    lctx.globalCompositeOperation = 'destination-in';
                    lctx.drawImage(radar, 0, 0, W, H);
                    lctx.globalCompositeOperation = 'source-over';
                }
                heatLayer = layer;
            }
        }
        if (!heatLayer) { ctx.restore(); return; }
        ctx.drawImage(heatLayer, 0, 0);

        const col = `rgb(${c[0]},${c[1]},${c[2]})`;

        
        
        
        
        const K = Math.max(1, W / 560) / view.z;

        
        const cal = window.Impact.calib(D.map);
        if (cal) {
            ctx.font = '600 ' + (9 * K).toFixed(1) + 'px "IBM Plex Mono", monospace';
            ctx.textAlign = 'center';
            ctx.textBaseline = 'middle';
            for (const [key, label] of window.Impact.LANDMARKS) {
                const w = cal[key];
                if (!w || typeof w.x !== 'number') continue;
                const lx = (w.x - cal.pos_x) / cal.scale * S;
                const ly = (cal.pos_y - w.y) / cal.scale * S;
                ctx.fillStyle = tokenRGBA('--text-primary', 0.32, 'rgba(236,230,218,0.30)');
                ctx.fillText(T(label), lx, ly);
            }
        }

        
        const spots = window.Impact.hotspots(v, 3);
        const cellPx = (D.radarSize || 1024) / G;
        const spotNames = window.Impact.nameSpots(D.map, spots, cellPx);
        ctx.textBaseline = 'middle';
        spots.forEach((s, i) => {
            const x = (s.gx + 0.5) * cellPx * S, y = (s.gy + 0.5) * cellPx * S;
            const label = (spotNames[i] ? T(spotNames[i]) : T('impact.zone') + ' ' + (i + 1))
                          .toUpperCase();
            const share = (s.share * 100).toFixed(0) + '%';

            ctx.font = '700 ' + (11 * K).toFixed(1) + 'px "Exo 2", "Segoe UI", sans-serif';
            const tw = ctx.measureText(label).width;
            ctx.font = '600 ' + (10 * K).toFixed(1) + 'px "IBM Plex Mono", monospace';
            const sw = ctx.measureText(share).width;
            const bw = Math.max(tw, sw) + 16 * K, bh = 30 * K;

            
            const gap = 12 * K;
            const cands = [
                [x - bw / 2, y - bh - gap],
                [x - bw / 2, y + gap],
                [x + gap, y - bh / 2],
                [x - bw - gap, y - bh / 2],
            ];
            let bx = null, by = null;
            for (const [cx, cy] of cands) {
                const px = Math.max(2, Math.min(W - bw - 2, cx));
                const py = Math.max(2, Math.min(W - bh - 2, cy));
                if (onMap(px + bw / 2, py + bh / 2, W)) { bx = px; by = py; break; }
            }
            if (bx === null) {
                bx = Math.max(2, Math.min(W - bw - 2, x - bw / 2));
                by = Math.max(2, Math.min(W - bh - 2, y - bh - gap));
            }

            ctx.fillStyle = tokenRGBA('--bg', 0.86, 'rgba(14,13,11,0.86)');
            ctx.fillRect(bx, by, bw, bh);
            ctx.fillStyle = col;
            ctx.fillRect(bx, by, 3 * K, bh);

            ctx.textAlign = 'center';
            ctx.fillStyle = token('--paper-100', '#f2ede4');
            ctx.font = '700 ' + (11 * K).toFixed(1) + 'px "Exo 2", "Segoe UI", sans-serif';
            ctx.fillText(label, bx + bw / 2 + 1.5 * K, by + bh * 0.34);
            ctx.fillStyle = col;
            ctx.font = '600 ' + (10 * K).toFixed(1) + 'px "IBM Plex Mono", monospace';
            ctx.fillText(share, bx + bw / 2 + 1.5 * K, by + bh * 0.72);

            
            ctx.strokeStyle = col;
            ctx.lineWidth = 1.2 * K;
            ctx.beginPath();
            ctx.moveTo(x, by > y ? by : by + bh);
            ctx.lineTo(x, y);
            ctx.stroke();
            ctx.beginPath();
            ctx.arc(x, y, 3.5 * K, 0, Math.PI * 2);
            ctx.fillStyle = col;
            ctx.fill();
        });

        
        
        const ax = v.mx * S, ay = v.my * S;

        
        
        ctx.font = '700 ' + (11 * K).toFixed(1) + 'px "Exo 2", "Segoe UI", sans-serif';
        ctx.textAlign = 'center';
        ctx.textBaseline = 'bottom';
        const label = nameOf(v.idx).toUpperCase();
        const w = ctx.measureText(label).width;
        ctx.fillStyle = tokenRGBA('--bg', 0.88, 'rgba(14,13,11,0.88)');
        ctx.fillRect(ax - w / 2 - 5 * K, ay - 32 * K, w + 10 * K, 14 * K);
        ctx.fillStyle = col;
        ctx.fillText(label, ax, ay - 20 * K);

        ctx.restore();
    }


    if (btn) btn.addEventListener('click', open);
    if (closeBtn) closeBtn.addEventListener('click', close);
    seg.addEventListener('click', e => {
        const b = e.target.closest('.imp-seg-btn');
        if (b) setSide(+b.dataset.side);
    });

    window.ImpactPanel = { attach, open, close };
})();


(function () {
    const $ = id => document.getElementById(id);
    const card = $('faceitKeyCard');
    if (!card) return;

    const state = $('fkState'), input = $('fkInput'), save = $('fkSave'),
          clear = $('fkClear'), msg = $('fkMsg');

    const TT = k => (window.t ? window.t(k) : k);
    let connected = false;

    function setState(on) {
        connected = !!on;
        document.dispatchEvent(new CustomEvent('faceitkey', { detail: { set: !!on } }));
        state.textContent = TT(on ? 'fk.connected' : 'fk.notSet');
        state.classList.toggle('on', !!on);
        clear.hidden = !on;
        input.placeholder = TT(on ? 'fk.phSet' : 'fk.ph');
    }

    
    
    document.addEventListener('langchange', () => { setState(connected); msg.textContent = ''; });

    function note(text, bad) {
        msg.textContent = text || '';
        msg.classList.toggle('bad', !!bad);
    }

    fetch('/api/faceit/key').then(r => r.json())
        .then(j => setState(j && j.set))
        .catch(() => setState(false));

    save.addEventListener('click', () => {
        const key = input.value.trim();
        if (!key) { note(TT('fk.needKey'), true); return; }
        save.disabled = true; note(TT('fk.checkingMsg'));
        fetch('/api/faceit/key', {
            method: 'POST', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ key }),
        }).then(r => r.json()).then(j => {
            if (j && j.ok) { input.value = ''; setState(true); note(TT('fk.saved')); }
            else note((j && j.message) || TT('fk.rejected'), true);
        }).catch(() => note(TT('fk.noBackend'), true))
          .finally(() => { save.disabled = false; });
    });

    clear.addEventListener('click', () => {
        fetch('/api/faceit/key', {
            method: 'POST', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ key: '' }),
        }).then(() => { input.value = ''; setState(false); note(TT('fk.removed')); })
          .catch(() => note(TT('fk.noBackend'), true));
    });
})();
