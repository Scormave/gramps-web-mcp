using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Tools;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class NotFoundHelperTests
{
    [Fact]
    public void NotFoundMessage_Says_No_Object_Has_A_Missing_Gramps_Id()
    {
        Assert.Equal(
            "Person not found: I0044\n\nHint: No person has Gramps ID I0044. " +
            "Find the person with search, or browse list_records(objectType: \"people\").",
            NotFoundHelper.NotFoundMessage("Person", "I0044"));
    }

    [Fact]
    public void NotFoundMessage_Names_The_Type_Of_A_Gramps_Id_For_Another_Type()
    {
        Assert.Equal(
            "Person not found: F0001\n\nHint: F0001 has the family prefix F; person IDs start with I. " +
            "To read the family with this ID, call get_records(identifiers: [\"F0001\"]).",
            NotFoundHelper.NotFoundMessage("Person", "F0001"));
    }

    [Fact]
    public void NotFoundMessage_Gives_The_Prefix_For_An_Unknown_One()
    {
        Assert.Equal(
            "Media not found: M0001\n\nHint: M0001 does not start with a Gramps ID prefix; " +
            "media object IDs start with O.",
            NotFoundHelper.NotFoundMessage("Media", "M0001"));
    }

    [Theory]
    [InlineData("Person", 'I', "people")]
    [InlineData("Family", 'F', "families")]
    [InlineData("Event", 'E', "events")]
    [InlineData("Place", 'P', "places")]
    [InlineData("Source", 'S', "sources")]
    [InlineData("Citation", 'C', "citations")]
    [InlineData("Repository", 'R', "repositories")]
    [InlineData("Note", 'N', "notes")]
    [InlineData("Media", 'O', "media")]
    public void NotFoundMessage_Uses_The_Prefixes_The_Resolver_Resolves(string objectType, char prefix, string collection)
    {
        Assert.Equal(collection, HandleResolver.PrefixToObjectType(prefix));
        Assert.EndsWith(
            $"list_records(objectType: \"{collection}\").",
            NotFoundHelper.NotFoundMessage(objectType, $"{prefix}0001"));
    }

    [Fact]
    public void NotFoundMessage_Says_No_Object_Has_A_Custom_Format_Id_Or_Handle()
    {
        Assert.Equal(
            "Source not found: S_ABC\n\nHint: No source has the Gramps ID or handle S_ABC. " +
            "Find the source with search, or browse list_records(objectType: \"sources\").",
            NotFoundHelper.NotFoundMessage("Source", "S_ABC"));
    }

    [Fact]
    public void NotFoundMessage_Asks_For_The_Type_Letter_Of_Bare_Digits()
    {
        Assert.Equal(
            "Person not found: 0044\n\nHint: No person has the Gramps ID or handle 0044. " +
            "Gramps IDs in the default format start with the type letter, such as I0001. " +
            "Find the person with search, or browse list_records(objectType: \"people\").",
            NotFoundHelper.NotFoundMessage("Person", "0044"));
    }

    [Theory]
    [InlineData("T0001")]
    [InlineData("family-photos")]
    public void NotFoundMessage_Says_Tags_Have_No_Gramps_Id(string identifier)
    {
        Assert.Equal(
            $"Tag not found: {identifier}\n\nHint: Tags have no Gramps ID; " +
            "find the tag's handle with list_records(objectType: \"tags\").",
            NotFoundHelper.NotFoundMessage("Tag", identifier));
    }

    [Fact]
    public void NotFoundMessage_Has_No_Hint_For_A_Handle()
    {
        Assert.Equal(
            "Person not found: 102adba5164d209d2a2b159c990c",
            NotFoundHelper.NotFoundMessage("Person", "102adba5164d209d2a2b159c990c"));
    }
}
