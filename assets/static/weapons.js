'use strict';


(function () {
    let SET = new Set();
    fetch('/static/weapon_icons/index.json')
        .then(r => r.json())
        .then(a => { SET = new Set(a); document.dispatchEvent(new Event('weaponicons')); })
        .catch(() => {});

    const slug = s => String(s || '').toLowerCase()
        .replace(/^weapon_/, '')
        .replace(/[^a-z0-9]/g, '');

    const KNIFE_RE = /knife|bayonet|karambit|daggers|talon|ursus|stiletto|nomad|skeleton|navaja|paracord|survival|classic|gut|flip|falchion|shadow|butterfly|huntsman|bowie|kukri|m9/i;

    
    const ALIAS = {
        uspsilencer: 'usps', usps: 'usps', hkp2000: 'p2000',
        m4a1silencer: 'm4a1s', m4a1: 'm4a1s', galil: 'galilar',
        elite: 'dualberettas', deagle: 'deserteagle', revolver: 'r8revolver',
        bizon: 'ppbizon', mp7a1: 'mp7', hegrenade: 'highexplosivegrenade',
        incgrenade: 'incendiarygrenade', inferno: 'molotov',
        smokegrenade: 'smokegrenade', decoy: 'decoygrenade', c4: 'c4explosive',
        knifet: 'knife', taser: 'zeusx27', zeus: 'zeusx27',
        glock: 'glock18', cz75a: 'cz75auto', sg556: 'sg553', scar17: 'scar20',
        mp5navy: 'mp5sd', ump: 'ump45', m4a1s: 'm4a1s',
    };

    
    const NO_WEAPON = new Set(['world', 'worldspawn', 'trigger_hurt', '']);

    
    function icon(name) {
        const s = slug(name);
        if (!s || NO_WEAPON.has(s)) return null;
        if (SET.has(s)) return s;
        if (ALIAS[s] && SET.has(ALIAS[s])) return ALIAS[s];
        if (KNIFE_RE.test(String(name)) && SET.has('knife')) return 'knife';
        return null;
    }

    
    function html(name, cls) {
        const sl = icon(name);
        const label = String(name == null ? '' : name);
        const safe = label.replace(/[&<>"]/g, c =>
            ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
        if (!sl) return '<span class="wic-txt">' + safe + '</span>';
        return '<i class="wic ' + (cls || '') + '" title="' + safe + '" role="img" aria-label="' + safe +
               '" style="-webkit-mask-image:url(/static/weapon_icons/' + sl +
               '.png);mask-image:url(/static/weapon_icons/' + sl + '.png)"></i>';
    }

    window.WeaponIcons = { icon, html, slug, ready: () => SET.size > 0 };
})();
