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
}
