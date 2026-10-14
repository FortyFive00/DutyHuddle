using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using DutyHuddle.Domain.Analysis;
using DutyHuddle.Domain.Models;

namespace DutyHuddle.UI;

/// <summary>
/// DutyHuddle's component kit: cards, toggles, pills, avatars, bars and status glyphs, all drawn with the
/// draw list so the screens do not look like stock ImGui. Dynamic strings go through TextUnformatted.
/// </summary>
public sealed class Widgets
{
    public Widgets(IUiAssets assets)
    {
        Assets = assets;
    }

    public IUiAssets Assets { get; }

    // ---------------------------------------------------------------- text

    public static void Text(string text, Vector4? color = null)
    {
        if (color is { } c)
            ImGui.PushStyleColor(ImGuiCol.Text, c);
        ImGui.TextUnformatted(text);
        if (color is not null)
            ImGui.PopStyleColor();
    }

    /// <summary>
    /// Wraps at the current wrap position: cards set it to their inner width, child cards to their edge.
    /// TextUnformatted honours the wrap position and never interprets format specifiers.
    /// </summary>
    public static void Wrapped(string text, Vector4? color = null) => Text(text, color);

    public void Title(string text, Vector4? color = null)
    {
        using (Assets.TitleFont())
            Text(text, color ?? Theme.Text);
    }

    public void Hero(string text, Vector4? color = null)
    {
        using (Assets.HeroFont())
            Text(text, color ?? Theme.Gold);
    }

    /// <summary>Small upper-case label above a section, like FFXIV's window headings.</summary>
    public static void Eyebrow(string text) => Text(text.ToUpperInvariant(), Theme.TextFaint);

    public static void Tooltip(string text)
    {
        if (ImGui.IsItemHovered())
            TooltipNow(text);
    }

    /// <summary>Shows a tooltip this frame, for hit-testing done by hand.</summary>
    public static void TooltipNow(string text)
    {
        using var style = new StyleScope()
            .Var(ImGuiStyleVar.WindowPadding, Theme.S(10, 8))
            .Color(ImGuiCol.Border, Theme.CardBorder);
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 30);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

    // ---------------------------------------------------------------- icons

    /// <summary>A game icon with rounded corners; a dim placeholder while it loads.</summary>
    public void GameIcon(uint iconId, float size, float rounding = 0.18f)
    {
        var min = ImGui.GetCursorScreenPos();
        var max = min + new Vector2(size);
        var draw = ImGui.GetWindowDrawList();
        if (Assets.GetGameIcon(iconId) is { } texture)
            draw.AddImageRounded(texture, min, max, Vector2.Zero, Vector2.One, 0xFFFFFFFF, size * rounding);
        else
            draw.AddRectFilled(min, max, Theme.U32(Theme.Well), size * rounding);
        ImGui.Dummy(new Vector2(size));
    }

    public void Glyph(FontAwesomeIcon icon, Vector4? color = null)
    {
        using (Assets.IconFont())
            Text(icon.ToIconString(), color ?? Theme.TextMuted);
    }

    /// <summary>Flat icon-only button.</summary>
    public bool IconButton(string id, FontAwesomeIcon icon, string tooltip, Vector4? color = null)
    {
        bool clicked;
        using (new StyleScope()
                   .Color(ImGuiCol.Button, Theme.Alpha(Theme.CardRaised, 0f))
                   .Color(ImGuiCol.ButtonHovered, Theme.CardRaised)
                   .Color(ImGuiCol.Text, color ?? Theme.TextMuted))
        using (Assets.IconFont())
            clicked = ImGui.Button($"{icon.ToIconString()}##{id}");
        Tooltip(tooltip);
        return clicked;
    }

    /// <summary>Width <see cref="Button"/> will take for this icon and label.</summary>
    public float ButtonWidth(FontAwesomeIcon icon, string label, float minimum = 0)
    {
        float iconWidth;
        using (Assets.IconFont())
            iconWidth = ImGui.CalcTextSize(icon.ToIconString()).X;
        return Math.Max(minimum, iconWidth + Theme.S(7) + ImGui.CalcTextSize(label).X + ImGui.GetStyle().FramePadding.X * 2 + Theme.S(8));
    }

    /// <summary>Button with an icon and a label; gold when <paramref name="primary"/>.</summary>
    public bool Button(string id, FontAwesomeIcon icon, string label, bool primary = false, float width = 0)
    {
        string iconText;
        float iconWidth;
        using (Assets.IconFont())
        {
            iconText = icon.ToIconString();
            iconWidth = ImGui.CalcTextSize(iconText).X;
        }

        var gap = Theme.S(7);
        var textWidth = ImGui.CalcTextSize(label).X;
        var size = new Vector2(ButtonWidth(icon, label, width), ImGui.GetFrameHeight());

        var start = ImGui.GetCursorScreenPos();
        bool clicked;
        using (primary
                   ? new StyleScope()
                       .Color(ImGuiCol.Button, Theme.Gold)
                       .Color(ImGuiCol.ButtonHovered, Theme.Rgba(244, 212, 132))
                       .Color(ImGuiCol.ButtonActive, Theme.Rgba(206, 170, 84))
                   : new StyleScope())
            clicked = ImGui.Button($"##{id}", size);

        var textColor = Theme.U32(primary ? Theme.Rgba(24, 22, 18) : Theme.Text);
        var x = start.X + (size.X - iconWidth - gap - textWidth) / 2;
        var draw = ImGui.GetWindowDrawList();
        using (Assets.IconFont())
        {
            var iconY = start.Y + (size.Y - ImGui.GetTextLineHeight()) / 2;
            draw.AddText(new Vector2(x, iconY), textColor, iconText);
        }

        draw.AddText(new Vector2(x + iconWidth + gap, start.Y + (size.Y - ImGui.GetTextLineHeight()) / 2), textColor, label);
        return clicked;
    }

    /// <summary>The DutyHuddle logo; until the image has loaded, a drawn stand-in of the same size.</summary>
    public void Logo(float size)
    {
        var origin = ImGui.GetCursorScreenPos() + new Vector2(0, Theme.S(4));
        var draw = ImGui.GetWindowDrawList();
        if (Assets.GetLogo(size) is var (logo, pixels))
        {
            // Native size on whole pixels: one texel per pixel, so nothing gets blurred.
            var min = new Vector2(MathF.Round(origin.X + (size - pixels) / 2), MathF.Round(origin.Y + (size - pixels) / 2));
            draw.AddImage(logo, min, min + new Vector2(pixels));
        }
        else
            draw.AddRectFilled(origin, origin + new Vector2(size), Theme.U32(Theme.Rgba(24, 32, 54)), size * 0.22f);

        ImGui.Dummy(new Vector2(size, size + Theme.S(4)));
    }

    // ---------------------------------------------------------------- small primitives

    /// <summary>The roulette status glyph: filled (available), check (done), dashed (unknown), bar (not eligible).</summary>
    public static void StatusGlyph(Availability state, float size)
    {
        DrawStatusGlyph(ImGui.GetWindowDrawList(), ImGui.GetCursorScreenPos() + new Vector2(size / 2), size, state);
        ImGui.Dummy(new Vector2(size));
    }

    public static void DrawStatusGlyph(ImDrawListPtr draw, Vector2 center, float size, Availability state)
    {
        var radius = size * 0.36f;
        var color = Theme.U32(Theme.ColorOf(state));
        switch (state)
        {
            case Availability.Available:
                draw.AddCircleFilled(center, radius + 2, Theme.U32(Theme.Alpha(Theme.Green, 0.18f)));
                draw.AddCircleFilled(center, radius * 0.72f, color);
                break;
            case Availability.Completed:
                draw.AddCircle(center, radius, color, 24, 1.6f);
                draw.AddLine(center + new Vector2(-radius * 0.45f, radius * 0.02f), center + new Vector2(-radius * 0.08f, radius * 0.38f), color, 1.8f);
                draw.AddLine(center + new Vector2(-radius * 0.08f, radius * 0.38f), center + new Vector2(radius * 0.5f, -radius * 0.36f), color, 1.8f);
                break;
            case Availability.Locked:
                draw.AddLine(center - new Vector2(radius * 0.7f, 0), center + new Vector2(radius * 0.7f, 0), color, 2f);
                break;
            default:
                DashedCircle(draw, center, radius, color, 1.6f);
                break;
        }
    }

    /// <summary>Who needs a WT duty: filled = needs it, hollow = does not, dashed = unknown journal.</summary>
    public static void CoverageDot(bool? needs, float size)
    {
        DrawCoverageDot(ImGui.GetWindowDrawList(), ImGui.GetCursorScreenPos() + new Vector2(size / 2), size, needs);
        ImGui.Dummy(new Vector2(size));
    }

    public static void DrawCoverageDot(ImDrawListPtr draw, Vector2 center, float size, bool? needs)
    {
        var radius = size * 0.3f;
        switch (needs)
        {
            case true:
                draw.AddCircleFilled(center, radius, Theme.U32(Theme.Blue));
                break;
            case false:
                draw.AddCircle(center, radius, Theme.U32(Theme.Grey), 20, 1.3f);
                break;
            default:
                DashedCircle(draw, center, radius, Theme.U32(Theme.Amber), 1.3f);
                break;
        }
    }

    public void DrawGameIcon(ImDrawListPtr draw, Vector2 min, float size, uint iconId, float rounding = 0.18f)
    {
        var max = min + new Vector2(size);
        if (Assets.GetGameIcon(iconId) is { } texture)
            draw.AddImageRounded(texture, min, max, Vector2.Zero, Vector2.One, 0xFFFFFFFF, size * rounding);
        else
            draw.AddRectFilled(min, max, Theme.U32(Theme.Well), size * rounding);
    }

    public static void DrawMiniBar(ImDrawListPtr draw, Vector2 min, Vector2 size, float fraction, Vector4 color)
    {
        var rounding = size.Y / 2;
        draw.AddRectFilled(min, min + size, Theme.U32(Theme.Well), rounding);
        var filled = Math.Clamp(fraction, 0f, 1f) * size.X;
        if (filled > 0.5f)
            draw.AddRectFilled(min, new Vector2(min.X + Math.Max(filled, size.Y), min.Y + size.Y), Theme.U32(color), rounding);
    }

    /// <summary>A hoverable full-width row. Returns the row rectangle and whether it is hovered.</summary>
    public static (Vector2 Min, Vector2 Max, bool Hovered, bool Clicked) Row(string id, float height, bool highlight = false, Vector4? highlightColor = null)
    {
        var min = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var clicked = ImGui.InvisibleButton(id, new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        var max = min + new Vector2(width, height);
        var draw = ImGui.GetWindowDrawList();
        var rounding = Theme.S(7);
        if (highlight)
        {
            var color = highlightColor ?? Theme.Gold;
            draw.AddRectFilled(min, max, Theme.U32(Theme.Alpha(color, 0.08f)), rounding);
            draw.AddRectFilled(min + new Vector2(0, Theme.S(6)), new Vector2(min.X + Theme.S(3), max.Y - Theme.S(6)), Theme.U32(color), Theme.S(1.5f));
        }

        if (hovered)
            draw.AddRectFilled(min, max, Theme.U32(Theme.Hover), rounding);
        return (min, max, hovered, clicked);
    }

    public static void Dot(Vector4 color, float radius)
    {
        var height = ImGui.GetTextLineHeight();
        var center = ImGui.GetCursorScreenPos() + new Vector2(radius, height / 2);
        ImGui.GetWindowDrawList().AddCircleFilled(center, radius, Theme.U32(color));
        ImGui.Dummy(new Vector2(radius * 2, height));
    }

    public static void ProgressBar(float fraction, Vector2 size, Vector4 color)
    {
        var min = ImGui.GetCursorScreenPos();
        var max = min + size;
        var draw = ImGui.GetWindowDrawList();
        var rounding = size.Y / 2;
        draw.AddRectFilled(min, max, Theme.U32(Theme.Well), rounding);
        var filled = Math.Clamp(fraction, 0f, 1f) * size.X;
        if (filled > 0.5f)
            draw.AddRectFilled(min, new Vector2(min.X + Math.Max(filled, size.Y), max.Y), Theme.U32(color), rounding);
        ImGui.Dummy(size);
    }

    /// <summary>Row of small circles, e.g. Second Chance points (filled / total).</summary>
    public static void Pips(int filled, int total, Vector4 color, float radius)
    {
        var height = ImGui.GetTextLineHeight();
        var start = ImGui.GetCursorScreenPos();
        var gap = radius * 0.9f;
        var draw = ImGui.GetWindowDrawList();
        for (var i = 0; i < total; i++)
        {
            var center = start + new Vector2(radius + i * (radius * 2 + gap), height / 2);
            if (i < filled)
                draw.AddCircleFilled(center, radius, Theme.U32(color));
            else
                draw.AddCircle(center, radius - 0.5f, Theme.U32(Theme.Grey), 16, 1.2f);
        }

        ImGui.Dummy(new Vector2(total * (radius * 2 + gap) - gap, height));
    }

    /// <summary>Round avatar: the member's job icon when known, otherwise their initial.</summary>
    public void Avatar(PartyMemberState member, float size, Vector4? ring = null)
    {
        DrawAvatar(ImGui.GetWindowDrawList(), ImGui.GetCursorScreenPos(), size, member, ring);
        ImGui.Dummy(new Vector2(size));
    }

    public void DrawAvatar(ImDrawListPtr draw, Vector2 min, float size, PartyMemberState member, Vector4? ring = null)
    {
        var center = min + new Vector2(size / 2);
        draw.AddCircleFilled(center, size / 2, Theme.U32(Theme.CardRaised));

        var icon = member.ClassJobId > 0 ? Assets.GetGameIcon(JobIcon(member.ClassJobId)) : null;
        if (icon is { } texture)
        {
            var inset = size * 0.12f;
            draw.AddImageRounded(texture, min + new Vector2(inset), min + new Vector2(size - inset), Vector2.Zero, Vector2.One, 0xFFFFFFFF, size / 2);
        }
        else
        {
            var initial = member.Member.Name.Length > 0 ? member.Member.Name[..1].ToUpperInvariant() : "?";
            var textSize = ImGui.CalcTextSize(initial);
            draw.AddText(center - textSize / 2, Theme.U32(Theme.Text), initial);
        }

        if (ring is { } color)
            draw.AddCircle(center, size / 2 - 0.5f, Theme.U32(color), 32, Theme.S(1.6f));
    }

    /// <summary>Framed job icons live at 062100 + ClassJob row.</summary>
    public static uint JobIcon(uint classJobId) => 62100 + classJobId;

    /// <summary>iOS-style switch. Returns true when it was flipped.</summary>
    public static bool Toggle(string id, ref bool value)
    {
        var height = ImGui.GetFrameHeight() * 0.82f;
        var width = height * 1.85f;
        var origin = ImGui.GetCursorScreenPos();
        var offsetY = (ImGui.GetFrameHeight() - height) / 2;
        var min = origin + new Vector2(0, offsetY);

        var clicked = ImGui.InvisibleButton(id, new Vector2(width, ImGui.GetFrameHeight()));
        if (clicked)
            value = !value;

        var draw = ImGui.GetWindowDrawList();
        var track = value ? Theme.Green : (ImGui.IsItemHovered() ? Theme.Rgba(64, 74, 104) : Theme.Rgba(52, 61, 88));
        draw.AddRectFilled(min, min + new Vector2(width, height), Theme.U32(track), height / 2);
        var knobX = value ? min.X + width - height / 2 : min.X + height / 2;
        draw.AddCircleFilled(new Vector2(knobX, min.Y + height / 2), height / 2 - Theme.S(2.5f), Theme.U32(Theme.Text));
        return clicked;
    }

    /// <summary>Toggle followed by its label on the same line, both clickable.</summary>
    public static bool ToggleRow(string id, string label, ref bool value)
    {
        var changed = Toggle(id, ref value);
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        Text(label);
        if (ImGui.IsItemClicked())
        {
            value = !value;
            changed = true;
        }

        return changed;
    }

    /// <summary>Segmented control: one rounded well, the selected segment raised with gold text.</summary>
    public static int Segmented(string id, IReadOnlyList<string> labels, int selected, float width)
    {
        var height = ImGui.GetFrameHeight();
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(origin, origin + new Vector2(width, height), Theme.U32(Theme.Well), height / 2);

        var segment = width / labels.Count;
        var result = selected;
        for (var i = 0; i < labels.Count; i++)
        {
            ImGui.SetCursorScreenPos(origin + new Vector2(i * segment, 0));
            if (ImGui.InvisibleButton($"{id}{i}", new Vector2(segment, height)))
                result = i;

            var hovered = ImGui.IsItemHovered();
            var min = origin + new Vector2(i * segment + 2, 2);
            var max = origin + new Vector2((i + 1) * segment - 2, height - 2);
            if (i == selected)
                draw.AddRectFilled(min, max, Theme.U32(Theme.CardRaised), (height - 4) / 2);
            else if (hovered)
                draw.AddRectFilled(min, max, Theme.U32(Theme.Hover), (height - 4) / 2);

            var textSize = ImGui.CalcTextSize(labels[i]);
            var color = i == selected ? Theme.Gold : Theme.TextMuted;
            draw.AddText(new Vector2(min.X + (max.X - min.X - textSize.X) / 2, origin.Y + (height - textSize.Y) / 2), Theme.U32(color), labels[i]);
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, height));
        return result;
    }

    /// <summary>Clickable filter chip. Returns true when clicked.</summary>
    public static bool Chip(string id, string label, bool active)
    {
        var padding = Theme.S(10, 4);
        var textSize = ImGui.CalcTextSize(label);
        var size = new Vector2(textSize.X + padding.X * 2, ImGui.GetFrameHeight());
        var origin = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();

        var draw = ImGui.GetWindowDrawList();
        var fill = active ? Theme.Alpha(Theme.Gold, 0.16f) : hovered ? Theme.Rgba(40, 50, 76) : Theme.Field;
        draw.AddRectFilled(origin, origin + size, Theme.U32(fill), size.Y / 2);
        if (active)
            draw.AddRect(origin, origin + size, Theme.U32(Theme.Alpha(Theme.Gold, 0.65f)), size.Y / 2, ImDrawFlags.None, 1f);
        draw.AddText(origin + new Vector2(padding.X, (size.Y - textSize.Y) / 2), Theme.U32(active ? Theme.Gold : Theme.TextMuted), label);
        return clicked;
    }

    /// <summary>Static badge with a tinted background.</summary>
    public static void Pill(string label, Vector4 color)
    {
        var padding = Theme.S(8, 2);
        var textSize = ImGui.CalcTextSize(label);
        var size = textSize + padding * 2;
        var origin = ImGui.GetCursorScreenPos();
        var offsetY = (ImGui.GetFrameHeight() - size.Y) / 2;
        var min = origin + new Vector2(0, Math.Max(0, offsetY));
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(min, min + size, Theme.U32(Theme.Alpha(color, 0.16f)), size.Y / 2);
        draw.AddText(min + padding, Theme.U32(color), label);
        ImGui.Dummy(new Vector2(size.X, Math.Max(size.Y, ImGui.GetFrameHeight())));
    }

    /// <summary>Pill with an icon in front of the label.</summary>
    public void IconPill(FontAwesomeIcon icon, string label, Vector4 color)
    {
        string iconText;
        Vector2 iconSize;
        using (Assets.IconFont())
        {
            iconText = icon.ToIconString();
            iconSize = ImGui.CalcTextSize(iconText);
        }

        var padding = Theme.S(9, 3);
        var gap = Theme.S(6);
        var textSize = ImGui.CalcTextSize(label);
        var size = new Vector2(padding.X * 2 + iconSize.X + gap + textSize.X, Math.Max(textSize.Y, iconSize.Y) + padding.Y * 2);
        var origin = ImGui.GetCursorScreenPos();
        var min = origin + new Vector2(0, Math.Max(0, (ImGui.GetFrameHeight() - size.Y) / 2));
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(min, min + size, Theme.U32(Theme.Alpha(color, 0.14f)), size.Y / 2);
        using (Assets.IconFont())
            draw.AddText(min + new Vector2(padding.X, (size.Y - iconSize.Y) / 2), Theme.U32(color), iconText);
        draw.AddText(min + new Vector2(padding.X + iconSize.X + gap, (size.Y - textSize.Y) / 2), Theme.U32(Theme.Text), label);
        ImGui.Dummy(new Vector2(size.X, Math.Max(size.Y, ImGui.GetFrameHeight())));
    }

    /// <summary>Underlined navigation tab. Returns true when clicked.</summary>
    public bool NavTab(string id, FontAwesomeIcon icon, string label, bool selected)
    {
        string iconText;
        Vector2 iconSize;
        using (Assets.IconFont())
        {
            iconText = icon.ToIconString();
            iconSize = ImGui.CalcTextSize(iconText);
        }

        Vector2 textSize;
        using (Assets.TitleFont())
            textSize = ImGui.CalcTextSize(label);

        var gap = Theme.S(8);
        var height = Math.Max(textSize.Y, iconSize.Y) + Theme.S(12);
        var size = new Vector2(iconSize.X + gap + textSize.X + Theme.S(8), height);
        var origin = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();

        var color = selected ? Theme.Gold : hovered ? Theme.Text : Theme.TextMuted;
        var draw = ImGui.GetWindowDrawList();
        using (Assets.IconFont())
            draw.AddText(origin + new Vector2(Theme.S(4), (height - Theme.S(4) - iconSize.Y) / 2), Theme.U32(color), iconText);
        using (Assets.TitleFont())
            draw.AddText(origin + new Vector2(Theme.S(4) + iconSize.X + gap, (height - Theme.S(4) - textSize.Y) / 2), Theme.U32(color), label);
        if (selected)
            draw.AddRectFilled(origin + new Vector2(0, height - Theme.S(3)), origin + new Vector2(size.X, height), Theme.U32(Theme.Gold), Theme.S(1.5f));
        return clicked;
    }

    // ---------------------------------------------------------------- containers

    /// <summary>
    /// A card that sizes itself to its content (the background is drawn behind it afterwards).
    /// Do not put tables inside: use <see cref="BeginChildCard"/> for scrolling or tabular content.
    /// </summary>
    public static CardScope Card(float? width = null, Vector4? accent = null) => new(width, accent, null);

    /// <summary>Tinted card used for the advice banner.</summary>
    public static CardScope Banner(Vector4 color, float? width = null) => new(width, color, Theme.Alpha(color, 0.10f));

    /// <summary>Fixed-size, scrollable card. Always pair with <see cref="EndChildCard"/>.</summary>
    public static bool BeginChildCard(string id, Vector2 size, Vector4? border = null)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Theme.Card);
        ImGui.PushStyleColor(ImGuiCol.Border, border ?? Theme.CardBorder);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Theme.S(14, 12));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, Theme.S(10));
        var open = ImGui.BeginChild(id, size, true);
        ImGui.PushTextWrapPos(0);
        return open;
    }

    public static void EndChildCard()
    {
        ImGui.PopTextWrapPos();
        ImGui.EndChild();
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);
    }

    public static void Separator(float width)
    {
        var origin = ImGui.GetCursorScreenPos() + new Vector2(0, Theme.S(2));
        ImGui.GetWindowDrawList().AddLine(origin, origin + new Vector2(width, 0), Theme.U32(Theme.CardBorder), 1f);
        ImGui.Dummy(new Vector2(width, Theme.S(4)));
    }

    private static void DashedCircle(ImDrawListPtr draw, Vector2 center, float radius, uint color, float thickness)
    {
        const int segments = 12;
        for (var i = 0; i < segments; i += 2)
        {
            var a0 = MathF.Tau * i / segments;
            var a1 = MathF.Tau * (i + 1) / segments;
            draw.AddLine(
                center + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * radius,
                center + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * radius,
                color,
                thickness);
        }
    }
}

/// <summary>Auto-height card: content goes in a group, the background is added behind it on dispose.</summary>
public sealed class CardScope : IDisposable
{
    private readonly Vector2 start;
    private readonly float width;
    private readonly float padding;
    private readonly Vector4? accent;
    private readonly Vector4 background;
    private readonly ImDrawListPtr draw;

    public CardScope(float? width, Vector4? accent, Vector4? background)
    {
        padding = Theme.S(14);
        this.accent = accent;
        this.background = background ?? Theme.Card;
        start = ImGui.GetCursorScreenPos();
        this.width = width ?? ImGui.GetContentRegionAvail().X;
        draw = ImGui.GetWindowDrawList();
        draw.ChannelsSplit(2);
        draw.ChannelsSetCurrent(1);
        ImGui.SetCursorScreenPos(start + new Vector2(padding, padding));
        ImGui.BeginGroup();
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + InnerWidth);
    }

    /// <summary>Usable width inside the card's padding.</summary>
    public float InnerWidth => width - padding * 2;

    /// <summary>
    /// Continues the current line so that an item of <paramref name="itemWidth"/> ends at the card's right edge.
    /// Inside a group, SameLine offsets are relative to the group start, which is the card's content start.
    /// </summary>
    public void SameLineRight(float itemWidth) => ImGui.SameLine(InnerWidth - itemWidth);

    public void Dispose()
    {
        ImGui.PopTextWrapPos();
        ImGui.EndGroup();
        var bottom = ImGui.GetItemRectMax().Y + padding;
        var max = new Vector2(start.X + width, bottom);
        var rounding = Theme.S(10);

        draw.ChannelsSetCurrent(0);
        draw.AddRectFilled(start, max, Theme.U32(background), rounding);
        draw.AddRect(start, max, Theme.U32(accent is { } a ? Theme.Alpha(a, 0.55f) : Theme.CardBorder), rounding, ImDrawFlags.None, 1f);
        if (accent is { } color)
            draw.AddRectFilled(start + new Vector2(0, rounding), new Vector2(start.X + Theme.S(3), bottom - rounding), Theme.U32(color));
        draw.ChannelsMerge();

        ImGui.SetCursorScreenPos(new Vector2(start.X, bottom));
        ImGui.Dummy(new Vector2(width, 0));
    }
}
