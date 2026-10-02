using System.Text.Json.Serialization;

namespace GrampsWeb.Mcp.Models;

/// <summary>
/// One row of <c>GET /api/transactions/history/</c> (OpenAPI <c>UndoTransaction</c>).
/// Requested without <c>old</c>/<c>new</c>, so changes carry no object data.
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

    /// <summary>Object class name, e.g. "Person".</summary>
    [JsonPropertyName("obj_class")]
    public string? ObjClass { get; set; }

    /// <summary>Gramps TXNADD = 0, TXNUPD = 1, TXNDEL = 2.</summary>
    [JsonPropertyName("trans_type")]
    public int? TransType { get; set; }

    [JsonPropertyName("obj_handle")]
    public string? ObjHandle { get; set; }

    [JsonPropertyName("timestamp")]
    public double? Timestamp { get; set; }
}
