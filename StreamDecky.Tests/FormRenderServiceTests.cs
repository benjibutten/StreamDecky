using StreamDecky.Models;
using StreamDecky.Services;
using Xunit;

namespace StreamDecky.Tests;

public sealed class FormRenderServiceTests
{
    [Fact]
    public void GetValidationErrors_FindsAmbiguousTokensAndLabels()
    {
        var template = new FormTemplate();
        template.Fields.Add(new FormField { Key = "person", Label = "Person" });
        template.Fields.Add(new FormField { Key = "PERSON", Label = "Person" });
        template.Fields.Add(new FormField { Key = "date", Label = "Date override" });
        template.Fields.Add(new FormField { Key = "answer", Label = "Answer", Type = FormFieldType.Choice });
        template.Counters.Add(new FormCounter { Name = "answer_choice" });

        var errors = FormRenderService.GetValidationErrors(template);

        Assert.Contains(errors, error => error.Contains("{PERSON}", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("{date}", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("{answer_choice}", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("label 'Person'", StringComparison.Ordinal));
    }

    private static FormTemplate CreateInvoiceTemplate()
    {
        var template = new FormTemplate
        {
            Name = "Payment note",
            OutputTemplate = "Invoice {invoiceNo} - {who}\n{description}"
        };
        template.Fields.Add(new FormField { Key = "who", Label = "Who" });
        template.Fields.Add(new FormField { Key = "description", Label = "Description" });
        template.Counters.Add(new FormCounter { Name = "invoiceNo", NextValue = 42, PadWidth = 4 });
        template.EnsureInitialized();
        return template;
    }

    [Fact]
    public void Render_ExpandsFieldsCountersAndKeepsUnknownTokens()
    {
        var template = CreateInvoiceTemplate();
        template.OutputTemplate += " {missing}";

        string result = FormRenderService.Render(template, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["who"] = "John Doe",
            ["description"] = "Rent for the garage"
        });

        Assert.Equal("Invoice 0042 - John Doe\nRent for the garage {missing}", result);
    }

    [Fact]
    public void Render_FieldValueWinsOverCounterWithSameName()
    {
        var template = CreateInvoiceTemplate();

        string result = FormRenderService.Render(template, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["invoiceNo"] = "override",
            ["who"] = "A",
            ["description"] = "B"
        });

        Assert.StartsWith("Invoice override - A", result);
    }

    [Fact]
    public void Render_ExpandsBuiltInDateTokens()
    {
        var template = new FormTemplate { OutputTemplate = "{date} {time} {datetime}" };
        template.EnsureInitialized();
        var timestamp = new DateTime(2026, 7, 18, 9, 5, 0);

        string result = FormRenderService.Render(template, new Dictionary<string, string>(), timestamp);

        Assert.Equal("2026-07-18 09:05 2026-07-18 09:05", result);
    }

    [Fact]
    public void ExpandInlineTokens_ExpandsCountersButNotFieldKeys()
    {
        var template = CreateInvoiceTemplate();

        string result = FormRenderService.ExpandInlineTokens(template, "Invoice {invoiceNo} for {who}");

        Assert.Equal("Invoice 0042 for {who}", result);
    }

    [Fact]
    public void SubmissionTemplateSnapshot_FreezesCountersAndBuiltInsButKeepsFieldsEditable()
    {
        var template = CreateInvoiceTemplate();
        template.OutputTemplate = "{invoiceNo} {date} {who}";
        var timestamp = new DateTime(2026, 7, 18, 9, 5, 0);

        string snapshot = FormRenderService.CreateSubmissionTemplateSnapshot(template, timestamp);
        template.Counters[0].NextValue++;
        string rerendered = FormRenderService.RenderTemplate(
            snapshot,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["who"] = "Jane" });

        Assert.Equal("0042 2026-07-18 Jane", rerendered);
    }

    [Fact]
    public void ChoiceToken_RendersSelectedOptionTitleSeparatelyFromEditableText()
    {
        var template = new FormTemplate { OutputTemplate = "{task_choice}: {task}" };
        var field = new FormField
        {
            Key = "task",
            Label = "Title",
            Type = FormFieldType.Choice
        };
        field.Options.Add(new FormFieldOption { Label = "Laundry", Text = "Invoice {invoiceNo} Laundry" });
        template.Fields.Add(field);
        template.Counters.Add(new FormCounter { Name = "invoiceNo", NextValue = 42, PadWidth = 4 });
        template.EnsureInitialized();

        string snapshot = FormRenderService.CreateSubmissionTemplateSnapshot(template);
        string result = FormRenderService.RenderTemplate(
            snapshot,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["task"] = "Invoice 0042 Laundry",
                [FormRenderService.GetChoiceToken("task")] = "Laundry"
            });

        Assert.Equal("Laundry: Invoice 0042 Laundry", result);
        Assert.Empty(FormRenderService.GetUnknownTokens(template));
    }

    [Fact]
    public void GetUnknownTokens_ReportsOnlyUnmatchedTokens()
    {
        var template = CreateInvoiceTemplate();
        template.OutputTemplate = "{who} {invoiceNo} {date} {typo} {typo}";

        var unknown = FormRenderService.GetUnknownTokens(template);

        Assert.Equal(new[] { "typo" }, unknown);
    }

    [Fact]
    public void Render_WithEmptyOutputTemplate_ComposesLabelValueLines()
    {
        var template = CreateInvoiceTemplate();
        template.OutputTemplate = string.Empty;

        string result = FormRenderService.Render(template, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["who"] = "John",
            ["description"] = "Rent"
        });

        Assert.Equal($"Who: John{Environment.NewLine}Description: Rent", result);
    }

    [Fact]
    public void GetEffectiveOutputTemplate_PrefersAuthoredTemplate()
    {
        var template = CreateInvoiceTemplate();

        Assert.Equal(template.OutputTemplate, FormRenderService.GetEffectiveOutputTemplate(template));
    }

    [Fact]
    public void ResolveFieldValues_ExpandsOtherFieldsCountersAndBuiltIns()
    {
        var template = CreateInvoiceTemplate();
        var timestamp = new DateTime(2026, 7, 18, 9, 5, 0);

        var resolved = FormRenderService.ResolveFieldValues(template, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["who"] = "John Doe",
            ["description"] = "Payment from {who} on {date}, invoice {invoiceNo}"
        }, timestamp);

        Assert.Equal("John Doe", resolved["who"]);
        Assert.Equal("Payment from John Doe on 2026-07-18, invoice 0042", resolved["description"]);
    }

    [Fact]
    public void ResolveFieldValues_LeavesSelfReferenceAndUnknownTokensUntouched()
    {
        var template = CreateInvoiceTemplate();

        var resolved = FormRenderService.ResolveFieldValues(template, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["who"] = "I am {who} and {nobody}"
        });

        Assert.Equal("I am {who} and {nobody}", resolved["who"]);
    }

    [Fact]
    public void FormatValue_WithoutPadding_UsesPlainNumber()
    {
        var counter = new FormCounter { Name = "n", NextValue = 7, PadWidth = 0 };

        Assert.Equal("7", counter.FormatValue());
    }

    [Fact]
    public void NormalizeKey_StripsBracesAndWhitespace()
    {
        Assert.Equal("fakturaNr", FormField.NormalizeKey(" {faktura Nr} "));
        Assert.Equal(string.Empty, FormField.NormalizeKey(null));
        Assert.Equal("a-b_c", FormField.NormalizeKey("a-b_c"));
    }
}
