# Database Foundation (Smart Casier)

Tahap ini menyiapkan basis data **SQLite offline** untuk katalog general store. Semua nilai di data/examples adalah **simulasi**, bukan inventaris atau harga toko nyata.

## Struktur data

~~~mermaid
erDiagram
    CATEGORIES ||--o{ PRODUCTS : groups
    PRODUCTS ||--o{ VARIANTS : contains
    UNITS ||--o{ VARIANTS : measures
    VARIANTS ||--o{ BARCODES : optional
    VARIANTS ||--o{ VARIANT_ALIASES : searchable
    VARIANTS ||--o{ STOCK_MOVEMENTS : ledger
~~~

- products: keluarga produk (mis. Kertas Minyak).
- variants: SKU tiap warna/ukuran, harga IDR, unit dan ambang restock.
- barcodes: barcode opsional; pencarian manual tetap berfungsi.
- variant_aliases: sinonim nama.
- stock_movements: ledger stok append-only; stok tidak bisa negatif.
- v_inventory_status: UNTRACKED, OUT, LOW, OK.

**Aturan angka:** harga integer IDR; jumlah sebagai integer milli-unit (1 PCS=1000; 2.5 M=2500). Untuk PCS hanya kelipatan 1000. Status LOW adalah stok positif **< ambang** (tepat 3 pcs dengan ambang 3 bukan LOW). Barang meteran bisa memakai ambang per-SKU. Produk digital bisa tanpa stok.

## Template input

- data/templates/catalog_products.csv: template kosong, siap pendataan.
- data/examples/catalog_demo.csv: 6 SKU **SINTETIS**, untuk mencoba validator.
- CSV harus memiliki seluruh kolom sesuai urutan header, termasuk SKU unik.
- Atribut khusus kategori disimpan sebagai JSON object; alias pisahkan dengan karakter |.
- Kategori akan dibuat ketika impor bila belum ada; jangan ubah nama untuk category_code yang sama.
- Barcode boleh kosong; harga dan stok harus berdasarkan pengamatan nyata, jangan ditebak.

## Menjalankan (Python 3.11+)

~~~bash
python -c "import sqlite3,pathlib; c=sqlite3.connect('demo.db'); c.executescript(pathlib.Path('database/migrations/001_core.sql').read_text()); c.close()"
python -m tools.import_catalog data/examples/catalog_demo.csv
python -m tools.import_catalog data/examples/catalog_demo.csv --apply --db demo.db
python -m unittest discover -s tests -v
~~~

Default importer hanya memvalidasi; --apply melakukan impor atomik, seluruh batch rollback saat error. Import ulang SKU yang sama ditolak agar stok pembukaan tidak dihitung ganda. Jangan gunakan demo.db sebagai data toko nyata.

## Pengumpulan 30 SKU nyata (belum dilakukan)

1. Kelompokkan produk di rak dan definisikan satuan jual/stock (PCS/M/TXN).
2. Catat ukuran, warna, merek, dan nomor part sesuai label/fakta; jangan mengarang detail.
3. Hitung stok fisik dan harga jual; bila belum diketahui, **biarkan di luar file impor**, jangan isi nol semu.
4. Periksa nama alternatif, SKU yang setara, barcode yang tersedia, dan kelompok varian.
5. Catat contoh minimal 30 SKU lintas kategori; minta verifikasi orang toko.
6. Foto untuk ML dikumpulkan belakangan dengan label SKU yang sudah benar.

Belum termasuk POS checkout, perhitungan pajak, pembayaran, supplier, sistem foto AI, atau firmware/hardware meteran. Sistem alat ukur baru menambahkan draft panjang untuk dikonfirmasi kasir pada fase berikutnya.
