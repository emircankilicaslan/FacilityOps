"use strict";

// ---- Yardımcılar -----------------------------------------------------------

const $ = (s, k = document) => k.querySelector(s);
const $$ = (s, k = document) => [...k.querySelectorAll(s)];

/** Güvenli DOM üretimi: metinler her zaman textContent olarak eklenir (innerHTML kullanılmaz). */
function h(etiket, ozellikler = {}, ...cocuklar) {
  const e = document.createElement(etiket);
  for (const [k, v] of Object.entries(ozellikler)) {
    if (v === false || v == null) continue;
    if (k === "class") e.className = v;
    else if (k.startsWith("on")) e.addEventListener(k.slice(2), v);
    else e.setAttribute(k, v === true ? "" : v);
  }
  for (const c of cocuklar.flat()) if (c != null && c !== false) e.append(c);
  return e;
}

const DURUM = ["Acik", "Atandi", "Cozuldu", "Kapandi"];
const DURUM_AD = { Acik: "Açık", Atandi: "Atandı", Cozuldu: "Çözüldü", Kapandi: "Kapandı" };
const ONCELIK = ["Dusuk", "Normal", "Yuksek", "Kritik"];
const ONCELIK_AD = { Dusuk: "Düşük", Normal: "Normal", Yuksek: "Yüksek", Kritik: "Kritik" };

// API enum'ları sayı ya da ad olarak dönebilir; ikisini de karşıla.
const durumAdi = (d) => (typeof d === "number" ? DURUM[d] : d);
const oncelikAdi = (o) => (typeof o === "number" ? ONCELIK[o] : o);

/** Sunucu UTC zamanı bazen 'Z' olmadan döner; UTC kabul edip yerel saate çevir. */
function tarih(s) {
  if (!s) return null;
  return new Date(/[zZ]|[+-]\d\d:\d\d$/.test(s) ? s : s + "Z");
}
const yerel = (s) => {
  const d = tarih(s);
  return d ? d.toLocaleString("tr-TR", { dateStyle: "short", timeStyle: "short" }) : "–";
};

function rozet(sinif, metin) {
  return h("span", { class: `rozet ${sinif}` }, metin);
}

function slaHucresi(a) {
  const son = tarih(a.slaSonTarih);
  if (durumAdi(a.durum) === "Kapandi") return h("span", { class: "soluk" }, "Kapandı");
  const gecikmis = a.gecikmis ?? (son && son < new Date());
  return h("span", { class: gecikmis ? "sla-gecikmis" : "" }, (gecikmis ? "Gecikti · " : "") + yerel(a.slaSonTarih));
}

// ---- Oturum ---------------------------------------------------------------
// Tokenlar sekme kapanınca silinsin diye sessionStorage'da tutulur.

const Oturum = {
  oku() {
    try { return JSON.parse(sessionStorage.getItem("oturum") || "null"); } catch { return null; }
  },
  yaz(o) { sessionStorage.setItem("oturum", JSON.stringify(o)); },
  sil() { sessionStorage.removeItem("oturum"); },
};

function jwtCoz(token) {
  try {
    const yuk = JSON.parse(atob(token.split(".")[1].replace(/-/g, "+").replace(/_/g, "/")));
    const al = (...anahtarlar) => anahtarlar.map((k) => yuk[k]).find((v) => v != null);
    return {
      ad: al("unique_name", "name", "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name"),
      rol: al("role", "http://schemas.microsoft.com/ws/2008/06/identity/claims/role"),
    };
  } catch { return { ad: "", rol: "" }; }
}

let yenilemeSozu = null;

/** Yenileme tokenı döndürülür; eşzamanlı 401'ler tek bir yenileme isteğini paylaşır. */
function tokenYenile() {
  if (yenilemeSozu) return yenilemeSozu;
  yenilemeSozu = (async () => {
    const o = Oturum.oku();
    if (!o?.yenilemeTokeni) throw new Error("oturum yok");
    const r = await fetch("/auth/yenile", {
      method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ yenilemeTokeni: o.yenilemeTokeni }),
    });
    if (!r.ok) throw new Error("yenilenemedi");
    const y = await r.json();
    Oturum.yaz({ token: y.token, yenilemeTokeni: y.yenilemeTokeni });
  })().finally(() => { yenilemeSozu = null; });
  return yenilemeSozu;
}

class ApiHatasi extends Error {
  constructor(durum, mesaj) { super(mesaj); this.durum = durum; }
}

async function istek(yol, { yontem = "GET", govde, ham = false } = {}, tekrar = true) {
  const o = Oturum.oku();
  const basliklar = {};
  if (govde !== undefined) basliklar["Content-Type"] = "application/json";
  if (o?.token) basliklar.Authorization = `Bearer ${o.token}`;

  const r = await fetch(yol, { method: yontem, headers: basliklar, body: govde !== undefined ? JSON.stringify(govde) : undefined });

  if (r.status === 401 && tekrar && o?.yenilemeTokeni && !yol.startsWith("/auth/")) {
    try { await tokenYenile(); } catch { cikisYap(false); throw new ApiHatasi(401, "Oturum süresi doldu, yeniden giriş yapın."); }
    return istek(yol, { yontem, govde, ham }, false);
  }
  if (!r.ok) {
    let mesaj = `İstek başarısız (${r.status}).`;
    try { const p = await r.json(); mesaj = p.detail || p.title || mesaj; } catch { /* gövde yok */ }
    if (r.status === 429) mesaj = "Çok fazla deneme. Bir dakika sonra tekrar deneyin.";
    throw new ApiHatasi(r.status, mesaj);
  }
  if (ham) return r;
  return r.status === 204 ? null : r.json();
}

// ---- Durum ----------------------------------------------------------------

const durum = {
  ben: { ad: "", rol: "" },
  istasyonlar: [],
  sayfa: 1,
  boyut: 15,
  toplamSayfa: 1,
  filtre: { ara: "", durum: "", oncelik: "", gecikmis: false },
};
const yonetici = () => durum.ben.rol === "Yonetici";
const istasyonAdi = (id) => durum.istasyonlar.find((i) => i.id === id)?.ad ?? `#${id}`;

function hataGoster(el, e) {
  el.textContent = e instanceof Error ? e.message : String(e);
  el.hidden = false;
}
const hataTemizle = (...els) => els.forEach((e) => { e.hidden = true; e.textContent = ""; });

// ---- Giriş / çıkış ----------------------------------------------------------

let girisModu = "giris";

$$(".sekme").forEach((b) => b.addEventListener("click", () => {
  girisModu = b.dataset.mod;
  $$(".sekme").forEach((x) => x.classList.toggle("aktif", x === b));
  $("#giris-gonder").textContent = girisModu === "giris" ? "Giriş yap" : "Kayıt ol ve giriş yap";
  $("#kayit-ipucu").hidden = girisModu !== "kayit";
  $("[name=sifre]", $("#giris-form")).autocomplete = girisModu === "giris" ? "current-password" : "new-password";
  hataTemizle($("#giris-hata"));
}));

$("#giris-form").addEventListener("submit", async (ev) => {
  ev.preventDefault();
  const f = ev.target, hata = $("#giris-hata"), dugme = $("#giris-gonder");
  hataTemizle(hata);
  const govde = { kullaniciAdi: f.kullaniciAdi.value.trim(), sifre: f.sifre.value };
  dugme.disabled = true;
  try {
    if (girisModu === "kayit") await istek("/auth/kayit", { yontem: "POST", govde });
    const y = await istek("/auth/giris", { yontem: "POST", govde });
    Oturum.yaz({ token: y.token, yenilemeTokeni: y.yenilemeTokeni });
    f.reset();
    await uygulamayiAc();
  } catch (e) { hataGoster(hata, e); }
  finally { dugme.disabled = false; }
});

async function cikisYap(sunucuyaBildir = true) {
  const o = Oturum.oku();
  if (sunucuyaBildir && o?.yenilemeTokeni) {
    try { await istek("/auth/cikis", { yontem: "POST", govde: { yenilemeTokeni: o.yenilemeTokeni } }, false); } catch { /* yine de çık */ }
  }
  Oturum.sil();
  $("#uygulama").hidden = true;
  $("#giris-gorunum").hidden = false;
}
$("#cikis").addEventListener("click", () => cikisYap());

// ---- Gezinme ----------------------------------------------------------------

function sayfaAc(ad) {
  $$("#menu button").forEach((b) => b.classList.toggle("aktif", b.dataset.sayfa === ad));
  for (const s of ["arizalar", "istasyonlar", "kullanicilar"]) $(`#sayfa-${s}`).hidden = s !== ad;
  hataTemizle($("#genel-hata"));
  if (ad === "arizalar") arizalariYukle();
  if (ad === "istasyonlar") istasyonlariYukle();
  if (ad === "kullanicilar") kullanicilariYukle();
}
$$("#menu button").forEach((b) => b.addEventListener("click", () => sayfaAc(b.dataset.sayfa)));

async function uygulamayiAc() {
  durum.ben = jwtCoz(Oturum.oku().token);
  $("#kullanici-etiket").textContent = `${durum.ben.ad} · ${durum.ben.rol === "Yonetici" ? "Yönetici" : "Personel"}`;
  $$("[data-yonetici]").forEach((e) => { e.hidden = !yonetici(); });
  $("#giris-gorunum").hidden = true;
  $("#uygulama").hidden = false;
  try { durum.istasyonlar = await istek("/istasyonlar"); } catch (e) { hataGoster($("#genel-hata"), e); }
  sayfaAc("arizalar");
}

// ---- Arızalar ---------------------------------------------------------------

async function ozetiYukle() {
  const o = await istek("/arizalar/ozet");
  const kutu = $("#ozet");
  kutu.replaceChildren(
    ...DURUM.map((d) => {
      const secili = durum.filtre.durum === d && !durum.filtre.gecikmis;
      return h("button", { class: `kart ozet-kart${secili ? " secili" : ""}`, onclick: () => filtreUygula({ durum: durum.filtre.durum === d ? "" : d, gecikmis: false }) },
        h("strong", {}, String(o.durumlar?.[d] ?? 0)), h("span", {}, DURUM_AD[d]));
    }),
    h("button", { class: `kart ozet-kart gecikme${durum.filtre.gecikmis ? " secili" : ""}`, onclick: () => filtreUygula({ gecikmis: !durum.filtre.gecikmis, durum: "" }) },
      h("strong", {}, String(o.gecikmis ?? 0)), h("span", {}, "SLA gecikmiş")),
  );
}

function filtreUygula(degisim) {
  Object.assign(durum.filtre, degisim);
  $("#f-durum").value = durum.filtre.durum;
  $("#f-gecikmis").checked = durum.filtre.gecikmis;
  durum.sayfa = 1;
  arizalariYukle();
}

function sorgu(f) {
  const p = new URLSearchParams();
  if (f.durum) p.set("durum", f.durum);
  if (f.oncelik) p.set("oncelik", f.oncelik);
  if (f.gecikmis) p.set("gecikmis", "true");
  if (f.ara) p.set("ara", f.ara);
  return p;
}

async function arizalariYukle() {
  const genel = $("#genel-hata");
  hataTemizle(genel);
  try {
    const p = sorgu(durum.filtre);
    p.set("sayfa", durum.sayfa);
    p.set("boyut", durum.boyut);
    const [liste] = await Promise.all([istek(`/arizalar?${p}`), ozetiYukle()]);
    durum.toplamSayfa = Math.max(1, liste.toplamSayfa ?? Math.ceil(liste.toplam / durum.boyut));
    const govde = $("#ariza-govde");
    govde.replaceChildren(...liste.ogeler.map((a) =>
      h("tr", { class: "tikla", tabindex: "0", onclick: () => detayAc(a.id), onkeydown: (e) => { if (e.key === "Enter") detayAc(a.id); } },
        h("td", {}, String(a.id)),
        h("td", { class: "baslik-hucre", title: a.baslik }, a.baslik),
        h("td", {}, a.istasyon?.ad ?? istasyonAdi(a.istasyonId)),
        h("td", {}, rozet(`r-${oncelikAdi(a.oncelik).toLowerCase()}`, ONCELIK_AD[oncelikAdi(a.oncelik)])),
        h("td", {}, rozet(`d-${durumAdi(a.durum).toLowerCase()}`, DURUM_AD[durumAdi(a.durum)])),
        h("td", {}, a.atananKisi || h("span", { class: "soluk" }, "–")),
        h("td", {}, slaHucresi(a)))));
    $("#ariza-bos").hidden = liste.ogeler.length > 0;
    $("#sayfa-bilgi").textContent = `Sayfa ${durum.sayfa} / ${durum.toplamSayfa} · ${liste.toplam} kayıt`;
    $("#onceki").disabled = durum.sayfa <= 1;
    $("#sonraki").disabled = durum.sayfa >= durum.toplamSayfa;
  } catch (e) { hataGoster(genel, e); }
}

let aramaZamanlayici;
$("#f-ara").addEventListener("input", (e) => {
  clearTimeout(aramaZamanlayici);
  aramaZamanlayici = setTimeout(() => filtreUygula({ ara: e.target.value.trim() }), 300);
});
$("#f-durum").addEventListener("change", (e) => filtreUygula({ durum: e.target.value, gecikmis: false }));
$("#f-oncelik").addEventListener("change", (e) => filtreUygula({ oncelik: e.target.value }));
$("#f-gecikmis").addEventListener("change", (e) => filtreUygula({ gecikmis: e.target.checked }));
$("#onceki").addEventListener("click", () => { durum.sayfa--; arizalariYukle(); });
$("#sonraki").addEventListener("click", () => { durum.sayfa++; arizalariYukle(); });

$("#csv-indir").addEventListener("click", async () => {
  try {
    const r = await istek(`/arizalar/disa-aktar?${sorgu({ ...durum.filtre, ara: "" })}`, { ham: true });
    const url = URL.createObjectURL(await r.blob());
    const a = h("a", { href: url, download: "arizalar.csv" });
    document.body.append(a); a.click(); a.remove();
    URL.revokeObjectURL(url);
  } catch (e) { hataGoster($("#genel-hata"), e); }
});

// ---- Yeni arıza -------------------------------------------------------------

const yeniDialog = $("#yeni-dialog");
$("#yeni-ariza").addEventListener("click", async () => {
  hataTemizle($("#yeni-hata"));
  if (!durum.istasyonlar.length) {
    try { durum.istasyonlar = await istek("/istasyonlar"); } catch (e) { hataGoster($("#genel-hata"), e); return; }
  }
  if (!durum.istasyonlar.length) {
    hataGoster($("#genel-hata"), "Önce İstasyonlar sayfasından bir istasyon ekleyin.");
    return;
  }
  $("#yeni-istasyon").replaceChildren(...durum.istasyonlar.map((i) => h("option", { value: i.id }, `${i.ad} (${i.sehir})`)));
  yeniDialog.showModal();
});

$("#yeni-form").addEventListener("submit", async (ev) => {
  ev.preventDefault();
  const f = ev.target, hata = $("#yeni-hata");
  hataTemizle(hata);
  try {
    await istek("/arizalar", { yontem: "POST", govde: {
      istasyonId: Number(f.istasyonId.value), baslik: f.baslik.value.trim(),
      aciklama: f.aciklama.value.trim() || null, oncelik: Number(f.oncelik.value),
    } });
    yeniDialog.close();
    f.reset();
    durum.sayfa = 1;
    arizalariYukle();
  } catch (e) { hataGoster(hata, e); }
});

// ---- Detay ------------------------------------------------------------------

const detayDialog = $("#detay-dialog");

async function detayAc(id) {
  const kutu = $("#detay-icerik");
  kutu.replaceChildren(h("p", { class: "soluk" }, "Yükleniyor…"));
  detayDialog.showModal();
  try {
    const [a, gecmis] = await Promise.all([istek(`/arizalar/${id}`), istek(`/arizalar/${id}/gecmis`)]);
    detayiCiz(a, gecmis);
  } catch (e) { kutu.replaceChildren(h("p", { class: "hata" }, e.message), kapatDugmesi()); }
}

const kapatDugmesi = () => h("div", { class: "dialog-alt" }, h("button", { class: "ikincil", onclick: () => detayDialog.close() }, "Kapat"));

function detayiCiz(a, gecmis) {
  const d = durumAdi(a.durum);
  const hata = h("p", { class: "hata", role: "alert", hidden: true });

  const islem = async (yol, govde) => {
    hataTemizle(hata);
    try {
      await istek(`/arizalar/${a.id}/${yol}`, { yontem: "POST", govde });
      const [yeni, yeniGecmis] = await Promise.all([istek(`/arizalar/${a.id}`), istek(`/arizalar/${a.id}/gecmis`)]);
      detayiCiz(yeni, yeniGecmis);
      arizalariYukle();
    } catch (e) { hataGoster(hata, e); }
  };

  const atanan = h("input", { placeholder: "Atanacak kişi", maxlength: "100", "aria-label": "Atanacak kişi", value: a.atananKisi || "" });
  const eylemler = [];
  if (d === "Acik" || d === "Atandi") {
    eylemler.push(h("div", { class: "atama-satiri" }, atanan,
      h("button", { class: "ikincil", onclick: () => { if (atanan.value.trim()) islem("ata", { kisi: atanan.value.trim() }); } }, d === "Acik" ? "Ata" : "Yeniden ata")));
  }
  const alt = [];
  if (d === "Atandi") alt.push(h("button", { class: "birincil", onclick: () => islem("coz") }, "Çözüldü olarak işaretle"));
  if (yonetici() && d !== "Kapandi") alt.push(h("button", { class: "tehlike", onclick: () => islem("kapat") }, "Kapat"));
  alt.push(h("button", { class: "ikincil", onclick: () => detayDialog.close() }, "Kapat penceresi"));

  $("#detay-icerik").replaceChildren(
    h("h2", {}, `#${a.id} · ${a.baslik}`),
    h("dl", { class: "detay-meta" },
      h("dt", {}, "İstasyon"), h("dd", {}, a.istasyon?.ad ?? istasyonAdi(a.istasyonId)),
      h("dt", {}, "Öncelik"), h("dd", {}, rozet(`r-${oncelikAdi(a.oncelik).toLowerCase()}`, ONCELIK_AD[oncelikAdi(a.oncelik)])),
      h("dt", {}, "Durum"), h("dd", {}, rozet(`d-${d.toLowerCase()}`, DURUM_AD[d])),
      h("dt", {}, "Atanan"), h("dd", {}, a.atananKisi || "–"),
      h("dt", {}, "Oluşturma"), h("dd", {}, yerel(a.olusturmaZamani)),
      h("dt", {}, "SLA hedefi"), h("dd", {}, slaHucresi(a)),
      a.kapanmaZamani ? [h("dt", {}, "Kapanma"), h("dd", {}, yerel(a.kapanmaZamani))] : null),
    a.aciklama ? h("p", { class: "aciklama-kutu" }, a.aciklama) : null,
    h("h3", {}, "Geçmiş"),
    h("ul", { class: "zaman-cizgisi" }, gecmis.map((g) =>
      h("li", {}, h("strong", {}, g.islem), ` · ${g.kullanici} · `, h("span", { class: "soluk" }, yerel(g.zaman)),
        g.eskiDurum != null ? h("div", { class: "soluk" }, `${DURUM_AD[durumAdi(g.eskiDurum)]} → ${DURUM_AD[durumAdi(g.yeniDurum)]}`) : null))),
    ...eylemler, hata,
    h("div", { class: "dialog-alt" }, alt),
  );
}

// ---- İstasyonlar ------------------------------------------------------------

async function istasyonlariYukle() {
  try {
    durum.istasyonlar = await istek("/istasyonlar");
    $("#istasyon-govde").replaceChildren(...durum.istasyonlar.map((i) =>
      h("tr", {}, h("td", {}, String(i.id)), h("td", {}, i.ad), h("td", {}, i.sehir))));
  } catch (e) { hataGoster($("#genel-hata"), e); }
}

$("#istasyon-form").addEventListener("submit", async (ev) => {
  ev.preventDefault();
  const f = ev.target;
  hataTemizle($("#genel-hata"));
  try {
    await istek("/istasyonlar", { yontem: "POST", govde: { ad: f.ad.value.trim(), sehir: f.sehir.value.trim() } });
    f.reset();
    istasyonlariYukle();
  } catch (e) { hataGoster($("#genel-hata"), e); }
});

// ---- Kullanıcılar (yönetici) -----------------------------------------------

async function kullanicilariYukle() {
  const genel = $("#genel-hata");
  hataTemizle(genel);
  try {
    const liste = await istek("/kullanicilar");
    const degistir = async (yol, govde) => {
      hataTemizle(genel);
      try { await istek(yol, { yontem: "PUT", govde }); kullanicilariYukle(); } catch (e) { hataGoster(genel, e); }
    };
    $("#kullanici-govde").replaceChildren(...liste.map((k) => {
      const kendim = k.kullaniciAdi === durum.ben.ad;
      return h("tr", {},
        h("td", {}, String(k.id)),
        h("td", {}, k.kullaniciAdi, kendim ? h("span", { class: "soluk" }, " (sen)") : null),
        h("td", {}, h("select", { "aria-label": `${k.kullaniciAdi} rolü`, onchange: (e) => degistir(`/kullanicilar/${k.id}/rol`, { rol: e.target.value }) },
          ["Personel", "Yonetici"].map((r) => h("option", { value: r, selected: r === k.rol }, r === "Yonetici" ? "Yönetici" : "Personel")))),
        h("td", {}, rozet(k.aktif ? "d-cozuldu" : "d-kapandi", k.aktif ? "Aktif" : "Pasif")),
        h("td", {}, h("button", { class: k.aktif ? "tehlike kucuk" : "ikincil kucuk", onclick: () => degistir(`/kullanicilar/${k.id}/durum`, { aktif: !k.aktif }) },
          k.aktif ? "Pasife al" : "Aktifleştir")));
    }));
  } catch (e) { hataGoster(genel, e); }
}

// ---- Şifre değiştir -----------------------------------------------------------

const sifreDialog = $("#sifre-dialog");
$("#sifre-ac").addEventListener("click", () => { hataTemizle($("#sifre-hata")); sifreDialog.showModal(); });
$$("[data-kapat]").forEach((b) => b.addEventListener("click", () => b.closest("dialog").close()));

$("#sifre-form").addEventListener("submit", async (ev) => {
  ev.preventDefault();
  const f = ev.target, hata = $("#sifre-hata");
  hataTemizle(hata);
  try {
    await istek("/auth/sifre-degistir", { yontem: "POST", govde: { mevcutSifre: f.mevcutSifre.value, yeniSifre: f.yeniSifre.value } });
    f.reset();
    sifreDialog.close();
    // Tüm yenileme tokenları iptal edildi; yeniden giriş gerekir.
    await cikisYap(false);
    hataGoster($("#giris-hata"), "Şifre değiştirildi. Yeni şifrenle giriş yap.");
  } catch (e) { hataGoster(hata, e); }
});

// ---- Başlangıç ------------------------------------------------------------------

if (Oturum.oku()) uygulamayiAc().catch(() => cikisYap(false));
else $("#giris-gorunum").hidden = false;
