# Catatan Progres

## 2026-11-18 — v0.4: Sesi operator dan scanner/encoder virtual

- Migrasi SQLite v5 mencatat operator pada penjualan baru, dan v6 menyimpan event scanner/panjang terpisah.
- Checkout, pembacaan struk serta histori memerlukan sesi operator aktif pada Core. Checkout CLI dinonaktifkan.
- Scanner dan encoder virtual memiliki event ID unik, retry idempoten dan ACK/REJECT manual; ACK mengusulkan item keranjang, bukan penjualan.
- [71 pengujian per OS dan build WPF lulus](https://github.com/Skyreaaa/smart-casier/actions/runs/44291496358); paket Windows diterbitkan sebagai artifact.
- Progres naik menjadi **16/32 (50%)** karena SC-DEV-01/02/03, hanya kriteria software; hardware fisik tetap ditunda.
- Belum ada selector varian lengkap, simulasi disconnect/timeout, tes interaktif UI maupun sertifikasi ukuran.


## 2026-11-18 — Windows WPF v0.3: login lokal Admin/Kasir

- Skema POS **v4** menambah akun Admin/Kasir dan audit autentikasi; data stok/penjualan sebelumnya tidak dihapus.
- Setup Admin pertama wajib memakai password sendiri; tidak ada password/default user yang ditanamkan.
- Login WPF, pembuatan Kasir oleh Admin dan ganti operator. Perubahan SKU/harga/stok serta backup/restore membutuhkan sesi Admin dan diverifikasi oleh Core.
- Hash password PBKDF2 SHA-256 dengan salt acak, lima percobaan gagal mengunci akun 15 menit, sesi memori 8 jam.
- [53 tes per platform lulus Windows/Linux](https://github.com/Skyreaaa/smart-casier/actions/runs/44285643615); patch Microsoft.Data.Sqlite `10.0.13` menghilangkan `NU1903`.
- Program CLI checkout dibatasi ke nama database demo, **bukan** mekanisme keamanan kuat. Akses langsung DB/seluruh service Core, audit user per mutasi, reset akun dan UAT GUI masih belum selesai.
- Progres utama tetap **13/32**; tugas fisik masih `DEFERRED_ON_SITE`.


## 2026-11-18 — WPF v0.2: histori transaksi dan keamanan SQLite

- [Riwayat transaksi](../src/SmartCasier.Core/SalesHistoryService.cs) read-only dengan pencarian receipt/SKU/nama, pagination, rincian harga snapshot pada UI.
- [DatabaseSafetyService](../src/SmartCasier.Core/DatabaseSafetyService.cs) memakai `SqliteConnection.BackupDatabase` agar WAL konsisten; memeriksa quick_check, foreign_key_check dan skema POS v3/v4.
- Restore **hanya** ke file DB baru, tidak menimpa/menukar DB aktif. Tujuan diklaim dengan `FileMode.CreateNew` agar file pengguna tidak terhapus.
- [Tes sejarah dan backup/recovery](../tests/SmartCasier.Safety.SelfTests/Program.cs) ditambahkan ke CI Windows + Linux.
- [CI berhasil](https://github.com/Skyreaaa/smart-casier/actions/runs/44282401781), [panduan](SALES_HISTORY_AND_BACKUP.md); tetap `13/32` checklist karena aktivasi restore, backup terjadwal dan receipt cetak belum selesai.
- Belum ada uji visual GUI, hak akses admin, enkripsi backup, penggunaan hardware atau toko.


## 2026-11-18 — Windows WPF GUI + katalog editor (CI PASS)

- Antarmuka WPF untuk pencarian SKU/alias/barcode, keranjang, pembayaran tunai, dan notifikasi; F2 fokus cari, F4 checkout.
- Dialog menambah SKU/varian dan koreksi stok; pengelolaan harga melindungi histori penjualan, dan checkout menolak total keranjang yang sudah berubah.
- Database per pengguna dipisahkan dari database demo sintetis; aplikasi tidak membutuhkan kamera/encoder fisik.
- [Build Windows WPF + tes core/katalog lulus; artifact demo diterbitkan](https://github.com/Skyreaaa/smart-casier/actions/runs/44280002437).
- [Panduan Windows](WINDOWS_DESKTOP_V0_1.md). **Checklist utama naik ke 13/32 (41%)** tanpa menghitung bukti on-site.
- GUI belum diuji langsung dengan mouse, keyboard, zoom, screen reader atau alur pengguna di PC toko.


## 2026-11-18 — Software POS Core v0.1 (CI Windows/Linux PASS)

- .NET 10/C# + SQLite operasional, migrasi baru 003 untuk sales/receipt snapshots (002 tetap file DB staging terpisah).
- Katalog/read SKU, alias dan nama; inventaris dari ledger per SKU; checkout uang tunai atomik, idempotent request, kuantitas pcs/meter, harga integer IDR.
- CLI untuk simulasi offline (tanpa hardware) dan 13 pengujian integrasi [Windows/Linux sukses](https://github.com/Skyreaaa/smart-casier/actions/runs/44276517575).
- Belum ada GUI, hardware fisik, data toko nyata, atau fulfillment pulsa. Checklist proyek global tetap 11/32.


## 2026-11-18 — Pivot software-only

- Kunjungan toko, 30 SKU asli, pengadaan/kalibrasi hardware, kamera fisik, uji potong, pilot, dan metrologi ditandai `DEFERRED_ON_SITE` pada [kebijakan implementasi](EXECUTION_POLICY_SOFTWARE_ONLY.md).
- Pekerjaan software/CI/simulasi **lanjut tanpa menunggu validasi fisik**; persyaratan lapangan tetap wajib sebelum operasional komersial.
- Status tugas fisik tidak dihitung sebagai selesai.


## 2026-11-18 — Intake sumber nyata + kurasi metadata (47 CI PASS)

- Berhasil membaca ekspor Open Products Facts: 49.771 record, hanya 233 bertag Indonesia, **0** cocok dengan kategori non-makanan toko yang dipetakan konservatif.
- Berhasil membaca CSV PRDECT-ID: 5.400 record, 1.000 pada 5 kategori non-food, 261 kandidat nama unik setelah filter dan deduplikasi.
- Staging nyata terpisah: **261 HOLD_LICENSE**, **0 disetujui**, **0 promosi ke master POS**; tidak ada data ulasan, harga, lokasi, atau informasi pelanggan di-commit.
- False positive untuk oven listrik berwarna "Putih Susu" diperbaiki dengan regresi khusus.
- [47 tes CI lulus](https://github.com/Skyreaaa/smart-casier/actions/runs/44273327558). Bukti metodologi dan checksum: [laporan intake](REAL_NONFOOD_INTAKE_2026-11-18.md).


## 2026-11-18 — Audit dataset dan adapter nyata (belum ada impor riil)

- Audit 5 sumber non-makanan: Mendeley bengkel (CC BY + PII), Open Products Facts (ODbL), Zenodo e-Katalog (rights tidak jelas), dua sumber GitHub (lisensi belum jelas).
- Adapter Open Products Facts untuk CSV/TSV atau .gz lokal; hanya tag Indonesia eksplisit dan kelas non-food; menolak barcode invalid.
- Adapter spare part mengekstrak hanya kolom master barang; membuang seluruh informasi pelanggan, transaksi, dan harga sumber.
- CI otomatis diperluas; kedua adapter tidak mengisi stok/harga master POS.
- Sumber disetujui untuk data nyata **0**, SKU asli **0**, progres utama tetap **11/32**.


## 2026-11-18 — Staging dataset non-makanan

- Dataset makanan/minuman dikeluarkan dari scope Smart Casier.
- Disediakan registry kandidat non-food Indonesia, template ingest dan SQLite staging terpisah.
- Filter kategori, validasi GTIN, deteksi duplikasi, status persetujuan manual dan audit sumber ditambahkan.
- Sumber eksternal tidak diambil otomatis; seluruh 4 kandidat masih perlu pemeriksaan hak pakai.
- Data SKU toko yang telah diverifikasi tetap 0. Tidak ada data toko yang diubah.


## 2026-11-18 — Database Foundation v0.1 (CI PASS)

- Migrasi SQLite pertama: produk, varian SKU, barcode, alias, unit dan ledger stok append-only.
- Template CSV + validasi dan importer atomik, serta enam SKU contoh berlabel **sintetis**.
- 11 tes database lulus pada [workflow GitHub Actions](https://github.com/Skyreaaa/smart-casier/actions/runs/44265209926).
- Dashboard progres disesuaikan: **11/32 checklist**, dengan 30 SKU toko nyata masih pending.


## 2026-11-18 — Dasar proyek dan dashboard visual

- Repository GitHub pribadi dibuat dengan visibilitas **private**.
- README diganti menjadi project dashboard dengan SVG, tabel progres, dan alur Mermaid.
- Laporan progres berkode task ditambahkan; status implementasi tetap **belum dimulai**.
- Roadmap dan aturan inventaris/alat ukur didokumentasikan sebagai rencana, bukan hasil uji.

### Belum dikerjakan

- Migrasi dan pengisian database, aplikasi POS, firmware hardware, integrasi barcode/AI, dan pilot toko.
