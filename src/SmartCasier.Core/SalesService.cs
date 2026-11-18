using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;

namespace SmartCasier.Core;

public sealed record SaleLineRequest(string Sku, long QuantityMilli);
public sealed record SaleReceiptLine(
    string Sku, string Name, string UnitCode, long QuantityMilli,
    long PriceIdr, long TotalIdr);
public sealed record SaleReceipt(
    Guid SaleId, string ReceiptNo, long TotalIdr, long PaidIdr,
    long ChangeIdr, IReadOnlyList<SaleReceiptLine> Lines,
    bool AlreadyProcessed);

/// <summary>Transaksi POS offline atomik, idempoten dan menjaga stok tidak negatif.</summary>
public sealed class SalesService(PosDatabase database)
{
    private sealed record VariantData(
        string Id, string Sku, string Name, string UnitCode, long PriceIdr,
        bool FractionAllowed, long StockMilli);

    public SaleReceipt Checkout(Guid requestId, IEnumerable<SaleLineRequest> requestLines, long paidIdr,
        long? expectedTotalIdr = null)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException("Sale request ID may not be empty.", nameof(requestId));
        if (requestLines is null) throw new ArgumentNullException(nameof(requestLines));
        if (paidIdr < 0) throw new ArgumentOutOfRangeException(nameof(paidIdr));

        var list = requestLines.ToArray();
        if (list.Length == 0) throw new ArgumentException("Cart is empty.",nameof(requestLines));
        if (list.Any(x => string.IsNullOrWhiteSpace(x.Sku) || x.QuantityMilli <= 0))
            throw new ArgumentException("Every item needs a SKU and positive quantity.",nameof(requestLines));

        var grouped = list.GroupBy(x => x.Sku.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new SaleLineRequest(g.Key.ToUpperInvariant(),
                checked(g.Sum(x => x.QuantityMilli))))
            .OrderBy(x => x.Sku, StringComparer.Ordinal)
            .ToArray();
        string canonical = paidIdr + "|" + string.Join("|",
            grouped.Select(x => x.Sku + ":" + x.QuantityMilli));
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));

        using var conn = database.Open();
        using var tx = conn.BeginTransaction();
        var saleKey = requestId.ToString("N");
        var prior = ReadReceipt(conn,tx,saleKey);
        if (prior is not null)
        {
            var savedHash = ScalarString(conn,tx,
                "SELECT request_hash FROM sales WHERE sale_id=@id;",
                ("@id",saleKey));
            if (!string.Equals(savedHash, hash, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Request id already used with a different cart or payment.");
            tx.Commit();
            return prior with { AlreadyProcessed=true };
        }

        var receiptLines = new List<(VariantData Variant, long QuantityMilli, long LineTotalIdr)>();
        decimal total = 0;
        foreach (var item in grouped)
        {
            var v = GetVariant(conn,tx,item.Sku);
            if (!v.FractionAllowed && item.QuantityMilli % 1000 != 0)
                throw new ArgumentException("Fractional piece quantity is forbidden for " + item.Sku);
            if (v.StockMilli < item.QuantityMilli)
                throw new InvalidOperationException("Insufficient stock for " + item.Sku);
            decimal amount = Math.Round(v.PriceIdr * (decimal)item.QuantityMilli / 1000m,
                0, MidpointRounding.AwayFromZero);
            long lineTotal = checked((long)amount);
            total += amount;
            receiptLines.Add((v,item.QuantityMilli,lineTotal));
        }
        long grandTotal = checked((long)total);
        if (expectedTotalIdr is long expected && grandTotal != expected)
            throw new InvalidOperationException("Harga produk berubah. Muat ulang keranjang sebelum pembayaran.");
        if (paidIdr < grandTotal)
            throw new InvalidOperationException("Paid amount is below sale total.");
        long change = checked(paidIdr - grandTotal);
        var receiptNo = "SC-" + saleKey.ToUpperInvariant();

        Execute(conn,tx,"""
            INSERT INTO sales(sale_id,receipt_no,request_hash,total_idr,paid_idr,change_idr)
            VALUES(@id,@receipt,@hash,@total,@paid,@change);
            """,
            ("@id",saleKey),("@receipt",receiptNo),("@hash",hash),
            ("@total",grandTotal),("@paid",paidIdr),("@change",change));
        foreach (var line in receiptLines)
        {
            Execute(conn,tx,"""
                INSERT INTO sale_lines(line_id,sale_id,variant_id,sku_snapshot,
                                       product_snapshot,unit_code,quantity_milli,
                                       unit_price_idr,line_total_idr)
                VALUES(@line,@sale,@variant,@sku,@name,@unit,@qty,@price,@total);
                """,
                ("@line",Guid.NewGuid().ToString("N")),("@sale",saleKey),
                ("@variant",line.Variant.Id),("@sku",line.Variant.Sku),
                ("@name",line.Variant.Name),("@unit",line.Variant.UnitCode),
                ("@qty",line.QuantityMilli),("@price",line.Variant.PriceIdr),
                ("@total",line.LineTotalIdr));
            Execute(conn,tx,"""
                INSERT INTO stock_movements(id,variant_id,movement_type,
                                            delta_qty_milli,source_ref,note)
                VALUES(@id,@variant,'SALE',@delta,@source,'POS checkout');
                """,
                ("@id",Guid.NewGuid().ToString("N")),
                ("@variant",line.Variant.Id),("@delta",-line.QuantityMilli),
                ("@source",saleKey));
        }
        tx.Commit();
        return ReadReceiptWithoutTransaction(conn,saleKey)
            ?? throw new InvalidOperationException("Checkout commit succeeded but receipt missing.");
    }

    private static VariantData GetVariant(
        SqliteConnection conn, SqliteTransaction tx, string sku)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT v.id,v.sku,p.name||CASE WHEN v.label='' THEN ''
                       ELSE ' - '||v.label END,v.unit_code,v.sale_price_idr,
                   u.fractional_allowed,i.on_hand_milli,p.product_type,v.track_stock
            FROM variants v
            JOIN products p ON p.id=v.product_id
            JOIN units u ON u.code=v.unit_code
            JOIN v_inventory_status i ON i.variant_id=v.id
            WHERE v.sku=@sku COLLATE NOCASE AND v.is_active=1 AND p.is_active=1;
            """;
        cmd.Parameters.AddWithValue("@sku",sku);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new KeyNotFoundException("Unknown or inactive SKU: " + sku);
        if (r.GetString(7) != "PHYSICAL" || r.GetInt64(8) != 1)
            throw new NotSupportedException(
                "Digital/service fulfillment is not implemented for SKU " + sku);
        return new VariantData(r.GetString(0),r.GetString(1),r.GetString(2),
            r.GetString(3),r.GetInt64(4),r.GetInt64(5)==1,r.GetInt64(6));
    }

    public SaleReceipt? FindReceipt(Guid saleId)
    {
        using var conn = database.Open();
        return ReadReceiptWithoutTransaction(conn,saleId.ToString("N"));
    }

    private static SaleReceipt? ReadReceiptWithoutTransaction(SqliteConnection conn,string id)
        => ReadReceipt(conn,null,id);

    private static SaleReceipt? ReadReceipt(SqliteConnection conn,SqliteTransaction? tx,string id)
    {
        using var command = conn.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            SELECT receipt_no,total_idr,paid_idr,change_idr FROM sales WHERE sale_id=@id;
            """;
        command.Parameters.AddWithValue("@id",id);
        using var r = command.ExecuteReader();
        if (!r.Read()) return null;
        var receiptNo=r.GetString(0);
        var total=r.GetInt64(1);
        var paid=r.GetInt64(2);
        var change=r.GetInt64(3);
        r.Close();
        using var linesCmd=conn.CreateCommand();
        linesCmd.Transaction=tx;
        linesCmd.CommandText = """
            SELECT sku_snapshot,product_snapshot,unit_code,quantity_milli,
                   unit_price_idr,line_total_idr
            FROM sale_lines WHERE sale_id=@id ORDER BY rowid;
            """;
        linesCmd.Parameters.AddWithValue("@id",id);
        using var lr=linesCmd.ExecuteReader();
        var lines=new List<SaleReceiptLine>();
        while(lr.Read())lines.Add(new SaleReceiptLine(lr.GetString(0),lr.GetString(1),
            lr.GetString(2),lr.GetInt64(3),lr.GetInt64(4),lr.GetInt64(5)));
        return new SaleReceipt(Guid.ParseExact(id,"N"),receiptNo,total,paid,change,lines,false);
    }

    private static string? ScalarString(SqliteConnection conn,SqliteTransaction tx,
        string query,params (string Name,object Value)[] args)
    {
        using var cmd=CreateCommand(conn,tx,query,args);
        return cmd.ExecuteScalar() as string;
    }

    private static void Execute(SqliteConnection conn,SqliteTransaction tx,string sql,
        params (string Name,object Value)[] args)
    {
        using var cmd=CreateCommand(conn,tx,sql,args);
        cmd.ExecuteNonQuery();
    }

    private static SqliteCommand CreateCommand(SqliteConnection conn,SqliteTransaction tx,
        string sql,params (string Name,object Value)[] args)
    {
        var command=conn.CreateCommand();
        command.Transaction=tx;
        command.CommandText=sql;
        foreach(var (key,value) in args)command.Parameters.AddWithValue(key,value);
        return command;
    }
}
