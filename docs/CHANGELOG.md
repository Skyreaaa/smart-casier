# Catatan Progres

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
