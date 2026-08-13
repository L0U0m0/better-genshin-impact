using System;
using System.Globalization;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.Genshin.Settings;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service.Interface;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Genshin.Settings2;

public class GameSettingsChecker
{
    /// <summary>
    /// OCR key known to exist in every localized resx dictionary of AutoDomainTask (en/fr/zh-Hans/zh-Hant,
    /// plus it once the Italian fork branch is merged). Used purely as a probe: if the resource set for a
    /// given culture doesn't contain it, that culture has no OCR dictionary at all.
    /// </summary>
    private const string OcrDictionaryProbeKey = "挑战达成";

    /// <summary>
    /// Maps the game's registry-reported text language to the culture name used by the resx dictionaries'
    /// file-name suffix (AutoDomainTask.&lt;culture&gt;.resx and friends). Only languages that currently ship
    /// an OCR dictionary are listed; anything else falls through to "no dictionary".
    /// </summary>
    private static string? ToOcrCultureName(TextLanguage lang) => lang switch
    {
        TextLanguage.SimplifiedChinese => "zh-Hans",
        TextLanguage.TraditionalChinese => "zh-Hant",
        TextLanguage.English => "en",
        TextLanguage.French => "fr",
        _ => null
    };

    /// <summary>
    /// The Serilog pipeline doesn't route ILogger calls through ITranslationService (see App.xaml.cs),
    /// so log messages that must be readable in the non-Chinese UI are translated explicitly at the call
    /// site, on the raw template (placeholders preserved), before being handed to the logger.
    /// </summary>
    private static string Tr(string template)
    {
        return App.GetService<ITranslationService>()?.Translate(template, TranslationSourceInfo.From(MissingTextSource.Log)) ?? template;
    }

    /// <summary>
    /// Checks at runtime whether the OCR resx satellite for <paramref name="lang"/> actually exists, instead
    /// of hard-coding "must be Simplified Chinese". This way the warning automatically stops firing for any
    /// language that gains a dictionary later (e.g. it does today on the build-it/#3399 fork), with no code
    /// change needed here. Any failure (missing mapping, DI not ready, resource lookup error) is treated as
    /// "no dictionary" so the check stays at least as conservative as the previous hard-coded comparison.
    /// </summary>
    private static bool HasOcrDictionary(TextLanguage lang)
    {
        try
        {
            var cultureName = ToOcrCultureName(lang);
            if (cultureName == null)
            {
                return false;
            }

            var stringLocalizer = App.GetService<IStringLocalizer<AutoDomainTask>>();
            if (stringLocalizer == null)
            {
                return false;
            }

            using var _ = CultureHelper.Use(new CultureInfo(cultureName));
            LocalizedString probe = stringLocalizer[OcrDictionaryProbeKey];
            return !probe.ResourceNotFound;
        }
        catch
        {
            return false;
        }
    }

    public static void LoadGameSettingsAndCheck()
    {
        try
        {
            var settingStr = GenshinGameSettings.GetStrFromRegistry();
            if (settingStr == null)
            {
                TaskControl.Logger.LogDebug("获取原神游戏设置失败");
                return;
            }

            GenshinGameSettings? settings = GenshinGameSettings.Parse(settingStr);
            if (settings == null)
            {
                TaskControl.Logger.LogDebug("获取原神游戏设置失败");
                return;
            }

            GenshinGameInputSettings? inputSettings = GenshinGameInputSettings.Parse(settings.InputData);
            if (inputSettings == null)
            {
                TaskControl.Logger.LogError("获取原神游戏输入设置失败");
                return;
            }
            
            if (settings.GammaValue != "2.200000047683716")
            {
                TaskControl.Logger.LogError("检测到游戏亮度非默认值，将会影响功能正常使用，请在原神 游戏设置——图像——亮度 中恢复默认亮度！");
            }

            if (settings.MiniMapConfig != 1)
            {
                TaskControl.Logger.LogWarning("检测到游戏小地图锁定配置不是【锁定方向】，无法正常使用地图追踪功能。请在原神 游戏设置——其他——小地图锁定 中调整为【锁定方向】！");
            }

            if (inputSettings.MouseSenseIndex != 2
                || inputSettings.MouseSenseIndexY != 2
                || inputSettings.MouseFocusSenseIndex != 2
                || inputSettings.MouseFocusSenseIndexY != 2)
            {
                TaskControl.Logger.LogInformation(Tr("当前：镜头水平灵敏度{X1}，镜头垂直灵敏度{Y1}，镜头水平灵敏度（瞄准模式）{X2}，镜头垂直灵敏度（瞄准模式）{Y2}"),
                    inputSettings.MouseSenseIndex + 1, inputSettings.MouseSenseIndexY + 1,
                    inputSettings.MouseFocusSenseIndex + 1, inputSettings.MouseFocusSenseIndexY + 1);
                TaskControl.Logger.LogError("检测到镜头灵敏度不是默认值3，将会影响所有视角移动功能的正常使用，请在原神 游戏设置——控制 中恢复默认灵敏度！");
            }

            var lang = (TextLanguage)settings.DeviceLanguageType;
            if (!HasOcrDictionary(lang))
            {
                TaskControl.Logger.LogWarning(Tr("当前游戏语言{Lang}不是简体中文，部分功能可能无法正常使用。The game language is not Simplified Chinese, some functions may not work properly"), lang);
            }
        }
        catch (Exception e)
        {
            TaskControl.Logger.LogDebug(e, "获取原神游戏设置失败");
        }
    }
}
