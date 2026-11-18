# Smart Casier

> **Status: Database Foundation + Non-food Staging v0.3** — skema SQLite, importer CSV, filter non-makanan, **adapter katalog Open Products Facts dan master spare part**, serta pengujian CI tersedia. **261 kandidat metadata non-makanan dari dataset asli sudah dikurasi dan ditahan untuk review hak pakai; 0 SKU toko diverifikasi dan 0 produk masuk POS**.

![Dashboard progres Smart Casier](docs/assets/progress-dashboard.svg)

![Status](https://img.shields.io/badge/status-nonfood%20staging-2563eb) ![Checklist](https://img.shields.io/badge/checklist-11%2F32-0f766e) ![POS](https://img.shields.io/badge/POS-belum%20dibangun-64748b) ![Mode](https://img.shields.io/badge/mode-offline--first-475569)

**Update terakhir:** 18 November 2026 · **Repository:** private · **Branch:** `main`

**Hasil kurasi data asli:** [Audit dan hasil 18 November](docs/REAL_NONFOOD_INTAKE_2026-11-18.md) · [Laporan JSON](data/reports/real_nonfood_intake_2026-11-18.json) · [GitHub CI (47 lulus)](https://github.com/Skyreaaa/smart-casier/actions/runs/44273327558)

**Sumber non-makanan:** [Audit lisensi dan relevansi](docs/SOURCE_AUDIT_2026-11-18.md) · [Panduan dua adapter](docs/NONFOOD_INTAKE.md)

**Dataset non-makanan:** [Pipeline staging](docs/DATASET_STAGING.md) · [Sumber kandidat](data/sources/nonfood_registry.json) · [Contoh sintetis](data/examples/nonfood_staging_demo.csv) · [Staging schema](database/migrations/002_dataset_staging.sql)

**Database:** [Panduan pendataan](docs/DATABASE_FOUNDATION.md) · [Template CSV kosong](data/templates/catalog_products.csv) · [Migrasi SQLite](database/migrations/001_core.sql) · [Hasil CI](https://github.com/Skyreaaa/smart-casier/actions/runs/44265209926)

**Baca:** [Laporan progres lengkap](docs/PROGRESS.md) · [Roadmap dan milestone](docs/ROADMAP.md) · [Riwayat perubahan](docs/CHANGELOG.md)

## Status per fase

| Area | Selesai | Progres checklist | Kondisi nyata |
| --- | ---: | --- | --- |
| Discovery & scope | 4/5 | ▰▰▰▰▰▰▰▰▱▱ 80% | Dalam finalisasi |
| Repository & dokumentasi | 2/4 | ▰▰▰▰▰▱▱▱▱▱ 50% | Aktif |
| Fondasi database | 5/6 | ▰▰▰▰▰▰▰▰▱▱ 83% | Skema + importer + CI siap; butuh 30 SKU nyata |
| POS & inventaris | 0/6 | ▱▱▱▱▱▱▱▱▱▱ 0% | Belum dimulai |
| Simulator perangkat | 0/5 | ▱▱▱▱▱▱▱▱▱▱ 0% | Belum dimulai |
| Visi komputer & pilot | 0/6 | ▱▱▱▱▱▱▱▱▱▱ 0% | Fase lanjutan |

**Interpretasi:** 11 dari 32 checklist selesai (34% checklist, bukan kesiapan produksi). **Database dan pipeline staging non-makanan tersedia.** Dataset real Indonesia dalam master POS: **0 SKU terverifikasi**. Belum ada inventaris nyata, antarmuka POS, kamera AI, atau alat ukur yang bekerja.

## Alur pekerjaan

```mermaid
flowchart LR
  A[Definisi kebutuhan] --> B[Master data & satuan]
  B --> C[Skema SQLite & validasi]
  C --> D[POS & inventaris]
  D --> E[Simulator perangkat]
  E --> F[Integrasi hardware]
  D --> G[Dataset & uji kamera AI]
  F --> H[Pilot + metrologi]
  G --> H
  classDef done fill:#d1fae5,stroke:#0f766e,color:#064e3b;
  classDef next fill:#dbeafe,stroke:#2563eb,color:#1e3a8a;
  class A done;
  class B next;
```

## Yang sudah diputuskan

- POS Windows **offline-first**, PC kasir sudah ada.
- Barcode + pencarian manual; kamera AI direncanakan untuk kandidat barang tanpa barcode, bukan penentu SKU tanpa verifikasi.
- Alat ukur material lembaran: target via keypad, panjang dikirim melalui USB, **kasir memilih material/warna dan mengonfirmasi**.
- Inventaris dengan stok per SKU dan satuan; laporan **stok habis** dan **stok menipis** (ambang per SKU, default 3 untuk satuan pcs).
- **Batas perangkat tambahan Rp10 juta**; estimasi RAB awal belum merupakan penawaran harga dan dapat direvisi setelah prototipe.

## Non-food dataset intake (fase paralel)

| Indikator | Kondisi |
| --- | --- |
| Scope | Spare part, baut, perkakas, pipa, material lembaran, listrik, pulsa |
| Makanan/minuman | **Tidak termasuk**; record out-of-scope dikarantina |
| Sumber eksternal kandidat | 6; seluruhnya **PENDING** persetujuan penggunaan |
| Data eksternal yang masuk master POS | **0 SKU** |
| Kandidat hasil kurasi sumber nyata | **261 nama produk**; seluruhnya **HOLD_LICENSE** pada staging pengujian terpisah |
| Pipeline SQLite | Staging + 3 adapter; converter sumber PRDECT telah dijalankan pada data nyata |
| Quality gates | Kategori non-food, nama duplikat, barcode bila tersedia, sumber/lisensi, reviewer, audit |

Pipeline dan kebijakan impor ada di [dokumen staging](docs/DATASET_STAGING.md). [Hasil ekstraksi data asli](docs/REAL_NONFOOD_INTAKE_2026-11-18.md) mencantumkan 261 kandidat yang **belum disetujui**. Dataset sintetis untuk CI bukan inventaris toko.

## Tugas langsung berikutnya

1. Inventarisasi **minimal 30 SKU toko nyata** dari kategori berbeda menggunakan [template CSV](data/templates/catalog_products.csv).
2. Periksa SKU/varian, satuan PCS atau M, stok, harga, dan barcode opsional bersama pengelola toko.
3. Validasi hasil dengan importer dry-run, lalu audit kecocokannya dengan skema.
4. Setelah data nyata tervalidasi, mulai CRUD produk dan fitur POS core. **Demo enam SKU bukan inventaris toko**.

> Dashboard dibuat sebagai laporan *manual snapshot*. Perbarui centang di `docs/PROGRESS.md` dan sinkronkan angka/diagram di README serta SVG saat pekerjaan benar-benar selesai; tidak ada klaim otomatisasi progres dari GitHub Actions.
