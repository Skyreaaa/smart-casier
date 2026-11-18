# Audit Intake Data Asli — Smart Casier

**Tanggal:** 18 November 2026  
**Tujuan:** mencari kandidat **barang non-makanan** untuk katalog POS, bukan mengisi stok/harga secara otomatis.

## Hasil terverifikasi

| Pengukuran | Open Products Facts | PRDECT-ID |
| --- | ---: | ---: |
| Ukuran berkas unduh | 6.496.518 byte | 1.262.177 byte |
| Jumlah baris sumber | 49.771 | 5.400 |
| Baris terpilih | 233 bertag Indonesia | 1.000 pada kategori relevan |
| Lolos pemetaan konservatif | **0** | **261** setelah filter + deduplikasi |
| Masuk master POS | **0** | **0** |

PRDECT-ID menghasilkan 261 record kandidat; setiap record masuk staging eksperimen sebagai **HOLD_LICENSE**. Tidak ada yang berstatus approved.

## Rincian PRDECT-ID

- File asal: [PRDECT-ID_Dataset.csv](https://huggingface.co/datasets/reyhanksatria05/PRDECT-ID_Dataset/tree/main), dibaca dari sumber publik.
- SHA256 file asli: `1dfdde6bb169ad57aab4211ecf45a75a4111b774ab43932f6d39c349bfd92bde`.
- Dari 5.400 baris, 4.400 di luar lima kategori dan 1.000 dalam kategori; 9 nama ditahan karena indikasi makanan/kontak; 730 baris duplikat setelah normalisasi; **261 kandidat**.
- SHA256 CSV kandidat tanpa PII: `ead4407786a83273de1a333dcbe2b06e7329fb10a1a987618a8075a72ba04ad`.

| Kategori staging | Kandidat |
| --- | ---: |
| Automotive general | 49 |
| Hardware general / pertukangan | 25 |
| Stationery | 51 |
| Electronics general | 73 |
| Household non-food | 63 |
| **Total** | **261** |

Banyak judul otomotif adalah aksesoris/perawatan, bukan suku cadang mesin. Kategori tersebut **sementara**, dan harga serta variasi produk e-commerce tidak otomatis sah sebagai harga/SKU toko.

**Perbaikan quality gate:** pertama kali muncul satu false positive dari teks warna *Putih Susu* pada oven listrik. Filter diperbaiki dan diuji; setelah impor ulang seluruh **261** masuk HOLD_LICENSE. Tes semantik baru ikut pada [47 tes CI yang lulus](https://github.com/Skyreaaa/smart-casier/actions/runs/44273327558).

## Open Products Facts

- Sumber: [CSV gzip publik](https://static.openproductsfacts.org/data/en.openproductsfacts.org.products.csv.gz).
- SHA256: `a410d9be9c75135f8961480689e0977c56f57c775f89d55fdad41cd572f7c378`.
- Dari **49.771** baris, hanya **233** bertag negara Indonesia; **0** cocok dengan kelas non-makanan toko secara eksplisit (`fasteners`, `plumbing`, `tools`, `building-materials`, dll).
- Produk yang kategorinya ambigu **tidak** dipaksa menjadi barang toko. Karena itu, sumber tidak menghasilkan kandidat yang langsung bisa dipakai.

## Batas hukum dan privasi

1. PRDECT-ID mencantumkan `cc0-1.0` di metadata Hugging Face, tetapi file `LICENSE` kosong; izin penggunaan kembali atas materi e-commerce asal masih belum diverifikasi. **Registry tetap PENDING.**
2. Open Products Facts dilisensikan ODbL dan punya implikasi atribusi/share-alike pada penggunaan tertentu. Data tidak dicampur dengan inventaris privat.
3. **Dataset asli, ulasan pelanggan, lokasi, rating, harga dan informasi sensitif tidak pernah di-commit** ke GitHub. Hanya kode, tes dengan fixture sintetis, metadata statistik, dan audit sumber yang disimpan.
4. Audit dilakukan di sandbox staging terpisah. File staging riil bukan bagian dari deploy aplikasi POS, dan tidak dapat dianggap sudah tersedia pada PC kasir.

## Kesimpulan & tindakan berikutnya

- 261 **nama kandidat non-makanan** siap untuk tahap audit lisensi dan *category review*, tetapi **bukan SKU valid**.
- **0 nama disetujui untuk promosi**, **0 stok**, **0 harga** dibawa dari sumber luar, **0 SKU toko terverifikasi**.
- Jangan memaksa persetujuan lisensi hanya demi menaikkan angka progres. Bila ragu, minta izin pemilik data atau kumpulkan nama dan spesifikasi dari katalog supplier/toko sendiri.
- Untuk barang yang benar-benar kritis (baut ukuran spesifik, fitting, kertas minyak), lakukan pencatatan 30 SKU nyata di toko beserta atribut ukuran/warna/satuan.

Baca juga [panduan konverter](NONFOOD_INTAKE.md) dan [registri sumber](../data/sources/nonfood_registry.json).
