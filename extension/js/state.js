/*
 * Transport for the commander card: where snapshots come from, and nothing about how they look.
 *
 * Two sources feed the same callback:
 *   - GET {ebs}/api/initial-state/{channelId}, once, so a viewer who opens the stream mid-session
 *     sees the card immediately instead of waiting for the commander's next journal event;
 *   - Twitch Extensions PubSub ("broadcast" target), for every update after that.
 *
 * The payload is produced by EDNexus.Core.Twitch.StreamCardSnapshot. Every field is optional —
 * broadcasters choose which sections they publish — so nothing here assumes a section exists.
 */
(function (global) {
  'use strict';

  /** Fallback EBS, used when the broadcaster has not configured one. Keep in sync with AppSettings.Twitch.EbsBaseUrl. */
  var DEFAULT_EBS = 'https://ebs.ednexus.app';

  /** Schema version this frontend understands. See StreamCardSchema.Version on the app side. */
  var SUPPORTED_SCHEMA = 1;

  /** A card older than this is flagged as stale — the app has probably stopped publishing. */
  var STALE_AFTER_MS = 5 * 60 * 1000;

  function parseConfig(raw) {
    if (!raw) return {};
    try {
      var parsed = JSON.parse(raw);
      return parsed && typeof parsed === 'object' ? parsed : {};
    } catch (err) {
      return {};
    }
  }

  /**
   * The EBS the broadcaster configured, from the extension configuration service. Only an https
   * origin is honoured: the configuration string is broadcaster-supplied, and this value is used to
   * build a fetch URL.
   */
  function resolveEbsBase(helper) {
    var configured = '';
    try {
      var broadcaster = helper && helper.configuration && helper.configuration.broadcaster;
      configured = parseConfig(broadcaster && broadcaster.content).ebsBaseUrl || '';
    } catch (err) {
      configured = '';
    }

    if (!configured) return DEFAULT_EBS;

    try {
      var url = new URL(configured);
      if (url.protocol !== 'https:' && url.hostname !== 'localhost' && url.hostname !== '127.0.0.1') {
        return DEFAULT_EBS;
      }
      return url.origin;
    } catch (err) {
      return DEFAULT_EBS;
    }
  }

  /**
   * Narrows whatever arrived to a plain snapshot object. A message that is not an object, or carries
   * a schema version this frontend does not know, is rejected rather than half-rendered.
   */
  function normalize(payload) {
    if (!payload || typeof payload !== 'object') return null;
    var version = typeof payload.v === 'number' ? payload.v : 0;
    if (version > SUPPORTED_SCHEMA) return { unsupported: true, v: version };
    return payload;
  }

  function parseMessage(message) {
    if (typeof message !== 'string') return normalize(message);
    try {
      return normalize(JSON.parse(message));
    } catch (err) {
      return null;
    }
  }

  /**
   * @param {object} handlers
   * @param {function} handlers.onSnapshot Called with each snapshot (or {unsupported:true}).
   * @param {function} handlers.onStatus   Called with 'waiting' | 'live' | 'offline' | 'error'.
   */
  function connect(handlers) {
    var onSnapshot = handlers.onSnapshot || function () {};
    var onStatus = handlers.onStatus || function () {};
    var helper = global.Twitch && global.Twitch.ext;

    // Mock mode: `?mock=1` renders the bundled sample payload with no Twitch and no EBS in the
    // loop, so the card can be developed and reviewed in a plain browser.
    var mock = /[?&]mock=1\b/.test(global.location.search);
    if (mock || !helper) {
      loadMock(onSnapshot, onStatus);
      return;
    }

    onStatus('waiting');

    helper.onAuthorized(function (auth) {
      var base = resolveEbsBase(helper);
      fetchInitialState(base, auth.channelId, onSnapshot, onStatus);
    });

    helper.listen('broadcast', function (_target, _contentType, message) {
      var snapshot = parseMessage(message);
      if (!snapshot) return;
      onStatus('live');
      onSnapshot(snapshot);
    });
  }

  function fetchInitialState(base, channelId, onSnapshot, onStatus) {
    // 404 simply means the commander has not published anything yet this session — the card stays
    // in its "waiting" state until the next PubSub message arrives.
    global.fetch(base + '/api/initial-state/' + encodeURIComponent(channelId), { method: 'GET' })
      .then(function (response) {
        if (response.status === 404) { onStatus('offline'); return null; }
        if (!response.ok) { onStatus('error'); return null; }
        return response.json();
      })
      .then(function (body) {
        var snapshot = normalize(body);
        if (!snapshot) return;
        onStatus('live');
        onSnapshot(snapshot);
      })
      .catch(function () { onStatus('error'); });
  }

  function loadMock(onSnapshot, onStatus) {
    onStatus('waiting');
    global.fetch('dev/sample-state.json')
      .then(function (response) { return response.json(); })
      .then(function (body) {
        var snapshot = normalize(body);
        if (!snapshot) { onStatus('error'); return; }
        // The sample is committed with fixed timestamps; move them relative to now so the card
        // doesn't read as stale, or show a carrier jump that departed hours ago.
        snapshot.at = new Date().toISOString();
        if (snapshot.carrier && snapshot.carrier.departsAt) {
          snapshot.carrier.departsAt = new Date(Date.now() + 15 * 60 * 1000).toISOString();
        }
        onStatus('live');
        onSnapshot(snapshot);
      })
      .catch(function () { onStatus('error'); });
  }

  global.EDNexusState = {
    connect: connect,
    SUPPORTED_SCHEMA: SUPPORTED_SCHEMA,
    STALE_AFTER_MS: STALE_AFTER_MS,
    DEFAULT_EBS: DEFAULT_EBS,
  };
})(window);
