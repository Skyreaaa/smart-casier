using Microsoft.Data.Sqlite;

namespace SmartCasier.Core;

/// <summary>Fixtures DEMO ONLY: bukan harga atau stok toko yang sesungguhnya.</summary>
public static class DemoSeeder
{
    public static void Seed(PosDatabase db)
    {
        using var conn=db.Open();
        using var tx=conn.BeginTransaction();
        string[][] items =
        [
            ["CAT-BOLT","BAUT","Baut dan mur","P-BOLT","BAUT-M8","Baut segi enam","BAUT-M8X30","M8x30","PCS","1200","3000","3000"],
            ["CAT-BOLT","BAUT","Baut dan mur","P-BOLT","BAUT-M8","Baut segi enam","BAUT-M8X35","M8x35","PCS","1400","2000","3000"],
            ["CAT-ROLL","ROLL","Material lembaran","P-ROLL","KMY","Kertas minyak","KMY-MERAH","Merah","M","5000","12500","3000"],
            ["CAT-ROLL","ROLL","Material lembaran","P-ROLL","KMY","Kertas minyak","KMY-BIRU","Biru","M","5000","1500","3000"]
        ];
        foreach(var row in items)
        {
            Execute(conn,tx,"INSERT OR IGNORE INTO categories(id,code,name) VALUES(@id,@code,@name);",
                ("@id",row[0]),("@code",row[1]),("@name",row[2]));
            Execute(conn,tx,"INSERT OR IGNORE INTO products(id,product_key,category_id,name,product_type) " +
                "VALUES(@id,@key,@category,@name,'PHYSICAL');",
                ("@id",row[3]),("@key",row[4]),("@category",row[0]),("@name",row[5]));
            Execute(conn,tx,"""
                INSERT OR IGNORE INTO variants(id,product_id,sku,label,unit_code,
                  sale_price_idr,track_stock,low_stock_threshold_milli,attributes_json)
                VALUES(@id,@product,@sku,@label,@unit,@price,1,@low,'{}');
                """,
                ("@id",row[6]),("@product",row[3]),("@sku",row[6]),
                ("@label",row[7]),("@unit",row[8]),("@price",long.Parse(row[9])),
                ("@low",long.Parse(row[11])));
            if (!Exists(conn,tx,"SELECT 1 FROM stock_movements WHERE variant_id=@id AND movement_type='OPENING';",
                    ("@id",row[6])))
            {
                Execute(conn,tx,"INSERT INTO stock_movements(id,variant_id,movement_type,delta_qty_milli,note) " +
                    "VALUES(@move,@variant,'OPENING',@quantity,'SYNTHETIC DEMO');",
                    ("@move",Guid.NewGuid().ToString("N")),("@variant",row[6]),
                    ("@quantity",long.Parse(row[10])));
            }
        }
        Execute(conn,tx,"INSERT OR IGNORE INTO variant_aliases(id,variant_id,alias) " +
            "VALUES('ALIAS-DEMO-M8X30','BAUT-M8X30','baut 8 panjang 30');");
        tx.Commit();
    }

    private static bool Exists(SqliteConnection connection,SqliteTransaction tx,
        string sql,params (string Name,object Value)[] args)
    {
        using var command=connection.CreateCommand();
        command.Transaction=tx;
        command.CommandText=sql;
        foreach(var (name,value) in args)command.Parameters.AddWithValue(name,value);
        return command.ExecuteScalar() is not null;
    }

    private static void Execute(SqliteConnection connection,SqliteTransaction tx,
        string sql,params (string Name,object Value)[] args)
    {
        using var command=connection.CreateCommand();
        command.Transaction=tx;
        command.CommandText=sql;
        foreach(var (name,value) in args)command.Parameters.AddWithValue(name,value);
        command.ExecuteNonQuery();
    }
}
