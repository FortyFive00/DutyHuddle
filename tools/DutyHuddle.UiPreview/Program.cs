using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using DutyHuddle.Configuration;
using DutyHuddle.Domain.Duty;
using DutyHuddle.Domain.Models;
using DutyHuddle.Domain.Time;
using DutyHuddle.GameAdapters;
using DutyHuddle.Services;
using DutyHuddle.Sync;
using DutyHuddle.UI;
using DutyHuddle.UI.Pages;
using DutyHuddle.UiPreview;
using Lumina;
using Lumina.Data;
using Lumina.Data.Files;
using Lumina.Excel;

// Usage: DutyHuddle.UiPreview [output folder] [game install folder] [height]
var output = Path.GetFullPath(args.Length > 0 ? args[0] : "artifacts/ui-preview");
var install = args.Length > 1 ? args[1] : @"C:\Program Files (x86)\Steam\steamapps\common\FINAL FANTASY XIV Online";
Directory.CreateDirectory(output);

using var game = new GameData(Path.Combine(install, "game", "sqpack"), new LuminaOptions { PanicOnSheetChecksumMismatch = false, DefaultExcelLanguage = Language.English });
var repository = new GameDataRepository(new LuminaSheetSource(game));
var catalog = repository.Load();
var resolver = new WtObjectiveResolver(catalog);

var width = 1000;
var height = args.Length > 2 ? int.Parse(args[2]) : 720;

ImGui.CreateContext();
var io = ImGui.GetIO();
unsafe
{
    io.IniFilename = null;
}

io.DisplaySize = new Vector2(width, height);
io.DeltaTime = 1f / 60f;
io.MousePos = new Vector2(-10000, -10000);

var textures = new Dictionary<ulong, CpuTexture>();
var assets = new PreviewAssets(io, game, textures);
var ui = new Widgets(assets);

// Sample party: you, two friends who share, one who does not run the plugin.
var config = new PluginConfig { ShareWithParty = true };
var store = Scenario.Build(catalog, resolver);
var analysis = new AnalysisService(store, config, catalog, resolver);
var solvers = new SolverCache(null!);
var sharing = new FakeSharing();
var finder = new NoDutyFinder();
var view = new MainView(
    ui,
    config,
    () => { },
    sharing,
    new RoulettePage(ui, config, () => { }, finder, new FakeHistory()),
    new WondrousTailsPage(ui, config, () => { }, catalog, repository.LoadExpansionNames(), finder, solvers),
    new SharingPage(ui, config, () => { }, sharing),
    () => { });

// Let the solver finish before rendering the journal page.
while (solvers.TryGet(config.AdvisorGoal) is null)
    Thread.Sleep(50);

foreach (var (page, name, shareOn) in new[]
         {
             (MainView.RoulettesPage, "roulettes", true),
             (MainView.WondrousTailsPage, "wondrous-tails", true),
             (MainView.SharingPage, "sharing", true),
             (MainView.RoulettesPage, "roulettes-sharing-off", false),
         })
{
    config.SelectedPage = page;
    config.ShareWithParty = shareOn;
    sharing.State = shareOn ? SharingState.Connected : SharingState.Off;
    ImDrawDataPtr drawData = default;
    for (var frame = 0; frame < 4; frame++)
    {
        io.DisplaySize = new Vector2(width, height);
        ImGui.NewFrame();
        ImGui.SetNextWindowPos(new Vector2(16, 16));
        ImGui.SetNextWindowSize(new Vector2(width - 32, height - 32));
        using (Theme.PushWindowStyle())
        {
            ImGui.Begin("DutyHuddle###main", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings);
            view.Draw(analysis.Current);
            ImGui.End();
        }

        ImGui.Render();
        drawData = ImGui.GetDrawData();
    }

    var rasterizer = new Rasterizer(width, height, new Vector4(0.23f, 0.26f, 0.30f, 1f), textures);
    rasterizer.Render(drawData);
    var path = Path.Combine(output, $"{name}.png");
    rasterizer.SavePng(path);
    Console.WriteLine($"Saved {path}");
}

// Party journals with another member's journal open (the popup is drawn as a plain window here).
const System.Reflection.BindingFlags Private = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var journalsPage = new WondrousTailsPage(ui, config, () => { }, catalog, repository.LoadExpansionNames(), finder, solvers);
typeof(WondrousTailsPage).GetField("partySection", Private)!.SetValue(journalsPage, 1);
var friend = analysis.Current.Journals.First(j => !j.IsLocal && j.Board is not null).Member;
typeof(WondrousTailsPage).GetField("viewedMember", Private)!.SetValue(journalsPage, friend);
var journalView = typeof(WondrousTailsPage).GetMethod("DrawJournalView", Private)!;
Render("party-journals", 1000, 720, () =>
{
    Window("DutyHuddle###main", new Vector2(16), new Vector2(968, 688), ImGuiWindowFlags.NoCollapse, () => journalsPage.Draw(analysis.Current));
    Window("##journalView", new Vector2(330, 260), null, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize, () => journalView.Invoke(journalsPage, [analysis.Current]));
});

var welcomeConfig = new PluginConfig();
var welcome = new WelcomeWindow(ui, welcomeConfig, () => { }, () => { });
Render("welcome", 560, 330, () =>
    Window("Welcome to DutyHuddle###welcome", new Vector2(16), null, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize, welcome.Draw));

var overlay = new JournalOverlay(null!, analysis, config, solvers, ui, () => { });
Render("journal-overlay", 400, 200, () =>
{
    ImGui.SetNextWindowSizeConstraints(new Vector2(350, 0), new Vector2(350, float.MaxValue));
    using var overlayStyle = new StyleScope()
        .Var(ImGuiStyleVar.WindowPadding, Theme.S(10, 8))
        .Var(ImGuiStyleVar.ItemSpacing, Theme.S(6, 5))
        .Color(ImGuiCol.Border, Theme.Alpha(Theme.Gold, 0.45f));
    Window("##overlay", new Vector2(16), null, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize, overlay.DrawPanel);
});

return 0;

void Window(string title, Vector2 position, Vector2? size, ImGuiWindowFlags flags, Action draw)
{
    ImGui.SetNextWindowPos(position);
    if (size is { } fixedSize)
        ImGui.SetNextWindowSize(fixedSize);
    ImGui.Begin(title, flags | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings);
    draw();
    ImGui.End();
}

void Render(string name, int sceneWidth, int sceneHeight, Action draw)
{
    ImDrawDataPtr drawData = default;
    for (var frame = 0; frame < 4; frame++)
    {
        io.DisplaySize = new Vector2(sceneWidth, sceneHeight);
        ImGui.NewFrame();
        using (Theme.PushWindowStyle())
            draw();
        ImGui.Render();
        drawData = ImGui.GetDrawData();
    }

    var rasterizer = new Rasterizer(sceneWidth, sceneHeight, new Vector4(0.23f, 0.26f, 0.30f, 1f), textures);
    rasterizer.Render(drawData);
    var path = Path.Combine(output, $"{name}.png");
    rasterizer.SavePng(path);
    Console.WriteLine($"Saved {path}");
}

internal sealed class LuminaSheetSource(GameData game) : ISheetSource
{
    public ExcelSheet<T> GetSheet<T>()
        where T : struct, IExcelRow<T>
        => game.GetExcelSheet<T>(Language.English)!;
}

/// <summary>Fonts from Dalamud's own assets (Noto Sans, Font Awesome) plus Arial Narrow for the game's Trump Gothic.</summary>
internal sealed unsafe class PreviewAssets : IUiAssets
{
    private static readonly ushort[] IconRanges = [0xE000, 0xF8FF, 0];

    private readonly GameData game;
    private readonly Dictionary<ulong, CpuTexture> textures;
    private readonly Dictionary<uint, ImTextureID?> icons = [];
    private readonly ImFontPtr iconFont;
    private readonly ImFontPtr titleFont;
    private readonly ImFontPtr heroFont;
    private readonly GCHandle iconRanges;
    private ulong nextHandle = 1000;

    public PreviewAssets(ImGuiIOPtr io, GameData game, Dictionary<ulong, CpuTexture> textures)
    {
        this.game = game;
        this.textures = textures;
        var assetsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncher", "dalamudAssets", "dev", "UIRes");
        var fonts = io.Fonts;
        fonts.AddFontFromFileTTF(Path.Combine(assetsDir, "NotoSansCJKjp-Medium.otf"), 17f, default, fonts.GetGlyphRangesDefault());
        iconRanges = GCHandle.Alloc(IconRanges, GCHandleType.Pinned);
        iconFont = fonts.AddFontFromFileTTF(Path.Combine(assetsDir, "FontAwesomeFreeSolid.otf"), 15f, default, (ushort*)iconRanges.AddrOfPinnedObject());
        titleFont = fonts.AddFontFromFileTTF(@"C:\Windows\Fonts\ARIALN.TTF", 23f, default, fonts.GetGlyphRangesDefault());
        heroFont = fonts.AddFontFromFileTTF(@"C:\Windows\Fonts\ARIALN.TTF", 34f, default, fonts.GetGlyphRangesDefault());
        fonts.Build();

        for (var i = 0; i < fonts.Textures.Size; i++)
        {
            byte* pixels;
            int w, h, bpp;
            fonts.GetTexDataAsRGBA32(i, &pixels, &w, &h, &bpp);
            var data = new byte[w * h * 4];
            Marshal.Copy((nint)pixels, data, 0, data.Length);
            var handle = (ulong)(i + 1);
            textures[handle] = new CpuTexture(w, h, data);
            fonts.SetTexID(i, new ImTextureID(handle));
        }
    }

    public float Scale => 1f;

    public ImTextureID? GetGameIcon(uint iconId)
    {
        if (icons.TryGetValue(iconId, out var cached))
            return cached;

        ImTextureID? result = null;
        var folder = iconId / 1000 * 1000;
        var tex = game.GetFile<TexFile>($"ui/icon/{folder:D6}/{iconId:D6}_hr1.tex") ?? game.GetFile<TexFile>($"ui/icon/{folder:D6}/{iconId:D6}.tex");
        if (tex is not null)
        {
            var bgra = tex.ImageData;
            var rgba = new byte[bgra.Length];
            for (var i = 0; i < bgra.Length; i += 4)
            {
                rgba[i] = bgra[i + 2];
                rgba[i + 1] = bgra[i + 1];
                rgba[i + 2] = bgra[i];
                rgba[i + 3] = bgra[i + 3];
            }

            var handle = nextHandle++;
            textures[handle] = new CpuTexture(tex.Header.Width, tex.Header.Height, rgba);
            result = new ImTextureID(handle);
        }

        icons[iconId] = result;
        return result;
    }

    public (ImTextureID Texture, int Size)? GetLogo(float pixels)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "DutyHuddle", "images", "logo");
        if (LogoSizes.Closest(LogoSizes.In(folder), pixels) is not { } size)
            return null;

        var key = 0xFFFF0000u | (uint)size;
        if (!icons.TryGetValue(key, out var cached))
        {
            var (w, h, rgba) = Png.ReadRgba(Path.Combine(folder, $"{size}.png"));
            var handle = nextHandle++;
            textures[handle] = new CpuTexture(w, h, rgba);
            icons[key] = cached = new ImTextureID(handle);
        }

        return cached is { } texture ? (texture, size) : null;
    }

    public IDisposable IconFont() => Push(iconFont);

    public IDisposable TitleFont() => Push(titleFont);

    public IDisposable HeroFont() => Push(heroFont);

    private static IDisposable Push(ImFontPtr font)
    {
        ImGui.PushFont(font);
        return new Popper();
    }

    private sealed class Popper : IDisposable
    {
        public void Dispose() => ImGui.PopFont();
    }
}

internal sealed class FakeSharing : ISharing
{
    public SharingState State { get; set; } = SharingState.Connected;

    public string Status => State == SharingState.Off ? "Sharing off" : "Sharing on";

    public bool HasServer => true;

    public string? BuildShareMessage(DateTime nowUtc) => null;

    public void ForgetReceived()
    {
    }
}

internal sealed class NoDutyFinder : IDutyFinder
{
    public void OpenDuty(uint contentFinderConditionId)
    {
    }

    public void OpenRoulette(uint rouletteId)
    {
    }
}

internal sealed class FakeHistory : IRouletteHistory
{
    public IReadOnlyList<(DateOnly Day, IReadOnlySet<uint> Roulettes)> Recent(MemberId member, int count) => [];
}

/// <summary>A four-person party with realistic roulettes and journals drawn from real objectives.</summary>
internal static class Scenario
{
    public static PartyStateStore Build(DutyCatalog catalog, WtObjectiveResolver resolver)
    {
        var now = DateTime.UtcNow;
        var epoch = GameClock.DailyEpoch(now);
        var lyra = new MemberId("Lyra Moonveil", 1);
        var kaia = new MemberId("Kaia Stormborn", 1);
        var rhen = new MemberId("Rhen Ashford", 1);
        var mira = new MemberId("Mira Vael", 1);

        var store = new PartyStateStore();
        store.SetMembers(
        [
            new PartyMemberInfo(lyra, 1, true, 24),
            new PartyMemberInfo(kaia, 2, false, 19),
            new PartyMemberInfo(rhen, 3, false, 25),
            new PartyMemberInfo(mira, 4, false, 38),
        ]);

        // Roulette ids: 1 Leveling, 2 High-level, 3 MSQ, 4 Guildhests, 5 Expert, 6 Trials, 7 Frontline, 8 Level Cap, 9 Mentor, 15 Alliance, 17 Normal raids.
        var lyraRoulettes = States("1A 2A 3C 4C 5C 6A 7A 8A 9L 15A 17A");
        var kaiaRoulettes = States("1C 2A 3C 4A 5A 6A 7A 8C 9L 15A 17C");
        var rhenRoulettes = States("1A 2C 3A 4A 5A 6A 7A 8C 9A 15C 17A");

        // The local journal reproduces the design mockup: seven spread seals, four points.
        const ushort spread = (1 << 0) | (1 << 2) | (1 << 5) | (1 << 7) | (1 << 8) | (1 << 10) | (1 << 13);
        store.SetLocal(new MemberSnapshot(lyra, now, epoch, lyraRoulettes,
            Board([109, 190, 173, 165, 157, 54, 1, 3, 4, 59, 60, 85, 86, 108, 121, 46], open: 9, spread, points: 4, now), SnapshotSource.Local));
        store.UpdateRemote(new MemberSnapshot(kaia, now.AddMinutes(-2), epoch, kaiaRoulettes,
            Board([109, 173, 1, 158, 52, 165, 59, 60, 85, 86, 108, 121, 122, 123, 124, 125], open: 9, 0b0000_0000_0111_1111, points: 1, now), SnapshotSource.Relay));
        store.UpdateRemote(new MemberSnapshot(rhen, now.AddMinutes(-11), epoch, rhenRoulettes,
            Board([165, 173, 157, 46, 53, 109, 2, 3, 4, 59, 60, 85, 86, 108, 121, 122], open: 13, 0b0000_0000_0000_0111, points: 6, now), SnapshotSource.ChatCode));
        return store;
    }

    private static Dictionary<uint, Availability> States(string spec)
        => spec.Split(' ').ToDictionary(
            s => uint.Parse(s[..^1]),
            s => s[^1] switch { 'A' => Availability.Available, 'C' => Availability.Completed, 'L' => Availability.Locked, _ => Availability.Unknown });

    private static WtBoard Board(byte[] orders, int open, ushort stickers, byte points, DateTime now)
    {
        var status = new WtTaskStatus[16];
        for (var i = 0; i < 16; i++)
            status[i] = i < open ? WtTaskStatus.Open : WtTaskStatus.Claimed;
        return new WtBoard
        {
            OrderIds = orders,
            Status = status,
            StickerMask = stickers,
            SecondChancePoints = points,
            ExpiresUtc = now.AddDays(5).AddHours(7),
        };
    }
}

/// <summary>Just enough PNG decoding for the logo: 8-bit RGBA, not interlaced.</summary>
internal static class Png
{
    public static (int Width, int Height, byte[] Rgba) ReadRgba(string path)
    {
        var bytes = File.ReadAllBytes(path);
        int width = 0, height = 0;
        using var idat = new MemoryStream();
        for (var offset = 8; offset < bytes.Length;)
        {
            var length = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset));
            var type = System.Text.Encoding.ASCII.GetString(bytes, offset + 4, 4);
            var data = bytes.AsSpan(offset + 8, length);
            if (type == "IHDR")
            {
                width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(data);
                height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                if (data[8] != 8 || data[9] != 6 || data[12] != 0)
                    throw new NotSupportedException("Only 8-bit RGBA, non-interlaced PNG files are supported.");
            }
            else if (type == "IDAT")
            {
                idat.Write(data);
            }

            offset += length + 12;
        }

        idat.Position = 0;
        using var inflated = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(idat, System.IO.Compression.CompressionMode.Decompress))
            zlib.CopyTo(inflated);
        var raw = inflated.ToArray();

        var stride = width * 4;
        var rgba = new byte[height * stride];
        for (var y = 0; y < height; y++)
        {
            var filter = raw[y * (stride + 1)];
            for (var x = 0; x < stride; x++)
            {
                int a = x >= 4 ? rgba[y * stride + x - 4] : 0;
                int b = y > 0 ? rgba[(y - 1) * stride + x] : 0;
                int c = x >= 4 && y > 0 ? rgba[(y - 1) * stride + x - 4] : 0;
                int value = raw[y * (stride + 1) + 1 + x];
                value += filter switch
                {
                    1 => a,
                    2 => b,
                    3 => (a + b) / 2,
                    4 => Paeth(a, b, c),
                    _ => 0,
                };
                rgba[y * stride + x] = (byte)value;
            }
        }

        return (width, height, rgba);
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
