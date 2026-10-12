using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using DutyHuddle.Configuration;
using DutyHuddle.Domain.Duty;

namespace DutyHuddle.UI;

/// <summary>Preferences that are not part of daily use. Sharing lives in the main window.</summary>
public sealed class SettingsWindow : Window
{
    private readonly Widgets ui;
    private readonly PluginConfig config;
    private readonly Action saveConfig;
    private readonly DutyCatalog catalog;
    private StyleScope? style;
    private string importText = string.Empty;
    private string importMessage = string.Empty;

    public SettingsWindow(Widgets ui, PluginConfig config, Action saveConfig, DutyCatalog catalog)
        : base("DutyHuddle settings###DutyHuddleSettings")
    {
        this.ui = ui;
        this.config = config;
        this.saveConfig = saveConfig;
        this.catalog = catalog;
        Size = new Vector2(600, 640);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void PreDraw()
    {
        Theme.Scale = ui.Assets.Scale;
        style = Theme.PushWindowStyle();
    }

    public override void PostDraw()
    {
        style?.Dispose();
        style = null;
    }

    public override void Draw()
    {
        DrawGeneral();
        ImGui.Spacing();
        DrawRoulettes();
        ImGui.Spacing();
        DrawData();
    }

    private void DrawGeneral()
    {
        using var card = Widgets.Card();
        ui.Title("Recommendations");

        var pvp = config.IncludePvP;
        if (Widgets.ToggleRow("##pvp", "Include Frontline in recommendations", ref pvp))
        {
            config.IncludePvP = pvp;
            saveConfig();
        }

        var notify = config.NotifyCommonDuty;
        if (Widgets.ToggleRow("##notify", "Notify me when everyone sharing needs the same Wondrous Tails duty", ref notify))
        {
            config.NotifyCommonDuty = notify;
            saveConfig();
        }

        ImGui.Spacing();
        Widgets.Text("Members with unknown status count as", Theme.TextMuted);
        var unknown = (float)config.UnknownWeight;
        ImGui.SetNextItemWidth(card.InnerWidth);
        if (ImGui.SliderFloat("##unknownWeight", ref unknown, 0f, 1f, "%.2f of a member"))
        {
            config.UnknownWeight = Math.Round(unknown, 2);
            saveConfig();
        }
    }

    private void DrawRoulettes()
    {
        var height = ImGui.GetFrameHeightWithSpacing() * (catalog.Roulettes.Count + 1) + Theme.S(80);
        if (Widgets.BeginChildCard("##priorities", new Vector2(0, height)))
        {
            ui.Title("Roulette priority and time");
            Widgets.Text("Priority multiplies a roulette's value (0 hides it). Minutes override the estimate (0 = automatic).", Theme.TextMuted);
            ImGui.Spacing();

            const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit;
            if (ImGui.BeginTable("##roulettePriority", 3, flags))
            {
                ImGui.TableSetupColumn("Roulette", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Priority", ImGuiTableColumnFlags.WidthFixed, Theme.S(150));
                ImGui.TableSetupColumn("Minutes", ImGuiTableColumnFlags.WidthFixed, Theme.S(120));
                ImGui.TableHeadersRow();
                foreach (var roulette in catalog.Roulettes)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.AlignTextToFramePadding();
                    Widgets.Text(roulette.ShortName);

                    ImGui.TableNextColumn();
                    var priority = (float)config.GetPriority(roulette.Id);
                    ImGui.SetNextItemWidth(-1);
                    if (ImGui.SliderFloat($"##priority{roulette.Id}", ref priority, 0f, 3f, "%.1f×"))
                    {
                        config.RoulettePriority[roulette.Id] = Math.Round(priority, 1);
                        saveConfig();
                    }

                    ImGui.TableNextColumn();
                    var minutes = config.RouletteMinutesOverride.GetValueOrDefault(roulette.Id);
                    ImGui.SetNextItemWidth(-1);
                    if (ImGui.InputInt($"##minutes{roulette.Id}", ref minutes))
                    {
                        config.RouletteMinutesOverride[roulette.Id] = Math.Clamp(minutes, 0, 300);
                        saveConfig();
                    }
                }

                ImGui.EndTable();
            }
        }

        Widgets.EndChildCard();
    }

    private void DrawData()
    {
        using var card = Widgets.Card();
        ui.Title("Your data");

        if (ui.Button("exportConfig", FontAwesomeIcon.Copy, "Copy settings to clipboard"))
            ImGui.SetClipboardText(ConfigTransfer.Export(config));

        ImGui.Spacing();
        ImGui.SetNextItemWidth(card.InnerWidth - Theme.S(110));
        ImGui.InputTextWithHint("##import", "Paste a DHCFG1 settings string", ref importText, 8192);
        ImGui.SameLine();
        if (ui.Button("importConfig", FontAwesomeIcon.FileImport, "Import", width: Theme.S(102)))
        {
            if (ConfigTransfer.TryImport(importText, out var imported, out var error))
            {
                Apply(imported!);
                saveConfig();
                importMessage = "Settings imported.";
                importText = string.Empty;
            }
            else
            {
                importMessage = error;
            }
        }

        if (importMessage.Length > 0)
            Widgets.Text(importMessage, Theme.TextMuted);

        ImGui.Spacing();
        var samples = config.MeasuredDurations.Values.Sum(s => s.Count);
        Widgets.Text($"Measured clear times: {config.MeasuredDurations.Count} duties, {samples} clears.", Theme.TextMuted);
        if (ui.Button("clearTimes", FontAwesomeIcon.Trash, "Clear measured times"))
        {
            config.MeasuredDurations.Clear();
            saveConfig();
        }
    }

    /// <summary>Imports preferences only; sharing and the server address stay as they are on this computer.</summary>
    private void Apply(PluginConfig imported)
    {
        config.OnlyQueueableRoulettes = imported.OnlyQueueableRoulettes;
        config.HideCompletedByEveryone = imported.HideCompletedByEveryone;
        config.UnknownWeight = imported.UnknownWeight;
        config.IncludePvP = imported.IncludePvP;
        config.RoulettePriority = new Dictionary<uint, double>(imported.RoulettePriority);
        config.RouletteMinutesOverride = new Dictionary<uint, int>(imported.RouletteMinutesOverride);
        config.GroupCategories = imported.GroupCategories;
        config.HideUniqueDuties = imported.HideUniqueDuties;
        config.NotifyCommonDuty = imported.NotifyCommonDuty;
        config.AdvisorGoal = imported.AdvisorGoal;
        config.HideReceivedChatCodes = imported.HideReceivedChatCodes;
    }
}
