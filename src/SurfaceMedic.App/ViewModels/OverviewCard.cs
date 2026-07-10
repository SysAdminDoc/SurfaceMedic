using SurfaceMedic.App.Infrastructure;

namespace SurfaceMedic.App.ViewModels;

public sealed class OverviewCard : ObservableObject
{
    private string _primaryText = "Collecting diagnostics...";
    private string _secondaryText = "This usually takes only a moment.";
    private string _status = "Loading";

    public required string Title { get; init; }

    public required string Glyph { get; init; }

    public required AppPage TargetPage { get; init; }

    public string PrimaryText
    {
        get => _primaryText;
        set => SetProperty(ref _primaryText, value);
    }

    public string SecondaryText
    {
        get => _secondaryText;
        set => SetProperty(ref _secondaryText, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }
}
