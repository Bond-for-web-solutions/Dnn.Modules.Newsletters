using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using DotNetNuke.Abstractions.Logging;
using DotNetNuke.Abstractions.Portals;
using DotNetNuke.Services.Exceptions;
using DotNetNuke.Services.Log.EventLog;
using DotNetNuke.Services.Mail;

namespace Dnn.Modules.Newsletters.Components
{
    /// <summary>
    /// Pure, stateless helpers shared by the legacy WebForms control (<c>Newsletter.ascx.cs</c>)
    /// and the MVC API controller (<c>NewsletterApiController</c>) so the two surfaces cannot
    /// drift on URL rewriting, email validation, header-injection rejection, or the bulk-send
    /// background-thread shutdown contract.
    /// </summary>
    internal static class NewsletterMailHelper
    {
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

        /// <summary>
        /// Matches anchor/link/img/script/object tags and captures the href/src/action URL.
        /// Lazy-quantified and whitespace-bounded to avoid catastrophic backtracking.
        /// </summary>
        private static readonly Regex AbsoluteUrlPattern = new Regex(
            "<(a|link|img|script|object)[^>]*?(href|src|action)=(\"|'|)(?<url>[^\"'>\\s]+)(\"|'|)[^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
            RegexTimeout);

        private static readonly Regex EmailRegex = new Regex(
            @"^[A-Za-z0-9_]+([\-+.][A-Za-z0-9_]+)*@[A-Za-z0-9_]+([-.][A-Za-z0-9_]+)*\.[A-Za-z0-9_]+([-.][A-Za-z0-9_]+)*$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled,
            RegexTimeout);

        /// <summary>
        /// Rewrites relative URLs in supported HTML tags to absolute URLs using the supplied
        /// trusted portal alias and application path. The alias must be sourced from
        /// <c>PortalSettings.PortalAlias.HTTPAlias</c>, never from the Host header (which is
        /// attacker-controlled and would enable host-header injection in newsletter links).
        /// </summary>
        /// <param name="content">HTML body to rewrite.</param>
        /// <param name="portalAlias">Trusted HTTP alias (e.g. <c>www.example.com</c>).</param>
        /// <param name="applicationPath">DNN application path (may be empty).</param>
        /// <returns>The rewritten HTML, or the original content on regex timeout / missing alias.</returns>
        public static string ConvertToAbsoluteUrls(string content, string portalAlias, string applicationPath)
        {
            if (string.IsNullOrEmpty(content))
            {
                return content ?? string.Empty;
            }

            if (string.IsNullOrEmpty(portalAlias))
            {
                // No reliable base URL available; return content unchanged rather than emit broken or unsafe links.
                return content;
            }

            var hostBase = DotNetNuke.Common.Globals.AddHTTP(portalAlias);
            var appPath = applicationPath ?? string.Empty;

            try
            {
                return AbsoluteUrlPattern.Replace(content, match =>
                {
                    var originalValue = match.Value;
                    var url = match.Groups["url"].Value;

                    if (string.IsNullOrEmpty(url))
                    {
                        return originalValue;
                    }

                    if (url.StartsWith("/"))
                    {
                        return originalValue.Replace(url, hostBase + url);
                    }

                    return url.Contains("://") || url.Contains("mailto:")
                        ? originalValue
                        : originalValue.Replace(url, hostBase + appPath + "/" + url);
                });
            }
            catch (RegexMatchTimeoutException)
            {
                // Pathological input — return content unchanged rather than fail send.
                return content;
            }
        }

        /// <summary>
        /// Returns <c>true</c> when the value is empty/whitespace or matches a permissive
        /// email shape. Length is bounded to 254 chars (RFC 5321 path limit).
        /// </summary>
        public static bool IsValidEmailOrEmpty(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            if (value.Length > 254)
            {
                return false;
            }

            try
            {
                return EmailRegex.IsMatch(value);
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }

        /// <summary>
        /// Rejects strings containing CR/LF or NUL — protects against email header (SMTP)
        /// injection in Subject/From/ReplyTo/RelayAddress and additional-recipient fields.
        /// </summary>
        public static bool ContainsHeaderInjection(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (c == '\r' || c == '\n' || c == '\0')
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Background-thread entry point for bulk send. Restores the captured culture, sends,
        /// and disposes the email. Catches all exceptions: an unhandled exception on a
        /// background thread crashes the worker process (w3wp) on .NET Framework.
        /// </summary>
        public static void SendAndDispose(SendTokenizedBulkEmail email, CultureInfo culture, CultureInfo uiCulture)
        {
            if (email == null)
            {
                return;
            }

            try
            {
                if (culture != null)
                {
                    Thread.CurrentThread.CurrentCulture = culture;
                }
                if (uiCulture != null)
                {
                    Thread.CurrentThread.CurrentUICulture = uiCulture;
                }

                using (email)
                {
                    email.Send();
                }
            }
            catch (Exception ex)
            {
                try { Exceptions.LogException(ex); } catch { /* logging must not throw out of background thread */ }
                try { email.Dispose(); } catch { /* best-effort */ }
            }
        }

        /// <summary>
        /// Writes a structured compliance audit entry for a successful newsletter dispatch.
        /// Records WHO sent WHAT (subject only, truncated) to HOW MANY recipients via WHICH
        /// transport from WHICH IP. By design this method does NOT receive — and therefore
        /// cannot log — the message body, attachment contents, SMTP credentials, or the
        /// individual recipient email addresses (only counts).
        /// </summary>
        public static void WriteSendAuditLog(
            IEventLogger eventLogger,
            IPortalSettings portalSettings,
            int sendingUserId,
            string subject,
            System.Collections.Generic.IList<string> roleNames,
            int userRecipientCount,
            int additionalEmailCount,
            string sendMethod,
            string sendAction,
            string clientIp)
        {
            if (eventLogger == null)
            {
                return;
            }

            try
            {
                var portalId = portalSettings?.PortalId ?? -1;
                var roleCount = roleNames?.Count ?? 0;
                // Role names are operator-chosen group identifiers, not personal data; safe to log.
                var roleList = roleNames != null ? string.Join(",", roleNames) : string.Empty;

                var properties = new LogProperties();
                properties.Add(new LogDetailInfo { PropertyName = "SendingUserId", PropertyValue = sendingUserId.ToString(CultureInfo.InvariantCulture) });
                properties.Add(new LogDetailInfo { PropertyName = "PortalId", PropertyValue = portalId.ToString(CultureInfo.InvariantCulture) });
                properties.Add(new LogDetailInfo { PropertyName = "Subject", PropertyValue = Truncate(subject, 200) });
                properties.Add(new LogDetailInfo { PropertyName = "RoleCount", PropertyValue = roleCount.ToString(CultureInfo.InvariantCulture) });
                properties.Add(new LogDetailInfo { PropertyName = "RoleNames", PropertyValue = Truncate(roleList, 500) });
                properties.Add(new LogDetailInfo { PropertyName = "UserRecipientCount", PropertyValue = userRecipientCount.ToString(CultureInfo.InvariantCulture) });
                properties.Add(new LogDetailInfo { PropertyName = "AdditionalEmailCount", PropertyValue = additionalEmailCount.ToString(CultureInfo.InvariantCulture) });
                properties.Add(new LogDetailInfo { PropertyName = "SendMethod", PropertyValue = sendMethod ?? string.Empty });
                properties.Add(new LogDetailInfo { PropertyName = "SendAction", PropertyValue = sendAction ?? string.Empty });
                properties.Add(new LogDetailInfo { PropertyName = "ClientIP", PropertyValue = clientIp ?? string.Empty });

                // bypassBuffering: true so the audit row is flushed immediately rather than
                // sitting in the in-memory log buffer where a process recycle could lose it.
                eventLogger.AddLog(properties, portalSettings, sendingUserId, "NEWSLETTER_SENT", true);
            }
            catch (Exception ex)
            {
                // Audit failure must never break the send flow.
                try { Exceptions.LogException(ex); } catch { /* swallow */ }
            }
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }
    }
}
