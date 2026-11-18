# Smart Casier — Windows Desktop v0.1

**Mode:** SOFTWARE_ONLY — **bukan aplikasi siap dipakai sebagai kasir komersial**.  
**Teknologi:** WPF, C#/.NET 10, SQLite WAL, offline, satu PC Windows.  
**Status bukti:** [Build Windows + Linux dan 53 tes per OS lulus](https://github.com/Skyreaaa/smart-casier/actions/runs/44285643615). **Belum ada pengujian interaksi/visual langsung pada Windows desktop.**

## Dapat dijalankan dari GitHub Actions

1. Masuk ke repository private [Smart Casier](https://github.com/Skyreaaa/smart-casier).
2. Buka [workflow software Windows terakhir yang lulus](https://github.com/Skyreaaa/smart-casier/actions/runs/44285643615).
3. Di bagian *Artifacts*, pilih `smart-casier-windows-demo` (artifact retensi 7 hari dari tanggal run), ekstrak semua berkas dalam satu folder.
4. Instal **.NET 10 Desktop Runtime x64** di Windows bila belum ada (ini versi **framework-dependent**, bukan installer mandiri).
5. Dari PowerShell pada folder hasil ekstrak jalankan:
   ```powershell
   .\SmartCasier.Desktop.exe --demo
   ```
6. Untuk mode database normal (tanpa seed produk), jalankan `.\SmartCasier.Desktop.exe`.

**Penting:** jangan menguji data toko asli pada demo; jangan anggap data demo sesuai harga dan saldo toko.

## Menjalankan dari source

Butuh Windows dengan **.NET 10 SDK**:

```powershell
git clone https://github.com/Skyreaaa/smart-casier.git
cd smart-casier
dotnet run --project src/SmartCasier.Desktop -- --demo
```

Untuk mengulang pengujian logika lintas platform:

```powershell
dotnet run --project tests/SmartCasier.Core.SelfTests/SmartCasier.Core.SelfTests.csproj -c Release
dotnet run --project tests/SmartCasier.Catalog.SelfTests/SmartCasier.Catalog.SelfTests.csproj -c Release
```

## Lokasi database

- Normal: `%LOCALAPPDATA%\SmartCasier\pos.db` — **tidak diberi produk contoh**.
- Demo: `%LOCALAPPDATA%\SmartCasier\demo-pos.db` — data produk/harga/stok **sintetis**.
- Semua operasi berjalan offline menggunakan SQLite WAL. Jangan menyalin file `.db` secara manual ketika aplikasi sedang aktif sebagai cara backup; fitur backup terkoordinasi belum dibuat.

## Yang saat ini berfungsi

| Bagian | Kondisi |
| --- | --- |
| Pencarian katalog | Nama, SKU, alias, barcode opsional |
| Stok | View stok berdasarkan ledger; status Habis/Menipis/Tersedia |
| Item keranjang | PCS atau meter hingga 3 desimal, sesuai SKU |
| Checkout | Tunai, total dan kembalian, receipt tersimpan di SQLite |
| Anti-duplikasi | Request ID idempoten, stok dicek atomik |
| Harga di keranjang | Perubahan harga melalui editor merefresh subtotal; checkout menolak total stale |
| SKU baru | Dialog memasukkan kategori, keluarga, varian, harga, stok, barcode opsional |
| Koreksi stok | Ledger ADJUSTMENT dengan alasan wajib |
| Keyboard | F2 fokus pencarian, F4 checkout |
| CI | WPF dibangun di Windows; POS Core dan editor diuji pada Windows/Linux |

Saat membuat varian baru untuk keluarga yang sama, gunakan **kode keluarga dan nama/kategori induk yang konsisten**. SKU unik. Barang `PCS` hanya jumlah bulat; satuan `M` hingga tiga angka desimal. Semua harga dalam integer IDR.

## Belum siap / pembatasan

- **UI:** belum ada screenshot/tes klik interaktif yang diverifikasi di perangkat Windows; tidak boleh mengklaim UX dan aksesibilitas sudah lulus.
- **Admin/security:** pengelolaan harga/stok belum memiliki login/role atau audit user. Jalankan hanya dalam konteks demo/terkontrol.
- **CRUD lengkap:** belum tersedia edit nama/atribut/varian, penghapusan lunak, harga historis per perubahan, daftar pemasok.
- **POS:** belum ada tampilan riwayat penjualan, retur, printer struk, diskon/pajak, pembayaran non-tunai, dan fulfillment pulsa.
- **Data protection:** backup dan restore ke file baru tersedia, tetapi belum ada jadwal/enkripsi dan prosedur aktivasi pemulihan. Belum diuji pemadaman listrik, akses lokal, atau penyimpanan jangka panjang.
- **Hardware:** kamera AI, barcode scanner khusus, encoder, display dan cutter tidak terintegrasi. Input barcode dari scanner keyboard HID masih perlu diuji fisik (ditunda).
- **Legal/operasional:** semua [DEFERRED_ON_SITE](EXECUTION_POLICY_SOFTWARE_ONLY.md) tetap wajib sebelum digunakan secara komersial.

## Sprint software berikutnya

1. Autentikasi admin dan audit perubahan harga/stok (belum tersedia).
2. Prosedur aktivasi hasil restore secara aman, serta backup otomatis yang diverifikasi.
3. Login serta pembatasan izin admin untuk perubahan harga/stok.
4. Lengkapi pengelolaan SKU (edit metadata, nonaktifkan, audit harga).
5. Simulator device events — scanner dan meter encoder — tanpa menuntut hardware tersedia.

**Pembaruan v0.2:** Riwayat receipt dan backup/restore aman ke file BARU tersedia dalam WPF. [Panduan dan keterbatasan pemulihan](SALES_HISTORY_AND_BACKUP.md).

**Pembaruan v0.3:** Saat pertama dijalankan, buat Admin lokal sendiri (password minimal 12 karakter); login wajib sebelum membuka kasir. Admin dapat membuat akun Kasir melalui tombol di header, lalu ganti pengguna. Harga/stok/backup/restore hanya Admin. [Panduan otorisasi, risiko dan status lengkap](LOCAL_AUTH_V0_3.md).

**Definisi selesai versi ini:** source ter-commit, aplikasi WPF terkompilasi, tes bisnis lulus, artifact tersedia, dan gap pengujian UI dicatat. Tidak ada klaim *production ready*.
