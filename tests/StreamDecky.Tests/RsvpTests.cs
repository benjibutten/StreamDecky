using StreamDecky.SpeedReader;
using Xunit;

namespace StreamDecky.Tests;

public sealed class RsvpTests
{
    [Theory]
    [InlineData("a", "", "a", "")]
    [InlineData("hej", "h", "e", "j")]
    [InlineData("läsning", "lä", "s", "ning")]
    [InlineData("\"citat\"", "\"c", "i", "tat\"")]
    public void Parse_PutsThePivotSlightlyLeftOfCentre(string text, string before, string pivot, string after)
    {
        var word = Assert.Single(Rsvp.Parse(text));

        Assert.Equal(before, word.Before);
        Assert.Equal(pivot, word.Pivot);
        Assert.Equal(after, word.After);
    }

    [Fact]
    public void Parse_PausesLongerAfterSentencesThanCommasAndLongestAtParagraphs()
    {
        var words = Rsvp.Parse("Ett, två. Tre\n\nfyra");

        Assert.Equal(1.5, words[0].DelayFactor);
        Assert.Equal(2.2, words[1].DelayFactor);
        Assert.Equal(3.0, words[2].DelayFactor);
        Assert.Equal(1.0, words[3].DelayFactor);
    }

    [Fact]
    public void Parse_SplitsLongWordsIntoEvenHyphenatedParts()
    {
        var words = Rsvp.Parse("nationalencyklopedin");

        Assert.Equal(["nationalen-", "cyklopedin"], words.Select(w => w.Text));
    }

    [Fact]
    public void Parse_SplitsAtExistingHyphensFirst()
    {
        var words = Rsvp.Parse("e-post");

        Assert.Equal(["e-", "post"], words.Select(w => w.Text));
    }

    [Fact]
    public void Parse_KeepsADecomposedAccentWithItsLetter()
    {
        var word = Assert.Single(Rsvp.Parse("Café"));

        Assert.Equal("Café", word.Text);
    }

    [Fact]
    public void Parse_ReturnsNothingForWhitespace()
    {
        Assert.Empty(Rsvp.Parse(" \n\t "));
    }
}
