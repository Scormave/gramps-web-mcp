using GrampsWeb.Mcp.Client;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// Builds helpful not-found messages with hints when agents pass wrong identifiers.
/// </summary>
internal static class NotFoundHelper
{
    private sealed record Kind(string Noun, string Collection, char Prefix);

    // By the display name tools pass; prefixes are the Gramps defaults that HandleResolver resolves.
    private static readonly IReadOnlyDictionary<string, Kind> Kinds =
        new Dictionary<string, Kind>(StringComparer.OrdinalIgnoreCase)
        {
            ["Person"] = new("person", "people", 'I'),
            ["Family"] = new("family", "families", 'F'),
            ["Event"] = new("event", "events", 'E'),
            ["Place"] = new("place", "places", 'P'),
            ["Source"] = new("source", "sources", 'S'),
            ["Citation"] = new("citation", "citations", 'C'),
            ["Repository"] = new("repository", "repositories", 'R'),
            ["Note"] = new("note", "notes", 'N'),
            ["Media"] = new("media object", "media", 'O'),
            ["Tag"] = new("tag", "tags", 'T')
        };

    /// <summary>
    /// Builds a not-found message. For an identifier that looks like a Gramps ID, the hint says
    /// whether its prefix belongs to another object type, or else that no object of this type has
    /// the ID and how to find one.
    /// </summary>
    public static string NotFoundMessage(string objectType, string identifier)
    {
        var msg = $"{objectType} not found: {identifier}";

        if (HandleResolver.LooksLikeGrampsId(identifier))
        {
            msg += "\n\nHint: " + GrampsIdHint(objectType, identifier);
        }
        else if (identifier.Length < 5)
        {
            msg += $"\n\nHint: This identifier is very short. Handles are typically 20+ character strings. " +
                   $"If you meant a Gramps ID (like I0001), include the type prefix letter.";
        }

        return msg;
    }

    private static string GrampsIdHint(string objectType, string id)
    {
        if (!Kinds.TryGetValue(objectType, out var wanted))
            return $"No {objectType.ToLowerInvariant()} has Gramps ID {id}.";

        var idKind = Kinds.Values.FirstOrDefault(k => k.Prefix == id[0]);
        if (idKind is null)
            return $"{id} does not start with a Gramps ID prefix; {wanted.Noun} IDs start with {wanted.Prefix}.";
        if (idKind != wanted)
        {
            return $"{id} has the {idKind.Noun} prefix {idKind.Prefix}; {wanted.Noun} IDs start with {wanted.Prefix}. " +
                   $"To read the {idKind.Noun} with this ID, call get_object(identifier: \"{id}\").";
        }

        return $"No {wanted.Noun} has Gramps ID {id}. " +
               $"Find the {wanted.Noun} with search, or browse list_objects(objectType: \"{wanted.Collection}\").";
    }
}
