using System.Text.Json;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Serialization;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class FlexibleGrampsNameTests
{
    private static FlexibleGrampsName? Deserialize(string json)
        => JsonSerializer.Deserialize<FlexibleGrampsName?>(json);

    [Fact]
    public void Json_Object_RoundTrip()
    {
        const string json = """{"first_name":"A","surname_list":[{"surname":"B","primary":true}]}""";
        var v = Deserialize(json);
        Assert.NotNull(v);
        Assert.Equal("A", v!.Name.FirstName);
        Assert.Single(v.Name.SurnameList!);
        Assert.Equal("B", v.Name.SurnameList![0].Surname);
    }

    [Fact]
    public void String_Pipe_Given_Surname()
    {
        var v = Deserialize("\"Jane|Smith\"");
        Assert.NotNull(v);
        Assert.Equal("Jane", v!.Name.FirstName);
        Assert.Equal("Smith", v.Name.SurnameList![0].Surname);
    }

    [Fact]
    public void String_Last_Space()
    {
        var v = Deserialize("\"Mary Ann Jones\"");
        Assert.Equal("Mary Ann", v!.Name.FirstName);
        Assert.Equal("Jones", v.Name.SurnameList![0].Surname);
    }

    [Fact]
    public void String_Type_Double_Colon()
    {
        var v = Deserialize("\"Married Name:: Anna|Kovacs\"");
        Assert.Equal("Married Name", v!.Name.Type);
        Assert.Equal("Anna", v.Name.FirstName);
        Assert.Equal("Kovacs", v.Name.SurnameList![0].Surname);
    }

    [Fact]
    public void Single_Word_Surname_Empty()
    {
        var v = Deserialize("\"Madonna\"");
        Assert.Equal("Madonna", v!.Name.FirstName);
        Assert.Equal("", v.Name.SurnameList![0].Surname);
    }

    [Fact]
    public void Null_Json()
    {
        Assert.Null(Deserialize("null"));
    }

    [Fact]
    public void ParseSimpleLine_Empty_Throws()
    {
        Assert.Throws<JsonException>(() => FlexibleGrampsNameParsing.ParseSimpleLine("   "));
    }

    // ── AI-friendly object aliases ──────────────────────────────────────────

    [Fact]
    public void Object_Given_Surname_Patronymic_Type_Fields()
    {
        // given + surname + patronymic → surname_list with two entries
        const string json = """{"given":"Alexandra","surname":"Andreeva","patronymic":"Andreyevna","type":"Married Name"}""";
        var v = Deserialize(json);
        Assert.NotNull(v);
        Assert.Equal("Alexandra", v!.Name.FirstName);
        Assert.Equal(2, v.Name.SurnameList!.Length);
        Assert.Equal("Andreeva", v.Name.SurnameList[0].Surname);
        Assert.True(v.Name.SurnameList[0].Primary);
        Assert.Equal("Andreyevna", v.Name.SurnameList[1].Surname);
        Assert.False(v.Name.SurnameList[1].Primary);
        Assert.Equal("Patronymic", v.Name.SurnameList[1].OriginType);
        Assert.Equal("Married Name", v.Name.Type);
    }

    [Fact]
    public void Object_Patronymic_Only_Becomes_Primary_Surname()
    {
        // When only patronymic is provided (no surname), it becomes the primary surname entry.
        const string json = """{"given":"Ivan","patronymic":"Petrovich"}""";
        var v = Deserialize(json);
        Assert.NotNull(v);
        Assert.Equal("Ivan", v!.Name.FirstName);
        Assert.NotNull(v.Name.SurnameList);
        Assert.Single(v.Name.SurnameList);
        Assert.Equal("Petrovich", v.Name.SurnameList![0].Surname);
        Assert.True(v.Name.SurnameList[0].Primary);
        Assert.Equal("Patronymic", v.Name.SurnameList[0].OriginType);
    }

    [Fact]
    public void Object_Surname_Prefix_Von()
    {
        // German/Dutch particle before surname
        const string json = """{"given":"Ludwig","surname":"Beethoven","prefix":"van","type":"Birth Name"}""";
        var v = Deserialize(json);
        Assert.NotNull(v);
        Assert.Equal("Ludwig", v!.Name.FirstName);
        Assert.NotNull(v.Name.SurnameList);
        Assert.Single(v.Name.SurnameList);
        Assert.Equal("Beethoven", v.Name.SurnameList![0].Surname);
        Assert.Equal("van", v.Name.SurnameList[0].Prefix);
        Assert.True(v.Name.SurnameList[0].Primary);
        Assert.Equal("Birth Name", v.Name.Type);
    }

    [Fact]
    public void Object_Spanish_Double_Surname_Via_Native_Format()
    {
        // Spanish naming via native surname_list: Patrilineal (father) + Matrilineal (mother)
        const string json = """
            {
                "first_name": "Maria",
                "surname_list": [
                    {"surname": "Garcia", "primary": true, "origintype": {"string": "Patrilineal"}},
                    {"surname": "Lopez",  "primary": false, "origintype": {"string": "Matrilineal"}}
                ]
            }
            """;
        var v = Deserialize(json);
        Assert.NotNull(v);
        Assert.Equal("Maria", v!.Name.FirstName);
        Assert.NotNull(v.Name.SurnameList);
        Assert.Equal(2, v.Name.SurnameList!.Length);
        Assert.Equal("Garcia", v.Name.SurnameList[0].Surname);
        Assert.Equal("Lopez", v.Name.SurnameList[1].Surname);
    }

    [Fact]
    public void Object_Surname_With_Patrilineal_Origin_Type()
    {
        // origin_type maps to the primary surname's OriginType
        const string json = """{"given":"Jose","surname":"Garcia","origin_type":"Patrilineal"}""";
        var v = Deserialize(json);
        Assert.NotNull(v);
        Assert.NotNull(v!.Name.SurnameList);
        Assert.Single(v.Name.SurnameList!);
        Assert.Equal("Garcia", v.Name.SurnameList[0].Surname);
        Assert.Equal("Patrilineal", v.Name.SurnameList[0].OriginType);
    }

    [Fact]
    public void Object_Call_And_FamNick()
    {
        const string json = """{"given":"William","surname":"Gates","call":"Bill","famnick":"Billsy"}""";
        var v = Deserialize(json);
        Assert.NotNull(v);
        Assert.Equal("Bill", v!.Name.Call);
        Assert.Equal("Billsy", v.Name.FamNick);
    }

    [Fact]
    public void Object_Suffix_Jr()
    {
        const string json = """{"given":"John","surname":"Smith","suffix":"Jr."}""";
        var v = Deserialize(json);
        Assert.NotNull(v);
        Assert.Equal("Jr.", v!.Name.Suffix);
    }

    [Fact]
    public void Object_Surname_With_Custom_Origin_Type()
    {
        const string json = """{"given":"Ivan","surname":"Smirnov","origin_type":"Taken"}""";
        var v = Deserialize(json);
        Assert.NotNull(v);
        Assert.Equal("Taken", v!.Name.SurnameList![0].OriginType);
    }

    [Fact]
    public void Object_Name_Field_With_Type()
    {
        // name field as full-name string + explicit type
        const string json = """{"name":"Alexandra Andreyevna Andreeva","type":"Married Name"}""";
        var v = Deserialize(json);
        Assert.NotNull(v);
        Assert.Equal("Alexandra Andreyevna", v!.Name.FirstName);
        Assert.Equal("Andreeva", v.Name.SurnameList![0].Surname);
        Assert.Equal("Married Name", v.Name.Type);
    }

    [Fact]
    public void Object_Text_Field()
    {
        // text field as full-name string alternative
        const string json = """{"text":"Alexandra Andreyevna Andreeva","type":"Married Name"}""";
        var v = Deserialize(json);
        Assert.NotNull(v);
        Assert.Equal("Married Name", v!.Name.Type);
        Assert.False(string.IsNullOrEmpty(v.Name.FirstName));
    }

    [Fact]
    public void Object_Given_Only_No_Surname()
    {
        const string json = """{"given":"Ivan","type":"Birth Name"}""";
        var v = Deserialize(json);
        Assert.NotNull(v);
        Assert.Equal("Ivan", v!.Name.FirstName);
        Assert.Equal("Birth Name", v.Name.Type);
        Assert.Equal("", v.Name.SurnameList![0].Surname);
    }

    [Fact]
    public void Object_Native_Fields_Unchanged()
    {
        // native Gramps fields must still work as before
        const string json = """{"first_name":"Ivan","surname_list":[{"surname":"Petrov","primary":true}],"type":"Birth Name"}""";
        var v = Deserialize(json);
        Assert.NotNull(v);
        Assert.Equal("Ivan", v!.Name.FirstName);
        Assert.Equal("Petrov", v.Name.SurnameList![0].Surname);
    }
}
