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
}

public sealed class DalamudUiAssets : IUiAssets, IDisposable
{
    private readonly IUiBuilder uiBuilder;
    private readonly ITextureProvider textures;
    private readonly IFontHandle title;
    private readonly IFontHandle hero;

    public DalamudUiAssets(IUiBuilder uiBuilder, ITextureProvider textures)
    {
        this.uiBuilder = uiBuilder;
        this.textures = textures;
        title = uiBuilder.FontAtlas.NewGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.TrumpGothic23));
        hero = uiBuilder.FontAtlas.NewGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.TrumpGothic34));
    }

    public float Scale => ImGuiHelpers.GlobalScale;

    public ImTextureID? GetGameIcon(uint iconId)
        => iconId == 0 ? null : textures.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrDefault()?.Handle;

    public IDisposable IconFont() => uiBuilder.IconFontHandle.Push();

    public IDisposable TitleFont() => title.Push();

    public IDisposable HeroFont() => hero.Push();

    public void Dispose()
    {
        title.Dispose();
        hero.Dispose();
    }
}
