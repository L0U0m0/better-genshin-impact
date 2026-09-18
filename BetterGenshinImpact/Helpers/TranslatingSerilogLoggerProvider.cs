using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Service.I18n;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace BetterGenshinImpact.Helpers;

/// <summary>
/// Wraps a specific Serilog <see cref="Serilog.ILogger"/> pipeline and translates the log
/// template before it reaches that pipeline's sinks. Intended for the UI-facing sinks
/// (overlay RichTextBox + console) only — the file sink pipeline is wired separately
/// (see App.xaml.cs) so that logs on disk stay untranslated for bug-report triage.
///
/// I log non passano per il markup {i18n:T} delle view: da 0.65 la traduzione arriva da
/// <see cref="I18nService"/> (I18n v2), dopo che upstream ha rimosso con #3566 la pipeline
/// a iniezione (ITranslationService/JsonTranslationService) su cui questo provider poggiava.
/// </summary>
public sealed class TranslatingSerilogLoggerProvider : ILoggerProvider
{
    private readonly Serilog.ILogger _baseLogger;

    public TranslatingSerilogLoggerProvider(Serilog.ILogger baseLogger)
    {
        _baseLogger = baseLogger;
    }

    public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName)
    {
        return new TranslatingSerilogLogger(categoryName, _baseLogger);
    }

    public void Dispose()
    {
    }

    private sealed class TranslatingSerilogLogger : Microsoft.Extensions.Logging.ILogger
    {
        private readonly Serilog.ILogger _logger;

        public TranslatingSerilogLogger(string categoryName, Serilog.ILogger baseLogger)
        {
            _logger = baseLogger.ForContext("SourceContext", categoryName);
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel != LogLevel.None;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var serilogLevel = ConvertLevel(logLevel);
            if (serilogLevel == null)
            {
                return;
            }

            var (template, values) = ExtractTemplateAndValues(state, formatter, exception);
            if (RuntimeHelper.IsDebuggerAttached)
            {
                Write(serilogLevel.Value, exception, template, values);
                return;
            }

            var translatedTemplate = I18nService.Instance.Translate(template);

            // Many call sites pass a Chinese literal as a template argument, e.g.
            // LogInformation("自动烹饪：{Text}", "自动点击确认"). Translating only the template
            // would leave the argument untranslated in the rendered line, so string
            // arguments go through the same dictionary (Translate returns the input
            // unchanged when there is no entry, so values in the game language are untouched).
            for (var i = 0; i < values.Length; i++)
            {
                if (values[i] is string { Length: > 0 } s)
                {
                    values[i] = I18nService.Instance.Translate(s);
                }
            }

            Write(serilogLevel.Value, exception, translatedTemplate, values);
        }

        private void Write(LogEventLevel level, Exception? exception, string template, object?[] values)
        {
            if (values.Length == 0)
            {
                _logger.Write(level, exception, template);
                return;
            }

            _logger.Write(level, exception, template, values);
        }

        private (string Template, object?[] Values) ExtractTemplateAndValues<TState>(
            TState state,
            Func<TState, Exception?, string> formatter,
            Exception? exception)
        {
            if (state is IReadOnlyList<KeyValuePair<string, object?>> kvps)
            {
                var original = kvps.FirstOrDefault(kv => string.Equals(kv.Key, "{OriginalFormat}", StringComparison.Ordinal));
                var template = original.Value as string;
                if (string.IsNullOrEmpty(template))
                {
                    template = formatter(state, exception);
                }

                var values = kvps
                    .Where(kv =>
                        !string.Equals(kv.Key, "{OriginalFormat}", StringComparison.Ordinal) &&
                        !string.Equals(kv.Key, "EventId", StringComparison.Ordinal))
                    .Select(kv => kv.Value)
                    .ToArray();

                return (template ?? string.Empty, values);
            }

            return (formatter(state, exception), Array.Empty<object?>());
        }

        private static LogEventLevel? ConvertLevel(LogLevel level)
        {
            return level switch
            {
                LogLevel.Trace => LogEventLevel.Verbose,
                LogLevel.Debug => LogEventLevel.Debug,
                LogLevel.Information => LogEventLevel.Information,
                LogLevel.Warning => LogEventLevel.Warning,
                LogLevel.Error => LogEventLevel.Error,
                LogLevel.Critical => LogEventLevel.Fatal,
                _ => null
            };
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}
