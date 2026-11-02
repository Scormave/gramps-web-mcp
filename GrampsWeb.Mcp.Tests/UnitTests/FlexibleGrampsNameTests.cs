using System.Text.Json;
using GrampsWeb.Mcp.Exceptions;
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
        Assert.Throws<JsonInputException>(() => FlexibleGrampsNameParsing.ParseSimpleLine("   "));
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

    [Fact]
    public void Stringified_Object_Given_Surname_Patronymic()
    {
        const string json =
            "\"{\\\"given\\\":\\\"Петр\\\",\\\"surname\\\":\\\"Соколов\\\",\\\"patronymic\\\":\\\"Иванович\\\",\\\"type\\\":\\\"Birth Name\\\"}\"";
        var v = Deserialize(json);
        Assert.NotNull(v);
        Assert.Equal("Петр", v!.Name.FirstName);
        Assert.Equal(2, v.Name.SurnameList!.Length);
        Assert.Equal("Соколов", v.Name.SurnameList[0].Surname);
        Assert.True(v.Name.SurnameList[0].Primary);
        Assert.Equal("Иванович", v.Name.SurnameList[1].Surname);
        Assert.False(v.Name.SurnameList[1].Primary);
        Assert.Equal("Patronymic", v.Name.SurnameList[1].OriginType);
        Assert.Equal("Birth Name", v.Name.Type);
    }

    [Fact]
    public void Stringified_Object_Native_FirstName_SurnameList()
    {
        const string json =
            "\"{\\\"first_name\\\":\\\"Петр\\\",\\\"surname_list\\\":[{\\\"surname\\\":\\\"Соколов\\\",\\\"primary\\\":true,\\\"origintype\\\":\\\"Inherited\\\"},{\\\"surname\\\":\\\"Иванович\\\",\\\"primary\\\":false,\\\"origintype\\\":\\\"Patronymic\\\"}]}\"";
        var v = Deserialize(json);
        Assert.NotNull(v);
        Assert.Equal("Петр", v!.Name.FirstName);
        Assert.Equal(2, v.Name.SurnameList!.Length);
        Assert.Equal("Соколов", v.Name.SurnameList[0].Surname);
        Assert.Equal("Иванович", v.Name.SurnameList[1].Surname);
        Assert.Equal("Patronymic", v.Name.SurnameList[1].OriginType);
    }

    [Fact]
    public void Stringified_Invalid_Json_Throws()
    {
        const string json = "\"{\\\"given\\\":\\\"Broken\\\"\"";
        Assert.Throws<JsonInputException>(() => Deserialize(json));
    }

    [Fact]
    public void Stringified_Object_ConvertsToApiRequest()
    {
        const string json =
            "\"{\\\"given\\\":\\\"Петр\\\",\\\"surname\\\":\\\"Соколов\\\",\\\"patronymic\\\":\\\"Иванович\\\",\\\"type\\\":\\\"Birth Name\\\"}\"";
        var v = Deserialize(json);
        Assert.NotNull(v);

        var request = GrampsWeb.Mcp.Tools.PersonTools.ConvertNameToRequest(v!.Name);
        Assert.Equal("Петр", request.FirstName);
        Assert.NotNull(request.SurnameList);
        Assert.Equal(2, request.SurnameList!.Length);
        Assert.Equal("Соколов", request.SurnameList[0].Surname);
        Assert.True(request.SurnameList[0].Primary);
        Assert.Equal("Иванович", request.SurnameList[1].Surname);
        Assert.False(request.SurnameList[1].Primary);
        Assert.Equal("Patronymic", request.SurnameList[1].OriginType);
    }

    [Fact]
    public void Object_SurnamePartsAndNonPatronymicOrigin_ConvertToApiRequest()
    {
        const string json =
            """{"given":"Ludwig","surname":"Beethoven","prefix":"van","connector":"und","origin_type":"Inherited"}""";
        var v = Deserialize(json);
        Assert.NotNull(v);

        var request = GrampsWeb.Mcp.Tools.PersonTools.ConvertNameToRequest(v!.Name);

        Assert.NotNull(request.SurnameList);
        Assert.Single(request.SurnameList!);
        Assert.Equal("Beethoven", request.SurnameList[0].Surname);
        Assert.Equal("van", request.SurnameList[0].Prefix);
        Assert.Equal("und", request.SurnameList[0].Connector);
        Assert.Equal("Inherited", request.SurnameList[0].OriginType);
        Assert.True(request.SurnameList[0].Primary);
    }

    [Fact]
    public void Object_CamelCase_SurnameList_Keeps_Every_Surname()
    {
        // Used to come out as "Федор" alone: surnameList was not first_name/surname_list, so it was dropped.
        const string json = """
            {"first":"Федор","surnameList":[
                {"surname":"Пробнякин","primary":true},
                {"surname":"Иванович","originType":"Patronymic"}]}
            """;
        var name = Deserialize(json)!.Name;

        Assert.Equal("Федор", name.FirstName);
        Assert.Equal(2, name.SurnameList!.Length);
        Assert.Equal("Пробнякин", name.SurnameList[0].Surname);
        Assert.True(name.SurnameList[0].Primary);
        Assert.Equal("Иванович", name.SurnameList[1].Surname);
        Assert.Equal("Patronymic", name.SurnameList[1].OriginType);
        Assert.False(name.SurnameList[1].Primary);
    }

    [Fact]
    public void Object_CamelCase_Native_Fields()
    {
        const string json = """
            {"firstName":"Anna","surnameList":[{"surname":"Berg","origin_type":"Taken"}],
             "groupAs":"Berg","sortAs":1,"nickName":"Annie","familyNick":"Bergs"}
            """;
        var name = Deserialize(json)!.Name;

        Assert.Equal("Anna", name.FirstName);
        Assert.Equal("Berg", Assert.Single(name.SurnameList!).Surname);
        Assert.Equal("Taken", name.SurnameList![0].OriginType);
        Assert.True(name.SurnameList[0].Primary);
        Assert.Equal("Berg", name.GroupAs);
        Assert.Equal(1, name.SortAs);
        Assert.Equal("Annie", name.Nick);
        Assert.Equal("Bergs", name.FamNick);
    }

    [Fact]
    public void Object_OriginType_Shortcut_In_CamelCase()
    {
        var name = Deserialize("""{"given":"Ola","surname":"Nordmann","originType":"Location"}""")!.Name;

        Assert.Equal("Location", Assert.Single(name.SurnameList!).OriginType);
    }

    [Fact]
    public void Object_Native_FirstName_With_Surname_And_Patronymic_Shortcuts()
    {
        var name = Deserialize("""{"first_name":"Петр","surname":"Соколов","patronymic":"Иванович"}""")!.Name;

        Assert.Equal("Петр", name.FirstName);
        Assert.Equal(["Соколов", "Иванович"], name.SurnameList!.Select(s => s.Surname));
        Assert.Equal([true, false], name.SurnameList!.Select(s => s.Primary));
    }

    [Fact]
    public void Object_SurnameList_With_Patronymic_Shortcut_Appends_It()
    {
        var name = Deserialize(
            """{"given":"Петр","surname_list":[{"surname":"Соколов"}],"patronymic":"Иванович"}""")!.Name;

        Assert.Equal(["Соколов", "Иванович"], name.SurnameList!.Select(s => s.Surname));
        Assert.Equal([true, false], name.SurnameList!.Select(s => s.Primary));
        Assert.Equal("Patronymic", name.SurnameList![1].OriginType);
    }

    [Fact]
    public void Object_Whole_Name_With_Patronymic_And_Title()
    {
        var name = Deserialize("""{"name":"Федор Пробнякин","patronymic":"Иванович","title":"Купец"}""")!.Name;

        Assert.Equal("Федор", name.FirstName);
        Assert.Equal("Купец", name.Title);
        Assert.Equal(["Пробнякин", "Иванович"], name.SurnameList!.Select(s => s.Surname));
        Assert.Equal([true, false], name.SurnameList!.Select(s => s.Primary));
    }

    [Fact]
    public void Object_SurnameList_Without_Primary_Flags_Gets_One_Primary()
    {
        var name = Deserialize("""{"first_name":"A","surname_list":[{"surname":"B"},{"surname":"C"}]}""")!.Name;

        Assert.Equal([true, false], name.SurnameList!.Select(s => s.Primary));
    }

    [Fact]
    public void Object_SurnameList_Explicit_Primary_Wins_Over_Order()
    {
        var name = Deserialize(
            """{"first_name":"A","surname_list":[{"surname":"B"},{"surname":"C","primary":true}]}""")!.Name;

        Assert.Equal([false, true], name.SurnameList!.Select(s => s.Primary));
    }

    [Fact]
    public void Object_As_Returned_By_Gramps_Is_Accepted()
    {
        // A name copied from get_object: _class markers, wire types, empty strings and empty lists.
        const string json = """
            {"_class":"Name","private":false,"surname_list":[
                {"_class":"Surname","surname":"Garcia","prefix":"","primary":true,
                 "origintype":{"_class":"NameOriginType","string":"Patrilineal"},"connector":""}],
             "citation_list":[],"note_list":[],"first_name":"Jose","suffix":"","title":"",
             "type":{"_class":"NameType","string":"Birth Name"},"group_as":"","sort_as":0,"display_as":0,
             "call":"","nick":"","famnick":""}
            """;
        var name = Deserialize(json)!.Name;

        Assert.Equal("Jose", name.FirstName);
        Assert.Equal("Birth Name", name.Type);
        Assert.Equal("Patrilineal", Assert.Single(name.SurnameList!).OriginType);
        Assert.Null(name.Title);
    }

    [Fact]
    public void Object_Null_And_Blank_Fields_Count_As_Not_Sent()
    {
        var name = Deserialize("""{"given":"A","surname":"B","nick":"  ","title":null,"first":""}""")!.Name;

        Assert.Equal("A", name.FirstName);
        Assert.Null(name.Nick);
        Assert.Null(name.Title);
    }

    [Theory]
    [InlineData("""{"given":"Anna","surname":"Berg","maidenName":"Holm"}""", "Unknown field \"maidenName\" in name object.")]
    [InlineData("""{"given":"Anna","foo":1,"bar":2}""", "Unknown fields \"foo\", \"bar\" in name object.")]
    [InlineData("""{"given":"Anna","surname_list":[{"surname":"Berg","kind":"Taken"}]}""",
        "Unknown field \"kind\" in surname_list[0].")]
    public void Object_Unknown_Field_Throws_With_Accepted_Fields(string json, string start)
    {
        var ex = Assert.Throws<JsonInputException>(() => Deserialize(json));

        Assert.StartsWith(start, ex.Message);
        Assert.Contains("Accepted fields: ", ex.Message);
    }

    [Theory]
    [InlineData("""{"given":"A","first_name":"B","surname":"C"}""", "Fields \"given\" and \"first_name\"")]
    [InlineData("""{"name":"Anna Berg","given":"Anna"}""", "Field \"name\" holds the whole name")]
    [InlineData("""{"given":"A","surname":"B","surnameList":[{"surname":"C"}]}""", "Field \"surnameList\" cannot be combined with \"surname\"")]
    [InlineData("""{"given":"Ludwig","prefix":"van"}""", "\"prefix\" describe the surname")]
    [InlineData("""{"given":5,"surname":"B"}""", "Field \"given\" in name object must be a string, not a number.")]
    [InlineData("""{"given":"A","surname_list":{"surname":"B"}}""", "Field \"surname_list\" in name object must be an array")]
    [InlineData("""{"given":"A","surname_list":["B"]}""", "Entry surname_list[0] must be an object")]
    [InlineData("""{"given":"A","surname_list":[{"surname":"B","primary":"yes"}]}""", "Field \"primary\" in surname_list[0] must be true or false")]
    [InlineData("""{"title":"Dr."}""", "Name object has no name in it")]
    [InlineData("""{}""", "Name object has no name in it")]
    [InlineData("42", "Unexpected token Number for name")]
    public void Object_Conflicting_Or_Empty_Name_Throws(string json, string start)
    {
        var ex = Assert.Throws<JsonInputException>(() => Deserialize(json));

        Assert.StartsWith(start, ex.Message);
    }

    [Fact]
    public void Stringified_Object_Unknown_Field_Throws()
    {
        const string json = "\"{\\\"given\\\":\\\"Anna\\\",\\\"lastname\\\":\\\"Berg\\\",\\\"maiden\\\":\\\"Holm\\\"}\"";

        var ex = Assert.Throws<JsonInputException>(() => Deserialize(json));

        Assert.StartsWith("Unknown field \"maiden\" in name object.", ex.Message);
    }
}
