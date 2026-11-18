using System.Globalization;
using System.Windows;
using SmartCasier.Core;

namespace SmartCasier.Desktop;

/// <summary>Satu aksi per dialog; harga dan stok tidak diubah dalam satu submit.</summary>
public partial class SkuEditDialog : Window
{
    public enum EditKind { None, Price, Adjustment }

    public EditKind Operation { get; private set; }
    public long Value { get; private set; }
    public string Reason { get; private set; } = "";

    public SkuEditDialog(string sku, long currentPrice)
    {
        InitializeComponent();
        SkuLabel.Text = sku;
        PriceBox.Text = currentPrice.ToString(CultureInfo.InvariantCulture);
    }

    private void Price_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!long.TryParse(PriceBox.Text.Trim(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var price) || price < 0)
                throw new ArgumentException("Harga baru harus rupiah integer nonnegatif.");
            Operation = EditKind.Price;
            Value = price;
            DialogResult = true;
        }
        catch (Exception ex) { ErrorText.Text = ex.Message; }
    }

    private void Adjust_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var raw = DeltaBox.Text.Trim().Replace(',', '.');
            if (!decimal.TryParse(raw, NumberStyles.AllowDecimalPoint |
                    NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var delta)
                || delta == 0 || delta * 1000m != decimal.Truncate(delta * 1000m))
                throw new ArgumentException("Koreksi stok harus angka bertanda nonnol, maksimal 3 desimal.");
            if (string.IsNullOrWhiteSpace(ReasonBox.Text))
                throw new ArgumentException("Alasan koreksi stok wajib diisi.");
            Value = checked((long)(delta * 1000m));
            Reason = ReasonBox.Text.Trim();
            Operation = EditKind.Adjustment;
            DialogResult = true;
        }
        catch (Exception ex) { ErrorText.Text = ex.Message; }
    }
}
