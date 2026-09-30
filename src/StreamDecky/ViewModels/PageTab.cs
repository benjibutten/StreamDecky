namespace StreamDecky.ViewModels;

/// <param name="Background">The tab's fill as a colour string WPF can parse.</param>
public sealed record PageTab(string Id, string Name, bool IsCurrent, string Background);
