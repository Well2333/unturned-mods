using System;
using System.Collections.Generic;

namespace UnturnedMods.Shared.WebPanel
{
    /// <summary>The submitted field values for an action invocation.</summary>
    public sealed class WebActionRequest
    {
        public WebActionRequest(IReadOnlyDictionary<string, string> values)
        {
            Values = values ?? throw new ArgumentNullException(nameof(values));
        }

        /// <summary>Raw field name → value map (form keys or the search box's <c>query</c>).</summary>
        public IReadOnlyDictionary<string, string> Values { get; }

        /// <summary>Returns the trimmed value for <paramref name="name"/>, or null if absent/blank.</summary>
        public string? Get(string name)
        {
            if (Values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }

            return null;
        }

        /// <summary>Parses the value for <paramref name="name"/> as a decimal, or null if absent/unparseable.</summary>
        public decimal? GetDecimal(string name)
        {
            var raw = Get(name);
            if (raw != null && decimal.TryParse(
                raw, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }

            return null;
        }
    }

    /// <summary>One record in a <see cref="WebActionKind.Collection"/>: a title, pill badges, and edit values.</summary>
    public sealed class WebRecord
    {
        public WebRecord(
            string key,
            string label,
            IReadOnlyDictionary<string, string> values,
            IReadOnlyList<string>? tags = null)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Label = label ?? throw new ArgumentNullException(nameof(label));
            Values = values ?? throw new ArgumentNullException(nameof(values));
            Tags = tags ?? System.Array.Empty<string>();
        }

        /// <summary>Identifies the record (passed to the delete handler).</summary>
        public string Key { get; }

        /// <summary>The record's title (the slot's heading).</summary>
        public string Label { get; }

        /// <summary>
        /// Badges rendered as separated "pill" elements in the slot — one per sub-element
        /// (e.g. each item in a bundle, or an account's balance). Easier to read than packing
        /// everything into one parenthesised string.
        /// </summary>
        public IReadOnlyList<string> Tags { get; }

        /// <summary>Field values used to pre-fill the editor when the record is clicked.</summary>
        public IReadOnlyDictionary<string, string> Values { get; }
    }

    /// <summary>
    /// The outcome of an action: a success/failure flag, an optional message, and an
    /// optional table. The panel shows the message and renders the table if present.
    /// </summary>
    public sealed class WebActionResult
    {
        public bool Success { get; set; }

        public string? Message { get; set; }

        public IReadOnlyList<string>? Columns { get; set; }

        public IReadOnlyList<IReadOnlyList<string>>? Rows { get; set; }

        public static WebActionResult Ok(string? message = null)
            => new WebActionResult { Success = true, Message = message };

        public static WebActionResult Fail(string message)
            => new WebActionResult { Success = false, Message = message };

        public static WebActionResult Table(
            IReadOnlyList<string> columns,
            IReadOnlyList<IReadOnlyList<string>> rows,
            string? message = null)
            => new WebActionResult
            {
                Success = true,
                Columns = columns,
                Rows = rows,
                Message = message
            };
    }
}
