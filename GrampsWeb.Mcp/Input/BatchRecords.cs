using System.ComponentModel;
using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Tools;

namespace GrampsWeb.Mcp.Input;

/// <summary>
/// One record of create_records: the arguments of the matching create tool, except links to families on a
/// person, plus a key other records of the same call refer to it by as <c>"#key"</c>.
/// </summary>
public abstract class BatchRecord
{
    [JsonPropertyName("key")]
    [JsonPropertyOrder(-1)]
    [Description("Optional name for this record, unique in the call: letters, digits, _ and -, up to 64. " +
                 "Other records of the call link to it by \"#key\" wherever they take a handle.")]
    public string? Key { get; init; }

    /// <summary>The Gramps class sent as <c>_class</c>: Person, Event, ….</summary>
    internal abstract string GrampsClass { get; }

    /// <summary>The checked request body, as the matching create tool builds it, and a label for the result.</summary>
    internal abstract Task<(object Request, string Label)> BuildAsync(GrampsApiClient client);

    internal const string CitationsText = "Citations: handles of existing ones or #keys of citations in this call.";
    internal const string NotesText = "Notes: handles of existing ones or #keys of notes in this call.";
    internal const string TagsText = "Existing tags: handles (create_tag).";
    internal const string MediaText = "Existing media: handles, or {ref, region} to show part of an image, as in create_person.";
    internal const string AttributesText = "Attributes, as in the create tool: [{type, value}] or \"Type: Value\".";
    internal const string PrivateText = "Mark the record private (default false).";
}

public sealed class PersonRecord : BatchRecord
{
    [JsonPropertyName("primaryName")]
    [Description("Main name, as in create_person: an object {given, surname, patronymic, …} or a plain \"Given Surname\" (required).")]
    public required FlexibleGrampsName PrimaryName { get; init; }

    [JsonPropertyName("gender")]
    [Description("Female, Male or Unknown (default).")]
    public string Gender { get; init; } = "Unknown";

    [JsonPropertyName("alternateNames")]
    [Description("Other names, as in create_person.")]
    public FlexibleAlternateNameList? AlternateNames { get; init; }

    [JsonPropertyName("eventRefs")]
    [Description("Events the person takes part in: handles or #keys, each optionally with a role as \"#key::Witness\" " +
                 "or {ref, role} (default Primary). The first Primary Birth and Death become the birth and death.")]
    public FlexibleEventRefList? EventRefs { get; init; }

    [JsonPropertyName("mediaHandles")]
    [Description(MediaText)]
    public FlexibleMediaLinkList? MediaHandles { get; init; }

    [JsonPropertyName("citationHandles")]
    [Description(CitationsText)]
    public FlexibleHandleList? CitationHandles { get; init; }

    [JsonPropertyName("noteHandles")]
    [Description(NotesText)]
    public FlexibleHandleList? NoteHandles { get; init; }

    [JsonPropertyName("tagHandles")]
    [Description(TagsText)]
    public FlexibleHandleList? TagHandles { get; init; }

    [JsonPropertyName("attributes")]
    [Description(AttributesText)]
    public FlexibleAttributeList? Attributes { get; init; }

    [JsonPropertyName("addresses")]
    [Description("Addresses, as in create_person.")]
    public FlexibleAddressList? Addresses { get; init; }

    [JsonPropertyName("urls")]
    [Description("Web links, as in create_person.")]
    public FlexibleUrlList? Urls { get; init; }

    [JsonPropertyName("personAssociations")]
    [Description("Associations with other people: handles or #keys with a relation, \"#key:: Godfather\" or {ref, rel}.")]
    public FlexiblePersonRefList? PersonAssociations { get; init; }

    [JsonPropertyName("isPrivate")]
    [Description(PrivateText)]
    public bool IsPrivate { get; init; }

    internal override string GrampsClass => "Person";

    internal override async Task<(object Request, string Label)> BuildAsync(GrampsApiClient client)
    {
        var request = await PersonTools.BuildPersonRequestAsync(PrimaryName, Gender, AlternateNames, EventRefs,
            familyHandles: null, parentFamilyHandles: null, MediaHandles, CitationHandles, NoteHandles, TagHandles,
            Attributes, Addresses, Urls, PersonAssociations, IsPrivate, client);
        return (request, GrampsValueFormatter.FormatName(PrimaryName.Name));
    }
}

public sealed class EventRecord : BatchRecord
{
    [JsonPropertyName("eventType")]
    [Description("Event type, e.g. Birth, Death, Marriage, Baptism, Burial, Residence (required).")]
    public required string EventType { get; init; }

    [JsonPropertyName("date")]
    [Description(ToolDescriptionFragments.DateText)]
    public string? Date { get; init; }

    [JsonPropertyName("placeHandle")]
    [Description("Place: handle, Gramps ID or #key of a place in this call.")]
    public string? PlaceHandle { get; init; }

    [JsonPropertyName("description")]
    [Description("Description.")]
    public string? Description { get; init; }

    [JsonPropertyName("citationHandles")]
    [Description(CitationsText)]
    public FlexibleHandleList? CitationHandles { get; init; }

    [JsonPropertyName("noteHandles")]
    [Description(NotesText)]
    public FlexibleHandleList? NoteHandles { get; init; }

    [JsonPropertyName("tagHandles")]
    [Description(TagsText)]
    public FlexibleHandleList? TagHandles { get; init; }

    [JsonPropertyName("mediaHandles")]
    [Description(MediaText)]
    public FlexibleMediaLinkList? MediaHandles { get; init; }

    [JsonPropertyName("attributes")]
    [Description(AttributesText)]
    public FlexibleAttributeList? Attributes { get; init; }

    [JsonPropertyName("isPrivate")]
    [Description(PrivateText)]
    public bool IsPrivate { get; init; }

    internal override string GrampsClass => "Event";

    internal override async Task<(object Request, string Label)> BuildAsync(GrampsApiClient client)
    {
        var request = await EventTools.BuildEventRequestAsync(EventType, Date, PlaceHandle, Description,
            CitationHandles, NoteHandles, TagHandles, MediaHandles, Attributes, IsPrivate, client);
        return (request, await GrampsDefaultTypeLabels.FormatEventTypeAsync(client, request.Type));
    }
}

public sealed class FamilyRecord : BatchRecord
{
    [JsonPropertyName("fatherHandle")]
    [Description("Father: handle, Gramps ID or #key of a person in this call.")]
    public string? FatherHandle { get; init; }

    [JsonPropertyName("motherHandle")]
    [Description("Mother: handle, Gramps ID or #key of a person in this call.")]
    public string? MotherHandle { get; init; }

    [JsonPropertyName("relationshipType")]
    [Description("Married (default), Unmarried, Civil Union or Unknown.")]
    public string? RelationshipType { get; init; } = "Married";

    [JsonPropertyName("childRefs")]
    [Description("Children in birth order: handles or #keys, each optionally with a relation as \"#key::Adopted\" " +
                 "or {ref, frel, mrel} (default Birth).")]
    public FlexibleChildRefList? ChildRefs { get; init; }

    [JsonPropertyName("eventRefs")]
    [Description("Family events such as Marriage: handles or #keys, optionally \"#key::Family\" or {ref, role}.")]
    public FlexibleEventRefList? EventRefs { get; init; }

    [JsonPropertyName("mediaHandles")]
    [Description(MediaText)]
    public FlexibleMediaLinkList? MediaHandles { get; init; }

    [JsonPropertyName("citationHandles")]
    [Description(CitationsText)]
    public FlexibleHandleList? CitationHandles { get; init; }

    [JsonPropertyName("noteHandles")]
    [Description(NotesText)]
    public FlexibleHandleList? NoteHandles { get; init; }

    [JsonPropertyName("tagHandles")]
    [Description(TagsText)]
    public FlexibleHandleList? TagHandles { get; init; }

    [JsonPropertyName("attributes")]
    [Description(AttributesText)]
    public FlexibleAttributeList? Attributes { get; init; }

    [JsonPropertyName("isPrivate")]
    [Description(PrivateText)]
    public bool IsPrivate { get; init; }

    internal override string GrampsClass => "Family";

    internal override async Task<(object Request, string Label)> BuildAsync(GrampsApiClient client)
    {
        var request = await FamilyTools.BuildFamilyRequestAsync(FatherHandle, MotherHandle, RelationshipType, ChildRefs,
            EventRefs, MediaHandles, CitationHandles, NoteHandles, TagHandles, Attributes, IsPrivate, client);
        return (request, await FamilyTools.FormatRelationshipAsync(request.Relationship, client));
    }
}

public sealed class CitationRecord : BatchRecord
{
    [JsonPropertyName("sourceHandle")]
    [Description("Source: handle, Gramps ID or #key of a source in this call (required).")]
    public required string SourceHandle { get; init; }

    [JsonPropertyName("page")]
    [Description("Where in the source: page, act or entry number.")]
    public FlexibleString? Page { get; init; }

    [JsonPropertyName("confidence")]
    [Description("Very Low, Low, Normal (default), High or Very High.")]
    public string Confidence { get; init; } = "Normal";

    [JsonPropertyName("date")]
    [Description("Date of the cited entry, written as an event date is.")]
    public string? Date { get; init; }

    [JsonPropertyName("noteHandles")]
    [Description(NotesText)]
    public FlexibleHandleList? NoteHandles { get; init; }

    [JsonPropertyName("mediaHandles")]
    [Description(MediaText)]
    public FlexibleMediaLinkList? MediaHandles { get; init; }

    [JsonPropertyName("tagHandles")]
    [Description(TagsText)]
    public FlexibleHandleList? TagHandles { get; init; }

    [JsonPropertyName("attributes")]
    [Description(AttributesText + " " + FlexibleAttributeList.SourceAttributesHint)]
    public FlexibleAttributeList? Attributes { get; init; }

    [JsonPropertyName("isPrivate")]
    [Description(PrivateText)]
    public bool IsPrivate { get; init; }

    internal override string GrampsClass => "Citation";

    internal override async Task<(object Request, string Label)> BuildAsync(GrampsApiClient client)
    {
        var request = await CitationTools.BuildCitationRequestAsync(SourceHandle, Page, Confidence, Date, NoteHandles,
            MediaHandles, TagHandles, Attributes, IsPrivate, client);
        return (request, request.Page ?? "");
    }
}

public sealed class NoteRecord : BatchRecord
{
    [JsonPropertyName("text")]
    [Description("Text of the note (required).")]
    public required string Text { get; init; }

    [JsonPropertyName("noteType")]
    [Description("Note type, e.g. General (default), Research, Transcript, Person Note, Event Note.")]
    public string NoteType { get; init; } = "General";

    [JsonPropertyName("format")]
    [Description("Plain (default) or Preformatted.")]
    public string Format { get; init; } = "Plain";

    [JsonPropertyName("tagHandles")]
    [Description(TagsText)]
    public FlexibleHandleList? TagHandles { get; init; }

    [JsonPropertyName("isPrivate")]
    [Description(PrivateText)]
    public bool IsPrivate { get; init; }

    internal override string GrampsClass => "Note";

    internal override async Task<(object Request, string Label)> BuildAsync(GrampsApiClient client)
    {
        var request = await NoteTools.BuildNoteRequestAsync(Text, NoteType, Format, TagHandles, IsPrivate, client);
        return (request, await NoteTools.FormatNoteTypeAsync(request.Type, client));
    }
}

public sealed class SourceRecord : BatchRecord
{
    [JsonPropertyName("title")]
    [Description("Title (required).")]
    public required string Title { get; init; }

    [JsonPropertyName("author")]
    [Description("Author or issuing body.")]
    public string? Author { get; init; }

    [JsonPropertyName("pubinfo")]
    [Description("Publication info.")]
    public string? Pubinfo { get; init; }

    [JsonPropertyName("abbrev")]
    [Description("Short title.")]
    public string? Abbrev { get; init; }

    [JsonPropertyName("repositoryHandles")]
    [Description("Repositories holding it: handles or #keys, each optionally with a call number as \"#key : 123\" " +
                 "or {ref, call_number, media_type}.")]
    public FlexibleRepositoryRefList? RepositoryHandles { get; init; }

    [JsonPropertyName("noteHandles")]
    [Description(NotesText)]
    public FlexibleHandleList? NoteHandles { get; init; }

    [JsonPropertyName("mediaHandles")]
    [Description(MediaText)]
    public FlexibleMediaLinkList? MediaHandles { get; init; }

    [JsonPropertyName("tagHandles")]
    [Description(TagsText)]
    public FlexibleHandleList? TagHandles { get; init; }

    [JsonPropertyName("attributes")]
    [Description(AttributesText + " " + FlexibleAttributeList.SourceAttributesHint)]
    public FlexibleAttributeList? Attributes { get; init; }

    [JsonPropertyName("isPrivate")]
    [Description(PrivateText)]
    public bool IsPrivate { get; init; }

    internal override string GrampsClass => "Source";

    internal override async Task<(object Request, string Label)> BuildAsync(GrampsApiClient client)
    {
        var request = await SourceTools.BuildSourceRequestAsync(Title, Author, Pubinfo, Abbrev, RepositoryHandles,
            NoteHandles, MediaHandles, TagHandles, Attributes, IsPrivate, client);
        return (request, Title);
    }
}

public sealed class RepositoryRecord : BatchRecord
{
    [JsonPropertyName("name")]
    [Description("Name (required).")]
    public required string Name { get; init; }

    [JsonPropertyName("repoType")]
    [Description("Archive, Library, Church, Web site, … (Gramps stores Library when omitted).")]
    public string? RepoType { get; init; }

    [JsonPropertyName("address")]
    [Description("Postal address as one line.")]
    public string? Address { get; init; }

    [JsonPropertyName("url")]
    [Description("Website.")]
    public string? Url { get; init; }

    [JsonPropertyName("noteHandles")]
    [Description(NotesText)]
    public FlexibleHandleList? NoteHandles { get; init; }

    [JsonPropertyName("tagHandles")]
    [Description(TagsText)]
    public FlexibleHandleList? TagHandles { get; init; }

    [JsonPropertyName("isPrivate")]
    [Description(PrivateText)]
    public bool IsPrivate { get; init; }

    internal override string GrampsClass => "Repository";

    internal override async Task<(object Request, string Label)> BuildAsync(GrampsApiClient client)
    {
        var request = await RepositoryTools.BuildRepositoryRequestAsync(Name, RepoType, Address, Url, NoteHandles,
            TagHandles, IsPrivate, client);
        return (request, Name);
    }
}

public sealed class PlaceRecord : BatchRecord
{
    [JsonPropertyName("name")]
    [Description("Name (required).")]
    public required string Name { get; init; }

    [JsonPropertyName("placeType")]
    [Description("Place type, e.g. Village, City, Parish, County, Country.")]
    public string? PlaceType { get; init; }

    [JsonPropertyName("lat")]
    [Description("Latitude, e.g. 57.62.")]
    public FlexibleString? Lat { get; init; }

    [JsonPropertyName("lon")]
    [Description("Longitude, e.g. 39.87.")]
    public FlexibleString? Lon { get; init; }

    [JsonPropertyName("enclosedBy")]
    [Description("Larger places it lies in: handles, Gramps IDs or #keys, optionally with a date span as \"#key::1708-1927\".")]
    public FlexiblePlaceRefList? EnclosedBy { get; init; }

    [JsonPropertyName("nameLang")]
    [Description("Language of the name, e.g. ru.")]
    public string? NameLang { get; init; }

    [JsonPropertyName("nameDate")]
    [Description("When the name was used, e.g. 1708-1927.")]
    public string? NameDate { get; init; }

    [JsonPropertyName("alternateNames")]
    [Description("Other names, as in create_place.")]
    public FlexiblePlaceNameList? AlternateNames { get; init; }

    [JsonPropertyName("noteHandles")]
    [Description(NotesText)]
    public FlexibleHandleList? NoteHandles { get; init; }

    [JsonPropertyName("code")]
    [Description("Code, e.g. a postal or administrative code.")]
    public string? Code { get; init; }

    [JsonPropertyName("mediaHandles")]
    [Description(MediaText)]
    public FlexibleMediaLinkList? MediaHandles { get; init; }

    [JsonPropertyName("citationHandles")]
    [Description(CitationsText)]
    public FlexibleHandleList? CitationHandles { get; init; }

    [JsonPropertyName("tagHandles")]
    [Description(TagsText)]
    public FlexibleHandleList? TagHandles { get; init; }

    [JsonPropertyName("isPrivate")]
    [Description(PrivateText)]
    public bool IsPrivate { get; init; }

    internal override string GrampsClass => "Place";

    internal override async Task<(object Request, string Label)> BuildAsync(GrampsApiClient client)
    {
        var request = await PlaceTools.BuildPlaceRequestAsync(Name, PlaceType, Lat, Lon, EnclosedBy, NameLang, NameDate,
            AlternateNames, NoteHandles, Code, MediaHandles, CitationHandles, TagHandles, IsPrivate, client);
        return (request, Name);
    }
}
