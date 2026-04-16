using Dnn.Modules.Newsletters.Models;
using DotNetNuke.Abstractions.Application;
using DotNetNuke.Common;
using DotNetNuke.Common.Utilities;
using DotNetNuke.Entities.Users;
using DotNetNuke.Security;
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
using System.Text.RegularExpressions;
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
    [DnnModuleAuthorize(AccessLevel = SecurityAccessLevel.View)]
    public class NewsletterApiController : DnnApiController
    {
        private const string LocalResource = "~/DesktopModules/Admin/Newsletters/App_LocalResources/View.resx";

        private readonly IMailSettings _mailSettings;
        private readonly IFileManager _fileManager;
        private readonly IHostSettings _hostSettings;

        /// <summary>Initializes a new instance of the <see cref="NewsletterApiController"/> class.</summary>
        /// <param name="mailSettings">The mail settings service.</param>
        /// <param name="hostSettings">The host settings service.</param>
        public NewsletterApiController(IMailSettings mailSettings, IHostSettings hostSettings)
        {
            _mailSettings = mailSettings;
            _fileManager = FileManager.Instance;
            _hostSettings = hostSettings;
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
                if (string.IsNullOrWhiteSpace(model.Subject) || string.IsNullOrWhiteSpace(model.Message))
                {
                    return CreateStatusResponse("warning", Localization.GetString("MessageValidation", LocalResource));
                }

                var previewBody = ConvertToAbsoluteUrls(model.Message);
                string previewSubject;

                if (model.ReplaceTokens)
                {
                    var tokenReplace = new TokenReplace();
                    if (string.Equals(model.SendMethod, "TO", StringComparison.OrdinalIgnoreCase))
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
                return CreateStatusResponse("error", ex.Message);
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
                if (string.IsNullOrWhiteSpace(model.Subject) || string.IsNullOrWhiteSpace(model.Message))
                {
                    return CreateStatusResponse("warning", Localization.GetString("MessageValidation", LocalResource));
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
                    string.Format(Localization.GetString("NoMessagesSentPlusError", LocalResource), ex.Message));
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
                if (folder == null)
                {
                    return Request.CreateResponse(HttpStatusCode.BadRequest,
                        new { success = false, message = "Folder not found." });
                }

                var results = new List<object>();
                for (int i = 0; i < httpRequest.Files.Count; i++)
                {
                    var uploaded = httpRequest.Files[i];
                    if (uploaded == null || uploaded.ContentLength == 0)
                    {
                        continue;
                    }

                    var fileName = Path.GetFileName(uploaded.FileName);
                    var file = _fileManager.AddFile(folder, fileName, uploaded.InputStream, true);
                    results.Add(new { fileId = file.FileId, fileName = file.FileName });
                }

                return Request.CreateResponse(HttpStatusCode.OK, new { success = true, files = results });
            }
            catch (Exception ex)
            {
                Exceptions.LogException(ex);
                return Request.CreateResponse(HttpStatusCode.InternalServerError,
                    new { success = false, message = ex.Message });
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
                    case "1":
                        email.Priority = MailPriority.High;
                        break;
                    case "2":
                        email.Priority = MailPriority.Normal;
                        break;
                    case "3":
                        email.Priority = MailPriority.Low;
                        break;
                    default:
                        return CreateStatusResponse("error", Localization.GetString("MessageValidation", LocalResource));
                }

                if (!string.IsNullOrWhiteSpace(model.From) &&
                    !string.Equals(email.SendingUser.Email, model.From, StringComparison.OrdinalIgnoreCase))
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
                        if (attachmentFile != null)
                        {
                            email.AddAttachment(
                                _fileManager.GetFileContent(attachmentFile),
                                new ContentType { MediaType = attachmentFile.ContentType, Name = attachmentFile.FileName });
                        }
                    }
                }

                switch (model.SendMethod)
                {
                    case "TO":
                        email.AddressMethod = SendTokenizedBulkEmail.AddressMethods.Send_TO;
                        break;
                    case "BCC":
                        email.AddressMethod = SendTokenizedBulkEmail.AddressMethods.Send_BCC;
                        break;
                    case "RELAY":
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

                if (string.Equals(model.SendAction, "S", StringComparison.OrdinalIgnoreCase))
                {
                    return SendMailSynchronously(email);
                }
                else
                {
                    var result = SendMailAsynchronously(model, email);
                    email = null; // thread takes ownership and will dispose
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

        private HttpResponseMessage SendMailSynchronously(SendTokenizedBulkEmail email)
        {
            var mailsSent = email.SendMails();
            if (mailsSent > 0)
            {
                return CreateStatusResponse("success",
                    string.Format(Localization.GetString("MessagesSentCount", LocalResource), mailsSent));
            }
            else
            {
                return CreateStatusResponse("warning",
                    string.Format(Localization.GetString("NoMessagesSent", LocalResource), email.SendingUser.Email));
            }
        }

        private HttpResponseMessage SendMailAsynchronously(NewsletterFormDto model, SendTokenizedBulkEmail email)
        {
            var portalId = PortalSettings.PortalId;
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
                model.From,
                model.From,
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
                var thread = new Thread(() => SendAndDispose(email));
                thread.Start();
                return CreateStatusResponse("success", Localization.GetString("MessageSent", LocalResource));
            }
            else
            {
                return CreateStatusResponse("warning",
                    string.Format(Localization.GetString("NoMessagesSentPlusError", LocalResource), sendMailResult));
            }
        }

        private static void SendAndDispose(SendTokenizedBulkEmail email)
        {
            using (email)
            {
                email.Send();
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
                foreach (var email in model.AdditionalEmails.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmedEmail = email.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmedEmail))
                    {
                        users.Add(new UserInfo
                        {
                            UserID = Null.NullInteger,
                            Email = trimmedEmail,
                            DisplayName = trimmedEmail
                        });
                    }
                }
            }

            foreach (var value in (model.Recipients ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (value.StartsWith("role-", StringComparison.OrdinalIgnoreCase))
                {
                    var role = RoleController.Instance.GetRoleById(portalId, int.Parse(value.Substring(5)));
                    if (role != null && !string.IsNullOrWhiteSpace(role.RoleName))
                    {
                        roleNames.Add(role.RoleName);
                    }
                }
                else if (value.StartsWith("user-", StringComparison.OrdinalIgnoreCase))
                {
                    var user = UserController.GetUserById(_hostSettings, portalId, int.Parse(value.Substring(5)));
                    if (user != null)
                    {
                        users.Add(user);
                    }
                }
            }
        }

        private static bool IsValidEmailOrEmpty(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            return Regex.IsMatch(value, @"^\w+([\-+.]\w+)*@\w+([-.]\w+)*\.\w+([-.]\w+)*$");
        }

        private static string ConvertToAbsoluteUrls(string content)
        {
            const string pattern = "<(a|link|img|script|object).[^>]*(href|src|action)=(\\\"|'|)(?<url>(.[^\\\"']*))(\\\"|'|)[^>]*>";
            return Regex.Replace(content ?? string.Empty, pattern, FormatUrls);
        }

        private static string FormatUrls(Match match)
        {
            var originalValue = match.Value;
            var url = match.Groups["url"].Value;

            if (url.StartsWith("/"))
            {
                return originalValue.Replace(url,
                    Globals.AddHTTP(HttpContext.Current.Request.Url.Host) + url);
            }

            return url.Contains("://") || url.Contains("mailto:")
                ? originalValue
                : originalValue.Replace(url,
                    Globals.AddHTTP(HttpContext.Current.Request.Url.Host) + Globals.ApplicationPath + "/" + url);
        }

        private HttpResponseMessage CreateStatusResponse(string statusType, string message)
        {
            string cssClass;
            switch (statusType)
            {
                case "success":
                    cssClass = "dnnFormMessage dnnFormSuccess";
                    break;
                case "error":
                    cssClass = "dnnFormMessage dnnFormError";
                    break;
                default:
                    cssClass = "dnnFormMessage dnnFormWarning";
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
