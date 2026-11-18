using Microsoft.Data.Sqlite;
using SmartCasier.Core;

var pass=0;
var fail=0;
void Test(string name, Action work)
{
    try { work(); pass++; Console.WriteLine("PASS " + name); }
    catch(Exception error) { fail++; Console.Error.WriteLine("FAIL " + name + ": " + error); }
}
static void Assert(bool value, string reason)
{
    if (!value) throw new Exception(reason);
}
static void Throws<T>(Action action) where T:Exception
{
    try { action(); }
    catch (T) { return; }
    throw new Exception("Expected exception: " + typeof(T).Name);
}
static void WithDemo(Action<PosDatabase,string> work)
{
    var folder = Path.Combine(Path.GetTempPath(), "sc-safety-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    try
    {
        var db = new PosDatabase(Path.Combine(folder,"active.db"));
        db.Initialize();
        DemoSeeder.Seed(db);
        work(db,folder);
    }
    finally
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(folder,true);
    }
}
static long Scalar(PosDatabase db,string sql)
{
    using var conn=db.Open();
    using var cmd=conn.CreateCommand();
    cmd.CommandText=sql;
    return Convert.ToInt64(cmd.ExecuteScalar());
}
static void Execute(PosDatabase db,string sql)
{
    using var conn=db.Open();
    using var cmd=conn.CreateCommand();
    cmd.CommandText=sql;
    cmd.ExecuteNonQuery();
}
static Guid Sale(PosDatabase db,string sku="BAUT-M8X30")
{
    var id=Guid.NewGuid();
    new SalesService(db).Checkout(id,[new(sku,1000)],50000);
    return id;
}

Test("empty sale history returns empty collection",()=>WithDemo((db,_) =>
{
    Assert(new SalesHistoryService(db).Search().Count==0,"Empty database returned sales");
}));

Test("sale history ordered by newest row and has snapshot totals",()=>WithDemo((db,_) =>
{
    var first=Sale(db);
    var second=Sale(db,"BAUT-M8X35");
    var rows=new SalesHistoryService(db).Search();
    Assert(rows.Count==2,"Two sales expected");
    Assert(rows[0].SaleId==second && rows[1].SaleId==first,"Unexpected sale order");
    Assert(rows[0].TotalIdr==1400 && rows[0].LineCount==1,"Wrong summary");
    Assert(rows[0].PaidIdr==50000 && rows[0].ChangeIdr==48600,"Wrong cash details");
}));

Test("search by receipt SKU and product name",()=>WithDemo((db,_) =>
{
    var id=Sale(db);
    var service=new SalesHistoryService(db);
    var receipt=service.Search()[0].ReceiptNo;
    Assert(service.Search(receipt).Single().SaleId==id,"Receipt lookup failed");
    Assert(service.Search("BAUT-M8X30").Single().SaleId==id,"SKU lookup failed");
    Assert(service.Search("Baut segi enam").Single().SaleId==id,"Name lookup failed");
    Assert(service.Search("NOT-FOUND").Count==0,"Search yielded unrelated sale");
}));

Test("history query treats percent underscore literally",()=>WithDemo((db,_) =>
{
    Sale(db);
    var svc=new SalesHistoryService(db);
    Assert(svc.Search("%").Count==0,"Wildcard should be escaped");
    Assert(svc.Search("_").Count==0,"Wildcard should be escaped");
}));

Test("pagination and invalid range",()=>WithDemo((db,_) =>
{
    Sale(db);
    Sale(db,"BAUT-M8X35");
    var svc=new SalesHistoryService(db);
    Assert(svc.Search(limit:1,offset:1).Count==1,"Page two not returned");
    Assert(svc.Search(limit:1,offset:2).Count==0,"Expected end of list");
    Throws<ArgumentOutOfRangeException>(()=>svc.Search(limit:0));
    Throws<ArgumentOutOfRangeException>(()=>svc.Search(limit:201));
    Throws<ArgumentOutOfRangeException>(()=>svc.Search(offset:-1));
    Throws<ArgumentException>(()=>svc.Search(new string('a',121)));
}));

Test("saved receipt price snapshot survives price change",()=>WithDemo((db,_) =>
{
    var saleId=Sale(db);
    new ProductCatalogEditor(db).UpdatePrice("BAUT-M8X30",9000);
    var receipt=new SalesHistoryService(db).Receipt(saleId);
    Assert(receipt is not null && receipt.Lines[0].PriceIdr==1200,"Historical price was altered");
}));

Test("online backup includes committed receipt while WAL active",()=>WithDemo((db,folder) =>
{
    Sale(db);
    var safety=new DatabaseSafetyService(db);
    var backup=Path.Combine(folder,"backup-before.db");
    var info=safety.BackupToNewFile(backup);
    Assert(info.SizeBytes>0 && info.Sha256.Length==64,"Backup metadata missing");
    DatabaseSafetyService.ValidateBackup(backup);
    var snapshot=new PosDatabase(backup);
    Assert(Scalar(snapshot,"SELECT COUNT(*) FROM sales;")==1,"Backup missing committed transaction");
    Assert(new CatalogService(snapshot).Search("BAUT-M8X30")[0].OnHandMilli==2000,
        "Backup is missing stock movement");
}));

Test("backup to pre-existing path refuses to overwrite",()=>WithDemo((db,folder) =>
{
    var destination=Path.Combine(folder,"existing.db");
    File.WriteAllText(destination,"important existing user data");
    Throws<IOException>(()=>new DatabaseSafetyService(db).BackupToNewFile(destination));
    Assert(File.ReadAllText(destination)=="important existing user data","Destination overwritten");
}));

Test("restore generates separate database preserving original",()=>WithDemo((db,folder) =>
{
    Sale(db);
    var safety=new DatabaseSafetyService(db);
    var source=Path.Combine(folder,"history-backup.db");
    safety.BackupToNewFile(source);
    Sale(db,"BAUT-M8X35"); // after backup
    var recovered=Path.Combine(folder,"restored.db");
    safety.RestoreToNewFile(source,recovered);
    var restored=new PosDatabase(recovered);
    Assert(Scalar(restored,"SELECT COUNT(*) FROM sales;")==1,
        "Restored DB includes later live transaction");
    Assert(Scalar(db,"SELECT COUNT(*) FROM sales;")==2,
        "Restoration modified active database");
}));

Test("restore to existing path is rejected without damage",()=>WithDemo((db,folder) =>
{
    var safety=new DatabaseSafetyService(db);
    var backup=Path.Combine(folder,"safe.db");
    safety.BackupToNewFile(backup);
    var destination=Path.Combine(folder,"in-use.db");
    File.WriteAllText(destination,"preserve this");
    Throws<IOException>(()=>safety.RestoreToNewFile(backup,destination));
    Assert(File.ReadAllText(destination)=="preserve this","Restore overwrote destination");
}));

Test("restore rejects corrupt input before creating output",()=>WithDemo((db,folder) =>
{
    var corrupt=Path.Combine(folder,"fake.db");
    File.WriteAllText(corrupt,"not SQLite");
    var restored=Path.Combine(folder,"should-not-exist.db");
    Throws<SqliteException>(()=>new DatabaseSafetyService(db)
        .RestoreToNewFile(corrupt,restored));
    Assert(!File.Exists(restored),"Corrupt input created an output");
}));

Test("restore rejects staging schema v2",()=>WithDemo((db,folder) =>
{
    var staging=Path.Combine(folder,"stage.db");
    using(var connection=new SqliteConnection("Data Source="+staging))
    {
        connection.Open();
        using var command=connection.CreateCommand();
        command.CommandText="PRAGMA user_version=2;";
        command.ExecuteNonQuery();
    }
    Throws<InvalidDataException>(()=>DatabaseSafetyService.ValidateBackup(staging));
    Assert(Scalar(db,"SELECT COUNT(*) FROM sales;")==0,"Active POS changed");
}));

Test("fake database with user_version 3 but missing POS tables rejected",()=>WithDemo((db,folder) =>
{
    var invalid=Path.Combine(folder,"fake-v3.db");
    using(var conn=new SqliteConnection("Data Source="+invalid))
    {
        conn.Open();
        using var cmd=conn.CreateCommand();
        cmd.CommandText="PRAGMA user_version=3;";
        cmd.ExecuteNonQuery();
    }
    Throws<InvalidDataException>(()=>DatabaseSafetyService.ValidateBackup(invalid));
}));

Test("cannot backup to the active database path",()=>WithDemo((db,folder) =>
{
    var active=Path.Combine(folder,"active.db");
    Throws<InvalidOperationException>(()=>new DatabaseSafetyService(db).BackupToNewFile(active));
}));

Console.WriteLine("SAFETY TESTS: " + pass + " passed / " + fail + " failed");
if (fail>0) Environment.ExitCode=1;
