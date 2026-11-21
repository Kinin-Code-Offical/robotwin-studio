/* RobotWin Studio docs: menu, search, copy buttons and the download links of the latest release. */
(function () {
  'use strict';
  var RW = window.RW || { t: {} };
  var t = RW.t || {};

  // ---------- menu (narrow screens) ----------
  var menu = document.querySelector('[data-menu]');
  if (menu) {
    if (!document.querySelector('[data-side]')) menu.style.visibility = 'hidden';
    menu.addEventListener('click', function () {
      var open = document.body.classList.toggle('menu-open');
      menu.setAttribute('aria-expanded', open ? 'true' : 'false');
    });
  }

  // ---------- copy buttons on code blocks ----------
  document.querySelectorAll('.prose pre').forEach(function (pre) {
    var b = document.createElement('button');
    b.type = 'button';
    b.className = 'copy';
    b.textContent = t.copy || 'Copy';
    b.addEventListener('click', function () {
      var code = pre.querySelector('code') || pre;
      navigator.clipboard.writeText(code.innerText).then(function () {
        b.textContent = t.copied || 'Copied';
        setTimeout(function () { b.textContent = t.copy || 'Copy'; }, 1500);
      });
    });
    pre.appendChild(b);
  });

  // ---------- search ----------
  var input = document.querySelector('[data-search]');
  var list = document.querySelector('[data-results]');
  var index = null, loading = null;

  function load() {
    if (index || loading) return loading;
    loading = fetch(RW.searchUrl).then(function (r) { return r.json(); }).then(function (items) {
      index = items.map(function (it) {
        var div = document.createElement('div');
        div.innerHTML = it.html;
        return { title: it.title, url: it.url, text: div.textContent.replace(/\s+/g, ' ') };
      });
      return index;
    });
    return loading;
  }

  function norm(s) { return s.toLocaleLowerCase(RW.lang === 'tr' ? 'tr' : 'en'); }

  function show(q) {
    q = q.trim();
    if (q.length < 2) { list.hidden = true; return; }
    load().then(function () {
      var words = norm(q).split(/\s+/);
      var hits = [];
      index.forEach(function (it) {
        var title = norm(it.title), text = norm(it.text), score = 0, first = -1;
        for (var i = 0; i < words.length; i++) {
          var w = words[i], at = text.indexOf(w);
          if (title.indexOf(w) >= 0) score += 10;
          if (at >= 0) { score += 1; if (first < 0) first = at; }
          else if (title.indexOf(w) < 0) return;
        }
        var snip = first >= 0 ? it.text.substr(Math.max(0, first - 40), 120) : '';
        hits.push({ it: it, score: score, snip: snip });
      });
      hits.sort(function (a, b) { return b.score - a.score; });
      list.innerHTML = '';
      if (!hits.length) {
        var li = document.createElement('li');
        li.className = 'empty';
        li.textContent = t.search_empty || 'No results.';
        list.appendChild(li);
      }
      hits.slice(0, 12).forEach(function (h) {
        var li = document.createElement('li'), a = document.createElement('a'), s = document.createElement('small');
        a.href = h.it.url;
        a.textContent = h.it.title;
        s.textContent = h.snip ? '… ' + h.snip + ' …' : '';
        a.appendChild(s);
        li.appendChild(a);
        list.appendChild(li);
      });
      list.hidden = false;
    }).catch(function () {
      list.innerHTML = '<li class="empty"></li>';
      list.firstChild.textContent = t.search_error || 'Search unavailable.';
      list.hidden = false;
    });
  }

  if (input && list) {
    input.addEventListener('input', function () { show(input.value); });
    input.addEventListener('focus', function () { load(); if (input.value) show(input.value); });
    input.addEventListener('keydown', function (e) {
      if (e.key === 'Escape') { input.value = ''; list.hidden = true; input.blur(); }
      if (e.key === 'Enter') { var a = list.querySelector('a'); if (a) location.href = a.href; }
    });
    document.addEventListener('click', function (e) { if (!e.target.closest('.search')) list.hidden = true; });
    document.addEventListener('keydown', function (e) {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') { e.preventDefault(); input.focus(); }
    });
  }

  // ---------- downloads: the latest release's two installers ----------
  var box = document.querySelector('[data-downloads]');
  if (box && RW.api) {
    fetch(RW.api + '/releases?per_page=10').then(function (r) {
      if (!r.ok) throw new Error(r.status);
      return r.json();
    }).then(function (rels) {
      var rel = rels.filter(function (r) { return !r.draft && !r.prerelease; })[0];
      if (!rel) return;
      var exes = rel.assets.filter(function (a) { return /^RobotWinStudio-Setup-.+\.exe$/i.test(a.name); });
      // From 1.1 the full package has the plain name and the small one ends in -lite;
      // older releases called the full one -tam and the small one plain.
      var lite = exes.filter(function (a) { return /-lite\.exe$/i.test(a.name); })[0];
      var full, small;
      if (lite) { small = lite; full = exes.filter(function (a) { return a !== lite; })[0]; }
      else { full = exes.filter(function (a) { return /-tam\.exe$/i.test(a.name); })[0]; small = exes.filter(function (a) { return !/-tam\.exe$/i.test(a.name); })[0]; }
      function mb(n) { return n ? ' · ' + (n / 1048576).toFixed(n < 10485760 ? 1 : 0) + ' MB' : ''; }
      var fb = box.querySelector('[data-pkg="full"]'), lb = box.querySelector('[data-pkg="lite"]');
      if (full && fb) { fb.href = full.browser_download_url; fb.querySelector('.btn-note').textContent += mb(full.size); }
      if (small && lb) { lb.href = small.browser_download_url; lb.querySelector('.btn-note').textContent += mb(small.size); }
      if (!small && lb) lb.style.display = 'none';
      var meta = document.querySelector('[data-release]');
      if (meta) {
        var a = document.createElement('a');
        a.href = rel.html_url;
        a.textContent = rel.tag_name;
        meta.insertBefore(document.createTextNode(' · '), meta.firstChild);
        meta.insertBefore(a, meta.firstChild);
      }
    }).catch(function () { /* the buttons already point at the releases page */ });
  }
})();
