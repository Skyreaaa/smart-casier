# Laporan Progres — Smart Casier

**Per 18 November 2026** · Tahap **Discovery → Persiapan Master Data**

## Ringkasan terverifikasi

- **6/32 checklist selesai (19%):** hasil keputusan/pendokumentasian, bukan pekerjaan software yang sudah lolos tes.
- **Aplikasi POS berfungsi: tidak ada/belum dibangun.** Tidak ada database terisi, firmware, integrasi kamera, atau pengujian hardware.
- **Repository:** [Skyreaaa/smart-casier](https://github.com/Skyreaaa/smart-casier), private, branch main.
- **Tugas aktif berikutnya:** model data produk dan struktur inventaris.
- **Risiko utama:** variasi barang tanpa barcode, ukuran benda yang mirip, satuan stok meter/pcs, ketepatan ukur untuk penetapan harga, dan kelayakan metrologi legal.

![Dashboard](assets/progress-dashboard.svg)

## Checklist berdasarkan bukti pekerjaan

Status: `[x]` selesai dibahas/disediakan; `[ ]` belum selesai. Semua pekerjaan yang berstatus selesai ditelusuri dari keputusan percakapan atau repository ini, bukan klaim pengujian.

### 1. Discovery & scope — 4/5

- [x] **SC-REQ-01** — Definisi POS general offline-first
- [x] **SC-REQ-02** — Fallback manual dan konfirmasi akhir kasir
- [x] **SC-REQ-03** — Batas hardware Rp10 juta; PC tersedia
- [x] **SC-REQ-04** — Alur panjang -> pilih produk/warna -> konfirmasi
- [ ] **SC-REQ-05** — Validasi kebutuhan lewat contoh barang dan observasi toko

### 2. Repository & dokumentasi — 2/4

- [x] **SC-OPS-01** — Repository GitHub private tersedia
- [x] **SC-OPS-02** — Dashboard progres + roadmap di repository
- [ ] **SC-OPS-03** — Issue backlog terstruktur
- [ ] **SC-OPS-04** — Review dan persetujuan acceptance criteria

### 3. Fondasi database — 0/6

- [ ] **SC-DAT-01** — Kamus data produk, kategori, varian, satuan
- [ ] **SC-DAT-02** — ERD dan aturan relasi SKU/variant
- [ ] **SC-DAT-03** — Template CSV dan validasi impor
- [ ] **SC-DAT-04** — 30 SKU sampel yang diverifikasi
- [ ] **SC-DAT-05** — Migrasi schema SQLite
- [ ] **SC-DAT-06** — Tes integritas database dan stok

### 4. POS & inventaris — 0/6

- [ ] **SC-POS-01** — CRUD master produk dan pencarian
- [ ] **SC-POS-02** — Varian dan satuan pcs/meter
- [ ] **SC-POS-03** — Keranjang dan transaksi dasar
- [ ] **SC-POS-04** — Pergerakan stok + peringatan ambang per SKU
- [ ] **SC-POS-05** — Checkout, pembayaran, struk dan riwayat
- [ ] **SC-POS-06** — Backup/restore dan penanganan gangguan

### 5. Simulator perangkat — 0/5

- [ ] **SC-DEV-01** — Kontrak adapter hardware
- [ ] **SC-DEV-02** — Simulasi barcode scanner
- [ ] **SC-DEV-03** — Simulasi encoder/display panjang
- [ ] **SC-DEV-04** — Draft pengukuran + pilih material/warna
- [ ] **SC-DEV-05** — Uji error/disconnect dan mode manual

### 6. Visi komputer & pilot — 0/6

- [ ] **SC-AI-01** — Benchmark kamera dan kalibrasi
- [ ] **SC-AI-02** — Baseline pencarian kandidat visual
- [ ] **SC-AI-03** — Estimasi ukuran + penolakan kandidat ambigu
- [ ] **SC-AI-04** — Integrasi perangkat fisik
- [ ] **SC-AI-05** — Pemeriksaan persyaratan metrologi legal
- [ ] **SC-AI-06** — Pilot, metrik akurasi dan waktu transaksi

## Gate dan definisi selesai

| Gate | Harus terbukti sebelum naik fase | Status |
| --- | --- | --- |
| G0 — Scope | Kebutuhan disetujui, data sampel toko mewakili jenis SKU & satuan | Belum lulus (observasi toko belum ada) |
| G1 — Database | Migrasi SQLite + test integritas + import/rollback valid | Belum dimulai |
| G2 — POS | Transaksi offline, stok konsisten, backup/restore teruji | Belum dimulai |
| G3 — Alat ukur | Panjang aktual dibanding referensi pada variasi material; error handling | Belum dimulai |
| G4 — Commercial | Kewajiban metrologi legal dikonfirmasi; perangkat dinyatakan layak sesuai ketentuan | Belum diperiksa |
| G5 — AI | Evaluasi identifikasi SKU, rejection ambiguous, latency pada PC target | Belum dimulai |

## Prioritas pengumpulan data

1. Katalog produk dengan SKU internal yang stabil; barcode opsional.
2. Kategori dan varian (material, warna, dimensi bila relevan); hindari kolom universal yang penuh null.
3. Satuan stok dasar (`pcs`, `m`) dan satuan penjualan, konversi eksplisit jika diperlukan.
4. Harga per varian; jumlah stok dan ambang restock terpisah.
5. Riwayat perubahan stok yang dapat diaudit; stok tidak sekadar diubah tanpa catatan.
6. Foto referensi dengan label SKU yang diverifikasi, hanya bila siap memulai dataset AI.

## Asumsi dan yang belum diketahui

- Belum ada data Excel/katalog produk yang diunggah; struktur data belum diuji pada SKU nyata.
- RAB awal perangkat **Rp8.404.000 + Rp1.596.000 cadangan** adalah angka *perencanaan* dari pembahasan, bukan quote vendor terkonfirmasi; desain casing premium mungkin mengubah biaya.
- Batas Rp10 juta berlaku untuk hardware tambahan, tidak termasuk PC yang sudah ada.
- Lebar, kekakuan, dan jenis material gulungan belum diketahui; alat universal perlu prototipe sebelum menyatakan toleransi.
- Definisi teknis toleransi akurasi dan kelayakan metrologi legal belum final.

## Catatan pembaruan progres

Setiap penyelesaian task harus menyertakan bukti: commit/PR, dokumen desain yang direview, dataset terverifikasi atau hasil pengujian yang bisa direproduksi. Jangan mencentang task hanya karena sudah masuk roadmap.
