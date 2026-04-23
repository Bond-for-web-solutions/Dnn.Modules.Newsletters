using Dnn.Modules.Newsletters.Components;
using Dnn.Modules.Newsletters.Models;
using DotNetNuke.Abstractions.Application;
using DotNetNuke.Abstractions.Logging;
using DotNetNuke.Common;
using DotNetNuke.Common.Utilities;
using DotNetNuke.Entities.Host;
using DotNetNuke.Entities.Users;
using DotNetNuke.Security;
using DotNetNuke.Security.Permissions;
using DotNetNuke.Security.Roles;
using DotNetNuke.Services.Exceptions;
using DotNetNuke.Services.FileSystem;
using DotNetNuke.Services.Localization;
using DotNetNuke.Services.Mail;
using DotNetNuke.Services.Tokens;
using DotNetNuke.Web.Api;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Mime;
using System.Text;
using System.Threading;
using System.Web;
using System.Web.Http;

namespace Dnn.Modules.Newsletters.Controllers
{
    /// <summary>
    /// Web API controller for newsletter POST actions (Preview, Send, Upload).
    /// Called via AJAX from the client to avoid full-page form POST through the DNN page pipeline.
    /// </summary>
    [SupportedModules("Newsletters")]
    [DnnModuleAuthorize(AccessLevel = SecurityAccessLevel.Edit)]
    public class NewsletterApiController : DnnApiController
    {
        private const string LocalResource = "~/DesktopModules/Admin/Newsletters/App_LocalResources/View.resx";
        private const long MaxUploadBytes = 25 * 1024 * 1024; // 25 MB per file

        private readonly IMailSettings _mailSettings;
        private readonly IFileManager _fileManager;
        private readonly IHostSettings _hostSettings;
        private readonly IEventLogger _eventLogger;

        /// <summary>Initializes a new instance of the <see cref="NewsletterApiController"/> class.</summary>
        /// <param name="mailSettings">The mail settings service.</param>
        /// <param name="hostSettings">The host settings service.</param>
        /// <param name="eventLogger">The DNN event logger used to write the compliance audit
        /// trail for newsletter Send actions (operator id, recipient counts, transport, IP).</param>
        public NewsletterApiController(IMailSettings mailSettings, IHostSettings hostSettings, IEventLogger eventLogger)
        {
            _mailSettings = mailSettings;
            _fileManager = FileManager.Instance;
            _hostSettings = hostSettings;
            _eventLogger = eventLogger;
        }

        /// <summary>Generates a preview of the newsletter.</summary>
        /// <param name="model">The newsletter form data.</param>
        /// <returns>An HTTP response containing preview HTML or a status message.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public HttpResponseMessage Preview([FromBody] NewsletterFormDto model)
        {
            try
            {
                if (model == null || string.IsNullOrWhiteSpace(model.Subject) || string.IsNullOrWhiteSpace(model.Message))
                {
                    return CreateStatusResponse("warning", Localization.GetString("MessageValidation", LocalResource));
                }

                if (ContainsHeaderInjection(model.Subject))
                {
                    return CreateStatusResponse("error", Localization.GetString("MessageValidation", LocalResource));
                }

                var previewBody = ConvertToAbsoluteUrls(model.Message);
                string previewSubject;

                if (model.ReplaceTokens)
                {
                    var tokenReplace = new TokenReplace();
                    if (string.Equals(model.SendMethod, Constants.SendMethod.To, StringComparison.OrdinalIgnoreCase))
                    {
                        tokenReplace.User = UserInfo;
                        tokenReplace.AccessingUser = UserInfo;
                        tokenReplace.DebugMessages = true;
                    }

                    previewSubject = tokenReplace.ReplaceEnvironmentTokens(model.Subject);
                    previewBody = tokenReplace.ReplaceEnvironmentTokens(previewBody);
                }
                else
                {
                    previewSubject = model.Subject;
                }

                return Request.CreateResponse(HttpStatusCode.OK, new
                {
                    success = true,
                    previewVisible = true,
                    previewSubject,
                    previewBody,
                });
            }
            catch (Exception ex)
            {
                Exceptions.LogException(ex);
                return CreateStatusResponse("error", Localization.GetString("MessageValidation", LocalResource));
            }
        }

        /// <summary>Sends the newsletter to the specified recipients.</summary>
        /// <param name="model">The newsletter form data.</param>
        /// <returns>An HTTP response containing a status message.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public HttpResponseMessage Send([FromBody] NewsletterFormDto model)
        {
            try
            {
                if (model == null || string.IsNullOrWhiteSpace(model.Subject) || string.IsNullOrWhiteSpace(model.Message))
                {
                    return CreateStatusResponse("warning", Localization.GetString("MessageValidation", LocalResource));
                }

                // NOTE: AdditionalEmails is a multi-line textarea; CR/LF are legitimate separators here
                // and must NOT be treated as header injection at the field level. Per-token CR/LF/NUL
                // validation is performed inside GetRecipients after splitting.
                if (ContainsHeaderInjection(model.Subject) ||
                    ContainsHeaderInjection(model.From) ||
                    ContainsHeaderInjection(model.ReplyTo) ||
                    ContainsHeaderInjection(model.RelayAddress))
                {
                    return CreateStatusResponse("error", Localization.GetString("revEmailAddress.ErrorMessage", LocalResource));
                }

                GetRecipients(model, out var roleNames, out var users);

                if (users.Count == 0 && roleNames.Count == 0)
                {
                    return CreateStatusResponse("warning",
                        string.Format(Localization.GetString("NoRecipients", LocalResource), -1));
                }

                if (!IsValidEmailOrEmpty(model.From) || !IsValidEmailOrEmpty(model.ReplyTo) || !IsValidEmailOrEmpty(model.RelayAddress))
                {
                    return CreateStatusResponse("error", Localization.GetString("revEmailAddress.ErrorMessage", LocalResource));
                }

                return SendEmail(model, roleNames, users);
            }
            catch (Exception ex)
            {
                Exceptions.LogException(ex);
                return CreateStatusResponse("warning",
                    Localization.GetString("NoMessagesSent", LocalResource));
            }
        }

        /// <summary>Uploads one or more files as newsletter attachments.</summary>
        /// <returns>An HTTP response containing the uploaded file details.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public HttpResponseMessage Upload()
        {
            try
            {
                var httpRequest = HttpContext.Current.Request;
                if (httpRequest.Files.Count == 0)
                {
                    return Request.CreateResponse(HttpStatusCode.BadRequest,
                        new { success = false, message = "No file uploaded." });
                }

                int folderId;
                if (!int.TryParse(httpRequest.Form["folderId"], out folderId))
                {
                    return Request.CreateResponse(HttpStatusCode.BadRequest,
                        new { success = false, message = "Invalid folder." });
                }

                var folder = FolderManager.Instance.GetFolder(folderId);
                if (folder == null || folder.PortalID != PortalSettings.PortalId)
                {
                    return Request.CreateResponse(HttpStatusCode.NotFound,
                        new { success = false, message = "Folder not found." });
                }

                // Authorization: user must have WRITE/ADD permission on the target folder.
                if (!FolderPermissionController.Instance.CanAddFolder(folder))
                {
                    return Request.CreateResponse(HttpStatusCode.Forbidden,
                        new { success = false, message = "Not authorized to upload to this folder." });
                }

                // Restrict to host-configured extension whitelist (rejects executables, scripts, etc).
                var whitelist = Host.AllowedExtensionWhitelist;

                var results = new List<object>();
                for (int i = 0; i < httpRequest.Files.Count; i++)
                {
                    var uploaded = httpRequest.Files[i];
                    if (uploaded == null || uploaded.ContentLength == 0)
                    {
                        continue;
                    }

                    if (uploaded.ContentLength > MaxUploadBytes)
                    {
                        return Request.CreateResponse(HttpStatusCode.BadRequest,
                            new { success = false, message = "File exceeds maximum allowed size." });
                    }

                    var fileName = Path.GetFileName(uploaded.FileName);
                    if (string.IsNullOrWhiteSpace(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    {
                        return Request.CreateResponse(HttpStatusCode.BadRequest,
                            new { success = false, message = "Invalid file name." });
                    }

                    var extension = (Path.GetExtension(fileName) ?? string.Empty).TrimStart('.');
                    if (string.IsNullOrEmpty(extension) || !whitelist.IsAllowedExtension(extension))
                    {
                        return Request.CreateResponse(HttpStatusCode.BadRequest,
                            new { success = false, message = "File type not allowed." });
                    }

                    using (var inputStream = uploaded.InputStream)
                    {
                        var file = _fileManager.AddFile(folder, fileName, inputStream, true);
                        results.Add(new { fileId = file.FileId, fileName = file.FileName });
                    }
                }

                return Request.CreateResponse(HttpStatusCode.OK, new { success = true, files = results });
            }
            catch (Exception ex)
            {
                Exceptions.LogException(ex);
                return Request.CreateResponse(HttpStatusCode.InternalServerError,
                    new { success = false, message = "Upload failed." });
            }
        }

        #region Email Logic

        private HttpResponseMessage SendEmail(NewsletterFormDto model, List<string> roleNames, List<UserInfo> users)
        {
            var message = ConvertToAbsoluteUrls(model.Message);
            var email = new SendTokenizedBulkEmail(roleNames, users, true, model.Subject, message);

            try
            {
                email.BodyFormat = model.IsHtmlMessage ? MailFormat.Html : MailFormat.Text;

                switch (model.Priority)
                {
                    case Constants.Priority.High:
                        email.Priority = MailPriority.High;
                        break;
                    case Constants.Priority.Normal:
                        email.Priority = MailPriority.Normal;
                        break;
                    case Constants.Priority.Low:
                        email.Priority = MailPriority.Low;
                        break;
                    default:
                        return CreateStatusResponse("error", Localization.GetString("MessageValidation", LocalResource));
                }

                if (!string.IsNullOrWhiteSpace(model.From) &&
                    !string.Equals(email.SendingUser?.Email, model.From, StringComparison.OrdinalIgnoreCase))
                {
                    var sendingUser = email.SendingUser ?? new UserInfo();
                    sendingUser.Email = model.From;
                    email.SendingUser = sendingUser;
                }

                if (!string.IsNullOrWhiteSpace(model.ReplyTo))
                {
                    email.ReplyTo = new UserInfo { Email = model.ReplyTo };
                }

                if (model.SelectedLanguages != null && model.SelectedLanguages.Count > 0)
                {
                    email.LanguageFilter = model.SelectedLanguages.ToArray();
                }

                if (model.AttachmentFileIds != null)
                {
                    foreach (var fileId in model.AttachmentFileIds)
                    {
                        var attachmentFile = _fileManager.GetFile(fileId);
                        if (attachmentFile == null || attachmentFile.PortalId != PortalSettings.PortalId)
                        {
                            continue;
                        }

                        var attachmentFolder = FolderManager.Instance.GetFolder(attachmentFile.FolderId);
                        if (attachmentFolder == null ||
                            !FolderPermissionController.Instance.CanViewFolder(attachmentFolder))
                        {
                            continue;
                        }

                        try
                        {
                            email.AddAttachment(
                                _fileManager.GetFileContent(attachmentFile),
                                new ContentType { MediaType = attachmentFile.ContentType, Name = attachmentFile.FileName });
                        }
                        catch (Exception attachEx)
                        {
                            // Log and continue: a single unreadable attachment must not abort the whole send.
                            Exceptions.LogException(attachEx);
                        }
                    }
                }

                switch (model.SendMethod)
                {
                    case Constants.SendMethod.To:
                        email.AddressMethod = SendTokenizedBulkEmail.AddressMethods.Send_TO;
                        break;
                    case Constants.SendMethod.Bcc:
                        email.AddressMethod = SendTokenizedBulkEmail.AddressMethods.Send_BCC;
                        break;
                    case Constants.SendMethod.Relay:
                        email.AddressMethod = SendTokenizedBulkEmail.AddressMethods.Send_Relay;
                        if (string.IsNullOrWhiteSpace(model.RelayAddress))
                        {
                            return CreateStatusResponse("warning",
                                string.Format(Localization.GetString("NoMessagesSent", LocalResource), -1));
                        }

                        email.RelayEmailAddress = model.RelayAddress;
                        break;
                    default:
                        return CreateStatusResponse("error", Localization.GetString("MessageValidation", LocalResource));
                }

                email.SuppressTokenReplace = !model.ReplaceTokens;

                if (string.Equals(model.SendAction, Constants.SendAction.Synchronous, StringComparison.OrdinalIgnoreCase))
                {
                    return SendMailSynchronously(email, model, roleNames, users);
                }
                else
                {
                    bool ownershipTransferred;
                    var result = SendMailAsynchronously(model, email, roleNames, users, out ownershipTransferred);
                    if (ownershipTransferred)
                    {
                        email = null; // background thread now owns disposal
                    }
                    return result;
                }
            }
            finally
            {
                if (email != null)
                {
                    email.Dispose();
                }
            }
        }

        private HttpResponseMessage SendMailSynchronously(SendTokenizedBulkEmail email, NewsletterFormDto model, List<string> roleNames, List<UserInfo> users)
        {
            var mailsSent = email.SendMails();
            if (mailsSent > 0)
            {
                WriteSendAuditLog(model, roleNames, users);
                return CreateStatusResponse("success",
                    string.Format(Localization.GetString("MessagesSentCount", LocalResource), mailsSent));
            }
            else
            {
                return CreateStatusResponse("warning",
                    string.Format(Localization.GetString("NoMessagesSent", LocalResource), email.SendingUser?.Email ?? string.Empty));
            }
        }

        private HttpResponseMessage SendMailAsynchronously(NewsletterFormDto model, SendTokenizedBulkEmail email, List<string> roleNames, List<UserInfo> users, out bool ownershipTransferred)
        {
            ownershipTransferred = false;
            var portalId = PortalSettings.PortalId;
            // The async path sends a test mail synchronously; an empty From would make Mail.SendMail fail
            // and the bulk send would never start. Fall back to the current user's email (matches the legacy
            // pre-population of txtFrom in OnLoad) and finally the portal admin/host email.
            var fromAddress = !string.IsNullOrWhiteSpace(model.From)
                ? model.From
                : (UserInfo?.Email ?? PortalSettings?.Email ?? Host.HostEmail ?? string.Empty);
            var startSubject = Localization.GetString("EMAIL_BulkMailStart_Subject.Text", Localization.GlobalResourceFile);
            if (!string.IsNullOrEmpty(startSubject))
            {
                startSubject = string.Format(startSubject, model.Subject);
            }

            var startBody = Localization.GetString("EMAIL_BulkMailStart_Body.Text", Localization.GlobalResourceFile);
            if (!string.IsNullOrEmpty(startBody))
            {
                startBody = string.Format(startBody, model.Subject, UserInfo.DisplayName, email.Recipients().Count);
            }

            var sendMailResult = Mail.SendMail(
                fromAddress,
                fromAddress,
                string.Empty,
                string.Empty,
                MailPriority.Normal,
                startSubject,
                MailFormat.Text,
                Encoding.UTF8,
                startBody,
                string.Empty,
                _mailSettings.GetServer(portalId),
                _mailSettings.GetAuthentication(portalId),
                _mailSettings.GetUsername(portalId),
                _mailSettings.GetPassword(portalId),
                _mailSettings.GetSecureConnectionEnabled(portalId));

            if (string.IsNullOrEmpty(sendMailResult))
            {
                // Capture thread-affine state before transferring ownership to the background thread.
                // HttpContext.Current is null on the worker thread; token replacement and URL building
                // inside SendTokenizedBulkEmail.Send() must not depend on it.
                var culture = Thread.CurrentThread.CurrentCulture;
                var uiCulture = Thread.CurrentThread.CurrentUICulture;
                    var thread = new Thread(() => Components.NewsletterMailHelper.SendAndDispose(email, culture, uiCulture))
                {
                    IsBackground = true,
                    Name = "Newsletters.BulkSend",
                };
                thread.Start();
                ownershipTransferred = true;
                WriteSendAuditLog(model, roleNames, users);
                return CreateStatusResponse("success", Localization.GetString("MessageSent", LocalResource));
            }
            else
            {
                return CreateStatusResponse("warning",
                    string.Format(Localization.GetString("NoMessagesSentPlusError", LocalResource), sendMailResult));
            }
        }

        #endregion

        #region Helpers

        private void GetRecipients(NewsletterFormDto model, out List<string> roleNames, out List<UserInfo> users)
        {
            roleNames = new List<string>();
            users = new List<UserInfo>();
            var portalId = PortalSettings.PortalId;

            if (!string.IsNullOrWhiteSpace(model.AdditionalEmails))
            {
                // Accept any common separator (newline/CR/semicolon/comma/whitespace). The view exposes a
                // multi-line textarea, so newline-separated input is the most likely user pattern.
                var separators = new[] { ';', ',', '\r', '\n', '\t', ' ' };
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var email in model.AdditionalEmails.Split(separators, StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmedEmail = email.Trim();
                    if (string.IsNullOrWhiteSpace(trimmedEmail) || ContainsHeaderInjection(trimmedEmail) || !IsValidEmailOrEmpty(trimmedEmail))
                    {
                        continue;
                    }

                    if (!seen.Add(trimmedEmail))
                    {
                        continue;
                    }

                    users.Add(new UserInfo
                    {
                        UserID = Null.NullInteger,
                        Email = trimmedEmail,
                        DisplayName = trimmedEmail
                    });
                }
            }

            foreach (var value in (model.Recipients ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int entityId;
                if (value.StartsWith("role-", StringComparison.OrdinalIgnoreCase))
                {
                    if (!int.TryParse(value.Substring(5), out entityId))
                    {
                        continue;
                    }

                    var role = RoleController.Instance.GetRoleById(portalId, entityId);
                    if (role != null && !string.IsNullOrWhiteSpace(role.RoleName))
                    {
                        roleNames.Add(role.RoleName);
                    }
                }
                else if (value.StartsWith("user-", StringComparison.OrdinalIgnoreCase))
                {
                    if (!int.TryParse(value.Substring(5), out entityId))
                    {
                        continue;
                    }

                    var user = UserController.GetUserById(_hostSettings, portalId, entityId);
                    if (user != null)
                    {
                        users.Add(user);
                    }
                }
            }
        }

        private void WriteSendAuditLog(NewsletterFormDto model, List<string> roleNames, List<UserInfo> users)
        {
            // Anonymous additional-email entries were merged into `users` with UserID == Null.NullInteger;
            // split them back out so the audit row distinguishes registered users from free-text addresses.
            var additionalEmailCount = users.Count(u => u != null && u.UserID == Null.NullInteger);
            var userRecipientCount = users.Count - additionalEmailCount;
            var clientIp = HttpContext.Current?.Request?.UserHostAddress ?? string.Empty;

            Components.NewsletterMailHelper.WriteSendAuditLog(
                _eventLogger,
                PortalSettings,
                UserInfo?.UserID ?? -1,
                model?.Subject,
                roleNames,
                userRecipientCount,
                additionalEmailCount,
                model?.SendMethod,
                model?.SendAction,
                clientIp);
        }

        private static bool IsValidEmailOrEmpty(string value)
            => Components.NewsletterMailHelper.IsValidEmailOrEmpty(value);

        private static bool ContainsHeaderInjection(string value)
            => Components.NewsletterMailHelper.ContainsHeaderInjection(value);

        private string ConvertToAbsoluteUrls(string content)
            => Components.NewsletterMailHelper.ConvertToAbsoluteUrls(
                content,
                PortalSettings?.PortalAlias?.HTTPAlias,
                Globals.ApplicationPath);

        private HttpResponseMessage CreateStatusResponse(string statusType, string message)
        {
            string cssClass;
            switch (statusType)
            {
                case "success":
                    cssClass = "nl-msg nl-msg-success";
                    break;
                case "error":
                    cssClass = "nl-msg nl-msg-error";
                    break;
                default:
                    cssClass = "nl-msg nl-msg-warning";
                    break;
            }

            return Request.CreateResponse(HttpStatusCode.OK, new
            {
                success = statusType == "success",
                statusMessage = message ?? string.Empty,
                statusCssClass = cssClass,
            });
        }

        #endregion
    }
}
