using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using DutyHuddle.Services;
using DutyHuddle.UI.Pages;

namespace DutyHuddle.UI;

public sealed class MainWindow : Window
{
    private readonly AnalysisService analysis;
    private readonly MainView view;
    private readonly IUiAssets assets;
    private StyleScope? style;

    public MainWindow(AnalysisService analysis, MainView view, IUiAssets assets)
        : base("DutyHuddle###DutyHuddleMain")
    {
        this.analysis = analysis;
        this.view = view;
        this.assets = assets;

        Size = new Vector2(960, 680);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(820, 560),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void PreDraw()
    {
        Theme.Scale = assets.Scale;
        style = Theme.PushWindowStyle();
    }

    public override void PostDraw()
    {
        style?.Dispose();
        style = null;
    }

    public override void Draw() => view.Draw(analysis.Current);
}
