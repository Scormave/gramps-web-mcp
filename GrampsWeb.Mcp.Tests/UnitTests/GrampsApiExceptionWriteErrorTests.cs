using System.Net;
using System.Net.Http.Headers;
using GrampsWeb.Mcp.Exceptions;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class GrampsApiExceptionWriteErrorTests
{
    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "sqlite3.OperationalError: database is locked")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "Database is locked")]
    [InlineData(HttpStatusCode.InternalServerError, "SQLITE3.OPERATIONALERROR: busy")]
    public void FromMutationResponse_Rewrites_Sqlite_Lock(HttpStatusCode status, string body)
    {
        using var response = new HttpResponseMessage(status);
        var ex = GrampsApiException.FromMutationResponse(response, body);

        Assert.Contains("database is locked", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Retry after", ex.Message);
        Assert.DoesNotContain("Gramps API error", ex.Message);
        Assert.Equal(body, ex.ResponseBody);
    }

    [Fact]
    public void FromMutationResponse_Leaves_Generic_500_Unchanged()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.InternalServerError);
        var ex = GrampsApiException.FromMutationResponse(response, "internal error");

        Assert.StartsWith("Gramps API error 500", ex.Message);
        Assert.Contains("internal error", ex.Message);
    }

    [Fact]
    public void FromMutationResponse_Rewrites_429_With_RetryAfter()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMilliseconds(1500));

        var ex = GrampsApiException.FromMutationResponse(response, "slow down");

        Assert.Contains("HTTP 429", ex.Message);
        Assert.Contains("Retry after ~1500ms", ex.Message);
    }

    [Fact]
    public void FromMutationResponse_Uses_Default_Retry_When_429_Has_No_Header()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        var ex = GrampsApiException.FromMutationResponse(response, "slow down");

        Assert.Contains($"Retry after ~{GrampsRetryableWriteErrors.DefaultRateLimitRetryAfterMs}ms", ex.Message);
    }
}
