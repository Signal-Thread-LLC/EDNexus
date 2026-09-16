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

  if (helper) {
    helper.onAuthorized(function () {
      input.value = currentConfig().ebsBaseUrl || '';
    });
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
