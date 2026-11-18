using Microsoft.Data.Sqlite;
using System.Text.RegularExpressions;

namespace SmartCasier.Core;

/// <summary>
/// Pengelolaan master produk untuk mode offline. Tidak mengimpor data eksternal
/// yang lisensi/identitasnya belum diverifikasi.
/// </summary>
public sealed record NewSku(
    string CategoryCode, string CategoryName,
    string ProductKey, string ProductName,
    string Sku, string VariantLabel, string UnitCode,
    long SalePriceIdr, long OpeningMilli, long ThresholdMilli,
    string? Barcode = null);

public sealed class ProductCatalogEditor(PosDatabase database)
{
    private static readonly Regex KeyPattern = new(
        @"^[A-Z0-9][A-Z0-9._-]{1,63}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static string CleanKey(string value, string label)
    {
        if (value is null) throw new ArgumentNullException(label);
        var key = value.Trim().ToUpperInvariant();
        if (!KeyPattern.IsMatch(key))
            throw new ArgumentException(label + " harus 2–64 karakter: A–Z, 0–9, titik, _ atau -.");
        return key;
    }

    private static string CleanName(string value, string label)
    {
        var text = value?.Trim() ?? "";
        if (text.Length is < 1 or > 180)
            throw new ArgumentException(label + " harus 1–180 karakter.");
        return text;
    }

    private static void ValidateUnitQuantity(string unit, long quantity, string label)
    {
        if (quantity < 0) throw new ArgumentOutOfRangeException(label);
        if (unit == "PCS" && quantity % 1000 != 0)
            throw new ArgumentException(label + " untuk PCS harus bilangan bulat.");
    }

    /// <summary>Menciptakan kategori/keluarga bila belum tersedia dan SKU jual baru, atomik.</summary>
    public string Create(NewSku request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var categoryCode = CleanKey(request.CategoryCode, "Kode kategori");
        var categoryName = CleanName(request.CategoryName, "Nama kategori");
        var productKey = CleanKey(request.ProductKey, "Kode keluarga");
        var productName = CleanName(request.ProductName, "Nama produk");
        var sku = CleanKey(request.Sku, "SKU");
        var label = request.VariantLabel?.Trim() ?? "";
        if (label.Length > 180)
            throw new ArgumentException("Nama varian maksimal 180 karakter.");
        var unit = (request.UnitCode ?? "").Trim().ToUpperInvariant();
        if (unit is not ("PCS" or "M"))
            throw new ArgumentException("Hanya satuan PCS dan M yang tersedia pada editor awal.");
        if (request.SalePriceIdr < 0)
            throw new ArgumentOutOfRangeException(nameof(request.SalePriceIdr));
        ValidateUnitQuantity(unit, request.OpeningMilli, "Stok awal");
        ValidateUnitQuantity(unit, request.ThresholdMilli, "Ambang stok");
        var barcode = request.Barcode?.Trim() ?? "";
        if (barcode.Length > 100 || barcode.Any(char.IsWhiteSpace))
            throw new ArgumentException("Barcode maksimal 100 karakter tanpa spasi.");

        using var conn = database.Open();
        using var tx = conn.BeginTransaction();

        string categoryId;
        using (var cmd = Command(conn, tx,
            "SELECT id,name FROM categories WHERE code=@code COLLATE NOCASE;",
            ("@code", categoryCode)))
        using (var reader = cmd.ExecuteReader())
        {
            if (reader.Read())
            {
                categoryId = reader.GetString(0);
                if (!string.Equals(reader.GetString(1), categoryName,
                    StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Nama kategori berbeda dari kode kategori yang sudah ada.");
            }
            else categoryId = "";
        }
        if (categoryId.Length == 0)
        {
            categoryId = Guid.NewGuid().ToString("N");
            Execute(conn, tx,
                "INSERT INTO categories(id,code,name) VALUES(@id,@code,@name);",
                ("@id", categoryId), ("@code", categoryCode), ("@name", categoryName));
        }

        string productId;
        using (var cmd = Command(conn, tx,
            "SELECT id,name,category_id,product_type FROM products WHERE product_key=@key COLLATE NOCASE;",
            ("@key", productKey)))
        using (var reader = cmd.ExecuteReader())
        {
            if (reader.Read())
            {
                productId = reader.GetString(0);
                if (!string.Equals(reader.GetString(1), productName,
                        StringComparison.OrdinalIgnoreCase) ||
                    reader.GetString(2) != categoryId ||
                    reader.GetString(3) != "PHYSICAL")
                    throw new InvalidOperationException(
                        "Kode keluarga sudah ada tetapi nama/kategori/tipenya berbeda.");
            }
            else productId = "";
        }
        if (productId.Length == 0)
        {
            productId = Guid.NewGuid().ToString("N");
            Execute(conn, tx,
                "INSERT INTO products(id,product_key,category_id,name,product_type) " +
                "VALUES(@id,@key,@category,@name,'PHYSICAL');",
                ("@id", productId), ("@key", productKey),
                ("@category", categoryId), ("@name", productName));
        }

        var variantId = Guid.NewGuid().ToString("N");
        Execute(conn, tx, """
            INSERT INTO variants(id,product_id,sku,label,unit_code,sale_price_idr,
                                 track_stock,low_stock_threshold_milli,attributes_json)
            VALUES(@id,@product,@sku,@label,@unit,@price,1,@threshold,'{}');
            """,
            ("@id", variantId), ("@product", productId), ("@sku", sku),
            ("@label", label), ("@unit", unit), ("@price", request.SalePriceIdr),
            ("@threshold", request.ThresholdMilli));

        if (!string.IsNullOrEmpty(barcode))
            Execute(conn, tx, "INSERT INTO barcodes(code,variant_id) VALUES(@code,@variant);",
                ("@code", barcode), ("@variant", variantId));
        if (request.OpeningMilli > 0)
            Execute(conn, tx, """
                INSERT INTO stock_movements(id,variant_id,movement_type,delta_qty_milli,note)
                VALUES(@id,@variant,'OPENING',@qty,'Manual catalog entry');
                """,
                ("@id", Guid.NewGuid().ToString("N")),
                ("@variant", variantId), ("@qty", request.OpeningMilli));
        tx.Commit();
        return sku;
    }

    /// <summary>Perubahan harga tidak mengubah snapshot harga di histori penjualan.</summary>
    public void UpdatePrice(string sku, long newPriceIdr)
    {
        if (newPriceIdr < 0) throw new ArgumentOutOfRangeException(nameof(newPriceIdr));
        var normalized = CleanKey(sku, "SKU");
        using var conn = database.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE variants SET sale_price_idr=@price WHERE sku=@sku COLLATE NOCASE AND is_active=1;";
        cmd.Parameters.AddWithValue("@price", newPriceIdr);
        cmd.Parameters.AddWithValue("@sku", normalized);
        if (cmd.ExecuteNonQuery() != 1)
            throw new KeyNotFoundException("SKU tidak ditemukan atau nonaktif.");
    }

    /// <summary>Mutasi stok bukan edit saldo langsung; alasan wajib tercatat di ledger.</summary>
    public void AdjustStock(string sku, long deltaMilli, string reason)
    {
        var normalized = CleanKey(sku, "SKU");
        var note = CleanName(reason, "Alasan koreksi");
        if (deltaMilli == 0) throw new ArgumentException("Perubahan stok tidak boleh nol.");
        using var conn = database.Open();
        using var tx = conn.BeginTransaction();
        using var cmd = Command(conn, tx, """
            SELECT v.id,v.unit_code,v.track_stock
            FROM variants v WHERE v.sku=@sku COLLATE NOCASE AND v.is_active=1;
            """, ("@sku", normalized));
        string id, unit;
        using (var reader = cmd.ExecuteReader())
        {
            if (!reader.Read()) throw new KeyNotFoundException("SKU tidak ditemukan atau nonaktif.");
            id = reader.GetString(0);
            unit = reader.GetString(1);
            if (reader.GetInt64(2) != 1)
                throw new NotSupportedException("SKU ini tidak melacak stok.");
        }
        if (unit == "PCS" && deltaMilli % 1000 != 0)
            throw new ArgumentException("Koreksi PCS harus bilangan bulat.");
        Execute(conn, tx, """
            INSERT INTO stock_movements(id,variant_id,movement_type,delta_qty_milli,note)
            VALUES(@id,@variant,'ADJUSTMENT',@delta,@reason);
            """,
            ("@id", Guid.NewGuid().ToString("N")),
            ("@variant", id), ("@delta", deltaMilli), ("@reason", note));
        tx.Commit();
    }

    private static SqliteCommand Command(SqliteConnection conn, SqliteTransaction tx,
        string sql, params (string Name, object Value)[] parameters)
    {
        var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        return cmd;
    }

    private static void Execute(SqliteConnection conn, SqliteTransaction tx,
        string sql, params (string Name, object Value)[] parameters)
    {
        using var cmd = Command(conn, tx, sql, parameters);
        cmd.ExecuteNonQuery();
    }
}
