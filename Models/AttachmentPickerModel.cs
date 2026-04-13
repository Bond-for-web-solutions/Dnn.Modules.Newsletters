using System.Collections.Generic;

namespace Dnn.Modules.Newsletters.Models
{
    /// <summary>
    /// Reusable model for the DNN file attachment picker control.
    /// </summary>
    public class AttachmentPickerModel
    {
        /// <summary>Represents a folder option in the picker.</summary>
        public class FolderOption
        {
            /// <summary>Gets or sets the folder ID.</summary>
            public int FolderId { get; set; }

            /// <summary>Gets or sets the display name.</summary>
            public string DisplayName { get; set; }
        }

        /// <summary>Represents a file option in the picker.</summary>
        public class FileOption
        {
            /// <summary>Gets or sets the file ID.</summary>
            public int FileId { get; set; }

            /// <summary>Gets or sets the file name.</summary>
            public string FileName { get; set; }
        }

        /// <summary>Gets or sets the unique HTML ID prefix to avoid collisions when used multiple times.</summary>
        public string IdPrefix { get; set; } = "Attachment";

        /// <summary>Gets or sets the module ID (needed for ServicesFramework AJAX calls).</summary>
        public int ModuleId { get; set; }

        /// <summary>Gets or sets the selected folder ID.</summary>
        public int SelectedFolderId { get; set; }

        /// <summary>Gets or sets the selected file ID.</summary>
        public int? SelectedFileId { get; set; }

        /// <summary>Gets or sets the available folders.</summary>
        public List<FolderOption> Folders { get; set; } = new List<FolderOption>();

        /// <summary>Gets or sets the files in the currently selected folder.</summary>
        public List<FileOption> Files { get; set; } = new List<FileOption>();

        /// <summary>Gets or sets the resolved file name for display.</summary>
        public string SelectedFileName { get; set; }

        /// <summary>Gets or sets the label text for the control.</summary>
        public string Label { get; set; } = "Attachment";

        /// <summary>Gets or sets the upload command name used in the form post.</summary>
        public string UploadCommand { get; set; } = "Upload";
    }
}
