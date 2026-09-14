'use strict';

const MAPS = [
    { id: 'de_mirage',    name: 'MIRAGE',    file: 'Mirage.jpg'    },
    { id: 'de_inferno',   name: 'INFERNO',   file: 'Inferno.jpg'   },
    { id: 'de_dust2',     name: 'DUST2',     file: 'Dust2.jpg'     },
    { id: 'de_nuke',      name: 'NUKE',      file: 'Nuke.jpg'      },
    { id: 'de_ancient',   name: 'ANCIENT',   file: 'Ancient.jpg'   },
    { id: 'de_anubis',    name: 'ANUBIS',    file: 'Anubis.jpg'    },
    { id: 'de_cache',     name: 'CACHE',     file: 'Cache.jpg'     },
    { id: 'de_overpass',  name: 'OVERPASS',  file: 'Overpass.jpg'  },
    
    { id: 'de_vertigo',   name: 'VERTIGO',   file: 'Vertigo.jpg', extra: true },
    { id: 'de_train',     name: 'TRAIN',     file: 'Train.jpg',   extra: true },
];

let selectedMap   = null;
let serverRunning = false;
let heartbeatTimer = null;

const mapsGrid        = document.getElementById('mapsGrid');
const selectedMapName = document.getElementById('selectedMapName');
const launchBtn       = document.getElementById('launchBtn');
const launchInfoText  = document.getElementById('launchInfoText');
const statusDot       = document.getElementById('statusDot');
const statusLabel     = document.getElementById('statusLabel');
const steamIdInput    = document.getElementById('steamId');
const resolveSteamBtn = document.getElementById('resolveSteamBtn');
const loadSkinsBtn    = document.getElementById('loadSkinsBtn');
const skinsStatus     = document.getElementById('skinsStatus');
const toast           = document.getElementById('toast');
const connectBar      = document.getElementById('connectBar');
const connectAddr     = document.getElementById('connectAddr');
const connectCopyBtn  = document.getElementById('connectCopyBtn');
const connectJoinBtn  = document.getElementById('connectJoinBtn');

function buildMaps() {
    const extraGrid = document.getElementById('mapsGridExtra');
    const extraWrap = document.getElementById('mapsExtra');
    let extras = 0;

    MAPS.forEach(map => {
        const card = document.createElement('div');
        card.className = 'map-card';
        card.dataset.id = map.id;
        card.setAttribute('role', 'button');
        card.tabIndex = 0;
        card.setAttribute('aria-pressed', 'false');
        card.setAttribute('aria-label', map.name);
        card.innerHTML = `
            <img class="map-image" src="/maps/${map.file}" alt="${map.name}" draggable="false">
            <div class="map-overlay">
                <div class="map-checkmark">
                    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round">
                        <polyline points="20 6 9 17 4 12"/>
                    </svg>
                </div>
            </div>
            <div class="map-footer">
                <span class="map-name">${map.name}</span>
            </div>
        `;
        card.addEventListener('click', () => selectMap(map, card));
        card.addEventListener('keydown', e => {
            if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); selectMap(map, card); }
        });
        
        
        const img = card.querySelector('.map-image');
        img.addEventListener('error', () => card.classList.add('no-thumb'));

        if (map.extra && extraGrid) { extraGrid.appendChild(card); extras++; }
        else { mapsGrid.appendChild(card); }
    });

    if (extraWrap) {
        extraWrap.hidden = extras === 0;
        const n = extraWrap.querySelector('.maps-extra-n');
        if (n) n.textContent = extras;
    }
}

function selectMap(map, card) {
    document.querySelectorAll('.map-card').forEach(c => {
        c.classList.remove('selected');
        c.setAttribute('aria-pressed', 'false');
    });
    card.classList.add('selected');
    card.setAttribute('aria-pressed', 'true');
    selectedMap = map;
    selectedMapName.textContent = map.name;
    updateServerStatusLine();
    updateLaunchState();
}

function updateLaunchState() {
    if (serverRunning) {
        launchBtn.disabled = false;
        launchBtn.classList.add('running');
        launchBtn.innerHTML = `
            <svg class="btn-icon" width="18" height="18" viewBox="0 0 24 24" fill="currentColor"><rect x="6" y="6" width="12" height="12" rx="1"/></svg>
            STOP SERVER
        `;
        launchInfoText.textContent = `Server running on ${selectedMap ? selectedMap.name : '—'}`;
    } else if (selectedMap) {
        launchBtn.disabled = false;
        launchBtn.classList.remove('running');
        launchBtn.innerHTML = `
            <svg class="btn-icon" width="18" height="18" viewBox="0 0 24 24" fill="currentColor"><path d="M8 5v14l11-7z"/></svg>
            LAUNCH SERVER
        `;
        launchInfoText.textContent = `Ready to launch on ${selectedMap.name}`;
    } else {
        launchBtn.disabled = true;
        launchBtn.classList.remove('running');
        launchBtn.innerHTML = `
            <svg class="btn-icon" width="18" height="18" viewBox="0 0 24 24" fill="currentColor"><path d="M8 5v14l11-7z"/></svg>
            LAUNCH SERVER
        `;
        launchInfoText.textContent = 'Select a map to launch the server';
    }
}

launchBtn.addEventListener('click', async () => {
    if (serverRunning) {
        launchBtn.disabled = true;
        try {
            const res  = await fetch('/stop', { method: 'POST' });
            const data = await res.json();
            if (data.ok) {
                serverRunning = false;
                setStatus(false);
                hideConnectBar();
                showToast('Server stopped', 'error');
            } else {
                showToast(data.message || 'Failed to stop server', 'error');
            }
        } catch {
            showToast('Could not reach backend', 'error');
        }
        updateLaunchState();
    } else {
        if (!selectedMap) return;
        launchBtn.disabled = true;
        launchInfoText.textContent = 'Starting server…';
        try {
            const res  = await fetch('/launch', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ map: selectedMap.id }),
            });
            const data = await res.json();
            if (data.ok) {
                serverRunning = true;
                setStatus(true);
                showConnectBar();
                showToast(`Launched on ${selectedMap.name}`, 'success');
                (data.health || []).filter(h => h.repaired)
                    .forEach(h => showToast(h.message, 'success'));
                showOutdatedPlugins(data.outdated);
            } else {
                showToast(data.message || 'Failed to launch server', 'error');
            }
        } catch {
            showToast('Could not reach backend', 'error');
        }
        updateLaunchState();
    }
});

function setStatus(online) {
    if (statusDot) statusDot.classList.toggle('online', online);
    if (statusLabel) {
        statusLabel.classList.toggle('online', online);
        statusLabel.textContent = online ? 'ONLINE' : 'OFFLINE';
    }
    updateServerStatusLine();
}

function updateServerStatusLine() {
    const dot = document.getElementById('serverLineDot');
    const st  = document.getElementById('serverLineStatus');
    const mp  = document.getElementById('serverLineMap');
    if (!dot) return;
    dot.classList.toggle('online', serverRunning);
    st.classList.toggle('online', serverRunning);
    st.textContent = serverRunning ? 'ONLINE' : 'OFFLINE';
    mp.textContent = selectedMap ? selectedMap.name : '—';
}

let _connectStr = '';

async function showConnectBar() {
    try {
        const d = await fetch('/api/server/connect-info').then(r => r.json());
        _connectStr = `connect ${d.ip}:${d.port}`;
        connectAddr.textContent = _connectStr;
    } catch {
        _connectStr = 'connect ?:27015';
        connectAddr.textContent = _connectStr;
    }
    connectBar.style.display = '';
}

function hideConnectBar() {
    connectBar.style.display = 'none';
    _connectStr = '';
}

connectCopyBtn.addEventListener('click', () => {
    if (!_connectStr) return;
    navigator.clipboard.writeText(_connectStr).then(() => {
        showToast('Connect command copied!', 'success');
    }).catch(() => {
        showToast('Copy failed', 'error');
    });
});

connectJoinBtn.addEventListener('click', () => {
    if (!_connectStr) return;
    const addr = _connectStr.replace('connect ', '');
    window.location.href = `steam://connect/${addr}`;
});

function validateSteamId(id) {
    return /^7656119\d{10}$/.test(id);
}

steamIdInput.addEventListener('input', () => {
    const val = steamIdInput.value.trim();
    if (!val) {
        steamIdInput.className = 'input-field';
        skinsStatus.textContent = '';
        skinsStatus.className = 'skins-status';
    } else if (validateSteamId(val)) {
        steamIdInput.className = 'input-field valid';
        skinsStatus.textContent = '';
        skinsStatus.className = 'skins-status';
    } else {
        
        steamIdInput.className = 'input-field';
        skinsStatus.textContent = 'Profile link detected — click APPLY to convert it to your SteamID64.';
        skinsStatus.className = 'skins-status';
    }
});

async function resolveSteamId() {
    const val = steamIdInput.value.trim();
    if (!val) { showToast('Paste your SteamID64 or profile link first', 'error'); return false; }
    if (validateSteamId(val)) return true;  
    resolveSteamBtn.disabled = true;
    skinsStatus.textContent = 'Resolving…';
    skinsStatus.className = 'skins-status';
    try {
        const res = await fetch('/api/resolve-steamid', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ input: val }),
        });
        const data = await res.json();
        if (data.ok) {
            steamIdInput.value = data.steamid;
            steamIdInput.dispatchEvent(new Event('input'));
            localStorage.setItem('cs2prak_steamid', data.steamid);
            showToast('SteamID64 resolved', 'success');
            resolveSteamBtn.disabled = false;
            return true;
        }
        skinsStatus.textContent = data.message || 'Could not resolve SteamID64';
        skinsStatus.className = 'skins-status error';
        showToast(data.message || 'Could not resolve', 'error');
    } catch {
        showToast('Could not reach backend', 'error');
    }
    resolveSteamBtn.disabled = false;
    return false;
}

resolveSteamBtn.addEventListener('click', resolveSteamId);
steamIdInput.addEventListener('keydown', e => {
    if (e.key === 'Enter' && !validateSteamId(steamIdInput.value.trim())) {
        e.preventDefault();
        resolveSteamId();
    }
});

loadSkinsBtn.addEventListener('click', async () => {
    let val = steamIdInput.value.trim();
    if (!val) { showToast('Enter your Steam ID or profile link first', 'error'); return; }
    if (!validateSteamId(val)) {
        
        const ok = await resolveSteamId();
        if (!ok) return;
        val = steamIdInput.value.trim();
    }
    window.openSkinPicker(val);
});

const importHltvBtn = document.getElementById('importHltvBtn');
importHltvBtn?.addEventListener('click', async () => {
    let val = steamIdInput.value.trim();
    if (!val) { showToast('Enter your Steam ID or profile link first', 'error'); return; }
    if (!validateSteamId(val)) {
        const ok = await resolveSteamId();
        if (!ok) return;
        val = steamIdInput.value.trim();
    }
    window.openHltvImport(val);
});

let toastTimer = null;
function showToast(msg, type = '') {
    clearTimeout(toastTimer);
    toast.textContent = msg;
    toast.className = 'toast show' + (type ? ' ' + type : '');
    toastTimer = setTimeout(() => toast.classList.remove('show'), 2800);
}

const HEARTBEAT_MS = 30_000;

function _sendHeartbeat() {
    fetch('/heartbeat', { method: 'POST' }).catch(() => {});
}

function startHeartbeat() {
    if (heartbeatTimer) return;
    _sendHeartbeat();
    heartbeatTimer = setInterval(_sendHeartbeat, HEARTBEAT_MS);
}

function stopHeartbeat() {
    if (heartbeatTimer) { clearInterval(heartbeatTimer); heartbeatTimer = null; }
}

const STATUS_POLL_MS = 4000;
let statusPollTimer = null;

async function _syncServerStatus() {
    try {
        const data = await fetch('/status').then(r => r.json());
        if (data.running === serverRunning) return;   
        serverRunning = data.running;
        setStatus(data.running);
        if (data.running) {
            showConnectBar();
        } else {
            hideConnectBar();
            showToast('Server stopped', 'error');
        }
        updateLaunchState();
    } catch {  }
}

function startStatusPoll() {
    if (!statusPollTimer) statusPollTimer = setInterval(_syncServerStatus, STATUS_POLL_MS);
}
function stopStatusPoll() {
    if (statusPollTimer) { clearInterval(statusPollTimer); statusPollTimer = null; }
}

function _updatePerfMode() {
    document.body.classList.toggle('low-power', document.hidden || !document.hasFocus());
}
window.addEventListener('blur',  _updatePerfMode);
window.addEventListener('focus', _updatePerfMode);

document.addEventListener('visibilitychange', () => {
    _updatePerfMode();
    if (document.hidden) {
        stopHeartbeat();
        stopUpdPoll();
        stopStatusPoll();
    } else {
        startHeartbeat();
        startStatusPoll();
        _syncServerStatus();   
        if (document.getElementById('updBackdrop').classList.contains('open') &&
            !_updPollTimer && updStatusLabel.textContent === 'RUNNING…') {
            _updPollTimer = setInterval(pollUpdateStatus, 1000);
        }
    }
});

const _tabBtns  = document.querySelectorAll('.tab-btn');
const tabsTrack = document.getElementById('tabsTrack');
let _activeTab  = 0;
let _pluginsLoaded = false;
let skinsReady = false, skinsDetail = { server: false, weaponpaints: false };
let recBindsSeen = localStorage.getItem('cs2prak_recbinds_seen') === '1';

const _demoDdToggle = document.getElementById('demoDdToggle');
const _demoDdMenu   = document.getElementById('demoDdMenu');

_tabBtns.forEach(btn => {
    btn.addEventListener('click', () => {
        if (btn.disabled) return;
        const next = parseInt(btn.dataset.tab);
        if (isNaN(next)) return;
        if (_demoDdMenu) _demoDdMenu.hidden = true;
        if (next === 1 && !skinsReady) {
            checkSkinsReady().then(ready => ready ? btn.click() : showSkinsLock());
            return;
        }
        
        
        if ((next === 4 || next === 8 || next === 9) && !faceitReady) {
            showFaceitLock(next); return;
        }
        if (next === _activeTab) { syncDemoDd(next); return; }
        _activeTab = next;
        tabsTrack.style.transform = `translateX(-${(100 / 10) * next}%)`;
        _tabBtns.forEach(b => b.classList.remove('active'));
        btn.classList.add('active');
        syncDemoDd(next);
        
        document.querySelector('.footer').style.display = (next === 4 || next === 8 || next === 9) ? 'none' : '';
        if (next === 2 && !_pluginsLoaded) {
            _pluginsLoaded = true;
            loadPlugins();
        }
        if (next === 4 && window.initDemo) window.initDemo();
        if (next === 5) { checkServerInstalled(); checkSkinsReady(); }
        if (next === 6) initHighlights();
        if (next === 2) checkSkinsReady();
        if (next === 8 && window.initStatistics) window.initStatistics();
        if (next === 9 && window.initAdvanced) window.initAdvanced();
        if ((next === 3 || next === 4) && !recBindsSeen) {
            recBindsSeen = true;
            localStorage.setItem('cs2prak_recbinds_seen', '1');
            showRecBinds();
        }
    });
});

function syncDemoDd(active) {
    if (_demoDdToggle) _demoDdToggle.classList.toggle('active', active === 4 || active === 8 || active === 9);
}

function updateSkinsLock() {
    const b = document.querySelector('.tab-btn[data-tab="1"]');
    if (b) b.classList.toggle('locked', !skinsReady);
}
function checkSkinsReady() {
    return fetch('/api/skins/ready').then(r => r.json()).then(d => {
        if (!d) return skinsReady;
        skinsReady = !!d.ready; skinsDetail = d;
        updateSkinsLock();
        return skinsReady;
    }).catch(() => skinsReady);
}
const _slBd = document.getElementById('skinsLockBackdrop');
let faceitReady = false;
let _flockWanted = 4;
const _flBd = document.getElementById('faceitLockBackdrop');

function refreshFaceitReady() {
    return fetch('/api/faceit/key').then(r => r.json())
        .then(j => (faceitReady = !!(j && j.set)))
        .catch(() => faceitReady);
}
refreshFaceitReady();

function showFaceitLock(wanted) {
    _flockWanted = wanted || 4;
    const msg = document.getElementById('flockMsg');
    if (msg) msg.textContent = '';
    if (_flBd) _flBd.classList.add('open');
    const inp = document.getElementById('flockInput');
    if (inp) setTimeout(() => inp.focus(), 40);
}

(function () {
    const close = document.getElementById('flockClose');
    const save = document.getElementById('flockSave');
    const inp = document.getElementById('flockInput');
    const msg = document.getElementById('flockMsg');
    if (!save) return;

    const note = (text, bad) => {
        if (!msg) return;
        msg.textContent = text || '';
        msg.classList.toggle('bad', !!bad);
    };

    const submit = () => {
        const key = (inp.value || '').trim();
        if (!key) { note(t('fk.needKey'), true); return; }
        save.disabled = true; note(t('fk.checkingMsg'));
        fetch('/api/faceit/key', {
            method: 'POST', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ key }),
        }).then(r => r.json()).then(j => {
            if (j && j.ok) {
                faceitReady = true;
                inp.value = '';
                if (_flBd) _flBd.classList.remove('open');
                const btn = document.querySelector('.tab-btn[data-tab="' + _flockWanted + '"]');
                if (btn) btn.click();
            } else {
                note((j && j.message) || t('fk.rejected'), true);
            }
        }).catch(() => note(t('fk.noBackend'), true))
          .finally(() => { save.disabled = false; });
    };

    save.addEventListener('click', submit);
    inp && inp.addEventListener('keydown', e => { if (e.key === 'Enter') submit(); });
    close && close.addEventListener('click', () => _flBd && _flBd.classList.remove('open'));
    _flBd && _flBd.addEventListener('click', e => {
        if (e.target === _flBd) _flBd.classList.remove('open');
    });
    
    document.addEventListener('faceitkey', e => { faceitReady = !!(e.detail && e.detail.set); });
})();

function showSkinsLock() {
    const go = document.getElementById('skinsLockGo');
    if (go) go.textContent = skinsDetail.server ? t('skinsLock.plugins') : t('skinsLock.download');
    if (_slBd) _slBd.classList.add('open');
}
(function () {
    const close = document.getElementById('skinsLockClose');
    const go = document.getElementById('skinsLockGo');
    close && close.addEventListener('click', () => _slBd && _slBd.classList.remove('open'));
    go && go.addEventListener('click', () => {
        if (_slBd) _slBd.classList.remove('open');
        if (!skinsDetail.server) {
            const t5 = document.querySelector('.tab-btn[data-tab="5"]'); if (t5) t5.click();
            const cb = document.getElementById('useExistingBtn'); if (cb && !cb.disabled) cb.click();
        } else {
            const t2 = document.querySelector('.tab-btn[data-tab="2"]'); if (t2) t2.click();
        }
    });
})();
checkSkinsReady();

function showRecBinds() {
    if (window.openPracticeBinds) window.openPracticeBinds();
}

function showBetaNotice(anchor) {
    if (document.getElementById('betaNotice')) return;
    const card = document.createElement('div');
    card.id = 'betaNotice';
    card.className = 'tour-tip tip-bottom beta-pop';

    const arrow = document.createElement('div'); arrow.className = 'tour-arrow';
    const head = document.createElement('div'); head.className = 'tour-tip-head';
    const title = document.createElement('span'); title.className = 'tour-tip-title';
    title.textContent = t('tab.betaTitle');
    const tag = document.createElement('span'); tag.className = 'tour-step-count'; tag.textContent = t('tab.beta');
    head.append(title, tag);
    const text = document.createElement('div'); text.className = 'tour-tip-text';
    text.textContent = t('tab.betaNotice');
    const btns = document.createElement('div'); btns.className = 'tour-tip-btns';
    const spacer = document.createElement('span'); spacer.className = 'tour-btn-spacer';
    const ok = document.createElement('button'); ok.className = 'tour-btn tour-next';
    ok.textContent = t('tab.betaOk');
    btns.append(spacer, ok);
    card.append(arrow, head, text, btns);
    document.body.appendChild(card);

    const place = () => {
        const r = anchor.getBoundingClientRect(), w = 320;
        let left = r.left + r.width / 2 - w / 2;
        left = Math.max(14, Math.min(left, window.innerWidth - w - 14));
        card.style.left = left + 'px';
        card.style.top = (r.bottom + 12) + 'px';
        let ax = r.left + r.width / 2 - left;
        arrow.style.left = Math.max(18, Math.min(ax, w - 18)) + 'px';
    };
    place();
    const dismiss = () => {
        card.remove();
        window.removeEventListener('resize', place);
        try { localStorage.setItem('cs2prak_beta_seen', '1'); } catch (e) {}
    };
    ok.addEventListener('click', dismiss);
    window.addEventListener('resize', place);
    requestAnimationFrame(() => card.classList.add('show'));
}

if (_demoDdToggle) {
    _demoDdToggle.addEventListener('click', e => {
        e.stopPropagation();
        if (_demoDdMenu) _demoDdMenu.hidden = !_demoDdMenu.hidden;
        let seen = '0';
        try { seen = localStorage.getItem('cs2prak_beta_seen') || '0'; } catch (e) {}
        if (_demoDdMenu && !_demoDdMenu.hidden && seen !== '1') showBetaNotice(_demoDdToggle);
    });
    document.addEventListener('click', () => { if (_demoDdMenu) _demoDdMenu.hidden = true; });
}

window.cs2reveal = function (container, selector) {
    if (!container) return;
    const targets = [].slice.call(container.querySelectorAll(selector));
    if (!targets.length) return;
    const reduce = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    if (reduce || !('IntersectionObserver' in window)) return;   
    targets.forEach(t => t.classList.add('reveal'));
    const scroller = container.closest('.tab-panel') || null;
    const io = new IntersectionObserver((entries, obs) => {
        entries.forEach(e => {
            if (!e.isIntersecting) return;
            const t = e.target;
            t.style.transitionDelay = (t.getAttribute('data-rd') || 0) + 'ms';
            t.classList.add('is-in');
            t.addEventListener('transitionend', () => { t.style.transitionDelay = ''; }, { once: true });
            obs.unobserve(t);
        });
    }, { root: scroller, rootMargin: '0px 0px -6% 0px', threshold: 0.04 });
    targets.forEach((t, i) => { t.setAttribute('data-rd', Math.min(i, 14) * 28); io.observe(t); });
};

const osSwitch    = document.getElementById('osSwitch');
let _selectedOS   = localStorage.getItem('cs2prak_os') || 'windows';

function _syncOSLabels() {
    const isLinux = _selectedOS === 'linux';
    osSwitch.checked = isLinux;
    document.getElementById('osLabelWin').classList.toggle('active', !isLinux);
    document.getElementById('osLabelLin').classList.toggle('active',  isLinux);
}

osSwitch.addEventListener('change', () => {
    _selectedOS = osSwitch.checked ? 'linux' : 'windows';
    localStorage.setItem('cs2prak_os', _selectedOS);
    _syncOSLabels();
});

document.getElementById('osLabelWin').addEventListener('click', () => {
    if (_selectedOS !== 'windows') { osSwitch.checked = false; osSwitch.dispatchEvent(new Event('change')); }
});
document.getElementById('osLabelLin').addEventListener('click', () => {
    if (_selectedOS !== 'linux') { osSwitch.checked = true; osSwitch.dispatchEvent(new Event('change')); }
});

_syncOSLabels();


const THEMES_ENABLED = false;

window.applyTheme = function (theme) {
    const t = THEMES_ENABLED ? (theme || 'dark') : 'dark';
    if (t === 'dark') document.documentElement.removeAttribute('data-theme');
    else              document.documentElement.setAttribute('data-theme', t);
    try { localStorage.setItem('cs2prak_theme', t); } catch (e) {}
};

(function initSettings() {
    
    const themeSeg = document.getElementById('setThemeSeg');
    if (themeSeg) {
        if (!THEMES_ENABLED) {
            
            window.applyTheme('dark');
            themeSeg.querySelectorAll('.dv-seg-btn').forEach(b => {
                const dark = b.dataset.theme === 'dark';
                b.classList.toggle('on', dark);
                b.setAttribute('aria-checked', dark ? 'true' : 'false');
                b.disabled = true;
            });
            const card = themeSeg.closest('.set-card');
            if (card) card.classList.add('is-locked');
        } else {
            const syncTheme = () => {
                const cur = localStorage.getItem('cs2prak_theme') || 'dark';
                themeSeg.querySelectorAll('.dv-seg-btn').forEach(b => {
                    const on = b.dataset.theme === cur;
                    b.classList.toggle('on', on);
                    b.setAttribute('aria-checked', on ? 'true' : 'false');
                });
            };
            themeSeg.querySelectorAll('.dv-seg-btn').forEach(btn => {
                btn.addEventListener('click', () => {
                    window.applyTheme(btn.dataset.theme);
                    syncTheme();
                });
            });
            syncTheme();
        }
    }

    const langSeg = document.getElementById('setLangSeg');
    if (langSeg) {
        const syncLang = () => {
            const cur = localStorage.getItem('cs2prak_lang') || 'en';
            langSeg.querySelectorAll('.dv-seg-btn').forEach(b => {
                const on = b.dataset.lang === cur;
                b.classList.toggle('on', on);
                b.setAttribute('aria-checked', on ? 'true' : 'false');
            });
        };
        langSeg.querySelectorAll('.dv-seg-btn').forEach(btn => {
            btn.addEventListener('click', () => {
                if (window.applyLang) window.applyLang(btn.dataset.lang);
                syncLang();
            });
        });
        
        document.addEventListener('langchange', syncLang);
        syncLang();
    }

    const tut = document.getElementById('setTutorialToggle');
    if (tut) {
        tut.checked = localStorage.getItem('cs2prak_tutorial_off') !== '1';
        tut.addEventListener('change', () => {
            if (tut.checked) localStorage.removeItem('cs2prak_tutorial_off');
            else             localStorage.setItem('cs2prak_tutorial_off', '1');
        });
    }
    const restart = document.getElementById('setRestartTour');
    if (restart) {
        restart.addEventListener('click', () => {
            if (window.startTutorial) window.startTutorial();
        });
    }

    const pencil = document.getElementById('setPencilToggle');
    if (pencil) {
        pencil.checked = localStorage.getItem('cs2prak_pencil') === '1';
        pencil.addEventListener('change', () => {
            if (pencil.checked) localStorage.setItem('cs2prak_pencil', '1');
            else                localStorage.removeItem('cs2prak_pencil');
        });
    }
})();

buildMaps();
updateLaunchState();
updateServerStatusLine();
startHeartbeat();

const _savedId = localStorage.getItem('cs2prak_steamid');
if (_savedId) {
    steamIdInput.value = _savedId;
    steamIdInput.dispatchEvent(new Event('input'));
}

fetch('/status').then(r => r.json()).then(data => {
    if (data.running) {
        serverRunning = true;
        setStatus(true);
        showConnectBar();
        updateLaunchState();
    }
}).catch(() => {});

startStatusPoll();
_updatePerfMode();

const updateBtn      = document.getElementById('updateBtn');
const updBackdrop    = document.getElementById('updBackdrop');
const updLog         = document.getElementById('updLog');
const updStatusLabel = document.getElementById('updStatusLabel');
const updCloseBtn    = document.getElementById('updCloseBtn');

let _updPollTimer = null;
let _updLogCursor = 0;
let _updPollMode  = 'server';

function openUpdateModal()  { updBackdrop.classList.add('open'); }
function closeUpdateModal() { updBackdrop.classList.remove('open'); }

function stopUpdPoll() {
    if (_updPollTimer) { clearInterval(_updPollTimer); _updPollTimer = null; }
}

async function pollUpdateStatus() {
    try {
        const res  = await fetch('/update/status');
        const data = await res.json();
        const newLines = data.log.slice(_updLogCursor);
        if (newLines.length) {
            updLog.textContent += newLines.join('\n') + '\n';
            _updLogCursor = data.log.length;
            updLog.scrollTop = updLog.scrollHeight;
        }
        if (!data.running) {
            stopUpdPoll();
            if (updateBtn) { updateBtn.classList.remove('updating'); updateBtn.disabled = false; }
            if (data.exitCode === 0) {
                updStatusLabel.textContent = 'DONE';
                updStatusLabel.className   = 'upd-status-label done';
                showToast('Server updated successfully', 'success');
            } else {
                updStatusLabel.textContent = 'FAILED';
                updStatusLabel.className   = 'upd-status-label failed';
                showToast('Update failed (exit ' + data.exitCode + ')', 'error');
            }
        }
    } catch {  }
}

updateBtn && updateBtn.addEventListener('click', async () => {
    if (updateBtn.classList.contains('updating')) return;
    updLog.textContent = '';
    _updLogCursor = 0;
    updStatusLabel.textContent = 'RUNNING…';
    updStatusLabel.className   = 'upd-status-label';
    document.getElementById('updTitle').textContent = 'SERVER UPDATE';
    _updPollMode = 'server';
    openUpdateModal();
    try {
        const res  = await fetch('/update', { method: 'POST' });
        const data = await res.json();
        if (!data.ok) {
            updStatusLabel.textContent = 'ERROR';
            updStatusLabel.className   = 'upd-status-label failed';
            updLog.textContent = data.message || 'Could not start update';
            return;
        }
        updateBtn.classList.add('updating');
        updateBtn.disabled = true;
        _updPollTimer = setInterval(pollUpdateStatus, 1000);
    } catch {
        updStatusLabel.textContent = 'ERROR';
        updStatusLabel.className   = 'upd-status-label failed';
        updLog.textContent = 'Could not reach backend';
    }
});

updCloseBtn.addEventListener('click', closeUpdateModal);
updBackdrop.addEventListener('click', e => { if (e.target === updBackdrop) closeUpdateModal(); });

let _pluginInstallId = null;
let _pluginInstallName = null;

function _normVer(v) {
    if (!v || v === '—' || v === 'unknown') return null;
    v = v.trim().replace(/^v/, '').split('-')[0];
    return v.split('.').map(x => parseInt(x, 10) || 0);
}

function _isOutdated(local, latest) {
    const tl = _normVer(local);
    const tr = _normVer(latest);
    if (!tl || !tr) return false;
    const n = Math.min(tl.length, tr.length);
    const ls = tl.slice(-n);
    const rs = tr.slice(-n);
    for (let i = 0; i < n; i++) {
        if (ls[i] < rs[i]) return true;
        if (ls[i] > rs[i]) return false;
    }
    return false;
}

function _buildPluginCard(p) {
    const card = document.createElement('div');
    card.className = 'plugin-card' + (p.is_dependency ? ' plugin-card-dep' : '');
    card.dataset.id = p.id;

    const badge = p.installed
        ? '<span class="plugin-badge badge-installed">INSTALLED</span>'
        : '<span class="plugin-badge badge-missing">NOT INSTALLED</span>';
    const depBadge = p.is_dependency
        ? '<span class="plugin-badge badge-dep">DEPENDENCY</span>' : '';

    const localVer = p.local_version || (p.installed ? 'unknown' : '—');

    card.innerHTML = `
        <div class="plugin-card-main">
            <div class="plugin-card-info">
                <div class="plugin-card-title">${p.name} ${badge}${depBadge}</div>
                <div class="plugin-card-desc">${p.description}</div>
                <div class="plugin-versions-row">
                    <span class="plugin-ver-item">
                        <span class="plugin-ver-label">LOCAL</span>
                        <span class="plugin-ver-val" id="localVer_${p.id}">${localVer}</span>
                    </span>
                    <span class="plugin-ver-sep">→</span>
                    <span class="plugin-ver-item">
                        <span class="plugin-ver-label">LATEST</span>
                        <span class="plugin-ver-val" id="latestVer_${p.id}">—</span>
                    </span>
                </div>
            </div>
            <div class="plugin-card-actions">
                <a class="plugin-gh-link" href="${p.github_url}" target="_blank" rel="noopener">
                    <svg width="13" height="13" viewBox="0 0 24 24" fill="currentColor"><path d="M12 0C5.37 0 0 5.37 0 12c0 5.31 3.435 9.795 8.205 11.385.6.105.825-.255.825-.57 0-.285-.015-1.23-.015-2.235-3.015.555-3.795-.735-4.035-1.41-.135-.345-.72-1.41-1.23-1.695-.42-.225-1.02-.78-.015-.795.945-.015 1.62.87 1.845 1.23 1.08 1.815 2.805 1.305 3.495.99.105-.78.42-1.305.765-1.605-2.67-.3-5.46-1.335-5.46-5.925 0-1.305.465-2.385 1.23-3.225-.12-.3-.54-1.53.12-3.18 0 0 1.005-.315 3.3 1.23.96-.27 1.98-.405 3-.405s2.04.135 3 .405c2.295-1.56 3.3-1.23 3.3-1.23.66 1.65.24 2.88.12 3.18.765.84 1.23 1.905 1.23 3.225 0 4.605-2.805 5.625-5.475 5.925.435.375.81 1.095.81 2.22 0 1.605-.015 2.895-.015 3.3 0 .315.225.69.825.57A12.02 12.02 0 0 0 24 12c0-6.63-5.37-12-12-12z"/></svg>
                    RELEASES
                </a>
                <button class="plugin-install-btn" id="installBtn_${p.id}">INSTALL</button>
            </div>
        </div>
        <div class="plugin-update-badge" id="updateBadge_${p.id}" style="display:none">
            UPDATE AVAILABLE
        </div>`;

    card.querySelector(`#installBtn_${p.id}`)
        .addEventListener('click', () => startPluginDownload(p.id, p.name));
    return card;
}

async function loadPlugins() {
    const list = document.getElementById('pluginsList');
    const hint = document.getElementById('pluginsHint');
    list.textContent = '';
    hint.textContent = 'Loading installed plugins...';
    try {
        const res  = await fetch('/api/plugins');
        const data = await res.json();
        data.forEach(p => list.appendChild(_buildPluginCard(p)));
        hint.textContent = 'Click CHECK UPDATES to compare with GitHub releases.';
    } catch {
        hint.textContent = 'Could not load plugin info.';
    }
}

async function checkPluginUpdates() {
    const hint    = document.getElementById('pluginsHint');
    const btn     = document.getElementById('checkUpdatesBtn');
    hint.textContent = 'Fetching latest versions from GitHub...';
    btn.disabled = true;
    try {
        const res    = await fetch('/api/plugins/latest');
        const latest = await res.json();
        let updates  = 0;
        for (const [id, ver] of Object.entries(latest)) {
            const latestEl = document.getElementById(`latestVer_${id}`);
            if (latestEl) latestEl.textContent = ver || 'N/A';
            if (ver) {
                const localEl = document.getElementById(`localVer_${id}`);
                const local   = localEl ? localEl.textContent : null;
                if (_isOutdated(local, ver)) {
                    const badge = document.getElementById(`updateBadge_${id}`);
                    if (badge) badge.style.display = '';
                    updates++;
                }
            }
        }
        hint.textContent = updates
            ? `${updates} update${updates > 1 ? 's' : ''} available.`
            : 'All plugins are up to date.';
    } catch {
        hint.textContent = 'Failed to fetch latest versions.';
    }
    btn.disabled = false;
}

async function startPluginDownload(pluginId, pluginName) {
    updLog.textContent = '';
    _updLogCursor = 0;
    updStatusLabel.textContent = 'RUNNING…';
    updStatusLabel.className   = 'upd-status-label';
    document.getElementById('updTitle').textContent =
        `INSTALLING ${pluginName.toUpperCase()}`;
    _updPollMode       = 'plugin';
    _pluginInstallId   = pluginId;
    _pluginInstallName = pluginName;
    openUpdateModal();

    try {
        const res  = await fetch(`/api/plugins/${pluginId}/download?os=${_selectedOS}`, { method: 'POST' });
        const data = await res.json();
        if (!data.ok) {
            updStatusLabel.textContent = 'ERROR';
            updStatusLabel.className   = 'upd-status-label failed';
            updLog.textContent = data.message || 'Could not start download';
            return;
        }
        _updPollTimer = setInterval(() => pollPluginStatus(pluginId), 1000);
    } catch {
        updStatusLabel.textContent = 'ERROR';
        updStatusLabel.className   = 'upd-status-label failed';
        updLog.textContent = 'Could not reach backend';
    }
}

async function startPluginInstallAll() {
    updLog.textContent = '';
    _updLogCursor = 0;
    updStatusLabel.textContent = 'RUNNING…';
    updStatusLabel.className   = 'upd-status-label';
    document.getElementById('updTitle').textContent = 'AUTO-INSTALL PLUGINS';
    _updPollMode       = 'plugin';
    _pluginInstallId   = '__all__';
    _pluginInstallName = 'All plugins';
    openUpdateModal();
    try {
        const res  = await fetch(`/api/plugins/install-all?os=${_selectedOS}`, { method: 'POST' });
        const data = await res.json();
        if (!data.ok) {
            updStatusLabel.textContent = 'ERROR';
            updStatusLabel.className   = 'upd-status-label failed';
            updLog.textContent = data.message || 'Could not start auto-install';
            return;
        }
        _updPollTimer = setInterval(() => pollPluginStatus('__all__'), 1000);
    } catch {
        updStatusLabel.textContent = 'ERROR';
        updStatusLabel.className   = 'upd-status-label failed';
        updLog.textContent = 'Could not reach backend';
    }
}
const autoInstallBtn = document.getElementById('autoInstallBtn');
autoInstallBtn && autoInstallBtn.addEventListener('click', startPluginInstallAll);

async function pollPluginStatus(pluginId) {
    try {
        const res  = await fetch(`/api/plugins/${pluginId}/download/status`);
        const data = await res.json();
        const newLines = data.log.slice(_updLogCursor);
        if (newLines.length) {
            updLog.textContent += newLines.join('\n') + '\n';
            _updLogCursor = data.log.length;
            updLog.scrollTop = updLog.scrollHeight;
        }
        if (!data.running) {
            stopUpdPoll();
            loadPlugins();
            checkSkinsReady();
            if (data.exitCode === 0) {
                updStatusLabel.textContent = 'DONE';
                updStatusLabel.className   = 'upd-status-label done';
                showToast(`${_pluginInstallName || pluginId} installed`, 'success');
            } else {
                updStatusLabel.textContent = 'FAILED';
                updStatusLabel.className   = 'upd-status-label failed';
                showToast('Install failed', 'error');
            }
        }
    } catch {  }
}

document.getElementById('checkUpdatesBtn')
    .addEventListener('click', checkPluginUpdates);

document.getElementById('openCsgoBtn').addEventListener('click', async () => {
    try {
        const res  = await fetch('/api/open-csgo', { method: 'POST' });
        const data = await res.json();
        if (!data.ok) showToast(data.message || 'Could not open server folder', 'error');
    } catch {
        showToast('Could not reach backend', 'error');
    }
});

(function () {
    const menu  = document.getElementById('pluginsMenu');
    const fnBtn = document.getElementById('pluginsFnBtn');
    const list  = document.getElementById('pluginsMenuList');
    if (!menu || !fnBtn) return;
    const close = () => { menu.classList.remove('open'); fnBtn.setAttribute('aria-expanded', 'false'); };
    fnBtn.addEventListener('click', (e) => {
        e.stopPropagation();
        const open = menu.classList.toggle('open');
        fnBtn.setAttribute('aria-expanded', open ? 'true' : 'false');
    });
    if (list) list.addEventListener('click', close);          
    document.addEventListener('click', (e) => { if (!menu.contains(e.target)) close(); });
})();

const dlServerBtn      = document.getElementById('dlServerBtn');
const dlStatusDot      = document.getElementById('dlStatusDot');
const dlStatusText     = document.getElementById('dlStatusText');
async function checkServerInstalled() {
    try {
        const res = await fetch('/api/server/status');
        const srv = await res.json();
        dlStatusDot.classList.toggle('online', srv.installed);
        dlStatusText.textContent = srv.installed ? 'CS2 Server installed' : 'Not installed';
    } catch {
        dlStatusText.textContent = 'Could not check status';
    }
    checkServerUpdate();
    if (window.refreshServerRemoveCard) window.refreshServerRemoveCard();
}

async function checkServerUpdate() {
    const banner = document.getElementById('serverUpdateBanner');
    if (!banner) return;
    try {
        const d = await (await fetch('/api/server/update-check')).json();
        banner.style.display = d.outdated ? 'flex' : 'none';
    } catch {
        banner.style.display = 'none';
    }
}

const serverRebuildBtn = document.getElementById('serverRebuildBtn');
const rebuildBackdrop  = document.getElementById('rebuildBackdrop');
serverRebuildBtn && serverRebuildBtn.addEventListener('click', () => {
    if (rebuildBackdrop) rebuildBackdrop.classList.add('open');
});
const rebuildCancel = document.getElementById('rebuildCancel');
rebuildCancel && rebuildCancel.addEventListener('click', () => rebuildBackdrop.classList.remove('open'));
rebuildBackdrop && rebuildBackdrop.addEventListener('click', e => {
    if (e.target === rebuildBackdrop) rebuildBackdrop.classList.remove('open');
});
const rebuildGo = document.getElementById('rebuildGo');
rebuildGo && rebuildGo.addEventListener('click', async () => {
    rebuildBackdrop.classList.remove('open');
    updLog.textContent = '';
    _updLogCursor = 0;
    updStatusLabel.textContent = 'RUNNING…';
    updStatusLabel.className   = 'upd-status-label';
    document.getElementById('updTitle').textContent = 'REBUILD SERVER';
    _updPollMode = 'server-install';
    openUpdateModal();
    try {
        const res  = await fetch('/api/server/rebuild', { method: 'POST' });
        const data = await res.json();
        if (!data.ok) {
            updStatusLabel.textContent = 'ERROR';
            updStatusLabel.className   = 'upd-status-label failed';
            updLog.textContent = data.message || 'Could not start rebuild';
            return;
        }
        _updPollTimer = setInterval(pollServerInstallStatus, 1000);
    } catch {
        updStatusLabel.textContent = 'ERROR';
        updStatusLabel.className   = 'upd-status-label failed';
        updLog.textContent = 'Could not reach backend';
    }
});

dlServerBtn && dlServerBtn.addEventListener('click', async () => {
    updLog.textContent = '';
    _updLogCursor = 0;
    updStatusLabel.textContent = 'RUNNING…';
    updStatusLabel.className   = 'upd-status-label';
    document.getElementById('updTitle').textContent = 'DOWNLOAD SERVER';
    _updPollMode = 'server-install';
    openUpdateModal();
    try {
        const res  = await fetch('/api/server/install', { method: 'POST' });
        const data = await res.json();
        if (!data.ok) {
            updStatusLabel.textContent = 'ERROR';
            updStatusLabel.className   = 'upd-status-label failed';
            updLog.textContent = data.message || 'Could not start install';
            return;
        }
        dlServerBtn.disabled = true;
        _updPollTimer = setInterval(pollServerInstallStatus, 1000);
    } catch {
        updStatusLabel.textContent = 'ERROR';
        updStatusLabel.className   = 'upd-status-label failed';
        updLog.textContent = 'Could not reach backend';
    }
});

const useExistingBtn = document.getElementById('useExistingBtn');
useExistingBtn && useExistingBtn.addEventListener('click', async () => {
    updLog.textContent = '';
    _updLogCursor = 0;
    updStatusLabel.textContent = 'RUNNING…';
    updStatusLabel.className   = 'upd-status-label';
    document.getElementById('updTitle').textContent = 'USE INSTALLED CS2';
    _updPollMode = 'server-install';
    openUpdateModal();
    try {
        const res  = await fetch('/api/server/use-existing', { method: 'POST' });
        const data = await res.json();
        if (!data.ok) {
            updStatusLabel.textContent = 'ERROR';
            updStatusLabel.className   = 'upd-status-label failed';
            updLog.textContent = data.message || 'Could not start';
            return;
        }
        useExistingBtn.disabled = true;
        _updPollTimer = setInterval(pollServerInstallStatus, 1000);
    } catch {
        updStatusLabel.textContent = 'ERROR';
        updStatusLabel.className   = 'upd-status-label failed';
        updLog.textContent = 'Could not reach backend';
    }
});

async function pollServerInstallStatus() {
    try {
        const res  = await fetch('/api/server/install/status');
        const data = await res.json();
        const newLines = data.log.slice(_updLogCursor);
        if (newLines.length) {
            updLog.textContent += newLines.join('\n') + '\n';
            _updLogCursor = data.log.length;
            updLog.scrollTop = updLog.scrollHeight;
        }
        if (!data.running) {
            stopUpdPoll();
            if (dlServerBtn) dlServerBtn.disabled = false;
            if (useExistingBtn) useExistingBtn.disabled = false;
            if (data.exitCode === 0) {
                updStatusLabel.textContent = 'DONE';
                updStatusLabel.className   = 'upd-status-label done';
                showToast('Server ready', 'success');
                checkServerInstalled();
                checkSkinsReady();
            } else {
                updStatusLabel.textContent = 'FAILED';
                updStatusLabel.className   = 'upd-status-label failed';
                showToast('Server install failed', 'error');
            }
        }
    } catch {  }
}

document.getElementById('configureBtn').addEventListener('click', async () => {
    updLog.textContent = '';
    _updLogCursor = 0;
    updStatusLabel.textContent = 'RUNNING…';
    updStatusLabel.className   = 'upd-status-label';
    document.getElementById('updTitle').textContent = 'CONFIGURE SERVER';
    _updPollMode = 'configure';
    openUpdateModal();
    try {
        const res  = await fetch('/api/configure', { method: 'POST' });
        const data = await res.json();
        if (!data.ok) {
            updStatusLabel.textContent = 'ERROR';
            updStatusLabel.className   = 'upd-status-label failed';
            updLog.textContent = data.message || 'Could not start configure';
            return;
        }
        _updPollTimer = setInterval(pollConfigureStatus, 1000);
    } catch {
        updStatusLabel.textContent = 'ERROR';
        updStatusLabel.className   = 'upd-status-label failed';
        updLog.textContent = 'Could not reach backend';
    }
});

async function pollConfigureStatus() {
    try {
        const res  = await fetch('/api/configure/status');
        const data = await res.json();
        const newLines = data.log.slice(_updLogCursor);
        if (newLines.length) {
            updLog.textContent += newLines.join('\n') + '\n';
            _updLogCursor = data.log.length;
            updLog.scrollTop = updLog.scrollHeight;
        }
        if (!data.running) {
            stopUpdPoll();
            if (data.exitCode === 0) {
                updStatusLabel.textContent = 'DONE';
                updStatusLabel.className   = 'upd-status-label done';
                showToast('Server configured successfully', 'success');
            } else {
                updStatusLabel.textContent = 'FAILED';
                updStatusLabel.className   = 'upd-status-label failed';
                showToast('Configuration failed', 'error');
            }
        }
    } catch {  }
}

const adminInput  = document.getElementById('adminSteamId');
const saveAdminBtn = document.getElementById('saveAdminBtn');

fetch('/api/admin')
    .then(r => r.json())
    .then(d => { if (d.steamid) adminInput.value = d.steamid; })
    .catch(() => {});

saveAdminBtn.addEventListener('click', async () => {
    const steamid = adminInput.value.trim();
    if (!/^7656119\d{10}$/.test(steamid)) {
        showToast('Invalid SteamID64 format', 'error');
        return;
    }
    try {
        const res  = await fetch('/api/admin', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ steamid }),
        });
        const data = await res.json();
        if (data.ok) showToast('Admin saved', 'success');
        else showToast(data.message || 'Failed to save admin', 'error');
    } catch {
        showToast('Could not reach backend', 'error');
    }
});

const managePluginsBtn = document.getElementById('managePluginsBtn');
const pmBackdrop = document.getElementById('pmBackdrop');
const pmCloseBtn = document.getElementById('pmCloseBtn');
const pmList     = document.getElementById('pmList');

const PLUGIN_AUTHORS = {
    'MatchZy':            'shobhit-pathak',
    'MenuManagerCS2':     'NickFox007',
    'PlayerSettings':     'NickFox007',
    'AnyBaseLibCS2':      'NickFox007',
    'WeaponPaints':       'Nereziel',
    'CounterStrikeSharp': 'roflmuffin',
    'Metamod:Source':     'alliedmodders',
    'TimerHUD':           'Sevelinish',
    'MovementHUD':        'Sevelinish',
};

function buildPmRow(p) {
    const row = document.createElement('div');
    row.className = 'pm-row';
    const author = PLUGIN_AUTHORS[p.name];
    const desc = author
        ? `by <a class="pm-row-author" href="https://github.com/${author}" target="_blank" rel="noopener noreferrer">@${author}</a>`
        : p.folder;
    row.innerHTML = `
        <div class="pm-row-info">
            <div class="pm-row-name">
                <span>${p.name}</span>
                ${p.external ? '<span class="pm-badge-ext">External</span>' : ''}
            </div>
            <div class="pm-row-folder">${desc}</div>
        </div>
        <span class="pm-state ${p.enabled ? 'on' : ''}">${p.enabled ? 'ON' : 'OFF'}</span>
        <label class="sp-toggle">
            <input type="checkbox" ${p.enabled ? 'checked' : ''}>
            <span class="sp-toggle-track"></span>
        </label>
    `;
    const input = row.querySelector('input');
    const state = row.querySelector('.pm-state');
    input.addEventListener('change', async () => {
        const enabled = input.checked;
        input.disabled = true;
        try {
            const res = await fetch(`/api/plugins/installed/${encodeURIComponent(p.folder)}/toggle`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ enabled }),
            });
            const data = await res.json();
            if (data.ok) {
                p.enabled = enabled;
                state.textContent = enabled ? 'ON' : 'OFF';
                state.className = 'pm-state ' + (enabled ? 'on' : '');
                showToast(enabled ? 'Plugin enabled' : 'Plugin disabled', 'success');
            } else {
                input.checked = !enabled;
                showToast(data.message || 'Failed to toggle plugin', 'error');
            }
        } catch {
            input.checked = !enabled;
            showToast('Could not reach backend', 'error');
        }
        input.disabled = false;
    });
    return row;
}

async function openPluginManager() {
    pmBackdrop.classList.add('open');
    pmList.innerHTML = '<div class="pm-empty">Loading…</div>';
    try {
        const items = await fetch('/api/plugins/installed').then(r => r.json());
        pmList.innerHTML = '';
        if (!items.length) {
            pmList.innerHTML = '<div class="pm-empty">No plugins found in the plugins folder.</div>';
            return;
        }
        items.forEach(p => pmList.appendChild(buildPmRow(p)));
    } catch {
        pmList.innerHTML = '<div class="pm-empty">Could not load plugins.</div>';
    }
}

managePluginsBtn.addEventListener('click', openPluginManager);
pmCloseBtn.addEventListener('click', () => pmBackdrop.classList.remove('open'));
pmBackdrop.addEventListener('click', e => { if (e.target === pmBackdrop) pmBackdrop.classList.remove('open'); });

(function () {
    const bd = document.getElementById('appUpdBackdrop');
    const txt = document.getElementById('appUpdText');
    const notes = document.getElementById('appUpdNotes');
    const go = document.getElementById('appUpdGo');
    const later = document.getElementById('appUpdLater');
    let last = null, phase = 'idle', autoShown = false;
    const mb = b => (b / 1048576).toFixed(1) + ' MB';
    const show = () => { if (bd) bd.classList.add('open'); };
    const hide = () => { if (bd) bd.classList.remove('open'); };
    const isUpd = s => s && (s.status === 'available' || s.status === 'downloading'
        || s.status === 'ready' || s.staged);

    function render() {
        const s = last; if (!s) return;
        if (notes) {
            const has = !!(s.notes && s.notes.trim());
            notes.hidden = !has;
            if (has) notes.textContent = s.notes.trim();
        }
        if (s.status === 'ready' || s.staged) {
            phase = 'ready';
            txt.textContent = t('update.ready');
            go.textContent = t('update.restart'); go.disabled = false; later.disabled = false;
        } else if (s.status === 'downloading') {
            phase = 'downloading';
            txt.textContent = t('update.downloading'); go.disabled = true;
        } else if (s.status === 'available') {
            phase = 'available';
            txt.textContent = (s.latest ? s.latest + ' · ' : '') + mb(s.size || 0);
            go.textContent = t('update.download'); go.disabled = false;
        }
    }
    function openModal() { if (isUpd(last)) { render(); show(); } }
    window.cs2OpenUpdate = openModal;   

    later && later.addEventListener('click', hide);
    go && go.addEventListener('click', () => {
        if (phase === 'available') {
            fetch('/api/update/download', { method: 'POST' }).catch(() => {});
            phase = 'downloading'; txt.textContent = t('update.downloading'); go.disabled = true;
        } else if (phase === 'ready') {
            txt.textContent = t('update.restarting'); go.disabled = later.disabled = true;
            fetch('/api/update/apply', { method: 'POST' }).catch(() => {});
        }
    });

    function paintSettings(s) {
        const st = document.getElementById('setUpdStatus');
        const btn = document.getElementById('setUpdBtn');
        if (!st || !btn) return;
        const cur = s.current ? 'v' + s.current : '';
        if (s.status === 'ready' || s.staged) {
            st.textContent = cur + ' · ' + t('settings.updReady');
            btn.hidden = false; btn.textContent = t('update.restart');
        } else if (s.status === 'available' || s.status === 'downloading') {
            st.textContent = cur + ' → ' + (s.latest || '') + ' ' + t('settings.updAvail');
            btn.hidden = false; btn.textContent = t('update.download');
        } else {
            st.textContent = cur + ' · ' + t('settings.updLatest');
            btn.hidden = true;
        }
    }
    const setBtn = document.getElementById('setUpdBtn');
    setBtn && setBtn.addEventListener('click', openModal);

    function poll() {
        fetch('/api/update/status').then(r => r.json()).then(s => {
            if (!s) return;
            last = s;
            
            
            if (s.current) {
                document.querySelectorAll('#appVersion, #deskVersion')
                    .forEach(el => { el.textContent = 'v' + s.current; });
            }
            paintSettings(s);
            if (bd && bd.classList.contains('open')) render();    

            if (!autoShown && !s.seen && isUpd(s)) {
                autoShown = true;
                fetch('/api/update/seen', { method: 'POST' }).catch(() => {});
                openModal();
            }
            const terminal = ['up-to-date', 'dev', 'error', 'no-release', 'no-manifest'];
            if (!terminal.includes(s.status) && s.status !== 'ready' && !s.staged) {
                setTimeout(poll, s.status === 'downloading' ? 2000 : 3000);
            }
        }).catch(() => { setTimeout(poll, 8000); });
    }
    poll();
})();

(function initUninstall() {
    const card = document.getElementById('uninstallCard');
    if (!card) return;
    const startBtn = document.getElementById('uninstStart');
    const step1  = document.getElementById('uninstStep1');
    const step2  = document.getElementById('uninstStep2');
    const list   = document.getElementById('uninstList');
    const total  = document.getElementById('uninstTotal');
    const prompt = document.getElementById('uninstPrompt');
    const input  = document.getElementById('uninstInput');
    const go     = document.getElementById('uninstGo');
    const logEl  = document.getElementById('uninstLog');
    const msg    = document.getElementById('uninstMsg');

    let preview = null;
    let msgKey  = '';       
    let running = false;

    const size = b => b >= 1073741824 ? (b / 1073741824).toFixed(2) + ' GB'
                    : b >= 1048576    ? (b / 1048576).toFixed(1) + ' MB'
                    : b >= 1024       ? Math.round(b / 1024) + ' KB'
                    : b + ' B';
    
    const tail = p => p.split(/[\\/]/).slice(-2).join('\\');

    function note(key, bad) {
        msgKey = key || '';
        msg.textContent = msgKey ? t(msgKey) : '';
        msg.classList.toggle('bad', !!bad);
    }

    function render() {
        if (!preview || !preview.items) return;
        list.innerHTML = '';
        
        preview.items.filter(i => !i.nested).forEach(i => {
            const row = document.createElement('div');
            row.className = 'unin-item' + (i.exists ? '' : ' gone');
            row.title = i.path;
            const label = document.createElement('i');
            label.textContent = t('uninst.i.' + i.key);
            const path = document.createElement('s');
            path.textContent = tail(i.path);
            const sz = document.createElement('b');
            sz.textContent = i.exists ? size(i.size) : t('uninst.absent');
            row.append(label, path, sz);
            list.appendChild(row);
        });
        total.textContent = size(preview.total || 0);
        prompt.textContent = '';
        t('uninst.type').split('{w}').forEach((part, n) => {
            if (n) {
                const w = document.createElement('b');
                w.textContent = t('uninst.word');
                prompt.appendChild(w);
            }
            prompt.appendChild(document.createTextNode(part));
        });
        input.placeholder = t('uninst.word');
        if (msgKey) msg.textContent = t(msgKey);
    }

    function validate() {
        go.disabled = running ||
            input.value.trim().toUpperCase() !== t('uninst.word').toUpperCase();
    }

    function show(step) {
        startBtn.hidden = step !== 0;
        step1.hidden = step !== 1;
        step2.hidden = step !== 2;
    }

    startBtn.addEventListener('click', () => {
        startBtn.disabled = true;
        note('uninst.loading');
        fetch('/api/uninstall/preview').then(r => r.json()).then(p => {
            preview = p;
            if (!p || !p.ok) {
                note('');
                msg.textContent = (p && p.blocked) || t('uninst.failed');
                msg.classList.add('bad');
                return;
            }
            render(); validate(); show(1); note('');
        }).catch(() => note('uninst.noBackend', true))
          .finally(() => { startBtn.disabled = false; });
    });

    document.getElementById('uninstNext').addEventListener('click', () => {
        show(2); input.value = ''; validate(); input.focus();
    });
    [document.getElementById('uninstCancel1'),
     document.getElementById('uninstCancel2')].forEach(b => {
        b.addEventListener('click', () => { preview = null; show(0); note(''); });
    });

    input.addEventListener('input', validate);
    input.addEventListener('keydown', e => { if (e.key === 'Enter') go.click(); });

    go.addEventListener('click', () => {
        if (go.disabled || !preview) return;
        running = true; validate();
        note('uninst.working');
        logEl.hidden = false; logEl.textContent = '';
        fetch('/api/uninstall', {
            method: 'POST', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ confirm: preview.confirm }),
        }).then(r => r.json()).then(j => {
            if (j && j.ok) { poll(); return; }
            running = false; validate();
            note('uninst.failed', true);
            if (j && j.message) logEl.textContent = j.message;
        }).catch(() => { running = false; validate(); note('uninst.noBackend', true); });
    });

    function poll() {
        fetch('/api/uninstall/status').then(r => r.json()).then(s => {
            if (!s) return;
            logEl.textContent = (s.log || []).join('\n');
            logEl.scrollTop = logEl.scrollHeight;
            if (s.running) { setTimeout(poll, 700); return; }
            if (s.error) { running = false; validate(); note('uninst.failed', true); return; }
            note('uninst.closing');
        }).catch(() => note('uninst.closing'));   
    }

    document.addEventListener('langchange', () => { render(); validate(); });
})();

function showOutdatedPlugins(list) {
    const backdrop = document.getElementById('poBackdrop');
    const listEl   = document.getElementById('poList');
    if (!backdrop || !listEl || !Array.isArray(list) || list.length === 0) return;

    listEl.textContent = '';
    list.forEach(u => {
        const row = document.createElement('div');
        row.className = 'po-item';

        const name = document.createElement('i');
        name.textContent = u.name;

        const local = document.createElement('s');
        local.textContent = u.local;

        const arrow = document.createElement('em');
        arrow.textContent = '\u2192';

        const latest = document.createElement('b');
        latest.textContent = u.latest;

        row.append(name, local, arrow, latest);
        listEl.appendChild(row);
    });

    backdrop.classList.add('open');
}

(function initOutdatedPopup() {
    const backdrop = document.getElementById('poBackdrop');
    if (!backdrop) return;

    const close = () => backdrop.classList.remove('open');

    document.getElementById('poCloseBtn').addEventListener('click', close);
    document.getElementById('poDismiss').addEventListener('click', close);
    backdrop.addEventListener('click', e => { if (e.target === backdrop) close(); });

    document.getElementById('poGoPlugins').addEventListener('click', () => {
        close();
        const btn = document.querySelector('.tab-btn[data-tab="2"]');
        if (btn) btn.click();
    });
})();

(function initServerRemove() {
    const card = document.getElementById('srvRemoveCard');
    if (!card) return;

    const startBtn = document.getElementById('srvRemoveStart');
    const step     = document.getElementById('srvRemoveStep');
    const sizeEl   = document.getElementById('srvRemoveSize');
    const goBtn    = document.getElementById('srvRemoveGo');
    const cancel   = document.getElementById('srvRemoveCancel');
    const logEl    = document.getElementById('srvRemoveLog');
    const msg      = document.getElementById('srvRemoveMsg');

    let preview = null;
    let wasInstalled = null;

    const size = b => b >= 1073741824 ? (b / 1073741824).toFixed(2) + ' GB'
                    : b >= 1048576    ? (b / 1048576).toFixed(1) + ' MB'
                    : b >= 1024       ? Math.round(b / 1024) + ' KB'
                    : b + ' B';

    function note(text, bad) {
        msg.textContent = text || '';
        msg.classList.toggle('bad', !!bad);
    }

    function show(open) {
        startBtn.hidden = open;
        step.hidden = !open;
    }

    async function refreshCard() {
        try {
            const p = await (await fetch('/api/server/remove/preview')).json();
            card.hidden = !p.installed;
            preview = p;
            sizeEl.textContent = size(p.size || 0);

            if (wasInstalled !== null && p.installed !== wasInstalled) {
                show(false);
                note('');
                logEl.hidden = true;
                logEl.textContent = '';
                goBtn.disabled = false;
            }
            wasInstalled = p.installed;
        } catch {
            card.hidden = true;
        }
    }

    startBtn.addEventListener('click', async () => {
        startBtn.disabled = true;
        await refreshCard();
        startBtn.disabled = false;
        if (!preview || !preview.installed) { note(t('srvrm.none')); return; }
        note('');
        show(true);
    });

    cancel.addEventListener('click', () => { show(false); note(''); });

    goBtn.addEventListener('click', async () => {
        if (!preview) return;
        goBtn.disabled = true;
        note(t('srvrm.working'));
        logEl.hidden = false;
        logEl.textContent = '';
        try {
            const r = await fetch('/api/server/remove', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ confirm: preview.confirm }),
            });
            const j = await r.json();
            if (!j.ok) {
                goBtn.disabled = false;
                note(j.message || t('srvrm.failed'), true);
                return;
            }
            poll();
        } catch {
            goBtn.disabled = false;
            note(t('srvrm.noBackend'), true);
        }
    });

    function poll() {
        fetch('/api/server/remove/status').then(r => r.json()).then(s => {
            if (!s) return;
            logEl.textContent = (s.log || []).join('\n');
            logEl.scrollTop = logEl.scrollHeight;
            if (s.running) { setTimeout(poll, 700); return; }

            goBtn.disabled = false;
            if (s.exitCode === 0) {
                show(false);
                showToast(t('srvrm.done'), 'success');
                checkServerInstalled();
            } else {
                note(t('srvrm.failed'), true);
            }
        }).catch(() => { goBtn.disabled = false; note(t('srvrm.noBackend'), true); });
    }

    window.refreshServerRemoveCard = refreshCard;
    refreshCard();
})();

const HL = {
    status: null,
    source: 'highlights',
    kind: 'all',
    demos: [],
    loaded: false,
    busy: false,
    recording: false,
    sort: { key: 'round', dir: 'asc' },
    jobTimer: null,
};

function hlEl(id) { return document.getElementById(id); }

function hlSetHint(text, bad) {
    const hint = hlEl('hmHint');
    if (!hint) return;
    hint.textContent = text || '';
    hint.classList.toggle('bad', !!bad);
}

function hlRenderProbe() {
    const box = hlEl('hmProbe');
    if (!box) return;
    const probe = HL.status && HL.status.probe;
    box.textContent = '';
    if (!probe) return;

    const strip = document.createElement('div');
    strip.className = 'hm-probe-strip';

    (probe.tools || []).forEach(tool => {
        const name = String(tool.name || '');

        const chip = document.createElement('span');
        chip.className = 'hm-tool' + (tool.ready ? ' ok' : '');
        chip.title = name.toUpperCase()
            + (tool.note ? ' \u2014 ' + tool.note : tool.path ? ' \u2014 ' + tool.path : '');

        const icon = document.createElement('img');
        icon.className = 'hm-tool-icon';
        icon.alt = name;
        icon.src = '/api/highlights/tool-icon?name=' + encodeURIComponent(name);
        icon.addEventListener('load', () => {
            if (icon.naturalHeight && icon.naturalWidth / icon.naturalHeight > 1.6) {
                chip.classList.add('hm-tool-wide');
            }
        });
        icon.addEventListener('error', () => {
            icon.remove();
            chip.classList.remove('hm-tool-wide');
            chip.textContent = name.toUpperCase();
            chip.classList.add('hm-tool-text');
        });

        chip.appendChild(icon);
        strip.appendChild(chip);
    });

    const note = document.createElement('span');
    note.className = 'hm-probe-note';
    note.textContent = probe.ready ? t('hl.ready')
        : probe.gameRunning ? t('hl.gameRunning')
        : t('hl.toolsLater');
    strip.appendChild(note);

    box.appendChild(strip);
}

function hlRenderDemos() {
    const select = hlEl('hmDemoSelect');
    if (!select) return;
    const previous = select.value;
    select.textContent = '';

    if (!HL.demos.length) {
        const option = document.createElement('option');
        option.value = '';
        option.textContent = t('hl.noDemos');
        select.appendChild(option);
        return;
    }

    HL.demos.forEach(demo => {
        const option = document.createElement('option');
        option.value = demo.path;
        const mb = Math.round((demo.sizeBytes || 0) / 1048576);
        option.textContent = demo.name + '  ·  ' + mb + ' MB';
        select.appendChild(option);
    });

    if (previous && HL.demos.some(d => d.path === previous)) select.value = previous;
}

async function hlLoadStatus() {
    const gate = hlEl('hmGate');
    const main = hlEl('hmMain');
    const loading = hlEl('hmLoading');

    const slow = setTimeout(() => {
        if (!loading) return;
        loading.hidden = false;
        if (gate) gate.hidden = true;
        if (main) main.hidden = true;
    }, 250);

    try {
        HL.status = await (await fetch('/api/highlights/status')).json();
    } catch {
        HL.status = null;
    }

    clearTimeout(slow);
    if (loading) loading.hidden = true;

    const installed = !!(HL.status && HL.status.installed);
    if (gate) gate.hidden = installed;
    if (main) main.hidden = !installed;
    if (installed) {
        hlRenderProbe();
        if (HL.status && HL.status.error) hlSetHint(HL.status.error, true);
    }
    return installed;
}

function hlRenderPlayers(players) {
    const select = hlEl('hmPlayerSelect');
    const previous = select.value;
    select.textContent = '';

    const any = document.createElement('option');
    any.value = '';
    any.textContent = t('hl.anyPlayer');
    select.appendChild(any);

    (players || []).forEach(p => {
        const option = document.createElement('option');
        option.value = p.steamId64;
        option.textContent = p.name + '  \u00b7  ' + p.killCount + 'K';
        select.appendChild(option);
    });

    if (previous && [...select.options].some(o => o.value === previous)) select.value = previous;
}

async function hlLoadPlayers() {
    const demo = hlEl('hmDemoSelect').value;
    hlRenderPlayers([]);
    if (!demo) return;

    hlSetHint(t('hl.readingRoster'));
    try {
        const res = await fetch('/api/highlights/players', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ demo: demo }),
        });
        const r = await res.json();
        if (!r.ok) { hlSetHint(r.message || t('hl.failed'), true); return false; }
        hlRenderPlayers(r.data.players);
        hlSetHint('');
        return true;
    } catch {
        hlSetHint(t('hl.noBackend'), true);
        return false;
    }
}

async function hlLoadDemos() {
    try {
        const r = await (await fetch('/api/highlights/demos')).json();
        HL.demos = (r.ok && r.data && r.data.demos) ? r.data.demos : [];
    } catch {
        HL.demos = [];
    }
    hlRenderDemos();
}

function hlParseRounds(text, selection) {
    const raw = (text || '').trim();
    if (!raw) return;
    const range = raw.match(/^(\d+)\s*-\s*(\d+)$/);
    if (range) {
        selection.roundRange = { from: +range[1], to: +range[2] };
        return;
    }
    const list = raw.split(/[,\s]+/).map(x => parseInt(x, 10)).filter(x => !isNaN(x));
    if (list.length) selection.rounds = list;
}

function hlBuildSelection() {
    const selection = { order: 'round_asc' };

    const player = hlEl('hmPlayerSelect').value;
    if (player) selection.players = [player];

    return selection;
}

const HL_NADE_KINDS = ['smoke', 'flash', 'he', 'molotov', 'decoy'];

function hlSelectedKinds() {
    return HL.kind === 'all' ? HL_NADE_KINDS.slice() : [HL.kind];
}

function hlRow(id, cells, keys) {
    const row = document.createElement('div');
    row.className = 'hm-item';
    if (keys) {
        row.dataset.round = keys.round;
        row.dataset.value = keys.value;
    }

    const pick = document.createElement('input');
    pick.type = 'checkbox';
    pick.className = 'hm-pick';
    pick.value = id;
    pick.checked = false;
    pick.addEventListener('change', hlPickedChanged);
    row.appendChild(pick);

    cells.forEach(cell => {
        const span = document.createElement('span');
        span.className = cell.cls || '';
        if (cell.icon) {
            const mark = document.createElement('span');
            mark.className = 'hm-kind-icon';
            const url = 'url(/static/weapon_icons/' + cell.icon + '.png)';
            mark.style.webkitMaskImage = url;
            mark.style.maskImage = url;
            mark.title = cell.text;
            span.appendChild(mark);
        } else {
            span.textContent = cell.text;
        }
        if (cell.title) span.title = cell.title;
        row.appendChild(span);
    });
    return row;
}

function hlRenderHighlights(data) {
    const box = hlEl('hmResults');
    box.textContent = '';

    hlRenderHead([
        { key: 'hl.cRound', cls: 'round', sort: 'round' },
        { key: 'hl.cPlayer', cls: 'who' },
        { key: 'hl.cWhat', cls: 'head' },
        { key: 'hl.cScore', cls: 'score', sort: 'value' },
    ], 'highlights');

    (data.highlights || []).forEach(h => {
        box.appendChild(hlRow(h.id, [
            { text: String(h.roundNumber), cls: 'hm-round' },
            { text: h.player.name, cls: 'hm-who', title: h.player.steamId64 },
            { text: h.headline, cls: 'hm-head' },
            { text: Math.round(h.score), cls: 'hm-score' },
        ], { round: h.roundNumber, value: h.score }));
    });
}

const HL_NADE_ICON = {
    smoke: 'smokegrenade',
    flash: 'flashbang',
    he: 'highexplosivegrenade',
    molotov: 'molotov',
    incendiary: 'incendiarygrenade',
    decoy: 'decoygrenade',
};

function hlRenderHead(labels, mode) {
    const head = hlEl('hmResultsHead');
    head.textContent = '';
    head.hidden = false;
    head.className = 'hm-results-head mode-' + mode;
    hlEl('hmResults').className = 'hm-results mode-' + mode;

    head.appendChild(document.createElement('span'));

    labels.forEach(label => {
        if (!label.sort) {
            const cell = document.createElement('span');
            cell.className = 'hm-col hm-col-' + label.cls;
            cell.textContent = t(label.key);
            head.appendChild(cell);
            return;
        }

        const cell = document.createElement('button');
        cell.type = 'button';
        cell.className = 'hm-col hm-col-' + label.cls + ' hm-col-sort';
        cell.dataset.sort = label.sort;
        cell.textContent = t(label.key);

        const caret = document.createElement('span');
        caret.className = 'hm-col-caret';
        cell.appendChild(caret);

        cell.addEventListener('click', () => hlSortBy(label.sort));
        head.appendChild(cell);
    });

    hlApplySort();
}

function hlSortBy(key) {
    if (HL.sort.key === key) HL.sort.dir = HL.sort.dir === 'asc' ? 'desc' : 'asc';
    else HL.sort = { key: key, dir: key === 'value' ? 'desc' : 'asc' };
    hlApplySort();
}

function hlApplySort() {
    const box = hlEl('hmResults');
    if (!box) return;

    const sign = HL.sort.dir === 'asc' ? 1 : -1;
    const rows = [...box.children];

    rows.sort((a, b) => {
        const main = (Number(a.dataset[HL.sort.key]) - Number(b.dataset[HL.sort.key])) * sign;
        return main || (Number(a.dataset.round) - Number(b.dataset.round));
    });
    rows.forEach(row => box.appendChild(row));

    document.querySelectorAll('#hmResultsHead .hm-col-sort').forEach(cell => {
        const on = cell.dataset.sort === HL.sort.key;
        cell.classList.toggle('active', on);
        cell.classList.toggle('desc', on && HL.sort.dir === 'desc');
    });
}

function hlRenderGrenades(data) {
    const box = hlEl('hmResults');
    box.textContent = '';

    hlRenderHead([
        { key: 'hl.cRound', cls: 'round', sort: 'round' },
        { key: 'hl.cPlayer', cls: 'who' },
        { key: 'hl.cPlace', cls: 'head' },
        { key: 'hl.cClock', cls: 'k', sort: 'value' },
        { key: 'hl.cKind', cls: 'score' },
    ], 'grenades');

    const items = [...(data.grenades || [])].sort((a, b) =>
        (a.roundNumber - b.roundNumber) || ((a.throwTick || 0) - (b.throwTick || 0)));

    items.forEach(g => {
        const kind = String(g.kind || '').toLowerCase();
        box.appendChild(hlRow(g.id, [
            { text: String(g.roundNumber), cls: 'hm-round' },
            { text: g.thrower.name, cls: 'hm-who', title: g.thrower.steamId64 },
            { text: g.landingPlace || '\u2014', cls: 'hm-head' },
            { text: g.roundClock || '', cls: 'hm-k' },
            { icon: HL_NADE_ICON[kind], text: kind.toUpperCase(), cls: 'hm-score hm-kind' },
        ], { round: g.roundNumber, value: g.roundTimeSeconds || 0 }));
    });
}

async function hlFind() {
    const demo = hlEl('hmDemoSelect').value;
    if (!demo) { hlSetHint(t('hl.pickDemo'), true); return; }
    if (HL.busy) return;

    HL.busy = true;
    hlEl('hmFindBtn').disabled = true;
    hlEl('hmSearching').hidden = false;
    hlSetHint(t('hl.working'));
    hlEl('hmResults').textContent = '';
    hlEl('hmResultsHead').hidden = true;

    const body = { demo: demo, source: HL.source, selection: hlBuildSelection() };
    if (HL.source === 'grenades') body.kinds = hlSelectedKinds();

    try {
        const res = await fetch('/api/highlights/find', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body),
        });
        const r = await res.json();
        if (!r.ok) {
            hlSetHint(r.message || t('hl.failed'), true);
        } else if (HL.source === 'grenades') {
            hlRenderGrenades(r.data);
            hlSetHint(r.data.count + ' / ' + r.data.total);
        } else {
            hlRenderHighlights(r.data);
            hlSetHint(r.data.count + ' / ' + r.data.total);
        }
        hlPickedChanged();
        if (document.querySelectorAll('#hmResults .hm-item').length) hlSetHint(t('hl.tickThem'));
    } catch {
        hlSetHint(t('hl.noBackend'), true);
    }

    HL.busy = false;
    hlEl('hmSearching').hidden = true;
    hlEl('hmFindBtn').disabled = false;
}

async function hlUpload(file) {
    hlSetHint(t('hl.uploading'));
    try {
        const res = await fetch('/api/highlights/upload?name=' + encodeURIComponent(file.name), {
            method: 'POST',
            body: file,
        });
        const r = await res.json();
        if (!r.ok) { hlSetHint(r.message || t('hl.failed'), true); return; }
        await hlLoadDemos();
        hlEl('hmDemoSelect').value = r.path;
        hlEl('hmResults').textContent = '';
    hlEl('hmResultsHead').hidden = true;
        hlPickedChanged();
        if (await hlLoadPlayers()) hlSetHint(t('hl.added'));
    } catch {
        hlSetHint(t('hl.noBackend'), true);
    }
}

function hlInstall() {
    const btn = hlEl('hmInstallBtn');
    const log = hlEl('hmInstallLog');
    btn.disabled = true;
    log.hidden = false;
    log.textContent = '';

    function pollInstall() {
        fetch('/api/highlights/install/status').then(r => r.json()).then(async s => {
            log.textContent = (s.log || []).join('\n');
            log.scrollTop = log.scrollHeight;
            if (s.running) { setTimeout(pollInstall, 700); return; }

            btn.disabled = false;
            if (s.exitCode === 0) {
                showToast(t('hl.installed'), 'success');
                if (await hlLoadStatus()) hlLoadDemos();
            } else {
                showToast(t('hl.failed'), 'error');
            }
        }).catch(() => { btn.disabled = false; });
    }

    fetch('/api/highlights/install', { method: 'POST' })
        .then(r => r.json())
        .then(j => {
            if (!j.ok) { btn.disabled = false; log.textContent = j.message || ''; return; }
            pollInstall();
        })
        .catch(() => { btn.disabled = false; log.textContent = t('hl.noBackend'); });
}

async function initHighlights() {
    if (!HL.loaded) {
        HL.loaded = true;

        hlEl('hmInstallBtn').addEventListener('click', hlInstall);
        hlEl('hmRefreshBtn').addEventListener('click', async () => {
            await hlLoadDemos();
            hlLoadPlayers();
        });
        hlEl('hmDemoSelect').addEventListener('change', () => {
            hlEl('hmResults').textContent = '';
    hlEl('hmResultsHead').hidden = true;
            hlPickedChanged();
            hlLoadPlayers();
        });
        hlEl('hmFindBtn').addEventListener('click', hlFind);

        hlEl('hmUploadInput').addEventListener('change', e => {
            const file = e.target.files && e.target.files[0];
            if (file) hlUpload(file);
            e.target.value = '';
        });

        hlEl('hmTypeSelect').addEventListener('change', e => {
            const parts = e.target.value.split(':');
            HL.source = parts[0];
            HL.kind = parts[1] || 'all';
            hlEl('hmResults').textContent = '';
    hlEl('hmResultsHead').hidden = true;
            hlPickedChanged();
            hlSetHint('');
        });

        hlInitRecording();
        hlInitExtras();
        hlDressSelects();
        document.addEventListener('langchange', hlRenderProbe);
    }

    if (await hlLoadStatus()) {
        await hlLoadDemos();
        hlLoadPlayers();
        clearTimeout(HL.jobTimer);
        hlPollJob();
    }
}

function hlPickedIds() {
    return [...document.querySelectorAll('#hmResults .hm-pick:checked')].map(b => b.value);
}

function hlPickedChanged() {
    const total = document.querySelectorAll('#hmResults .hm-pick').length;
    const picked = hlPickedIds().length;
    const actions = hlEl('hmActions');

    actions.hidden = total === 0;
    hlEl('hmPicked').textContent = picked + ' / ' + total;
    hlEl('hmRecordBtn').disabled = picked === 0 || HL.recording;
    hlEl('hmPreviewBtn').disabled = picked === 0 || HL.recording;
}

function hlSetAllPicks(on) {
    document.querySelectorAll('#hmResults .hm-pick').forEach(b => { b.checked = on; });
    hlPickedChanged();
}

function hlRecordBody() {
    const body = {
        demo: hlEl('hmDemoSelect').value,
        source: HL.source,
        selection: { ids: hlPickedIds() },
    };
    if (hlEl('hmSingleFile').checked) {
        body.overrides = { recording: { singleFile: true } };
    }
    return body;
}

async function hlPreview() {
    if (!hlPickedIds().length) return;
    hlSetHint(t('hl.planning'));
    try {
        const res = await fetch('/api/highlights/preview', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(hlRecordBody()),
        });
        const r = await res.json();
        if (!r.ok) { hlSetHint(r.message || t('hl.failed'), true); return; }

        const s = (r.data.plan && r.data.plan.summary) || {};
        const minutes = Math.round((s.totalSeconds || 0) / 6) / 10;
        hlSetHint(t('hl.planIs')
            .replace('{c}', s.clipCount || 0)
            .replace('{m}', minutes));
    } catch {
        hlSetHint(t('hl.noBackend'), true);
    }
}

function hlRenderJob(job) {
    const box = hlEl('hmJob');
    if (!job || !job.jobId) { box.hidden = true; return; }
    box.hidden = false;

    const done = job.state === 'succeeded' || job.state === 'failed' || job.state === 'cancelled';
    HL.recording = !done;

    const stage = job.state === 'queued' ? 0 : job.stage;
    hlEl('hmJobStage').textContent = job.state === 'queued'
        ? t('hl.queued')
        : stage + ' / ' + job.total;

    hlEl('hmJobTitle').textContent =
        job.state === 'succeeded' ? t('hl.doneN').replace('{n}', job.clips.length)
        : job.state === 'failed' ? (job.error || t('hl.failed'))
        : job.state === 'cancelled' ? t('hl.cancelled')
        : job.title || '';

    const pct = job.state === 'succeeded' ? 100 : Math.round((stage / job.total) * 100);
    const fill = hlEl('hmBarFill');
    fill.style.width = pct + '%';
    fill.classList.toggle('failed', job.state === 'failed');

    let detail = job.detail || '';
    if (!detail && job.clipCount) {
        detail = t('hl.planIs')
            .replace('{c}', job.clipCount)
            .replace('{m}', Math.round((job.totalSeconds || 0) / 6) / 10);
    }
    hlEl('hmJobDetail').textContent = detail;

    const clips = hlEl('hmJobClips');
    clips.textContent = '';
    (job.clips || []).forEach(clip => {
        const line = document.createElement('span');
        line.className = 'hm-clip';
        line.textContent = clip.name;
        line.title = clip.path;
        clips.appendChild(line);
    });

    hlEl('hmCancelBtn').hidden = done;
    hlEl('hmRevealBtn').hidden = job.state !== 'succeeded';
    hlEl('hmRecordBtn').disabled = !done || !hlPickedIds().length;
    hlEl('hmPreviewBtn').disabled = !done || !hlPickedIds().length;
}

async function hlPollJob() {
    try {
        const job = await (await fetch('/api/highlights/job')).json();
        hlRenderJob(job);
        if (job.state === 'queued' || job.state === 'running') {
            HL.jobTimer = setTimeout(hlPollJob, 1000);
            return;
        }
        if (job.state === 'succeeded') showToast(t('hl.doneN').replace('{n}', job.clips.length), 'success');
        if (job.state === 'failed') showToast(job.error || t('hl.failed'), 'error');
    } catch {
        HL.jobTimer = setTimeout(hlPollJob, 3000);
    }
}

async function hlRecord() {
    if (HL.recording || !hlPickedIds().length) return;

    hlEl('hmRecordBtn').disabled = true;
    hlSetHint(t('hl.submitting'));

    try {
        const res = await fetch('/api/highlights/record', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(hlRecordBody()),
        });
        const r = await res.json();
        if (!r.ok) {
            hlSetHint(r.message || t('hl.failed'), true);
            hlEl('hmRecordBtn').disabled = false;
            return;
        }
        hlSetHint('');
        HL.recording = true;
        hlRenderJob(r.job);
        clearTimeout(HL.jobTimer);
        hlPollJob();
    } catch {
        hlSetHint(t('hl.noBackend'), true);
        hlEl('hmRecordBtn').disabled = false;
    }
}

async function hlCancel() {
    hlEl('hmCancelBtn').disabled = true;
    try {
        const r = await (await fetch('/api/highlights/cancel', { method: 'POST' })).json();
        if (r.job) hlRenderJob(r.job);
    } catch { }
    hlEl('hmCancelBtn').disabled = false;
}

async function hlReveal() {
    try {
        await fetch('/api/highlights/reveal', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ demo: hlEl('hmDemoSelect').value }),
        });
    } catch { }
}

function hlInitRecording() {
    hlEl('hmAllBtn').addEventListener('click', () => hlSetAllPicks(true));
    hlEl('hmNoneBtn').addEventListener('click', () => hlSetAllPicks(false));
    hlEl('hmPreviewBtn').addEventListener('click', hlPreview);
    hlEl('hmRecordBtn').addEventListener('click', hlRecord);
    hlEl('hmCancelBtn').addEventListener('click', hlCancel);
    hlEl('hmRevealBtn').addEventListener('click', hlReveal);
}

function hlSwitchView(view) {
    document.querySelectorAll('.hm-subtab').forEach(b => {
        const on = b.dataset.view === view;
        b.classList.toggle('active', on);
        b.setAttribute('aria-selected', on ? 'true' : 'false');
    });
    hlEl('hmViewFind').hidden = view !== 'find';
    hlEl('hmViewMine').hidden = view !== 'mine';
    if (view !== 'mine') hlCloseVideo();
    if (view === 'mine') hlLoadMine();
}

function hlSize(bytes) {
    return bytes >= 1073741824 ? (bytes / 1073741824).toFixed(2) + ' GB'
         : bytes >= 1048576 ? (bytes / 1048576).toFixed(1) + ' MB'
         : Math.round(bytes / 1024) + ' KB';
}

function hlCloseVideo() {
    const el = hlEl('hmVideo');
    if (!el) return;
    el.pause();
    el.removeAttribute('src');
    el.load();
    hlEl('hmPlayerBox').hidden = true;
    hlEl('hmVideoName').textContent = '';
    HL.playing = null;
    document.querySelectorAll('#hmMineList .hm-clip-row.playing')
        .forEach(r => r.classList.remove('playing'));
}

function hlPlayVideo(video, row) {
    if (HL.playing === video.path) { hlCloseVideo(); return; }

    document.querySelectorAll('#hmMineList .hm-clip-row.playing')
        .forEach(r => r.classList.remove('playing'));
    if (row) row.classList.add('playing');

    HL.playing = video.path;
    hlEl('hmPlayerBox').hidden = false;
    hlEl('hmVideoName').textContent = video.name;

    const el = hlEl('hmVideo');
    el.src = '/api/highlights/video?path=' + encodeURIComponent(video.path);
    el.play().catch(() => { });
    hlEl('hmPlayerBox').scrollIntoView({ block: 'nearest' });
}

async function hlLoadMine() {
    const list = hlEl('hmMineList');
    const hint = hlEl('hmMineHint');
    hlCloseVideo();
    list.textContent = '';
    hint.textContent = t('hl.working');

    try {
        const r = await (await fetch('/api/highlights/output')).json();
        if (!r.ok) { hint.textContent = r.message || t('hl.failed'); return; }

        const videos = r.data.videos || [];
        hint.textContent = videos.length ? String(videos.length) : t('hl.noClips');

        videos.forEach(video => {
            const row = document.createElement('button');
            row.className = 'hm-clip-row';
            row.type = 'button';

            const name = document.createElement('span');
            name.className = 'hm-clip-name';
            name.textContent = video.name;

            const size = document.createElement('span');
            size.className = 'hm-clip-size';
            size.textContent = hlSize(video.sizeBytes || 0);

            row.append(name, size);
            row.addEventListener('click', () => hlPlayVideo(video, row));
            list.appendChild(row);
        });
    } catch {
        hint.textContent = t('hl.noBackend');
    }
}

function hlCfgInput(field, value) {
    const kind = field.kind;

    if (kind === 'boolean') {
        const box = document.createElement('input');
        box.type = 'checkbox';
        box.className = 'hm-pick';
        box.checked = value === true;
        return box;
    }

    if (kind === 'choice') {
        const options = String(field.description || '').split(',').map(x => x.trim()).filter(Boolean);
        if (options.length > 1 && options.every(o => /^[\w-]+$/.test(o))) {
            const select = document.createElement('select');
            select.className = 'fk-input';
            options.forEach(o => {
                const option = document.createElement('option');
                option.value = o;
                option.textContent = o;
                select.appendChild(option);
            });
            select.value = value == null ? options[0] : String(value);
            return select;
        }
    }

    const input = document.createElement('input');
    input.className = 'fk-input';
    input.spellcheck = false;

    if (kind === 'integer' || kind === 'number') {
        input.type = 'number';
        if (kind === 'integer') input.step = '1';
        input.value = value == null ? '' : value;
        return input;
    }

    if (kind === 'list') {
        input.type = 'text';
        input.value = Array.isArray(value) ? value.join(', ') : '';
        return input;
    }

    if (kind === 'map') {
        const area = document.createElement('textarea');
        area.className = 'fk-input hm-cfg-map';
        area.spellcheck = false;
        area.rows = 4;
        area.value = value == null ? '{}' : JSON.stringify(value, null, 1);
        return area;
    }

    input.type = 'text';
    input.value = value == null ? '' : String(value);
    return input;
}

function hlCfgValueAt(config, path) {
    return path.split('.').reduce((node, key) => (node == null ? undefined : node[key]), config);
}

function hlCfgRead(field, el) {
    const kind = field.kind;
    if (kind === 'boolean') return el.checked;
    if (kind === 'integer') { const n = parseInt(el.value, 10); return isNaN(n) ? null : n; }
    if (kind === 'number') { const n = parseFloat(el.value); return isNaN(n) ? null : n; }
    if (kind === 'list') return el.value.split(',').map(x => x.trim()).filter(Boolean);
    if (kind === 'map') { try { return JSON.parse(el.value); } catch { return undefined; } }
    return el.value;
}

async function hlOpenConfig() {
    const body = hlEl('hmCfgBody');
    const hint = hlEl('hmCfgHint');
    body.textContent = '';
    hint.textContent = t('hl.working');
    hlEl('hmCfgBackdrop').classList.add('open');

    let data;
    try {
        const r = await (await fetch('/api/highlights/config')).json();
        if (!r.ok) { hint.textContent = r.message || t('hl.failed'); return; }
        data = r;
    } catch {
        hint.textContent = t('hl.noBackend');
        return;
    }

    HL.cfgFields = [];
    const groups = new Map();

    (data.fields || []).forEach(field => {
        const group = field.path.split('.')[0];
        if (!groups.has(group)) groups.set(group, []);
        groups.get(group).push(field);
    });

    groups.forEach((fields, group) => {
        const section = document.createElement('div');
        section.className = 'hm-cfg-group';

        const title = document.createElement('div');
        title.className = 'hm-cfg-group-title';
        title.textContent = group.toUpperCase();
        section.appendChild(title);

        fields.forEach(field => {
            const row = document.createElement('label');
            row.className = 'hm-cfg-field' + (field.kind === 'map' ? ' hm-cfg-field-wide' : '');

            const label = document.createElement('span');
            label.className = 'hm-cfg-label';
            label.textContent = field.path.split('.').slice(1).join('.') || field.path;
            label.title = field.description || '';

            const el = hlCfgInput(field, hlCfgValueAt(data.config, field.path));
            HL.cfgFields.push({ field: field, el: el });

            row.append(label, el);

            if (field.unit) {
                const unit = document.createElement('span');
                unit.className = 'hm-cfg-unit';
                unit.textContent = field.unit;
                row.appendChild(unit);
            }

            section.appendChild(row);
        });

        body.appendChild(section);
    });

    hint.textContent = data.configFile || '';
}

async function hlSaveConfig() {
    const hint = hlEl('hmCfgHint');
    const patch = {};

    for (const entry of HL.cfgFields || []) {
        const value = hlCfgRead(entry.field, entry.el);
        if (value === undefined) {
            hint.textContent = t('hl.cfgBad').replace('{p}', entry.field.path);
            return;
        }
        entry.field.path.split('.').reduce((node, key, i, all) => {
            if (i === all.length - 1) { node[key] = value; return node; }
            node[key] = node[key] || {};
            return node[key];
        }, patch);
    }

    hlEl('hmCfgSave').disabled = true;
    hint.textContent = t('hl.working');
    try {
        const r = await (await fetch('/api/highlights/config', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ patch: patch }),
        })).json();
        hint.textContent = r.ok ? t('hl.cfgSaved') : (r.message || t('hl.failed'));
        if (r.ok) showToast(t('hl.cfgSaved'), 'success');
    } catch {
        hint.textContent = t('hl.noBackend');
    }
    hlEl('hmCfgSave').disabled = false;
}

function hlInitExtras() {
    document.querySelectorAll('.hm-subtab').forEach(btn => {
        btn.addEventListener('click', () => hlSwitchView(btn.dataset.view));
    });

    hlEl('hmMineRefresh').addEventListener('click', hlLoadMine);
    hlEl('hmMineFolder').addEventListener('click', hlReveal);
    hlEl('hmVideoClose').addEventListener('click', hlCloseVideo);

    hlEl('hmConfigBtn').addEventListener('click', hlOpenConfig);
    hlEl('hmCfgSave').addEventListener('click', hlSaveConfig);

    const close = () => hlEl('hmCfgBackdrop').classList.remove('open');
    hlEl('hmCfgClose').addEventListener('click', close);
    hlEl('hmCfgBackdrop').addEventListener('click', e => {
        if (e.target === hlEl('hmCfgBackdrop')) close();
    });
}

function hlDressSelect(select) {
    if (!select || select.dataset.dressed) return;
    select.dataset.dressed = '1';

    const wrap = document.createElement('div');
    wrap.className = 'hm-sel';
    select.parentNode.insertBefore(wrap, select);
    wrap.appendChild(select);

    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'hm-sel-btn';

    const label = document.createElement('span');
    label.className = 'hm-sel-label';

    const caret = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    caret.setAttribute('class', 'hm-sel-caret');
    caret.setAttribute('viewBox', '0 0 16 16');
    caret.setAttribute('aria-hidden', 'true');
    const arrow = document.createElementNS('http://www.w3.org/2000/svg', 'path');
    arrow.setAttribute('d', 'M3 6l5 5 5-5');
    arrow.setAttribute('fill', 'none');
    arrow.setAttribute('stroke', 'currentColor');
    arrow.setAttribute('stroke-width', '2');
    arrow.setAttribute('stroke-linecap', 'square');
    caret.appendChild(arrow);

    button.append(label, caret);

    const list = document.createElement('div');
    list.className = 'hm-sel-list';

    wrap.append(button, list);

    function close() { wrap.classList.remove('open'); }

    function sync() {
        const chosen = select.selectedOptions[0];
        label.textContent = chosen ? chosen.textContent : '';
        button.title = label.textContent;
        button.disabled = select.options.length === 0;

        list.textContent = '';
        [...select.options].forEach(option => {
            const item = document.createElement('button');
            item.type = 'button';
            item.className = 'hm-sel-item' + (option.selected ? ' active' : '');
            item.textContent = option.textContent;
            item.addEventListener('click', event => {
                event.preventDefault();
                event.stopPropagation();
                select.value = option.value;
                close();
                sync();
                select.dispatchEvent(new Event('change', { bubbles: true }));
            });
            list.appendChild(item);
        });
    }

    button.addEventListener('click', event => {
        event.preventDefault();
        event.stopPropagation();
        const wasOpen = wrap.classList.contains('open');
        document.querySelectorAll('.hm-sel.open').forEach(other => other.classList.remove('open'));
        if (!wasOpen) wrap.classList.add('open');
    });

    document.addEventListener('click', close);
    document.addEventListener('keydown', event => { if (event.key === 'Escape') close(); });
    select.addEventListener('change', sync);

    new MutationObserver(sync).observe(select, { childList: true });
    sync();
}

function hlDressSelects() {
    ['hmDemoSelect', 'hmPlayerSelect', 'hmTypeSelect'].forEach(id => hlDressSelect(hlEl(id)));
}
