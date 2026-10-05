using GrampsWeb.Mcp.Formatters;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class ContextualNextStepsTests
{
    [Fact]
    public void PersonWithoutEvents_IsShownHowToAttachBirthOrDeath()
    {
        var hints = ResponseEnvelope.PersonCreateNextSteps("person", hasFamily: true, hasNotes: true);
        var hint = Assert.Single(hints);
        Assert.Contains("create_event(eventType: \"Birth\"", hint);
        Assert.Contains("update_person(handle: \"person\"", hint);
        Assert.Contains("linkMode: \"add\"", hint);
    }

    [Fact]
    public void ExistingPersonLinks_AreNotSuggestedAgain_UnknownEventsAreChecked()
    {
        var hints = ResponseEnvelope.PersonCreateNextSteps("person", hasEvents: true, hasFamily: true, hasNotes: true);
        var hint = Assert.Single(hints);
        Assert.Contains("Check linked events", hint);
        Assert.Contains("get_object", hint);
        Assert.DoesNotContain("add_event_to_person", hint);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void FamilyHints_OnlySuggestMissingLinks(bool children, bool events)
    {
        var hints = ResponseEnvelope.FamilyCreateNextSteps("family", children, events);
        var text = string.Join('\n', hints);
        Assert.Equal(!children, text.Contains("childRefs:"));
        Assert.Equal(!events, text.Contains("eventRefs:"));
        Assert.Equal(!children || !events, text.Contains("Use linkMode: \"add\""));
        if (children && events)
            Assert.DoesNotContain("Next steps:", ResponseEnvelope.CreateSuccess("Family", "family", null, null, hints));
    }

    [Fact]
    public void MissingHandles_DoNotProduceUnusableWriteExamples()
    {
        foreach (var hints in new[] { ResponseEnvelope.PersonCreateNextSteps(null), ResponseEnvelope.FamilyCreateNextSteps("") })
        {
            Assert.Contains("do not create it again", Assert.Single(hints));
            Assert.DoesNotContain("handle:", hints[0]);
        }
    }

    [Fact]
    public void StandaloneEventHint_AttachesExistingEventInsteadOfCreatingAnother()
    {
        var text = string.Join('\n', ResponseEnvelope.EventCreateNextSteps("event"));
        Assert.Contains("ref: \"event\"", text);
        Assert.Contains("Existing event references and roles are preserved", text);
        Assert.DoesNotContain("add_event_to_person", text);
    }
}
