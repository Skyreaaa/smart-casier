using Microsoft.Data.Sqlite;
using System.Security;

namespace SmartCasier.Core;

/// <summary>Device-neutral frame. EventId must be preserved when retrying after a timeout.</summary>
public sealed record DeviceFrame(
    Guid EventId, string DeviceId, string Kind, string? Barcode, long? LengthMilli);

public interface IBarcodeReader
{
    DeviceFrame Scan(string code, Guid? eventId = null);
}
public interface ILengthMeter
{
    long FeedMilli { get; }
    void Feed(long deltaMilli);
    DeviceFrame SimulateManualCut(Guid? eventId = null);
}

/// <summary>Only synthesizes input events. It does not access HID/USB hardware.</summary>
public sealed class VirtualBarcodeReader(string deviceId = "SIM-BARCODE") : IBarcodeReader
{
    public DeviceFrame Scan(string code, Guid? eventId = null)
        => new(eventId ?? Guid.NewGuid(), deviceId, "BARCODE", code, null);
}

/// <summary>One milli-unit = 1 mm for roll material sold in meters.</summary>
public sealed class VirtualLengthMeter(string deviceId = "SIM-ENCODER") : ILengthMeter
{
    public long FeedMilli { get; private set; }
    public void Feed(long deltaMilli)
    {
        if (deltaMilli <= 0 || deltaMilli > 100_000 - FeedMilli)
            throw new ArgumentOutOfRangeException(nameof(deltaMilli),
                "Virtual feed must be positive and at most 100 meters total.");
        FeedMilli = checked(FeedMilli + deltaMilli);
    }
    public DeviceFrame SimulateManualCut(Guid? eventId = null)
    {
        if (FeedMilli <= 0)
            throw new InvalidOperationException("Feed simulated material before cut.");
        var frame = new DeviceFrame(eventId ?? Guid.NewGuid(), deviceId, "LENGTH", null, FeedMilli);
        FeedMilli = 0;
        return frame;
    }
}

public sealed record DeviceEventReceipt(
    Guid EventId, string DeviceId, string Kind, string? Barcode,
    long? LengthMilli, string Status, string? ResolvedSku, bool IsDuplicate)
{
    /// <summary>Cart input proposal only. No stock is touched until SalesService.Checkout.</summary>
    public long ProposedQuantityMilli =>
        Kind == "BARCODE" ? 1000L : LengthMilli ?? 0L;
}

/// <summary>
/// Persistent device inbox with deduplication, deliberate SKU confirmation, and ACK.
/// Only software-simulated devices are provided in this milestone.
/// </summary>
public sealed class DeviceInbox(PosDatabase database)
{
    public DeviceEventReceipt Publish(AuthSession? operatorSession, DeviceFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.EventId == Guid.Empty)
            throw new ArgumentException("Device event ID must not be empty.");
        if (string.IsNullOrWhiteSpace(frame.DeviceId) ||
            frame.DeviceId.Length is < 2 or > 64)
            throw new ArgumentException("Device ID must have 2–64 characters.");
        if (frame.Kind is not ("BARCODE" or "LENGTH"))
            throw new ArgumentException("Unsupported device event type.");
        if (frame.Kind == "BARCODE" &&
            (string.IsNullOrWhiteSpace(frame.Barcode) ||
             frame.Barcode.Length > 100 ||
             frame.Barcode.Any(char.IsWhiteSpace) ||
             frame.LengthMilli is not null))
            throw new ArgumentException("Barcode event requires one non-space code, 1–100 chars.");
        if (frame.Kind == "LENGTH" &&
            (frame.Barcode is not null || frame.LengthMilli is null ||
             frame.LengthMilli is < 1 or > 100_000))
            throw new ArgumentException("Length must be between 1 mm and 100 meters.");

        using var conn = database.Open();
        using var tx = conn.BeginTransaction();
        new AuthService(database).RequireOperator(operatorSession, conn, tx);
        var previous = Read(conn, tx, frame.EventId);
        if (previous is not null)
        {
            if (previous.DeviceId != frame.DeviceId ||
                previous.Kind != frame.Kind ||
                previous.Barcode != frame.Barcode ||
                previous.LengthMilli != frame.LengthMilli)
                throw new InvalidOperationException("Event ID collision with different payload.");
            tx.Commit();
            return previous with { IsDuplicate = true };
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO device_events(event_id,device_id,kind,barcode,length_milli)
                VALUES(@id,@device,@kind,@code,@length);
                """;
            cmd.Parameters.AddWithValue("@id", frame.EventId.ToString("N"));
            cmd.Parameters.AddWithValue("@device", frame.DeviceId);
            cmd.Parameters.AddWithValue("@kind", frame.Kind);
            cmd.Parameters.AddWithValue("@code", (object?)frame.Barcode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@length", (object?)frame.LengthMilli ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
        return new DeviceEventReceipt(frame.EventId,frame.DeviceId,frame.Kind,
            frame.Barcode,frame.LengthMilli,"PENDING",null,false);
    }

    /// <summary>Explicit cashier confirmation. Does not generate a sale or decrement stock.</summary>
    public DeviceEventReceipt Acknowledge(
        AuthSession? operatorSession, Guid eventId, string sku)
    {
        if (eventId == Guid.Empty) throw new ArgumentException("Event ID is required.");
        var normalized = (sku ?? "").Trim();
        if (normalized.Length == 0)
            throw new ArgumentException("Choose a SKU before ACK.");

        using var conn = database.Open();
        using var tx = conn.BeginTransaction();
        new AuthService(database).RequireOperator(operatorSession,conn,tx);
        var previous = Read(conn,tx,eventId)
            ?? throw new KeyNotFoundException("Unknown device event.");
        if (previous.Status == "ACKED")
        {
            if (!string.Equals(previous.ResolvedSku,normalized,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Event already ACKed to another SKU.");
            tx.Commit();
            return previous with { IsDuplicate = true };
        }
        if (previous.Status != "PENDING")
            throw new InvalidOperationException("Rejected event cannot be ACKed.");

        string variantId, chosenSku, unit, type;
        long track;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                SELECT v.id,v.sku,v.unit_code,p.product_type,v.track_stock
                FROM variants v
                JOIN products p ON p.id=v.product_id
                WHERE v.sku=@sku COLLATE NOCASE AND v.is_active=1 AND p.is_active=1;
                """;
            cmd.Parameters.AddWithValue("@sku",normalized);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) throw new KeyNotFoundException("Selected SKU does not exist.");
            variantId=r.GetString(0);
            chosenSku=r.GetString(1);
            unit=r.GetString(2);
            type=r.GetString(3);
            track=r.GetInt64(4);
        }
        if (type != "PHYSICAL" || track != 1)
            throw new InvalidOperationException("Only tracked physical items can be confirmed.");
        if (previous.Kind == "LENGTH" && unit != "M")
            throw new InvalidOperationException("Encoder length requires a meter-based SKU.");
        if (previous.Kind == "BARCODE")
        {
            if (unit != "PCS")
                throw new InvalidOperationException(
                    "Barcode alone does not establish a roll material length.");
            bool codeMatches = string.Equals(previous.Barcode,chosenSku,
                StringComparison.OrdinalIgnoreCase);
            if (!codeMatches)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    SELECT EXISTS(SELECT 1 FROM barcodes
                                  WHERE variant_id=@variant AND code=@code);
                    """;
                cmd.Parameters.AddWithValue("@variant",variantId);
                cmd.Parameters.AddWithValue("@code",previous.Barcode!);
                codeMatches = Convert.ToInt64(cmd.ExecuteScalar()) == 1;
            }
            if (!codeMatches)
                throw new InvalidOperationException(
                    "Barcode must match the selected SKU or an assigned barcode.");
        }

        using (var cmd=conn.CreateCommand())
        {
            cmd.Transaction=tx;
            cmd.CommandText="""
                UPDATE device_events SET status='ACKED',resolved_sku=@sku,
                    resolved_operator_id=@operator,resolved_at_utc=CURRENT_TIMESTAMP
                WHERE event_id=@event AND status='PENDING';
                """;
            cmd.Parameters.AddWithValue("@sku",chosenSku);
            cmd.Parameters.AddWithValue("@operator",operatorSession!.UserId);
            cmd.Parameters.AddWithValue("@event",eventId.ToString("N"));
            if(cmd.ExecuteNonQuery()!=1)throw new InvalidOperationException("Failed to ACK device event.");
        }
        tx.Commit();
        return previous with { Status="ACKED",ResolvedSku=chosenSku };
    }

    public DeviceEventReceipt Reject(AuthSession? operatorSession, Guid eventId)
    {
        using var conn=database.Open();
        using var tx=conn.BeginTransaction();
        new AuthService(database).RequireOperator(operatorSession,conn,tx);
        var previous=Read(conn,tx,eventId)
            ?? throw new KeyNotFoundException("Unknown device event.");
        if (previous.Status=="REJECTED")
        {
            tx.Commit();
            return previous with { IsDuplicate=true };
        }
        if (previous.Status!="PENDING")
            throw new InvalidOperationException("ACKed event cannot be rejected.");
        using(var cmd=conn.CreateCommand())
        {
            cmd.Transaction=tx;
            cmd.CommandText="""
                UPDATE device_events SET status='REJECTED',
                    resolved_operator_id=@operator,resolved_at_utc=CURRENT_TIMESTAMP
                WHERE event_id=@event AND status='PENDING';
                """;
            cmd.Parameters.AddWithValue("@operator",operatorSession!.UserId);
            cmd.Parameters.AddWithValue("@event",eventId.ToString("N"));
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
        return previous with { Status="REJECTED" };
    }

    public IReadOnlyList<DeviceEventReceipt> Pending(AuthSession? operatorSession)
    {
        using var conn=database.Open();
        new AuthService(database).RequireOperator(operatorSession,conn,null);
        using var cmd=conn.CreateCommand();
        cmd.CommandText = """
            SELECT event_id,device_id,kind,barcode,length_milli,status,resolved_sku
            FROM device_events WHERE status='PENDING'
            ORDER BY created_at_utc, rowid LIMIT 100;
            """;
        using var reader=cmd.ExecuteReader();
        var output=new List<DeviceEventReceipt>();
        while(reader.Read())
            output.Add(FromRow(reader));
        return output;
    }

    public DeviceEventReceipt? Find(AuthSession? operatorSession, Guid eventId)
    {
        using var conn=database.Open();
        new AuthService(database).RequireOperator(operatorSession,conn,null);
        return Read(conn,null,eventId);
    }

    private static DeviceEventReceipt? Read(
        SqliteConnection conn, SqliteTransaction? tx, Guid eventId)
    {
        using var cmd=conn.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText="""
            SELECT event_id,device_id,kind,barcode,length_milli,status,resolved_sku
            FROM device_events WHERE event_id=@id;
            """;
        cmd.Parameters.AddWithValue("@id",eventId.ToString("N"));
        using var row=cmd.ExecuteReader();
        return row.Read()?FromRow(row):null;
    }

    private static DeviceEventReceipt FromRow(SqliteDataReader row) =>
        new(Guid.ParseExact(row.GetString(0),"N"),row.GetString(1),row.GetString(2),
            row.IsDBNull(3)?null:row.GetString(3),
            row.IsDBNull(4)?null:row.GetInt64(4),row.GetString(5),
            row.IsDBNull(6)?null:row.GetString(6),false);
}
