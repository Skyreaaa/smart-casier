# Smart Casier — Riwayat Transaksi & Backup/Restore SQLite (Software Sprint v0.2; arsip)

> **Pembaruan:** database operasional kini versi **v6**, dan backup validasi menerima skema historis yang didukung. [Bukti CI terbaru](https://github.com/Skyreaaa/smart-casier/actions/runs/44291496358) dan [panduan virtual device](VIRTUAL_DEVICE_PROTOCOL_V0_4.md).

**Mode:** SOFTWARE_ONLY · WPF Windows, .NET 10, SQLite offline.  
**Bukti CI:** [Windows + Linux, build/tes berhasil](https://github.com/Skyreaaa/smart-casier/actions/runs/44282401781). **Tidak ada klaim pengujian klik/fokus GUI langsung.**

## Menggunakan fitur WPF

Jalankan aplikasi Windows dari [panduan desktop](WINDOWS_DESKTOP_V0_1.md) dengan `--demo` untuk menggunakan produk sintetis; tanpa `--demo` database normal tidak diisi barang contoh.

- **Riwayat Transaksi:** tombol di header WPF membuka daftar struk yang tersimpan. Cari berdasarkan nomor struk, nama, atau SKU; tekan Enter. Daftar memuat maksimum 100 struk per halaman dan mendukung Sebelumnya/Berikutnya. Pilih satu struk untuk membaca total, pembayaran, kembalian, serta snapshot item dan harga **pada saat transaksi**. Tidak ada fitur edit/hapus transaksi dari dialog.
- **Buat Backup:** pilih path file baru `.db`. Program memakai API backup SQLite (bukan penyalinan berkas database aktif), lalu memeriksa `quick_check`, `foreign_key_check`, versi skema dan tabel wajib. Setelah sukses tampil path, ukuran berkas, dan SHA-256.
- **Restore ke File Baru:** pilih file backup yang valid, lalu pilih tujuan **baru**. Program memeriksa input sebelum membuat tujuan, meminta konfirmasi, menyalin snapshot SQLite ke database baru dan menguji integritas. **Database kasir aktif tidak berubah dan aplikasi tidak otomatis berpindah ke hasil restore.**

## Batasan dan alasan desain

1. SQLite beroperasi dalam WAL; menyalin berkas `pos.db` secara manual ketika proses aktif tidak menjamin konsistensi transaksi. Gunakan fungsi **Buat Backup**.
2. Restore sengaja **non-destruktif**. Fitur aktivasi database hasil restore (setelah aplikasi ditutup, inspeksi operator, rollback plan, re-open) belum diimplementasikan. Tidak boleh mengaku proses disaster recovery lengkap sudah tersedia.
3. Baik backup maupun target restore harus berupa **file yang belum ada**; tidak menimpa file tujuan. Pembuatan tujuan memakai `FileMode.CreateNew` untuk mencegah race overwrite.
4. Berkas backup **belum terenkripsi** dan UI backup/restore **belum dibatasi role admin**. Jangan menyimpan backup sensitif di folder publik; gunakan media cadangan terpisah yang aman. Penerapan permissions, enkripsi dan jadwal otomatis masuk sprint berikutnya.
5. Validasi file meliputi integritas SQLite, foreign keys, versi skema POS (`user_version = 3`) dan tabel penting. Database staging ber-versi 2 ditolak. Pemeriksaan tersebut bukan jaminan bahwa sumber tidak mengandung data berbahaya; gunakan hanya berkas yang asalnya dapat dipercaya.
6. Snapshot histori menggunakan waktu UTC dari SQLite yang dikonversi ke waktu lokal untuk ditampilkan. Tidak ada retur, pembatalan, pajak atau ekspor struk cetak pada sprint ini.

## Tes otomatis

Lihat [suite Safety](../tests/SmartCasier.Safety.SelfTests/Program.cs), [POS Core](../tests/SmartCasier.Core.SelfTests/Program.cs), dan [Catalog Editor](../tests/SmartCasier.Catalog.SelfTests/Program.cs).

Dari root repository:

```powershell
dotnet run --project tests/SmartCasier.Safety.SelfTests -c Release
dotnet run --project tests/SmartCasier.Core.SelfTests -c Release
dotnet run --project tests/SmartCasier.Catalog.SelfTests -c Release
```

Suite menguji pencarian dan halaman histori, literal wildcard, snapshot harga, backup dengan WAL, restore ke database terpisah, penolakan file rusak/tidak cocok, dan perlindungan dari overwrite.

## Status roadmap

**Checklist utama tetap 13/32 (41%).** Fitur riwayat dan keamanan data sekarang sudah diimplementasikan secara software, tetapi:
- **SC-POS-05** masih memerlukan cara mencetak/mengekspor receipt, finalisasi alur dan uji UI.
- **SC-POS-06** masih memerlukan aktivasi restore terkontrol, jadwal backup dan pengujian pemulihan operasional.
- **Kontrol akses admin** belum ada dan berisiko jika program dipakai di tempat dengan banyak operator.
- Seluruh pekerjaan fisik tetap [DEFERRED_ON_SITE](EXECUTION_POLICY_SOFTWARE_ONLY.md), tidak memblokir software sprint.

**Prioritas berikutnya:** login/role admin dan pembatasan akses perubahan harga/stok dan pemulihan DB; sesudah itu simulator scanner/encoder.
