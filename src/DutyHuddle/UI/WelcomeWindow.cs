using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using DutyHuddle.Configuration;

namespace DutyHuddle.UI;

/// <summary>Shown once after installing: sharing is opt-in, so the plugin asks instead of leaving it to be found.</summary>
public sealed class WelcomeWindow : Window
{
    private readonly Widgets ui;
    private readonly PluginConfig config;
    private readonly Action saveConfig;
    private readonly Action openMain;
    private StyleScope? style;

    public WelcomeWindow(Widgets ui, PluginConfig config, Action saveConfig, Action openMain)
        : base("Welcome to DutyHuddle###DutyHuddleWelcome", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings)
    {
        this.ui = ui;
        this.config = config;
        this.saveConfig = saveConfig;
        this.openMain = openMain;
        IsOpen = !config.SharingPromptAnswered;
    }

    public override void PreDraw()
    {
        Theme.Scale = ui.Assets.Scale;
        style = Theme.PushWindowStyle();
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f));
    }

    public override void PostDraw()
    {
        style?.Dispose();
        style = null;
    }

    public override void Draw()
    {
        var width = Theme.S(440);
        Widgets.Logo(Theme.S(30));
        ImGui.SameLine(0, Theme.S(10));
        ImGui.BeginGroup();
        ui.Title("DutyHuddle", Theme.Gold);
        Widgets.Text("Roulettes and Wondrous Tails for your party.", Theme.TextMuted);
        ImGui.EndGroup();
        ImGui.Spacing();

        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
        Widgets.Text("Share with your party?", Theme.Text);
        Widgets.Text(
            "Party members who also use DutyHuddle then see your daily roulettes and your Wondrous Tails journal, and you see theirs. " +
            "Nothing else is shared. The data is encrypted, so not even the sharing server can read it, and it is deleted two hours after your last update.",
            Theme.TextMuted);
        ImGui.PopTextWrapPos();
        ImGui.Spacing();

        var notNow = ui.ButtonWidth(FontAwesomeIcon.Times, "Not now");
        var turnOn = ui.ButtonWidth(FontAwesomeIcon.UserFriends, "Turn on sharing");
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + width - notNow - turnOn - ImGui.GetStyle().ItemSpacing.X);
        if (ui.Button("welcomeLater", FontAwesomeIcon.Times, "Not now"))
            Answer(false);
        ImGui.SameLine();
        if (ui.Button("welcomeShare", FontAwesomeIcon.UserFriends, "Turn on sharing", primary: true))
            Answer(true);

        Widgets.Text("You can change this any time in the Sharing tab.", Theme.TextFaint);
    }

    /// <summary>Closing the window counts as "not now", so it never comes back.</summary>
    public override void OnClose()
    {
        if (config.SharingPromptAnswered)
            return;
        config.SharingPromptAnswered = true;
        saveConfig();
    }

    private void Answer(bool share)
    {
        config.ShareWithParty = share;
        config.SharingPromptAnswered = true;
        saveConfig();
        IsOpen = false;
        if (share)
            openMain();
    }
}
