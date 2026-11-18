using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SmartCasier.Core;

namespace SmartCasier.Desktop;

/// <summary>Penampil histori read-only; tidak ada aksi hapus atau edit receipt.</summary>
public partial class SalesHistoryDialog : Window
{
    private const int PageSize = 100;
    private static readonly CultureInfo Id = CultureInfo.GetCultureInfo("id-ID");
    private readonly SalesHistoryService _history;
    private readonly AuthSession _operatorSession;
    private int _offset;

    public ObservableCollection<HistoryRow> Sales { get; } = [];

    public SalesHistoryDialog(PosDatabase database, AuthSession operatorSession)
    {
        new AuthService(database).RequireOperator(operatorSession);
        _operatorSession = operatorSession;
        InitializeComponent();
        DataContext = this;
        _history = new SalesHistoryService(database);
        Loaded += (_, _) => LoadHistory();
    }

    private static string Rupiah(long amount) => "Rp" + amount.ToString("N0", Id);

    public sealed class HistoryRow(SaleHistoryRow original)
    {
        public Guid SaleId { get; } = original.SaleId;
        public string ReceiptNo { get; } = original.ReceiptNo;
        public int LineCount { get; } = original.LineCount;
        public string TotalLabel { get; } = Rupiah(original.TotalIdr);
        public string PaidLabel { get; } = Rupiah(original.PaidIdr);
        public string ChangeLabel { get; } = Rupiah(original.ChangeIdr);
        public string DateLabel { get; } = ParseUtc(original.CreatedUtc);

        private static string ParseUtc(string value)
        {
            if (DateTime.TryParseExact(value, "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal |
                        DateTimeStyles.AdjustToUniversal, out var utc))
                return utc.ToLocalTime().ToString("dd MMM yyyy HH:mm", Id);
            return value + " UTC";
        }
    }

    private void LoadHistory()
    {
        try
        {
            var records = _history.Search(_operatorSession, FilterBox.Text, PageSize, _offset);
            Sales.Clear();
            foreach (var item in records)
                Sales.Add(new HistoryRow(item));
            PrevButton.IsEnabled = _offset > 0;
            NextButton.IsEnabled = records.Count == PageSize;
            PageLabel.Text = records.Count == 0
                ? "Belum ada transaksi pada halaman ini."
                : $"Transaksi {_offset + 1}–{_offset + records.Count} (maks. {PageSize}/halaman)";
            HistoryStatus.Text = "Histori lokal berhasil dimuat. Pilih salah satu struk.";
            ReceiptText.Text = "Pilih transaksi untuk melihat rincian.";
        }
        catch (Exception ex)
        {
            HistoryStatus.Text = "Gagal memuat histori: " + ex.Message;
        }
    }

    private void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        _offset = 0;
        LoadHistory();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => LoadHistory();

    private void FilterBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        _offset = 0;
        LoadHistory();
        e.Handled = true;
    }

    private void PrevButton_Click(object sender, RoutedEventArgs e)
    {
        _offset = Math.Max(0, _offset - PageSize);
        LoadHistory();
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        _offset = checked(_offset + PageSize);
        LoadHistory();
    }

    private void HistoryGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HistoryGrid.SelectedItem is not HistoryRow row)
            return;
        try
        {
            var receipt = _history.Receipt(row.SaleId, _operatorSession)
                ?? throw new InvalidOperationException("Struk tidak ditemukan pada database.");
            var parts = new List<string>
            {
                "STRUK: " + receipt.ReceiptNo,
                "TOTAL: " + Rupiah(receipt.TotalIdr),
                "TUNAI: " + Rupiah(receipt.PaidIdr),
                "KEMBALIAN: " + Rupiah(receipt.ChangeIdr),
                "",
                "RINCIAN BARANG"
            };
            foreach (var line in receipt.Lines)
            {
                var qty = (line.QuantityMilli / 1000m).ToString("0.###", Id);
                parts.Add(line.Sku + "  " + line.Name);
                parts.Add("  " + qty + " " + line.UnitCode + " × " +
                    Rupiah(line.PriceIdr) + " = " + Rupiah(line.TotalIdr));
            }
            ReceiptText.Text = string.Join(Environment.NewLine, parts);
        }
        catch (Exception ex)
        {
            HistoryStatus.Text = "Gagal membaca rincian struk: " + ex.Message;
        }
    }
}
