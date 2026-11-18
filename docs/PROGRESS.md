# Laporan Progres — Smart Casier

**EXECUTION MODE: SOFTWARE_ONLY** — [detail keputusan dan daftar tugas fisik ditunda](EXECUTION_POLICY_SOFTWARE_ONLY.md). Tugas lapangan tidak dihapus atau di-centang. Ketiadaan 30 SKU nyata tidak memblokir development aplikasi dengan fixture sintetis.


**Per 18 November 2026** · Tahap **Software-Only → Windows WPF / POS Core**

## Ringkasan terverifikasi

- **13/32 checklist selesai (41%):** termasuk dasar database dan 2 acceptance criteria POS. Angka bukan kesiapan produksi.
- **WPF login lokal Admin/Kasir dan pemeriksaan akses Admin di Core tersedia.** [Build dan 53 tes per OS lulus](https://github.com/Skyreaaa/smart-casier/actions/runs/44285643615). Restore tetap non-destruktif; GUI klik/aksesibilitas dan database/OS hardening belum diuji.
- **Repository:** [Skyreaaa/smart-casier](https://github.com/Skyreaaa/smart-casier), private, branch main.
- **Tugas aktif berikutnya:** hardening autentikasi seluruh operasi Core dan histori audit mutasi per operator, lalu simulator barcode/encoder; aktivasi restore terkontrol dan backup terjadwal tetap backlog. Validasi fisik tetap DEFERRED_ON_SITE.
- **Risiko utama:** variasi barang tanpa barcode, ukuran benda yang mirip, satuan stok meter/pcs, ketepatan ukur untuk penetapan harga, dan kelayakan metrologi legal.

![Dashboard](assets/progress-dashboard.svg)

## Checklist berdasarkan bukti pekerjaan

Status: `[x]` selesai dibahas/disediakan; `[ ]` belum selesai. Semua pekerjaan yang berstatus selesai ditelusuri dari keputusan percakapan atau repository ini, bukan klaim pengujian.

### 1. Discovery & scope — 4/5

- [x] **SC-REQ-01** — Definisi POS general offline-first
- [x] **SC-REQ-02** — Fallback manual dan konfirmasi akhir kasir
- [x] **SC-REQ-03** — Batas hardware Rp10 juta; PC tersedia
- [x] **SC-REQ-04** — Alur panjang -> pilih produk/warna -> konfirmasi
- [ ] **SC-REQ-05** — [DEFERRED_ON_SITE] Validasi kebutuhan lewat contoh barang dan observasi toko

### 2. Repository & dokumentasi — 2/4

- [x] **SC-OPS-01** — Repository GitHub private tersedia
- [x] **SC-OPS-02** — Dashboard progres + roadmap di repository
- [ ] **SC-OPS-03** — Issue backlog terstruktur
- [ ] **SC-OPS-04** — Review dan persetujuan acceptance criteria

### 3. Fondasi database — 5/6

- [x] **SC-DAT-01** — Kamus data produk, kategori, varian, satuan
- [x] **SC-DAT-02** — ERD dan aturan relasi SKU/variant
- [x] **SC-DAT-03** — Template CSV dan validasi impor
- [ ] **SC-DAT-04** — [DEFERRED_ON_SITE] 30 SKU sampel yang diverifikasi
- [x] **SC-DAT-05** — Migrasi schema SQLite
- [x] **SC-DAT-06** — Tes integritas database dan stok

### 4. POS & inventaris — 2/6 (implementasi software; deploy toko belum diizinkan)

- [ ] **SC-POS-01** — CRUD master produk dan pencarian
- [x] **SC-POS-02** — Varian dan satuan pcs/meter (UI + Core + tes)
- [x] **SC-POS-03** — Keranjang dan transaksi dasar (UI + Core + tes)
- [ ] **SC-POS-04** — Pergerakan stok + peringatan ambang per SKU
- [ ] **SC-POS-05** — Checkout, pembayaran, struk dan riwayat (UI histori + receipt tersimpan; cetak/ekspor struk dan UAT belum selesai)
- [ ] **SC-POS-06** — Backup/restore dan penanganan gangguan (online backup + restore ke FILE BARU lulus CI; aktivasi, disaster recovery dan jadwal otomatis belum)

### 5. Simulator perangkat — 0/5

- [ ] **SC-DEV-01** — Kontrak adapter hardware
- [ ] **SC-DEV-02** — Simulasi barcode scanner
- [ ] **SC-DEV-03** — Simulasi encoder/display panjang
- [ ] **SC-DEV-04** — Draft pengukuran + pilih material/warna
- [ ] **SC-DEV-05** — Uji error/disconnect dan mode manual

### 6. Visi komputer & pilot — 0/6

- [ ] **SC-AI-01** — [DEFERRED_ON_SITE untuk kamera nyata; simulasi software tetap lanjut] Benchmark kamera dan kalibrasi
- [ ] **SC-AI-02** — Baseline pencarian kandidat visual
- [ ] **SC-AI-03** — Estimasi ukuran + penolakan kandidat ambigu
- [ ] **SC-AI-04** — [DEFERRED_ON_SITE] Integrasi perangkat fisik
- [ ] **SC-AI-05** — [DEFERRED_ON_SITE + LEGAL] Pemeriksaan persyaratan metrologi legal
- [ ] **SC-AI-06** — [DEFERRED_ON_SITE] Pilot, metrik akurasi dan waktu transaksi

## Aturan fase software-only

- Semua bukti cek alat, SKU asli, observasi toko, akurasi dan legal metrologi dicatat `DEFERRED_ON_SITE` dalam [execution policy](EXECUTION_POLICY_SOFTWARE_ONLY.md).
- Fase implementasi POS **diizinkan dimulai sekarang** memakai database dan data sintetis; bukan menunggu validasi fisik.
- Dasbor 13/32 tetap merefleksikan checklist yang *benar-benar* sudah selesai; tugas fisik yang ditunda tidak dianggap selesai.

## Bukti login Admin/Kasir v0.3 (software)

- [x] Migrasi [akun lokal v4](../database/migrations/004_local_auth.sql): Admin, Kasir, PBKDF2-SHA256 + salt, lockout login, audit autentikasi.
- [x] [Dialog WPF](../src/SmartCasier.Desktop/LoginDialog.xaml) login dan first-run Admin **tanpa default password**; [pembuatan Kasir](../src/SmartCasier.Desktop/CreateCashierDialog.xaml); [service otorisasi](../src/SmartCasier.Core/AuthService.cs).
- [x] Hak Admin wajib pada operasi edit SKU/harga/stok dan backup/restore, termasuk pemeriksaan dari service Core; tidak hanya tombol dinonaktifkan.
- [x] [14 tes keamanan](../tests/SmartCasier.Auth.SelfTests/Program.cs), total [53 tes per OS (Windows/Linux CI lulus)](https://github.com/Skyreaaa/smart-casier/actions/runs/44285643615).
- [x] Dependensi SQLite diperbarui ke `Microsoft.Data.Sqlite 10.0.13`; log CI tidak lagi memunculkan peringatan keamanan `NU1903`.
- [ ] Hak akses **seluruh API** transaksi/read-only, audit user per perubahan data, password reset/rotasi, penguncian OS/file database, uji klik GUI dan threat-model.
- [ ] Tidak ada klaim production-ready; lihat [batas keamanan login](LOCAL_AUTH_V0_3.md).

## Bukti software POS Core + Windows WPF GUI

- [x] [Core .NET](../src/SmartCasier.Core) dan [CLI demo](../src/SmartCasier.Cli); migrasi [sales/receipt](../database/migrations/003_pos_sales.sql).
- [x] Checkout lokal atomik, idempotency request, harga snapshot IDR, stok PCS/M, batas stok tidak negatif.
- [x] [14 tes integrasi](../tests/SmartCasier.Core.SelfTests/Program.cs) pada [CI Windows + Linux (PASS)](https://github.com/Skyreaaa/smart-casier/actions/runs/44276517575).
- [x] [GUI WPF](../src/SmartCasier.Desktop), [editor SKU](../src/SmartCasier.Core/ProductCatalogEditor.cs), [tes katalog](../tests/SmartCasier.Catalog.SelfTests/Program.cs), [CI Windows/Linux](https://github.com/Skyreaaa/smart-casier/actions/runs/44280002437).
- [x] Histori transaksi read-only dan lookup receipt; tes pencarian dan snapshot harga.
- [x] SQLite WAL backup aman serta restore terverifikasi ke file baru; [tes Windows/Linux](https://github.com/Skyreaaa/smart-casier/actions/runs/44282401781).
- [ ] CRUD metadata lengkap, aktivasi restore, backup terjadwal/enkripsi, laporan lengkap, refund, payment selain tunai, role/permissions, dan tes klik GUI.
- [ ] Hardware adapter dan verifikasi fisik tetap DEFERRED_ON_SITE sesuai policy.

**Checklist utama 13/32** tetap: peningkatan role tidak otomatis menyelesaikan seluruh CRUD, checkout cetak struk atau disaster recovery. Tes WPF interaktif/keamanan file dan seluruh bukti on-site masih terbuka.

## Gate dan definisi selesai

| Gate | Harus terbukti sebelum naik fase | Status |
| --- | --- | --- |
| G0 — Scope | Kebutuhan disetujui, data sampel toko mewakili jenis SKU & satuan | Belum lulus (observasi toko belum ada) |
| G1 — Database | Migrasi SQLite + test integritas + import/rollback valid | Baseline teknis lulus CI; belum lulus validasi 30 SKU nyata |
| G2 — POS | Transaksi offline, stok konsisten, backup/restore teruji | Belum dimulai |
| G3 — Alat ukur | Panjang aktual dibanding referensi pada variasi material; error handling | Belum dimulai |
| G4 — Commercial | Kewajiban metrologi legal dikonfirmasi; perangkat dinyatakan layak sesuai ketentuan | Belum diperiksa |
| G5 — AI | Evaluasi identifikasi SKU, rejection ambiguous, latency pada PC target | Belum dimulai |

## Bukti pekerjaan database

- [Migrasi SQLite](../database/migrations/001_core.sql), [importer CSV](../tools/import_catalog.py), [template kosong](../data/templates/catalog_products.csv), [contoh sintetis](../data/examples/catalog_demo.csv).
- [Tes regresi](../tests/test_database.py) dan [GitHub Actions berhasil](https://github.com/Skyreaaa/smart-casier/actions/runs/44265209926) pada commit 5c7edd6.
- Database produksi dan data toko nyata **belum dibuat**. Tes yang lulus tidak membuktikan kesiapan kasir komersial.

## Dataset staging non-makanan — tambahan yang sudah diimplementasikan

- [x] Filter kategori non-makanan dan guard tambahan untuk indikasi makanan yang salah kategori.
- [x] Prototipe staging SQLite, registry sumber, validasi GTIN, deduplikasi, dan larangan memasukkan data langsung ke POS.
- [x] Adapter lokal Open Products Facts (hanya non-food bertag Indonesia) dan spare part (hanya kolom master, tanpa data pelanggan/transaksi).
- [x] Adapter PRDECT-ID untuk mengambil nama non-makanan dari kategori relevan; ulasan, lokasi, harga, dan atribut pelanggan tidak disalin.
- [x] Data riil PRDECT-ID telah diproses **dalam staging pengujian terpisah**: 261 kandidat metadata, seluruhnya ditahan `HOLD_LICENSE` (bukan SKU terverifikasi).
- [x] Persetujuan record dan sumber dengan jejak audit, termasuk penahanan kembali ketika sumber ditolak.
- [x] **47 pengujian lulus:** [hasil CI](https://github.com/Skyreaaa/smart-casier/actions/runs/44273327558).
- [ ] Lisensi/privasi sumber eksternal ditinjau dan disetujui untuk ekstraksi data nyata.
- [x] Berkas sumber PRDECT-ID berhasil diakses dan dikonversi; status hak pakai/privasi dan identitas SKU toko masih menunggu peninjauan.
- [ ] Minimal 30 SKU toko nyata dan lisensi sumber eksternal dikonfirmasi untuk promosi data ke master.
- [ ] Setidaknya 30 SKU toko nyata diverifikasi.

Enam sumber eksternal (termasuk Open Products Facts dan PRDECT-ID e-commerce Indonesia) tercantum dalam [registry](../data/sources/nonfood_registry.json) dengan status `PENDING`. Satu fixture sintetis internal berstatus `APPROVED` **hanya untuk pengujian**, bukan izin penggunaan data nyata. **Progres global tetap 13/32**: staging merupakan pekerjaan pendukung, tidak menggantikan tugas validasi 30 SKU asli.

## Audit data nyata (bukan data master toko)

[Laporan audit faktual](REAL_NONFOOD_INTAKE_2026-11-18.md) mencatat:

- Open Products Facts: 49.771 baris total, 233 bertag Indonesia, **0** lolos pemetaan kategori toko secara eksplisit.
- PRDECT-ID: 5.400 baris ulasan; 1.000 kategori terpilih; **261** kandidat nama produk setelah penolakan dan deduplikasi; **261 HOLD_LICENSE**.
- Lisensi PRDECT-ID mengklaim CC0 dari metadata, tetapi file LICENSE kosong dan hak data ulasan aslinya belum diverifikasi; data tetap tidak dipromosikan.
- Barang yang diverifikasi secara fisik di toko: **0 SKU**.

## Prioritas pengumpulan data

1. Katalog produk dengan SKU internal yang stabil; barcode opsional.
2. Kategori dan varian (material, warna, dimensi bila relevan); hindari kolom universal yang penuh null.
3. Satuan stok dasar (`pcs`, `m`) dan satuan penjualan, konversi eksplisit jika diperlukan.
4. Harga per varian; jumlah stok dan ambang restock terpisah.
5. Riwayat perubahan stok yang dapat diaudit; stok tidak sekadar diubah tanpa catatan.
6. Foto referensi dengan label SKU yang diverifikasi, hanya bila siap memulai dataset AI.

## Asumsi dan yang belum diketahui

- Belum ada data Excel/katalog produk yang diunggah; skema lulus uji sintetis tetapi belum diuji pada SKU toko nyata.
- RAB awal perangkat **Rp8.404.000 + Rp1.596.000 cadangan** adalah angka *perencanaan* dari pembahasan, bukan quote vendor terkonfirmasi; desain casing premium mungkin mengubah biaya.
- Batas Rp10 juta berlaku untuk hardware tambahan, tidak termasuk PC yang sudah ada.
- Lebar, kekakuan, dan jenis material gulungan belum diketahui; alat universal perlu prototipe sebelum menyatakan toleransi.
- Definisi teknis toleransi akurasi dan kelayakan metrologi legal belum final.

## Catatan pembaruan progres

Setiap penyelesaian task harus menyertakan bukti: commit/PR, dokumen desain yang direview, dataset terverifikasi atau hasil pengujian yang bisa direproduksi. Jangan mencentang task hanya karena sudah masuk roadmap.
