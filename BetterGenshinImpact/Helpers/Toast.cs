using System.Windows;
using BetterGenshinImpact.Service.Interface;
using LibToast = Wpf.Ui.Violeta.Controls.Toast;
using ToastConfig = Wpf.Ui.Violeta.Controls.ToastConfig;
using ToastLocation = Wpf.Ui.Violeta.Controls.ToastLocation;

namespace BetterGenshinImpact.Helpers;

/// <summary>
/// Translating façade over <see cref="Wpf.Ui.Violeta.Controls.Toast"/>.
/// <para>
/// GlobalUsing.cs aliases the bare "Toast" identifier used at every call site to this
/// class instead of the Violeta one, so no call site needs to change. This is the single
/// choke point where every toast message can be translated before it is rendered.
/// </para>
/// <para>
/// Toasts are rendered through a WPF <see cref="System.Windows.Controls.Primitives.Popup"/>
/// that Violeta's Toast creates on the fly. A Popup starts a new element tree that does not
/// inherit attached properties (such as
/// <see cref="BetterGenshinImpact.View.Behavior.AutoTranslateInterceptor.EnableAutoTranslateProperty"/>)
/// from the owning window, so toasts never reach the XAML auto-translate interceptor —
/// unlike <see cref="BetterGenshinImpact.View.Windows.ThemedMessageBox"/>, which sets that
/// attached property directly on its own XAML root and is therefore already covered.
/// </para>
/// </summary>
public static class Toast
{
    public static bool IsStacked
    {
        get => LibToast.IsStacked;
        set => LibToast.IsStacked = value;
    }

    public static void Information(FrameworkElement owner, string message, ToastLocation location = ToastLocation.TopCenter, Thickness offsetMargin = default, int time = 2000) =>
        LibToast.Information(owner, TranslateText(message), location, offsetMargin, time);

    public static void Information(string message, ToastLocation location = ToastLocation.TopCenter, Thickness offsetMargin = default, int time = 2000) =>
        LibToast.Information(TranslateText(message), location, offsetMargin, time);

    public static void Success(FrameworkElement owner, string message, ToastLocation location = ToastLocation.TopCenter, Thickness offsetMargin = default, int time = 2000) =>
        LibToast.Success(owner, TranslateText(message), location, offsetMargin, time);

    public static void Success(string message, ToastLocation location = ToastLocation.TopCenter, Thickness offsetMargin = default, int time = 2000) =>
        LibToast.Success(TranslateText(message), location, offsetMargin, time);

    public static void Error(FrameworkElement owner, string message, ToastLocation location = ToastLocation.TopCenter, Thickness offsetMargin = default, int time = 2000) =>
        LibToast.Error(owner, TranslateText(message), location, offsetMargin, time);

    public static void Error(string message, ToastLocation location = ToastLocation.TopCenter, Thickness offsetMargin = default, int time = 2000) =>
        LibToast.Error(TranslateText(message), location, offsetMargin, time);

    public static void Warning(FrameworkElement owner, string message, ToastLocation location = ToastLocation.TopCenter, Thickness offsetMargin = default, int time = 2000) =>
        LibToast.Warning(owner, TranslateText(message), location, offsetMargin, time);

    public static void Warning(string message, ToastLocation location = ToastLocation.TopCenter, Thickness offsetMargin = default, int time = 2000) =>
        LibToast.Warning(TranslateText(message), location, offsetMargin, time);

    public static void Question(FrameworkElement owner, string message, ToastLocation location = ToastLocation.TopCenter, Thickness offsetMargin = default, int time = 2000) =>
        LibToast.Question(owner, TranslateText(message), location, offsetMargin, time);

    public static void Question(string message, ToastLocation location = ToastLocation.TopCenter, Thickness offsetMargin = default, int time = 2000) =>
        LibToast.Question(TranslateText(message), location, offsetMargin, time);

    public static void Show(FrameworkElement owner, string message, ToastConfig? options = null) =>
        LibToast.Show(owner, TranslateText(message), options);

    /// <summary>
    /// Translates a toast message. Mirrors the fallback behavior used for the fatal-exception
    /// dialog in App.xaml.cs: if the translation service is not resolvable yet (e.g. a toast
    /// fired before the host finished starting) or translation itself throws, the original
    /// text is shown rather than losing the toast. <see cref="ITranslationService.Translate(string, TranslationSourceInfo)"/>
    /// is already a no-op for text without CJK characters and for the zh-Hans UI culture, so
    /// this needs no extra gating of its own.
    /// </summary>
    private static string TranslateText(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return message;
        }

        try
        {
            var translator = App.GetService<ITranslationService>();
            if (translator == null)
            {
                return message;
            }

            return translator.Translate(message, TranslationSourceInfo.From(MissingTextSource.UiStaticLiteral));
        }
        catch
        {
            return message;
        }
    }
}
