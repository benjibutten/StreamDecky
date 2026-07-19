using StreamDecky.Models;
using StreamDecky.ViewModels;
using Xunit;

namespace StreamDecky.Tests;

public sealed class FormFieldSessionViewModelTests
{
    private static FormFieldSessionViewModel CreateSession(
        FormField field,
        Func<string, string?> resolveToken,
        Action? onValueChanged = null,
        IReadOnlyList<string>? historyValues = null)
    {
        return new FormFieldSessionViewModel(
            field,
            historyValues ?? Array.Empty<string>(),
            (_, pattern) => System.Text.RegularExpressions.Regex.Replace(
                pattern ?? string.Empty,
                @"\{([\p{L}\p{Nd}_-]+)\}",
                match => resolveToken(match.Groups[1].Value) ?? match.Value),
            onValueChanged ?? (() => { }));
    }

    [Fact]
    public void DisplayValue_ExpandsDefaultPatternAndFollowsReferencedField()
    {
        string whoValue = string.Empty;
        var field = new FormField { Key = "description", DefaultValue = "Payment from {who}" };
        var session = CreateSession(field, token => token == "who" && whoValue.Length > 0 ? whoValue : null);

        Assert.Equal("Payment from {who}", session.Value);

        whoValue = "John";
        session.RefreshFromPattern();

        Assert.Equal("Payment from John", session.Value);
    }

    [Fact]
    public void UserEdit_BecomesTheNewPattern()
    {
        string whoValue = "John";
        var field = new FormField { Key = "description", DefaultValue = "Payment from {who}" };
        var session = CreateSession(field, token => token == "who" ? whoValue : null);

        session.Value = "Handwritten note";
        whoValue = "Jane";
        session.RefreshFromPattern();

        Assert.Equal("Handwritten note", session.Value);
    }

    [Fact]
    public void UserEdit_WithTokens_StaysLive()
    {
        string whoValue = "John";
        var field = new FormField { Key = "description" };
        var session = CreateSession(field, token => token == "who" ? whoValue : null);

        session.Value = "Send to {who} today";
        session.RefreshFromPattern();
        Assert.Equal("Send to John today", session.Value);

        whoValue = "Jane";
        session.RefreshFromPattern();
        Assert.Equal("Send to Jane today", session.Value);
    }

    [Fact]
    public void ApplyOption_SetsPatternAndSelectsOption()
    {
        var option = new FormFieldOption { Label = "Rent", Text = "Rent for {who}" };
        var field = new FormField { Key = "description", Type = FormFieldType.Choice };
        field.Options.Add(option);
        var session = CreateSession(field, token => token == "who" ? "John" : null);

        session.ApplyOptionCommand.Execute(option);

        Assert.Same(option, session.SelectedOption);
        Assert.Equal("Rent for John", session.Value);
    }

    [Fact]
    public void RefreshFromPattern_DoesNotNotifyParent()
    {
        int notifications = 0;
        string whoValue = "John";
        var field = new FormField { Key = "description", DefaultValue = "{who}" };
        var session = CreateSession(field, token => token == "who" ? whoValue : null, () => notifications++);

        int afterConstruction = notifications;
        whoValue = "Jane";
        session.RefreshFromPattern();

        Assert.Equal("Jane", session.Value);
        Assert.Equal(afterConstruction, notifications);
    }

    [Fact]
    public void Suggestions_AreLimitedToThreeVisibleChips()
    {
        var field = new FormField { Key = "who", RememberHistory = true };
        var session = CreateSession(
            field,
            _ => null,
            historyValues: new[] { "One", "Two", "Three", "Four" });

        Assert.Equal(FormFieldSessionViewModel.MaxVisibleSuggestions, session.Suggestions.Count);
        Assert.Equal(new[] { "One", "Two", "Three" }, session.Suggestions);
    }
}
