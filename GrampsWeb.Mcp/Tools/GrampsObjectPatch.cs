using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Exceptions;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// The stored JSON of one Gramps object, changed only where an update tool received an argument.
/// Gramps Web replaces the whole object on PUT, so the body starts from the raw GET payload rather than
/// from a typed model: fields the models do not carry (attribute citations, styled note text, LDS ordinances,
/// fields added by newer Gramps versions, …) are sent back unchanged.
/// </summary>
internal sealed class GrampsObjectPatch
{
    /// <summary>Keys GET adds only when asked for them; they are not part of the stored object.</summary>
    private static readonly string[] ResponseOnlyKeys = ["extended", "profile", "backlinks", "formatted"];

    private readonly string _path;

    private GrampsObjectPatch(string path, JsonObject root)
    {
        _path = path;
        Root = root;
    }

    /// <summary>The object body that <see cref="SaveAsync"/> sends.</summary>
    public JsonObject Root { get; }

    public string? Handle => StringValue(Root["handle"]);

    public string? GrampsId => StringValue(Root["gramps_id"]);

    /// <summary>GETs the object at <paramref name="path"/>; <c>null</c> when Gramps answers 404.</summary>
    public static async Task<GrampsObjectPatch?> LoadAsync(GrampsApiClient client, string path)
    {
        var element = await client.GetJsonOrNullIfNotFoundAsync(path);
        if (element is null)
            return null;
        if (JsonNode.Parse(element.Value.GetRawText()) is not JsonObject root)
            throw new GrampsApiException(HttpStatusCode.BadGateway,
                $"Gramps returned {element.Value.ValueKind} instead of an object for {path}; nothing was changed.");

        foreach (var key in ResponseOnlyKeys)
            root.Remove(key);
        return new GrampsObjectPatch(path, root);
    }

    /// <summary>PUTs the patched object back to the path it was loaded from.</summary>
    public Task SaveAsync(GrampsApiClient client) => client.PutMutationAsync(_path, Root);

    /// <summary>Sets <paramref name="key"/> when <paramref name="value"/> is not null; null keeps the stored value.</summary>
    public void Set<T>(string key, T? value)
    {
        if (value is not null)
            Root[key] = ToNode(value);
    }

    /// <summary>Sets <paramref name="key"/>, or removes it when <paramref name="value"/> is null.</summary>
    public void SetOrRemove<T>(string key, T? value)
    {
        if (value is null)
            Root.Remove(key);
        else
            Root[key] = ToNode(value);
    }

    /// <summary>
    /// Applies a supplied link list to the stored one (see <see cref="LinkUpdates"/>). Stored entries are kept
    /// as they are. In replace mode a supplied object whose ref matches a stored entry is laid over that entry,
    /// so metadata the caller did not send (privacy, citations, notes, crop rectangle, …) stays;
    /// several references to the same handle are matched in order.
    /// </summary>
    public void ApplyLinks(string key, IEnumerable<JsonNode?>? supplied, string mode)
    {
        if (supplied is null)
            return;
        var stored = (Root[key] as JsonArray)?.ToArray();
        var result = LinkUpdates.Apply(stored, supplied.ToArray(), mode, LinkKey);
        if (result is null || ReferenceEquals(result, stored))
            return;
        if (mode == "replace")
            result = Overlay(stored ?? [], result, LinkKey);
        Root[key] = new JsonArray(result.Select(node => node?.DeepClone()).ToArray());
    }

    /// <summary>Applies a list of plain handles, e.g. <c>note_list</c> or <c>tag_list</c>.</summary>
    public void ApplyHandles(string key, string[]? handles, string mode) =>
        ApplyLinks(key, handles?.Select(h => (JsonNode?)JsonValue.Create(h)), mode);

    /// <summary>
    /// Applies media links to <c>media_list</c>, whose entries are <c>MediaRef</c> objects. A link with a region
    /// sets the stored <c>rect</c> (the whole image clears it); a link without one keeps the stored region.
    /// In add mode a media already shown keeps its link and takes the region given; a region for a media
    /// whose links all took one already adds another link, so one image can show several regions.
    /// </summary>
    public void ApplyMediaLinks(MediaLink[]? links, string mode, string argument = "mediaHandles")
    {
        if (links is null)
            return;
        LinkUpdates.Validate(mode);
        FlexibleMediaLinkList.EnsureValid(links, argument);
        if (mode != "add")
        {
            ApplyLinks("media_list", links.Select(link => (JsonNode?)MediaRefNode(link)), mode);
            return;
        }

        if (links.Any(link => string.IsNullOrWhiteSpace(link.Ref)))
            throw McpToolErrors.ValidationError("Each link must have a non-empty handle/ref.");
        var list = Root["media_list"] as JsonArray ?? new JsonArray();
        var stored = list.ToArray();
        var used = new bool[stored.Length];
        var shown = stored.Select(LinkKey).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var changed = false;

        // Links that already show their region stay as they are, so repeating a call changes nothing.
        var pending = new List<MediaLink>();
        foreach (var link in links.Where(link => link.Region is not null))
        {
            var region = link.Region!.IsWholeImage ? null : link.Region;
            var index = Enumerable.Range(0, stored.Length).FirstOrDefault(i => !used[i] && LinkKey(stored[i]) == link.Ref.Trim()
                && stored[i] is JsonObject o && MediaRegion.FromStored(IntArray(o["rect"])) == region, -1);
            if (index >= 0)
                used[index] = true;
            else
                pending.Add(link);
        }

        foreach (var link in links)
        {
            var handle = link.Ref.Trim();
            if (link.Region is null)
            {
                if (shown.Add(handle))
                {
                    list.Add(MediaRefNode(link));
                    changed = true;
                }
                continue;
            }

            if (!pending.Remove(link))
                continue;
            var index = FindUnused(stored, used, handle, LinkKey);
            if (index >= 0 && stored[index] is JsonObject match)
            {
                used[index] = true;
                match["rect"] = RectNode(link.Region);
                changed = true;
                continue;
            }

            shown.Add(handle);
            list.Add(MediaRefNode(link));
            changed = true;
        }

        if (changed && list.Parent is null)
            Root["media_list"] = list;
    }

    private static JsonObject MediaRefNode(MediaLink link)
    {
        var node = new JsonObject { ["_class"] = "MediaRef", ["ref"] = link.Ref.Trim() };
        if (link.Region is not null)
            node["rect"] = RectNode(link.Region);
        return node;
    }

    /// <summary>The stored <c>rect</c>: the region's edges, or null for the whole image, as Gramps stores a link without one.</summary>
    private static JsonNode? RectNode(MediaRegion region) =>
        region.IsWholeImage ? null : new JsonArray(region.ToArray().Select(edge => (JsonNode?)edge).ToArray());

    private static int[]? IntArray(JsonNode? node) =>
        node is JsonArray array && array.All(item => item is JsonValue value && value.TryGetValue<int>(out _))
            ? array.Select(item => item!.GetValue<int>()).ToArray()
            : null;

    /// <summary>Applies typed references, written by their converters (which leave out empty fields).</summary>
    public void ApplyRefs<T>(string key, T[]? refs, string mode) =>
        ApplyLinks(key, refs?.Select(r => ToNode(r)), mode);

    /// <summary>
    /// Replaces <c>attribute_list</c> with the supplied attributes. An attribute whose type and value match a
    /// stored one keeps that entry, with its citations, notes and privacy, unless the caller sent new ones.
    /// Source and citation tools check <see cref="Requests.GrampsRequestMapping.SourceAttributeError"/> first, since their
    /// attributes have no citations or notes.
    /// </summary>
    public void ReplaceAttributes(GrampsAttribute[]? supplied)
    {
        if (supplied is null)
            return;
        var stored = (Root["attribute_list"] as JsonArray)?.ToArray() ?? [];
        var used = new bool[stored.Length];
        var result = new JsonArray();
        foreach (var attribute in supplied)
        {
            var index = FindUnused(stored, used, AttributeKey(attribute.Type, attribute.Value), StoredAttributeKey);
            JsonObject node;
            if (index >= 0 && stored[index] is JsonObject match)
            {
                used[index] = true;
                node = (JsonObject)match.DeepClone();
            }
            else
            {
                node = new JsonObject();
                if (attribute.Type is not null)
                    node["type"] = attribute.Type;
                if (attribute.Value is not null)
                    node["value"] = attribute.Value;
            }

            if (attribute.CitationList is { Length: > 0 })
                node["citation_list"] = ToNode(attribute.CitationList);
            if (attribute.NoteList is { Length: > 0 })
                node["note_list"] = ToNode(attribute.NoteList);
            if (attribute.Private)
                node["private"] = true;
            result.Add(node);
        }

        Root["attribute_list"] = result;
    }

    /// <summary>The nested object at <paramref name="key"/>, created when missing or not an object.</summary>
    public JsonObject Object(string key)
    {
        if (Root[key] is JsonObject existing)
            return existing;
        var created = new JsonObject();
        Root[key] = created;
        return created;
    }

    /// <summary>The label of a Gramps type: a plain string, or the <c>string</c> of a type object.</summary>
    public static string? TypeString(JsonNode? node) => node switch
    {
        JsonObject o => StringValue(o["string"]),
        _ => StringValue(node)
    };

    public static string? StringValue(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    public static JsonNode? ToNode<T>(T value) => JsonSerializer.SerializeToNode(value, GrampsJson.UpdateOptions);

    /// <summary>The handle of a link entry: a handle string, or the <c>ref</c> of a reference object.</summary>
    private static string? LinkKey(JsonNode? node) => node switch
    {
        JsonObject o => StringValue(o["ref"])?.Trim(),
        _ => StringValue(node)?.Trim()
    };

    private static string AttributeKey(string? type, string? value) => $"{type?.Trim()}\0{value}";

    private static string? StoredAttributeKey(JsonNode? node) =>
        node is JsonObject o ? AttributeKey(TypeString(o["type"]), StringValue(o["value"])) : null;

    private static JsonNode?[] Overlay(JsonNode?[] stored, JsonNode?[] supplied, Func<JsonNode?, string?> key)
    {
        var used = new bool[stored.Length];
        var result = new JsonNode?[supplied.Length];
        for (var i = 0; i < supplied.Length; i++)
        {
            result[i] = supplied[i];
            if (supplied[i] is not JsonObject patch || key(patch) is not { Length: > 0 } handle)
                continue;
            var index = FindUnused(stored, used, handle, key);
            if (index < 0 || stored[index] is not JsonObject match)
                continue;

            used[index] = true;
            var merged = (JsonObject)match.DeepClone();
            foreach (var (name, value) in patch)
                merged[name] = value?.DeepClone();
            result[i] = merged;
        }

        return result;
    }

    private static int FindUnused(JsonNode?[] stored, bool[] used, string key, Func<JsonNode?, string?> keyOf)
    {
        for (var i = 0; i < stored.Length; i++)
            if (!used[i] && string.Equals(keyOf(stored[i]), key, StringComparison.Ordinal))
                return i;
        return -1;
    }
}
