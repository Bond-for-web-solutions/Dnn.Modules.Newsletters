#region Copyright
//
// DotNetNuke(R) - http://www.dnnsoftware.com
// Copyright (c) 2002-2015
// by DNN Corp.
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
// documentation files (the "Software"), to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and
// to permit persons to whom the Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all copies or substantial portions
// of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED
// TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
// THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF
// CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.
#endregion

using Dnn.Modules.Newsletters.Components;
using Dnn.Modules.Newsletters.Models;
using DotNetNuke.Abstractions.Application;
using DotNetNuke.Common;
using DotNetNuke.Common.Utilities;
using DotNetNuke.Entities.Users;
using DotNetNuke.Framework;
using DotNetNuke.Security.Roles;
using DotNetNuke.Services.Exceptions;
using DotNetNuke.Services.FileSystem;
using DotNetNuke.Services.Localization;
using DotNetNuke.Services.Mail;
using DotNetNuke.Services.Tokens;
using DotNetNuke.Web.MvcPipeline.ModuleControl;
using DotNetNuke.Web.MvcPipeline.ModuleControl.Page;
using DotNetNuke.Web.MvcPipeline.ModuleControl.Razor;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web;
using System.Web.Mvc;

namespace Dnn.Modules.Newsletters.Controls
{
    /// <summary>
    /// Razor-based replacement for the legacy newsletter WebForms control.
    /// </summary>
    /// VAlidateinput is disabled for this control to allow HTML content in the message body, but all user input is still validated and encoded as needed before rendering or sending emails.
    [ValidateInput(false)]
    public class NewsletterViewControl : RazorModuleControlBase, IPageContributor
    {
        private readonly IMailSettings _mailSettings;
        private readonly IFileManager _fileManager;

        private UserInfo CurrentUser => UserController.Instance.GetCurrentUserInfo();

        public NewsletterViewControl()
        {
            _mailSettings = DependencyProvider.GetRequiredService<IMailSettings>();
            _fileManager = DependencyProvider.GetRequiredService<IFileManager>();
        }

        public override IRazorModuleResult Invoke()
        {
            try
            {
                var model = CreateModel();

                if (Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
                {
                    MapRequestToModel(model);

                    var command = Request.Form["command"];
                    switch (command)
                    {
                        case "Preview":
                            return HandlePreview(model);
                        case "CancelPreview":
                            model.PreviewVisible = false;
                            return View(model);
                        case "Send":
                            return HandleSend(model);
                    }
                }

                return View(model);
            }
            catch (Exception ex)
            {
                Exceptions.LogException(ex);
                return Error("Newsletter Error", ex.Message);
            }
        }

        private NewsletterViewModel CreateModel()
        {
            var selectedLanguages = new List<string>();
            var locales = LocaleController.Instance.GetLocales(PortalSettings.PortalId);

            return new NewsletterViewModel
            {
                ModuleId = ModuleId,
                From = CurrentUser?.Email ?? string.Empty,
                SendMethod = "TO",
                SendAction = "A",
                Priority = "2",
                ReplaceTokens = true,
                InitialEntries = GetInitialEntries(),
                LanguagesVisible = locales.Count > 1,
                SelectedLanguages = selectedLanguages,
                AvailableLanguages = locales
                    .Select(locale => new NewsletterViewModel.LanguageOption
                    {
                        Value = locale.Key,
                        Text = locale.Value.Text,
                        Selected = selectedLanguages.Contains(locale.Key, StringComparer.OrdinalIgnoreCase)
                    })
                    .ToList()
            };
        }

        private void MapRequestToModel(NewsletterViewModel model)
        {
            model.Recipients = Request.Form["Recipients"] ?? string.Empty;
            model.AdditionalEmails = Request.Form["AdditionalEmails"] ?? string.Empty;
            model.Subject = Request.Form["Subject"] ?? string.Empty;
            model.Message = Request.Form["Message"] ?? string.Empty;
            model.From = Request.Form["From"] ?? string.Empty;
            model.ReplyTo = Request.Form["ReplyTo"] ?? string.Empty;
            model.AttachmentUrl = Request.Form["AttachmentUrl"] ?? string.Empty;
            model.RelayAddress = Request.Form["RelayAddress"] ?? string.Empty;
            model.Priority = Request.Form["Priority"] ?? "2";
            model.SendMethod = Request.Form["SendMethod"] ?? "TO";
            model.SendAction = Request.Form["SendAction"] ?? "A";
            model.ReplaceTokens = !string.IsNullOrEmpty(Request.Form["ReplaceTokens"]);
            model.IsHtmlMessage = !string.Equals(Request.Form["IsHtmlMessage"], "false", StringComparison.OrdinalIgnoreCase);
            model.RelayAddressVisible = string.Equals(model.SendMethod, "RELAY", StringComparison.OrdinalIgnoreCase);

            var selectedLanguages = Request.Form.GetValues("SelectedLanguages") ?? Array.Empty<string>();
            model.SelectedLanguages = selectedLanguages.Where(value => !string.IsNullOrWhiteSpace(value)).ToList();

            foreach (var language in model.AvailableLanguages)
            {
                language.Selected = model.SelectedLanguages.Contains(language.Value, StringComparer.OrdinalIgnoreCase);
            }

            model.AttachmentFileName = ResolveAttachmentFileName(model.AttachmentUrl);
        }

        private IRazorModuleResult HandlePreview(NewsletterViewModel model)
        {
            if (!ValidateCommonFields(model))
            {
                return View(model);
            }

            var previewBody = ConvertToAbsoluteUrls(model.Message);

            if (model.ReplaceTokens)
            {
                var tokenReplace = new TokenReplace();
                if (string.Equals(model.SendMethod, "TO", StringComparison.OrdinalIgnoreCase))
                {
                    tokenReplace.User = CurrentUser;
                    tokenReplace.AccessingUser = CurrentUser;
                    tokenReplace.DebugMessages = true;
                }

                model.PreviewSubject = tokenReplace.ReplaceEnvironmentTokens(model.Subject);
                model.PreviewBody = tokenReplace.ReplaceEnvironmentTokens(previewBody);
            }
            else
            {
                model.PreviewSubject = model.Subject;
                model.PreviewBody = previewBody;
            }

            model.PreviewVisible = true;
            return View(model);
        }

        private IRazorModuleResult HandleSend(NewsletterViewModel model)
        {
            try
            {
                List<string> roleNames;
                List<UserInfo> users;
                GetRecipients(model, out roleNames, out users);

                if (!ValidateReadyToSend(model, roleNames, users))
                {
                    return View(model);
                }

                SendEmail(model, roleNames, users);
                model.PreviewVisible = false;
                return View(model);
            }
            catch (Exception ex)
            {
                Exceptions.LogException(ex);
                SetStatus(model, Localization.GetString("NoMessagesSentPlusError", LocalResourceFile), "warning", ex.Message);
                return View(model);
            }
        }

        private void SendEmail(NewsletterViewModel model, List<string> roleNames, List<UserInfo> users)
        {
            var email = new SendTokenizedBulkEmail(roleNames, users, true, model.Subject, ConvertToAbsoluteUrls(model.Message));

            try
            {
                if (!PrepareEmail(model, email))
                {
                    return;
                }

                if (string.Equals(model.SendAction, "S", StringComparison.OrdinalIgnoreCase))
                {
                    SendMailSynchronously(model, email);
                }
                else
                {
                    SendMailAsynchronously(model, email);
                    email = null;
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

        private bool PrepareEmail(NewsletterViewModel model, SendTokenizedBulkEmail email)
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
                    SetStatus(model, Localization.GetString("MessageValidation", LocalResourceFile), "error");
                    return false;
            }

            if (!string.IsNullOrWhiteSpace(model.From) && !string.Equals(email.SendingUser.Email, model.From, StringComparison.OrdinalIgnoreCase))
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

            var attachmentFile = ResolveAttachment(model.AttachmentUrl);
            if (attachmentFile != null)
            {
                email.AddAttachment(
                    _fileManager.GetFileContent(attachmentFile),
                    new ContentType { MediaType = attachmentFile.ContentType, Name = attachmentFile.FileName });
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
                        SetStatus(model, Localization.GetString("NoMessagesSent", LocalResourceFile), "warning", -1);
                        return false;
                    }

                    email.RelayEmailAddress = model.RelayAddress;
                    break;
                default:
                    SetStatus(model, Localization.GetString("MessageValidation", LocalResourceFile), "error");
                    return false;
            }

            email.SuppressTokenReplace = !model.ReplaceTokens;
            return true;
        }

        private void SendMailSynchronously(NewsletterViewModel model, SendTokenizedBulkEmail email)
        {
            var mailsSent = email.SendMails();
            if (mailsSent > 0)
            {
                SetStatus(model, Localization.GetString("MessagesSentCount", LocalResourceFile), "success", mailsSent);
            }
            else
            {
                SetStatus(model, Localization.GetString("NoMessagesSent", LocalResourceFile), "warning", email.SendingUser.Email);
            }
        }

        private void SendMailAsynchronously(NewsletterViewModel model, SendTokenizedBulkEmail email)
        {
            var startSubject = Localization.GetString("EMAIL_BulkMailStart_Subject.Text", Localization.GlobalResourceFile);
            if (!string.IsNullOrEmpty(startSubject))
            {
                startSubject = string.Format(startSubject, model.Subject);
            }

            var startBody = Localization.GetString("EMAIL_BulkMailStart_Body.Text", Localization.GlobalResourceFile);
            if (!string.IsNullOrEmpty(startBody))
            {
                startBody = string.Format(startBody, model.Subject, CurrentUser.DisplayName, email.Recipients().Count);
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
                _mailSettings.GetServer(PortalId),
                _mailSettings.GetAuthentication(PortalId),
                _mailSettings.GetUsername(PortalId),
                _mailSettings.GetPassword(PortalId),
                _mailSettings.GetSecureConnectionEnabled(PortalId));

            if (string.IsNullOrEmpty(sendMailResult))
            {
                var thread = new Thread(() => SendAndDispose(email));
                thread.Start();
                SetStatus(model, Localization.GetString("MessageSent", LocalResourceFile), "success");
            }
            else
            {
                SetStatus(model, Localization.GetString("NoMessagesSentPlusError", LocalResourceFile), "warning", sendMailResult);
            }
        }

        private static void SendAndDispose(SendTokenizedBulkEmail email)
        {
            using (email)
            {
                email.Send();
            }
        }

        private bool ValidateReadyToSend(NewsletterViewModel model, List<string> roleNames, List<UserInfo> users)
        {
            if (!ValidateCommonFields(model))
            {
                return false;
            }

            if (users.Count == 0 && roleNames.Count == 0)
            {
                SetStatus(model, Localization.GetString("NoRecipients", LocalResourceFile), "warning", -1);
                return false;
            }

            if (!IsValidEmailOrEmpty(model.From) || !IsValidEmailOrEmpty(model.ReplyTo) || !IsValidEmailOrEmpty(model.RelayAddress))
            {
                SetStatus(model, Localization.GetString("revEmailAddress.ErrorMessage", LocalResourceFile), "error");
                return false;
            }

            return true;
        }

        private bool ValidateCommonFields(NewsletterViewModel model)
        {
            if (string.IsNullOrWhiteSpace(model.Subject) || string.IsNullOrWhiteSpace(model.Message))
            {
                SetStatus(model, Localization.GetString("MessageValidation", LocalResourceFile), "warning");
                return false;
            }

            return true;
        }

        private static bool IsValidEmailOrEmpty(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            return Regex.IsMatch(value, @"^\w+([\-+.]\w+)*@\w+([-.]\w+)*\.\w+([-.]\w+)*$");
        }

        private void GetRecipients(NewsletterViewModel model, out List<string> roleNames, out List<UserInfo> users)
        {
            roleNames = new List<string>();
            users = new List<UserInfo>();

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
                    var role = RoleController.Instance.GetRoleById(PortalId, int.Parse(value.Substring(5)));
                    if (role != null && !string.IsNullOrWhiteSpace(role.RoleName))
                    {
                        roleNames.Add(role.RoleName);
                    }
                }
                else if (value.StartsWith("user-", StringComparison.OrdinalIgnoreCase))
                {
                    var user = UserController.GetUserById(PortalId, int.Parse(value.Substring(5)));
                    if (user != null)
                    {
                        users.Add(user);
                    }
                }
            }
        }

        private string GetInitialEntries()
        {
            int id;
            UserInfo user;
            RoleInfo role;
            var entities = new StringBuilder("[");

            foreach (var value in (Request.QueryString["users"] ?? string.Empty).Split(','))
            {
                if (int.TryParse(value, out id) && (user = UserController.GetUserById(PortalId, id)) != null)
                {
                    entities.AppendFormat(@"{{ ""id"": ""user-{0}"", ""name"": ""{1}"" }},", user.UserID, user.DisplayName.Replace("\"", string.Empty));
                }
            }

            foreach (var value in (Request.QueryString["roles"] ?? string.Empty).Split(','))
            {
                if (int.TryParse(value, out id) && (role = RoleController.Instance.GetRoleById(PortalId, id)) != null)
                {
                    entities.AppendFormat(@"{{ ""id"": ""role-{0}"", ""name"": ""{1}"" }},", role.RoleID, role.RoleName.Replace("\"", string.Empty));
                }
            }

            if (entities.Length > 1)
            {
                entities.Length--;
            }

            return entities.Append(']').ToString();
        }

        private string ResolveAttachmentFileName(string attachmentValue)
        {
            var file = ResolveAttachment(attachmentValue);
            return file?.FileName;
        }

        private IFileInfo ResolveAttachment(string attachmentValue)
        {
            if (string.IsNullOrWhiteSpace(attachmentValue) || !attachmentValue.StartsWith("FileID=", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (!int.TryParse(attachmentValue.Substring(7), out var fileId))
            {
                return null;
            }

            return _fileManager.GetFile(fileId);
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
                return originalValue.Replace(url, Globals.AddHTTP(System.Web.HttpContext.Current.Request.Url.Host) + url);
            }

            return url.Contains("://") || url.Contains("mailto:")
                ? originalValue
                : originalValue.Replace(url, Globals.AddHTTP(System.Web.HttpContext.Current.Request.Url.Host) + Globals.ApplicationPath + "/" + url);
        }

        private static void SetStatus(NewsletterViewModel model, string format, string statusType, params object[] args)
        {
            model.StatusMessage = string.IsNullOrEmpty(format) ? string.Empty : string.Format(format, args);
            model.StatusCssClass = "dnnFormMessage " + GetStatusCssClass(statusType);
        }

        private static string GetStatusCssClass(string statusType)
        {
            switch (statusType)
            {
                case "success":
                    return "dnnFormSuccess";
                case "error":
                    return "dnnFormError";
                default:
                    return "dnnFormWarning";
            }
        }

        #region ConfigurePage
        /// <summary>
        /// Page configuration
        /// </summary>
        /// <param name="context"></param>
        public void ConfigurePage(PageConfigurationContext context)
        {
            // Request AJAX support (required for AJAX calls and form submissions)
            //context.ServicesFramework.RequestAjaxAntiForgerySupport();
            //context.ServicesFramework.RequestAjaxScriptSupport();

            // Register CSS stylesheets
            //context.ClientResourceController
            //    .CreateStylesheet("~/DesktopModules/YourModule/styles.css")
            //    .Register();

            // Register JavaScript files
            context.ClientResourceController
                .CreateScript("~/DesktopModules/Admin/Newsletters/js/edit.js")
                .Register();

            // Set page title
            //context.PageService.SetTitle("Your Module - Edit");
        }
        #endregion
    }
}
