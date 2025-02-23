﻿using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Json;
using Serilog.Parsing;

namespace Serilog.Sinks.Splunk.Durable
{
    /// <summary>
    /// Compact Splunk Json Formatter
    /// </summary>
    public class CompactSplunkJsonFormatter : ITextFormatter
    {
        private static readonly JsonValueFormatter ValueFormatter = new JsonValueFormatter(typeTagName: "$type");
        private readonly string _suffix;
        private readonly bool _renderTemplate;

        /// <summary>
        /// Construct a <see cref="CompactSplunkJsonFormatter"/>.
        /// </summary>
        /// <param name="source">The source of the event</param>
        /// <param name="sourceType">The source type of the event</param>
        /// <param name="host">The host of the event</param>
        /// <param name="index">The Splunk index to log to</param>
        /// <param name="renderTemplate">If true, the template used will be rendered and written to the output as a property named MessageTemplate</param>
        public CompactSplunkJsonFormatter(bool renderTemplate = false, string source = null, string sourceType = null, string host = null, string index = null)
        {
            _renderTemplate = renderTemplate;
            var suffixWriter = new StringWriter();
            suffixWriter.Write("}"); // Terminates "event"

            if (!string.IsNullOrWhiteSpace(source))
            {
                suffixWriter.Write(",\"source\":");
                JsonValueFormatter.WriteQuotedJsonString(source, suffixWriter);
            }

            if (!string.IsNullOrWhiteSpace(sourceType))
            {
                suffixWriter.Write(",\"sourcetype\":");
                JsonValueFormatter.WriteQuotedJsonString(sourceType, suffixWriter);
            }

            if (!string.IsNullOrWhiteSpace(host))
            {
                suffixWriter.Write(",\"host\":");
                JsonValueFormatter.WriteQuotedJsonString(host, suffixWriter);
            }

            if (!string.IsNullOrWhiteSpace(index))
            {
                suffixWriter.Write(",\"index\":");
                JsonValueFormatter.WriteQuotedJsonString(index, suffixWriter);
            }
            suffixWriter.Write('}'); // Terminates the payload
            _suffix = suffixWriter.ToString();
        }

        /// <inheritdoc/>
        public void Format(LogEvent logEvent, TextWriter output)
        {
            if (logEvent == null) throw new ArgumentNullException(nameof(logEvent));
            if (output == null) throw new ArgumentNullException(nameof(output));

            output.Write("{\"time\":\"");
            output.Write(logEvent.Timestamp.ToEpoch().ToString(CultureInfo.InvariantCulture));
            output.Write("\",\"event\":{\"@level\":\"");
            output.Write(logEvent.Level);
            output.Write('"');
            if (_renderTemplate)
            {
                output.Write(",\"@messageTemplate\":");
                JsonValueFormatter.WriteQuotedJsonString(logEvent.MessageTemplate.Text, output);

                var tokensWithFormat = logEvent.MessageTemplate.Tokens
                    .OfType<PropertyToken>()
                    .Where(pt => pt.Format != null);

                // Better not to allocate an array in the 99.9% of cases where this is false
                // ReSharper disable once PossibleMultipleEnumeration
                if (tokensWithFormat.Any())
                {
                    output.Write(",\"@messageRender\":[");
                    var delim = "";
                    foreach (var r in tokensWithFormat)
                    {
                        output.Write(delim);
                        delim = ",";
                        var space = new StringWriter();
                        r.Render(logEvent.Properties, space);
                        JsonValueFormatter.WriteQuotedJsonString(space.ToString(), output);
                    }
                    output.Write(']');
                }
            }
            if (logEvent.Exception != null)
            {
                output.Write(",\"@exception\":");
                JsonValueFormatter.WriteQuotedJsonString(logEvent.Exception.ToString(), output);
            }

            foreach (var property in logEvent.Properties)
            {
                var name = property.Key;
                if (name.Length > 0 && name[0] == '@')
                {
                    // Escape first '@' by doubling
                    name = '@' + name;
                }

                output.Write(',');
                JsonValueFormatter.WriteQuotedJsonString(name, output);
                output.Write(':');
                ValueFormatter.Format(property.Value, output);
            }
            output.WriteLine(_suffix);
        }
    }
}
