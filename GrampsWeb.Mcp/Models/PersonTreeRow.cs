namespace GrampsWeb.Mcp.Models;

/// <summary>
/// One person in an ancestor or descendant listing with generation metadata.
/// For ancestors, <see cref="AncestorPathFromRoot"/> is one entry per step up from the root (true = father link, false = mother link).
/// For descendants it is <c>null</c>.
/// </summary>
public sealed record PersonTreeRow(
    GrampsPerson Person,
    int Generation,
    IReadOnlyList<bool>? AncestorPathFromRoot);

/// <summary>The root person of a tree, read with its profile, and the ancestor or descendant rows.</summary>
public sealed record PersonTree(GrampsPerson Root, PersonTreeRow[] Rows);
