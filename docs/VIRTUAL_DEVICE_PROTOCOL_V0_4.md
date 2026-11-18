# Smart Casier — Operator dan Simulator Perangkat v0.4

**Mode: SOFTWARE_ONLY.** Ini bukan pengukur bersertifikat, tidak menggerakkan cutter dan tidak membaca scanner/encoder fisik. [Build dan 71 tes per OS lulus](https://github.com/Skyreaaa/smart-casier/actions/runs/44291496358).

## Cara mencoba di Windows

1. Buka [GitHub Actions](https://github.com/Skyreaaa/smart-casier/actions/runs/44291496358) dan unduh artifact **smart-casier-windows-demo**. Ekstrak dan jalankan aplikasi dengan opsi `--demo` setelah memasang .NET 10 Desktop Runtime x64.
2. Masuk dengan akun Admin/Kasir lokal. Database demo, harga dan stok adalah **sintetis**, bukan data toko.
3. Tekan **Simulator Perangkat** pada jendela WPF.
4. **Scanner virtual:** isi `BAUT-M8X30`, tekan **Scan Virtual**, pilih event PENDING, cocokkan SKU, lalu **ACK & Tambahkan ke Keranjang**.
5. **Encoder virtual:** isi `0.25` meter lalu **Tambah Umpan** berulang. Tekan **Catat Potong Virtual**, pilih event, isi SKU roll meteran (contoh sintetis `KMY-MERAH`) lalu ACK.
6. Periksa item, kuantitas dan harga di keranjang sebelum checkout. **Event dan ACK tidak otomatis membuat transaksi atau mengurangi stok.**

## Arsitektur dan keamanan

- [DeviceSimulator.cs](../src/SmartCasier.Core/DeviceSimulator.cs) menyediakan `IBarcodeReader`, `ILengthMeter`, event dengan UUID unik, virtual scanner/encoder, dan `DeviceInbox` untuk menampung input.
- [Skema SQLite v6](../database/migrations/006_device_inbox.sql) menyimpan event `PENDING`, `ACKED`, `REJECTED`. Retry ID/payload identik bersifat idempoten; ID sama dengan isi berbeda ditolak. Event PENDING tetap ada setelah aplikasi dimulai ulang.
- ACK memerlukan sesi operator valid. Barcode hanya dapat dikaitkan dengan SKU PCS yang benar, sementara panjang encoder hanya ke SKU bersatuan meter. Tidak ada pengenalan warna/ukuran fisik otomatis.
- [Migrasi v5](../database/migrations/005_sales_operator.sql) mencatat `operator_id` untuk transaksi baru. Riwayat lama tidak diisi identitas operator secara mengada-ada.
- Checkout, pembacaan receipt dan pencarian histori di Core memerlukan sesi aktif. Checkout tanpa autentikasi lewat CLI dinonaktifkan.

## Verifikasi

| Suite | Tes lulus per platform |
| --- | ---: |
| POS Core | 14 |
| Katalog/editor | 11 |
| Riwayat dan backup | 14 |
| Autentikasi/otorisasi | 18 |
| Simulator scanner/encoder | 14 |
| **Total** | **71** |

[Bukti CI Windows dan Linux](https://github.com/Skyreaaa/smart-casier/actions/runs/44291496358) juga mengonfirmasi build WPF dan artifact Windows. **Belum ada tes klik UI otomatis maupun pengujian perangkat fisik.**

## Batasan dan backlog

- **SC-DEV-04:** input SKU varian meter sudah tersedia tetapi pemilihan material/warna berbasis daftar dan perbaikan setelah ACK masih belum lengkap.
- **SC-DEV-05:** simulator belum mencakup disconnect USB, timeout, recovery transport, atau UI retry UUID manual. Deduplikasi retry sudah diuji di Core.
- Panjang virtual tidak membuktikan kalibrasi roller, slip kertas, akurasi panjang atau keberhasilan potong. Tidak ada aktuator fisik.
- ACK mengusulkan item untuk keranjang, bukan menjamin item masuk atau stok tersedia sampai checkout. Verifikasi keranjang tetap diperlukan.
- Hardening file SQLite dan backup, kontrol akses OS, serta GUI keyboard/screen-reader perlu pengujian lanjutan. Perangkat dan metrologi tetap [DEFERRED_ON_SITE](EXECUTION_POLICY_SOFTWARE_ONLY.md).

**Status: 16/32 checklist (50%)** merupakan progres software dan dokumen, bukan kesiapan produksi.
