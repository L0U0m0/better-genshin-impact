using System.Collections.Generic;
using BetterGenshinImpact.Service.I18n;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;

namespace BetterGenshinImpact.Helpers;

/// <summary>
/// Avvolge un sink (o un Logger Serilog usato come sink) e traduce il template del messaggio e i
/// valori stringa dei parametri tramite il dizionario I18n (it.json) prima di inoltrarlo.
/// Pensato per i sink rivolti all'utente (riquadro log dell'overlay e console): il file di log e
/// l'ExternalLogSink restano grezzi, cioe' in cinese, per il triage dei bug upstream.
///
/// Lavora a livello di LogEvent, quindi funziona anche per chi scrive direttamente su
/// <c>Serilog.Log.Logger</c> (es. QuestVoiceLog) e non dipende dal provider di ILogger.
/// </summary>
public sealed class TranslatingLogEventSink : ILogEventSink
{
    private static readonly MessageTemplateParser Parser = new();

    private readonly ILogEventSink _inner;

    public TranslatingLogEventSink(ILogEventSink inner)
    {
        _inner = inner;
    }

    public void Emit(LogEvent logEvent)
    {
        var i18n = I18nService.Instance;
        var template = logEvent.MessageTemplate.Text;
        var translatedTemplate = i18n.Translate(template);
        var changed = !ReferenceEquals(translatedTemplate, template) && translatedTemplate != template;

        Dictionary<string, LogEventPropertyValue>? props = null;
        foreach (var (name, value) in logEvent.Properties)
        {
            // Molti call site passano un letterale cinese come argomento del template
            // (es. LogInformation("自动烹饪：{Text}", "自动点击确认")): tradurre solo il template
            // lascerebbe l'argomento in cinese nella riga resa. Translate restituisce l'input
            // invariato se non c'e' una voce, quindi i valori in lingua di gioco restano intatti.
            if (value is ScalarValue { Value: string { Length: > 0 } s })
            {
                var t = i18n.Translate(s);
                if (t != s)
                {
                    props ??= new Dictionary<string, LogEventPropertyValue>(logEvent.Properties);
                    props[name] = new ScalarValue(t);
                }
            }
        }

        if (!changed && props is null)
        {
            _inner.Emit(logEvent);
            return;
        }

        var properties = new List<LogEventProperty>();
        foreach (var (name, value) in props ?? new Dictionary<string, LogEventPropertyValue>(logEvent.Properties))
        {
            properties.Add(new LogEventProperty(name, value));
        }

        var newEvent = new LogEvent(
            logEvent.Timestamp,
            logEvent.Level,
            logEvent.Exception,
            changed ? Parser.Parse(translatedTemplate) : logEvent.MessageTemplate,
            properties);
        _inner.Emit(newEvent);
    }
}
