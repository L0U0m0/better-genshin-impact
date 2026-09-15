using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.ONNX.SVTR;
using BetterGenshinImpact.Core.Script.Dependence.Model.TimerConfig;
using BetterGenshinImpact.GameTask.AutoPick.Assets;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.View.Windows;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Runtime;
// Newtonsoft.Json 与 System.Text.Json 都有 JsonSerializer，只引入需要的 JsonConvert
using JsonConvert = Newtonsoft.Json.JsonConvert;
using Microsoft.Extensions.Localization;

namespace BetterGenshinImpact.GameTask.AutoPick;

public partial class AutoPickTrigger : ITaskTrigger
{
    private readonly ILogger<AutoPickTrigger> _logger = App.GetLogger<AutoPickTrigger>();

    public string Name => "自动拾取";
    public bool IsEnabledByConfig => _config.AutoPickConfig.Enabled;
    public int Priority => 30;
    public bool IsExclusive => false;

    /// <summary>
    /// 本次截图会话绑定的运行环境，素材缩放比例取自它的画面信息
    /// </summary>
    private readonly GameRuntime _runtime;

    /// <summary>
    /// 配置根，由创建方传入
    /// </summary>
    private readonly AllConfig _config;

    private AutoPickAssets _autoPickAssets = null!;

    /// <summary>
    /// 黑名单模式的不拾取列表
    /// </summary>
    private HashSet<string> _blackList = [];

    /// <summary>
    /// 黑名单模式的不拾取列表(模糊匹配)
    /// </summary>
    private List<string> _fuzzyBlackList = [];

    /// <summary>
    /// 黑名单模式的拾取列表
    /// </summary>
    private HashSet<string> _whiteList = [];

    /// <summary>
    /// 白名单模式最终需要拾取的列表
    /// </summary>
    private HashSet<string> _whitelistModeFinalPickList = [];

    /// <summary>
    /// 白名单模式的不拾取列表，OCR 原文命中时不做模糊匹配
    /// </summary>
    private HashSet<string> _whitelistModeDoNotPickList = [];

    /// <summary>
    /// 白名单模式的 OCR 单字模糊匹配器，只包含准确名称在最终拾取列表中的规则
    /// </summary>
    private AutoPickFuzzyMatcher _whitelistFuzzyMatcher = AutoPickFuzzyMatcher.Empty;

    private RecognitionObject _pickRo = null!;

    /// <summary>
    /// 脚本传入的外部配置，只在本次启用期间有效
    /// </summary>
    private AutoPickExternalConfig? _externalConfig;

    /// <summary>
    /// 名单文件版本号。界面修改名单文件后递增，触发器下一次拾取时重读
    /// </summary>
    private static int _listVersion;

    /// <summary>
    /// 当前名单对应的缓存键，键不变就不重读文件
    /// </summary>
    private (AutoPickMode Mode, bool BlacklistModePickEnabled, bool WhitelistModeDoNotPickEnabled, int Version)? _loadedListKey;

    /// <summary>
    /// 游戏语言，用于选择拾取名单/DoNotPick本地化文案
    /// </summary>
    private readonly CultureInfo _gameCulture;

    /// <summary>
    /// DoNotPick() 用到的本地化文案（详见方法内注释）。
    /// 均为在 TextMap 中找到确凿依据的复合词片段；未能确定对应关系的
    /// （长时间 / 眶螂）不做本地化，继续沿用原始中文子串判断。
    /// </summary>
    private readonly string _reputationWithLocalized;
    private readonly string _meetingPointLocalized;
    private readonly string _frostmoonEnclaveLocalized;
    private readonly string _clinkClankLocalized;
    private readonly string _krumkakeCraftshopLocalized;
    private readonly string _favoniusKeepLocalized;
    private readonly string _cliffwatchCampLocalized;
    private readonly string _witchsGardenLocalized;
    private readonly string _lunarArcanumLocalized;

    /// <summary>
    /// 创建自动拾取触发器
    /// </summary>
    /// <param name="runtime">本次截图会话绑定的运行环境</param>
    /// <param name="config">配置根</param>
    public AutoPickTrigger(GameRuntime runtime, AllConfig config)
    {
        _runtime = runtime;
        _config = config;
        _gameCulture = new CultureInfo(config.OtherConfig.GameCultureInfoName);

        IStringLocalizer<AutoPickTrigger> stringLocalizer =
            App.GetService<IStringLocalizer<AutoPickTrigger>>() ?? throw new NullReferenceException();
        // ProcessOcrText() rimuove ogni spazio dal testo OCR ("GiardinodellaStrega"):
        // i frammenti localizzati devono essere confrontati senza spazi, altrimenti
        // nelle lingue con spazi (en/fr/it) DoNotPick() non scatta mai.
        static string NoSpaces(string s) => string.Concat(s.Where(c => !char.IsWhiteSpace(c)));
        _reputationWithLocalized = NoSpaces(stringLocalizer.WithCultureGet(_gameCulture, "」的声望"));
        _meetingPointLocalized = NoSpaces(stringLocalizer.WithCultureGet(_gameCulture, "聚所"));
        _frostmoonEnclaveLocalized = NoSpaces(stringLocalizer.WithCultureGet(_gameCulture, "霜月之坊"));
        _clinkClankLocalized = NoSpaces(stringLocalizer.WithCultureGet(_gameCulture, "叮铃哐啷"));
        _krumkakeCraftshopLocalized = NoSpaces(stringLocalizer.WithCultureGet(_gameCulture, "蛋卷工坊"));
        _favoniusKeepLocalized = NoSpaces(stringLocalizer.WithCultureGet(_gameCulture, "西风戍垒"));
        _cliffwatchCampLocalized = NoSpaces(stringLocalizer.WithCultureGet(_gameCulture, "望崖营壁"));
        _witchsGardenLocalized = NoSpaces(stringLocalizer.WithCultureGet(_gameCulture, "魔女的花园"));
        _lunarArcanumLocalized = NoSpaces(stringLocalizer.WithCultureGet(_gameCulture, "月谕圣牌"));
    }

    /// <summary>
    /// 名单文件被界面修改后调用，下一次拾取时重读
    /// </summary>
    public static void ReloadLists()
    {
        Interlocked.Increment(ref _listVersion);
    }

    public void OnEnabled(object? options)
    {
        _externalConfig = options as AutoPickExternalConfig;
        _lastText = string.Empty;
        _prevClickFrameIndex = -1;
    }

    public void OnDisabled()
    {
        _externalConfig = null;
    }

    /// <summary>
    /// 拾取模式、名单开关或名单文件变化时重读名单，否则直接使用缓存
    /// </summary>
    private void EnsureLists(AutoPickConfig config)
    {
        var key = (config.Mode, config.BlacklistModePickEnabled, config.WhitelistModeDoNotPickEnabled, Volatile.Read(ref _listVersion));
        if (_loadedListKey == key)
        {
            return;
        }

        // 读取失败时也记下这个键，同一份配置只提示一次
        _loadedListKey = key;
        LoadLists(config);
    }

    private void LoadLists(AutoPickConfig config)
    {
        var blackList = new HashSet<string>();
        var fuzzyBlackList = new List<string>();
        var whiteList = new HashSet<string>();
        var whitelistModeFinalPickList = new HashSet<string>();
        var whitelistModeDoNotPickList = new HashSet<string>();
        var whitelistFuzzyMatcher = AutoPickFuzzyMatcher.Empty;

        if (config.Mode == AutoPickMode.Blacklist)
        {
            blackList = ReadJson(ResolveLocalizedPickListPath(@"Assets\Config\Pick\default_pick_black_lists.json"));
            blackList.UnionWith(ReadText(@"User\pick_black_lists.txt"));
            fuzzyBlackList = ReadTextList(@"User\pick_fuzzy_black_lists.txt");

            if (config.BlacklistModePickEnabled)
            {
                whiteList = ReadText(@"User\pick_white_lists.txt");
            }
        }
        else
        {
            whitelistModeFinalPickList = ReadJson(ResolveLocalizedPickListPath(@"Assets\Config\Pick\default_pick_white_lists.json"));
            whitelistModeFinalPickList.UnionWith(ReadText(@"User\pick_whitelist_mode_pick_lists.txt"));
            if (config.WhitelistModeDoNotPickEnabled)
            {
                whitelistModeDoNotPickList = ReadText(@"User\pick_whitelist_mode_do_not_pick_lists.txt");
                whitelistModeFinalPickList.ExceptWith(whitelistModeDoNotPickList);
            }

            // 模糊匹配规则必须在最终拾取列表确定后读取，用它过滤规则
            whitelistFuzzyMatcher = ReadFuzzyRules(@"Assets\Config\Pick\default_pick_fuzzy_white_lists.json", whitelistModeFinalPickList);
        }

        // 使用完整的新集合替换旧集合，防止关闭规则或切换模式后残留旧数据。
        _blackList = blackList;
        _fuzzyBlackList = fuzzyBlackList;
        _whiteList = whiteList;
        _whitelistModeFinalPickList = whitelistModeFinalPickList;
        _whitelistModeDoNotPickList = whitelistModeDoNotPickList;
        _whitelistFuzzyMatcher = whitelistFuzzyMatcher;
    }

    /// <summary>
    /// 读取白名单模式的模糊匹配规则（内部配置，键为匹配模式，值为准确名称），文件不存在时视为没有规则。
    /// 不合法的规则跳过并记录警告；准确名称不在最终拾取列表中的规则直接丢弃，使不拾取规则同样约束模糊匹配。
    /// </summary>
    /// <param name="jsonFilePath">规则文件的相对路径</param>
    /// <param name="finalPickList">白名单模式最终拾取列表</param>
    private AutoPickFuzzyMatcher ReadFuzzyRules(string jsonFilePath, HashSet<string> finalPickList)
    {
        try
        {
            var json = Global.ReadAllTextIfExist(jsonFilePath);
            if (string.IsNullOrEmpty(json))
            {
                return AutoPickFuzzyMatcher.Empty;
            }

            // 新增的内部配置，按项目约定使用 Newtonsoft.Json
            var ruleMap = JsonConvert.DeserializeObject<Dictionary<string, string?>>(json);
            if (ruleMap is null || ruleMap.Count == 0)
            {
                return AutoPickFuzzyMatcher.Empty;
            }

            var rules = new List<AutoPickFuzzyRule>(ruleMap.Count);
            foreach (var (pattern, text) in ruleMap)
            {
                var error = AutoPickFuzzyMatcher.Validate(pattern, text);
                if (error is not null)
                {
                    _logger.LogWarning("自动拾取模糊匹配规则 {Pattern} 无效：{Reason}", pattern, error);
                    continue;
                }

                if (finalPickList.Contains(text!))
                {
                    rules.Add(new AutoPickFuzzyRule(pattern, text!));
                }
            }

            return rules.Count == 0 ? AutoPickFuzzyMatcher.Empty : new AutoPickFuzzyMatcher(rules);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "读取拾取名单配置失败");
            ShowListReadError();
        }

        return AutoPickFuzzyMatcher.Empty;
    }

    /// <summary>
    /// 名单在截图线程上读取，弹窗投递到 UI 线程，不等待用户确认
    /// </summary>
    private static void ShowListReadError()
    {
        UIDispatcherHelper.BeginInvoke(() => ThemedMessageBox.Error("读取拾取名单配置失败，请确认修改后的名单内容格式是否正确！"));
    }

    /// <summary>
    /// 若存在当前游戏语言对应的本地化拾取名单文件（如
    /// default_pick_black_lists.en.json，与原始文件同目录，随本地化 json 一起
    /// 打包进仓库，构建时随 Assets\Config\** 通配符一起复制到输出目录），
    /// 优先使用；否则回退到原始文件（zh-Hans 默认名单，行为不变）。
    /// </summary>
    private string ResolveLocalizedPickListPath(string defaultRelativePath)
    {
        var dir = Path.GetDirectoryName(defaultRelativePath) ?? string.Empty;
        var fileNameNoExt = Path.GetFileNameWithoutExtension(defaultRelativePath);
        var ext = Path.GetExtension(defaultRelativePath);
        var localizedRelativePath = Path.Combine(dir, $"{fileNameNoExt}.{_gameCulture.Name}{ext}");
        return File.Exists(Global.Absolute(localizedRelativePath)) ? localizedRelativePath : defaultRelativePath;
    }

    private HashSet<string> ReadJson(string jsonFilePath)
    {
        try
        {
            var json = Global.ReadAllTextIfExist(jsonFilePath);
            if (!string.IsNullOrEmpty(json))
            {
                return JsonSerializer.Deserialize<HashSet<string>>(json, ConfigService.JsonOptions) ?? [];
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "读取拾取名单配置失败");
            ShowListReadError();
        }

        return [];
    }

    private HashSet<string> ReadText(string textFilePath)
    {
        try
        {
            var txt = Global.ReadAllTextIfExist(textFilePath);
            if (!string.IsNullOrEmpty(txt))
            {
                // 明确指定使用 char[] 重载版本
                return new HashSet<string>(txt.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "读取拾取名单配置失败");
            ShowListReadError();
        }

        return [];
    }

    private List<string> ReadTextList(string textFilePath)
    {
        try
        {
            var txt = Global.ReadAllTextIfExist(textFilePath);
            if (!string.IsNullOrEmpty(txt))
            {
                // 明确指定使用 char[] 重载版本
                return [..txt.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)];
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "读取拾取名单配置失败");
            ShowListReadError();
        }

        return [];
    }


    /// <summary>
    /// 用于日志只输出一次
    /// </summary>
    private string _lastText = string.Empty;

    /// <summary>
    /// 用于日志只输出一次
    /// </summary>
    private int _prevClickFrameIndex = -1;

    //private int _fastModePickCount = 0;

    public void OnCapture(CaptureContent content)
    {
        _autoPickAssets = AutoPickAssets.Get(content.CaptureRectArea, _config.AutoPickConfig.PickKey);
        _pickRo = _autoPickAssets.PickRo;

        var speedTimer = new SpeedTimer();

        using var foundRectArea = content.CaptureRectArea.Find(_pickRo);

        if (foundRectArea.IsEmpty())
        {
            // 没有识别到F键，先判断是否有滚轮图标信息
            if (HasScrollIcon(content.CaptureRectArea))
            {
                // 滚轮下
                InputHub.Foreground.Mouse.VerticalScroll(2);
                Thread.Sleep(50);
            }

            return;
        }

        speedTimer.Record($"识别到拾取键");

        var scale = _runtime.SystemInfo.AssetScale;
        var config = _config.AutoPickConfig;

        if (_externalConfig is { ForceInteraction: true })
        {
            if (_externalConfig.SkipDialog && HasIconNextToPickKey(content, foundRectArea, config, scale, "AutoSkip", "ChatIcon"))
            {
                // 对话气泡：脚本要求强制拾取但跳过 NPC 对话
                return;
            }

            if (_externalConfig.SkipMechanism && HasIconNextToPickKey(content, foundRectArea, config, scale, "AutoPick", "SettingsIcon"))
            {
                // 设置/机关图标：解谜、宝藏屏障、电梯、活动等，强制拾取时跳过
                return;
            }

            LogPick(content, "直接拾取");
            InputHub.Foreground.Keyboard.KeyPress(_autoPickAssets.PickVk);
            return;
        }

        EnsureLists(config);

        // 存在 L 键位是千星奇遇，无需拾取
        using var lKeyRa = content.CaptureRectArea.Find(RecognitionAssets.Get("AutoPick", "L", content.CaptureRectArea));
        if (lKeyRa.IsExist())
        {
            return;
        }

        // 识别到拾取键，开始识别物品图标
        var isExcludeIcon = false;
        var iconRoi = new Rect(
            foundRectArea.X + (int)(config.ItemIconLeftOffset * scale), foundRectArea.Y,
            (int)((config.ItemTextLeftOffset - config.ItemIconLeftOffset) * scale), foundRectArea.Height);
        var chatIconRo = RecognitionAssets.Get("AutoSkip", "ChatIcon", content.CaptureRectArea).Clone();
        chatIconRo.RegionOfInterest = iconRoi;
        using var chatIconRa = content.CaptureRectArea.Find(chatIconRo);
        speedTimer.Record("识别聊天图标");
        if (!chatIconRa.IsEmpty())
        {
            // 物品图标是聊天气泡，一般是NPC对话，文字不在白名单不拾取
            isExcludeIcon = true;
        }
        else
        {
            var settingsIconRo = RecognitionAssets.Get("AutoPick", "SettingsIcon", content.CaptureRectArea).Clone();
            settingsIconRo.RegionOfInterest = iconRoi;
            using var settingsIconRa = content.CaptureRectArea.Find(settingsIconRo);
            speedTimer.Record("识别设置图标");
            if (!settingsIconRa.IsEmpty())
            {
                // 物品图标是设置图标，一般是解谜、活动、电梯等
                isExcludeIcon = true;
            }
        }

        if (config.Mode == AutoPickMode.Whitelist)
        {
            // 白名单模式下，安全图标排除优先于拾取列表。
            if (isExcludeIcon)
            {
                return;
            }
        }
        else if (!config.BlacklistModePickEnabled && isExcludeIcon)
        {
            // 默认不拾取且没有拾取规则直接放弃OCR
            return;
        }

        //if (config.FastModeEnabled && !isExcludeIcon)
        //{
        //    _fastModePickCount++;
        //    if (_fastModePickCount > 2)
        //    {
        //        _fastModePickCount = 0;
        //        LogPick(content, "急速拾取");
        //        InputHub.Foreground.Keyboard.KeyPress(VirtualKeyCode.VK_F);
        //    }
        //    return;
        //}

        // 这类文字识别比较特殊，都是针对某个场景的文字识别，所以暂时未抽象到识别对象中
        // 计算出文字区域
        var textRect = new Rect(foundRectArea.X + (int)(config.ItemTextLeftOffset * scale), foundRectArea.Y,
            (int)((config.ItemTextRightOffset - config.ItemTextLeftOffset) * scale), foundRectArea.Height);
        if (textRect.X + textRect.Width > content.CaptureRectArea.CacheGreyMat.Width
            || textRect.Y + textRect.Height > content.CaptureRectArea.CacheGreyMat.Height)
        {
            Debug.WriteLine("AutoPickTrigger: 文字区域 out of range");
            return;
        }

        using var gradMat = new Mat(content.CaptureRectArea.CacheGreyMat,
            new Rect(textRect.X, textRect.Y, textRect.Width, Math.Min(textRect.Height, 3)));
        using var sobelMat = gradMat.Sobel(MatType.CV_32F, 1, 0);
        var avgGrad = sobelMat.Mean().Val0;
        if (avgGrad < -3)
        {
            Debug.WriteLine($"AutoPickTrigger: 已在拾取中，跳过本次拾取 {avgGrad}");
            return;
        }

        string text;
        if (config.OcrEngine == nameof(PickOcrEngineEnum.Yap))
        {
            using var textMat = new Mat(content.CaptureRectArea.CacheGreyMat, textRect);
            text = TextInferenceFactory.Pick.Value.Inference(textMat);
        }
        else
        {
            using var textMat = new Mat(content.CaptureRectArea.SrcMat, textRect);
            var boundingRect = TextRectExtractor.GetTextBoundingRect(textMat);
            // var boundingRect = new Rect(); // 不使用自己写的文字区域提取
            // 如果找到有效区域
            if (boundingRect.X < 20 && boundingRect.Width > 5 && boundingRect.Height > 5)
            {
                // 截取只包含文字的区域
                using var textOnlyMat = new Mat(textMat, new Rect(0, 0,
                    boundingRect.Right + 5 < textMat.Width ? boundingRect.Right + 5 : textMat.Width, textMat.Height));
                text = OcrFactory.Paddle.OcrWithoutDetector(textOnlyMat);

                // if (RuntimeHelper.IsDebug)
                // {
                //     // 如果不等于正确文字，则保存图片
                //     if (text != "烹饪")
                //     {
                //         var path = Global.Absolute("log/pick");
                //         Directory.CreateDirectory(path);
                //         var str = $"{DateTime.Now:yyyyMMddHHmmssfff}";
                //         // textMat.SaveImage(Path.Combine(path, $"pick_ocr_ori_{str}.png"));
                //         // 画上 boundingRect
                //         Cv2.Rectangle(textMat, boundingRect, new Scalar(0, 0, 255), 1);
                //         textMat.SaveImage(Path.Combine(path, $"pick_ocr_rect_{str}.png"));
                //         bin.SaveImage(Path.Combine(path, $"bin_{str}.png"));
                //     }
                // }
            }
            else
            {
                Debug.WriteLine("-- 无法识别到有效文字区域，尝试直接OCR DET");
                text = OcrFactory.Paddle.Ocr(textMat);
            }
        }

        speedTimer.Record("文字识别");
        if (!string.IsNullOrEmpty(text))
        {
            // 处理OCR识别结果，清理无效字符并确保引号配对
            text = ProcessOcrText(text);

            if (DoNotPick(text))
            {
                return;
            }

            // 单个字符不拾取，盐、糖除外
            if (text.Length <= 1 && text != "盐" && text != "糖")
            {
                return;
            }

            if (config.Mode == AutoPickMode.Whitelist)
            {
                if (_whitelistModeFinalPickList.Contains(text))
                {
                    LogPick(content, text);
                    InputHub.Foreground.Keyboard.KeyPress(_autoPickAssets.PickVk);
                }
                else if (_whitelistFuzzyMatcher.TryMatch(text, out var fuzzyRule)
                         && !_whitelistModeDoNotPickList.Contains(text))
                {
                    // 精确未命中时才尝试模糊匹配；OCR 原文命中不拾取列表时以用户排除为准
                    LogFuzzyPick(content, fuzzyRule, text);
                    InputHub.Foreground.Keyboard.KeyPress(_autoPickAssets.PickVk);
                }

                return;
            }

            if (config.BlacklistModePickEnabled && _whiteList.Contains(text))
            {
                LogPick(content, text);
                InputHub.Foreground.Keyboard.KeyPress(_autoPickAssets.PickVk);
                return;
            }

            speedTimer.Record("白名单判断");

            if (isExcludeIcon)
            {
                //Debug.WriteLine("AutoPickTrigger: 物品图标是聊天气泡，一般是NPC对话，不拾取");
                return;
            }

            if (_blackList.Contains(text))
            {
                return;
            }

            if (_fuzzyBlackList.Count > 0)
            {
                if (_fuzzyBlackList.Any(item => text.Contains(item)))
                {
                    return;
                }
            }

            speedTimer.Record("黑名单判断");

            LogPick(content, text);
            InputHub.Foreground.Keyboard.KeyPress(_autoPickAssets.PickVk);
        }

        speedTimer.DebugPrint();
    }

    /// <summary>
    /// F 键右侧的图标位是否为指定图标（对话气泡 / 设置机关图标）。与主流程中的判断使用同一模板与区域。
    /// </summary>
    private static bool HasIconNextToPickKey(CaptureContent content, Region foundRectArea, AutoPickConfig config, double scale,
        string assetGroup, string assetName)
    {
        var iconRoi = new Rect(
            foundRectArea.X + (int)(config.ItemIconLeftOffset * scale), foundRectArea.Y,
            (int)((config.ItemTextLeftOffset - config.ItemIconLeftOffset) * scale), foundRectArea.Height);
        var iconRo = RecognitionAssets.Get(assetGroup, assetName, content.CaptureRectArea).Clone();
        iconRo.RegionOfInterest = iconRoi;
        using var iconRa = content.CaptureRectArea.Find(iconRo);
        return !iconRa.IsEmpty();
    }

    private bool DoNotPick(string text)
    {
        // zh-Hans（及未知游戏语言）：保留原始的中文子串判断，逻辑与改动前完全一致。
        if (_gameCulture.Name == "zh-Hans")
        {
            return DoNotPickZhHans(text);
        }

        // 以下两项未能在 TextMap 中找到确凿的多语言复合词依据（长时间：太宽泛，
        // 命中304+条无关文本；眶螂：TextMap 中查无此词，可能是已失效/拼写有误的
        // 内部引用），继续沿用原始中文判断——对非中文客户端只是不再拦截这两种
        // （本就未知的）情况，不会产生新的误拾取。
        if (text.Contains("长时间"))
        {
            return true;
        }

        if (text.Contains("眶螂"))
        {
            return true;
        }

        // 纳塔部落声望特殊处理，不拾取（原判断："我在" + 声望/回声/悬木人/流泉）。
        // TextMap 证据：所有阵营声望询问句在 EN/FR/IT 中都遵循同一模式
        // "About my reputation with ..." / "... réputation auprès ..." /
        // "... reputazione presso ..."，而城市声望（"我在蒙德城的声望…"）用的是
        // "reputation in/at"，不含 "with"，因此该片段不会误伤城市声望文案。
        if (text.Contains(_reputationWithLocalized))
        {
            return true;
        }

        // 挪德卡莱聚所，不拾取
        if (text.Contains(_meetingPointLocalized))
        {
            return true;
        }

        // 霜月之坊
        if (text.Contains(_frostmoonEnclaveLocalized))
        {
            return true;
        }

        // 叮铃哐啷（军团/大作战等）/ 叮铃哐啷蛋卷工坊
        if (text.Contains(_clinkClankLocalized) || text.Contains(_krumkakeCraftshopLocalized))
        {
            return true;
        }

        // 西风戍垒（原中文判断写作"西风成垒"，经 TextMap 核实为上游拼写错误，
        // 正确地名是"西风戍垒"——此处按正确地名翻译，中文分支不受影响）
        // / 望崖营壁 / 魔女的花园
        if (text.Contains(_favoniusKeepLocalized) || text.Contains(_cliffwatchCampLocalized) ||
            text.Contains(_witchsGardenLocalized))
        {
            return true;
        }

        if (text.Contains(_lunarArcanumLocalized))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// DoNotPick() 原始实现（zh-Hans客户端 / 游戏语言未知时使用），逐字保留，
    /// 确保本次本地化改动对中文客户端的行为零变化。
    /// </summary>
    private static bool DoNotPickZhHans(string text)
    {
        // 唯一一个动态拾取项，特殊处理，不拾取
        if (text.Contains("长时间"))
        {
            return true;
        }

        // 纳塔部落中文名特殊处理，不拾取
        if (text.Contains("我在") && (text.Contains("声望") || text.Contains("回声") || text.Contains("悬木人") ||
                                    text.Contains("流泉")))
        {
            return true;
        }

        // 挪德卡莱聚所中文名特殊处理，不拾取
        if (text.Contains("聚所"))
        {
            return true;
        }

        if (text.Contains("霜月") && text.Contains("坊"))
        {
            return true;
        }

        if (text.Contains("叮铃") || text.Contains("眶螂") || (text.Contains("蛋卷") && text.Contains("坊")))
        {
            return true;
        }

        if (text.Contains("西风成垒") || text.Contains("望崖营壁") || text.Contains("魔女的花园"))
        {
            return true;
        }

        if (text.Contains("月谕圣牌"))
        {
            return true;
        }

        return false;
    }

    public static Rect GetWhiteTextBoundingRect(Mat textMat)
    {
        // 预处理提取纯白色文字
        var processedMat = new Mat();
        // 提取白色文字 (255,255,255)
        Cv2.InRange(textMat, new Scalar(254, 254, 254), new Scalar(255, 255, 255), processedMat);
        // 形态学操作，先腐蚀后膨胀，去除噪点并保持文字完整
        var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(2, 2));
        Cv2.MorphologyEx(processedMat, processedMat, MorphTypes.Open, kernel, iterations: 1);
        Cv2.Dilate(processedMat, processedMat, kernel, iterations: 1);
        // 寻找非零区域，即文字区域
        Rect boundingRect = Cv2.BoundingRect(processedMat);
        return boundingRect;
    }


    private bool HasScrollIcon(ImageRegion captureRectArea)
    {
        // 固定区域颜色判断
        // (1062,537)  (255,233,44) 黄色
        // (1062,524)  (255,255,255) 白色
        // (1062,583)  (255,255,255) 白色
        var mat = captureRectArea.SrcMat;
        var color1 = mat.At<Vec3b>(537, 1062);
        var color2 = mat.At<Vec3b>(524, 1062);
        var color3 = mat.At<Vec3b>(554, 1062);
        // BGR 的格式
        if (color1.Item2 == 255 && color1.Item1 == 233 && color1.Item0 == 44
            && color2.Item2 == 255 && color2.Item1 == 255 && color2.Item0 == 255
            && color3.Item2 == 255 && color3.Item1 == 255 && color3.Item0 == 255)
        {
            return true;
        }

        return false;
    }


    /// <summary>
    /// 相同文字前后3帧内只输出一次
    /// </summary>
    /// <param name="content"></param>
    /// <param name="text"></param>
    private void LogPick(CaptureContent content, string text)
    {
        if (ShouldLogPick(content, text))
        {
            _logger.LogInformation("交互或拾取：{Text}", text);
        }
    }

    /// <summary>
    /// 模糊匹配拾取的日志，按准确名称去重，去重规则与 <see cref="LogPick"/> 相同。
    /// 准确名称必须是第一个占位符：LogParse 按 交互或拾取："(.+?)" 统计拾取物品。
    /// </summary>
    /// <param name="content">截图内容</param>
    /// <param name="rule">命中的模糊匹配规则</param>
    /// <param name="ocrText">OCR 识别出的文字</param>
    private void LogFuzzyPick(CaptureContent content, AutoPickFuzzyRule rule, string ocrText)
    {
        if (ShouldLogPick(content, rule.Text))
        {
            _logger.LogInformation("交互或拾取：{Text}", rule.Text);
            _logger.LogInformation("模糊匹配成功，OCR 识别为 {OcrText}）",ocrText);
        }
    }

    /// <summary>
    /// 判断本次拾取是否需要输出日志，并记下本次拾取的文字和帧号
    /// </summary>
    /// <param name="content">截图内容</param>
    /// <param name="text">用于去重的文字</param>
    /// <returns>是否需要输出日志</returns>
    private bool ShouldLogPick(CaptureContent content, string text)
    {
        var shouldLog = _lastText != text || (_lastText == text && Math.Abs(content.FrameIndex - _prevClickFrameIndex) >= 5);
        _lastText = text;
        _prevClickFrameIndex = content.FrameIndex;
        return shouldLog;
    }

    /// <summary>
    /// 高性能处理OCR识别的文字结果
    /// 1. 替换【、[ 为「，替换】、] 为」
    /// 2. 清理左边非「字符和中文的字符
    /// 3. 清理右边非」字符和中文的字符  
    /// 4. 确保引号配对：有「必有」，有」必有「
    /// </summary>
    /// <param name="text">OCR识别的原始文字</param>
    /// <returns>处理后的文字</returns>
    private static string ProcessOcrText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // 0. 首先替换相似的括号字符并删除换行符、空格，使用Span<char>进行原地替换以获得最佳性能
        Span<char> chars = stackalloc char[text.Length];
        text.AsSpan().CopyTo(chars);

        int writeIndex = 0;
        bool hasChanges = false;

        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];

            // 跳过换行符、回车符、空格、制表符等空白字符
            if (char.IsWhiteSpace(c))
            {
                hasChanges = true;
                continue;
            }

            // 替换括号字符
            if (c == '【' || c == '[')
            {
                chars[writeIndex++] = '「';
                hasChanges = true;
            }
            else if (c == '】' || c == ']')
            {
                chars[writeIndex++] = '」';
                hasChanges = true;
            }
            else
            {
                chars[writeIndex++] = c;
            }
        }

        // 如果有变化，使用处理后的字符；否则使用原字符串的Span
        ReadOnlySpan<char> span = hasChanges ? chars.Slice(0, writeIndex) : text.AsSpan();
        int start = 0;
        int end = span.Length - 1;

        // 1. 从左边开始，删除非「字符、非中文、非(任意语言)字母的字符
        // 注：原判断只保留中文范围(0x4E00-0x9FFF)，导致非中文游戏客户端(EN/FR/IT等)
        // 的OCR结果——本就不含「」也不含中文字符——被整体清空为空字符串，
        // 拾取判断随即静默跳过（无日志、不拾取）。加入 char.IsLetter 使拉丁/
        // 西里尔等其他文字的字母同样视为有效内容起止边界；中文字符本身也满足
        // IsLetter，故中文客户端行为不变。
        while (start <= end)
        {
            char c = span[start];
            if (c == '「' || (c >= 0x4E00 && c <= 0x9FFF) || char.IsLetter(c)) // 「字符、中文字符或任意语言字母
                break;
            start++;
        }

        // 2. 从右边开始，删除非」字符、非中文、非字母的字符
        while (end >= start)
        {
            char c = span[end];
            if (c == '」' || c == '！' || (c >= 0x4E00 && c <= 0x9FFF) || char.IsLetter(c)) // 」字符、中文字符或任意语言字母
                break;
            end--;
        }

        // 如果所有字符都被删除了
        if (start > end)
            return string.Empty;

        // 获取清理后的文字
        var cleanedSpan = span.Slice(start, end - start + 1);

        // 3. 检查并补充引号配对
        bool hasLeftQuote = false;
        bool hasRightQuote = false;

        // 快速扫描是否存在引号
        for (int i = 0; i < cleanedSpan.Length; i++)
        {
            if (cleanedSpan[i] == '「')
                hasLeftQuote = true;
            else if (cleanedSpan[i] == '」')
                hasRightQuote = true;
        }

        // 根据引号配对规则补充
        if (hasLeftQuote && !hasRightQuote)
        {
            // 有「但没有」，在末尾补充」
            Debug.WriteLine("补充缺失的右引号");
            return string.Concat(cleanedSpan, "」");
        }
        else if (hasRightQuote && !hasLeftQuote)
        {
            // 有」但没有「，在开头补充「
            Debug.WriteLine("补充缺失的左引号");
            return string.Concat("「", cleanedSpan);
        }

        return cleanedSpan.ToString();
    }
}
