using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Models;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class GrampsMutationParserTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void ExtractNewObject_FromMutationArray_ParsesNoteWithStyledTextAndNoteTypeObject()
    {
        var note = GrampsMutationParser.ExtractNewObject<GrampsNote>(Fixture("note_mutation_create.json"), "Note");

        Assert.NotNull(note);
        Assert.Equal("1026866629cf221fee081e20af8", note.Handle);
        Assert.Equal("N0036", note.GrampsId);
        Assert.Equal("test", note.Text);
        Assert.Equal("1", note.Type);
    }

    [Fact]
    public void ExtractNewObject_FromMutationArray_WithoutExpectedClass_UsesFirstElement()
    {
        var note = GrampsMutationParser.ExtractNewObject<GrampsNote>(Fixture("note_mutation_create.json"), null);
        Assert.Equal("test", note.Text);
    }

    [Fact]
    public void ExtractNewObject_FromMutationUpdate_ParsesNewState()
    {
        var note = GrampsMutationParser.ExtractNewObject<GrampsNote>(Fixture("note_mutation_update.json"), "Note");
        Assert.Equal("updated body", note.Text);
        Assert.Equal("General", note.Type);
    }

    [Fact]
    public void ExtractNewObject_EventTypeObject_ParsesTypeValue()
    {
        const string json = """
            [{
              "_class":"Event",
              "handle":"evt1",
              "type":"add",
              "old":null,
              "new":{
                "_class":"Event",
                "handle":"evt1",
                "gramps_id":"E1",
                "type":{"_class":"EventType","string":"","value":12},
                "private":false
              }
            }]
            """;

        var evt = GrampsMutationParser.ExtractNewObject<GrampsEvent>(json, "Event");
        Assert.Equal("evt1", evt.Handle);
        Assert.Equal("12", evt.Type);
    }

    [Fact]
    public void ExtractNewObject_FamilyRelTypeObject_ParsesRelationshipValue()
    {
        const string json = """
            [{
              "_class":"Family",
              "handle":"fam1",
              "type":"add",
              "old":null,
              "new":{
                "_class":"Family",
                "handle":"fam1",
                "gramps_id":"F0762",
                "type":{"_class":"FamilyRelType","string":"","value":0},
                "private":false,
                "child_ref_list":[],
                "father_handle":null,
                "mother_handle":null
              }
            }]
            """;

        var fam = GrampsMutationParser.ExtractNewObject<GrampsFamily>(json, "Family");
        Assert.Equal("fam1", fam.Handle);
        Assert.Equal("0", fam.Relationship);
    }

    [Fact]
    public void ExtractNewObject_FromBareEntity_DoesNotRequireWrapper()
    {
        const string json = """
            {
              "handle": "h1",
              "gramps_id": "N99",
              "text": "plain",
              "type": "General",
              "format": 0,
              "tag_list": []
            }
            """;
        var note = GrampsMutationParser.ExtractNewObject<GrampsNote>(json, null);
        Assert.Equal("h1", note.Handle);
        Assert.Equal("plain", note.Text);
    }

    [Fact]
    public void ExtractNewObject_FromObjectWithNewProperty_Unwraps()
    {
        const string json = """
            {
              "type": "add",
              "new": {
                "handle": "h2",
                "gramps_id": "N2",
                "text": "wrapped",
                "type": "Research",
                "format": 0
              }
            }
            """;
        var note = GrampsMutationParser.ExtractNewObject<GrampsNote>(json, null);
        Assert.Equal("wrapped", note.Text);
        Assert.Equal("Research", note.Type);
    }

    [Fact]
    public void ExtractNewObject_EmptyArray_Throws()
    {
        const string json = "[]";
        var ex = Assert.Throws<InvalidOperationException>(() =>
            GrampsMutationParser.ExtractNewObject<GrampsNote>(json, null));
        Assert.Contains("empty array", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExtractNewObject_MissingExpectedClass_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            GrampsMutationParser.ExtractNewObject<GrampsNote>(Fixture("note_mutation_create.json"), "Person"));
        Assert.Contains("No mutation entry", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExtractNewObject_EmptyBody_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            GrampsMutationParser.ExtractNewObject<GrampsNote>("   ", null));
        Assert.Contains("Empty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
