using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using SmartCasier.Core;

namespace SmartCasier.Desktop;

public sealed record SimulatedCartSelection(string Sku, long QuantityMilli);

/// <summary>
/// UI virtual hardware only. ACK records operator confirmation, not payment/stock mutation.
/// </summary>
public partial class DeviceSimulatorDialog : Window
{
    private readonly DeviceInbox _inbox;
    private readonly AuthSession _session;
    private readonly CatalogService _catalog;
    private readonly VirtualBarcodeReader _scanner = new();
    private readonly VirtualLengthMeter _encoder = new();
    public ObservableCollection<PendingRow> PendingEvents { get; } = [];
    public SimulatedCartSelection? Result { get; private set; }
    private static readonly CultureInfo Id = CultureInfo.GetCultureInfo("id-ID");

    public DeviceSimulatorDialog(PosDatabase db, AuthSession session)
    {
        new AuthService(db).RequireOperator(session);
        _inbox = new DeviceInbox(db);
        _catalog = new CatalogService(db);
        _session = session;
        InitializeComponent();
        DataContext = this;
        Loaded += (_,_) => Reload();
    }

    private static long Milli(string text)
    {
        var value = text.Trim().Replace(',', '.');
        if(!decimal.TryParse(value,NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,out var d) ||
            d <= 0 || d * 1000m != decimal.Truncate(d * 1000m))
            throw new ArgumentException("Panjang harus positif dengan maksimal 3 desimal.");
        return checked((long)(d * 1000m));
    }

    private void Reload(Guid? focus=null)
    {
        var pending = _inbox.Pending(_session);
        PendingEvents.Clear();
        foreach (var row in pending)
            PendingEvents.Add(new PendingRow(row));
        if(focus is Guid id)
            PendingGrid.SelectedItem = PendingEvents.FirstOrDefault(x=>x.EventId==id);
        if(PendingGrid.SelectedItem is null && PendingEvents.Count>0)
            PendingGrid.SelectedItem = PendingEvents[0];
    }

    private void Scan_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var frame=_scanner.Scan(BarcodeBox.Text.Trim());
            var accepted=_inbox.Publish(_session,frame);
            Reload(frame.EventId);
            SimulatorStatus.Text="Scan virtual diterima: "+accepted.EventId+" (PENDING).";
        }
        catch(Exception error) { SimulatorStatus.Text="Gagal scan: "+error.Message; }
    }

    private void Feed_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _encoder.Feed(Milli(FeedBox.Text));
            LengthLabel.Text=(_encoder.FeedMilli/1000m).ToString("0.###",Id)+" m";
            SimulatorStatus.Text="Umpan virtual diperbarui. Potong virtual hanya mencatat panjang.";
        }
        catch(Exception error) { SimulatorStatus.Text="Umpan gagal: "+error.Message; }
    }

    private void Cut_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var frame=_encoder.SimulateManualCut();
            var accepted=_inbox.Publish(_session,frame);
            LengthLabel.Text="0 m";
            Reload(frame.EventId);
            SimulatorStatus.Text="Event panjang "+(accepted.LengthMilli/1000m)?.ToString("0.###",Id)+
                " m dibuat (PENDING); tidak ada pisau sungguhan.";
        }
        catch(Exception error) { SimulatorStatus.Text="Potong virtual gagal: "+error.Message; }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        try { Reload(); SimulatorStatus.Text="Antrian PENDING telah dimuat ulang."; }
        catch(Exception error) { SimulatorStatus.Text="Muat ulang gagal: "+error.Message; }
    }

    private void PendingGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PendingGrid.SelectedItem is not PendingRow selected) return;
        if (selected.Source.Kind=="BARCODE")
        {
            // Suggest SKU only for an exact match, never invent an identity from appearance.
            var found=_catalog.Search(selected.Source.Barcode ?? "", 100)
                .FirstOrDefault(x=>string.Equals(x.Sku,selected.Source.Barcode,
                    StringComparison.OrdinalIgnoreCase));
            if(found is not null) SkuBox.Text=found.Sku;
        }
        else SkuBox.Text = ""; // Roll material always requires manual variant selection.
    }

    private void Acknowledge_Click(object sender, RoutedEventArgs e)
    {
        if (PendingGrid.SelectedItem is not PendingRow selected)
        {
            SimulatorStatus.Text="Pilih event PENDING terlebih dahulu.";
            return;
        }
        try
        {
            var sku=SkuBox.Text.Trim();
            var item=_catalog.Search(sku,100)
                .SingleOrDefault(x=>string.Equals(x.Sku,sku,StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("SKU belum tersedia pada katalog.");
            var qty=selected.Source.ProposedQuantityMilli;
            if(qty<=0 || item.OnHandMilli<qty)
                throw new InvalidOperationException("Stok tidak mencukupi untuk jumlah simulasi.");
            var ack=_inbox.Acknowledge(_session,selected.EventId,sku);
            Result=new SimulatedCartSelection(ack.ResolvedSku!,qty);
            DialogResult=true;
        }
        catch(Exception ex) { SimulatorStatus.Text="ACK ditolak: "+ex.Message; }
    }

    private void Reject_Click(object sender, RoutedEventArgs e)
    {
        if(PendingGrid.SelectedItem is not PendingRow selected)
        {
            SimulatorStatus.Text="Pilih event sebelum ditolak.";
            return;
        }
        try
        {
            _inbox.Reject(_session,selected.EventId);
            Reload();
            SimulatorStatus.Text="Event ditolak; tidak ada transaksi.";
        }
        catch(Exception ex) { SimulatorStatus.Text="Gagal menolak event: "+ex.Message; }
    }

    public sealed class PendingRow(DeviceEventReceipt source)
    {
        public DeviceEventReceipt Source { get; }=source;
        public Guid EventId => Source.EventId;
        public string ShortId => Source.EventId.ToString("N")[..12];
        public string DeviceId => Source.DeviceId;
        public string Kind => Source.Kind;
        public string Payload => Source.Kind=="BARCODE"
            ? Source.Barcode ?? ""
            : (Source.LengthMilli.GetValueOrDefault()/1000m).ToString("0.###",Id)+" m";
    }
}
