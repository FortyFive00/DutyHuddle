using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Plugin.Services;

namespace DutyHuddle.UI;

/// <summary>
/// Icons and fonts the UI needs. In game they come from Dalamud; the offline preview tool supplies its own,
/// which is what makes it possible to render the real screens to PNG without the game.
/// </summary>
public interface IUiAssets
{
    float Scale { get; }

    /// <summary>A game icon texture, or null while it is still loading.</summary>
    ImTextureID? GetGameIcon(uint iconId);

    IDisposable IconFont();

    /// <summary>Condensed heading font (FFXIV's Trump Gothic in game).</summary>
    IDisposable TitleFont();

    /// <summary>Large heading font for the hero card.</summary>
    IDisposable HeroFont();

    /// <summary>
    /// The DutyHuddle logo at the shipped size closest to <paramref name="pixels"/>, drawn at that exact size
    /// so it is never resampled; null while it loads.
    /// </summary>
    (ImTextureID Texture, int Size)? GetLogo(float pixels);
}

public sealed class DalamudUiAssets : IUiAssets, IDisposable
{
    private readonly IUiBuilder uiBuilder;
    private readonly ITextureProvider textures;
    private readonly IFontHandle title;
    private readonly IFontHandle hero;
    private readonly string logoFolder;
    private readonly int[] logoSizes;

    public DalamudUiAssets(IUiBuilder uiBuilder, ITextureProvider textures, string pluginFolder)
    {
        this.uiBuilder = uiBuilder;
        this.textures = textures;
        logoFolder = Path.Combine(pluginFolder, "images", "logo");
        logoSizes = LogoSizes.In(logoFolder);
        title = uiBuilder.FontAtlas.NewGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.TrumpGothic23));
        hero = uiBuilder.FontAtlas.NewGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.TrumpGothic34));
    }

    public float Scale => ImGuiHelpers.GlobalScale;

    public ImTextureID? GetGameIcon(uint iconId)
        => iconId == 0 ? null : textures.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrDefault()?.Handle;

    public (ImTextureID Texture, int Size)? GetLogo(float pixels)
    {
        if (LogoSizes.Closest(logoSizes, pixels) is not { } size
            || textures.GetFromFile(Path.Combine(logoFolder, $"{size}.png")).GetWrapOrDefault() is not { } wrap)
            return null;
        return (wrap.Handle, size);
    }

    public IDisposable IconFont() => uiBuilder.IconFontHandle.Push();

    public IDisposable TitleFont() => title.Push();

    public IDisposable HeroFont() => hero.Push();

    public void Dispose()
    {
        title.Dispose();
        hero.Dispose();
    }
}

/// <summary>The logo sizes shipped in images/logo (one PNG per size, named after it).</summary>
public static class LogoSizes
{
    public static int[] In(string folder)
        => Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*.png")
                .Select(f => int.TryParse(Path.GetFileNameWithoutExtension(f), out var n) ? n : 0)
                .Where(n => n > 0)
                .Order()
                .ToArray()
            : [];

    public static int? Closest(int[] sizes, float pixels)
        => sizes.Length == 0 ? null : sizes.MinBy(n => Math.Abs(n - pixels));
}
