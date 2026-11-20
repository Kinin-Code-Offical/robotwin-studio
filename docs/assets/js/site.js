/* RobotWin Studio site script: navigation, search, docs helpers, lightbox,
   latest-release download buttons and the news feed. No dependencies. */
(function () {
  'use strict';

  var RW = window.RW || {};
  var T = RW.t || {};
  var LANG = RW.lang === 'en' ? 'en' : 'tr';
  var L = {
    tr: {
      announce: 'Duyuru', release: 'Sürüm', pre: 'Ön sürüm', more: 'Devamını göster', less: 'Daha az göster',
      details: 'Ayrıntılar', failed: 'Haberler şu anda yüklenemedi.', none: 'Henüz haber yok.',
      looking: 'Son sürüm aranıyor…', guide: 'Kılavuz', docs: 'Belgeler', downloads: 'İndirilebilir dosyalar',
      noRelease: 'Henüz yayımlanmış bir sürüm yok.', anchor: 'Bu başlığın bağlantısı'
    },
    en: {
      announce: 'Announcement', release: 'Release', pre: 'Pre-release', more: 'Show more', less: 'Show less',
      details: 'Details', failed: 'News could not be loaded right now.', none: 'No news yet.',
      looking: 'Looking up the latest release…', guide: 'Guide', docs: 'Docs', downloads: 'Downloads',
      noRelease: 'No release has been published yet.', anchor: 'Link to this heading'
    }
  }[LANG];

  function $(sel, root) { return (root || document).querySelector(sel); }
  function $$(sel, root) { return Array.prototype.slice.call((root || document).querySelectorAll(sel)); }
  function esc(s) {
    return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
    });
  }
  function store(kind) {
    try { var s = window[kind]; var k = '__rw'; s.setItem(k, '1'); s.removeItem(k); return s; } catch (e) { return null; }
  }

  /* ------------------------------------------------------------------ */
  /* Navigation drawer                                                   */
  /* ------------------------------------------------------------------ */

  function initNav() {
    var toggle = $('[data-nav-toggle]');
    if (!toggle) return;
    function set(open) {
      document.body.classList.toggle('nav-open', open);
      toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
    }
    toggle.addEventListener('click', function () { set(!document.body.classList.contains('nav-open')); });
    $$('[data-nav-close]').forEach(function (el) { el.addEventListener('click', function () { set(false); }); });
    $$('.hdr-nav a, .sidebar a').forEach(function (a) { a.addEventListener('click', function () { set(false); }); });
    document.addEventListener('keydown', function (e) {
      if (e.key === 'Escape' && document.body.classList.contains('nav-open')) set(false);
    });
    window.matchMedia('(min-width: 861px)').addEventListener('change', function (m) { if (m.matches) set(false); });

    var current = $('.sidebar [aria-current="page"]');
    var sidebar = $('.sidebar');
    if (current && sidebar) {
      var top = current.offsetTop - sidebar.clientHeight / 3;
      if (top > 0) sidebar.scrollTop = top;
    }
  }

  /* ------------------------------------------------------------------ */
  /* Dialog helpers                                                      */
  /* ------------------------------------------------------------------ */

  function openDialog(d) {
    if (!d) return;
    if (typeof d.showModal === 'function') { if (!d.open) d.showModal(); }
    else d.setAttribute('open', '');
    document.documentElement.style.overflow = 'hidden';
  }
  function closeDialog(d) {
    if (!d) return;
    if (typeof d.close === 'function' && d.open) d.close();
    else d.removeAttribute('open');
  }
  function bindDialog(d) {
    d.addEventListener('close', function () { document.documentElement.style.overflow = ''; });
    d.addEventListener('click', function (e) { if (e.target === d) closeDialog(d); });
  }

  /* ------------------------------------------------------------------ */
  /* Search                                                              */
  /* ------------------------------------------------------------------ */

  var FOLD = { 'ı': 'i', 'ş': 's', 'ğ': 'g', 'ü': 'u', 'ö': 'o', 'ç': 'c', 'â': 'a', 'î': 'i', 'û': 'u', 'é': 'e', 'ä': 'a' };
  function fold(s) {
    var lower = String(s).toLocaleLowerCase('tr');
    if (lower.length !== s.length) lower = String(s).toLowerCase();
    return lower.replace(/[ışğüöçâîûéä]/g, function (c) { return FOLD[c]; });
  }

  var index = null, indexPromise = null;

  function buildIndex(pages) {
    var parser = new DOMParser();
    var sections = [];
    pages.forEach(function (p) {
      var doc = parser.parseFromString('<div>' + p.html + '</div>', 'text/html');
      var root = doc.body.firstElementChild;
      var cur = { page: p.title, group: p.group, url: p.url, id: '', heading: p.title, parts: [] };
      var push = function () {
        var text = cur.parts.join(' ').replace(/\s+/g, ' ').trim();
        if (text || cur.id) {
          sections.push({
            page: cur.page, group: cur.group, url: cur.url + (cur.id ? '#' + cur.id : ''),
            heading: cur.heading, text: text, fh: fold(cur.heading), ft: fold(text), fp: fold(cur.page)
          });
        }
      };
      Array.prototype.forEach.call(root.children, function (el) {
        var tag = el.tagName;
        if (tag === 'H1') { return; }
        if (tag === 'H2' || tag === 'H3') {
          push();
          cur = { page: p.title, group: p.group, url: p.url, id: el.id || '', heading: el.textContent.trim(), parts: [] };
          return;
        }
        cur.parts.push(el.textContent);
      });
      push();
    });
    return sections;
  }

  function loadIndex() {
    if (index) return Promise.resolve(index);
    if (!indexPromise) {
      indexPromise = fetch(RW.searchUrl, { credentials: 'same-origin' })
        .then(function (r) { if (!r.ok) throw new Error(r.status); return r.json(); })
        .then(function (pages) { index = buildIndex(pages); return index; })
        .catch(function (e) { indexPromise = null; throw e; });
    }
    return indexPromise;
  }

  function search(q) {
    var fq = fold(q.trim());
    var terms = fq.split(/\s+/).filter(Boolean);
    if (!terms.length) return [];
    var res = [];
    index.forEach(function (s) {
      var score = 0;
      for (var i = 0; i < terms.length; i++) {
        var term = terms[i];
        var inH = s.fh.indexOf(term) !== -1, inT = s.ft.indexOf(term) !== -1, inP = s.fp.indexOf(term) !== -1;
        if (!inH && !inT && !inP) return;
        if (inH) score += 10;
        if (inP) score += 3;
        if (inT) {
          var n = 0, pos = -1;
          while ((pos = s.ft.indexOf(term, pos + 1)) !== -1 && n < 5) n++;
          score += n;
        }
      }
      if (terms.length > 1) {
        if (s.fh.indexOf(fq) !== -1) score += 15;
        else if (s.ft.indexOf(fq) !== -1) score += 6;
      }
      if (s.group === 'guide') score += 0.5;
      res.push({ s: s, score: score });
    });
    res.sort(function (a, b) { return b.score - a.score; });
    return res.slice(0, 20).map(function (r) { return r.s; });
  }

  function highlight(orig, folded, terms) {
    if (orig.length !== folded.length) return esc(orig);
    var marks = [];
    terms.forEach(function (t) {
      var pos = -1;
      while ((pos = folded.indexOf(t, pos + 1)) !== -1) marks.push([pos, pos + t.length]);
    });
    if (!marks.length) return esc(orig);
    marks.sort(function (a, b) { return a[0] - b[0]; });
    var out = '', last = 0;
    marks.forEach(function (m) {
      if (m[0] < last) return;
      out += esc(orig.slice(last, m[0])) + '<mark>' + esc(orig.slice(m[0], m[1])) + '</mark>';
      last = m[1];
    });
    return out + esc(orig.slice(last));
  }

  function snippet(s, terms) {
    var first = -1;
    terms.forEach(function (t) { var p = s.ft.indexOf(t); if (p !== -1 && (first === -1 || p < first)) first = p; });
    if (first === -1) first = 0;
    var start = Math.max(0, first - 50), end = Math.min(s.text.length, start + 170);
    var o = s.text.slice(start, end), f = s.ft.slice(start, end);
    return (start > 0 ? '…' : '') + highlight(o, f, terms) + (end < s.text.length ? '…' : '');
  }

  function initSearch() {
    var dlg = $('#search-dialog'), input = $('#search-input'), box = $('#search-results');
    if (!dlg || !input || !box) return;
    bindDialog(dlg);
    var sel = -1;

    function msg(text) { box.innerHTML = '<p class="search-msg">' + esc(text) + '</p>'; sel = -1; }
    function links() { return $$('a', box); }
    function select(i) {
      var ls = links();
      if (!ls.length) return;
      sel = (i + ls.length) % ls.length;
      ls.forEach(function (a, k) { a.setAttribute('aria-selected', k === sel ? 'true' : 'false'); });
      ls[sel].scrollIntoView({ block: 'nearest' });
    }
    function run() {
      var q = input.value;
      if (!q.trim()) { msg(T.search_hint || ''); return; }
      if (!index) {
        msg(T.search_loading || '');
        loadIndex().then(run, function () { msg(T.search_error || ''); });
        return;
      }
      var terms = fold(q.trim()).split(/\s+/).filter(Boolean);
      var results = search(q);
      if (!results.length) { msg(T.search_empty || ''); return; }
      box.innerHTML = results.map(function (s) {
        var path = (s.group === 'guide' ? L.guide : L.docs) + ' › ' + s.page;
        return '<a role="option" aria-selected="false" href="' + esc(s.url) + '">' +
          '<span class="sr-path">' + esc(path) + '</span>' +
          '<span class="sr-title">' + highlight(s.heading, s.fh, terms) + '</span>' +
          (s.text ? '<span class="sr-snip">' + snippet(s, terms) + '</span>' : '') + '</a>';
      }).join('');
      select(0);
    }
    function open() {
      openDialog(dlg);
      input.focus();
      input.select();
      loadIndex().catch(function () {});
      run();
    }

    $$('[data-search-open]').forEach(function (b) { b.addEventListener('click', open); });
    $$('[data-search-close]').forEach(function (b) { b.addEventListener('click', function () { closeDialog(dlg); }); });
    input.addEventListener('input', run);
    input.addEventListener('keydown', function (e) {
      if (e.key === 'ArrowDown') { e.preventDefault(); select(sel + 1); }
      else if (e.key === 'ArrowUp') { e.preventDefault(); select(sel - 1); }
      else if (e.key === 'Enter') {
        var ls = links();
        if (ls[sel]) { e.preventDefault(); ls[sel].click(); }
      }
    });
    box.addEventListener('click', function (e) {
      var a = e.target.closest('a');
      if (a) closeDialog(dlg);
    });
    document.addEventListener('keydown', function (e) {
      var typing = /^(INPUT|TEXTAREA|SELECT)$/.test(e.target.tagName) || e.target.isContentEditable;
      if ((e.key === 'k' || e.key === 'K') && (e.ctrlKey || e.metaKey)) { e.preventDefault(); open(); }
      else if (e.key === '/' && !typing) { e.preventDefault(); open(); }
    });
  }

  /* ------------------------------------------------------------------ */
  /* Docs page helpers: anchors, TOC, figures, tables, code, links        */
  /* ------------------------------------------------------------------ */

  function slug(text) {
    return fold(text).replace(/[^\w\- ]+/g, '').trim().replace(/\s+/g, '-');
  }

  function initDocs() {
    var content = $('#content');
    if (!content) return;

    // Heading ids and anchors
    var used = {};
    var heads = $$('h2, h3, h4', content);
    heads.forEach(function (h) {
      if (!h.id) {
        var base = slug(h.textContent) || 'bolum', id = base, n = 1;
        while (used[id] || document.getElementById(id)) id = base + '-' + n++;
        h.id = id;
      }
      used[h.id] = true;
      var a = document.createElement('a');
      a.className = 'anchor';
      a.href = '#' + h.id;
      a.setAttribute('aria-label', L.anchor);
      a.textContent = '#';
      h.insertBefore(a, h.firstChild);
    });

    // Table of contents
    var tocHeads = heads.filter(function (h) { return h.tagName !== 'H4'; });
    if (tocHeads.length >= 2) {
      var html = tocHeads.map(function (h) {
        var text = h.textContent.replace(/^#/, '').trim();
        return '<li class="lvl-' + h.tagName.charAt(1) + '"><a href="#' + esc(h.id) + '">' + esc(text) + '</a></li>';
      }).join('');
      $$('.toc-list').forEach(function (ol) { ol.innerHTML = html; });
      $$('.toc-sticky, .toc-inline').forEach(function (el) { el.hidden = false; });

      var tocLinks = $$('.toc-aside .toc-list a');
      var ticking = false;
      var spy = function () {
        ticking = false;
        var y = 120, active = null;
        for (var i = 0; i < tocHeads.length; i++) {
          if (tocHeads[i].getBoundingClientRect().top <= y) active = tocHeads[i].id; else break;
        }
        tocLinks.forEach(function (a) { a.classList.toggle('active', a.getAttribute('href') === '#' + active); });
      };
      window.addEventListener('scroll', function () { if (!ticking) { ticking = true; requestAnimationFrame(spy); } }, { passive: true });
      spy();
      $$('.toc-inline a').forEach(function (a) { a.addEventListener('click', function () { $('.toc-inline').open = false; }); });
    }

    // Images: standalone images become figures with captions
    $$('img', content).forEach(function (img) {
      img.loading = 'lazy';
      img.decoding = 'async';
      var p = img.parentElement;
      if (p.tagName === 'A') return;
      img.setAttribute('data-zoom', '');
      if (p.tagName === 'P' && p.children.length === 1 && p.textContent.trim() === '') {
        var fig = document.createElement('figure');
        fig.appendChild(img);
        var alt = img.getAttribute('alt');
        if (alt) {
          var cap = document.createElement('figcaption');
          cap.textContent = alt;
          fig.appendChild(cap);
        }
        p.parentNode.replaceChild(fig, p);
      }
    });

    // Tables: scroll container, drop empty header rows
    $$('table', content).forEach(function (table) {
      var thead = table.tHead;
      if (thead && thead.textContent.trim() === '') thead.parentNode.removeChild(thead);
      if (table.parentElement.classList.contains('table-wrap')) return;
      var wrap = document.createElement('div');
      wrap.className = 'table-wrap';
      table.parentNode.insertBefore(wrap, table);
      wrap.appendChild(table);
    });

    // Code blocks: copy button
    $$('pre', content).forEach(function (pre) {
      var target = pre.closest('.highlighter-rouge') || pre;
      if (target.parentElement.classList.contains('code-wrap')) return;
      var wrap = document.createElement('div');
      wrap.className = 'code-wrap';
      target.parentNode.insertBefore(wrap, target);
      wrap.appendChild(target);
      var btn = document.createElement('button');
      btn.type = 'button';
      btn.className = 'copy-btn';
      btn.textContent = T.copy || 'Copy';
      btn.addEventListener('click', function () {
        var text = pre.innerText.replace(/\n$/, '');
        var done = function () {
          btn.textContent = T.copied || 'Copied';
          setTimeout(function () { btn.textContent = T.copy || 'Copy'; }, 1600);
        };
        if (navigator.clipboard && navigator.clipboard.writeText) navigator.clipboard.writeText(text).then(done, function () {});
      });
      wrap.appendChild(btn);
    });

    // Links that point outside docs/ (e.g. ../README.md) or to Markdown that
    // was not converted: send them to the file on GitHub.
    var dir = (RW.path || '').replace(/[^\/]*$/, '');
    var fake = 'https://repo.invalid/docs/' + dir;
    $$('a[href]', content).forEach(function (a) {
      var href = a.getAttribute('href');
      if (!href || /^(#|\/|[a-z][a-z0-9+.-]*:)/i.test(href)) return;
      var u;
      try { u = new URL(href, fake); } catch (e) { return; }
      var path = u.pathname;
      var outside = path.indexOf('/docs/') !== 0;
      if (outside || /\.md$/i.test(path)) {
        var kind = /\/$/.test(path) ? '/tree/main' : '/blob/main';
        a.href = (RW.repo || '') + kind + path + u.hash;
      }
    });
  }

  /* ------------------------------------------------------------------ */
  /* Lightbox                                                            */
  /* ------------------------------------------------------------------ */

  function initLightbox() {
    var dlg = $('#lightbox');
    if (!dlg) return;
    bindDialog(dlg);
    var imgEl = $('figure img', dlg), cap = $('figcaption', dlg);
    var list = [], cur = 0;

    function caption(img) {
      var fig = img.closest('figure');
      var fc = fig && fig.querySelector('figcaption');
      return (fc && fc.textContent.trim()) || img.getAttribute('alt') || '';
    }
    function show(i) {
      cur = (i + list.length) % list.length;
      var img = list[cur];
      imgEl.src = img.currentSrc || img.src;
      imgEl.alt = img.getAttribute('alt') || '';
      cap.textContent = caption(img);
      $$('.lb-nav', dlg).forEach(function (b) { b.hidden = list.length < 2; });
    }
    document.addEventListener('click', function (e) {
      var img = e.target.closest && e.target.closest('img[data-zoom]');
      if (!img) return;
      e.preventDefault();
      list = $$('img[data-zoom]');
      show(list.indexOf(img));
      openDialog(dlg);
    });
    $('[data-lb-close]', dlg).addEventListener('click', function () { closeDialog(dlg); });
    $('[data-lb-prev]', dlg).addEventListener('click', function () { show(cur - 1); });
    $('[data-lb-next]', dlg).addEventListener('click', function () { show(cur + 1); });
    dlg.addEventListener('click', function (e) { if (e.target.tagName === 'FIGURE') closeDialog(dlg); });
    dlg.addEventListener('keydown', function (e) {
      if (e.key === 'ArrowLeft') show(cur - 1);
      else if (e.key === 'ArrowRight') show(cur + 1);
    });
  }

  /* ------------------------------------------------------------------ */
  /* GitHub releases                                                     */
  /* ------------------------------------------------------------------ */

  var releasesPromise = null;
  function getReleases() {
    if (releasesPromise) return releasesPromise;
    var ss = store('sessionStorage'), key = 'rw.releases.v1';
    if (ss) {
      try {
        var c = JSON.parse(ss.getItem(key) || 'null');
        if (c && Date.now() - c.t < 15 * 60 * 1000 && Array.isArray(c.d)) return (releasesPromise = Promise.resolve(c.d));
      } catch (e) { /* ignore */ }
    }
    releasesPromise = fetch(RW.api + '/releases?per_page=20', { headers: { Accept: 'application/vnd.github+json' } })
      .then(function (r) { if (!r.ok) throw new Error('GitHub ' + r.status); return r.json(); })
      .then(function (list) {
        var d = (Array.isArray(list) ? list : []).filter(function (r) { return !r.draft; }).map(function (r) {
          return {
            tag: r.tag_name, name: r.name, body: r.body || '', url: r.html_url, pre: !!r.prerelease,
            date: r.published_at || r.created_at,
            assets: (r.assets || []).map(function (a) { return { name: a.name, url: a.browser_download_url, size: a.size }; })
          };
        });
        if (ss) { try { ss.setItem(key, JSON.stringify({ t: Date.now(), d: d })); } catch (e) { /* full */ } }
        return d;
      });
    return releasesPromise;
  }

  function latest(rels) {
    for (var i = 0; i < rels.length; i++) if (!rels[i].pre) return rels[i];
    return rels[0] || null;
  }
  function packages(rel) {
    var full = null, small = null;
    (rel ? rel.assets : []).forEach(function (a) {
      if (!/^RobotWinStudio-Setup-.+\.exe$/i.test(a.name)) return;
      if (/-tam\.exe$/i.test(a.name)) { if (!full) full = a; }
      else if (!small) small = a;
    });
    return { full: full, small: small };
  }
  function size(bytes) {
    if (!bytes) return '';
    var mb = bytes / 1048576;
    return new Intl.NumberFormat(LANG, { maximumFractionDigits: mb < 10 ? 1 : 0 }).format(mb) + ' MB';
  }

  function initDownloads() {
    var blocks = $$('[data-dl]'), pkgs = $$('[data-pkg]');
    if (!blocks.length && !pkgs.length) return;
    blocks.forEach(function (b) { var m = $('[data-dl-meta]', b); if (m) m.textContent = L.looking; });

    getReleases().then(function (rels) {
      var rel = latest(rels), p = packages(rel);
      blocks.forEach(function (b) {
        var main = $('[data-dl-main]', b), meta = $('[data-dl-meta]', b), alt = $('[data-dl-small]', b);
        var best = p.full || p.small;
        if (!rel) {
          meta.innerHTML = esc(L.noRelease) + ' <a href="' + esc(RW.releases) + '">' + esc(b.getAttribute('data-s-fallback')) + '</a>';
          return;
        }
        if (best) {
          main.href = best.url;
          main.setAttribute('download', '');
          var bits = [rel.tag, size(best.size)];
          if (best === p.full) bits.push(b.getAttribute('data-s-full'));
          meta.textContent = bits.filter(Boolean).join(' · ');
        } else {
          main.href = rel.url;
          meta.textContent = rel.tag || '';
        }
        if (p.full && p.small && alt) {
          alt.href = p.small.url;
          alt.textContent = b.getAttribute('data-s-small') + ' · ' + size(p.small.size);
          alt.hidden = false;
        }
      });
      pkgs.forEach(function (el) {
        var a = el.getAttribute('data-pkg') === 'full' ? p.full : p.small;
        if (!a) return;
        el.href = a.url;
        var n = $('[data-pkg-name]', el), s = $('[data-pkg-size]', el);
        if (n) n.textContent = a.name;
        if (s) s.textContent = size(a.size);
      });
    }).catch(function () {
      blocks.forEach(function (b) {
        var meta = $('[data-dl-meta]', b);
        meta.innerHTML = '<a href="' + esc(RW.releases) + '">' + esc(b.getAttribute('data-s-fallback')) + '</a>';
      });
    });
  }

  /* ------------------------------------------------------------------ */
  /* Tiny, safe Markdown renderer (release notes, news.json)              */
  /* Everything is HTML-escaped first; only a fixed set of tags is made. */
  /* ------------------------------------------------------------------ */

  function safeUrl(u) {
    var raw = u.replace(/&amp;/g, '&');
    if (!/^(https?:\/\/|mailto:)/i.test(raw)) return null;
    return esc(raw);
  }

  function inline(src) {
    var keep = [];
    var hold = function (html) { keep.push(html); return '\u0000' + (keep.length - 1) + '\u0000'; };
    var s = String(src).replace(/\u0000/g, '');
    s = s.replace(/`([^`]+)`/g, function (m, c) { return hold('<code>' + esc(c) + '</code>'); });
    s = esc(s);
    s = s.replace(/\[([^\]]+)\]\(([^)\s]+)(?:\s+&quot;[^)]*?&quot;)?\)/g, function (m, text, url) {
      var u = safeUrl(url);
      return u ? hold('<a href="' + u + '" rel="noopener">' + text + '</a>') : text;
    });
    s = s.replace(/(^|[\s(])(https?:\/\/[^\s<]+[^\s<.,;:!?)\]'"])/g, function (m, pre, url) {
      var u = safeUrl(url);
      return u ? pre + hold('<a href="' + u + '" rel="noopener">' + url + '</a>') : m;
    });
    s = s.replace(/\*\*(\S(?:[^*]*?\S)?)\*\*/g, '<strong>$1</strong>')
         .replace(/__(\S(?:[^_]*?\S)?)__/g, '<strong>$1</strong>')
         .replace(/(^|[^*\w])\*(\S(?:[^*]*?\S)?)\*(?![*\w])/g, '$1<em>$2</em>')
         .replace(/(^|[^_\w])_(\S(?:[^_]*?\S)?)_(?![_\w])/g, '$1<em>$2</em>')
         .replace(/~~(\S(?:[^~]*?\S)?)~~/g, '<del>$1</del>');
    return s.replace(/\u0000(\d+)\u0000/g, function (m, i) { return keep[+i]; });
  }

  var LIST_RE = /^\s*([-*+]|\d+[.)])\s+(.*)$/;
  function markdown(src, shift) {
    shift = shift || 0;
    var lines = String(src || '').replace(/\r\n?/g, '\n').split('\n');
    var out = [], i = 0, m;
    var isTableSep = function (l) { return /^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?\s*$/.test(l); };
    var cells = function (l) { return l.trim().replace(/^\|/, '').replace(/\|$/, '').split('|').map(function (c) { return inline(c.trim()); }); };
    while (i < lines.length) {
      var line = lines[i];
      if (/^\s*$/.test(line)) { i++; continue; }
      if (/^\s*(```|~~~)/.test(line)) {
        var buf = []; i++;
        while (i < lines.length && !/^\s*(```|~~~)/.test(lines[i])) buf.push(lines[i++]);
        i++;
        out.push('<pre><code>' + esc(buf.join('\n')) + '</code></pre>');
        continue;
      }
      if ((m = /^(#{1,6})\s+(.*?)\s*#*\s*$/.exec(line))) {
        var lvl = Math.min(6, m[1].length + shift);
        out.push('<h' + lvl + '>' + inline(m[2]) + '</h' + lvl + '>');
        i++; continue;
      }
      if (/^\s*([-*_])(\s*\1){2,}\s*$/.test(line)) { out.push('<hr>'); i++; continue; }
      if (/^\s*>/.test(line)) {
        var q = [];
        while (i < lines.length && /^\s*>/.test(lines[i])) q.push(lines[i++].replace(/^\s*>\s?/, ''));
        out.push('<blockquote>' + markdown(q.join('\n'), shift) + '</blockquote>');
        continue;
      }
      if (/\|/.test(line) && i + 1 < lines.length && isTableSep(lines[i + 1])) {
        var head = cells(line); i += 2;
        var rows = [];
        while (i < lines.length && /\|/.test(lines[i]) && !/^\s*$/.test(lines[i])) rows.push(cells(lines[i++]));
        out.push('<div class="table-wrap"><table><thead><tr>' + head.map(function (c) { return '<th>' + c + '</th>'; }).join('') +
          '</tr></thead><tbody>' + rows.map(function (r) { return '<tr>' + r.map(function (c) { return '<td>' + c + '</td>'; }).join('') + '</tr>'; }).join('') +
          '</tbody></table></div>');
        continue;
      }
      if (LIST_RE.test(line)) {
        var ordered = /^\s*\d/.test(line), items = [];
        while (i < lines.length) {
          var l = lines[i];
          if ((m = LIST_RE.exec(l))) items.push(m[2]);
          else if (/^\s{2,}\S/.test(l) && items.length) items[items.length - 1] += ' ' + l.trim();
          else break;
          i++;
        }
        var tag = ordered ? 'ol' : 'ul';
        out.push('<' + tag + '>' + items.map(function (x) {
          var task = /^\[( |x|X)\]\s+/.exec(x);
          if (task) x = (task[1] === ' ' ? '☐ ' : '☑ ') + x.slice(task[0].length);
          return '<li>' + inline(x) + '</li>';
        }).join('') + '</' + tag + '>');
        continue;
      }
      var para = [];
      while (i < lines.length && !/^\s*$/.test(lines[i]) && !/^(#{1,6}\s|\s*(```|~~~)|\s*>)/.test(lines[i]) && !LIST_RE.test(lines[i])) {
        para.push(inline(lines[i++].trim()));
      }
      if (!para.length) { para.push(inline(lines[i++].trim())); }
      out.push('<p>' + para.join('<br>') + '</p>');
    }
    return out.join('\n');
  }
  RW.markdown = markdown;

  /* ------------------------------------------------------------------ */
  /* News feed (docs/news.json + GitHub releases)                        */
  /* ------------------------------------------------------------------ */

  function fmtDate(iso) {
    if (!iso) return '';
    var d = new Date(/^\d{4}-\d{2}-\d{2}$/.test(iso) ? iso + 'T12:00:00Z' : iso);
    if (isNaN(d)) return iso;
    return new Intl.DateTimeFormat(LANG === 'en' ? 'en-GB' : 'tr-TR', { day: 'numeric', month: 'long', year: 'numeric' }).format(d);
  }
  function plain(md) {
    return String(md || '').replace(/```[\s\S]*?```/g, '').replace(/!\[[^\]]*\]\([^)]*\)/g, '')
      .replace(/\[([^\]]+)\]\([^)]*\)/g, '$1').replace(/[*_`#>~]/g, '').replace(/\s+/g, ' ').trim();
  }

  function initNews() {
    var feeds = $$('[data-news]');
    if (!feeds.length) return;

    var news = fetch(RW.newsUrl, { credentials: 'same-origin' })
      .then(function (r) { if (!r.ok) throw new Error(r.status); return r.json(); })
      .then(function (j) {
        return (j.items || []).map(function (n) {
          return { kind: 'news', id: n.id, date: n.date, title: n.title, body: n.body || '', link: n.link };
        });
      });
    var rels = getReleases().then(function (list) {
      return list.map(function (r) {
        return { kind: 'release', id: r.tag, date: r.date, title: r.name || r.tag, tag: r.tag, body: r.body, link: r.url, pre: r.pre, assets: r.assets };
      });
    });

    Promise.all([news.catch(function () { return null; }), rels.catch(function () { return null; })]).then(function (res) {
      var failed = res[0] === null && res[1] === null;
      var items = [].concat(res[0] || [], res[1] || []);
      items.sort(function (a, b) { return String(b.date).localeCompare(String(a.date)); });
      feeds.forEach(function (feed) {
        if (failed) {
          feed.innerHTML = '<p class="news-msg">' + esc(L.failed) + ' <a href="' + esc(RW.releases) + '">GitHub</a></p>';
          return;
        }
        if (!items.length) { feed.innerHTML = '<p class="news-msg">' + esc(L.none) + '</p>'; return; }
        var full = feed.hasAttribute('data-full');
        var limit = parseInt(feed.getAttribute('data-limit') || '0', 10);
        var list = limit ? items.slice(0, limit) : items;
        feed.innerHTML = list.map(function (it) { return full ? renderFull(it) : renderTeaser(it); }).join('');
        if (full) wireFull(feed);
      });
    });
  }

  function kindBadge(it) {
    var color = it.kind === 'release' ? 'var(--pcb)' : 'var(--accent)';
    var label = it.kind === 'release' ? (it.pre ? L.pre : L.release) + (it.tag ? ' ' + it.tag : '') : L.announce;
    return '<span class="news-kind"><span class="dot" style="--c:' + color + '"></span>' + esc(label) + '</span>';
  }

  function renderTeaser(it) {
    var text = plain(it.body);
    if (text.length > 170) text = text.slice(0, 167).replace(/\s+\S*$/, '') + '…';
    var href = (RW.newsPage || '#') + '#' + encodeURIComponent(it.kind + '-' + (it.id || ''));
    return '<article class="news-item">' +
      '<p class="news-meta">' + kindBadge(it) + '<time datetime="' + esc(it.date) + '">' + esc(fmtDate(it.date)) + '</time></p>' +
      '<h3><a href="' + esc(href) + '">' + esc(it.title) + '</a></h3>' +
      '<div class="news-body"><p>' + esc(text) + '</p></div></article>';
  }

  function renderFull(it) {
    var id = it.kind + '-' + (it.id || '');
    var assets = (it.assets || []).filter(function (a) { return /\.exe$/i.test(a.name); });
    var link = it.link && /^https?:\/\//i.test(it.link) ? it.link : '';
    return '<article class="news-item" id="' + esc(id) + '" data-kind="' + it.kind + '">' +
      '<p class="news-meta">' + kindBadge(it) + '<time datetime="' + esc(it.date) + '">' + esc(fmtDate(it.date)) + '</time></p>' +
      '<h2>' + (link ? '<a href="' + esc(link) + '">' + esc(it.title) + '</a>' : esc(it.title)) + '</h2>' +
      '<div class="news-body prose">' + markdown(it.body, 2) + '</div>' +
      (assets.length ? '<div class="news-assets">' + assets.map(function (a) {
        return '<a class="btn btn-sm" href="' + esc(a.url) + '">' + esc(a.name) + (a.size ? ' · ' + esc(size(a.size)) : '') + '</a>';
      }).join('') + '</div>' : '') +
      (link && it.kind === 'news' ? '<p><a href="' + esc(link) + '">' + esc(L.details) + ' →</a></p>' : '') +
      '</article>';
  }

  function wireFull(feed) {
    $$('.news-body', feed).forEach(function (b) {
      if (b.scrollHeight > 420) {
        b.classList.add('clamp');
        var btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'news-more';
        btn.textContent = L.more;
        btn.addEventListener('click', function () {
          var c = b.classList.toggle('clamp');
          btn.textContent = c ? L.more : L.less;
        });
        b.parentNode.insertBefore(btn, b.nextSibling);
      }
    });
    var buttons = $$('[data-filter]');
    buttons.forEach(function (btn) {
      btn.addEventListener('click', function () {
        var f = btn.getAttribute('data-filter');
        buttons.forEach(function (o) { o.setAttribute('aria-pressed', o === btn ? 'true' : 'false'); });
        $$('.news-item', feed).forEach(function (it) { it.hidden = f !== 'all' && it.getAttribute('data-kind') !== f; });
      });
    });
    if (location.hash) {
      var target = document.getElementById(decodeURIComponent(location.hash.slice(1)));
      if (target) target.scrollIntoView();
    }
  }

  /* ------------------------------------------------------------------ */

  function initMisc() {
    var ls = store('localStorage');
    $$('[data-lang-switch]').forEach(function (a) {
      a.addEventListener('click', function () { if (ls) ls.setItem('rw.lang', a.getAttribute('data-lang-switch')); });
    });
    var video = $('video[data-demo]');
    if (video && window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
      video.autoplay = false;
      video.pause();
      video.controls = true;
    }
  }

  function start() {
    initMisc();
    initNav();
    initSearch();
    initDocs();
    initLightbox();
    initDownloads();
    initNews();
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
  else start();
})();
