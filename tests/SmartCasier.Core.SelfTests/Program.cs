using SmartCasier.Core;
using Microsoft.Data.Sqlite;

int passed=0,failed=0;
void Run(string name,Action body)
{
    try{body();passed++;Console.WriteLine("PASS "+name);}
    catch(Exception e){failed++;Console.Error.WriteLine("FAIL "+name+": "+e);}
}
static void Assert(bool yes,string message)
{
    if(!yes)throw new Exception(message);
}
static void Throws<T>(Action f)where T:Exception
{
    try{f();}catch(T){return;}
    throw new Exception("Expected "+typeof(T).Name);
}
static void Demo(Action<PosDatabase> work)
{
    string folder=Path.Combine(Path.GetTempPath(),"sc-test-"+Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    var path=Path.Combine(folder,"demo.db");
    try{var db=new PosDatabase(path);db.Initialize();DemoSeeder.Seed(db);work(db);}
    finally{SqliteConnection.ClearAllPools();Directory.Delete(folder,true);}
}
static long Query(PosDatabase db,string query)
{
    using var c=db.Open();using var cmd=c.CreateCommand();
    cmd.CommandText=query;return Convert.ToInt64(cmd.ExecuteScalar());
}
static void Exec(PosDatabase db,string query)
{
    using var c=db.Open();using var cmd=c.CreateCommand();
    cmd.CommandText=query;cmd.ExecuteNonQuery();
}
Run("migration creates operational schema v5",()=>Demo(db=>
{
    Assert(Query(db,"PRAGMA user_version;")==5,"Incorrect schema version");
    Assert(Query(db,"PRAGMA foreign_keys;")==1,"Foreign keys disabled");
    db.Initialize();
}));
Run("demo seed is safe to rerun",()=>Demo(db=>
{
    long before=Query(db,"SELECT COUNT(*) FROM stock_movements;");
    DemoSeeder.Seed(db);
    Assert(Query(db,"SELECT COUNT(*) FROM stock_movements;")==before,"Duplicated opening stock");
}));
Run("SKU, alias and family lookup",()=>Demo(db=>
{
    var c=new CatalogService(db);
    Assert(c.Search("BAUT-M8X30").Count==1,"SKU lookup failed");
    Assert(c.Search("baut 8 panjang 30").Count==1,"Alias lookup failed");
    Assert(c.Search("kertas minyak").Count==2,"Family lookup failed");
    Assert(c.Search("%").Count==0,"LIKE wildcard leaked");
}));
Run("stock threshold below 3 not at 3",()=>Demo(db=>
{
    var c=new CatalogService(db);
    Assert(c.Search("BAUT-M8X30")[0].StockStatus=="OK","3 pcs should be OK");
    Assert(c.Search("BAUT-M8X35")[0].StockStatus=="LOW","2 pcs should be LOW");
}));
Run("checkout saves receipt, cash and stock",()=>Demo(db=>
{
    var receipt=new TestSales(db).Checkout(Guid.NewGuid(),[new("BAUT-M8X30",1000)],5000);
    Assert(receipt.TotalIdr==1200&&receipt.ChangeIdr==3800,"Wrong total/change");
    Assert(receipt.Lines.Count==1&&receipt.Lines[0].PriceIdr==1200,"Bad receipt");
    Assert(new CatalogService(db).Search("BAUT-M8X30")[0].OnHandMilli==2000,"Stock did not decrease");
}));
Run("replay same request cannot double-charge",()=>Demo(db=>
{
    var id=Guid.NewGuid();
    var service=new TestSales(db);
    service.Checkout(id,[new("BAUT-M8X30",1000)],5000);
    var replay=service.Checkout(id,[new("baut-m8x30",1000)],5000);
    Assert(replay.AlreadyProcessed,"Replay not detected");
    Assert(Query(db,"SELECT COUNT(*) FROM sales;")==1,"Duplicate sale");
    Assert(new CatalogService(db).Search("BAUT-M8X30")[0].OnHandMilli==2000,"Duplicate stock decrement");
    Throws<InvalidOperationException>(()=>service.Checkout(id,[new("BAUT-M8X30",2000)],5000));
}));
Run("insufficient cash rolls back",()=>Demo(db=>
{
    Throws<InvalidOperationException>(()=>new TestSales(db).Checkout(
        Guid.NewGuid(),[new("BAUT-M8X30",1000)],0));
    Assert(Query(db,"SELECT COUNT(*) FROM sales;")==0,"Partial sale");
    Assert(new CatalogService(db).Search("BAUT-M8X30")[0].OnHandMilli==3000,"Stock changed");
}));
Run("insufficient stock rolls back",()=>Demo(db=>
{
    Throws<InvalidOperationException>(()=>new TestSales(db).Checkout(
        Guid.NewGuid(),[new("BAUT-M8X30",4000)],20000));
    Assert(Query(db,"SELECT COUNT(*) FROM sales;")==0,"Partial sale");
}));
Run("no fractional pieces",()=>Demo(db=>
{
    Throws<ArgumentException>(()=>new TestSales(db).Checkout(
        Guid.NewGuid(),[new("BAUT-M8X30",500)],10000));
}));
Run("meter price exact and stock decreases",()=>Demo(db=>
{
    var receipt=new TestSales(db).Checkout(Guid.NewGuid(),[new("KMY-MERAH",2500)],15000);
    Assert(receipt.TotalIdr==12500,"Meter price mismatch");
    Assert(new CatalogService(db).Search("KMY-MERAH")[0].OnHandMilli==10000,"Meter stock mismatch");
}));
Run("multiple item failure is atomic",()=>Demo(db=>
{
    Throws<KeyNotFoundException>(()=>new TestSales(db).Checkout(Guid.NewGuid(),
        [new("BAUT-M8X30",1000),new("UNKNOWN",1000)],50000));
    Assert(Query(db,"SELECT COUNT(*) FROM sales;")==0,"Partial cart sale");
}));
Run("read saved receipt independent of price change",()=>Demo(db=>
{
    var id=Guid.NewGuid();
    new TestSales(db).Checkout(id,[new("BAUT-M8X30",1000)],5000);
    Exec(db,"UPDATE variants SET sale_price_idr=9999 WHERE sku='BAUT-M8X30';");
    var receipt=new TestSales(db).FindReceipt(id);
    Assert(receipt is not null&&receipt.Lines[0].PriceIdr==1200,"Price snapshot changed");
}));
Run("SQLite stock trigger blocks negative balance",()=>Demo(db=>
{
    Throws<SqliteException>(()=>Exec(db,
        "INSERT INTO stock_movements(id,variant_id,movement_type,delta_qty_milli) "+
        "VALUES('bad','BAUT-M8X30','SALE',-999999);"));
}));
Run("expected cart total detects stale product price",()=>Demo(db=>
{
    var id=Guid.NewGuid();
    var s=new TestSales(db);
    Throws<InvalidOperationException>(()=>s.Checkout(
        id,[new("BAUT-M8X30",1000)],5000,expectedTotalIdr:999));
    Assert(Query(db,"SELECT COUNT(*) FROM sales;")==0,"Stale cart was charged");
    Assert(new CatalogService(db).Search("BAUT-M8X30")[0].OnHandMilli==3000,
        "Stock changed on stale cart");
}));
Console.WriteLine("TEST SUMMARY: "+passed+" passed, "+failed+" failed");
if(failed>0)Environment.ExitCode=1;

sealed class TestSales(PosDatabase database)
{
    private readonly SalesService service = new(database);
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PosDatabase, AuthSession> Sessions = new();
    private AuthSession Operator => Sessions.GetValue(database, db =>
    {
        var auth = new AuthService(db);
        var pass = Guid.NewGuid().ToString("N") + "Aa!";
        auth.CreateFirstAdmin("testoperator", pass);
        return auth.Login("testoperator", pass);
    });
    public SaleReceipt Checkout(Guid id, IEnumerable<SaleLineRequest> lines, long paid, long? expectedTotalIdr = null)
        => service.Checkout(id, lines, paid, Operator, expectedTotalIdr);
    public SaleReceipt? FindReceipt(Guid id) => service.FindReceipt(id, Operator);
}
