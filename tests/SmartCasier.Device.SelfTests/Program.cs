using Microsoft.Data.Sqlite;
using SmartCasier.Core;
using System.Security;

int passed=0,failed=0;
void Run(string name,Action test)
{
    try { test();passed++;Console.WriteLine("PASS "+name); }
    catch(Exception e) { failed++;Console.Error.WriteLine("FAIL "+name+": "+e); }
}
static void Assert(bool yes,string message)
{
    if(!yes)throw new Exception(message);
}
static void Raises<T>(Action action) where T:Exception
{
    try {action();}
    catch(T){return;}
    throw new Exception("Expected "+typeof(T).Name);
}
static void Demo(Action<PosDatabase,AuthSession> test)
{
    var dir=Path.Combine(Path.GetTempPath(),"sc-device-"+Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
        var db=new PosDatabase(Path.Combine(dir,"demo.db"));
        db.Initialize();
        DemoSeeder.Seed(db);
        var auth=new AuthService(db);
        var pass=Guid.NewGuid().ToString("N")+"Aa!";
        auth.CreateFirstAdmin("testoperator",pass);
        var operatorSession=auth.Login("testoperator",pass);
        test(db,operatorSession);
    }
    finally {SqliteConnection.ClearAllPools();Directory.Delete(dir,true);}
}
static long Scalar(PosDatabase db,string query)
{
    using var conn=db.Open();
    using var cmd=conn.CreateCommand();
    cmd.CommandText=query;
    return Convert.ToInt64(cmd.ExecuteScalar());
}
static void Sql(PosDatabase db,string query)
{
    using var conn=db.Open();
    using var cmd=conn.CreateCommand();
    cmd.CommandText=query;
    cmd.ExecuteNonQuery();
}
Run("first-run inbox starts empty at schema v6",()=>Demo((db,actor)=>
{
    Assert(Scalar(db,"PRAGMA user_version;")==6,"Database migration not v6");
    Assert(new DeviceInbox(db).Pending(actor).Count==0,"Queue not empty");
}));
Run("barcode scan requires deliberate ACK, never a sale",()=>Demo((db,actor)=>
{
    var inbox=new DeviceInbox(db);
    var scan=new VirtualBarcodeReader().Scan("BAUT-M8X30");
    var pending=inbox.Publish(actor,scan);
    Assert(pending.Status=="PENDING"&&pending.ProposedQuantityMilli==1000,"Not pending");
    Assert(Scalar(db,"SELECT COUNT(*) FROM sales;")==0,"Scan caused a sale");
    var ack=inbox.Acknowledge(actor,scan.EventId,"BAUT-M8X30");
    Assert(ack.Status=="ACKED" && ack.ResolvedSku=="BAUT-M8X30","ACK missing");
    Assert(Scalar(db,"SELECT COUNT(*) FROM stock_movements WHERE movement_type='SALE';")==0,
        "Simulated scan modified stock");
}));
Run("same frame retry is idempotent",()=>Demo((db,actor)=>
{
    var inbox=new DeviceInbox(db);
    var frame=new VirtualBarcodeReader().Scan("BAUT-M8X30");
    Assert(!inbox.Publish(actor,frame).IsDuplicate,"New event reported duplicate");
    Assert(inbox.Publish(actor,frame).IsDuplicate,"Duplicate frame not deduplicated");
    Assert(Scalar(db,"SELECT COUNT(*) FROM device_events;")==1,"Duplicate event inserted");
}));
Run("same id different barcode is rejected",()=>Demo((db,actor)=>
{
    var inbox=new DeviceInbox(db);
    var id=Guid.NewGuid();
    inbox.Publish(actor,new VirtualBarcodeReader().Scan("BAUT-M8X30",id));
    Raises<InvalidOperationException>(()=>inbox.Publish(actor,
        new VirtualBarcodeReader().Scan("BAUT-M8X35",id)));
    Assert(Scalar(db,"SELECT COUNT(*) FROM device_events;")==1,"Collision modified inbox");
}));
Run("ACK is idempotent only for same SKU",()=>Demo((db,actor)=>
{
    var inbox=new DeviceInbox(db);
    var frame=new VirtualBarcodeReader().Scan("BAUT-M8X30");
    inbox.Publish(actor,frame);
    inbox.Acknowledge(actor,frame.EventId,"BAUT-M8X30");
    Assert(inbox.Acknowledge(actor,frame.EventId,"BAUT-M8X30").IsDuplicate,
        "Duplicate ACK should be idempotent");
    Raises<InvalidOperationException>(()=>inbox.Acknowledge(
        actor,frame.EventId,"BAUT-M8X35"));
}));
Run("wrong SKU does not ACK a barcode",()=>Demo((db,actor)=>
{
    var inbox=new DeviceInbox(db);
    var frame=new VirtualBarcodeReader().Scan("BAUT-M8X30");
    inbox.Publish(actor,frame);
    Raises<InvalidOperationException>(()=>inbox.Acknowledge(
        actor,frame.EventId,"BAUT-M8X35"));
    Assert(inbox.Find(actor,frame.EventId)!.Status=="PENDING","Wrong match not left pending");
}));
Run("catalog-assigned barcode is accepted",()=>Demo((db,actor)=>
{
    Sql(db,"INSERT INTO barcodes(code,variant_id) VALUES('123456789','BAUT-M8X30');");
    var inbox=new DeviceInbox(db);
    var frame=new VirtualBarcodeReader().Scan("123456789");
    inbox.Publish(actor,frame);
    Assert(inbox.Acknowledge(actor,frame.EventId,"BAUT-M8X30").Status=="ACKED",
        "Existing mapped barcode not accepted");
}));
Run("virtual length encoder 2.5m does not auto-charge",()=>Demo((db,actor)=>
{
    var encoder=new VirtualLengthMeter();
    encoder.Feed(1000);encoder.Feed(1500);
    var frame=encoder.SimulateManualCut();
    Assert(encoder.FeedMilli==0,"Encoder not reset after virtual cut");
    var inbox=new DeviceInbox(db);
    var p=inbox.Publish(actor,frame);
    Assert(p.LengthMilli==2500&&p.Status=="PENDING","Length payload incorrect");
    var ack=inbox.Acknowledge(actor,frame.EventId,"KMY-MERAH");
    Assert(ack.ProposedQuantityMilli==2500,"Proposed cart length incorrect");
    Assert(Scalar(db,"SELECT COUNT(*) FROM sales;")==0,"Encoder created sale");
}));
Run("encoder cannot be mapped to PCS",()=>Demo((db,actor)=>
{
    var encoder=new VirtualLengthMeter();
    encoder.Feed(2500);
    var frame=encoder.SimulateManualCut();
    var inbox=new DeviceInbox(db);
    inbox.Publish(actor,frame);
    Raises<InvalidOperationException>(()=>inbox.Acknowledge(
        actor,frame.EventId,"BAUT-M8X30"));
    Assert(inbox.Find(actor,frame.EventId)!.Status=="PENDING","Rejected mismatch not pending");
}));
Run("encoder validates nonzero and maximum 100 meters",()=>Demo((db,actor)=>
{
    var encoder=new VirtualLengthMeter();
    Raises<ArgumentOutOfRangeException>(()=>encoder.Feed(0));
    Raises<InvalidOperationException>(()=>encoder.SimulateManualCut());
    Raises<ArgumentOutOfRangeException>(()=>encoder.Feed(100_001));
    encoder.Feed(100_000);
    Raises<ArgumentOutOfRangeException>(()=>encoder.Feed(1));
}));
Run("event inbox survives reopening service",()=>Demo((db,actor)=>
{
    var eventId=Guid.NewGuid();
    var frame=new VirtualBarcodeReader().Scan("BAUT-M8X30",eventId);
    new DeviceInbox(db).Publish(actor,frame);
    var newInbox=new DeviceInbox(db);
    Assert(newInbox.Publish(actor,frame).IsDuplicate,"Persistent replay not detected");
    Assert(newInbox.Pending(actor).Count==1,"Pending event lost");
}));
Run("pending event can be rejected but never ACKed afterward",()=>Demo((db,actor)=>
{
    var inbox=new DeviceInbox(db);
    var frame=new VirtualBarcodeReader().Scan("BAUT-M8X30");
    inbox.Publish(actor,frame);
    Assert(inbox.Reject(actor,frame.EventId).Status=="REJECTED","Not rejected");
    Assert(inbox.Reject(actor,frame.EventId).IsDuplicate,"Reject replay not idempotent");
    Raises<InvalidOperationException>(()=>inbox.Acknowledge(
        actor,frame.EventId,"BAUT-M8X30"));
}));
Run("unauthenticated actor cannot publish or ACK",()=>Demo((db,actor)=>
{
    var inbox=new DeviceInbox(db);
    var frame=new VirtualBarcodeReader().Scan("BAUT-M8X30");
    Raises<SecurityException>(()=>inbox.Publish(null,frame));
    Assert(Scalar(db,"SELECT COUNT(*) FROM device_events;")==0,"Unauthorized publish");
    inbox.Publish(actor,frame);
    Raises<SecurityException>(()=>inbox.Acknowledge(null,frame.EventId,"BAUT-M8X30"));
    Assert(inbox.Find(actor,frame.EventId)!.Status=="PENDING","Unauthorized ACK");
}));
Run("simulator barcode cannot bind length-only roll SKU",()=>Demo((db,actor)=>
{
    var inbox=new DeviceInbox(db);
    var frame=new VirtualBarcodeReader().Scan("KMY-MERAH");
    inbox.Publish(actor,frame);
    Raises<InvalidOperationException>(()=>inbox.Acknowledge(actor,frame.EventId,"KMY-MERAH"));
}));
Console.WriteLine($"DEVICE TESTS: {passed} passed, {failed} failed");
if(failed>0)Environment.ExitCode=1;
