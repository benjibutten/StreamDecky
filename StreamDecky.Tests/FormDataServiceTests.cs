using StreamDecky.Models;
using StreamDecky.Services;
using Xunit;

namespace StreamDecky.Tests;

public sealed class FormDataServiceTests
{
    private static FormSubmission CreateSubmission(string renderedText = "Invoice 0001 - John")
    {
        return new FormSubmission
        {
            TemplateId = "template1",
            TemplateName = "Payment note",
            RenderedText = renderedText,
            Values = new Dictionary<string, string> { ["Who"] = "John" }
        };
    }

    [Fact]
    public void RecordSubmission_PersistsSubmissionAndHistoryAcrossInstances()
    {
        using var tempDirectory = new TemporaryDirectory();
        var service = new FormDataService(tempDirectory.Path);

        service.RecordSubmission("profile1", CreateSubmission(), new[]
        {
            new KeyValuePair<string, string>("fieldWho", "John")
        });

        var reloaded = new FormDataService(tempDirectory.Path);
        var submissions = reloaded.GetSubmissions("profile1");
        Assert.Single(submissions);
        Assert.Equal("Invoice 0001 - John", submissions[0].RenderedText);
        Assert.Equal("Payment note", submissions[0].TemplateName);
        Assert.Equal(new[] { "John" }, reloaded.GetFieldHistory("profile1", "fieldWho"));
    }

    [Fact]
    public void RecordSubmission_NewestFirstAndCapped()
    {
        using var tempDirectory = new TemporaryDirectory();
        var service = new FormDataService(tempDirectory.Path);

        for (int i = 0; i < FormDataService.MaxSubmissionsPerProfile + 5; i++)
            service.RecordSubmission("profile1", CreateSubmission($"entry {i}"));

        var submissions = service.GetSubmissions("profile1");
        Assert.Equal(FormDataService.MaxSubmissionsPerProfile, submissions.Count);
        Assert.Equal($"entry {FormDataService.MaxSubmissionsPerProfile + 4}", submissions[0].RenderedText);
    }

    [Fact]
    public void FieldHistory_DedupesCaseInsensitiveAndMovesToFront()
    {
        using var tempDirectory = new TemporaryDirectory();
        var service = new FormDataService(tempDirectory.Path);

        service.RecordSubmission("profile1", CreateSubmission(), new[]
        {
            new KeyValuePair<string, string>("field1", "John Doe")
        });
        service.RecordSubmission("profile1", CreateSubmission(), new[]
        {
            new KeyValuePair<string, string>("field1", "Jane Roe")
        });
        service.RecordSubmission("profile1", CreateSubmission(), new[]
        {
            new KeyValuePair<string, string>("field1", "john doe")
        });

        Assert.Equal(new[] { "john doe", "Jane Roe" }, service.GetFieldHistory("profile1", "field1"));
    }

    [Fact]
    public void SetSubmissionCompleted_PersistsAcrossInstances()
    {
        using var tempDirectory = new TemporaryDirectory();
        var service = new FormDataService(tempDirectory.Path);
        var submission = CreateSubmission();
        service.RecordSubmission("profile1", submission);

        Assert.True(service.SetSubmissionCompleted("profile1", submission.Id, true));
        Assert.False(service.SetSubmissionCompleted("profile1", submission.Id, true));

        var reloaded = new FormDataService(tempDirectory.Path);
        Assert.True(reloaded.GetSubmissions("profile1")[0].IsCompleted);
    }

    [Fact]
    public void UpdateSubmissionField_PersistsValueAndUpdatesRenderedText()
    {
        using var tempDirectory = new TemporaryDirectory();
        var service = new FormDataService(tempDirectory.Path);
        var submission = CreateSubmission("John paid John");
        submission.OutputTemplateSnapshot = "{who} paid {who}";
        submission.FieldTokens["Who"] = "who";
        submission.FieldIds["Who"] = "field-who";
        submission.TokenValues["who"] = "John";
        service.RecordSubmission("profile1", submission);

        Assert.True(service.UpdateSubmissionField("profile1", submission.Id, "Who", "Jon"));
        Assert.False(service.UpdateSubmissionField("profile1", submission.Id, "Who", "Jon"));

        var reloaded = new FormDataService(tempDirectory.Path);
        var updated = Assert.Single(reloaded.GetSubmissions("profile1"));
        Assert.Equal("Jon", updated.Values["Who"]);
        Assert.Equal("Jon paid Jon", updated.RenderedText);
    }

    [Fact]
    public void UpdateSubmissionField_RerendersOnlyTheTargetFieldToken()
    {
        using var tempDirectory = new TemporaryDirectory();
        var service = new FormDataService(tempDirectory.Path);
        var submission = CreateSubmission("John met John; literal John");
        submission.Values["Approver"] = "John";
        submission.OutputTemplateSnapshot = "{who} met {approver}; literal John";
        submission.FieldTokens["Who"] = "who";
        submission.FieldTokens["Approver"] = "approver";
        submission.FieldIds["Who"] = "field-who";
        submission.FieldIds["Approver"] = "field-approver";
        submission.TokenValues["who"] = "John";
        submission.TokenValues["approver"] = "John";
        Assert.True(service.RecordSubmission("profile1", submission));

        Assert.True(service.UpdateSubmissionField("profile1", submission.Id, "Who", "Jane"));

        Assert.Equal("Jane met John; literal John", service.GetSubmissions("profile1")[0].RenderedText);
    }

    [Fact]
    public void UpdateSubmissionField_RenamesEveryMatchingEntryAndSuggestionForSameField()
    {
        using var tempDirectory = new TemporaryDirectory();
        var service = new FormDataService(tempDirectory.Path);

        for (int i = 0; i < 3; i++)
        {
            var submission = CreateSubmission($"Person: Emil Pellets ({i})");
            submission.Values["Who"] = "Emil Pellets";
            submission.OutputTemplateSnapshot = $"Person: {{who}} ({i})";
            submission.FieldTokens["Who"] = "who";
            if (i > 0)
                submission.FieldIds["Who"] = "field-who";
            submission.TokenValues["who"] = "Emil Pellets";
            Assert.True(service.RecordSubmission(
                "profile1",
                submission,
                new[] { new KeyValuePair<string, string>("field-who", "Emil Pellets") }));
        }

        var unrelated = CreateSubmission("Approver: Emil Pellets");
        unrelated.Values = new Dictionary<string, string> { ["Approver"] = "Emil Pellets" };
        unrelated.OutputTemplateSnapshot = "Approver: {approver}";
        unrelated.FieldTokens["Approver"] = "approver";
        unrelated.FieldIds["Approver"] = "field-approver";
        unrelated.TokenValues["approver"] = "Emil Pellets";
        Assert.True(service.RecordSubmission("profile1", unrelated));

        var target = service.GetSubmissions("profile1").First(item => item.FieldIds.ContainsValue("field-who"));
        Assert.True(service.UpdateSubmissionField("profile1", target.Id, "Who", "Emil Pelletsis"));

        var reloaded = new FormDataService(tempDirectory.Path);
        var renamed = reloaded.GetSubmissions("profile1")
            .Where(item => item.FieldIds.ContainsValue("field-who"))
            .ToList();
        Assert.Equal(3, renamed.Count);
        Assert.All(renamed, item =>
        {
            Assert.Equal("Emil Pelletsis", item.Values["Who"]);
            Assert.Contains("Emil Pelletsis", item.RenderedText, StringComparison.Ordinal);
        });
        Assert.Equal("Emil Pellets", reloaded.GetSubmissions("profile1")[0].Values["Approver"]);
        Assert.Equal(new[] { "Emil Pelletsis" }, reloaded.GetFieldHistory("profile1", "field-who"));
    }

    [Fact]
    public void RecordSubmission_WhenPersistenceFails_RollsBackInMemory()
    {
        using var tempDirectory = new TemporaryDirectory();
        string fileInsteadOfDirectory = System.IO.Path.Combine(tempDirectory.Path, "not-a-directory");
        System.IO.File.WriteAllText(fileInsteadOfDirectory, "occupied");
        var service = new FormDataService(fileInsteadOfDirectory);

        Assert.False(service.RecordSubmission("profile1", CreateSubmission()));
        Assert.Empty(service.GetSubmissions("profile1"));
    }

    [Fact]
    public void Load_WithNewerSchema_BlocksMutationsAndPreservesFile()
    {
        using var tempDirectory = new TemporaryDirectory();
        string path = System.IO.Path.Combine(tempDirectory.Path, "form-data.json");
        string json = $$"""
            {
              "SchemaVersion": {{FormDataStore.CurrentSchemaVersion + 1}},
              "FutureProperty": "keep-me",
              "Profiles": []
            }
            """;
        System.IO.File.WriteAllText(path, json);
        var service = new FormDataService(tempDirectory.Path);

        Assert.False(service.RecordSubmission("profile1", CreateSubmission()));
        Assert.Equal(json, System.IO.File.ReadAllText(path));
        Assert.Empty(service.GetSubmissions("profile1"));
    }

    [Fact]
    public void DeleteSubmission_RemovesOnlyTheTargetedEntry()
    {
        using var tempDirectory = new TemporaryDirectory();
        var service = new FormDataService(tempDirectory.Path);

        var first = CreateSubmission("first");
        var second = CreateSubmission("second");
        service.RecordSubmission("profile1", first);
        service.RecordSubmission("profile1", second);

        Assert.True(service.DeleteSubmission("profile1", first.Id));
        Assert.False(service.DeleteSubmission("profile1", first.Id));

        var remaining = service.GetSubmissions("profile1");
        Assert.Single(remaining);
        Assert.Equal("second", remaining[0].RenderedText);
    }

    [Fact]
    public void ClearSubmissions_RemovesAllForProfileButKeepsOthers()
    {
        using var tempDirectory = new TemporaryDirectory();
        var service = new FormDataService(tempDirectory.Path);

        service.RecordSubmission("profile1", CreateSubmission());
        service.RecordSubmission("profile2", CreateSubmission());

        Assert.Equal(1, service.ClearSubmissions("profile1"));
        Assert.Empty(service.GetSubmissions("profile1"));
        Assert.Single(service.GetSubmissions("profile2"));
    }

    [Fact]
    public void Load_WithCorruptFile_StartsEmptyWithoutThrowing()
    {
        using var tempDirectory = new TemporaryDirectory();
        System.IO.File.WriteAllText(System.IO.Path.Combine(tempDirectory.Path, "form-data.json"), "{ nope");

        var service = new FormDataService(tempDirectory.Path);

        Assert.Empty(service.GetSubmissions("profile1"));
    }
}
