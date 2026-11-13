using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Requests;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// Replaces the Gramps IDs in a tool's link fields by handles before anything is saved. Each field links one type,
/// so an ID is looked up in that type's collection, whatever its prefix; a field that links tags rejects IDs, since
/// tags have none. Handles and <c>#key</c> values stay as they are. Collect the fields, then call
/// <see cref="ResolveAsync"/>, which reports every ID it could not resolve in one error.
/// </summary>
internal sealed class LinkIds
{
    private readonly List<Pending> _pending = [];

    private sealed record Pending(string Field, string Collection, string Id, Action<string> SetHandle);

    /// <summary>A field holding one link: the value is replaced through <paramref name="setHandle"/>.</summary>
    public LinkIds One(string field, string? value, string collection, Action<string> setHandle)
    {
        var id = value?.Trim();
        if (id is not null && HandleResolver.LooksLikeGrampsId(id))
            _pending.Add(new Pending(field, collection, id, setHandle));
        return this;
    }

    /// <summary>A list of links, replaced in place.</summary>
    public LinkIds Handles(string field, string[]? values, string collection)
    {
        if (values is null)
            return this;
        for (var i = 0; i < values.Length; i++)
        {
            var index = i;
            One(field, values[i], collection, handle => values[index] = handle);
        }

        return this;
    }

    /// <summary>The citations and notes of each attribute.</summary>
    public LinkIds Attributes(string field, GrampsAttribute[]? attributes)
    {
        for (var i = 0; i < (attributes?.Length ?? 0); i++)
            CitationsAndNotes($"{field}[{i}]", attributes![i].CitationList, attributes[i].NoteList);
        return this;
    }

    /// <summary>The citations and notes of a name.</summary>
    public LinkIds Name(string field, GrampsName? name) =>
        name is null ? this : CitationsAndNotes(field, name.CitationList, name.NoteList);

    /// <summary>The citations and notes of each name.</summary>
    public LinkIds Names(string field, GrampsName[]? names)
    {
        for (var i = 0; i < (names?.Length ?? 0); i++)
            Name($"{field}[{i}]", names![i]);
        return this;
    }

    /// <summary>The citations and notes of each address.</summary>
    public LinkIds Addresses(string field, GrampsAddress[]? addresses)
    {
        for (var i = 0; i < (addresses?.Length ?? 0); i++)
            CitationsAndNotes($"{field}[{i}]", addresses![i].CitationList, addresses[i].NoteList);
        return this;
    }

    /// <summary>Events with the notes and attributes of each part.</summary>
    public LinkIds EventRefs(string field, EventRefRequest[]? refs)
    {
        for (var i = 0; i < (refs?.Length ?? 0); i++)
        {
            var eventRef = refs![i];
            One(field, eventRef.Ref, "events", handle => eventRef.Ref = handle);
            Handles($"{field}[{i}].note_list", eventRef.NoteList, "notes");
            Attributes($"{field}[{i}].attributes", eventRef.AttributeList);
        }

        return this;
    }

    /// <summary>Children with the tags of each child link.</summary>
    public LinkIds ChildRefs(string field, GrampsChildRef[]? refs)
    {
        for (var i = 0; i < (refs?.Length ?? 0); i++)
        {
            var childRef = refs![i];
            One(field, childRef.Ref, "people", handle => childRef.Ref = handle);
            Handles($"{field}[{i}].tag_list", childRef.TagList, "tags");
        }

        return this;
    }

    /// <summary>Associated people with the citations and notes of each association.</summary>
    public LinkIds PersonRefs(string field, GrampsPersonRef[]? refs)
    {
        for (var i = 0; i < (refs?.Length ?? 0); i++)
        {
            var personRef = refs![i];
            One(field, personRef.Ref, "people", handle => personRef.Ref = handle);
            CitationsAndNotes($"{field}[{i}]", personRef.CitationList, personRef.NoteList);
        }

        return this;
    }

    /// <summary>Repositories with the notes of each repository link.</summary>
    public LinkIds RepositoryRefs(string field, GrampsRepositoryRef[]? refs)
    {
        for (var i = 0; i < (refs?.Length ?? 0); i++)
        {
            var repositoryRef = refs![i];
            One(field, repositoryRef.Ref, "repositories", handle => repositoryRef.Ref = handle);
            Handles($"{field}[{i}].note_list", repositoryRef.NoteList, "notes");
        }

        return this;
    }

    /// <summary>Enclosing places.</summary>
    public LinkIds PlaceRefs(string field, PlaceRefRequest[]? refs)
    {
        foreach (var placeRef in refs ?? [])
            One(field, placeRef.Ref, "places", handle => placeRef.Ref = handle);
        return this;
    }

    /// <summary>
    /// Media links with the citations and notes of their attributes. With <paramref name="refs"/> false only the
    /// attributes are resolved, for links whose targets the caller resolves itself, such as attachTo.
    /// </summary>
    public LinkIds MediaLinks(string field, MediaLink[]? links, bool refs = true)
    {
        for (var i = 0; i < (links?.Length ?? 0); i++)
        {
            var index = i;
            if (refs)
                One(field, links![i].Ref, "media", handle => links[index] = links[index] with { Ref = handle });
            Attributes($"{field}[{i}].attributes", links![i].Attributes);
        }

        return this;
    }

    private LinkIds CitationsAndNotes(string field, string[]? citations, string[]? notes) =>
        Handles($"{field}.citation_list", citations, "citations").Handles($"{field}.note_list", notes, "notes");

    /// <summary>
    /// Looks up every collected ID in the collection of its field and puts the handles in place. Throws one
    /// validation error naming each field and ID that has no record, ending with <paramref name="nothingDone"/>.
    /// </summary>
    public async Task ResolveAsync(GrampsApiClient client, string nothingDone = "Nothing was saved.")
    {
        if (_pending.Count == 0)
            return;

        var found = new Dictionary<(string Collection, string Id), string?>();
        var problems = new List<string>();
        foreach (var pending in _pending)
        {
            var key = (pending.Collection, pending.Id.ToUpperInvariant());
            if (!found.TryGetValue(key, out var handle))
            {
                handle = await HandleResolver.FindHandleAsync(pending.Id, pending.Collection, client);
                found[key] = handle;
                if (handle is null)
                    problems.Add(Problem(pending));
            }

            if (handle is not null)
                pending.SetHandle(handle);
        }

        if (problems.Count > 0)
            throw McpToolErrors.ValidationError($"Error: {string.Join(" ", problems)} {nothingDone}");
    }

    private static string Problem(Pending pending)
    {
        if (pending.Collection == "tags")
            return $"{pending.Field}: {pending.Id} is not a tag handle; tags have no Gramps ID, so give the tag's handle " +
                   "from list_records(objectType: \"tags\").";

        var noun = Noun(pending.Collection);
        var prefixType = HandleResolver.PrefixToObjectType(pending.Id[0]);
        var hint = prefixType is not null && prefixType != pending.Collection
            ? $" {pending.Id[0]} is the prefix of {prefixType}, and {pending.Field} links {pending.Collection}."
            : "";
        return $"{pending.Field}: there is no {noun} {pending.Id}.{hint}";
    }

    private static string Noun(string collection) => collection switch
    {
        "people" => "person",
        "families" => "family",
        "events" => "event",
        "places" => "place",
        "sources" => "source",
        "citations" => "citation",
        "repositories" => "repository",
        "notes" => "note",
        "media" => "media object",
        _ => collection,
    };
}
