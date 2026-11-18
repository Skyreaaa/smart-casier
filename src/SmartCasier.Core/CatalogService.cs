using Microsoft.Data.Sqlite;

namespace SmartCasier.Core;

public sealed record CatalogItem(
    string Sku, string ProductName, string VariantLabel, string UnitCode,
    long UnitPriceIdr, long OnHandMilli, string StockStatus);

public sealed class CatalogService(PosDatabase database)
{
    /// <summary>Lookup barcode, SKU, nama produk, label, dan alias tanpa hardware.</summary>
    public IReadOnlyList<CatalogItem> Search(string query, int limit = 30)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        var needle = query.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        using var conn = database.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT i.sku,i.product_name,i.label,i.unit_code,
                   v.sale_price_idr,i.on_hand_milli,i.stock_status
            FROM v_inventory_status i
            JOIN variants v ON v.id=i.variant_id
            JOIN products p ON p.id=v.product_id
            WHERE v.is_active=1 AND p.is_active=1
              AND (i.sku LIKE @pattern ESCAPE '\' COLLATE NOCASE
                   OR i.product_name LIKE @pattern ESCAPE '\' COLLATE NOCASE
                   OR i.label LIKE @pattern ESCAPE '\' COLLATE NOCASE
                   OR EXISTS(SELECT 1 FROM barcodes b WHERE b.variant_id=v.id
                             AND b.code=@exact)
                   OR EXISTS(SELECT 1 FROM variant_aliases a WHERE a.variant_id=v.id
                             AND a.alias LIKE @pattern ESCAPE '\' COLLATE NOCASE))
            ORDER BY CASE WHEN i.sku=@exact COLLATE NOCASE THEN 0 ELSE 1 END,
                     i.product_name, i.sku
            LIMIT @limit;
            """;
        cmd.Parameters.AddWithValue("@pattern", "%" + needle + "%");
        cmd.Parameters.AddWithValue("@exact", query.Trim());
        cmd.Parameters.AddWithValue("@limit", limit);
        using var reader = cmd.ExecuteReader();
        var items = new List<CatalogItem>();
        while (reader.Read())
        {
            items.Add(new CatalogItem(
                reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetInt64(4), reader.GetInt64(5),
                reader.GetString(6)));
        }
        return items;
    }

    public IReadOnlyList<CatalogItem> Inventory()
    {
        using var conn = database.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT i.sku,i.product_name,i.label,i.unit_code,
                   v.sale_price_idr,i.on_hand_milli,i.stock_status
            FROM v_inventory_status i
            JOIN variants v ON v.id=i.variant_id
            JOIN products p ON p.id=v.product_id
            WHERE v.is_active=1 AND p.is_active=1 ORDER BY i.sku;
            """;
        using var reader = cmd.ExecuteReader();
        var items = new List<CatalogItem>();
        while (reader.Read())
            items.Add(new CatalogItem(reader.GetString(0),reader.GetString(1),
                reader.GetString(2),reader.GetString(3),reader.GetInt64(4),
                reader.GetInt64(5),reader.GetString(6)));
        return items;
    }
}
