#region Copyright
// 
// DotNetNuke� - http://www.dnnsoftware.com
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
#region Usings

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;

using DotNetNuke.Common;
using DotNetNuke.Common.Utilities;
using DotNetNuke.Entities.Host;
using DotNetNuke.Entities.Modules;
using DotNetNuke.Entities.Users;
using DotNetNuke.Framework;
using DotNetNuke.Framework.JavaScriptLibraries;
using DotNetNuke.Security.Roles;
using DotNetNuke.Services.Exceptions;
using DotNetNuke.Services.FileSystem;
using DotNetNuke.Services.Localization;
using DotNetNuke.Services.Mail;
using DotNetNuke.Services.Tokens;
using DotNetNuke.UI.Skins.Controls;
using Microsoft.Extensions.DependencyInjection; 
using Dnn.Modules.Newsletters.Components;
using System.Web;
using System.Net.Mime;
using DotNetNuke.Abstractions.Application;
using DotNetNuke.Abstractions.Logging;

#endregion

namespace Dnn.Modules.Newsletters
{

    /// -----------------------------------------------------------------------------
    /// <summary>
    /// The Newsletter PortalModuleBase is used to manage a Newsletters
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// -----------------------------------------------------------------------------
    public partial class Newsletter : PortalModuleBase
    {
        private readonly IMailSettings _mailSettings;
        private readonly IHostSettings _hostSettings;
        private readonly IApplicationStatusInfo _appStatus;
        private readonly IEventLogger _eventLogger;

        /// <summary>
        /// Initializes a new instance of the Newsletter class using the configured mail settings.
        /// </summary>
        /// <remarks>This constructor retrieves the required mail settings from the application's
        /// dependency provider. If the mail settings are not configured, an exception may be thrown during
        /// initialization.</remarks>
        public Newsletter()
        {
            _mailSettings = DependencyProvider.GetRequiredService<IMailSettings>();
            _hostSettings = DependencyProvider.GetRequiredService<IHostSettings>();
            _appStatus = DependencyProvider.GetRequiredService<IApplicationStatusInfo>();
            _eventLogger = DependencyProvider.GetRequiredService<IEventLogger>();
        }

        #region Private Methods

        /// <summary>JSON-string-encode an arbitrary value for safe inclusion between double quotes in a JS literal.</summary>
        private static string JsonEscape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(value.Length + 8);
            foreach (var c in value)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '/': sb.Append("\\/"); break; // also escapes "</script>" sequence
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ' || c == '\u2028' || c == '\u2029')
                        {
                            sb.AppendFormat("\\u{0:x4}", (int)c);
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            return sb.ToString();
        }

        #endregion

        #region Protected Methods

        /// <summary>
        /// Get Initial Entries.
        /// </summary>
        /// <returns></returns>
        protected string GetInitialEntries()
        {
            int id;
            UserInfo user;
            RoleInfo role;
            var entities = new StringBuilder("[");

            foreach (var value in (Request.QueryString["users"] ?? string.Empty).Split(','))
                if (int.TryParse(value, out id) && (user = UserController.GetUserById(_hostSettings, PortalId, id)) != null)
                    entities.AppendFormat(@"{{ ""id"": ""user-{0}"", ""name"": ""{1}"" }},", user.UserID, JsonEscape(user.DisplayName));
            foreach (var value in (Request.QueryString["roles"] ?? string.Empty).Split(','))
                if (int.TryParse(value, out id) && (role = RoleController.Instance.GetRoleById(PortalId, id)) != null)
                    entities.AppendFormat(@"{{ ""id"": ""role-{0}"", ""name"": ""{1}"" }},", role.RoleID, JsonEscape(role.RoleName));

            return entities.Append(']').ToString();
        }
        #endregion

        #region Event Handlers

        /// -----------------------------------------------------------------------------
        /// <summary>
        /// Page_Load runs when the control is loaded
        /// </summary>
        /// -----------------------------------------------------------------------------
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            cmdPreview.Click += OnPreviewClick;
            cmdSend.Click += OnSendClick;

            ServicesFramework.Instance.RequestAjaxScriptSupport();
            ServicesFramework.Instance.RequestAjaxAntiForgerySupport();
            JavaScript.RequestRegistration(_appStatus, _eventLogger, PortalSettings, CommonJs.DnnPlugins);

            try
            {
                if (!Page.IsPostBack)
                {
                    txtFrom.Text = UserInfo.Email;
                }
                plLanguages.Visible = (LocaleController.Instance.GetLocales(PortalId).Count > 1);
                pnlRelayAddress.Visible = (cboSendMethod.SelectedValue == Constants.SendMethod.Relay);
            }
            catch (Exception exc) //Module failed to load
            {
                Exceptions.ProcessModuleLoadException(this, exc);
            }
        }

        /// -----------------------------------------------------------------------------
        /// <summary>
        /// cmdSend_Click runs when the cmdSend Button is clicked
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// -----------------------------------------------------------------------------
        protected void OnSendClick(Object sender, EventArgs e)
        {
            var message = "";
            var messageType = ModuleMessage.ModuleMessageType.GreenSuccess;

            try
            {
                List<string> roleNames;
                List<UserInfo> users;
                GetRecipients(out roleNames, out users);

                if (IsReadyToSend(roleNames, users, ref message, ref messageType))
                {
                    SendEmail(roleNames, users, ref message, ref messageType);

                    // Compliance audit: only record on success. The audit row contains operator
                    // identity, role names, recipient counts, transport choice, and client IP --
                    // never the message body, attachments, SMTP credentials, or raw recipient
                    // addresses.
                    if (messageType == ModuleMessage.ModuleMessageType.GreenSuccess)
                    {
                        var additionalEmailCount = users.Count(u => u != null && u.UserID == Null.NullInteger);
                        var userRecipientCount = users.Count - additionalEmailCount;
                        var clientIp = Request?.UserHostAddress ?? string.Empty;

                        Components.NewsletterMailHelper.WriteSendAuditLog(
                            _eventLogger,
                            PortalSettings,
                            UserInfo?.UserID ?? -1,
                            txtSubject.Text,
                            roleNames,
                            userRecipientCount,
                            additionalEmailCount,
                            cboSendMethod.SelectedItem?.Value,
                            optSendAction.SelectedItem?.Value,
                            clientIp);
                    }
                }

                DotNetNuke.UI.Skins.Skin.AddModuleMessage(this, message, messageType);
                ((CDefault)Page).ScrollToControl(Page.Form);
            }
            catch (Exception exc)
            {
                Exceptions.ProcessModuleLoadException(this, exc);
            }
        }

        private void SendEmail(List<string> roleNames, List<UserInfo> users, ref string message, ref ModuleMessage.ModuleMessageType messageType)
        {
            //it is awkward to ensure that email is disposed correctly because when sent asynch it should be disposed by the  asynch thread
            var email = new SendTokenizedBulkEmail(roleNames, users, /*removeDuplicates*/ true, txtSubject.Text, ConvertToAbsoluteUrls(teMessage.Text));

            bool isValid;
            try
            {
                isValid = PrepareEmail(email, ref message, ref messageType);
            }
            catch (Exception)
            {
                email.Dispose();
                throw;
            }

            if (isValid)
            {

                var sendActionValue = optSendAction.SelectedItem?.Value;
                if (sendActionValue == Constants.SendAction.Synchronous)
                {
                    try
                    {
                        SendMailSynchronously(email, out message, out messageType);
                    }
                    finally
                    {
                        email.Dispose();
                    }
                }
                else
                {
                    bool ownershipTransferred;
                    SendMailAsynchronously(email, out message, out messageType, out ownershipTransferred);
                    if (!ownershipTransferred)
                    {
                        // Test message failed: thread was never started, we still own the email.
                        email.Dispose();
                    }
                }
            }
            else
            {
                email.Dispose();
            }
        }

        private void SendMailAsynchronously(SendTokenizedBulkEmail email, out string message, out ModuleMessage.ModuleMessageType messageType, out bool ownershipTransferred)
        {
            ownershipTransferred = false;
            //First send off a test message
            var strStartSubj = Localization.GetString("EMAIL_BulkMailStart_Subject.Text", Localization.GlobalResourceFile);
            if (!string.IsNullOrEmpty(strStartSubj)) strStartSubj = string.Format(strStartSubj, txtSubject.Text);

            var strStartBody = Localization.GetString("EMAIL_BulkMailStart_Body.Text", Localization.GlobalResourceFile);
            if (!string.IsNullOrEmpty(strStartBody)) strStartBody = string.Format(strStartBody, txtSubject.Text, UserInfo.DisplayName, email.Recipients().Count);

            var mailSettings = _mailSettings;
            var smtpServer = mailSettings.GetServer(PortalId);
            var smtpAuthentication = mailSettings.GetAuthentication(PortalId);
            var smtpUsername = mailSettings.GetUsername(PortalId);
            var smtpPassword = mailSettings.GetPassword(PortalId);
            var enableSmtpSsl = mailSettings.GetSecureConnectionEnabled(PortalId);

            var sendMailResult = Mail.SendMail(txtFrom.Text,
                txtFrom.Text,
                "",
                "",
                MailPriority.Normal,
                strStartSubj,
                MailFormat.Text,
                Encoding.UTF8,
                strStartBody,
                "",
                smtpServer,
                smtpAuthentication,
                smtpUsername,
                smtpPassword,
                enableSmtpSsl);

            if (string.IsNullOrEmpty(sendMailResult))
            {
                // Capture culture before transferring ownership; HttpContext.Current is null on the worker thread.
                var culture = Thread.CurrentThread.CurrentCulture;
                var uiCulture = Thread.CurrentThread.CurrentUICulture;
                var objThread = new Thread(() => Components.NewsletterMailHelper.SendAndDispose(email, culture, uiCulture))
                {
                    IsBackground = true,
                    Name = "Newsletters.BulkSend",
                };
                objThread.Start();
                ownershipTransferred = true;
                message = Localization.GetString("MessageSent", LocalResourceFile);
                messageType = ModuleMessage.ModuleMessageType.GreenSuccess;
            }
            else
            {
                message = string.Format(Localization.GetString("NoMessagesSentPlusError", LocalResourceFile), sendMailResult);
                messageType = ModuleMessage.ModuleMessageType.YellowWarning;
            }
        }

        private static void SendAndDispose(SendTokenizedBulkEmail email, CultureInfo culture, CultureInfo uiCulture)
            => Components.NewsletterMailHelper.SendAndDispose(email, culture, uiCulture);

        private void SendMailSynchronously(SendTokenizedBulkEmail email, out string strResult, out ModuleMessage.ModuleMessageType msgResult)
        {
            int mailsSent = email.SendMails();

            if (mailsSent > 0)
            {
                strResult = string.Format(Localization.GetString("MessagesSentCount", LocalResourceFile), mailsSent);
                msgResult = ModuleMessage.ModuleMessageType.GreenSuccess;
            }
            else
            {
                strResult = string.Format(Localization.GetString("NoMessagesSent", LocalResourceFile), email.SendingUser?.Email ?? string.Empty);
                msgResult = ModuleMessage.ModuleMessageType.YellowWarning;
            }
        }

        private bool PrepareEmail(SendTokenizedBulkEmail email, ref string message, ref ModuleMessage.ModuleMessageType messageType)
        {
            bool isValid = true;

            switch (teMessage.Mode)
            {
                case Constants.BodyMode.Rich:
                    email.BodyFormat = MailFormat.Html;
                    break;
                default:
                    email.BodyFormat = MailFormat.Text;
                    break;
            }

            switch (cboPriority.SelectedItem?.Value)
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
                    isValid = false;
                    break;
            }

            if (txtFrom.Text != string.Empty && email.SendingUser?.Email != txtFrom.Text)
            {
                var myUser = email.SendingUser ?? new UserInfo();
                myUser.Email = txtFrom.Text;
                email.SendingUser = myUser;
            }

            if (txtReplyTo.Text != string.Empty && email.ReplyTo?.Email != txtReplyTo.Text)
            {
                var myUser = new UserInfo { Email = txtReplyTo.Text };
                email.ReplyTo = myUser;
            }

            if (selLanguage.Visible && selLanguage.SelectedLanguages != null)
            {
                email.LanguageFilter = selLanguage.SelectedLanguages;
            }

            if (ctlAttachment.Url.StartsWith("FileID="))
            {
                int fileId = int.Parse(ctlAttachment.Url.Substring(7));
                var objFileInfo = FileManager.Instance.GetFile(fileId);
                //TODO: support secure storage locations for attachments! [sleupold 06/15/2007]
                email.AddAttachment(FileManager.Instance.GetFileContent(objFileInfo),
                                               new ContentType { MediaType = objFileInfo.ContentType, Name = objFileInfo.FileName });
            }

            switch (cboSendMethod.SelectedItem?.Value)
            {
                case Constants.SendMethod.To:
                    email.AddressMethod = SendTokenizedBulkEmail.AddressMethods.Send_TO;
                    break;
                case Constants.SendMethod.Bcc:
                    email.AddressMethod = SendTokenizedBulkEmail.AddressMethods.Send_BCC;
                    break;
                case Constants.SendMethod.Relay:
                    email.AddressMethod = SendTokenizedBulkEmail.AddressMethods.Send_Relay;
                    if (string.IsNullOrEmpty(txtRelayAddress.Text))
                    {
                        message = string.Format(Localization.GetString("MessagesSentCount", LocalResourceFile), -1);
                        messageType = ModuleMessage.ModuleMessageType.YellowWarning;
                        isValid = false;
                    }
                    else
                    {
                        email.RelayEmailAddress = txtRelayAddress.Text;
                    }
                    break;
            }

            email.SuppressTokenReplace = !chkReplaceTokens.Checked;

            return isValid;
        }

        private bool IsReadyToSend(List<string> roleNames, List<UserInfo> users, ref string message, ref ModuleMessage.ModuleMessageType messageType)
        {
            if (String.IsNullOrEmpty(txtSubject.Text) || String.IsNullOrEmpty(teMessage.Text))
            {
                message = Localization.GetString("MessageValidation", LocalResourceFile);
                messageType = ModuleMessage.ModuleMessageType.RedError;
                return false;
            }

            if (users.Count == 0 && roleNames.Count == 0)
            {
                message = string.Format(Localization.GetString("NoRecipients", LocalResourceFile), -1);
                messageType = ModuleMessage.ModuleMessageType.YellowWarning;
                return false;
            }

            return true;
        }

        private void GetRecipients(out List<string> objRoleNames, out List<UserInfo> objUsers)
        {
            objRoleNames = new List<string>();
            objUsers = new List<UserInfo>();

            if (!String.IsNullOrEmpty(txtEmail.Text))
            {
                Array arrEmail = txtEmail.Text.Split(';');
                foreach (string strEmail in arrEmail)
                {
                    var objUser = new UserInfo { UserID = Null.NullInteger, Email = strEmail, DisplayName = strEmail };
                    objUsers.Add(objUser);
                }
            }

            objRoleNames.AddRange(recipients.Value.Split(',').Where(value => value.StartsWith("role-")).Select(value => RoleController.Instance.GetRoleById(PortalId, int.Parse(value.Substring(5))).RoleName).Where(roleName => !string.IsNullOrWhiteSpace(roleName)));
            objUsers.AddRange(recipients.Value.Split(',').Where(value => value.StartsWith("user-")).Select(value => UserController.GetUserById(_hostSettings, PortalId, int.Parse(value.Substring(5)))).Where(user => user != null));
        }

        /// <summary>
        /// Display a preview of the message to be sent out
        /// </summary>
        /// <param name="sender">ignored</param>
        /// <param name="e">ignores</param>
        protected void OnPreviewClick(object sender, EventArgs e)
        {
            try
            {
                if (String.IsNullOrEmpty(txtSubject.Text) || String.IsNullOrEmpty(teMessage.Text))
                {
                    //no subject or message
                    var strResult = Localization.GetString("MessageValidation", LocalResourceFile);
                    const ModuleMessage.ModuleMessageType msgResult = ModuleMessage.ModuleMessageType.YellowWarning;
                    DotNetNuke.UI.Skins.Skin.AddModuleMessage(this, strResult, msgResult);
                    ((CDefault)Page).ScrollToControl(Page.Form);
                    return;
                }

                //convert links to absolute
                var strBody = ConvertToAbsoluteUrls(teMessage.Text);

                if (chkReplaceTokens.Checked)
                {
                    var objTr = new TokenReplace();
                    if (cboSendMethod.SelectedItem.Value == Constants.SendMethod.To)
                    {
                        objTr.User = UserInfo;
                        objTr.AccessingUser = UserInfo;
                        objTr.DebugMessages = true;
                    }
                    lblPreviewSubject.Text = objTr.ReplaceEnvironmentTokens(txtSubject.Text);
                    lblPreviewBody.Text = objTr.ReplaceEnvironmentTokens(strBody);
                }
                else
                {
                    lblPreviewSubject.Text = txtSubject.Text;
                    lblPreviewBody.Text = strBody;
                }
                pnlPreview.Visible = true;
            }
            catch (Exception exc) //Module failed to load
            {
                Exceptions.ProcessModuleLoadException(this, exc);
            }
        }

        private string ConvertToAbsoluteUrls(string content)
            => Components.NewsletterMailHelper.ConvertToAbsoluteUrls(
                content,
                PortalSettings?.PortalAlias?.HTTPAlias,
                Globals.ApplicationPath);

        #endregion

    }
}