namespace IIoT.Edge.Presentation.Navigation.Features.DiagnosticsView;

internal static class DiagnosticsTextNormalizer
{
    public static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? "--"
            : value;
}

