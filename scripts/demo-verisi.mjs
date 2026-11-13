// Demo verisi yükler: kullanıcılar, istasyonlar ve farklı durumlarda arıza kayıtları (Node 18+).
// Kullanım: node scripts/demo-verisi.mjs [http://localhost:8080]
// Boş bir veritabanında çalıştırın: ilk kayıt olan kullanıcı Yönetici olur.
const B = process.argv[2] || 'http://localhost:8080';
const SIFRE = 'Demo-Sifre-123';

async function api(yol, yontem = 'GET', govde, token) {
  const r = await fetch(B + yol, {
    method: yontem,
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: 'Bearer ' + token } : {}) },
    body: govde ? JSON.stringify(govde) : undefined,
  });
  const t = await r.text();
  if (!r.ok) throw new Error(`${yontem} ${yol} -> ${r.status} ${t.slice(0, 200)}`);
  return t ? JSON.parse(t) : null;
}
const giris = async (kullaniciAdi) => (await api('/auth/giris', 'POST', { kullaniciAdi, sifre: SIFRE })).token;

for (const k of ['yonetici', 'ayse.k', 'mehmet.t', 'selin.d']) await api('/auth/kayit', 'POST', { kullaniciAdi: k, sifre: SIFRE });
const t = await giris('yonetici');

const istasyonlar = [
  ['Ankara Garı', 'Ankara'], ['Esenboğa Havalimanı', 'Ankara'], ['Polatlı Trafo Merkezi', 'Polatlı'],
  ['Eskişehir İstasyonu', 'Eskişehir'], ['Sincan Yönetim Binası', 'Ankara'], ['Kırıkkale Aktarma Merkezi', 'Kırıkkale'],
];
const ist = [];
for (const [ad, sehir] of istasyonlar) ist.push((await api('/istasyonlar', 'POST', { ad, sehir }, t)).id);

// [istasyon, başlık, açıklama, öncelik (0 Düşük .. 3 Kritik), atanan, çöz, kapat]
const arizalar = [
  [1, 'Bagaj bandı B hattı durdu', 'Motor aşırı ısınma hatası veriyor, bant hareket etmiyor.', 3, 'mehmet.t'],
  [0, 'Peron 3 aydınlatması yanmıyor', '6 armatür arızalı, yolcu güvenliği için acil.', 2, 'ayse.k'],
  [2, 'Trafo yağ seviyesi alarmı', 'Seviye sensörü minimum eşiğin altını gösteriyor.', 3],
  [3, 'Biletmatik ekranı donuyor', 'Üçüncü cihazda dokunmatik ekran yanıt vermiyor.', 1, 'selin.d', true],
  [4, 'Klima soğutmuyor', 'İkinci kat toplantı salonu.', 0],
  [0, 'Yürüyen merdiven sağ hat durdu', 'Güvenlik rölesi devreye girmiş olabilir.', 2],
  [5, 'Acil çıkış levhası kırık', 'Doğu koridoru, bağlantı aparatı kopmuş.', 1, 'ayse.k'],
  [1, 'Güvenlik kapısı sensör hatası', 'Metal dedektörü sürekli alarm veriyor.', 2, 'mehmet.t', true, true],
  [2, 'Saha kamerası görüntü vermiyor', 'K4 kamerası bağlantı kaybı gösteriyor.', 1],
  [3, 'Duyuru sistemi kesik kesik çalışıyor', 'Peron hoparlörlerinde parazit.', 1, 'selin.d'],
  [0, 'Asansör periyodik bakım uyarısı', 'Bakım tarihi yaklaştı.', 0, 'ayse.k', true, true],
  [4, 'İkinci kat su kaçağı', 'Tavan döşemesinde su damlıyor.', 2, 'mehmet.t'],
  [5, 'Yangın dedektörü yanlış alarm veriyor', 'Z3 bölgesi dedektörü gece alarm üretti.', 3, 'selin.d', true],
  [1, 'Otopark bariyeri açılmıyor', 'Kart okuyucu yanıt vermiyor.', 1],
];
for (const [i, baslik, aciklama, oncelik, atanan, coz, kapat] of arizalar) {
  const a = await api('/arizalar', 'POST', { istasyonId: ist[i], baslik, aciklama, oncelik }, t);
  if (atanan) await api(`/arizalar/${a.id}/ata`, 'POST', { kisi: atanan }, t);
  if (coz) await api(`/arizalar/${a.id}/coz`, 'POST', null, t);
  if (kapat) await api(`/arizalar/${a.id}/kapat`, 'POST', null, t);
}
console.log(`tamam: ${ist.length} istasyon, ${arizalar.length} arıza`);
