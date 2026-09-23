/*
 * Renders the commander card and owns the flyout behaviour.
 *
 * Everything below builds DOM nodes and sets textContent. Nothing from the payload is ever passed to
 * innerHTML: the snapshot carries commander-authored strings (ship name, carrier name, mission
 * titles) that originate in the game and pass through the broadcaster's machine, and this code runs
 * in every viewer's browser.
 */
(function (global) {
  'use strict';

  var doc = global.document;
  var STORAGE_KEY = 'ednexus.card.open';

  var root = doc.getElementById('root');
  var handle = doc.getElementById('handle');
  var panel = doc.getElementById('panel');
  var closeButton = doc.getElementById('close');
  var handleHeadline = doc.getElementById('handle-headline');
  var handleSub = doc.getElementById('handle-sub');
  var cmdrName = doc.getElementById('cmdr-name');
  var cmdrWhere = doc.getElementById('cmdr-where');
  var sections = doc.getElementById('sections');
  var staleNote = doc.getElementById('stale');

  var current = null;

  /* ----------------------------------------------------------- formatting -- */

  function num(value, digits) {
    if (typeof value !== 'number' || !isFinite(value)) return null;
    return value.toLocaleString(undefined, {
      minimumFractionDigits: digits || 0,
      maximumFractionDigits: digits || 0,
    });
  }

  /** Credits, abbreviated the way the game's own UI does once the numbers get long. */
  function credits(value) {
    if (typeof value !== 'number' || !isFinite(value)) return null;
    var abs = Math.abs(value);
    if (abs >= 1e9) return (value / 1e9).toFixed(2) + ' bn CR';
    if (abs >= 1e6) return (value / 1e6).toFixed(1) + ' m CR';
    return num(value) + ' CR';
  }

  function text(value) {
    return typeof value === 'string' && value.trim() ? value.trim() : null;
  }

  /** "in 4 min" / "3 h ago" — deliberately coarse, since the card updates every few seconds. */
  function relative(iso) {
    var when = Date.parse(iso);
    if (isNaN(when)) return null;
    var deltaSec = Math.round((when - Date.now()) / 1000);
    var ahead = deltaSec >= 0;
    var abs = Math.abs(deltaSec);
    var value;
    if (abs < 60) value = abs + ' s';
    else if (abs < 3600) value = Math.round(abs / 60) + ' min';
    else value = Math.round(abs / 3600) + ' h';
    return ahead ? 'in ' + value : value + ' ago';
  }

  /* ------------------------------------------------------------- elements -- */

  function el(tag, className, content) {
    var node = doc.createElement(tag);
    if (className) node.className = className;
    if (content != null) node.textContent = String(content);
    return node;
  }

  function section(title) {
    var wrap = el('section', 'ednx-section');
    wrap.appendChild(el('h2', 'ednx-section__title', title));
    return wrap;
  }

  /** A definition list of label/value pairs, skipping any pair whose value is missing. */
  function rows(pairs) {
    var list = el('dl', 'ednx-rows');
    var written = 0;
    pairs.forEach(function (pair) {
      if (pair[1] == null || pair[1] === '') return;
      list.appendChild(el('dt', null, pair[0]));
      list.appendChild(el('dd', pair[2] ? 'ednx-num' : null, pair[1]));
      written++;
    });
    return written ? list : null;
  }

  function meter(fraction, low) {
    var clamped = Math.max(0, Math.min(1, fraction));
    var track = el('div', 'ednx-meter');
    var fill = el('div', 'ednx-meter__fill' + (low ? ' ednx-meter__fill--low' : ''));
    fill.style.width = (clamped * 100).toFixed(1) + '%';
    track.appendChild(fill);
    return track;
  }

  /** The three-sample exobiology run, as the suit shows it. */
  function pips(taken, total) {
    var wrap = el('span', 'ednx-pips');
    for (var i = 0; i < total; i++) {
      wrap.appendChild(el('span', 'ednx-pip' + (i < taken ? ' ednx-pip--on' : '')));
    }
    return wrap;
  }

  /* ------------------------------------------------------------- sections -- */

  function locationSection(loc) {
    if (!loc) return null;
    var body = section('Location');
    var list = rows([
      ['System', text(loc.system)],
      ['Body', text(loc.body)],
      ['Docked at', loc.docked ? text(loc.station) : null],
      ['Station', loc.docked ? text(loc.stationType) : null],
      ['Status', loc.docked ? 'Docked' : 'In flight'],
    ]);
    if (!list) return null;
    body.appendChild(list);
    return body;
  }

  function shipSection(ship) {
    if (!ship) return null;

    var name = text(ship.name);
    var ident = text(ship.ident);
    var label = name && ident ? name + ' (' + ident + ')' : (name || ident);

    var body = section('Ship');
    var list = rows([
      ['Hull', text(ship.type)],
      ['Registered', label],
      ['Cargo', typeof ship.cargo === 'number' ? num(ship.cargo) + ' t' : null],
      ['Jump range', typeof ship.jump === 'number' ? num(ship.jump, 2) + ' ly' : null],
    ]);
    if (list) body.appendChild(list);

    if (typeof ship.fuel === 'number' && typeof ship.fuelMax === 'number' && ship.fuelMax > 0) {
      var fraction = ship.fuel / ship.fuelMax;
      var line = el('div', 'ednx-rank__line');
      line.appendChild(el('span', 'ednx-rank__name', 'Fuel'));
      line.appendChild(el('span', 'ednx-rank__value', num(ship.fuel, 1) + ' / ' + num(ship.fuelMax, 1) + ' t'));
      var group = el('div');
      group.appendChild(line);
      group.appendChild(meter(fraction, fraction < 0.25));
      body.appendChild(group);
    }

    return body.children.length > 1 ? body : null;
  }

  function carrierSection(carrier) {
    if (!carrier) return null;
    var body = section('Fleet carrier');
    var pending = text(carrier.pendingSystem);
    var list = rows([
      ['Name', text(carrier.name)],
      ['Callsign', text(carrier.callsign)],
      ['Tritium', typeof carrier.fuel === 'number' ? num(carrier.fuel) + ' t' : null],
      ['Jump range', typeof carrier.jump === 'number' ? num(carrier.jump) + ' ly' : null],
      ['Jumping to', pending],
      ['Departs', pending ? relative(carrier.departsAt) : null],
    ]);
    if (!list) return null;
    body.appendChild(list);
    return body;
  }

  function commanderSection(cmdr) {
    if (!cmdr) return null;
    var hasCredits = typeof cmdr.credits === 'number';
    var ranks = Array.isArray(cmdr.ranks) ? cmdr.ranks : [];
    if (!hasCredits && !ranks.length) return null;

    var body = section('Commander');

    if (hasCredits) {
      var list = rows([['Balance', credits(cmdr.credits), true]]);
      if (list) body.appendChild(list);
    }

    if (ranks.length) {
      var wrap = el('div', 'ednx-ranks');
      ranks.forEach(function (rank) {
        var label = text(rank.label);
        var name = text(rank.name);
        if (!label || !name) return;

        var group = el('div');
        var line = el('div', 'ednx-rank__line');
        line.appendChild(el('span', 'ednx-rank__name', label));
        line.appendChild(el('span', 'ednx-rank__value' + (rank.elite ? ' ednx-elite' : ''), name));
        group.appendChild(line);

        // An Elite ladder's percentage is progress towards the next Elite tier; a maxed one
        // reports 0, which would read as "no progress" rather than "nothing left to earn".
        if (typeof rank.pct === 'number' && rank.pct > 0) group.appendChild(meter(rank.pct / 100));
        wrap.appendChild(group);
      });
      if (wrap.children.length) body.appendChild(wrap);
    }

    return body.children.length > 1 ? body : null;
  }

  function exobiologySection(exo) {
    if (!exo) return null;
    var body = section('Exobiology');

    var genus = text(exo.genus);
    if (genus) {
      var species = text(exo.species);
      var line = el('div', 'ednx-rank__line');
      line.appendChild(el('span', 'ednx-rank__name', species || genus));
      var value = el('span', 'ednx-rank__value');
      value.appendChild(pips(Math.min(exo.samples || 0, 3), 3));
      line.appendChild(value);
      body.appendChild(line);
    }

    var list = rows([
      ['Body', text(exo.body)],
      ['Bio signals', exo.signals ? num(exo.signals) : null],
      ['Unsold data', exo.pendingValue ? credits(exo.pendingValue) : null],
      ['Samples held', exo.pendingCount ? num(exo.pendingCount) : null],
      ['Sold this session', exo.soldValue ? credits(exo.soldValue) : null],
      ['First discoveries', exo.firsts ? num(exo.firsts) : null],
    ]);
    if (list) body.appendChild(list);

    return body.children.length > 1 ? body : null;
  }

  function missionsSection(missions) {
    if (!missions || !missions.active) return null;
    var body = section('Missions');

    var list = rows([
      ['Held', missions.cap ? num(missions.active) + ' / ' + num(missions.cap) : num(missions.active)],
      ['Total reward', missions.reward ? credits(missions.reward) : null],
    ]);
    if (list) body.appendChild(list);

    var stacks = Array.isArray(missions.stacks) ? missions.stacks : [];
    stacks.forEach(function (stack) {
      var target = text(stack.target) || text(stack.faction);
      if (!target) return;
      var line = el('div', 'ednx-rank__line');
      line.appendChild(el('span', 'ednx-rank__name', target));
      // KillsToClear, not the sum: one kill counts for every mission in the stack at once.
      line.appendChild(el('span', 'ednx-rank__value', num(stack.count) + '× · ' + num(stack.kills) + ' kills'));
      body.appendChild(line);
    });

    return body.children.length > 1 ? body : null;
  }

  function miningSection(mining) {
    if (!mining) return null;
    var body = section('Mining');

    var list = rows([
      ['Last rock', text(mining.content)],
      ['Core', text(mining.motherlode)],
      ['Remaining', typeof mining.remaining === 'number' ? num(mining.remaining, 1) + '%' : null],
      ['Prospected', mining.prospected ? num(mining.prospected) : null],
      ['Refined', mining.refined ? num(mining.refined) + ' t' : null],
    ]);
    if (list) body.appendChild(list);

    var materials = Array.isArray(mining.materials) ? mining.materials : [];
    materials.forEach(function (material) {
      var name = text(material.name);
      if (!name) return;
      var line = el('div', 'ednx-rank__line');
      line.appendChild(el('span', 'ednx-rank__name', name));
      line.appendChild(el('span', 'ednx-rank__value', num(material.pct, 1) + '%'));
      body.appendChild(line);
    });

    return body.children.length > 1 ? body : null;
  }

  function cargoSection(cargo, more) {
    if (!Array.isArray(cargo) || !cargo.length) return null;
    var body = section('Cargo hold');
    var list = el('dl', 'ednx-rows');
    cargo.forEach(function (item) {
      var name = text(item.name);
      if (!name) return;
      list.appendChild(el('dt', null, name));
      list.appendChild(el('dd', 'ednx-num', num(item.t) + ' t'));
    });
    if (!list.children.length) return null;
    body.appendChild(list);

    // The app sends the biggest lots only. Say so, rather than letting a truncated manifest read
    // as the whole hold.
    if (typeof more === 'number' && more > 0) {
      body.appendChild(el('p', 'ednx-more', '+ ' + num(more) + ' more ' + (more === 1 ? 'commodity' : 'commodities')));
    }
    return body;
  }

  /* --------------------------------------------------------------- render -- */

  function render(snapshot) {
    current = snapshot;

    if (snapshot && snapshot.unsupported) {
      handleHeadline.textContent = 'EDNexus';
      handleSub.textContent = '';
      // Clear the header too: a v1 snapshot followed by a newer broadcast would otherwise leave the
      // old commander and location sitting above the incompatibility notice, reading as current.
      cmdrName.textContent = 'CMDR';
      cmdrWhere.textContent = '';
      staleNote.hidden = true;
      sections.replaceChildren(el('p', 'ednx-empty',
        'This commander is running a newer EDNexus than this card understands.'));
      return;
    }

    var headline = text(snapshot && snapshot.headline) || 'Elite Dangerous';
    var subline = text(snapshot && snapshot.subline) || '';
    handleHeadline.textContent = headline;
    handleSub.textContent = subline;
    cmdrWhere.textContent = headline;

    var commander = snapshot && snapshot.cmdr;
    cmdrName.textContent = (commander && text(commander.name)) ? 'CMDR ' + text(commander.name) : 'CMDR';

    var built = [
      locationSection(snapshot && snapshot.loc),
      shipSection(snapshot && snapshot.ship),
      commanderSection(commander),
      exobiologySection(snapshot && snapshot.exo),
      missionsSection(snapshot && snapshot.missions),
      miningSection(snapshot && snapshot.mining),
      carrierSection(snapshot && snapshot.carrier),
      cargoSection(snapshot && snapshot.cargo, snapshot && snapshot.cargoMore),
    ].filter(Boolean);

    if (!built.length) {
      built.push(el('p', 'ednx-empty', 'Waiting for the commander to launch…'));
    }

    sections.replaceChildren.apply(sections, built);
    refreshStale();
  }

  /** Flags a card the app has stopped feeding, so viewers do not read old data as live. */
  function refreshStale() {
    var at = current && current.at ? Date.parse(current.at) : NaN;
    var stale = isNaN(at) || (Date.now() - at) > global.EDNexusState.STALE_AFTER_MS;
    staleNote.hidden = !stale || !current;
    if (stale && current) {
      staleNote.textContent = 'Last update ' + (relative(current.at) || 'unknown');
    }
  }

  /* --------------------------------------------------------------- flyout -- */

  function setOpen(open) {
    root.dataset.open = open ? 'true' : 'false';
    panel.hidden = !open;
    handle.setAttribute('aria-expanded', open ? 'true' : 'false');
    try {
      global.localStorage.setItem(STORAGE_KEY, open ? '1' : '0');
    } catch (err) {
      // Storage can be unavailable in an embedded iframe; the card still works, it just
      // forgets the viewer's choice between page loads.
    }
    if (open) closeButton.focus();
    else handle.focus();
  }

  function restoreOpenState() {
    var stored = null;
    try { stored = global.localStorage.getItem(STORAGE_KEY); } catch (err) { stored = null; }
    // Collapsed unless this viewer previously opened it — the stream comes first.
    root.dataset.open = stored === '1' ? 'true' : 'false';
    panel.hidden = stored !== '1';
    handle.setAttribute('aria-expanded', stored === '1' ? 'true' : 'false');
  }

  handle.addEventListener('click', function () { setOpen(true); });
  closeButton.addEventListener('click', function () { setOpen(false); });
  doc.addEventListener('keydown', function (event) {
    if (event.key === 'Escape' && root.dataset.open === 'true') setOpen(false);
  });

  /* ----------------------------------------------------------------- boot -- */

  restoreOpenState();
  render(null);

  global.EDNexusState.connect({
    onSnapshot: render,
    onStatus: function (status) { root.dataset.state = status; },
  });

  global.setInterval(refreshStale, 30000);
})(window);
