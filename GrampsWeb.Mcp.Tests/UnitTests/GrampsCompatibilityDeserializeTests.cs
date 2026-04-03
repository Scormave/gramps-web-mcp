using System.Text.Json;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Requests;
using GrampsWeb.Mcp.Serialization;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

/// <summary>
/// Regression tests for Gramps API JSON shapes; uses the same options as <see cref="Client.GrampsApiClient"/>.
/// </summary>
public class GrampsCompatibilityDeserializeTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void Deserialize_Person_PolymorphicRefLists()
    {
        var p = JsonSerializer.Deserialize<GrampsPerson>(Fixture("person_parent_families_mixed.json"), GrampsJson.Options);
        Assert.NotNull(p);
        Assert.NotNull(p.ParentFamilyList);
        Assert.Equal(2, p.ParentFamilyList!.Length);
        Assert.Equal("FAM_HANDLE_STRING", p.ParentFamilyList[0].Ref);
        Assert.Equal("FAM_HANDLE_OBJ", p.ParentFamilyList[1].Ref);
        Assert.Equal("Birth", p.ParentFamilyList[1].FatherRelationship);

        Assert.NotNull(p.FamilyList);
        Assert.Equal(new[] { "CHILD_FAM1", "CHILD_FAM2" }, p.FamilyList);

        Assert.NotNull(p.MediaList);
        Assert.Equal(new[] { "MEDIA1", "MEDIA2" }, p.MediaList);
    }

    [Fact]
    public void Deserialize_Person_PrimaryName_GrampsWebApiShape_ResolvesDisplayName()
    {
        var p = JsonSerializer.Deserialize<GrampsPerson>(Fixture("person_primary_name_only.json"), GrampsJson.Options);
        Assert.NotNull(p);
        Assert.NotNull(p.PrimaryName);
        Assert.Equal("Anna", p.PrimaryName!.FirstName);
        Assert.Contains("Smith", GrampsValueFormatter.FormatName(p.PrimaryName));
        Assert.NotNull(p.AlternateNames);
        Assert.Single(p.AlternateNames!);
        Assert.Contains("Jones", GrampsValueFormatter.FormatName(p.AlternateNames![0]));
    }

    [Fact]
    public void Deserialize_Name_CallPropertyPerApispec()
    {
        const string json = """
            {
              "first_name": "John",
              "call": "Johnny",
              "surname_list": [{ "surname": "Doe" }]
            }
            """;
        var n = JsonSerializer.Deserialize<GrampsName>(json, GrampsJson.Options);
        Assert.NotNull(n);
        Assert.Equal("Johnny", n!.Call);
        Assert.Contains("Johnny", GrampsValueFormatter.FormatName(n));
    }

    [Fact]
    public void Deserialize_Source_Reporef_StringOrObject()
    {
        var s = JsonSerializer.Deserialize<GrampsSource>(Fixture("source_reporef_mixed.json"), GrampsJson.Options);
        Assert.NotNull(s?.RepositoryRefList);
        Assert.Equal(2, s!.RepositoryRefList!.Length);
        Assert.Equal("REPO_STRING", s.RepositoryRefList[0].Ref);
        Assert.Equal("REPO_OBJ", s.RepositoryRefList[1].Ref);
        Assert.Equal("A-1", s.RepositoryRefList[1].CallNumber);
        Assert.Equal("Book", s.RepositoryRefList[1].MediaType);
    }

    [Fact]
    public void Deserialize_SearchHit_UsesScoreField()
    {
        var hit = JsonSerializer.Deserialize<GrampsSearchHit>(Fixture("search_hit_score.json"), GrampsJson.Options);
        Assert.NotNull(hit);
        Assert.Equal("abc123", hit!.Handle);
        Assert.True(Math.Abs(hit.Score - 5.522300827719273) < 1e-9);
        Assert.Equal(0, hit.Rank);
    }

    [Fact]
    public void Deserialize_Place_WireKeysMatchApispecPropertyNames()
    {
        var place = JsonSerializer.Deserialize<GrampsPlace>(Fixture("place_wire_keys.json"), GrampsJson.Options);
        Assert.NotNull(place);
        Assert.Equal("YNUJQC8YM5EGRG868J", place!.Handle);
        Assert.Equal("Boise", place.Name);
        Assert.Equal("City", place.Type);
        Assert.Equal("-114.4608711", place.Longitude);
        Assert.Equal("42.5629668", place.Latitude);
    }

    [Fact]
    public void Deserialize_Note_TextAsStyledTextObject_ExtractsStringProperty()
    {
        const string json = """
            {
              "handle": "b39feb55e1173f4a699",
              "text": { "string": "Line one\nLine two", "tags": [] },
              "type": "General",
              "format": 0
            }
            """;
        var note = JsonSerializer.Deserialize<GrampsNote>(json, GrampsJson.Options);
        Assert.NotNull(note);
        Assert.Equal("Line one\nLine two", note!.Text);
    }

    [Fact]
    public void Deserialize_Place_NameAsPlaceNameObject_ExtractsValue()
    {
        const string json = """
            {
              "handle": "H1",
              "name": { "value": "Twin Falls", "lang": "" },
              "place_type": "City"
            }
            """;
        var place = JsonSerializer.Deserialize<GrampsPlace>(json, GrampsJson.Options);
        Assert.NotNull(place);
        Assert.Equal("Twin Falls", place!.Name);
    }

    [Fact]
    public void ParsePagedList_ArrayRoot_MapsToObjectsAndTotal()
    {
        var r = GrampsPagedResultParser.Parse<GrampsPerson>(Fixture("paged_people_array.json"), GrampsJson.Options);
        Assert.NotNull(r?.Objects);
        Assert.Equal(2, r!.Objects!.Length);
        Assert.Equal("P1", r.Objects[0].Handle);
        Assert.Equal(2, r.Total);
        Assert.Equal(1, r.Page);
    }

    [Fact]
    public void ParsePagedList_ObjectRoot_UsesMetadata()
    {
        var r = GrampsPagedResultParser.Parse<GrampsPerson>(Fixture("paged_people_object.json"), GrampsJson.Options);
        Assert.NotNull(r?.Objects);
        Assert.Single(r!.Objects!);
        Assert.Equal("PX", r.Objects[0].Handle);
        Assert.Equal(42, r.Total);
        Assert.Equal(3, r.Page);
    }

    [Fact]
    public void Deserialize_Family_ChildRef_StringOrObject()
    {
        var f = JsonSerializer.Deserialize<GrampsFamily>(Fixture("family_child_ref_string.json"), GrampsJson.Options);
        Assert.NotNull(f?.ChildRefList);
        Assert.Equal(2, f!.ChildRefList!.Length);
        Assert.Equal("CHILD1", f.ChildRefList[0].Ref);
        Assert.Equal("CHILD2", f.ChildRefList[1].Ref);
        Assert.Equal("Birth", f.ChildRefList[1].FatherRelType);
    }

    [Fact]
    public void JsonOptions_SkipsUnmappedMembers()
    {
        const string json = """
            {
              "handle": "H",
              "gender": 2,
              "future_api_field": { "x": 1 },
              "note_list": []
            }
            """;
        var p = JsonSerializer.Deserialize<GrampsPerson>(json, GrampsJson.Options);
        Assert.NotNull(p);
        Assert.Equal("H", p!.Handle);
    }

    [Fact]
    public void Serialize_CreateEvent_OmitsNullAndEmptyCollections()
    {
        var req = new CreateEventRequest
        {
            Type = "Birth",
            Place = null,
            Description = null,
            NoteList = Array.Empty<string>(),
            CitationList = Array.Empty<string>(),
            TagList = Array.Empty<string>()
        };

        var json = JsonSerializer.Serialize(req, GrampsJson.Options);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("type", out var typeEl));
        Assert.Equal("Birth", typeEl.GetString());
        Assert.False(root.TryGetProperty("place", out _));
        Assert.False(root.TryGetProperty("description", out _));
        Assert.False(root.TryGetProperty("note_list", out _));
        Assert.False(root.TryGetProperty("citation_list", out _));
        Assert.False(root.TryGetProperty("tag_list", out _));
    }

    [Fact]
    public void DateMapping_EmptyDate_BecomesNullAndIsOmitted()
    {
        var emptyDate = new GrampsDate
        {
            Calendar = 0,
            Modifier = 0,
            Quality = 0,
            Text = null,
            NewYear = 0
        };

        var req = new CreateEventRequest
        {
            Type = "Birth",
            Date = GrampsRequestMapping.ToDateRequestOrNull(emptyDate)
        };

        Assert.Null(req.Date);

        var json = JsonSerializer.Serialize(req, GrampsJson.Options);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.TryGetProperty("date", out _));
    }

    [Fact]
    public void Deserialize_Event_TypeAsString_Parses()
    {
        const string json = """
            {
              "handle": "E1",
              "gramps_id": "E0001",
              "type": "Birth",
              "private": false
            }
            """;
        var evt = JsonSerializer.Deserialize<GrampsEvent>(json, GrampsJson.Options);
        Assert.NotNull(evt);
        Assert.Equal("Birth", evt!.Type);
    }

    [Fact]
    public void Deserialize_Event_TypeAsObject_Parses()
    {
        const string json = """
            {
              "handle": "E2",
              "gramps_id": "E0002",
              "type": { "_class": "EventType", "string": "", "value": 12 },
              "private": false
            }
            """;
        var evt = JsonSerializer.Deserialize<GrampsEvent>(json, GrampsJson.Options);
        Assert.NotNull(evt);
        Assert.Equal("12", evt!.Type);
    }

    [Fact]
    public void Deserialize_GrampsDate_FromDatevalArray_PopulatesSegments()
    {
        const string json = """
            {
              "calendar": 0,
              "modifier": 0,
              "quality": 0,
              "dateval": [15, 3, 1990, false],
              "text": "",
              "newyear": 0,
              "sortval": 19900315
            }
            """;
        var d = JsonSerializer.Deserialize<GrampsDate>(json, GrampsJson.Options);
        Assert.NotNull(d);
        Assert.Equal(15, d!.Day);
        Assert.Equal(3, d.Month);
        Assert.Equal(1990, d.Year);
        Assert.False(d.Slash);
        Assert.Equal(19900315, d.SortVal);
    }

    [Fact]
    public void Serialize_DateRequest_RoundTripsDateval()
    {
        var req = new CreateEventRequest
        {
            Type = "Birth",
            Date = new DateRequest
            {
                Calendar = 0,
                Modifier = 0,
                Day = 1,
                Month = 2,
                Year = 2003,
                Slash = false
            }
        };
        var json = JsonSerializer.Serialize(req, GrampsJson.Options);
        using var doc = JsonDocument.Parse(json);
        var dateEl = doc.RootElement.GetProperty("date");
        var arr = dateEl.GetProperty("dateval");
        Assert.Equal(1, arr[0].GetInt32());
        Assert.Equal(2, arr[1].GetInt32());
        Assert.Equal(2003, arr[2].GetInt32());
        Assert.False(arr[3].GetBoolean());
    }
}
