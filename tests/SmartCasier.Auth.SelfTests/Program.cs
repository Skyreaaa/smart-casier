using System.Security;
using Microsoft.Data.Sqlite;
using SmartCasier.Core;

int passed=0,failed=0;
void Run(string name, Action action)
{
    try { action(); passed++; Console.WriteLine("PASS "+name); }
    catch(Exception ex) { failed++; Console.Error.WriteLine("FAIL "+name+": "+ex); }
}
static void Assert(bool condition,string message)
{
    if(!condition) throw new Exception(message);
}
static void Throws<T>(Action work) where T:Exception
{
    try { work(); }
    catch(T) { return; }
    throw new Exception("Expected "+typeof(T).Name);
}
static void WithDatabase(Action<PosDatabase,AuthService> work)
{
    var folder=Path.Combine(Path.GetTempPath(),"smartcasier-auth-"+Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    try
    {
        var db=new PosDatabase(Path.Combine(folder,"auth-test.db"));
        db.Initialize();
        work(db,new AuthService(db));
    }
    finally
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(folder,true);
    }
}
static string NewPassword() => Guid.NewGuid().ToString("N")+"!aA";
static AuthSession Owner(AuthService auth)
{
    string secret=NewPassword();
    auth.CreateFirstAdmin("owneruser",secret);
    return auth.Login("owneruser",secret);
}
static long Number(PosDatabase db,string query)
{
    using var connection=db.Open();
    using var cmd=connection.CreateCommand();
    cmd.CommandText=query;
    return Convert.ToInt64(cmd.ExecuteScalar());
}

Run("first run has no credentials until setup",()=>WithDatabase((db,auth)=>
{
    Assert(!auth.HasUsers(),"New database already has accounts");
    Assert(Number(db,"PRAGMA user_version;")==6,"Schema not migrated to v6");
}));

Run("first admin bootstrap and login",()=>WithDatabase((db,auth)=>
{
    var owner=Owner(auth);
    Assert(auth.HasUsers(),"Account missing");
    Assert(owner.Role=="ADMIN"&&owner.IsAdmin,"Owner role incorrect");
    auth.RequireAdmin(owner);
    Assert(Number(db,"SELECT COUNT(*) FROM security_audit;")>=2,"Missing security audit");
}));

Run("second admin bootstrap cannot overwrite owner",()=>WithDatabase((db,auth)=>
{
    Owner(auth);
    Throws<SecurityException>(()=>auth.CreateFirstAdmin("newowner",NewPassword()));
    Assert(Number(db,"SELECT COUNT(*) FROM app_users;")==1,"Unauthorized user inserted");
}));

Run("short password and malformed username rejected",()=>WithDatabase((db,auth)=>
{
    Throws<ArgumentException>(()=>auth.CreateFirstAdmin("a","abc"));
    Throws<ArgumentException>(()=>auth.CreateFirstAdmin("owneruser","short"));
    Assert(!auth.HasUsers(),"Invalid account created");
}));

Run("unique case insensitive usernames",()=>WithDatabase((db,auth)=>
{
    var owner=Owner(auth);
    auth.CreateCashier(owner,"dailycashier",NewPassword());
    Throws<SqliteException>(()=>auth.CreateCashier(owner,"DAILYCASHIER",NewPassword()));
    Assert(Number(db,"SELECT COUNT(*) FROM app_users;")==2,"Duplicated username");
}));

Run("cashier login has no privileged role",()=>WithDatabase((db,auth)=>
{
    var admin=Owner(auth);
    string password=NewPassword();
    auth.CreateCashier(admin,"cashierone",password);
    var cashier=auth.Login("cashierone",password);
    Assert(cashier.Role=="CASHIER"&&!cashier.IsAdmin,"Wrong cashier role");
    Throws<SecurityException>(()=>auth.RequireAdmin(cashier));
}));

Run("wrong password fails and does not grant session",()=>WithDatabase((db,auth)=>
{
    string secret=NewPassword();
    auth.CreateFirstAdmin("owneruser",secret);
    Throws<SecurityException>(()=>auth.Login("owneruser",secret+"wrong"));
    Assert(Number(db,"SELECT failed_attempts FROM app_users;")==1,
        "Failed attempt not recorded");
    Assert(auth.Login("owneruser",secret).IsAdmin,"Correct credential refused");
    Assert(Number(db,"SELECT failed_attempts FROM app_users;")==0,
        "Success should reset failed attempts");
}));

Run("five failures lock even correct password temporarily",()=>WithDatabase((db,auth)=>
{
    var password=NewPassword();
    auth.CreateFirstAdmin("owneruser",password);
    for(int i=0;i<5;i++)
        Throws<SecurityException>(()=>auth.Login("owneruser","wrong-value"));
    Throws<SecurityException>(()=>auth.Login("owneruser",password));
    Assert(Number(db,"SELECT failed_attempts FROM app_users;")==5,
        "Lock counter incorrect");
}));

Run("password plaintext never stored in users table",()=>WithDatabase((db,auth)=>
{
    var secret=NewPassword();
    auth.CreateFirstAdmin("owneruser",secret);
    using var conn=db.Open();
    using var cmd=conn.CreateCommand();
    cmd.CommandText="SELECT salt,password_hash,hash_iterations FROM app_users LIMIT 1;";
    using var r=cmd.ExecuteReader();
    Assert(r.Read(),"Missing user");
    Assert(((byte[])r[0]).Length==32 && ((byte[])r[1]).Length==32,"Hash/salt length wrong");
    Assert(r.GetInt32(2)>=200000,"PBKDF2 work factor low");
    Assert(!System.Text.Encoding.UTF8.GetString((byte[])r[1]).Contains(secret),
        "Password plaintext detected in hash");
}));

Run("catalog creation denied without admin session",()=>WithDatabase((db,auth)=>
{
    var owner=Owner(auth);
    var editor=new ProductCatalogEditor(db);
    var sku=new NewSku("TOOLS","Perkakas","BOLT-FAMILY","Baut",
        "BOLT-25","25mm","PCS",1200,3000,3000);
    Throws<SecurityException>(()=>editor.Create(sku,null!));
    editor.Create(sku,owner);
    Assert(new CatalogService(db).Search("BOLT-25").Count==1,"Admin create failed");
}));

Run("cashier cannot edit prices or stock even directly via Core",()=>WithDatabase((db,auth)=>
{
    var owner=Owner(auth);
    var editor=new ProductCatalogEditor(db);
    editor.Create(new("TOOLS","Perkakas","BOLT-FAMILY","Baut",
        "BOLT-25","25mm","PCS",1200,3000,3000),owner);
    string secret=NewPassword();
    auth.CreateCashier(owner,"cashierone",secret);
    var cashier=auth.Login("cashierone",secret);
    Throws<SecurityException>(()=>editor.UpdatePrice("BOLT-25",1,cashier));
    Throws<SecurityException>(()=>editor.AdjustStock("BOLT-25",1000,"Ubah stok",cashier));
    Assert(new CatalogService(db).Search("BOLT-25")[0].UnitPriceIdr==1200,
        "Cashier changed price");
    Assert(new CatalogService(db).Search("BOLT-25")[0].OnHandMilli==3000,
        "Cashier changed stock");
}));

Run("cashier cannot make backup or restore",()=>WithDatabase((db,auth)=>
{
    var owner=Owner(auth);
    string folder;
    using (var open = db.Open())
        folder = Path.GetDirectoryName(Path.GetFullPath(
            new SqliteConnectionStringBuilder(open.ConnectionString).DataSource))!;
    var backup=Path.Combine(folder,"backup.db");
    var restore=Path.Combine(folder,"restore.db");
    var svc=new DatabaseSafetyService(db);
    string pass=NewPassword();
    auth.CreateCashier(owner,"cashierone",pass);
    var cashier=auth.Login("cashierone",pass);
    Throws<SecurityException>(()=>svc.BackupToNewFile(backup,cashier));
    Assert(!File.Exists(backup),"Cashier created backup");
    svc.BackupToNewFile(backup,owner);
    Throws<SecurityException>(()=>svc.RestoreToNewFile(backup,restore,cashier));
    Assert(!File.Exists(restore),"Cashier restored database");
}));

Run("session cannot cross different POS database",()=>
{
    var directory=Path.Combine(Path.GetTempPath(),"sc-cross-"+Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        var first=new PosDatabase(Path.Combine(directory,"a.db"));
        var second=new PosDatabase(Path.Combine(directory,"b.db"));
        first.Initialize();second.Initialize();
        var owner=Owner(new AuthService(first));
        Throws<SecurityException>(()=>new AuthService(second).RequireAdmin(owner));
    }
    finally { SqliteConnection.ClearAllPools(); Directory.Delete(directory,true); }
});

Run("deactivated admin loses privileged access",()=>WithDatabase((db,auth)=>
{
    var admin=Owner(auth);
    using(var conn=db.Open())
    using(var command=conn.CreateCommand())
    {
        command.CommandText="UPDATE app_users SET active=0 WHERE id=@id";
        command.Parameters.AddWithValue("@id",admin.UserId);
        command.ExecuteNonQuery();
    }
    Throws<SecurityException>(()=>auth.RequireAdmin(admin));
}));


Run("checkout requires active operator even when core called directly",()=>WithDatabase((db,auth)=>
{
    DemoSeeder.Seed(db);
    var sales = new SalesService(db);
    var id = Guid.NewGuid();
    Throws<SecurityException>(()=>sales.Checkout(id,[new("BAUT-M8X30",1000)],5000,null));
    Assert(Number(db,"SELECT COUNT(*) FROM sales;")==0,"Unauthorized checkout wrote sale");
    Assert(new CatalogService(db).Search("BAUT-M8X30")[0].OnHandMilli==3000,"Unauthorized stock change");
}));
Run("cashier checkout records operator identity on sale",()=>WithDatabase((db,auth)=>
{
    DemoSeeder.Seed(db);
    var admin=Owner(auth);
    var secret=NewPassword();
    auth.CreateCashier(admin,"cashierone",secret);
    var cashier=auth.Login("cashierone",secret);
    var sale=new SalesService(db).Checkout(Guid.NewGuid(),
        [new("BAUT-M8X30",1000)],5000,cashier);
    Assert(sale.TotalIdr==1200,"Cashier checkout failed");
    using var conn=db.Open();
    using var cmd=conn.CreateCommand();
    cmd.CommandText="SELECT operator_id FROM sales WHERE sale_id=@id;";
    cmd.Parameters.AddWithValue("@id",sale.SaleId.ToString("N"));
    Assert(string.Equals(cmd.ExecuteScalar() as string,cashier.UserId,StringComparison.Ordinal),
        "Receipt did not record cashier id");
}));
Run("receipt and history require authenticated operator",()=>WithDatabase((db,auth)=>
{
    DemoSeeder.Seed(db);
    var admin=Owner(auth);
    var sale=new SalesService(db).Checkout(Guid.NewGuid(),
        [new("BAUT-M8X30",1000)],5000,admin);
    Throws<SecurityException>(()=>new SalesService(db).FindReceipt(sale.SaleId,null));
    Throws<SecurityException>(()=>new SalesHistoryService(db).Search(null));
    Assert(new SalesHistoryService(db).Search(admin).Count==1,"Admin cannot view history");
}));
Run("deactivated cashier cannot checkout with old session",()=>WithDatabase((db,auth)=>
{
    DemoSeeder.Seed(db);
    var admin=Owner(auth);
    var secret=NewPassword();
    auth.CreateCashier(admin,"cashierone",secret);
    var cashier=auth.Login("cashierone",secret);
    using(var conn=db.Open())
    using(var cmd=conn.CreateCommand())
    {
        cmd.CommandText="UPDATE app_users SET active=0 WHERE id=@id;";
        cmd.Parameters.AddWithValue("@id",cashier.UserId);
        cmd.ExecuteNonQuery();
    }
    Throws<SecurityException>(()=>new SalesService(db).Checkout(
        Guid.NewGuid(),[new("BAUT-M8X30",1000)],5000,cashier));
    Assert(Number(db,"SELECT COUNT(*) FROM sales;")==0,"Deactivated cashier created sale");
}));
Console.WriteLine($"AUTHORIZATION TESTS: {passed} passed, {failed} failed");
if(failed>0) Environment.ExitCode=1;
