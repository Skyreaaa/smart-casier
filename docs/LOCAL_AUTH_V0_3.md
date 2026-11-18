# Smart Casier — Login Lokal dan Role Admin/Kasir v0.3 (arsip)

> **Pembaruan v0.4:** [Checkout dan receipt sekarang wajib sesi operator, event barcode/encoder virtual tersedia](VIRTUAL_DEVICE_PROTOCOL_V0_4.md). CLI checkout dinonaktifkan.

**Status:** Software-only demo/preview. [CI Windows dan Linux lulus: 53 tes per platform](https://github.com/Skyreaaa/smart-casier/actions/runs/44285643615). Progres utama tetap 13/32. Semua pemeriksaan toko/hardware masih [DEFERRED_ON_SITE](EXECUTION_POLICY_SOFTWARE_ONLY.md).

## Cara pertama kali menjalankan

1. Jalankan `SmartCasier.Desktop.exe --demo` (paket Windows dari workflow) atau dari kode sumber dengan `dotnet run --project src/SmartCasier.Desktop -- --demo`.
2. Database demo `%LOCALAPPDATA%\SmartCasier\demo-pos.db` sekarang menggunakan skema **v4**. Jika belum ada akun, muncul form **Buat Admin Pertama**. Pilih username baru dan password kuat minimal 12 karakter. **Tidak ada password atau akun default.**
3. Setelah berhasil dibuat, login masuk secara lokal. Simpan password Admin pada pengelola password yang aman. Tidak ada fitur pemulihan password pada fase ini.
4. Pada akun Admin, klik **Buat Kasir** untuk membuat username/password operator berbeda. Gunakan **Ganti Pengguna** untuk memeriksa menu akun Kasir.
5. Mode tanpa `--demo` memakai `%LOCALAPPDATA%\SmartCasier\pos.db`, terpisah; pembuatan Admin pertama juga wajib di database tersebut. **Jangan uji pada data toko nyata sebelum UAT dan recovery tersedia.**

## Tabel izin versi ini

| Operasi lewat GUI | Admin | Kasir |
| --- | --- | --- |
| Login, lihat katalog, mencari produk | Ya | Ya |
| Tambah item keranjang dan checkout tunai | Ya | Ya |
| Baca histori dan receipt | Ya | Ya |
| Buat SKU dan ubah harga | Ya | Tidak |
| Koreksi stok dengan alasan | Ya | Tidak |
| Backup/restore ke file baru | Ya | Tidak |
| Buat akun Kasir | Ya | Tidak |
| Penghapusan user, reset password, promosi Kasir→Admin | Belum dibuat | Tidak |

**Pengamanan di Core:** `ProductCatalogEditor.Create`, `UpdatePrice`, `AdjustStock`, `DatabaseSafetyService.BackupToNewFile` dan `RestoreToNewFile` kini **membutuhkan `AuthSession` Admin**; sesi diperiksa terhadap database, masa aktif, dan status/role user aktif. UI nonaktifkan tombol terlarang sebagai perlindungan tambahan.

## Kontrol dan batas teknis

- Migrasi operasional [004_local_auth.sql](../database/migrations/004_local_auth.sql) menambah `app_users` dan `security_audit`, `user_version=4`, menjaga sales/stock existing. Database staging eksternal tetap file SQLite terpisah.
- Password disimpan sebagai hash `PBKDF2-HMAC-SHA256`, salt acak 32 byte, 310.000 iterasi. Tidak ada password hardcoded atau plaintext di database.
- Lima login gagal mengunci akun sementara 15 menit. Sesi Admin diperiksa terhadap batas 8 jam **saat mengakses operasi sensitif**. UI menghapus referensi sesi ketika operator diganti atau aplikasi ditutup, tetapi **kedaluwarsa sesi belum ditegakkan pada seluruh jalur checkout/histori** dan harus dilengkapi sebelum operasional.
- Audit keamanan saat ini mencakup bootstrap Admin, login sukses/gagal dan pembuatan Kasir. **Identitas admin yang mengubah harga/stok belum diikat ke ledger perubahan; ini masih backlog.**
- **Batas keamanan penting:** file SQLite belum terenkripsi atau dilindungi ACL khusus; orang dengan hak tulis file pada Windows tetap dapat memodifikasi database langsung. Sebagian API baca dan checkout pada Core/CLI masih memiliki jalur tanpa token yang diperiksa Core. Pembatasan checkout CLI telah diganti dengan penonaktifan perintah. Ini bukan batas keamanan untuk akses langsung file SQLite, **bukan batas keamanan yang kuat**.
- Backup mencakup data akun dan hash password; **tidak terenkripsi**. Simpan di folder terbatas dan jangan bagikan ke pihak lain.
- Belum tersedia reset/rotasi password Admin, manajemen nonaktif user di GUI, autentikasi multi-PC, pengujian aksesibilitas dengan screen reader, atau uji penembusan. Tidak boleh diklaim production-ready.

## Hasil pengujian otomatis

- POS Core: **14** skenario.
- CRUD parsial/editor katalog: **11** skenario.
- Database history dan backup: **14** skenario.
- Autentikasi: **14** skenario, termasuk first-run, role refusal, lockout, akun lintas database, nonaktif Admin, akses backup/katalog tanpa hak, hashing.
- Total **53 tes** per OS dan build paket Windows **lulus** pada [CI terakhir](https://github.com/Skyreaaa/smart-casier/actions/runs/44285643615). Peringatan kerentanan `NU1903` pada SQLite lama tidak muncul lagi setelah patch paket `Microsoft.Data.Sqlite 10.0.13`.

## Software berikutnya

1. Wajibkan identitas operator pada **semua** aksi mutasi Core, termasuk checkout dan histori audit per operator; tutup jalur CLI yang bisa menghindari login ketika dihubungkan ke database operasional.
2. Simulator barcode/encoder dengan interface, peristiwa idempoten, timeout dan fallback input manual tanpa hardware.
3. Perkuat backup dengan prosedur aktivasi restore terkontrol, verifikasi file lokal, jadwal dan enkripsi.
4. Uji interaktif WPF, UI focus/keyboard, validasi login, aksesibilitas dan recovery.

**Catatan:** data produk publik yang masih HOLD_LICENSE, SKU toko asli dan komponen hardware tidak dipromosikan atau dinyatakan selesai.
