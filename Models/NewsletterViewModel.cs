#region Copyright
// 
// DotNetNuke® - http://www.dnnsoftware.com
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

using System.Collections.Generic;

namespace Dnn.Modules.Newsletters.ViewModels
{
    /// <summary>
    /// Represents the data required to compose and send a newsletter, including recipients, message content, and
    /// delivery options.
    /// </summary>
    /// <remarks>This view model is typically used to transfer newsletter composition data between the user
    /// interface and backend processing logic. It includes properties for specifying recipients, message details,
    /// attachments, and various sending options such as priority and delivery method.</remarks>
    public class NewsletterViewModel
    {
        /// <summary>Represents a language selection option for the newsletter.</summary>
        public class LanguageOption
        {
            /// <summary>Gets or sets the language code value.</summary>
            public string Value { get; set; }

            /// <summary>Gets or sets the display text for the language.</summary>
            public string Text { get; set; }

            /// <summary>Gets or sets a value indicating whether this language is selected.</summary>
            public bool Selected { get; set; }
        }

        /// <summary>
        /// Gets or sets the recipients of the newsletter.
        /// </summary>
        public string Recipients { get; set; }
        /// <summary>
        /// Gets or sets the initial set of entries as a string.
        /// </summary>
        public string InitialEntries { get; set; }
        /// <summary>
        /// Gets or sets a value indicating whether language options are visible to the user.
        /// </summary>
        public bool LanguagesVisible { get; set; }
        /// <summary>
        /// Gets or sets the email address associated with the user.
        /// </summary>
        public string Email { get; set; }
        /// <summary>
        /// Gets or sets additional email addresses separated by semicolons.
        /// </summary>
        public string AdditionalEmails { get; set; }
        /// <summary>
        /// Gets or sets the available language filters.
        /// </summary>
        public IList<LanguageOption> AvailableLanguages { get; set; } = new List<LanguageOption>();
        /// <summary>
        /// Gets or sets the selected language codes.
        /// </summary>
        public IList<string> SelectedLanguages { get; set; } = new List<string>();
        /// <summary>
        /// Gets or sets the sender's address for the message.
        /// </summary>
        public string From { get; set; }
        /// <summary>
        /// Gets or sets the address to which replies should be sent.
        /// </summary>
        public string ReplyTo { get; set; }
        /// <summary>
        /// Gets or sets the subject line of the message.
        /// </summary>
        public string Subject { get; set; }
        /// <summary>
        /// Gets or sets the message content.
        /// </summary>
        public string Message { get; set; }
        /// <summary>
        /// Gets or sets the URL of the attachment associated with this item.
        /// </summary>
        public string AttachmentUrl { get; set; }
        /// <summary>
        /// Gets or sets the resolved attachment file name when the attachment points to a DNN file.
        /// </summary>
        public string AttachmentFileName { get; set; }
        /// <summary>
        /// Gets or sets a value indicating whether tokens in the input should be replaced with their corresponding
        /// values.
        /// </summary>
        public bool ReplaceTokens { get; set; } = true;
        /// <summary>
        /// Gets or sets the priority level associated with the item.
        /// </summary>
        public string Priority { get; set; } = "2";
        /// <summary>
        /// Gets or sets the method used to send the message.
        /// </summary>
        public string SendMethod { get; set; } = "TO";
        /// <summary>
        /// Gets or sets a value indicating whether the relay address is visible to clients.
        /// </summary>
        public bool RelayAddressVisible { get; set; }
        /// <summary>
        /// Gets or sets the address of the relay server used for communication.
        /// </summary>
        public string RelayAddress { get; set; }
        /// <summary>
        /// Gets or sets the action code to be sent with the request.
        /// </summary>
        public string SendAction { get; set; } = "A";
        /// <summary>
        /// Gets or sets a value indicating whether the preview is visible.
        /// </summary>
        public bool PreviewVisible { get; set; }
        /// <summary>
        /// Gets or sets the subject line preview text.
        /// </summary>
        public string PreviewSubject { get; set; }
        /// <summary>
        /// Gets or sets the plain text preview of the message body.
        /// </summary>
        public string PreviewBody { get; set; }
        /// <summary>
        /// Gets or sets the unique identifier for the module.
        /// </summary>
        public int ModuleId { get; set; }
        /// <summary>
        /// Gets or sets a feedback message shown above the form.
        /// </summary>
        public string StatusMessage { get; set; }
        /// <summary>
        /// Gets or sets the css class used for the feedback message.
        /// </summary>
        public string StatusCssClass { get; set; }
        /// <summary>
        /// Gets or sets a value indicating whether the message content should be sent as html.
        /// </summary>
        public bool IsHtmlMessage { get; set; } = true;
    }
}
