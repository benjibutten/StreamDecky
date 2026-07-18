using StreamDecky.Models;
using StreamDecky.Services;
using StreamDecky.ViewModels;
using Xunit;

namespace StreamDecky.Tests;

public sealed class FormTemplateProfileTests
{
    [Fact]
    public async Task SharedSuggestionKey_SharesValuesAcrossFormsOnlyWhenOptedIn()
    {
        using var tempDirectory = new TemporaryDirectory();
        var profileService = new ProfileService(tempDirectory.Path);
        var firstTemplate = new FormTemplate { Name = "First", OutputTemplate = "{name}" };
        firstTemplate.Fields.Add(new FormField
        {
            Key = "name",
            Label = "Name",
            RememberHistory = true,
            SharedSuggestionKey = "PERSON-NAME"
        });
        var secondTemplate = new FormTemplate { Name = "Second", OutputTemplate = "{customer}" };
        secondTemplate.Fields.Add(new FormField
        {
            Key = "customer",
            Label = "Customer",
            RememberHistory = true,
            SharedSuggestionKey = "person-name"
        });
        var isolatedTemplate = new FormTemplate { Name = "Isolated", OutputTemplate = "{name}" };
        isolatedTemplate.Fields.Add(new FormField
        {
            Key = "name",
            Label = "Name",
            RememberHistory = true
        });
        var profile = new DeckProfile
        {
            FormTemplates = new List<FormTemplate> { firstTemplate, secondTemplate, isolatedTemplate },
            ActiveFormTemplateId = firstTemplate.Id
        };
        var store = new DeckProfileStore
        {
            ActiveProfileId = profile.Id,
            Profiles = new List<DeckProfile> { profile }
        };
        store.Initialize();
        profileService.SaveStore(store);
        using var viewModel = new MainViewModel(
            profileService,
            formDataService: new FormDataService(tempDirectory.Path));
        Assert.Single(viewModel.FormSessionFields).Value = "Ada";
        Assert.True(await viewModel.RecordFormSubmissionAsync(viewModel.GetRenderedFormText()));

        viewModel.OverlayFormTemplate = viewModel.FormTemplates.Single(template => template.Id == secondTemplate.Id);
        Assert.Contains("Ada", Assert.Single(viewModel.FormSessionFields).Suggestions);

        viewModel.OverlayFormTemplate = viewModel.FormTemplates.Single(template => template.Id == isolatedTemplate.Id);
        Assert.DoesNotContain("Ada", Assert.Single(viewModel.FormSessionFields).Suggestions);
    }

    [Fact]
    public void EditorHistory_ShowsOnlySelectedFormTemplate()
    {
        using var tempDirectory = new TemporaryDirectory();
        var profileService = new ProfileService(tempDirectory.Path);
        var firstTemplate = new FormTemplate { Name = "First" };
        var secondTemplate = new FormTemplate { Name = "Second" };
        var profile = new DeckProfile
        {
            FormTemplates = new List<FormTemplate> { firstTemplate, secondTemplate },
            ActiveFormTemplateId = firstTemplate.Id
        };
        var store = new DeckProfileStore
        {
            ActiveProfileId = profile.Id,
            Profiles = new List<DeckProfile> { profile }
        };
        store.Initialize();
        profileService.SaveStore(store);
        var formDataService = new FormDataService(tempDirectory.Path);
        formDataService.RecordSubmission(profile.Id, new FormSubmission
        {
            TemplateId = firstTemplate.Id,
            TemplateName = firstTemplate.Name,
            RenderedText = "first"
        });
        formDataService.RecordSubmission(profile.Id, new FormSubmission
        {
            TemplateId = secondTemplate.Id,
            TemplateName = secondTemplate.Name,
            RenderedText = "second"
        });
        using var viewModel = new MainViewModel(profileService, formDataService: formDataService);

        Assert.Equal("first", Assert.Single(viewModel.FormSubmissions).RenderedText);
        Assert.Equal(2, viewModel.OverlayFormSubmissions.Count);

        viewModel.SelectedFormTemplate = viewModel.FormTemplates.Single(template => template.Id == secondTemplate.Id);

        Assert.Equal("second", Assert.Single(viewModel.FormSubmissions).RenderedText);
        Assert.Equal(2, viewModel.OverlayFormSubmissions.Count);
    }

    [Fact]
    public async Task RecordFormSubmission_RequiredEmptyFieldIsRejected()
    {
        using var tempDirectory = new TemporaryDirectory();
        var profileService = new ProfileService(tempDirectory.Path);
        var template = new FormTemplate { Name = "Required", OutputTemplate = "Hello {who}" };
        template.Fields.Add(new FormField { Key = "who", Label = "Who", IsRequired = true });
        var profile = new DeckProfile
        {
            FormTemplates = new List<FormTemplate> { template },
            ActiveFormTemplateId = template.Id
        };
        var store = new DeckProfileStore
        {
            ActiveProfileId = profile.Id,
            Profiles = new List<DeckProfile> { profile }
        };
        store.Initialize();
        profileService.SaveStore(store);
        var formDataService = new FormDataService(tempDirectory.Path);
        using var viewModel = new MainViewModel(profileService, formDataService: formDataService);

        Assert.False(viewModel.CanSubmitFormResult);
        Assert.False(viewModel.SaveFormResultCommand.CanExecute(null));
        Assert.False(await viewModel.RecordFormSubmissionAsync(viewModel.GetRenderedFormText()));
        Assert.Contains("Who is required.", viewModel.FormSessionValidationText, StringComparison.Ordinal);
        Assert.Empty(formDataService.GetSubmissions(profile.Id));

        Assert.Single(viewModel.FormSessionFields).Value = "Ada";
        Assert.True(viewModel.CanSubmitFormResult);
        Assert.True(viewModel.SaveFormResultCommand.CanExecute(null));
    }

    [Fact]
    public async Task RecordFormSubmission_PersistsAdvancedCounterBeforeReturning()
    {
        using var tempDirectory = new TemporaryDirectory();
        var profileService = new ProfileService(tempDirectory.Path);
        var template = new FormTemplate
        {
            Name = "Ticket",
            OutputTemplate = "Ticket {ticketNo}"
        };
        template.Counters.Add(new FormCounter { Name = "ticketNo", NextValue = 41 });
        var profile = new DeckProfile
        {
            Name = "Test",
            FormTemplates = new List<FormTemplate> { template },
            ActiveFormTemplateId = template.Id
        };
        var store = new DeckProfileStore
        {
            ActiveProfileId = profile.Id,
            Profiles = new List<DeckProfile> { profile }
        };
        store.Initialize();
        profileService.SaveStore(store);

        var formDataService = new FormDataService(tempDirectory.Path);
        using var viewModel = new MainViewModel(profileService, formDataService: formDataService);
        string rendered = viewModel.GetRenderedFormText();

        Assert.True(await viewModel.RecordFormSubmissionAsync(rendered));

        var persistedProfile = new ProfileService(tempDirectory.Path).LoadStore().GetActiveProfile();
        Assert.Equal(42, Assert.Single(Assert.Single(persistedProfile.FormTemplates).Counters).NextValue);
        Assert.Equal("Ticket 41", Assert.Single(new FormDataService(tempDirectory.Path)
            .GetSubmissions(profile.Id)).RenderedText);
    }

    [Fact]
    public void MigrateProfile_FromV4_InitializesFormDataAndBumpsVersion()
    {
        var profile = new DeckProfile
        {
            SchemaVersion = ProfileSchemaVersion.GlobalQuickTextTags,
            FormTemplates = null!,
            ActiveFormTemplateId = null!
        };

        ProfileSchemaMigrator.MigrateProfile(profile);

        Assert.Equal(ProfileSchemaVersion.Current, profile.SchemaVersion);
        Assert.NotNull(profile.FormTemplates);
        Assert.Empty(profile.FormTemplates);
        Assert.Equal(string.Empty, profile.ActiveFormTemplateId);
    }

    [Fact]
    public void SerializeStore_RoundTripsFormTemplates()
    {
        var template = new FormTemplate
        {
            Name = "Payment note",
            OutputTemplate = "Invoice {invoiceNo} - {who}"
        };
        template.Fields.Add(new FormField
        {
            Key = "who",
            Label = "Who?",
            IsRequired = true,
            RememberHistory = true,
            SharedSuggestionKey = "person-name",
            AllowHistoryEditing = true
        });
        var choice = new FormField
        {
            Key = "description",
            Label = "Description",
            Type = FormFieldType.Choice,
            IsMultiline = true
        };
        choice.Options.Add(new FormFieldOption { Label = "Rent", Text = "Monthly rent for the office" });
        template.Fields.Add(choice);
        template.Counters.Add(new FormCounter { Name = "invoiceNo", NextValue = 7, PadWidth = 3 });
        template.ActionSteps.Add(new ActionStep { Type = ActionStepType.TextInput, PressEnterAfter = true });
        template.EnsureInitialized();

        var store = new DeckProfileStore
        {
            Profiles = new List<DeckProfile>
            {
                new()
                {
                    Name = "Test",
                    FormTemplates = new List<FormTemplate> { template },
                    ActiveFormTemplateId = template.Id,
                    FormsHistoryCountsTodayOnly = false
                }
            }
        };
        store.Initialize();

        string json = ProfileService.SerializeStoreJson(store);
        DeckProfileStore reloaded = ProfileService.DeserializeStoreJson(json);

        var reloadedProfile = reloaded.GetActiveProfile();
        var reloadedTemplate = Assert.Single(reloadedProfile.FormTemplates);
        Assert.Equal(template.Id, reloadedProfile.ActiveFormTemplateId);
        Assert.False(reloadedProfile.FormsHistoryCountsTodayOnly);
        Assert.Equal("Payment note", reloadedTemplate.Name);
        Assert.Equal("Invoice {invoiceNo} - {who}", reloadedTemplate.OutputTemplate);
        Assert.Equal(2, reloadedTemplate.Fields.Count);
        Assert.True(reloadedTemplate.Fields[0].RememberHistory);
        Assert.Equal("person-name", reloadedTemplate.Fields[0].SharedSuggestionKey);
        Assert.True(reloadedTemplate.Fields[0].IsRequired);
        Assert.True(reloadedTemplate.Fields[0].AllowHistoryEditing);
        Assert.Equal(FormFieldType.Choice, reloadedTemplate.Fields[1].Type);
        Assert.Equal("Monthly rent for the office", Assert.Single(reloadedTemplate.Fields[1].Options).Text);
        var counter = Assert.Single(reloadedTemplate.Counters);
        Assert.Equal(7, counter.NextValue);
        Assert.Equal(3, counter.PadWidth);
        Assert.Equal("007", counter.FormatValue());
        var step = Assert.Single(reloadedTemplate.ActionSteps);
        Assert.Equal(ActionStepType.TextInput, step.Type);
        Assert.True(step.PressEnterAfter);
    }

    [Fact]
    public void NewProfile_CountsOnlyTodaysFormSubmissionsByDefault()
    {
        Assert.True(new DeckProfile().FormsHistoryCountsTodayOnly);
    }

    [Fact]
    public void Initialize_WithStaleActiveFormTemplateId_FallsBackToFirstTemplate()
    {
        var template = new FormTemplate { Name = "A" };
        template.EnsureInitialized();
        var profile = new DeckProfile
        {
            FormTemplates = new List<FormTemplate> { template },
            ActiveFormTemplateId = "does-not-exist"
        };

        profile.Initialize();

        Assert.Equal(template.Id, profile.ActiveFormTemplateId);
    }

    [Fact]
    public void Initialize_ClampsFormsPanelMetrics()
    {
        var profile = new DeckProfile
        {
            FormsPanelWidth = 5,
            FormsPanelHeight = 100000,
            FormsPanelX = -50,
            FormsPanelY = -10
        };

        profile.Initialize();

        Assert.Equal(DeckProfile.MinFormsPanelWidth, profile.FormsPanelWidth);
        Assert.Equal(DeckProfile.MaxFormsPanelHeight, profile.FormsPanelHeight);
        Assert.Equal(0, profile.FormsPanelX);
        Assert.Equal(0, profile.FormsPanelY);
    }
}
