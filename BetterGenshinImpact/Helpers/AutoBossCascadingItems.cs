using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.GameTask.AutoBoss;
using BetterGenshinImpact.Service.Interface;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.Helpers;

/// <summary>
/// 自动首领讨伐 Boss 级联下拉数据源。
/// </summary>
public static class AutoBossCascadingItems
{
    private static IReadOnlyList<ICascadingItem>? _items;

    /// <summary>
    /// 按国家分组的 Boss 级联选项集合。
    /// </summary>
    public static IReadOnlyList<ICascadingItem> Items =>
        _items ??= BuildItems();

    /// <summary>
    /// 将 AutoBoss 支持数据转换为 Violeta 级联下拉选项，并把 Boss 名称保存到子项 Tag。
    /// </summary>
    /// <returns>级联下拉可绑定的选项集合。</returns>
    private static IReadOnlyList<ICascadingItem> BuildItems()
    {
        var translator = App.GetService<ITranslationService>();
        return AutoBossData.CountryToBosses
            .Select(country => (ICascadingItem)new CascadingItem(
                Translate(translator, country.Key),
                country.Value.Select(boss => (ICascadingItem)new CascadingItem(Translate(translator, boss))
                {
                    Tag = boss
                })))
            .ToList();
    }

    /// <summary>
    /// Traduzione display-only: se il servizio non è disponibile o la voce manca a
    /// dizionario, ritorna il testo originale (fallback null-safe).
    /// </summary>
    private static string Translate(ITranslationService? translator, string text)
    {
        return translator?.Translate(text) ?? text;
    }
}
