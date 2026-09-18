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

    // Italian and Turkish were added together in game version 3.3 (2022-12-07), which
    // is why their deviceLanguageType ids land right after Indonesian (13). This slot
    // was previously guessed as 16 based on an earlier "live probe" that turned out to
    // be wrong. Re-verified 2026-08-21 directly against a running Italian client: the
    // game's own registry blob (HKCU\Software\miHoYo\Genshin Impact\GENERAL_DATA_*,
    // decoded JSON field "deviceLanguageType") reports 15, cross-checked against
    // MIHOYOSDK_CURRENT_LANGUAGE_h2559149783 = "it" in the same registry key and the
    // live client UI, which was unambiguously Italian. See GameSettingsChecker.HasOcrDictionary.
    //
    // Value 14 is presumably Turkish (added in the same version, adjacent id), but that
    // is an inference, not verified against a live Turkish client -- left unmapped here
    // until someone can confirm it the same way. 16 is now unknown/unmapped again.
    Italian = 15,
}
