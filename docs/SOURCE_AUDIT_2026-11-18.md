# Audit Sumber Dataset Non-Makanan — 18 November 2026

**Status:** dokumentasi asal dan hak pakai, belum ada produk luar yang disetujui ke master POS.

| Sumber | Isi yang relevan | Lisensi/risiko | Keputusan sementara |
| --- | --- | --- | --- |
| [Mendeley: Workshop Transaction Records for Motorcycle Spare-Part Recommendation](https://data.mendeley.com/datasets/bwyxwynbg5/1) | Kode/uraian spare part dan kategori masterparts | Landing page menyatakan **CC BY 4.0**, tetapi sumber mencampur metadata pelanggan (kota, pekerjaan) dan transaksi, sehingga tidak boleh mengimpor dataset mentah | **PENDING** — hanya ekstrak tabel master part tanpa PII; validasi nama/ukuran |
| [Zenodo e-Katalog V6](https://zenodo.org/records/19604457) | ~595 ribu produk/kategori procurement campuran | Rights pada halaman menunjukkan Copyright Telkom University 2026 tanpa lisensi penggunaan ulang yang eksplisit; file produk ~194 MB; bukan inventaris toko | **PENDING** — tidak boleh otomatis ditambahkan |
| [Open Products Facts](https://world.openproductsfacts.org/) | Katalog global *general non-food*; sebagian dapat bertag Indonesia | Dataset ODbL 1.0; hak pakai termasuk kewajiban atribusi dan dalam kondisi tertentu share-alike untuk database turunan. Tag negara dan kategori tidak membuktikan barang dijual di toko | **PENDING** — adapter lokal tersedia, metadata tetap ditahan sampai penilaian izin dan relevansi |
| [1000 barcode Indonesia (GitHub)](https://github.com/wpangestu/list-barcode-product-indonesia) | Barcode dan nama produk campuran | Lisensi untuk reuse belum terbukti; makanan perlu dipisahkan; validitas barcode mesti dicek | **PENDING** — tidak diunduh otomatis |
| [API Product Indonesia (GitHub)](https://github.com/ariph007/api-product-indonesia) | API katalog berisi contoh barang Indonesia | Hak reuse dataset belum terverifikasi; README menyebut harga contoh acak | **PENDING** — tidak mengimpor harga/stock dari API |

## Dasar bukti

1. Mendeley Data: halaman metadata menunjukkan tanggal **30 Maret 2026**, lisensi **CC BY 4.0**, field work-order termasuk atribut pelanggan, dan referensi `masterparts.xlsx`.
2. Zenodo: deskripsi menyebut **595.497 produk**, dan bagian Rights menulis **Copyright Telkom Universiy 2026** tanpa lisensi reusable yang terlihat.
3. [Open Products Facts — katalog ODbL](https://www.data.gouv.fr/datasets/open-products-facts) mencantumkan lisensi database ODbL.
4. Untuk setiap sumber, tahap yang diperlukan: unduh secara sah → inspeksi lisensi & data → buang PII → ekstrak kolom master barang → audit duplikasi/validasi → reviewer setujui jika layak → cocokkan SKU aktual toko.

## Temuan teknis penting

- Tidak ada data aktual yang telah diunduh ke repository pada tahap ini.
- Dataset **transaksi bengkel** tidak boleh langsung dipakai sebagai master SKU. Identitas pelanggan dan riwayat transaksi di luar tujuan POS ini.
- Dataset **e-Katalog** berisi barang procurement, bukan katalog barcode retail; nilai/harga dari sana tidak sama dengan harga jual toko.
- **Open Products Facts** dapat menjadi kandidat terstruktur non-food: adapter `tools/prepare_opf.py` menyaring tag negara `en:indonesia`, kategori eksplisit non-food dan GTIN valid, lalu membuang kolom tambahan termasuk data pribadi/harga. Produk yang tak bertag jelas **dilewati**, bukan dipaksa cocok.
- Keluaran adapter hanya *candidate staging*, unit PCS sementara dan belum ada SKU/stock/harga toko.

## Status hasil kurasi

- Sumber eksternal yang disetujui: **0**.
- SKU barang nyata toko yang diverifikasi: **0**.
- Sample data sintetis: **bukan** angka katalog retail.
- Tidak ada klaim bahwa produk pada sumber tersedia fisik atau dijual di Indonesia hanya karena tag negara.

## Kriteria untuk mengubah sumber menjadi APPROVED

1. Identifikasi pemilik dan pemegang hak database.
2. Hak penggunaan komersial, atribusi, dan kewajiban database turunan sudah dinilai.
3. Bila data berasal dari transaksi, kolom identitas pelanggan dan observasi individual dibuang sebelum staging.
4. Alur penyaringan terbukti pada contoh file yang sah dan berlabel sumber.
5. Review manual tiap record tetap diperlukan; SKU, stok, harga dan satuan diverifikasi di toko.

Hasil audit ini bukan pendapat hukum formal; bila ada ketidakpastian reuse komersial, minta izin tertulis atau gunakan data yang dikumpulkan sendiri.
