using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using SmartCasier.Core;

namespace SmartCasier.Desktop;

public partial class NewSkuDialog : Window
{
    public NewSku? Result { get; private set; }

    public NewSkuDialog()
    {
        InitializeComponent();
        ProductKeyBox.Focus();
    }

    private static long Milli(string text)
    {
        var normalized = text.Trim().Replace(',', '.');
        if (!decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var quantity) ||
            quantity < 0 || quantity * 1000m != decimal.Truncate(quantity * 1000m))
            throw new ArgumentException("Jumlah harus nonnegatif, maksimal 3 desimal.");
        return checked((long)(quantity * 1000m));
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!long.TryParse(PriceBox.Text.Trim(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var price) || price < 0)
                throw new ArgumentException("Harga jual harus rupiah integer nonnegatif.");
            var unit = (UnitBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "PCS";
            Result = new NewSku(
                CategoryCodeBox.Text, CategoryNameBox.Text, ProductKeyBox.Text,
                ProductNameBox.Text, SkuBox.Text, VariantBox.Text,
                unit, price, Milli(OpeningBox.Text), Milli(ThresholdBox.Text),
                BarcodeBox.Text);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            Result = null;
        }
    }
}
