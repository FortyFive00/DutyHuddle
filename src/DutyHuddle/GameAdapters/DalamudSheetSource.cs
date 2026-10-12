using Dalamud.Plugin.Services;
using Lumina.Excel;

namespace DutyHuddle.GameAdapters;

/// <summary>Sheets in the client's language, through Dalamud.</summary>
public sealed class DalamudSheetSource : ISheetSource
{
    private readonly IDataManager data;

    public DalamudSheetSource(IDataManager data)
    {
        this.data = data;
    }

    public ExcelSheet<T> GetSheet<T>()
        where T : struct, IExcelRow<T>
        => data.GetExcelSheet<T>();
}
