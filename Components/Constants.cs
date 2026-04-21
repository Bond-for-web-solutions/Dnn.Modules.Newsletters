namespace Dnn.Modules.Newsletters.Components
{
    /// <summary>
    /// Centralised string codes used by the newsletter UI (WebForms + Razor) and the API
    /// controller. Keeping the literals in one place prevents the two surfaces from drifting
    /// (e.g. one accepting "Sync"/"Async" while the other still expects "S"/"A") and makes
    /// the magic values self-documenting at every call site.
    /// </summary>
    internal static class Constants
    {
        /// <summary>Codes posted from the form for the addressing strategy.</summary>
        internal static class SendMethod
        {
            /// <summary>Send one message per recipient via the To: header.</summary>
            public const string To = "TO";

            /// <summary>Send one message with all recipients on Bcc:.</summary>
            public const string Bcc = "BCC";

            /// <summary>Send one message via a relay address; no per-recipient delivery.</summary>
            public const string Relay = "RELAY";
        }

        /// <summary>Codes posted from the form for the dispatch action.</summary>
        internal static class SendAction
        {
            /// <summary>Send synchronously on the request thread.</summary>
            public const string Synchronous = "S";

            /// <summary>Send asynchronously on a background worker thread.</summary>
            public const string Asynchronous = "A";
        }

        /// <summary>Numeric priority codes posted from the form.</summary>
        internal static class Priority
        {
            /// <summary>High priority (X-Priority: 1).</summary>
            public const string High = "1";

            /// <summary>Normal priority (X-Priority: 3).</summary>
            public const string Normal = "2";

            /// <summary>Low priority (X-Priority: 5).</summary>
            public const string Low = "3";
        }

        /// <summary>Editor body-format mode reported by the legacy text editor control.</summary>
        internal static class BodyMode
        {
            /// <summary>Rich (HTML) editor mode.</summary>
            public const string Rich = "RICH";
        }
    }
}
