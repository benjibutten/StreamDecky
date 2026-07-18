namespace StreamDecky.Models;

/// <summary>
/// One completed form fill-in. Stored outside the profile store in
/// form-data.json so frequent submissions never rewrite profiles.json and
/// personal history stays out of profile exports.
/// </summary>
public class FormSubmission
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TemplateId { get; set; } = string.Empty;
    /// <summary>Snapshot of the template name so history stays readable after
    /// the template is renamed or deleted.</summary>
    public string TemplateName { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    /// <summary>Field label → entered value, for structured inspection later.</summary>
    public Dictionary<string, string> Values { get; set; } = new();
    /// <summary>Field token → resolved value. Kept separately from the
    /// display-label dictionary so corrected history can be rendered safely.</summary>
    public Dictionary<string, string> TokenValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Display label → field token used by <see cref="Values"/>.</summary>
    public Dictionary<string, string> FieldTokens { get; set; } = new();
    /// <summary>Display label → stable field id. Used to apply a corrected
    /// historical value to every matching entry for the same logical field.</summary>
    public Dictionary<string, string> FieldIds { get; set; } = new();
    /// <summary>The effective output template with counters and built-ins frozen
    /// at submission time, while field tokens remain replaceable.</summary>
    public string OutputTemplateSnapshot { get; set; } = string.Empty;
    public string RenderedText { get; set; } = string.Empty;
    /// <summary>Marked done by the user, e.g. after pasting every field into the
    /// game. Set automatically when all field values have been copied.</summary>
    public bool IsCompleted { get; set; }

    public void EnsureInitialized()
    {
        if (string.IsNullOrWhiteSpace(Id))
            Id = Guid.NewGuid().ToString("N");

        TemplateId ??= string.Empty;
        TemplateName ??= string.Empty;
        Values ??= new Dictionary<string, string>();
        TokenValues = new Dictionary<string, string>(TokenValues ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        FieldTokens ??= new Dictionary<string, string>();
        FieldIds ??= new Dictionary<string, string>();
        OutputTemplateSnapshot ??= string.Empty;
        RenderedText ??= string.Empty;
    }
}
