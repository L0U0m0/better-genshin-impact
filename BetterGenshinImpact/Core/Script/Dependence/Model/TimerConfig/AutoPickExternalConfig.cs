namespace BetterGenshinImpact.Core.Script.Dependence.Model.TimerConfig;

public class AutoPickExternalConfig
{

    // 需要F的文本（对话、拾取）
    public string[] TextList { get; set; } = [];

    // 无视文本和图标遇到F就点击
    public bool ForceInteraction { get; set; } = false;

    // 仅在 ForceInteraction 下生效：F 键旁边是对话气泡图标（NPC对话）时不按 F。
    // 默认 false，保持上游"无视图标"的语义；拾取类脚本可显式开启，
    // 避免路线经过 NPC 时误触发对话。
    public bool SkipDialog { get; set; } = false;

    // 仅在 ForceInteraction 下生效：F 键旁边是设置/机关图标（解谜、机关、宝藏屏障、
    // 电梯、活动等）时不按 F。默认 false。拾取类脚本可开启：例如纳塔宝藏屏障的
    // "使用黑曜石戒指" 确认弹窗会挡住所有按键，而 ESC 关闭后强制拾取又会立刻重新按 F。
    public bool SkipMechanism { get; set; } = false;
}
