using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace UnturnedMods.Shared.WebPanel
{
    /// <summary>A group of related management actions shown as one section in the panel.</summary>
    public sealed class WebPanelModule
    {
        /// <summary>Shared 1.0 binary-compatible constructor.</summary>
        public WebPanelModule(
            string id,
            string title,
            IReadOnlyList<WebPanelAction> actions,
            string? icon = null)
            : this(id, title, actions, icon, null)
        {
        }

        public WebPanelModule(
            string id,
            string title,
            IReadOnlyList<WebPanelAction> actions,
            string? icon,
            WebUiExtension? ui)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Title = title ?? throw new ArgumentNullException(nameof(title));
            Actions = actions ?? throw new ArgumentNullException(nameof(actions));
            Icon = icon;
            Ui = ui;
        }

        /// <summary>Stable, unique module id (e.g. <c>well404.economy</c>). Used for routing.</summary>
        public string Id { get; }

        /// <summary>Human-readable section title.</summary>
        public string Title { get; }

        /// <summary>Optional icon hint (an emoji or short label) for the nav entry.</summary>
        public string? Icon { get; }

        /// <summary>Optional plugin-owned management surface mounted in an isolated Shadow DOM.</summary>
        public WebUiExtension? Ui { get; }

        public IReadOnlyList<WebPanelAction> Actions { get; }
    }

    /// <summary>How the panel renders and drives an action.</summary>
    public enum WebActionKind
    {
        /// <summary>Read-only data, auto-loaded when the page opens (no button).</summary>
        Table,

        /// <summary>
        /// A discrete command form with its own submit button (e.g. add item, adjust balance).
        /// Fields start blank; submitting invokes the handler once.
        /// </summary>
        Form,

        /// <summary>A single live query box; each query invokes the handler and lists the rows it returns.</summary>
        Search,

        /// <summary>
        /// An editable settings group. Its fields are pre-filled from <see cref="WebPanelAction.Loader"/>
        /// when the page opens, and it has no button of its own — the page's single "save" button
        /// submits every settings group at once (invoking each one's handler with its field values).
        /// </summary>
        Settings,

        /// <summary>
        /// A CRUD list of records (from <see cref="WebPanelAction.RecordsLoader"/>), shown as chips.
        /// "新增" opens a blank editor; clicking a record opens its editor pre-filled with the record's
        /// values. The editor's save invokes <see cref="WebPanelAction.Handler"/> (upsert by
        /// <see cref="WebPanelAction.KeyField"/>); its delete invokes <see cref="WebPanelAction.DeleteHandler"/>.
        /// </summary>
        Collection
    }

    /// <summary>One management action a feature plugin exposes.</summary>
    public sealed class WebPanelAction
    {
        /// <summary>Shared 1.0 binary-compatible constructor.</summary>
        public WebPanelAction(
            string id,
            string label,
            WebActionKind kind,
            Func<WebActionRequest, Task<WebActionResult>> handler,
            IReadOnlyList<WebField>? fields = null,
            string? description = null,
            Func<Task<IReadOnlyDictionary<string, string>>>? loader = null,
            Func<Task<IReadOnlyList<WebRecord>>>? recordsLoader = null,
            Func<WebActionRequest, Task<WebActionResult>>? deleteHandler = null,
            string? keyField = null,
            string? layout = null,
            bool hidden = false,
            IReadOnlyList<string>? summaryFields = null)
            : this(id, label, kind, handler, fields, description, loader, recordsLoader,
                deleteHandler, keyField, layout, hidden, summaryFields, null)
        {
        }

        public WebPanelAction(
            string id,
            string label,
            WebActionKind kind,
            Func<WebActionRequest, Task<WebActionResult>> handler,
            IReadOnlyList<WebField>? fields,
            string? description,
            Func<Task<IReadOnlyDictionary<string, string>>>? loader,
            Func<Task<IReadOnlyList<WebRecord>>>? recordsLoader,
            Func<WebActionRequest, Task<WebActionResult>>? deleteHandler,
            string? keyField,
            string? layout,
            bool hidden,
            IReadOnlyList<string>? summaryFields,
            Func<WebActionRequest, Task<WebActionResult>>? reorderHandler)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Label = label ?? throw new ArgumentNullException(nameof(label));
            Kind = kind;
            Handler = handler ?? throw new ArgumentNullException(nameof(handler));
            Fields = fields ?? Array.Empty<WebField>();
            Description = description;
            Loader = loader;
            RecordsLoader = recordsLoader;
            DeleteHandler = deleteHandler;
            KeyField = keyField;
            Layout = layout;
            Hidden = hidden;
            SummaryFields = summaryFields ?? Array.Empty<string>();
            ReorderHandler = reorderHandler;
        }

        /// <summary>
        /// When true the panel does not render a card for this action, but it stays invokable by id
        /// (e.g. the target of a table's per-row action). Generic helper for "invoke-only" actions.
        /// </summary>
        public bool Hidden { get; }

        /// <summary>
        /// For <see cref="WebActionKind.Collection"/>: field names whose values are shown as
        /// "<label>: <value>" pills on each record (using the localized field labels), so a record's
        /// key data (e.g. a shop item's buy/sell price) is visible without opening the editor.
        /// </summary>
        public IReadOnlyList<string> SummaryFields { get; }

        /// <summary>Stable, unique-within-module action id. Used for routing.</summary>
        public string Id { get; }

        public string Label { get; }

        public WebActionKind Kind { get; }

        /// <summary>Optional helper text shown under the action.</summary>
        public string? Description { get; }

        /// <summary>Input fields for <see cref="WebActionKind.Form"/> / <see cref="WebActionKind.Search"/>.</summary>
        public IReadOnlyList<WebField> Fields { get; }

        /// <summary>
        /// Executes the action. Runs on a web server thread — switch to the main thread
        /// (<c>await UniTask.SwitchToMainThread()</c>) before touching any Unturned API.
        /// </summary>
        public Func<WebActionRequest, Task<WebActionResult>> Handler { get; }

        /// <summary>
        /// Optional: returns the current field values, used to pre-fill the form when the page
        /// opens (chiefly for <see cref="WebActionKind.Settings"/>). Null = no pre-fill.
        /// </summary>
        public Func<Task<IReadOnlyDictionary<string, string>>>? Loader { get; }

        /// <summary>For <see cref="WebActionKind.Collection"/>: returns the records to list as chips.</summary>
        public Func<Task<IReadOnlyList<WebRecord>>>? RecordsLoader { get; }

        /// <summary>For <see cref="WebActionKind.Collection"/>: deletes the record whose key is in the request.</summary>
        public Func<WebActionRequest, Task<WebActionResult>>? DeleteHandler { get; }

        public Func<WebActionRequest, Task<WebActionResult>>? ReorderHandler { get; }

        /// <summary>
        /// For <see cref="WebActionKind.Collection"/>: the field that identifies a record. It is
        /// locked while editing an existing record (changing it would create a duplicate).
        /// </summary>
        public string? KeyField { get; }

        /// <summary>
        /// For <see cref="WebActionKind.Collection"/>: <c>"list"</c> renders records as a vertical
        /// list (per-entity data, e.g. player balances); anything else (default) renders a grid of
        /// small blocks (catalog-like data, e.g. shop items).
        /// </summary>
        public string? Layout { get; }
    }

    public enum WebFieldType
    {
        Text,
        Number,
        Boolean,
        Select,

        /// <summary>A multi-line text box (e.g. for Markdown content).</summary>
        TextArea
    }

    /// <summary>An input field in a <see cref="WebActionKind.Form"/> or <see cref="WebActionKind.Search"/> action.</summary>
    public sealed class WebField
    {
        public WebField(
            string name,
            string label,
            WebFieldType type = WebFieldType.Text,
            bool required = false,
            IReadOnlyList<string>? options = null,
            string? placeholder = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Label = label ?? throw new ArgumentNullException(nameof(label));
            Type = type;
            Required = required;
            Options = options;
            Placeholder = placeholder;
        }

        /// <summary>Form key; the submitted value arrives under this name in <see cref="WebActionRequest"/>.</summary>
        public string Name { get; }

        public string Label { get; }

        public WebFieldType Type { get; }

        public bool Required { get; }

        /// <summary>Allowed values for <see cref="WebFieldType.Select"/>.</summary>
        public IReadOnlyList<string>? Options { get; }

        public string? Placeholder { get; }
    }
}
