using System.Numerics;
using Dalamud.Bindings.ImGui;
using DutyHuddle.Domain.Models;

namespace DutyHuddle.UI;

/// <summary>
/// DutyHuddle's look: deep navy panels with FFXIV-style gold accents. Every colour used by the UI
/// is defined here so the screens stay consistent.
/// </summary>
public static class Theme
{
    public static readonly Vector4 WindowBg = Rgba(13, 17, 28, 0.97f);
    public static readonly Vector4 TitleBar = Rgba(18, 23, 37);
    public static readonly Vector4 Card = Rgba(23, 29, 45);
    public static readonly Vector4 CardRaised = Rgba(32, 40, 61);
    public static readonly Vector4 CardBorder = Rgba(48, 57, 84);
    public static readonly Vector4 Well = Rgba(16, 21, 34);

    /// <summary>Inputs, combos and chips: lighter than both the window and the cards so they read as controls.</summary>
    public static readonly Vector4 Field = Rgba(31, 39, 60);
    public static readonly Vector4 Hover = Rgba(255, 255, 255, 0.045f);

    public static readonly Vector4 Gold = Rgba(232, 196, 104);
    public static readonly Vector4 Blue = Rgba(98, 162, 238);
    public static readonly Vector4 Green = Rgba(102, 208, 142);
    public static readonly Vector4 Amber = Rgba(242, 182, 74);
    public static readonly Vector4 Red = Rgba(236, 112, 112);
    public static readonly Vector4 Grey = Rgba(108, 117, 136);

    public static readonly Vector4 Text = Rgba(232, 235, 242);
    public static readonly Vector4 TextMuted = Rgba(150, 160, 181);
    public static readonly Vector4 TextFaint = Rgba(100, 110, 133);

    /// <summary>Dalamud's global UI scale; layout constants are multiplied by it.</summary>
    public static float Scale { get; set; } = 1f;

    public static float S(float value) => value * Scale;

    public static Vector2 S(float x, float y) => new(x * Scale, y * Scale);

    public static uint U32(Vector4 color) => ImGui.GetColorU32(color);

    public static Vector4 Alpha(Vector4 color, float alpha) => color with { W = alpha };

    public static Vector4 Rgba(int r, int g, int b, float a = 1f) => new(r / 255f, g / 255f, b / 255f, a);

    public static Vector4 ColorOf(Availability state) => state switch
    {
        Availability.Available => Green,
        Availability.Completed => Grey,
        Availability.Locked => TextFaint,
        _ => Amber,
    };

    /// <summary>Pushed around every DutyHuddle window, including its title bar.</summary>
    public static StyleScope PushWindowStyle() => new StyleScope()
        .Var(ImGuiStyleVar.WindowRounding, S(10))
        .Var(ImGuiStyleVar.WindowPadding, S(14, 12))
        .Var(ImGuiStyleVar.FramePadding, S(10, 6))
        .Var(ImGuiStyleVar.FrameRounding, S(6))
        .Var(ImGuiStyleVar.ItemSpacing, S(8, 8))
        .Var(ImGuiStyleVar.ChildRounding, S(10))
        .Var(ImGuiStyleVar.PopupRounding, S(8))
        .Var(ImGuiStyleVar.GrabRounding, S(6))
        .Var(ImGuiStyleVar.ScrollbarRounding, S(8))
        .Var(ImGuiStyleVar.ScrollbarSize, S(10))
        .Var(ImGuiStyleVar.ChildBorderSize, 1f)
        .Var(ImGuiStyleVar.FrameBorderSize, 0f)
        .Color(ImGuiCol.WindowBg, WindowBg)
        .Color(ImGuiCol.TitleBg, TitleBar)
        .Color(ImGuiCol.TitleBgActive, TitleBar)
        .Color(ImGuiCol.TitleBgCollapsed, TitleBar)
        .Color(ImGuiCol.Border, CardBorder)
        .Color(ImGuiCol.ChildBg, Alpha(Card, 0f))
        .Color(ImGuiCol.PopupBg, Rgba(20, 26, 41, 0.98f))
        .Color(ImGuiCol.FrameBg, Field)
        .Color(ImGuiCol.FrameBgHovered, Rgba(37, 46, 70))
        .Color(ImGuiCol.FrameBgActive, Rgba(42, 52, 79))
        .Color(ImGuiCol.Button, CardRaised)
        .Color(ImGuiCol.ButtonHovered, Rgba(44, 54, 82))
        .Color(ImGuiCol.ButtonActive, Rgba(56, 68, 102))
        .Color(ImGuiCol.Header, CardRaised)
        .Color(ImGuiCol.HeaderHovered, Rgba(40, 49, 74))
        .Color(ImGuiCol.HeaderActive, Rgba(48, 58, 88))
        .Color(ImGuiCol.CheckMark, Gold)
        .Color(ImGuiCol.SliderGrab, Gold)
        .Color(ImGuiCol.SliderGrabActive, Rgba(244, 214, 136))
        .Color(ImGuiCol.Separator, CardBorder)
        .Color(ImGuiCol.Text, Text)
        .Color(ImGuiCol.TextDisabled, TextFaint)
        .Color(ImGuiCol.ScrollbarBg, Alpha(Well, 0f))
        .Color(ImGuiCol.ScrollbarGrab, Rgba(52, 62, 92))
        .Color(ImGuiCol.ScrollbarGrabHovered, Rgba(66, 78, 114))
        .Color(ImGuiCol.ScrollbarGrabActive, Rgba(80, 94, 136))
        .Color(ImGuiCol.TableHeaderBg, Card)
        .Color(ImGuiCol.TableRowBg, Alpha(Card, 0f))
        .Color(ImGuiCol.TableRowBgAlt, Rgba(255, 255, 255, 0.025f))
        .Color(ImGuiCol.TableBorderLight, CardBorder)
        .Color(ImGuiCol.TableBorderStrong, CardBorder);
}

/// <summary>Counts pushed style values so they are always popped in one place.</summary>
public sealed class StyleScope : IDisposable
{
    private int colors;
    private int vars;

    public StyleScope Color(ImGuiCol index, Vector4 color)
    {
        ImGui.PushStyleColor(index, color);
        colors++;
        return this;
    }

    public StyleScope Var(ImGuiStyleVar index, float value)
    {
        ImGui.PushStyleVar(index, value);
        vars++;
        return this;
    }

    public StyleScope Var(ImGuiStyleVar index, Vector2 value)
    {
        ImGui.PushStyleVar(index, value);
        vars++;
        return this;
    }

    public void Dispose()
    {
        if (vars > 0)
            ImGui.PopStyleVar(vars);
        if (colors > 0)
            ImGui.PopStyleColor(colors);
        vars = 0;
        colors = 0;
    }
}
