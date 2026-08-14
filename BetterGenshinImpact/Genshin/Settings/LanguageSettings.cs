namespace BetterGenshinImpact.Genshin.Settings;

public class LanguageSettings
{
    public TextLanguage TextLang { get; protected set; }
    public VoiceLanguage VoiceLang { get; protected set; }

    public LanguageSettings(MainJson data)
    {
        Load(data);
    }

    public void Load(MainJson data)
    {
        TextLang = (TextLanguage)data.DeviceLanguageType;
        VoiceLang = (VoiceLanguage)data.DeviceVoiceLanguageType;
    }
}

public enum VoiceLanguage
{
    Chinese,
    English,
    Japanese,
    Korean,
}

public enum TextLanguage
{
    None,
    English,
    SimplifiedChinese,
    TraditionalChinese,
    French,
    German,
    Spanish,
    Portugese,
    Russian,
    Japanese,
    Korean,
    Thai,
    Vietnamese,
    Indonesian,

    // Values 14-15 are unmapped: miHoYo added more text languages after this enum was
    // written and no authoritative list of their deviceLanguageType ids was found. 16
    // is confirmed by a live probe (settings.DeviceLanguageType) with the game's text
    // language set to Italiano, see GameSettingsChecker.HasOcrDictionary.
    Italian = 16,
}
