using GrampsWeb.Mcp.Client;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>
/// Maps stored <see cref="Models.GrampsPlace.Type"/> (plain string or numeric index from mutation payloads) to place-type labels from the API.
/// </summary>
public static class PlaceTypeDisplayFormatter
{
    /// <inheritdoc cref="GrampsDefaultTypeLabels.FormatStoredTypeAsync"/>
    public static Task<string> FormatStoredPlaceTypeAsync(GrampsApiClient client, string? storedType) =>
        GrampsDefaultTypeLabels.FormatStoredTypeAsync(client, storedType, "place_types", "place_types", "placeTypes");

    /// <inheritdoc cref="GrampsDefaultTypeLabels.ResolveStored"/>
    internal static string ResolveStoredPlaceType(string? storedType, IReadOnlyList<string>? labels) =>
        GrampsDefaultTypeLabels.ResolveStored(storedType, labels);

    /// <inheritdoc cref="GrampsDefaultTypeLabels.IsNumericIndex"/>
    internal static bool IsNumericIndex(string t) => GrampsDefaultTypeLabels.IsNumericIndex(t);
}
