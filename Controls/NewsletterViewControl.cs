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
using DotNetNuke.Common;
using DotNetNuke.Entities.Users;
using DotNetNuke.Security.Roles;
using DotNetNuke.Services.Exceptions;
using DotNetNuke.Services.FileSystem;
using DotNetNuke.Services.Localization;
using DotNetNuke.Web.MvcPipeline.ModuleControl;
using DotNetNuke.Web.MvcPipeline.ModuleControl.Page;
using DotNetNuke.Web.MvcPipeline.ModuleControl.Razor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web.Mvc;

namespace Dnn.Modules.Newsletters.Controls
{
    /// <summary>
    /// Razor-based replacement for the legacy newsletter WebForms control.
    /// POST actions are handled by NewsletterApiController via AJAX.
    /// </summary>
    [ValidateInput(false)]
    public class NewsletterViewControl : RazorModuleControlBase, IPageContributor
    {
        private readonly DotNetNuke.Abstractions.Application.IHostSettings _hostSettings;

        /// <summary>Initializes a new instance of the <see cref="NewsletterViewControl"/> class.</summary>
        /// <param name="hostSettings">Host settings injected by the DI container.</param>
        public NewsletterViewControl(DotNetNuke.Abstractions.Application.IHostSettings hostSettings)
        {
            _hostSettings = hostSettings;
        }

        private UserInfo CurrentUser => UserController.Instance.GetCurrentUserInfo();

        /// <summary>Invokes the newsletter view control and returns the Razor module result.</summary>
        /// <returns>An <see cref="IRazorModuleResult"/> representing the rendered view.</returns>
        public override IRazorModuleResult Invoke()
        {
            try
            {
                var model = CreateModel();
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
            var rootFolder = FolderManager.Instance.GetFolder(PortalId, string.Empty);
            var rootFolderId = rootFolder?.FolderID ?? 0;

            return new NewsletterViewModel
            {
                ModuleId = ModuleId,
                From = CurrentUser?.Email ?? string.Empty,
                SendMethod = Constants.SendMethod.To,
                SendAction = Constants.SendAction.Asynchronous,
                Priority = Constants.Priority.Normal,
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
                    .ToList(),
                Attachment = new AttachmentPickerModel
                {
                    ModuleId = ModuleId,
                    SelectedFolderId = rootFolderId,
                    Folders = GetPortalFolders(),
                    Files = GetFilesInFolder(rootFolderId)
                }
            };
        }

        private List<AttachmentPickerModel.FolderOption> GetPortalFolders()
        {
            return FolderManager.Instance.GetFolders(PortalId)
                .OrderBy(f => f.FolderPath)
                .Select(f => new AttachmentPickerModel.FolderOption
                {
                    FolderId = f.FolderID,
                    DisplayName = string.IsNullOrEmpty(f.FolderPath) ? "Site Root" : f.FolderPath.TrimEnd('/')
                })
                .ToList();
        }

        private List<AttachmentPickerModel.FileOption> GetFilesInFolder(int folderId)
        {
            var folder = FolderManager.Instance.GetFolder(folderId);
            if (folder == null)
            {
                return new List<AttachmentPickerModel.FileOption>();
            }

            return FolderManager.Instance.GetFiles(folder)
                .OrderBy(f => f.FileName)
                .Select(f => new AttachmentPickerModel.FileOption
                {
                    FileId = f.FileId,
                    FileName = f.FileName
                })
                .ToList();
        }

        private string GetInitialEntries()
        {
            int id;
            UserInfo user;
            RoleInfo role;
            var entities = new StringBuilder("[");

            foreach (var value in (Request.QueryString["users"] ?? string.Empty).Split(','))
            {
                if (int.TryParse(value, out id) && (user = UserController.GetUserById(_hostSettings, PortalId, id)) != null)
                {
                    entities.AppendFormat(@"{{ ""id"": ""user-{0}"", ""name"": ""{1}"" }},", user.UserID, JsonEscape(user.DisplayName));
                }
            }

            foreach (var value in (Request.QueryString["roles"] ?? string.Empty).Split(','))
            {
                if (int.TryParse(value, out id) && (role = RoleController.Instance.GetRoleById(PortalId, id)) != null)
                {
                    entities.AppendFormat(@"{{ ""id"": ""role-{0}"", ""name"": ""{1}"" }},", role.RoleID, JsonEscape(role.RoleName));
                }
            }

            if (entities.Length > 1)
            {
                entities.Length--;
            }

            return entities.Append(']').ToString();
        }

        /// <summary>JSON-string-encode an arbitrary value for safe inclusion between double quotes in a JSON literal
        /// (also handles control chars and the U+2028/U+2029 line terminators that break JS string parsing).</summary>
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
                    case '/': sb.Append("\\/"); break;
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

        #region ConfigurePage
        /// <summary>
        /// Registers CSS, JS and AJAX support on the page.
        /// Resources are registered here (not in Razor views) to avoid duplicates
        /// when the module appears multiple times on a page, and to support module caching.
        /// </summary>
        public void ConfigurePage(PageConfigurationContext context)
        {
            // Enable AJAX anti-forgery and script support (required for $.ServicesFramework)
            context.ServicesFramework.RequestAjaxAntiForgerySupport();
            context.ServicesFramework.RequestAjaxScriptSupport();

            // CSS stylesheets
            context.ClientResourceController
                .CreateStylesheet("~/DesktopModules/Admin/Newsletters/Resources/css/module.css")
                .Register();

            context.ClientResourceController
                .CreateStylesheet("~/DesktopModules/Admin/Newsletters/Resources/css/attachment-picker.css")
                .Register();

            // JavaScript files
            context.ClientResourceController
                .CreateScript("~/DesktopModules/Admin/Newsletters/Resources/js/edit.js")
                .Register();

            context.ClientResourceController
                .CreateScript("~/DesktopModules/Admin/Newsletters/Resources/js/attachment-picker.js")
                .Register();

            context.ClientResourceController
                .CreateScript("~/DesktopModules/Admin/Newsletters/Resources/js/newsletter.js")
                .Register();
        }
        #endregion
    }
}
