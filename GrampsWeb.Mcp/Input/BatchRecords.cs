using System.ComponentModel;
using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Requests;
using GrampsWeb.Mcp.Tools;

namespace GrampsWeb.Mcp.Input;

/// <summary>
/// One record of create_records: the fields of one new person, family, event or other record, plus a key other
/// records of the same call refer to it by as <c>"#key"</c>.
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

    /// <summary>The checked request body and a label for the result.</summary>
    internal abstract Task<(object Request, string Label)> BuildAsync(GrampsApiClient client);

    internal const string CitationsText = "Citations: handles of existing ones or #keys of citations in this call.";
    internal const string NotesText = "Notes: handles of existing ones or #keys of notes in this call.";
    internal const string TagsText = "Tags: handles of existing ones or #keys of tags in this call.";
    internal const string MediaText =
        "Existing media, uploaded first with upload_media: handles, or {\"ref\": …, \"region\": [left, top, right, bottom]} " +
        "to show one part of an image, such as one entry on a page scan. " + MediaRegion.DescriptionHint;
    internal const string AttributesText = FlexibleAttributeList.DescriptionHint;
    internal const string PrivateText = "Mark the record private (default false).";
}

public sealed class PersonRecord : BatchRecord
{
    [JsonPropertyName("primaryName")]
    [Description("Main name, usually the birth name (required); a married name or another spelling goes in alternateNames. " +
                 FlexibleGrampsName.DescriptionHint)]
    public required FlexibleGrampsName PrimaryName { get; init; }

    [JsonPropertyName("gender")]
    [Description("Female, Male or Unknown (default).")]
    public string Gender { get; init; } = "Unknown";

    [JsonPropertyName("alternateNames")]
    [Description("Other names, such as a married name or another spelling. " + FlexibleAlternateNameList.DescriptionHint)]
    public FlexibleAlternateNameList? AlternateNames { get; init; }

    [JsonPropertyName("eventRefs")]
    [Description("Events the person takes part in: handles or #keys, each optionally with a role as \"#key::Witness\" " +
                 "or {ref, role} (default Primary). The first Birth and the first Death in the Primary role become the " +
                 "person's birth and death; in another role, such as Godparent, the event is only linked. A role must be a " +
                 "standard Gramps one or a custom one the tree already uses. The person's own details from the record, such " +
                 "as the age a register gives, go on the entry: {\"ref\": \"#burial\", \"attributes\": [\"Age: 65\"]}.")]
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
    [Description(FlexibleAddressList.DescriptionHint)]
    public FlexibleAddressList? Addresses { get; init; }

    [JsonPropertyName("urls")]
    [Description(FlexibleUrlList.DescriptionHint)]
    public FlexibleUrlList? Urls { get; init; }

    [JsonPropertyName("personAssociations")]
    [Description("Associations with other people: handles or #keys with a relation, \"#key:: Godfather\" or {ref, rel}. " +
                 "Grammar: gramps://input-guide.")]
    public FlexiblePersonRefList? PersonAssociations { get; init; }

    [JsonPropertyName("isPrivate")]
    [Description(PrivateText)]
    public bool IsPrivate { get; init; }

    [JsonPropertyName("notDuplicateOf")]
    [Description("People of the tree who are someone else, from the possible duplicates of a refused call: " +
                 "Gramps IDs or handles, e.g. [\"I0012\"]. The duplicate check passes over them.")]
    public FlexibleHandleList? NotDuplicateOf { get; init; }

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
    [Description("Event type, e.g. Birth, Baptism, Death, Burial, Marriage, Residence, Occupation (required). " +
                 ToolDescriptionFragments.KnownType)]
    public required string EventType { get; init; }

    [JsonPropertyName("date")]
    [Description(ToolDescriptionFragments.DateText)]
    public string? Date { get; init; }

    [JsonPropertyName("placeHandle")]
    [Description("Place: handle, Gramps ID or #key of a place in this call.")]
    public string? PlaceHandle { get; init; }

    [JsonPropertyName("description")]
    [Description("Short description.")]
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
    [Description("Married, Unmarried, Civil Union or Unknown (default, as in Gramps). Pass Married when a source " +
                 "records the marriage.")]
    public string? RelationshipType { get; init; } = "Unknown";

    [JsonPropertyName("childRefs")]
    [Description("Children in birth order, kept as given: handles or #keys, each optionally with its relation to the " +
                 "father and the mother as \"#key::Adopted\" (both) or {ref, frel, mrel} (default Birth). A relation must " +
                 "be a standard Gramps one (Birth, Adopted, Stepchild, Sponsored, Foster, Unknown, None) or a custom one " +
                 "the tree already uses.")]
    public FlexibleChildRefList? ChildRefs { get; init; }

    [JsonPropertyName("eventRefs")]
    [Description("Family events such as Marriage or Divorce: handles or #keys, each optionally with a role as " +
                 "\"#key::Role\" or {ref, role}. The default Family role makes Gramps show the family among the " +
                 "event's participants.")]
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
    [Description("Where in the source: page, act or entry number. " + FlexibleString.DescriptionHint)]
    public FlexibleString? Page { get; init; }

    [JsonPropertyName("confidence")]
    [Description("How reliable the cited record is: Very Low, Low, Normal (default), High or Very High.")]
    public string Confidence { get; init; } = "Normal";

    [JsonPropertyName("date")]
    [Description("Date of the cited entry. " + ToolDescriptionFragments.DateText)]
    public string? Date { get; init; }

    [JsonPropertyName("noteHandles")]
    [Description("Notes, such as the transcript of the entry, a note of type Transcript: handles of existing ones or " +
                 "#keys of notes in this call.")]
    public FlexibleHandleList? NoteHandles { get; init; }

    [JsonPropertyName("mediaHandles")]
    [Description("The scan of the entry. " + MediaText)]
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
    [Description("Note type, e.g. General (default), Research, Transcript, Person Note, Event Note. " +
                 ToolDescriptionFragments.KnownType)]
    public string NoteType { get; init; } = "General";

    [JsonPropertyName("format")]
    [Description("Text format: Plain (default) or Html.")]
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
    [Description("Title naming the document, such as a parish register for one year (required).")]
    public required string Title { get; init; }

    [JsonPropertyName("author")]
    [Description("Who made it: author or issuing body.")]
    public string? Author { get; init; }

    [JsonPropertyName("pubinfo")]
    [Description("Publication info of a published work: publisher, place, year.")]
    public string? Pubinfo { get; init; }

    [JsonPropertyName("abbrev")]
    [Description("Short title for lists.")]
    public string? Abbrev { get; init; }

    [JsonPropertyName("repositoryHandles")]
    [Description("Repositories holding it: handles or #keys, each with the source's call number there, the archive's " +
                 "shelf mark such as fond, inventory and file, and the media type it is held in: \"#key : CallNumber : " +
                 "MediaType\" (later parts optional, \"#key :: Film\" gives only the type) or {ref, call_number, " +
                 "media_type}. A media type must be a standard Gramps one (Book, Manuscript, Film, Electronic, …) or a " +
                 "custom one the tree already uses.")]
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
    [Description("Name, e.g. \"State Archive in Warsaw\" (required).")]
    public required string Name { get; init; }

    [JsonPropertyName("repoType")]
    [Description("Repository type, e.g. Archive, Library, Church, Web site (Gramps stores Library when omitted). " +
                 ToolDescriptionFragments.KnownType)]
    public string? RepoType { get; init; }

    [JsonPropertyName("address")]
    [Description("Postal address as one line, stored as the street; for a city, postal code or country use addresses.")]
    public string? Address { get; init; }

    [JsonPropertyName("addresses")]
    [Description("Addresses with separate fields, not together with address. " + FlexibleAddressList.DescriptionHint)]
    public FlexibleAddressList? Addresses { get; init; }

    [JsonPropertyName("url")]
    [Description("Website, stored as a Web Home link; for other link types or a description use urls.")]
    public string? Url { get; init; }

    [JsonPropertyName("urls")]
    [Description("Links with a type and description, not together with url. " + FlexibleUrlList.DescriptionHint)]
    public FlexibleUrlList? Urls { get; init; }

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
        var request = await RepositoryTools.BuildRepositoryRequestAsync(Name, RepoType, Address, Addresses, Url,
            Urls, NoteHandles, TagHandles, IsPrivate, client);
        return (request, Name);
    }
}

public sealed class TagRecord : BatchRecord
{
    [JsonPropertyName("name")]
    [Description("Tag name shown on records, such as \"To check\" (required). No other tag may have it, in any case: " +
                 "Gramps filters find a tag by its name.")]
    public required string Name { get; init; }

    [JsonPropertyName("color")]
    [Description("Color as six hex digits RRGGBB, with or without #, e.g. FF5733; stored as #ff5733. Default 000000 (black).")]
    public string Color { get; init; } = "000000";

    [JsonPropertyName("priority")]
    [Description("Sort priority among tags in Gramps (default 0).")]
    public int Priority { get; init; }

    internal override string GrampsClass => "Tag";

    internal override Task<(object Request, string Label)> BuildAsync(GrampsApiClient client)
    {
        if (string.IsNullOrWhiteSpace(Name))
            throw McpToolErrors.ValidationError("Error: name is required");
        var request = new CreateTagRequest { Name = Name.Trim(), Color = TagTools.NormalizeColor(Color ?? "000000"), Priority = Priority };
        return Task.FromResult<(object, string)>((request, request.Name));
    }
}

public sealed class PlaceRecord : BatchRecord
{
    [JsonPropertyName("name")]
    [Description("Primary name (required).")]
    public required string Name { get; init; }

    [JsonPropertyName("placeType")]
    [Description("Place type, e.g. Country, Province, County, City, Town, Village, Hamlet, Parish. " +
                 ToolDescriptionFragments.KnownType)]
    public string? PlaceType { get; init; }

    [JsonPropertyName("lat")]
    [Description(PlaceTools.LatHint)]
    public FlexibleString? Lat { get; init; }

    [JsonPropertyName("lon")]
    [Description(PlaceTools.LonHint)]
    public FlexibleString? Lon { get; init; }

    [JsonPropertyName("enclosedBy")]
    [Description("Larger places it lies in: handles, Gramps IDs or #keys, each optionally with the dates it belonged " +
                 "there, as \"#key::1708-1927\" or {ref, date}. Dashes are spans; open \"1991-\" / \"from 1991\" are " +
                 "From. Unreadable dates are rejected.")]
    public FlexiblePlaceRefList? EnclosedBy { get; init; }

    [JsonPropertyName("nameLang")]
    [Description("Language code of the primary name, e.g. pl or de.")]
    public string? NameLang { get; init; }

    [JsonPropertyName("nameDate")]
    [Description("When the primary name applies (default: always). " + PlaceTools.NameDateHint)]
    public string? NameDate { get; init; }

    [JsonPropertyName("alternateNames")]
    [Description("Other names: historical, other languages. " + FlexiblePlaceNameList.DescriptionHint)]
    public FlexiblePlaceNameList? AlternateNames { get; init; }

    [JsonPropertyName("noteHandles")]
    [Description(NotesText)]
    public FlexibleHandleList? NoteHandles { get; init; }

    [JsonPropertyName("code")]
    [Description("Place code or postal code.")]
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
