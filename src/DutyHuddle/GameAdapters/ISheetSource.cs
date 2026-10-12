using Lumina.Excel;

namespace DutyHuddle.GameAdapters;

/// <summary>
/// Access to Excel sheets. In game it wraps Dalamud's IDataManager; the data-check tool wraps a plain
/// Lumina GameData pointed at the game install, so the catalog can be validated without the game running.
/// </summary>
public interface ISheetSource
{
    ExcelSheet<T> GetSheet<T>()
        where T : struct, IExcelRow<T>;
}
