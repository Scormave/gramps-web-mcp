using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrampsWeb.Mcp.Models;

/// <summary>
/// One row of <c>GET /api/transactions/history/</c> (OpenAPI <c>UndoTransaction</c>).
/// Changes carry object data only when requested with <c>old=true</c>.
/// </summary>
public class GrampsTransaction
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("connection")]
    public GrampsTransactionConnection? Connection { get; set; }

    /// <summary>Gramps transaction label, e.g. "Edit Person".</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>True when the transaction undid an earlier one.</summary>
    [JsonPropertyName("undo")]
    public bool Undo { get; set; }

    /// <summary>Commit time in Unix seconds.</summary>
    [JsonPropertyName("timestamp")]
    public double? Timestamp { get; set; }

    [JsonPropertyName("changes")]
    public GrampsTransactionChange[]? Changes { get; set; }
}

/// <summary>Session that committed a transaction; <see cref="User"/> is null for changes made outside Gramps Web.</summary>
public class GrampsTransactionConnection
{
    [JsonPropertyName("id")]
    public int? Id { get; set; }

    [JsonPropertyName("timestamp")]
    public double? Timestamp { get; set; }

    [JsonPropertyName("user")]
    public GrampsTransactionUser? User { get; set; }
}

public class GrampsTransactionUser
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("full_name")]
    public string? FullName { get; set; }
}

/// <summary>One object change inside a transaction (OpenAPI <c>ObjectChange</c>).</summary>
public class GrampsTransactionChange
{
    [JsonPropertyName("id")]
    public int? Id { get; set; }

    /// <summary>Object class name, e.g. "Person"; "7" (Gramps REFERENCE_KEY) for a link added or removed.</summary>
    [JsonPropertyName("obj_class")]
    public string? ObjClass { get; set; }

    /// <summary>Gramps TXNADD = 0, TXNUPD = 1, TXNDEL = 2.</summary>
    [JsonPropertyName("trans_type")]
    public int? TransType { get; set; }

    [JsonPropertyName("obj_handle")]
    public string? ObjHandle { get; set; }

    /// <summary>For a reference change, the object that <see cref="ObjHandle"/> links to.</summary>
    [JsonPropertyName("ref_handle")]
    public string? RefHandle { get; set; }

    [JsonPropertyName("timestamp")]
    public double? Timestamp { get; set; }

    /// <summary>The object as stored before the change, null for an add; sent only with <c>old=true</c>.</summary>
    [JsonPropertyName("old_data")]
    public JsonElement? OldData { get; set; }
}

/// <summary>
/// Reply of <c>GET /api/transactions/history/{id}/undo</c>: whether undoing the transaction would meet
/// records changed since. The spec has no schema for it.
/// </summary>
public class GrampsUndoCheck
{
    [JsonPropertyName("transaction_id")]
    public int TransactionId { get; set; }

    [JsonPropertyName("can_undo_without_force")]
    public bool CanUndoWithoutForce { get; set; }

    /// <summary>Changes other than references, which the undo leaves to the database.</summary>
    [JsonPropertyName("total_changes")]
    public int TotalChanges { get; set; }

    [JsonPropertyName("conflicts_count")]
    public int ConflictsCount { get; set; }

    [JsonPropertyName("conflicts")]
    public GrampsUndoConflict[]? Conflicts { get; set; }
}

public class GrampsUndoConflict
{
    [JsonPropertyName("change_index")]
    public int ChangeIndex { get; set; }

    [JsonPropertyName("object_class")]
    public string? ObjectClass { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    /// <summary>object_exists, object_changed or check_failed.</summary>
    [JsonPropertyName("conflict_type")]
    public string? ConflictType { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
