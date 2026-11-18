using System.IO;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SmartCasier.Core;

namespace SmartCasier.Desktop;

/// <summary>
/// Antarmuka kasir. Semua keputusan transaksi tetap milik SmartCasier.Core.
/// Tidak ada komunikasi hardware; keyboard barcode HID dapat memakai kolom pencarian.
/// </summary>
public partial class MainWindow : Window, INotifyPropertyChanged
{
    private static readonly CultureInfo Indonesian = CultureInfo.GetCultureInfo("id-ID");
    private PosDatabase? _database;
    private CatalogService? _catalog;
    private SalesService? _sales;
    private Guid _requestId = Guid.NewGuid();
    private bool _initialized;
    private string _itemCount = "0 produk";

    public ObservableCollection<ProductRow> Products { get; } = [];
    public ObservableCollection<CartRow> Cart { get; } = [];

    public string ItemCount
    {
        get => _itemCount;
        private set { _itemCount = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var isDemo = Environment.GetCommandLineArgs().Any(
                arg => string.Equals(arg, "--demo", StringComparison.OrdinalIgnoreCase));
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SmartCasier");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, isDemo ? "demo-pos.db" : "pos.db");
            _database = new PosDatabase(path);
            _database.Initialize();
            if (isDemo) DemoSeeder.Seed(_database);

            _catalog = new CatalogService(_database);
            _sales = new SalesService(_database);
            ModeText.Text = isDemo ? "DEMO · DATA SINTETIS" : "DATA LOKAL · BELUM VALIDASI TOKO";
            DatabaseText.Text = path;
            _initialized = true;
            ReloadProducts();
            ShowInfo(isDemo
                ? "Mode demo aktif. Harga dan stok adalah contoh sintetis, bukan data toko."
                : "Database lokal siap. Belum ada produk? Gunakan katalog terverifikasi pada tahap berikutnya.");
            SearchBox.Focus();
        }
        catch (Exception ex)
        {
            _initialized = false;
            CheckoutButton.IsEnabled = false;
            ShowError("Gagal membuka database: " + ex.Message);
        }
    }

    private void ReloadProducts()
    {
        if (!_initialized || _catalog is null) return;
        var selectedSku = (ProductGrid.SelectedItem as ProductRow)?.Sku;
        var query = SearchBox.Text.Trim();
        IReadOnlyList<CatalogItem> found = query.Length == 0
            ? _catalog.Inventory()
            : _catalog.Search(query, 100);

        Products.Clear();
        foreach (var item in found)
            Products.Add(new ProductRow(item));
        if (selectedSku is not null)
            ProductGrid.SelectedItem = Products.FirstOrDefault(
                p => string.Equals(p.Sku, selectedSku, StringComparison.OrdinalIgnoreCase));
    }

    private void SearchBox_TextChanged(object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        try { ReloadProducts(); }
        catch (Exception ex) { ShowError("Pencarian gagal: " + ex.Message); }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        try { ReloadProducts(); ShowInfo("Katalog dan stok telah dimuat ulang."); }
        catch (Exception ex) { ShowError("Gagal memuat ulang: " + ex.Message); }
    }

    private void NewSkuButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _database is null) return;
        var dialog = new NewSkuDialog { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null) return;
        try
        {
            var sku = new ProductCatalogEditor(_database).Create(dialog.Result);
            SearchBox.Text = sku;
            ReloadProducts();
            ShowInfo("SKU " + sku + " telah dibuat pada database lokal.");
        }
        catch (Exception ex)
        {
            ShowError("Produk gagal disimpan: " + ex.Message);
        }
    }

    private void ManageSkuButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _database is null) return;
        if (ProductGrid.SelectedItem is not ProductRow item)
        {
            ShowError("Pilih SKU terlebih dahulu untuk mengubah harga atau stok.");
            return;
        }
        var dialog = new SkuEditDialog(item.Sku, item.UnitPriceIdr) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var editor = new ProductCatalogEditor(_database);
            if (dialog.Operation == SkuEditDialog.EditKind.Price)
            {
                editor.UpdatePrice(item.Sku, dialog.Value);
                var line = Cart.FirstOrDefault(x => string.Equals(x.Sku, item.Sku,
                    StringComparison.OrdinalIgnoreCase));
                if (line is not null)
                {
                    line.Reprice(dialog.Value);
                    CartGrid.Items.Refresh();
                }
            }
            else if (dialog.Operation == SkuEditDialog.EditKind.Adjustment)
                editor.AdjustStock(item.Sku, dialog.Value, dialog.Reason);
            ResetPendingRequest();
            UpdateTotals();
            ReloadProducts();
            ShowInfo("Perubahan " + item.Sku + " tersimpan; nominal keranjang dihitung ulang.");
        }
        catch (Exception ex)
        {
            ShowError("Perubahan gagal: " + ex.Message);
        }
    }

    private void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _database is null) return;
        try
        {
            new SalesHistoryDialog(_database) { Owner = this }.ShowDialog();
        }
        catch (Exception ex)
        {
            ShowError("Gagal membuka histori: " + ex.Message);
        }
    }

    private void BackupButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _database is null) return;
        var dialog = new SaveFileDialog
        {
            Title = "Simpan Snapshot Backup Smart Casier",
            Filter = "Database SQLite (*.db)|*.db",
            DefaultExt = ".db",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = "smart-casier-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".db"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var info = new DatabaseSafetyService(_database).BackupToNewFile(dialog.FileName);
            ShowInfo("Backup SQLite terverifikasi: " + info.FilePath);
            MessageBox.Show(this,
                "Backup selesai dan telah diperiksa integritasnya.\n\n" +
                "File: " + info.FilePath + "\n" +
                "Ukuran: " + info.SizeBytes.ToString("N0", Indonesian) + " byte\n" +
                "SHA-256: " + info.Sha256 + "\n\n" +
                "Simpan salinan pada media lain. Backup belum otomatis dijadwalkan.",
                "Backup Berhasil", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ShowError("Backup tidak berhasil: " + ex.Message);
        }
    }

    private void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _database is null) return;
        var select = new OpenFileDialog
        {
            Title = "Pilih Backup SQLite Smart Casier",
            Filter = "Database SQLite (*.db)|*.db",
            CheckFileExists = true
        };
        if (select.ShowDialog(this) != true) return;
        try
        {
            DatabaseSafetyService.ValidateBackup(select.FileName);
        }
        catch (Exception ex)
        {
            ShowError("File backup tidak valid: " + ex.Message);
            return;
        }

        var target = new SaveFileDialog
        {
            Title = "Simpan Hasil Restore sebagai DATABASE BARU",
            Filter = "Database SQLite (*.db)|*.db",
            DefaultExt = ".db",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = "smart-casier-restore-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".db"
        };
        if (target.ShowDialog(this) != true) return;

        var confirm = MessageBox.Show(this,
            "Restore akan membuat database BARU yang tervalidasi.\n" +
            "Database aktif TIDAK diganti atau ditimpa.\n\n" +
            "Backup: " + select.FileName + "\n" +
            "Tujuan: " + target.FileName + "\n\n" +
            "Lanjutkan?",
            "Konfirmasi Restore Aman", MessageBoxButton.YesNo, MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            var restored = new DatabaseSafetyService(_database)
                .RestoreToNewFile(select.FileName, target.FileName);
            ShowInfo("Restore berhasil ke file baru; database aktif tidak berubah.");
            MessageBox.Show(this,
                "Database berhasil dipulihkan dan lulus pemeriksaan integritas.\n\n" +
                "File hasil: " + restored.FilePath + "\n" +
                "SHA-256: " + restored.Sha256 + "\n\n" +
                "Database aktif tetap berjalan. Peralihan ke hasil restore " +
                "memerlukan prosedur terpisah saat aplikasi ditutup.",
                "Restore Berhasil", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ShowError("Restore tidak berhasil: " + ex.Message);
        }
    }

    private void ProductGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        => AddSelectedProduct();

    private void AddButton_Click(object sender, RoutedEventArgs e) => AddSelectedProduct();

    private static long ParseQuantity(string input, string unit)
    {
        var normalized = input.Trim().Replace(',', '.');
        if (!decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var value) ||
            value <= 0 || value * 1000m != decimal.Truncate(value * 1000m))
            throw new ArgumentException("Jumlah harus positif, maksimal 3 desimal (contoh: 2.500).");
        var milli = checked((long)(value * 1000m));
        if (unit != "M" && milli % 1000 != 0)
            throw new ArgumentException("Barang PCS harus berupa jumlah bulat.");
        return milli;
    }

    private void AddSelectedProduct()
    {
        if (ProductGrid.SelectedItem is not ProductRow selected)
        {
            ShowError("Pilih produk pada tabel katalog terlebih dahulu.");
            return;
        }
        try
        {
            if (selected.StockStatus == "UNTRACKED")
                throw new InvalidOperationException(
                    "Transaksi produk digital/jasa belum didukung dalam versi ini.");
            var addedMilli = ParseQuantity(QuantityBox.Text, selected.UnitCode);
            var existing = Cart.FirstOrDefault(
                row => string.Equals(row.Sku, selected.Sku, StringComparison.OrdinalIgnoreCase));
            var nextMilli = checked((existing?.QuantityMilli ?? 0) + addedMilli);
            if (nextMilli > selected.OnHandMilli)
                throw new InvalidOperationException("Jumlah melebihi stok. Stok tersedia: " +
                    QuantityLabel(selected.OnHandMilli, selected.UnitCode) + ".");

            if (existing is null)
                Cart.Add(new CartRow(selected, addedMilli));
            else
            {
                existing.QuantityMilli = nextMilli;
                // Refresh tampilan setelah pembaruan kuantitas item.
                CartGrid.Items.Refresh();
            }
            ResetPendingRequest();
            UpdateTotals();
            ShowInfo(selected.Sku + " ditambahkan ke keranjang.");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (CartGrid.SelectedItem is not CartRow line)
        {
            ShowError("Pilih item keranjang yang ingin dihapus.");
            return;
        }
        Cart.Remove(line);
        ResetPendingRequest();
        UpdateTotals();
        ShowInfo("Item dihapus dari keranjang.");
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (Cart.Count == 0) return;
        Cart.Clear();
        ResetPendingRequest();
        UpdateTotals();
        ShowInfo("Keranjang dikosongkan.");
    }

    private void PaidBox_TextChanged(object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        ResetPendingRequest();
        UpdateTotals();
    }

    private void ResetPendingRequest() => _requestId = Guid.NewGuid();

    private static long LineTotal(long unitPriceIdr, long quantityMilli)
    {
        var amount = Math.Round((decimal)unitPriceIdr * quantityMilli / 1000m,
            0, MidpointRounding.AwayFromZero);
        return checked((long)amount);
    }

    private long CartTotal() => checked(Cart.Sum(line => LineTotal(line.UnitPriceIdr,
        line.QuantityMilli)));

    private bool TryParsePaid(out long paid)
    {
        return long.TryParse(PaidBox.Text.Trim(), NumberStyles.Integer |
            NumberStyles.AllowThousands, Indonesian, out paid) && paid >= 0;
    }

    private void UpdateTotals()
    {
        if (TotalText is null || ChangeText is null) return;
        try
        {
            var total = CartTotal();
            TotalText.Text = Money(total);
            ItemCount = Cart.Count + (Cart.Count == 1 ? " produk" : " produk");
            if (PaidBox is not null && TryParsePaid(out var paid))
                ChangeText.Text = paid >= total ? Money(paid - total) : "Belum cukup";
            else
                ChangeText.Text = "Nominal tidak valid";
        }
        catch (Exception ex)
        {
            ShowError("Perhitungan gagal: " + ex.Message);
        }
    }

    private void CheckoutButton_Click(object sender, RoutedEventArgs e) => Checkout();

    private void Checkout()
    {
        if (!_initialized || _sales is null) return;
        if (Cart.Count == 0)
        {
            ShowError("Keranjang kosong. Tambahkan produk terlebih dahulu.");
            return;
        }
        if (!TryParsePaid(out var paid))
        {
            ShowError("Uang diterima harus nominal rupiah bulat, misalnya 10000.");
            PaidBox.Focus();
            return;
        }
        CheckoutButton.IsEnabled = false;
        try
        {
            var request = Cart.Select(row => new SaleLineRequest(
                row.Sku, row.QuantityMilli)).ToArray();
            var receipt = _sales.Checkout(_requestId, request, paid, CartTotal());
            Cart.Clear();
            ResetPendingRequest();
            PaidBox.Text = "0";
            UpdateTotals();
            ReloadProducts();
            ShowInfo("Transaksi tersimpan: " + receipt.ReceiptNo);
            MessageBox.Show(this,
                "Transaksi berhasil tersimpan secara lokal.\n\n" +
                "Nomor: " + receipt.ReceiptNo + "\n" +
                "Total: " + Money(receipt.TotalIdr) + "\n" +
                "Tunai: " + Money(receipt.PaidIdr) + "\n" +
                "Kembalian: " + Money(receipt.ChangeIdr) + "\n\n" +
                "Belum mencetak struk fisik.",
                "Pembayaran Berhasil", MessageBoxButton.OK,
                MessageBoxImage.Information);
            SearchBox.Focus();
        }
        catch (Exception ex)
        {
            // Request ID tetap sama jika terjadi kegagalan agar retry aman.
            ShowError("Pembayaran dibatalkan: " + ex.Message);
        }
        finally
        {
            CheckoutButton.IsEnabled = true;
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.F4)
        {
            Checkout();
            e.Handled = true;
        }
    }

    private void ShowInfo(string message)
    {
        StatusText.Text = message;
        StatusText.Foreground = new SolidColorBrush(Color.FromRgb(28, 41, 51));
    }

    private void ShowError(string message)
    {
        StatusText.Text = message;
        StatusText.Foreground = new SolidColorBrush(Color.FromRgb(151, 39, 39));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static string Money(long amount) => "Rp" + amount.ToString("N0", Indonesian);

    private static string QuantityLabel(long milli, string unit)
        => (milli / 1000m).ToString("0.###", Indonesian) + " " + unit;

    public sealed class ProductRow(CatalogItem item)
    {
        public string Sku { get; } = item.Sku;
        public string DisplayName { get; } = item.ProductName + (string.IsNullOrEmpty(item.VariantLabel)
            ? "" : " — " + item.VariantLabel);
        public string UnitCode { get; } = item.UnitCode;
        public long UnitPriceIdr { get; } = item.UnitPriceIdr;
        public long OnHandMilli { get; } = item.OnHandMilli;
        public string StockStatus { get; } = item.StockStatus;
        public string PriceLabel { get; } = Money(item.UnitPriceIdr) + "/" + item.UnitCode;
        public string StockLabel { get; } = QuantityLabel(item.OnHandMilli, item.UnitCode);
        public string StatusLabel { get; } = item.StockStatus switch
        {
            "OK" => "Tersedia",
            "LOW" => "Menipis",
            "OUT" => "Habis",
            _ => "Tidak dilacak"
        };
    }

    public sealed class CartRow(ProductRow item, long quantityMilli)
    {
        public string Sku { get; } = item.Sku;
        public string UnitCode { get; } = item.UnitCode;
        public long UnitPriceIdr { get; private set; } = item.UnitPriceIdr;
        public void Reprice(long newPrice) => UnitPriceIdr = newPrice;
        public long QuantityMilli { get; set; } = quantityMilli;
        public string QuantityLabel => MainWindow.QuantityLabel(QuantityMilli, UnitCode);
        public string SubtotalLabel => Money(LineTotal(UnitPriceIdr, QuantityMilli));
    }
}
