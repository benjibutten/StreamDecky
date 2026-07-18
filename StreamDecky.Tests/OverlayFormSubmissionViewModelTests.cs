using StreamDecky.Models;
using StreamDecky.ViewModels;
using Xunit;

namespace StreamDecky.Tests;

public sealed class OverlayFormSubmissionViewModelTests
{
    [Fact]
    public void UncheckingCompleted_ResetsEveryCopiedField()
    {
        var submission = new FormSubmission
        {
            Values = new Dictionary<string, string>
            {
                ["First"] = "One",
                ["Second"] = "Two"
            }
        };
        var viewModel = new OverlayFormSubmissionViewModel(submission, (_, _) => true);
        foreach (var field in viewModel.FieldValues)
            field.IsCopied = true;

        viewModel.IsCompleted = true;
        viewModel.IsCompleted = false;

        Assert.All(viewModel.FieldValues, field => Assert.False(field.IsCopied));
    }

    [Fact]
    public void EditableField_CommitsCorrectionAndRequiresPermission()
    {
        var submission = new FormSubmission
        {
            Values = new Dictionary<string, string> { ["Title"] = "Misspeled" }
        };
        string? savedValue = null;
        var viewModel = new OverlayFormSubmissionViewModel(
            submission,
            (_, _) => true,
            (_, _, value) =>
            {
                savedValue = value;
                return true;
            },
            label => label == "Title");
        var field = Assert.Single(viewModel.FieldValues);

        field.BeginEdit();
        field.EditValue = "Misspelled";
        field.CommitEdit();

        Assert.Equal("Misspelled", savedValue);
        Assert.Equal("Misspelled", field.Value);
        Assert.False(field.IsEditing);
    }

    [Fact]
    public void Copy_WhenClipboardFails_DoesNotMarkFieldOrSubmissionCompleted()
    {
        bool completed = false;
        var field = new OverlayFormSubmissionFieldViewModel(
            "Title",
            "Value",
            canEdit: false,
            () => completed = true,
            (_, _) => true,
            _ => false);

        field.CopyCommand.Execute(null);

        Assert.False(field.IsCopied);
        Assert.False(completed);
    }

    [Fact]
    public void CompletedChange_IsPersistedBeforeTheModelIsUpdated()
    {
        var submission = new FormSubmission();
        bool modelWasStillUnchanged = false;
        var viewModel = new OverlayFormSubmissionViewModel(
            submission,
            (vm, completed) =>
            {
                modelWasStillUnchanged = !vm.Model.IsCompleted;
                return true;
            });

        viewModel.IsCompleted = true;

        Assert.True(modelWasStillUnchanged);
        Assert.True(submission.IsCompleted);
    }
}
