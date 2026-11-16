// Local simulation control page (N-34). Vanilla JS, no dependencies, offline.
(function () {
  'use strict';

  var PRESETS = [0.1, 0.25, 0.5, 1, 2, 5, 10, 20, 50, 100];
  var POLL_MS = 1000;
  var LIVE_REFRESH_S = 1; // Google Earth live KML refresh interval
  var API = '/api/simulation';

  function $(id) { return document.getElementById(id); }
  var el = {
    status: $('status-line'), state: $('f-state'), mission: $('f-mission'),
    t: $('f-t'), utc: $('f-utc'), speedFact: $('f-speed'), badge: $('badge-paused'),
    slider: $('speed'), label: $('speed-label'), hint: $('jump-hint'),
    pause: $('btn-pause'), start: $('btn-start'), stop: $('btn-stop'),
    mission_in: $('in-mission'), countdown: $('in-countdown'), error: $('error-line')
  };

  var lastState = null;
  var lastAnnounced = '';
  var busy = false;

  function fmtSpeed(v) { return String(v); }

  function speedText(v) {
    return v === 1 ? '1× = real time' : fmtSpeed(v) + '×';
  }

  function updateSpeedLabel(v) {
    el.label.textContent = speedText(v);
    el.slider.setAttribute('aria-valuetext', fmtSpeed(v) + ' times real time');
    el.hint.textContent = 'At ' + fmtSpeed(v) + '× the vehicle moves about ' +
      fmtSpeed(Math.round(v * LIVE_REFRESH_S * 100) / 100) + ' s of flight per ' +
      LIVE_REFRESH_S + ' s refresh.';
  }

  function nearestPreset(v) {
    var best = 0;
    for (var i = 1; i < PRESETS.length; i++) {
      if (Math.abs(PRESETS[i] - v) < Math.abs(PRESETS[best] - v)) best = i;
    }
    return best;
  }

  function fmtOffset(sec) {
    if (typeof sec !== 'number' || !isFinite(sec)) return '-';
    var sign = sec < 0 ? 'T-' : 'T+';
    var s = Math.floor(Math.abs(sec));
    var p = function (n) { return (n < 10 ? '0' : '') + n; };
    return sign + p(Math.floor(s / 3600)) + ':' + p(Math.floor(s / 60) % 60) + ':' + p(s % 60);
  }

  function showError(msg) { el.error.textContent = msg || ''; }

  function announce(text) {
    if (text !== lastAnnounced) { el.status.textContent = text; lastAnnounced = text; }
  }

  function render(st) {
    var state = String(st.state || '').toLowerCase();
    var running = state === 'running', countdown = state === 'countdown';
    var active = running || countdown;

    el.state.textContent = st.state || '-';
    el.mission.textContent = st.mission || '-';
    el.t.textContent = active || state === 'completed' ? fmtOffset(st.missionOffsetSeconds) : '-';
    el.utc.textContent = st.launchTimeUtc && typeof st.missionOffsetSeconds === 'number'
      ? new Date(Date.parse(st.launchTimeUtc) + st.missionOffsetSeconds * 1000).toISOString().slice(0, 19) + 'Z'
      : '-';
    el.speedFact.textContent = typeof st.speed === 'number' ? speedText(st.speed) : '-';
    el.badge.hidden = !st.paused;

    el.slider.disabled = !active;
    el.pause.disabled = !(running || st.paused);
    el.pause.textContent = st.paused ? 'Resume' : 'Pause';
    el.start.disabled = active;
    el.stop.disabled = !active;

    if (typeof st.speed === 'number' && document.activeElement !== el.slider) {
      el.slider.value = nearestPreset(st.speed);
      updateSpeedLabel(PRESETS[el.slider.value]);
    }

    var text;
    if (countdown) text = 'Countdown: ' + fmtOffset(st.missionOffsetSeconds);
    else if (running) text = st.paused ? 'Paused' : 'Running';
    else if (state === 'completed') text = 'Flight complete – Start to replay';
    else text = 'No simulation running – Start' + (st.reason ? ' (' + st.reason + ')' : '');
    // Announce only state changes, not the per-second T+ updates.
    var key = state + '|' + !!st.paused;
    if (key !== lastState) { lastState = key; announce(text); }
  }

  function readError(resp) {
    return resp.text().then(function (body) {
      var detail = '';
      try {
        var j = JSON.parse(body);
        detail = j.reason || j.detail || j.title || j.error || '';
      } catch (e) { detail = body.slice(0, 200); }
      var base = {
        400: 'Invalid request',
        403: 'Only allowed from this computer',
        409: 'Not possible in the current state',
        415: 'Unsupported content type'
      }[resp.status] || ('Server error ' + resp.status);
      return base + (detail ? ': ' + detail : '') + ' (HTTP ' + resp.status + ')';
    });
  }

  function call(method, path, body) {
    var opts = { method: method, headers: { 'Content-Type': 'application/json' } };
    if (body !== undefined) opts.body = JSON.stringify(body);
    return fetch(API + path, opts).then(function (resp) {
      if (!resp.ok) return readError(resp).then(function (m) { throw new Error(m); });
      return resp.json();
    });
  }

  function act(promise) {
    return promise.then(function (st) { showError(''); render(st); })
      .catch(function (e) { showError(e.message || 'Request failed'); });
  }

  function poll() {
    if (document.hidden || busy) return;
    fetch(API, { headers: { 'Accept': 'application/json' } })
      .then(function (resp) {
        if (!resp.ok) return readError(resp).then(function (m) { throw new Error(m); });
        return resp.json();
      })
      .then(function (st) { render(st); })
      .catch(function (e) {
        lastState = null;
        announce('Server not reachable – retrying');
        showError(e.message === 'Failed to fetch' ? '' : e.message);
      });
  }

  el.slider.addEventListener('input', function () { updateSpeedLabel(PRESETS[el.slider.value]); });
  el.slider.addEventListener('change', function () {
    act(call('PUT', '/speed', { speed: PRESETS[el.slider.value] }));
  });
  el.pause.addEventListener('click', function () {
    act(call('POST', el.badge.hidden ? '/pause' : '/resume', {}));
  });
  el.start.addEventListener('click', function () {
    var cd = parseInt(el.countdown.value, 10);
    act(call('POST', '/start', {
      mission: el.mission_in.value.trim(),
      speed: PRESETS[el.slider.value],
      countdownSeconds: isNaN(cd) ? 10 : cd
    }));
  });
  el.stop.addEventListener('click', function () { act(call('POST', '/stop', {})); });

  updateSpeedLabel(PRESETS[el.slider.value]);
  poll();
  setInterval(poll, POLL_MS);
  document.addEventListener('visibilitychange', poll);
})();
