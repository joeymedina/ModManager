namespace ModManager.Application.Models;

/// <summary>Suggested values for <see cref="ModFile.Category"/>. Not an enum — any string is valid.</summary>
public static class ModCategories
{
    public static readonly IReadOnlyList<string> Suggested =
    [
        "Scripts",
        "CAS",
        "Build/Buy",
        "Gameplay",
        "Overrides/Recolors",
        "Poses/Animations",
        "Traits/Careers",
        "UI",
    ];
}
