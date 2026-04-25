using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Requests;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class GrampsRequestMappingRepositoryRefTests
{
    [Fact]
    public void ToRepositoryRefRequests_Merge_Preserves_Metadata_For_Overlapping_Handles()
    {
        var existing = new[]
        {
            new GrampsRepositoryRef { Ref = "repo-a", CallNumber = "A-1", MediaType = "Book", Private = true },
            new GrampsRepositoryRef { Ref = "repo-b", CallNumber = "B-1", MediaType = "Microfilm" }
        };

        var mapped = GrampsRequestMapping.ToRepositoryRefRequests(["repo-a", "repo-new"], existing);

        Assert.NotNull(mapped);
        Assert.Equal(2, mapped!.Length);
        Assert.Equal("repo-a", mapped[0].Ref);
        Assert.Equal("A-1", mapped[0].CallNumber);
        Assert.Equal("Book", mapped[0].MediaType);
        Assert.True(mapped[0].Private);

        Assert.Equal("repo-new", mapped[1].Ref);
        Assert.Null(mapped[1].CallNumber);
        Assert.Null(mapped[1].MediaType);
    }

    [Fact]
    public void ToRepositoryRefRequests_From_Model_Clones_Entries()
    {
        var mapped = GrampsRequestMapping.ToRepositoryRefRequests(new[]
        {
            new GrampsRepositoryRef { Ref = "repo-x", CallNumber = "X-9", MediaType = "Manuscript" }
        });

        Assert.NotNull(mapped);
        Assert.Single(mapped!);
        Assert.Equal("repo-x", mapped[0].Ref);
        Assert.Equal("X-9", mapped[0].CallNumber);
        Assert.Equal("Manuscript", mapped[0].MediaType);
    }
}
