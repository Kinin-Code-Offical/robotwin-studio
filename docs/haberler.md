---
title: Haberler
lang: tr
description: RobotWin Studio duyuruları ve sürümleri; uygulamanın Haberler bölümüyle aynı kaynaktan.
---

# Haberler

Duyurular ve sürümler. Uygulamadaki **Yardım → Haberler** bölümü aynı haberleri gösterir. Her sürümün ayrıntıları: [11. Yenilikler](kullanim/11-yenilikler.md).

<div id="rw-news" class="rw-news" aria-live="polite">Haberler yükleniyor…</div>

## Sürümler

<div id="rw-releases" class="rw-news" aria-live="polite">Sürüm listesi yükleniyor…</div>

<script>
(function () {
  var NEWS = "{{ site.baseurl }}/news.json";
  var RELEASES = "https://api.github.com/repos/Kinin-Code-Offical/robotwin-studio/releases?per_page=8";
  function esc(s) { return String(s).replace(/[&<>"]/g, function (c) { return { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]; }); }
  function inline(s) { return esc(s).replace(/\*\*([^*]+)\*\*/g, "<strong>$1</strong>").replace(/\*([^*]+)\*/g, "<em>$1</em>").replace(/`([^`]+)`/g, "<code>$1</code>"); }
  function md(text) {
    var out = [], list = false;
    String(text || "").split(/\n/).forEach(function (line) {
      var item = /^\s*[-*]\s+(.*)$/.exec(line);
      if (item) { if (!list) { out.push("<ul>"); list = true; } out.push("<li>" + inline(item[1]) + "</li>"); return; }
      if (list) { out.push("</ul>"); list = false; }
      if (line.trim().length) out.push("<p>" + inline(line) + "</p>");
    });
    if (list) out.push("</ul>");
    return out.join("");
  }
  function date(s) { var d = new Date(s); return isNaN(d) ? s : d.toLocaleDateString("tr-TR", { day: "numeric", month: "long", year: "numeric" }); }
  fetch(NEWS, { cache: "no-cache" }).then(function (r) { return r.json(); }).then(function (data) {
    var items = (data.items || []).slice().sort(function (a, b) { return String(b.date).localeCompare(String(a.date)); });
    document.getElementById("rw-news").innerHTML = items.length ? items.map(function (n) {
      return '<article class="rw-news-item"><h3>' + esc(n.title) + '</h3><p class="rw-news-date">' + esc(date(n.date)) + '</p>' + md(n.body) +
        (n.link ? '<p><a href="' + esc(n.link) + '">Ayrıntılar</a></p>' : "") + "</article>";
    }).join("") : "Henüz haber yok.";
  }).catch(function () { document.getElementById("rw-news").textContent = "Haberler alınamadı."; });
  fetch(RELEASES).then(function (r) { return r.json(); }).then(function (list) {
    document.getElementById("rw-releases").innerHTML = (Array.isArray(list) ? list : []).filter(function (x) { return !x.draft; }).map(function (x) {
      return '<p><a href="' + esc(x.html_url) + '"><strong>' + esc(x.name || x.tag_name) + "</strong></a> · " + esc(date(x.published_at)) +
        " · " + x.assets.length + " dosya" + (x.prerelease ? " · ön sürüm" : "") + "</p>";
    }).join("") || "Sürüm bulunamadı.";
  }).catch(function () { document.getElementById("rw-releases").innerHTML = '<a href="https://github.com/Kinin-Code-Offical/robotwin-studio/releases">GitHub sürümleri</a>'; });
})();
</script>
