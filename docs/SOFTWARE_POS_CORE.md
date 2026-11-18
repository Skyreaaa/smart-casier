# Smart Casier — Software POS Core v0.1

**Mode:** SOFTWARE_ONLY, tanpa alat fisik dan tanpa data toko riil.  
**Platform:** .NET 10 (C#) + SQLite (offline), CLI lintas platform; antarmuka Windows grafis belum dibuat.  
**Hasil CI:** [Windows dan Linux PASS](https://github.com/Skyreaaa/smart-casier/actions/runs/44276517575).

## Sudah tersedia

- `src/SmartCasier.Core`: database bootstrap + katalog + checkout atomik + receipt.
- `src/SmartCasier.Cli`: CLI lokal untuk inisialisasi, seed demo, pencarian, laporan stok dan pembayaran kas.
- `database/migrations/003_pos_sales.sql`: tabel `sales`, `sale_lines`, snapshot harga saat transaksi.
- `tests/SmartCasier.Core.SelfTests`: **13 skenario** stok, nominal, meteran, rollback, idempotensi, restart, pencarian dan validasi.
- `.github/workflows/pos-core.yml`: build dan tests otomatis pada Windows dan Linux.

Semua data contoh diberi label **SYNTHETIC DEMO**. Tidak ada SKU fisik, harga jual toko, atau data produk publik yang telah diotorisasi dipakai sebagai data produksi.

## Menjalankan demo offline

Syarat: **.NET SDK 10** pada Windows (belum dibutuhkan barcode/camera/encoder).

Dari folder root repository:

```powershell
dotnet run --project src/SmartCasier.Cli -- init --db data/private/demo-pos.db
dotnet run --project src/SmartCasier.Cli -- seed-demo --db data/private/demo-pos.db --confirm-synthetic
dotnet run --project src/SmartCasier.Cli -- search --db data/private/demo-pos.db --query "baut"
dotnet run --project src/SmartCasier.Cli -- stock --db data/private/demo-pos.db
dotnet run --project src/SmartCasier.Cli -- checkout --db data/private/demo-pos.db --sku BAUT-M8X30 --qty 1 --paid 5000
```

Untuk memeriksa idempotensi, jalankan lagi command checkout dengan **`--request-id`** yang sama seperti output pertama (dan sku/qty/paid sama). Harus kembali `ALREADY_PROCESSED=True` tanpa mutasi stok kedua. Jangan menggunakan DB demo untuk toko nyata.

Baris meteran memakai `--qty 2.5` (2,5 meter) dan SKU `KMY-MERAH`. Harga di mata uang rupiah integer; panjang/stok disimpan milli-unit (1 meter = 1000, 1 pcs = 1000). Stok habis 0; menipis bila positif dan lebih kecil dari ambang per SKU (default 3 pcs).

## Arsitektur dan data

```mermaid
flowchart TD
  CLI[CLI demo, software-only] --> CORE[SmartCasier.Core]
  CORE --> SQL[SQLite lokal - operational DB]
  SQL --> CATALOG[Katalog/variant/barcode/alias]
  SQL --> LEDGER[Stock ledger - append-only]
  SQL --> SALES[Sales + receipt snapshots]
  SIM[Hardware simulator - backlog] -.-> CORE
  WPF[Windows GUI - backlog] -.-> CORE
  ON_SITE[DEFERRED_ON_SITE] -.-> WPF
```

**Penting:** `001_core.sql` dan `003_pos_sales.sql` adalah migrasi **database operasional**. `002_dataset_staging.sql` adalah migrasi untuk **file SQLite staging berbeda**, bukan langkah yang dijalankan di DB POS.

Penjualan ber-ID request unik, dilakukan dalam satu transaksi SQLite. Replay request dengan payload sama mengembalikan receipt sebelumnya; request ID yang sama dengan payload berbeda ditolak. Pembayaran kurang, stok tidak mencukupi, kuantitas `PCS` pecahan, atau SKU invalid menggagalkan seluruh checkout tanpa perubahan stok.

## Belum diimplementasikan (jangan disamakan dengan selesai)

- GUI WPF untuk kasir; input produk dan pengeditan inventaris (CRUD); laporan penjualan yang lengkap.
- Pembayaran selain uang tunai, pajak/diskon, retur/pembatalan, role admin/kasir, pengaturan toko, printer struk, backup/restore otomatis.
- Produk DIGITAL/SERVICE seperti pulsa belum bisa checkout; butuh lifecycle fulfillment online dan error/retry yang jelas.
- Seluruh hardware adapter/perangkat fisik, kamera AI, kalibrasi dan metrologi.
- Uji operasional toko, verifikasi SKU nyata, penilaian keamanan produk.

**Next software sprint:** GUI WPF berbasis Core + CRUD master produk + inventaris, lalu backup/restore dan device simulator. Lihat [kebijakan software-only](EXECUTION_POLICY_SOFTWARE_ONLY.md).
