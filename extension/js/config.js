/*
 * Broadcaster config page. Writes a single { "ebsBaseUrl": "..." } document to the extension
 * configuration service, which js/state.js reads back on the viewer side.
 */
(function (global) {
  'use strict';

  var doc = global.document;
  var input = doc.getElementById('ebs');
  var saveButton = doc.getElementById('save');
  var status = doc.getElementById('status');
  var helper = global.Twitch && global.Twitch.ext;

  function say(message, isError) {
    status.textContent = message;
    status.className = 'status' + (isError ? ' status--error' : ' status--ok');
  }

  function currentConfig() {
    try {
      var broadcaster = helper && helper.configuration && helper.configuration.broadcaster;
      if (!broadcaster || !broadcaster.content) return {};
      var parsed = JSON.parse(broadcaster.content);
      return parsed && typeof parsed === 'object' ? parsed : {};
    } catch (err) {
      return {};
    }
  }

  /** Same rule the viewer side enforces: https, or a loopback host for local development. */
  function normalizeUrl(raw) {
    var value = (raw || '').trim();
    if (!value) return '';
    var url;
    try {
      url = new URL(value);
    } catch (err) {
      return null;
    }
    var loopback = url.hostname === 'localhost' || url.hostname === '127.0.0.1';
    if (url.protocol !== 'https:' && !loopback) return null;
    return url.origin;
  }

  // onAuthorized does not mean the configuration has arrived — Twitch delivers that through
  // configuration.onChanged. Reading it on authorization showed an empty field to a broadcaster who
  // had already saved a URL.
  if (helper) {
    var loaded = false;

    function showSavedUrl() {
      // Never clobber an edit in progress: onChanged also fires on a token refresh, and on the
      // broadcaster's own save.
      if (loaded || input.value.trim() !== '') return;
      loaded = true;
      input.value = currentConfig().ebsBaseUrl || '';
    }

    if (helper.configuration && typeof helper.configuration.onChanged === 'function') {
      helper.configuration.onChanged(showSavedUrl);
    }
    // A channel with no configuration document yet never gets an onChanged, so the field simply
    // stays empty and the placeholder explains the default.
    helper.onAuthorized(showSavedUrl);
  }

  saveButton.addEventListener('click', function () {
    var normalized = normalizeUrl(input.value);
    if (normalized === null) {
      say('That is not a valid https URL.', true);
      return;
    }

    if (!helper || !helper.configuration) {
      say('Twitch configuration service unavailable.', true);
      return;
    }

    try {
      helper.configuration.set('broadcaster', '1', JSON.stringify({ ebsBaseUrl: normalized }));
      input.value = normalized;
      say(normalized ? 'Saved.' : 'Cleared — using the default EBS.');
    } catch (err) {
      say('Could not save: ' + err.message, true);
    }
  });
})(window);
