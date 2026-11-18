using SmartCasier.Core;
using Microsoft.Data.Sqlite;

static void Database(Action<PosDatabase> run)
{
    var folder = Path.Combine(Path.GetTempPath(),"sc-editor-test-"+Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    try
    {
        var db=new PosDatabase(Path.Combine(folder,"demo.db"));
        db.Initialize();
        run(db);
    }
    finally { SqliteConnection.ClearAllPools(); Directory.Delete(folder,true); }
}

static void Assert(bool condition,string text)
{
    if(!condition)throw new Exception(text);
}

static void Throws<T>(Action action) where T:Exception
{
    try {action();}
    catch(T){return;}
    throw new Exception("Expected "+typeof(T).Name);
}

var passed=0;var failed=0;
void Test(string description,Action action)
{
    try{action();passed++;Console.WriteLine("PASS "+description);}
    catch(Exception e){failed++;Console.Error.WriteLine("FAIL "+description+" "+e);}
}

static NewSku Product(string sku="BOLT-M8X30",long initial=3000) =>
    new("BOLT","Baut dan Mur","BOLT-M8","Baut kepala segi enam",
        sku,"M8 x 30","PCS",1200,initial,3000,"4006381333931");

Test("create SKU and opening stock",()=>Database(db=>
{
    var editor=new ProductCatalogEditor(db);
    editor.Create(Product());
    var item=new CatalogService(db).Search("BOLT-M8X30").Single();
    Assert(item.OnHandMilli==3000 && item.UnitPriceIdr==1200,"New SKU details wrong");
    Assert(new CatalogService(db).Search("4006381333931").Count==1,"Barcode lookup wrong");
}));
Test("duplicate SKU rollback does not duplicate opening stock",()=>Database(db=>
{
    var ed=new ProductCatalogEditor(db);
    ed.Create(Product());
    Throws<SqliteException>(()=>ed.Create(Product()));
    Assert(new CatalogService(db).Search("BOLT-M8X30")[0].OnHandMilli==3000,
        "Opening stock duplicated");
}));
Test("new variant reuses product family and category",()=>Database(db=>
{
    var ed=new ProductCatalogEditor(db);
    ed.Create(Product());
    ed.Create(Product("BOLT-M8X35",2000) with { Barcode = "" });
    Assert(new CatalogService(db).Search("baut").Count==2,"Family search should return 2");
}));
Test("invalid fractional PCS rejected",()=>Database(db=>
{
    var ed=new ProductCatalogEditor(db);
    Throws<ArgumentException>(()=>ed.Create(Product() with { OpeningMilli=1500 }));
    Assert(new CatalogService(db).Inventory().Count==0,"Partial product created");
}));
Test("meter SKU accepts fractional length",()=>Database(db=>
{
    var ed=new ProductCatalogEditor(db);
    ed.Create(new("ROLL","Material gulungan","KMY","Kertas minyak",
        "KMY-HIJAU","Hijau","M",5000,2550,3000,""));
    var item=new CatalogService(db).Search("KMY-HIJAU").Single();
    Assert(item.OnHandMilli==2550&&item.StockStatus=="LOW","Meter quantity incorrect");
}));
Test("stock adjustment creates ledger entries",()=>Database(db=>
{
    var ed=new ProductCatalogEditor(db);
    ed.Create(Product());
    ed.AdjustStock("BOLT-M8X30",-1000,"Opname gudang");
    Assert(new CatalogService(db).Search("BOLT-M8X30")[0].OnHandMilli==2000,
        "Adjustment not recorded");
}));
Test("negative stock adjustment rejected",()=>Database(db=>
{
    var ed=new ProductCatalogEditor(db);
    ed.Create(Product());
    Throws<SqliteException>(()=>ed.AdjustStock("BOLT-M8X30",-4000,"Koreksi"));
    Assert(new CatalogService(db).Search("BOLT-M8X30")[0].OnHandMilli==3000,
        "Stock changed after failure");
}));
Test("update price preserves sale snapshot",()=>Database(db=>
{
    var ed=new ProductCatalogEditor(db);
    ed.Create(Product());
    var id=Guid.NewGuid();
    new SalesService(db).Checkout(id,[new("BOLT-M8X30",1000)],5000);
    ed.UpdatePrice("BOLT-M8X30",1900);
    Assert(new CatalogService(db).Search("BOLT-M8X30")[0].UnitPriceIdr==1900,
        "Catalog not updated");
    Assert(new SalesService(db).FindReceipt(id)!.Lines[0].PriceIdr==1200,
        "Historical sale price changed");
}));
Test("bad product key does not create records",()=>Database(db=>
{
    var ed=new ProductCatalogEditor(db);
    Throws<ArgumentException>(()=>ed.Create(Product() with { Sku="../bad" }));
    Assert(new CatalogService(db).Inventory().Count==0,"Invalid SKU persisted");
}));
Test("reason is mandatory for adjustment",()=>Database(db=>
{
    var ed=new ProductCatalogEditor(db);
    ed.Create(Product());
    Throws<ArgumentException>(()=>ed.AdjustStock("BOLT-M8X30",1000," "));
}));
Test("no fractional inventory correction for PCS",()=>Database(db=>
{
    var ed=new ProductCatalogEditor(db);
    ed.Create(Product());
    Throws<ArgumentException>(()=>ed.AdjustStock("BOLT-M8X30",500,"Opname"));
}));
Console.WriteLine($"EDITOR TESTS: {passed} passed, {failed} failed");
if(failed>0)Environment.ExitCode=1;
