using StreamDecky.Models;

namespace StreamDecky.ViewModels;

/// <summary>Read-only presentation of a stored form submission for the editor history view.</summary>
public class FormSubmissionViewModel
{
    public FormSubmissionViewModel(FormSubmission model)
    {
        Model = model;
    }

    public FormSubmission Model { get; }

    public string Id => Model.Id;
    public bool IsCompleted => Model.IsCompleted;
    public string TemplateName => string.IsNullOrWhiteSpace(Model.TemplateName) ? "(unnamed form)" : Model.TemplateName;
    public string CreatedText => Model.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public string RenderedText => Model.RenderedText;
    public string ValuesSummary => string.Join("  •  ", Model.Values
        .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
        .Select(pair => $"{pair.Key}: {pair.Value}"));
    public bool HasValuesSummary => !string.IsNullOrWhiteSpace(ValuesSummary);
}
