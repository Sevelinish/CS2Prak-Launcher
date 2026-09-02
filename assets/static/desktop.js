'use strict';



(function () {
    const body = document.body;
    const T    = (k, fb) => {
        const v = (typeof window.t === 'function') ? window.t(k) : '';
        return (v && v !== k) ? v : fb;
    };

    
    let apiReady = !!(window.pywebview && window.pywebview.api);
    window.addEventListener('pywebviewready', () => { apiReady = true; syncMax(); });

    function call(name, ...args) {
        const api = window.pywebview && window.pywebview.api;
        if (!api || typeof api[name] !== 'function') return Promise.resolve(null);
        try { return Promise.resolve(api[name](...args)); }
        catch (e) { return Promise.resolve(null); }
    }

    
    const bar = document.getElementById('deskbar');
    if (bar) {
        document.getElementById('dkMin').addEventListener('click', () => call('minimize'));
        document.getElementById('dkMax').addEventListener('click',
            () => call('toggle_maximize').then(paintMax));
        document.getElementById('dkClose').addEventListener('click', closeToTray);

        
        bar.addEventListener('dblclick', e => {
            if (e.target.closest('.dk-win')) return;
            call('toggle_maximize').then(paintMax);
        });

        
        let t = null;
        window.addEventListener('resize', () => {
            clearTimeout(t);
            t = setTimeout(syncMax, 120);
        });
        syncMax();
    }

    function paintMax(maximized) {
        if (maximized === null || maximized === undefined) return;
        body.classList.toggle('is-max', !!maximized);
        const b = document.getElementById('dkMax');
        if (!b) return;
        const key = maximized ? 'deskbar.restore' : 'deskbar.max';
        b.title = T(key, maximized ? 'Restore' : 'Maximize');
        b.setAttribute('aria-label', b.title);
        b.setAttribute('data-i18n', key);
    }

    function syncMax() {
        if (!apiReady) return;
        call('is_maximized').then(paintMax);
    }

    
    function closeToTray() {
        let seen = false;
        try { seen = localStorage.getItem('cs2prak_tray_seen') === '1'; } catch (e) {}
        const bd = document.getElementById('trayHintBackdrop');
        if (seen || !bd) { call('hide_to_tray'); return; }
        bd.classList.add('open');
    }

    (function trayHint() {
        const bd = document.getElementById('trayHintBackdrop');
        if (!bd) return;
        document.getElementById('trayHintGo').addEventListener('click', () => {
            try { localStorage.setItem('cs2prak_tray_seen', '1'); } catch (e) {}
            bd.classList.remove('open');
            call('hide_to_tray');
        });
        document.getElementById('trayHintQuit').addEventListener('click', () => {
            bd.classList.remove('open');
            call('quit');
        });
    })();

    body.classList.add('has-deskbar');
})();
