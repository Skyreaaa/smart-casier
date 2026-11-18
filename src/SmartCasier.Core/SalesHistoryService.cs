using Microsoft.Data.Sqlite;

namespace SmartCasier.Core;

/// <summary>Ringkasan receipt read-only untuk audit penjualan lokal.</summary>
public sealed record SaleHistoryRow(
    Guid SaleId, string ReceiptNo, string CreatedUtc, long TotalIdr,
    long PaidIdr, long ChangeIdr, int LineCount);

/// <summary>
/// Query histori langsung dari SQLite, tanpa mengubah receipt lama atau saldo stok.
/// Waktu dalam kolom created_at dicatat oleh SQLite sebagai UTC.
/// </summary>
public sealed class SalesHistoryService(PosDatabase database)
{
    public IReadOnlyList<SaleHistoryRow> Search(
        string? searchTerm = null, int limit = 100, int offset = 0)
    {
        if (limit is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit harus 1-200.");
        if (offset is < 0 or > 100_000)
            throw new ArgumentOutOfRangeException(nameof(offset));
        var search = (searchTerm ?? "").Trim();
        if (search.Length > 120)
            throw new ArgumentException("Pencarian maksimal 120 karakter.", nameof(searchTerm));

        var pattern = "%" + search.Replace("\\", "\\\\")
            .Replace("%", "\\%").Replace("_", "\\_") + "%";

        using var conn = database.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT s.sale_id, s.receipt_no, s.created_at,
                   s.total_idr, s.paid_idr, s.change_idr,
                   COUNT(l.line_id) AS line_count
            FROM sales s
            LEFT JOIN sale_lines l ON l.sale_id=s.sale_id
            WHERE @empty=1 OR
                  s.receipt_no LIKE @pattern ESCAPE '\' COLLATE NOCASE OR
                  EXISTS (
                      SELECT 1 FROM sale_lines f
                      WHERE f.sale_id=s.sale_id
                        AND (f.sku_snapshot LIKE @pattern ESCAPE '\' COLLATE NOCASE
                          OR f.product_snapshot LIKE @pattern ESCAPE '\' COLLATE NOCASE)
                  )
            GROUP BY s.sale_id
            ORDER BY s.created_at DESC, s.rowid DESC
            LIMIT @limit OFFSET @offset;
            """;
        cmd.Parameters.AddWithValue("@empty", search.Length == 0 ? 1 : 0);
        cmd.Parameters.AddWithValue("@pattern", pattern);
        cmd.Parameters.AddWithValue("@limit", limit);
        cmd.Parameters.AddWithValue("@offset", offset);
        using var reader = cmd.ExecuteReader();
        var results = new List<SaleHistoryRow>();
        while (reader.Read())
            results.Add(new SaleHistoryRow(
                Guid.ParseExact(reader.GetString(0), "N"),
                reader.GetString(1), reader.GetString(2),
                reader.GetInt64(3), reader.GetInt64(4),
                reader.GetInt64(5), reader.GetInt32(6)));
        return results;
    }

    /// <summary>Detail receipt memakai snapshot tersimpan; tidak mengambil harga saat ini.</summary>
    public SaleReceipt? Receipt(Guid saleId) => new SalesService(database).FindReceipt(saleId);
}
