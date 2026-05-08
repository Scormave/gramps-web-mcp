using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Tools;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class EventToolsLinkedRolesTests
{
    private const string Ev = "event_handle_1";

    [Fact]
    public void ResolveDistinctRoles_NullPerson_DefaultsPrimary()
    {
        Assert.Equal("Primary", EventTools.ResolveDistinctRolesForPersonEvent(null, Ev));
    }

    [Fact]
    public void ResolveDistinctRoles_NoRefs_DefaultsPrimary()
    {
        var p = new GrampsPerson { EventRefList = [] };
        Assert.Equal("Primary", EventTools.ResolveDistinctRolesForPersonEvent(p, Ev));
    }

    [Fact]
    public void ResolveDistinctRoles_NoMatchingRef_DefaultsPrimary()
    {
        var p = new GrampsPerson
        {
            EventRefList = [new GrampsEventRef { Ref = "other", Role = "Witness" }]
        };
        Assert.Equal("Primary", EventTools.ResolveDistinctRolesForPersonEvent(p, Ev));
    }

    [Fact]
    public void ResolveDistinctRoles_OrdinalRefMatch_DistinguishesNfdFromNfc()
    {
        // Gramps handles are compared by ordinal code units; NFD and NFC forms remain distinct.
        var p = new GrampsPerson
        {
            EventRefList =
            [
                new GrampsEventRef { Ref = "a\u0300", Role = "A" },
                new GrampsEventRef { Ref = "\u00e0", Role = "B" }
            ]
        };
        Assert.Equal("B", EventTools.ResolveDistinctRolesForPersonEvent(p, "\u00e0"));
        Assert.Equal("A", EventTools.ResolveDistinctRolesForPersonEvent(p, "a\u0300"));
    }

    [Fact]
    public void ResolveDistinctRoles_EmptyOrWhitespaceRole_DefaultsPrimary()
    {
        var p = new GrampsPerson
        {
            EventRefList =
            [
                new GrampsEventRef { Ref = Ev, Role = null },
                new GrampsEventRef { Ref = Ev, Role = "   " }
            ]
        };
        Assert.Equal("Primary", EventTools.ResolveDistinctRolesForPersonEvent(p, Ev));
    }

    [Fact]
    public void ResolveDistinctRoles_MultipleRefsSameRole_Deduplicates()
    {
        var p = new GrampsPerson
        {
            EventRefList =
            [
                new GrampsEventRef { Ref = Ev, Role = "Witness" },
                new GrampsEventRef { Ref = Ev, Role = "Witness" }
            ]
        };
        Assert.Equal("Witness", EventTools.ResolveDistinctRolesForPersonEvent(p, Ev));
    }

    [Fact]
    public void ResolveDistinctRoles_MultipleDistinctRoles_JoinsInEncounterOrder()
    {
        var p = new GrampsPerson
        {
            EventRefList =
            [
                new GrampsEventRef { Ref = Ev, Role = "Witness" },
                new GrampsEventRef { Ref = Ev, Role = "Primary" }
            ]
        };
        Assert.Equal("Witness, Primary", EventTools.ResolveDistinctRolesForPersonEvent(p, Ev));
    }
}
