"use strict";

// ---------------------------------------------------------------------------
// Yardımcılar
// ---------------------------------------------------------------------------

const $ = (s, k = document) => k.querySelector(s);
const $$ = (s, k = document) => [...k.querySelectorAll(s)];
const SVG = "http://www.w3.org/2000/svg";
const svgEtiketi = (t) => ["svg", "path", "circle", "text"].includes(t);

/** Güvenli DOM üretimi: metinler textContent olarak eklenir, innerHTML kullanılmaz. */
function h(etiket, ozellikler = {}, ...cocuklar) {
  const e = svgEtiketi(etiket) ? document.createElementNS(SVG, etiket) : document.createElement(etiket);
  for (const [k, v] of Object.entries(ozellikler || {})) {
    if (v === false || v == null) continue;
    if (k === "class") e.setAttribute("class", v);
    else if (k.startsWith("on")) e.addEventListener(k.slice(2), v);
    else if (k === "value" && "value" in e) e.value = v;
    else e.setAttribute(k, v === true ? "" : v);
  }
  for (const c of cocuklar.flat(Infinity)) if (c != null && c !== false) e.append(c.nodeType ? c : String(c));
  return e;
}

/** replaceChildren boş değerleri ve dizileri metne çevirir; bu yardımcı bunları eler ve düzleştirir. */
const doldur = (kok, ...cocuklar) => kok.replaceChildren(...cocuklar.flat(Infinity).filter((c) => c != null && c !== false).map((c) => (c.nodeType ? c : String(c))));

const DURUM = ["Acik", "Atandi", "Cozuldu", "Kapandi"];
const DURUM_AD = { Acik: "Açık", Atandi: "Atandı", Cozuldu: "Çözüldü", Kapandi: "Kapandı" };
const DURUM_RENK = { Acik: "var(--kirmizi)", Atandi: "var(--sari)", Cozuldu: "var(--yesil)", Kapandi: "var(--gri)" };
const ONCELIK = ["Dusuk", "Normal", "Yuksek", "Kritik"];
const ONCELIK_AD = { Dusuk: "Düşük", Normal: "Normal", Yuksek: "Yüksek", Kritik: "Kritik" };
const ISLEM_AD = { Olusturuldu: "Oluşturuldu", Atandi: "Atandı", Cozuldu: "Çözüldü olarak işaretlendi", Kapandi: "Kapatıldı" };
const SLA_SAAT = { Kritik: 4, Yuksek: 24, Normal: 72, Dusuk: 168 };

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
  return d ? d.toLocaleString("tr-TR", { dateStyle: "medium", timeStyle: "short" }) : "–";
};
const bas = (ad) => (ad || "?").trim().split(/[\s._-]+/).slice(0, 2).map((p) => p[0]).join("").toLocaleUpperCase("tr-TR");

/** Milisaniyeyi "2 g 4 sa", "3 sa 20 dk" ya da "45 dk" olarak yazar. */
function sure(ms) {
  const dk = Math.max(1, Math.round(ms / 60000));
  const g = Math.floor(dk / 1440), sa = Math.floor((dk % 1440) / 60), d = dk % 60;
  if (g) return sa ? `${g} g ${sa} sa` : `${g} g`;
  if (sa) return d ? `${sa} sa ${d} dk` : `${sa} sa`;
  return `${d} dk`;
}

/** SLA durumu: kalan süre, doluluk oranı ve renk sınıfı. Çözülmüş/kapanmış kayıtlar için null. */
function slaDurumu(a) {
  const d = durumAdi(a.durum);
  if (d === "Cozuldu" || d === "Kapandi") return null;
  const olus = tarih(a.olusturmaZamani), son = tarih(a.slaSonTarih), simdi = new Date();
  const kalan = son - simdi;
  const oran = Math.min(Math.max((simdi - olus) / (son - olus), 0), 1);
  return {
    sinif: kalan < 0 ? "sla-gecikme" : oran > 0.75 ? "sla-uyari" : "sla-iyi",
    oran, kalan,
    metin: kalan < 0 ? `${sure(-kalan)} gecikti` : `${sure(kalan)} kaldı`,
  };
}

function slaGostergesi(a) {
  const s = slaDurumu(a);
  if (!s) return h("span", { class: "soluk kucuk-yazi" }, durumAdi(a.durum) === "Kapandi" ? "Tamamlandı" : "Kapatma bekliyor");
  return h("div", { class: `sla ${s.sinif}` }, h("span", { class: "sla-metin" }, s.metin),
    h("div", { class: "sla-ray" }, h("span", { "data-oran": Math.round(s.oran * 100) })));
}
/** Genişlikler satır içi stil yerine CSSOM ile verilir (CSP satır içi stile izin vermez). */
function oranlariUygula(kok = document) {
  $$("[data-oran]", kok).forEach((e) => { e.style.width = `${e.dataset.oran}%`; });
}

const rozet = (sinif, metin) => h("span", { class: `rozet ${sinif}` }, metin);
const oncelikRozeti = (o) => rozet(`p-${oncelikAdi(o).toLowerCase()}`, ONCELIK_AD[oncelikAdi(o)]);
const durumRozeti = (d) => rozet(`d-${durumAdi(d).toLowerCase()}`, DURUM_AD[durumAdi(d)]);

const ikon = (...yollar) =>
  h("svg", { viewBox: "0 0 24 24", fill: "none", stroke: "currentColor", "stroke-width": "1.8", "stroke-linecap": "round", "stroke-linejoin": "round", "aria-hidden": "true" },
    yollar.map((d) => h("path", { d })));
const IKONLAR = {
  ozet: () => ikon("M3 12l9-8 9 8", "M5 10v10h5v-6h4v6h5V10"),
  ariza: () => ikon("M12 9v4", "M12 17h.01", "M10.3 3.9L2.4 18a2 2 0 001.7 3h15.8a2 2 0 001.7-3L13.7 3.9a2 2 0 00-3.4 0z"),
  istasyon: () => ikon("M3 21h18", "M5 21V8l7-5 7 5v13", "M9 21v-6h6v6"),
  kullanici: () => ikon("M16 20v-1a4 4 0 00-4-4H8a4 4 0 00-4 4v1", "M10 11a4 4 0 100-8 4 4 0 000 8", "M20 20v-1a4 4 0 00-3-3.9", "M15 3.2a4 4 0 010 7.6"),
};
const bosIkon = () => ikon("M4 4h16v16H4z", "M4 9h16", "M9 9v11");

// ---------------------------------------------------------------------------
// Oturum ve API
// ---------------------------------------------------------------------------

const Oturum = {
  oku: () => { try { return JSON.parse(sessionStorage.getItem("oturum") || "null"); } catch { return null; } },
  yaz: (o) => sessionStorage.setItem("oturum", JSON.stringify(o)),
  sil: () => sessionStorage.removeItem("oturum"),
};

function jwtCoz(token) {
  try {
    const yuk = JSON.parse(atob(token.split(".")[1].replace(/-/g, "+").replace(/_/g, "/")));
    const al = (...k) => k.map((x) => yuk[x]).find((v) => v != null);
    return {
      ad: al("unique_name", "name", "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name"),
      rol: al("role", "http://schemas.microsoft.com/ws/2008/06/identity/claims/role"),
    };
  } catch { return { ad: "", rol: "" }; }
}

class ApiHatasi extends Error {
  constructor(durum, mesaj) { super(mesaj); this.durum = durum; }
}

let yenilemeSozu = null;
/** Yenileme tokenı döndürülür; eşzamanlı 401'ler tek bir yenileme isteğini paylaşır. */
function tokenYenile() {
  if (yenilemeSozu) return yenilemeSozu;
  yenilemeSozu = (async () => {
    const o = Oturum.oku();
    if (!o?.yenilemeTokeni) throw new Error("oturum yok");
    const r = await fetch("/auth/yenile", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ yenilemeTokeni: o.yenilemeTokeni }) });
    if (!r.ok) throw new Error("yenilenemedi");
    const y = await r.json();
    Oturum.yaz({ token: y.token, yenilemeTokeni: y.yenilemeTokeni });
  })().finally(() => { yenilemeSozu = null; });
  return yenilemeSozu;
}

async function api(yol, { yontem = "GET", govde, ham = false } = {}, tekrar = true) {
  const o = Oturum.oku();
  const basliklar = {};
  if (govde !== undefined) basliklar["Content-Type"] = "application/json";
  if (o?.token) basliklar.Authorization = `Bearer ${o.token}`;
  const r = await fetch(yol, { method: yontem, headers: basliklar, body: govde !== undefined ? JSON.stringify(govde) : undefined });

  if (r.status === 401 && tekrar && o?.yenilemeTokeni && !yol.startsWith("/auth/")) {
    try { await tokenYenile(); } catch { cikisYap(false, "Oturum süresi doldu. Lütfen yeniden giriş yapın."); throw new ApiHatasi(401, "Oturum süresi doldu."); }
    return api(yol, { yontem, govde, ham }, false);
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

// ---------------------------------------------------------------------------
// Durum ve bildirimler
// ---------------------------------------------------------------------------

const durum = {
  ben: { ad: "", rol: "" },
  istasyonlar: [],
  sayfa: "ozet",
  acik: 0,
  liste: { sayfa: 1, boyut: 12, toplamSayfa: 1, filtre: { ara: "", durum: "", oncelik: "", gecikmis: false } },
};
const yonetici = () => durum.ben.rol === "Yonetici";
const istasyonAdi = (id) => durum.istasyonlar.find((i) => i.id === id)?.ad ?? `#${id}`;

let zamanlayici;
function duyur(tur, mesaj) {
  $("#genel-hata").hidden = true;
  $("#genel-basari").hidden = true;
  const el = $(tur === "hata" ? "#genel-hata" : "#genel-basari");
  el.textContent = mesaj;
  el.hidden = false;
  clearTimeout(zamanlayici);
  if (tur !== "hata") zamanlayici = setTimeout(() => { el.hidden = true; }, 4000);
}
const hataGoster = (e) => duyur("hata", e instanceof Error ? e.message : String(e));
const temizle = () => { $("#genel-hata").hidden = true; $("#genel-basari").hidden = true; };

// ---------------------------------------------------------------------------
// Giriş / çıkış
// ---------------------------------------------------------------------------

let girisModu = "giris";
$$(".sekme").forEach((b) => b.addEventListener("click", () => {
  girisModu = b.dataset.mod;
  $$(".sekme").forEach((x) => x.classList.toggle("aktif", x === b));
  $("#giris-gonder").textContent = girisModu === "giris" ? "Giriş yap" : "Kayıt ol ve giriş yap";
  $("#kayit-ipucu").hidden = girisModu !== "kayit";
  $("[name=sifre]", $("#giris-form")).autocomplete = girisModu === "giris" ? "current-password" : "new-password";
  $("#giris-hata").hidden = true;
}));

$("#giris-form").addEventListener("submit", async (ev) => {
  ev.preventDefault();
  const f = ev.target, hata = $("#giris-hata"), dugme = $("#giris-gonder");
  hata.hidden = true;
  const govde = { kullaniciAdi: f.kullaniciAdi.value.trim(), sifre: f.sifre.value };
  dugme.disabled = true;
  try {
    if (girisModu === "kayit") await api("/auth/kayit", { yontem: "POST", govde });
    const y = await api("/auth/giris", { yontem: "POST", govde });
    Oturum.yaz({ token: y.token, yenilemeTokeni: y.yenilemeTokeni });
    f.reset();
    await uygulamayiAc();
  } catch (e) { hata.textContent = e.message; hata.hidden = false; }
  finally { dugme.disabled = false; }
});

async function cikisYap(sunucuyaBildir = true, mesaj = "") {
  const o = Oturum.oku();
  if (sunucuyaBildir && o?.yenilemeTokeni) {
    try { await api("/auth/cikis", { yontem: "POST", govde: { yenilemeTokeni: o.yenilemeTokeni } }, false); } catch { /* yine de çık */ }
  }
  Oturum.sil();
  cekmece.close();
  $("#uygulama").hidden = true;
  $("#giris").hidden = false;
  const hata = $("#giris-hata");
  hata.textContent = mesaj;
  hata.hidden = !mesaj;
}
$("#cikis").addEventListener("click", () => cikisYap());

// ---------------------------------------------------------------------------
// Gezinme
// ---------------------------------------------------------------------------

const SAYFALAR = [
  { id: "ozet", ad: "Genel bakış", ikon: "ozet", gorunur: () => true },
  { id: "arizalar", ad: "Arızalar", ikon: "ariza", gorunur: () => true },
  { id: "istasyonlar", ad: "İstasyonlar", ikon: "istasyon", gorunur: () => true },
  { id: "kullanicilar", ad: "Kullanıcılar", ikon: "kullanici", gorunur: yonetici },
];
const YUKLEYICILER = { ozet: ozetSayfasi, arizalar: arizalarSayfasi, istasyonlar: istasyonlarSayfasi, kullanicilar: kullanicilarSayfasi };

function menuyuCiz() {
  $("#menu").replaceChildren(...SAYFALAR.filter((s) => s.gorunur()).map((s) =>
    h("button", { class: s.id === durum.sayfa ? "aktif" : "", onclick: () => sayfaAc(s.id) },
      IKONLAR[s.ikon](), s.ad,
      s.id === "arizalar" && durum.acik > 0 ? h("span", { class: "sayac", title: "Açık arıza" }, durum.acik) : null)));
}

const menuKapat = () => { $("#yan").classList.remove("acik"); $("#yan-perde").hidden = true; };
$("#menu-ac").addEventListener("click", () => { $("#yan").classList.add("acik"); $("#yan-perde").hidden = false; });
$("#yan-perde").addEventListener("click", menuKapat);

async function sayfaAc(id) {
  durum.sayfa = id;
  menuKapat();
  temizle();
  menuyuCiz();
  const s = SAYFALAR.find((x) => x.id === id);
  $("#sayfa-baslik").textContent = s.ad;
  $("#sayfa-alt").textContent = "";
  $("#ust-eylem").replaceChildren();
  $("#icerik").replaceChildren(h("p", { class: "soluk" }, "Yükleniyor…"));
  try { await YUKLEYICILER[id](); } catch (e) { if (e.durum !== 401) { $("#icerik").replaceChildren(); hataGoster(e); } }
  oranlariUygula();
}

async function acikSayisi() {
  try {
    const o = await api("/arizalar/ozet");
    durum.acik = (o.durumlar?.Acik ?? 0) + (o.durumlar?.Atandi ?? 0);
    menuyuCiz();
    return o;
  } catch { return null; }
}

async function uygulamayiAc() {
  const k = jwtCoz(Oturum.oku().token);
  durum.ben = k;
  $("#kullanici-ad").textContent = k.ad;
  $("#kullanici-rol").textContent = yonetici() ? "Yönetici" : "Personel";
  $("#avatar").textContent = bas(k.ad);
  $("#giris").hidden = true;
  $("#uygulama").hidden = false;
  try { durum.istasyonlar = await api("/istasyonlar"); } catch (e) { hataGoster(e); }
  await acikSayisi();
  await sayfaAc("ozet");
}

// ---------------------------------------------------------------------------
// Ortak bileşenler
// ---------------------------------------------------------------------------

const bosDurum = (baslik, aciklama) => h("div", { class: "bos" }, bosIkon(), h("strong", {}, baslik), aciklama ? h("span", {}, aciklama) : null);

function sayfalama(sayfa, toplamSayfa, toplam, git) {
  return h("div", { class: "sayfalama" },
    h("span", { class: "soluk kucuk-yazi" }, `${toplam} kayıt · sayfa ${sayfa} / ${Math.max(1, toplamSayfa)}`),
    h("button", { class: "dugme ikincil kucuk", disabled: sayfa <= 1, onclick: () => git(sayfa - 1) }, "‹ Önceki"),
    h("button", { class: "dugme ikincil kucuk", disabled: sayfa >= toplamSayfa, onclick: () => git(sayfa + 1) }, "Sonraki ›"));
}

const dialog = $("#dialog");
const cekmece = $("#cekmece");
const dialogKapat = () => dialog.close();
[dialog, cekmece].forEach((d) => d.addEventListener("click", (e) => { if (e.target === d) d.close(); }));

/** Form penceresi: gönderirken düğmeyi kilitler, hatayı pencerede gösterir. */
function formDialog({ baslik, aciklama, alanlar, onay, tehlike = false, gonder }) {
  const hata = h("p", { class: "hata", role: "alert", hidden: true });
  const form = h("form", { class: "dialog-icerik" },
    h("h2", {}, baslik),
    aciklama ? h("p", { class: "ipucu-kutu" }, aciklama) : null,
    alanlar ? h("div", { class: "form-izgara" }, alanlar) : null,
    hata,
    h("div", { class: "dialog-alt" },
      h("button", { type: "button", class: "dugme ikincil", onclick: dialogKapat }, "Vazgeç"),
      h("button", { type: "submit", class: `dugme ${tehlike ? "tehlike" : "birincil"}` }, onay)));
  form.addEventListener("submit", async (ev) => {
    ev.preventDefault();
    const dugme = $("[type=submit]", form);
    hata.hidden = true;
    dugme.disabled = true;
    try { await gonder(new FormData(form)); dialogKapat(); }
    catch (e) { hata.textContent = e.message; hata.hidden = false; }
    finally { dugme.disabled = false; }
  });
  $("#dialog-icerik").replaceChildren(form);
  dialog.showModal();
  const ilk = $("input, select, textarea", form);
  if (ilk) ilk.focus();
}
const alan = (etiket, girdi, tam = false) => h("label", { class: tam ? "tam" : "" }, etiket, girdi);

// ---------------------------------------------------------------------------
// Genel bakış
// ---------------------------------------------------------------------------

function halka(parcalar, toplam) {
  const r = 58, c = 2 * Math.PI * r;
  let ofset = 0;
  const dilimler = parcalar.filter((p) => p.sayi > 0).map((p) => {
    const uzunluk = (p.sayi / Math.max(toplam, 1)) * c;
    const e = h("circle", { cx: 75, cy: 75, r, fill: "none", stroke: p.renk, "stroke-width": 16, "stroke-dasharray": `${uzunluk} ${c - uzunluk}`, "stroke-dashoffset": -ofset, transform: "rotate(-90 75 75)" });
    ofset += uzunluk;
    return e;
  });
  return h("svg", { class: "halka", viewBox: "0 0 150 150", role: "img", "aria-label": `${toplam} kayıt, durum dağılımı` },
    h("circle", { cx: 75, cy: 75, r, fill: "none", stroke: "var(--cizgi)", "stroke-width": 16 }),
    dilimler,
    h("text", { x: 75, y: 80, "text-anchor": "middle", class: "halka-metin", fill: "currentColor" }, String(toplam)),
    h("text", { x: 75, y: 97, "text-anchor": "middle", class: "halka-alt" }, "toplam kayıt"));
}

async function ozetSayfasi() {
  const [ozet, geciken, ...oncelikler] = await Promise.all([
    api("/arizalar/ozet"),
    api("/arizalar?gecikmis=true&boyut=5"),
    ...ONCELIK.map((o) => api(`/arizalar?oncelik=${o}&boyut=1`)),
  ]);
  const say = (d) => ozet.durumlar?.[d] ?? 0;
  const toplam = DURUM.reduce((t, d) => t + say(d), 0);
  durum.acik = say("Acik") + say("Atandi");
  menuyuCiz();

  $("#sayfa-baslik").textContent = `Merhaba, ${durum.ben.ad}`;
  $("#sayfa-alt").textContent = ozet.gecikmis ? `${ozet.gecikmis} arızada SLA süresi aşıldı; önce bunlara bakmanı öneririz.` : "SLA süresi aşılan arıza yok. Her şey yolunda.";
  $("#ust-eylem").replaceChildren(h("button", { class: "dugme birincil", onclick: yeniArizaDialogu }, "+ Yeni arıza"));

  const kart = (etiket, sayi, sinif, not, filtre) => h("button", { class: `kart istatistik ${sinif}`, onclick: () => arizalaraGit(filtre) },
    h("span", { class: "etiket" }, h("i", { class: "nokta" }), etiket), h("span", { class: "deger" }, String(sayi)), h("span", { class: "not" }, not));

  const oncelikSayilari = ONCELIK.map((o, i) => ({ ad: o, sayi: oncelikler[i].toplam })).reverse();
  const enCok = Math.max(1, ...oncelikSayilari.map((x) => x.sayi));

  $("#icerik").replaceChildren(
    h("div", { class: "izgara" },
      kart("Açık", say("Acik"), "d-acik", "atama bekliyor", { durum: "Acik" }),
      kart("Atandı", say("Atandi"), "d-atandi", "üzerinde çalışılıyor", { durum: "Atandi" }),
      kart("Çözüldü", say("Cozuldu"), "d-cozuldu", "kapatma bekliyor", { durum: "Cozuldu" }),
      kart("Kapandı", say("Kapandi"), "d-kapandi", "tamamlandı", { durum: "Kapandi" }),
      kart("SLA gecikmiş", ozet.gecikmis ?? 0, "d-acik", "hedef süre aşıldı", { gecikmis: true })),
    h("div", { class: "iki-sutun" },
      h("section", { class: "kart" },
        h("div", { class: "kart-baslik" }, h("h3", {}, "Durum dağılımı")),
        h("div", { class: "grafik-kap" }, halka(DURUM.map((d) => ({ sayi: say(d), renk: DURUM_RENK[d] })), toplam),
          h("ul", { class: "gosterge" }, DURUM.map((d) => h("li", { class: `d-${d.toLowerCase()}` },
            h("i", { class: "nokta" }), DURUM_AD[d], h("span", { class: "sayi" }, String(say(d)))))))),
      h("section", { class: "kart" },
        h("div", { class: "kart-baslik" }, h("h3", {}, "SLA süresi aşılan kayıtlar"),
          h("button", { class: "dugme ikincil kucuk", onclick: () => arizalaraGit({ gecikmis: true }) }, "Tümü")),
        geciken.ogeler.length
          ? h("ul", { class: "dikkat" }, geciken.ogeler.map((a) => h("li", { tabindex: "0", onclick: () => cekmeceAc(a.id), onkeydown: (e) => { if (e.key === "Enter") cekmeceAc(a.id); } },
              h("div", { class: "metin" }, h("strong", {}, a.baslik), h("span", { class: "soluk kucuk-yazi" }, `${a.istasyon?.ad ?? istasyonAdi(a.istasyonId)} · #${a.id}`)),
              oncelikRozeti(a.oncelik), slaGostergesi(a))))
          : bosDurum("Geciken arıza yok", "Tüm açık kayıtlar SLA süresi içinde."))),
    h("section", { class: "kart" },
      h("div", { class: "kart-baslik" }, h("h3", {}, "Önceliğe göre kayıtlar"), h("span", { class: "soluk kucuk-yazi" }, "SLA: Kritik 4 sa · Yüksek 24 sa · Normal 72 sa · Düşük 7 gün")),
      h("div", { class: "cubuk-grafik" }, oncelikSayilari.map((x) => h("div", { class: `cubuk-satir p-${x.ad.toLowerCase()}` },
        h("span", {}, ONCELIK_AD[x.ad]),
        h("div", { class: "cubuk-ray" }, h("span", { "data-oran": Math.round((x.sayi / enCok) * 100) })),
        h("span", { class: "cubuk-sayi" }, String(x.sayi)))))));
}

function arizalaraGit(filtre) {
  durum.liste.filtre = { ara: "", durum: "", oncelik: "", gecikmis: false, ...filtre };
  durum.liste.sayfa = 1;
  sayfaAc("arizalar");
}

// ---------------------------------------------------------------------------
// Arızalar
// ---------------------------------------------------------------------------

function sorgu(f) {
  const p = new URLSearchParams();
  if (f.durum) p.set("durum", f.durum);
  if (f.oncelik) p.set("oncelik", f.oncelik);
  if (f.gecikmis) p.set("gecikmis", "true");
  if (f.ara) p.set("ara", f.ara);
  return p;
}

async function arizalarSayfasi() {
  const L = durum.liste, f = L.filtre;
  $("#sayfa-alt").textContent = "Öncelik ve SLA sırasına göre listelenir";
  doldur($("#ust-eylem"),
    yonetici() ? h("button", { class: "dugme ikincil", onclick: csvIndir }, "CSV indir") : null,
    h("button", { class: "dugme birincil", onclick: yeniArizaDialogu }, "+ Yeni arıza"));

  const secim = (id, secenekler, deger, ilk) => h("select", { id, "aria-label": ilk },
    h("option", { value: "" }, ilk), ...secenekler.map(([k, v]) => h("option", { value: k, selected: k === deger }, v)));
  const ara = h("input", { id: "f-ara", type: "search", placeholder: "Başlık veya açıklamada ara…", "aria-label": "Ara", value: f.ara });
  let zamanla;
  ara.addEventListener("input", () => { clearTimeout(zamanla); zamanla = setTimeout(() => { f.ara = ara.value.trim(); L.sayfa = 1; tabloYukle(); }, 300); });
  const gecikmis = h("input", { id: "f-gecikmis", type: "checkbox", checked: f.gecikmis });
  gecikmis.addEventListener("change", () => { f.gecikmis = gecikmis.checked; L.sayfa = 1; tabloYukle(); });
  const durumSecim = secim("f-durum", Object.entries(DURUM_AD), f.durum, "Tüm durumlar");
  const oncSecim = secim("f-oncelik", ONCELIK.slice().reverse().map((o) => [o, ONCELIK_AD[o]]), f.oncelik, "Tüm öncelikler");
  durumSecim.addEventListener("change", (e) => { f.durum = e.target.value; L.sayfa = 1; tabloYukle(); });
  oncSecim.addEventListener("change", (e) => { f.oncelik = e.target.value; L.sayfa = 1; tabloYukle(); });

  $("#icerik").replaceChildren(
    h("div", { class: "arac-cubugu" }, ara, durumSecim, oncSecim, h("label", { class: "onay-kutu" }, gecikmis, "Yalnız SLA gecikmiş")),
    h("section", { class: "kart", id: "tablo-kart" }));
  await tabloYukle();
}

async function tabloYukle() {
  const L = durum.liste;
  try {
    const p = sorgu(L.filtre);
    p.set("sayfa", L.sayfa);
    p.set("boyut", L.boyut);
    const veri = await api(`/arizalar?${p}`);
    L.toplamSayfa = Math.max(1, veri.toplamSayfa ?? Math.ceil(veri.toplam / L.boyut));
    const tablo = veri.ogeler.length
      ? h("div", { class: "tablo-kap" }, h("table", {},
          h("thead", {}, h("tr", {}, ["#", "Arıza", "Öncelik", "Durum", "Atanan", "SLA"].map((b) => h("th", {}, b)))),
          h("tbody", {}, veri.ogeler.map((a) => h("tr", { class: "tikla", tabindex: "0", onclick: () => cekmeceAc(a.id), onkeydown: (e) => { if (e.key === "Enter") cekmeceAc(a.id); } },
            h("td", {}, h("code", {}, `#${a.id}`)),
            h("td", { class: "baslik-hucre" }, h("strong", {}, a.baslik), h("span", { class: "soluk kucuk-yazi" }, `${a.istasyon?.ad ?? istasyonAdi(a.istasyonId)} · ${yerel(a.olusturmaZamani)}`)),
            h("td", {}, oncelikRozeti(a.oncelik)),
            h("td", {}, durumRozeti(a.durum)),
            h("td", {}, a.atananKisi ? h("div", { class: "kisi" }, h("span", { class: "avatar" }, bas(a.atananKisi)), a.atananKisi) : h("span", { class: "soluk" }, "–")),
            h("td", {}, slaGostergesi(a)))))))
      : bosDurum("Kayıt bulunamadı", "Filtreleri değiştirmeyi ya da yeni bir arıza açmayı deneyin.");
    doldur($("#tablo-kart"), tablo, veri.ogeler.length || L.sayfa > 1 ? sayfalama(L.sayfa, L.toplamSayfa, veri.toplam, (s) => { L.sayfa = s; tabloYukle(); }) : null);
    oranlariUygula($("#tablo-kart"));
  } catch (e) { if (e.durum !== 401) hataGoster(e); }
}

async function csvIndir() {
  try {
    const r = await api(`/arizalar/disa-aktar?${sorgu({ ...durum.liste.filtre, ara: "" })}`, { ham: true });
    const url = URL.createObjectURL(await r.blob());
    const a = h("a", { href: url, download: "arizalar.csv" });
    document.body.append(a); a.click(); a.remove();
    URL.revokeObjectURL(url);
    duyur("basari", "CSV dosyası indirildi.");
  } catch (e) { hataGoster(e); }
}

function yeniArizaDialogu() {
  if (!durum.istasyonlar.length) {
    duyur("hata", "Önce İstasyonlar sayfasından bir istasyon ekleyin.");
    return;
  }
  formDialog({
    baslik: "Yeni arıza", onay: "Oluştur",
    alanlar: [
      alan("İstasyon", h("select", { name: "istasyonId", required: true }, durum.istasyonlar.map((i) => h("option", { value: i.id }, `${i.ad} (${i.sehir})`))), true),
      alan("Başlık", h("input", { name: "baslik", required: true, maxlength: "200", placeholder: "Örn. Peron aydınlatması yanmıyor" }), true),
      alan("Açıklama", h("textarea", { name: "aciklama", rows: "4", maxlength: "2000" }), true),
      alan("Öncelik", h("select", { name: "oncelik" }, ONCELIK.map((o, i) => h("option", { value: i, selected: o === "Normal" }, `${ONCELIK_AD[o]} · SLA ${SLA_SAAT[o] >= 48 ? `${SLA_SAAT[o] / 24} gün` : `${SLA_SAAT[o]} saat`}`))), true),
    ],
    gonder: async (f) => {
      const a = await api("/arizalar", { yontem: "POST", govde: { istasyonId: Number(f.get("istasyonId")), baslik: f.get("baslik").trim(), aciklama: f.get("aciklama").trim() || null, oncelik: Number(f.get("oncelik")) } });
      duyur("basari", `#${a.id} numaralı arıza oluşturuldu.`);
      await acikSayisi();
      if (durum.sayfa === "arizalar") { durum.liste.sayfa = 1; await tabloYukle(); } else await sayfaAc(durum.sayfa);
    },
  });
}

// ---------------------------------------------------------------------------
// Detay çekmecesi
// ---------------------------------------------------------------------------

async function cekmeceAc(id) {
  $("#cekmece-icerik").replaceChildren(h("p", { class: "soluk" }, "Yükleniyor…"));
  if (!cekmece.open) cekmece.showModal();
  try {
    const [a, gecmis] = await Promise.all([api(`/arizalar/${id}`), api(`/arizalar/${id}/gecmis`)]);
    cekmeceCiz(a, gecmis);
  } catch (e) { cekmece.close(); if (e.durum !== 401) hataGoster(e); }
}

function cekmeceCiz(a, gecmis) {
  const d = durumAdi(a.durum);
  const sla = slaDurumu(a);
  const hata = h("p", { class: "hata", role: "alert", hidden: true });

  const islem = async (yol, govde) => {
    hata.hidden = true;
    try {
      await api(`/arizalar/${a.id}/${yol}`, { yontem: "POST", govde });
      const [yeni, yeniGecmis] = await Promise.all([api(`/arizalar/${a.id}`), api(`/arizalar/${a.id}/gecmis`)]);
      cekmeceCiz(yeni, yeniGecmis);
      await acikSayisi();
      if (durum.sayfa === "arizalar") tabloYukle(); else sayfaAc(durum.sayfa);
    } catch (e) { hata.textContent = e.message; hata.hidden = false; }
  };

  const atanan = h("input", { placeholder: "Atanacak kişi", maxlength: "100", "aria-label": "Atanacak kişi", value: a.atananKisi || "" });
  const eylemler = [];
  if (d === "Acik" || d === "Atandi") {
    eylemler.push(h("div", { class: "atama-satiri" }, atanan,
      h("button", { class: "dugme ikincil", onclick: () => { if (atanan.value.trim()) islem("ata", { kisi: atanan.value.trim() }); } }, d === "Acik" ? "Ata" : "Yeniden ata")));
  }
  const alt = [];
  if (d === "Atandi") alt.push(h("button", { class: "dugme basari-dugme", onclick: () => islem("coz") }, "Çözüldü olarak işaretle"));
  if (yonetici() && d !== "Kapandi") alt.push(h("button", { class: "dugme tehlike", onclick: () => islem("kapat") }, "Kapat"));

  doldur($("#cekmece-icerik"),
    h("div", { class: "cekmece-baslik" },
      h("div", {}, h("span", { class: "soluk" }, `Arıza #${a.id}`), h("h2", {}, a.baslik), h("div", { class: "rozet-satir" }, oncelikRozeti(a.oncelik), durumRozeti(a.durum))),
      h("button", { class: "dugme ikincil kucuk", onclick: () => cekmece.close(), "aria-label": "Kapat" }, "✕")),
    sla ? h("div", { class: "sla-kart" }, h("span", { class: "soluk kucuk-yazi" }, `SLA hedefi: ${yerel(a.slaSonTarih)}`), slaGostergesi(a)) : null,
    h("dl", { class: "meta" },
      h("dt", {}, "İstasyon"), h("dd", {}, a.istasyon?.ad ?? istasyonAdi(a.istasyonId)),
      h("dt", {}, "Atanan"), h("dd", {}, a.atananKisi || "Atanmadı"),
      h("dt", {}, "Oluşturma"), h("dd", {}, yerel(a.olusturmaZamani)),
      a.kapanmaZamani ? [h("dt", {}, "Kapanma"), h("dd", {}, yerel(a.kapanmaZamani))] : null),
    a.aciklama ? [h("h3", { class: "bolum-baslik" }, "Açıklama"), h("p", { class: "aciklama-kutu" }, a.aciklama)] : null,
    eylemler.length || alt.length ? h("div", { class: "islem-kutu" }, h("h3", { class: "bolum-baslik" }, "İşlemler"), ...eylemler, hata, alt.length ? h("div", { class: "dialog-alt" }, alt) : null) : null,
    h("h3", { class: "bolum-baslik" }, "İşlem geçmişi"),
    h("ul", { class: "zaman-cizgisi" }, gecmis.map((g) => h("li", {},
      h("strong", {}, ISLEM_AD[g.islem] ?? g.islem), " · ", g.kullanici,
      h("div", { class: "zaman" }, yerel(g.zaman)),
      g.eskiDurum != null ? h("div", { class: "soluk kucuk-yazi" }, `${DURUM_AD[durumAdi(g.eskiDurum)]} → ${DURUM_AD[durumAdi(g.yeniDurum)]}`) : null))));
  oranlariUygula(cekmece);
}

// ---------------------------------------------------------------------------
// İstasyonlar
// ---------------------------------------------------------------------------

async function istasyonlarSayfasi() {
  durum.istasyonlar = await api("/istasyonlar");
  $("#sayfa-alt").textContent = `${durum.istasyonlar.length} istasyon`;
  $("#ust-eylem").replaceChildren(h("button", { class: "dugme birincil", onclick: () => formDialog({
    baslik: "Yeni istasyon", onay: "Ekle",
    alanlar: [alan("Ad", h("input", { name: "ad", required: true, maxlength: "100" }), true), alan("Şehir", h("input", { name: "sehir", required: true, maxlength: "50" }), true)],
    gonder: async (f) => { await api("/istasyonlar", { yontem: "POST", govde: { ad: f.get("ad").trim(), sehir: f.get("sehir").trim() } }); duyur("basari", "İstasyon eklendi."); await istasyonlarSayfasi(); },
  }) }, "+ İstasyon ekle"));
  const tablo = durum.istasyonlar.length
    ? h("div", { class: "tablo-kap" }, h("table", {}, h("thead", {}, h("tr", {}, ["#", "İstasyon", "Şehir"].map((b) => h("th", {}, b)))),
        h("tbody", {}, durum.istasyonlar.map((i) => h("tr", {}, h("td", {}, h("code", {}, `#${i.id}`)), h("td", {}, h("strong", {}, i.ad)), h("td", {}, i.sehir))))))
    : bosDurum("Henüz istasyon yok", "Arıza açabilmek için önce bir istasyon ekleyin.");
  $("#icerik").replaceChildren(h("section", { class: "kart" }, tablo));
}

// ---------------------------------------------------------------------------
// Kullanıcılar (yönetici)
// ---------------------------------------------------------------------------

async function kullanicilarSayfasi() {
  const liste = await api("/kullanicilar");
  $("#sayfa-alt").textContent = "Rol ve erişim yönetimi";
  const degistir = async (yol, govde) => {
    temizle();
    try { await api(yol, { yontem: "PUT", govde }); } catch (e) { hataGoster(e); }
    await kullanicilarSayfasi();
  };
  $("#icerik").replaceChildren(h("section", { class: "kart" }, h("div", { class: "tablo-kap" }, h("table", {},
    h("thead", {}, h("tr", {}, ["Kullanıcı", "Rol", "Durum", ""].map((b) => h("th", {}, b)))),
    h("tbody", {}, liste.map((k) => {
      const kendim = k.kullaniciAdi === durum.ben.ad;
      return h("tr", {},
        h("td", {}, h("div", { class: "kisi" }, h("span", { class: "avatar" }, bas(k.kullaniciAdi)), k.kullaniciAdi, kendim ? h("span", { class: "soluk" }, "(sen)") : null)),
        h("td", {}, h("select", { "aria-label": `${k.kullaniciAdi} rolü`, onchange: (e) => degistir(`/kullanicilar/${k.id}/rol`, { rol: e.target.value }) },
          ["Personel", "Yonetici"].map((r) => h("option", { value: r, selected: r === k.rol }, r === "Yonetici" ? "Yönetici" : "Personel")))),
        h("td", {}, rozet(k.aktif ? "aktif-rozet" : "pasif-rozet", k.aktif ? "Aktif" : "Pasif")),
        h("td", {}, h("div", { class: "eylemler" }, h("button", { class: k.aktif ? "dugme tehlike kucuk" : "dugme ikincil kucuk", onclick: () => degistir(`/kullanicilar/${k.id}/durum`, { aktif: !k.aktif }) }, k.aktif ? "Pasife al" : "Aktifleştir"))));
    }))))));
}

// ---------------------------------------------------------------------------
// Şifre değiştir
// ---------------------------------------------------------------------------

$("#sifre-ac").addEventListener("click", () => formDialog({
  baslik: "Şifre değiştir", onay: "Değiştir",
  aciklama: "Değişiklikten sonra tüm oturumların kapanır, yeniden giriş yapman gerekir.",
  alanlar: [
    alan("Mevcut şifre", h("input", { name: "mevcutSifre", type: "password", required: true, autocomplete: "current-password" }), true),
    alan("Yeni şifre", h("input", { name: "yeniSifre", type: "password", required: true, minlength: "8", autocomplete: "new-password" }), true),
  ],
  gonder: async (f) => {
    await api("/auth/sifre-degistir", { yontem: "POST", govde: { mevcutSifre: f.get("mevcutSifre"), yeniSifre: f.get("yeniSifre") } });
    await cikisYap(false, "Şifre değiştirildi. Yeni şifrenle giriş yap.");
  },
}));

// ---------------------------------------------------------------------------
// Başlangıç
// ---------------------------------------------------------------------------

if (Oturum.oku()) uygulamayiAc().catch(() => cikisYap(false));
else $("#giris").hidden = false;
