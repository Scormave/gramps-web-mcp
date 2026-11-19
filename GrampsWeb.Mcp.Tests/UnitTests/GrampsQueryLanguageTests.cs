using System.Net;
using GrampsWeb.Mcp.Exceptions;
using GrampsWeb.Mcp.Tools;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class GrampsQueryLanguageTests
{
    [Fact]
    public void IsGqlRefused_RecognisesTheUnknownFieldAnswer()
    {
        var error = new GrampsApiException(
            HttpStatusCode.UnprocessableEntity,
            """{"error": {"code": 422, "message": "query: gql: Unknown field.", "messages": {"query": {"gql": ["Unknown field."]}}}}""");

        Assert.True(GrampsQueryLanguage.IsGqlRefused(error));
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity, """{"error": {"code": 422, "message": "Invalid query syntax."}}""")]
    [InlineData(HttpStatusCode.UnprocessableEntity, """{"error": {"messages": {"query": {"rules": ["Unknown field."]}}}}""")]
    [InlineData(HttpStatusCode.UnprocessableEntity, """{"error": {"messages": {"query": {"gql": "Unknown field."}}}}""")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "not json")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "")]
    [InlineData(HttpStatusCode.BadRequest, """{"error": {"messages": {"query": {"gql": ["Unknown field."]}}}}""")]
    public void IsGqlRefused_IgnoresOtherFailures(HttpStatusCode status, string body)
    {
        Assert.False(GrampsQueryLanguage.IsGqlRefused(new GrampsApiException(status, body)));
    }

    [Fact]
    public void MatchesQueryRules_KeepsTheQuotesReadable()
    {
        Assert.Equal(
            """{"rules":[{"name":"MatchesQuery","values":["checksum == 'abc123'"]}]}""",
            GrampsQueryLanguage.MatchesQueryRules("checksum == 'abc123'"));
    }
}
