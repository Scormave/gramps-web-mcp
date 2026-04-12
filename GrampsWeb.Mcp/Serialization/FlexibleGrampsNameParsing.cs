using System.Text.Json;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Serialization;

/// <summary>Parses simple string forms into <see cref="GrampsName"/>; full objects are deserialized via JSON elsewhere.</summary>
public static class FlexibleGrampsNameParsing
{
    /// <summary>
    /// Optional Gramps name type (e.g. Birth Name, Married Name) as <c>Label:: remainder</c> (double colon).
    /// Remainder: <c>given|surname</c> (first pipe) or Western <c>given … surname</c> (last space).
    /// Single word: stored as <c>first_name</c> with one primary surname entry (empty string).
    /// </summary>
    public static GrampsName ParseSimpleLine(string line)
    {
        line = line.Trim();
        if (string.IsNullOrEmpty(line))
            throw new JsonException("Name line is empty.");

        string? type = null;
        var body = line;
        var dc = line.IndexOf("::", StringComparison.Ordinal);
        if (dc >= 0)
        {
            type = line[..dc].Trim();
            body = line[(dc + 2)..].Trim();
            if (string.IsNullOrEmpty(body))
                throw new JsonException(
                    $"Name type prefix \"{type}\" must be followed by text after ::.");
        }

        string first;
        string sur;

        var pipe = body.IndexOf('|');
        if (pipe >= 0)
        {
            first = body[..pipe].Trim();
            sur = body[(pipe + 1)..].Trim();
        }
        else
        {
            var lastSp = body.LastIndexOf(' ');
            if (lastSp > 0)
            {
                first = body[..lastSp].Trim();
                sur = body[(lastSp + 1)..].Trim();
            }
            else
            {
                first = body;
                sur = "";
            }
        }

        return new GrampsName
        {
            Type = string.IsNullOrEmpty(type) ? null : type,
            FirstName = first,
            SurnameList =
            [
                new GrampsSurname { Surname = sur, Primary = true }
            ]
        };
    }
}
