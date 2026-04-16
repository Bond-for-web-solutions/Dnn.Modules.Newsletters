using System.Collections.Generic;

namespace Dnn.Modules.Newsletters.Models
{
    /// <summary>
    /// Data transfer object for newsletter API form submissions.
    /// </summary>
    public class NewsletterFormDto
    {
        /// <summary>Gets or sets the token-input recipients string.</summary>
        public string Recipients { get; set; }

        /// <summary>Gets or sets additional email addresses separated by semicolons.</summary>
        public string AdditionalEmails { get; set; }

        /// <summary>Gets or sets the email subject.</summary>
        public string Subject { get; set; }

        /// <summary>Gets or sets the email body.</summary>
        public string Message { get; set; }

        /// <summary>Gets or sets the sender email address.</summary>
        public string From { get; set; }

        /// <summary>Gets or sets the reply-to email address.</summary>
        public string ReplyTo { get; set; }

        /// <summary>Gets or sets the relay address for RELAY send method.</summary>
        public string RelayAddress { get; set; }

        /// <summary>Gets or sets the email priority (1=High, 2=Normal, 3=Low).</summary>
        public string Priority { get; set; }

        /// <summary>Gets or sets the send method (TO, BCC, or RELAY).</summary>
        public string SendMethod { get; set; }

        /// <summary>Gets or sets the send action (S=Synchronous, A=Asynchronous).</summary>
        public string SendAction { get; set; }

        /// <summary>Gets or sets whether to replace tokens in the message.</summary>
        public bool ReplaceTokens { get; set; }

        /// <summary>Gets or sets whether the message is HTML.</summary>
        public bool IsHtmlMessage { get; set; }

        /// <summary>Gets or sets the selected language filter codes.</summary>
        public List<string> SelectedLanguages { get; set; }

        /// <summary>Gets or sets the file IDs of selected attachments.</summary>
        public List<int> AttachmentFileIds { get; set; }
    }
}
