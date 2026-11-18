using SmartCasier.Core;
using System.Globalization;

static Dictionary<string,string> Parse(string[] tokens)
{
    var options=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
    for(var i=0;i<tokens.Length;i++)
    {
        if(!tokens[i].StartsWith("--"))throw new ArgumentException("Expected --option");
        var key=tokens[i][2..];
        var value="true";
        if(i+1<tokens.Length&&!tokens[i+1].StartsWith("--"))value=tokens[++i];
        if(!options.TryAdd(key,value))throw new ArgumentException("Duplicate --"+key);
    }
    return options;
}
static string Required(Dictionary<string,string> options,string key)
    => options.TryGetValue(key,out var value)?value:throw new ArgumentException("Missing --"+key);
static long QuantityMilli(string input)
{
    var value=decimal.Parse(input,CultureInfo.InvariantCulture);
    if(value<=0||value*1000m!=decimal.Truncate(value*1000m))
        throw new ArgumentException("Quantity must be positive with <=3 decimals");
    return checked((long)(value*1000m));
}
try
{
    if(args.Length==0||args[0]=="help"||args[0]=="--help")
    {
        Console.WriteLine("""
        Smart Casier offline POS core — software-only, no connected hardware.
        init      --db data/private/demo-pos.db
        seed-demo --db data/private/demo-pos.db --confirm-synthetic
        search    --db data/private/demo-pos.db --query baut
        stock     --db data/private/demo-pos.db
        checkout  dinonaktifkan: transaksi wajib melalui WPF dengan login operator
        All demo prices/stock are synthetic, not actual store data.
        """);
        return;
    }
    var command=args[0].ToLowerInvariant();
    var opts=Parse(args[1..]);
    var path=Required(opts,"db");
    var db=new PosDatabase(path);
    if(command=="init")
    {
        db.Initialize();Console.WriteLine("SQLITE_READY "+Path.GetFullPath(path));return;
    }
    if(command=="seed-demo")
    {
        if(!opts.ContainsKey("confirm-synthetic")||
           !Path.GetFileName(path).Contains("demo",StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to insert demo stock without --confirm-synthetic and a demo filename");
        db.Initialize();DemoSeeder.Seed(db);Console.WriteLine("SYNTHETIC_DEMO_SEEDED");return;
    }
    db.Initialize();
    if(command=="search")
    {
        foreach(var row in new CatalogService(db).Search(Required(opts,"query")))
            Console.WriteLine(row.Sku+" | "+row.ProductName+" "+row.VariantLabel+
                " | Rp"+row.UnitPriceIdr+" | stock "+(row.OnHandMilli/1000m)+" "+row.UnitCode+
                " | "+row.StockStatus);
    }
    else if(command=="stock")
    {
        foreach(var row in new CatalogService(db).Inventory())
            Console.WriteLine(row.Sku+" | "+row.StockStatus+" | "+(row.OnHandMilli/1000m)+" "+row.UnitCode);
    }
    else if(command=="checkout")
    {
        throw new NotSupportedException(
            "Checkout CLI dinonaktifkan. Gunakan Windows WPF dengan login operator.");
    }
    else throw new ArgumentException("Unknown command "+command);
}
catch(Exception e)
{
    Console.Error.WriteLine("ERROR: "+e.Message);
    Environment.ExitCode=1;
}
