(function ($) {
    'use strict';

    function initAttachmentPicker($picker) {
        // Prevent double initialization
        if ($picker.data('picker-initialized')) return;
        $picker.data('picker-initialized', true);

        var mid = parseInt($picker.attr('data-moduleid'), 10);
        var prefix = $picker.attr('data-prefix') || 'Attachment';
        var folderSelectId = prefix + '_FolderId';
        var fileSelectId = prefix + '_FileId';
        var uploadInputId = prefix + '_UploadFiles';
        var dropzoneId = prefix + '_Dropzone';
        var fileListId = prefix + '_FileList';

        var sf = $.ServicesFramework(mid);
        var apiRoot = sf.getServiceRoot('Newsletters');

        function loadFiles(folderId) {
            $.ajax({
                type: 'POST',
                url: sf.getServiceRoot('InternalServices') + 'FileUpload/LoadFiles',
                contentType: 'application/json; charset=utf-8',
                data: JSON.stringify({ FolderId: parseInt(folderId), FileFilter: '', Required: true }),
                beforeSend: sf.setModuleHeaders,
                success: function (data) {
                    var $fileSelect = $('#' + fileSelectId);
                    $fileSelect.empty().append('<option value="">&lt;None Specified&gt;</option>');
                    if (data) {
                        $.each(data, function (i, item) {
                            $fileSelect.append($('<option></option>').val(item.Value).text(item.Text));
                        });
                    }
                }
            });
        }

        function htmlEncode(str) {
            return $('<div/>').text(str).html();
        }

        function addServerFile(fileId, fileName) {
            var $list = $('#' + fileListId);
            // Check duplicate by file ID
            if ($list.find('[data-file-id="' + fileId + '"]').length > 0) return;
            // Check duplicate by file name
            var isDup = false;
            $list.find('.attachment-item-name').each(function () {
                if ($(this).text() === fileName) isDup = true;
            });
            if (isDup) return;
            var $item = $('<div class="attachment-item" data-file-id="' + fileId + '"></div>');
            $item.append('<input type="hidden" name="AttachmentFileIds" value="' + fileId + '" />');
            $item.append('<span class="attachment-item-icon">&#128196;</span>');
            $item.append('<span class="attachment-item-name">' + htmlEncode(fileName) + '</span>');
            $item.append('<button type="button" class="attachment-item-remove" title="Remove">&times;</button>');
            $list.append($item);
        }

        function isDuplicateName(fileName) {
            var found = false;
            $('#' + fileListId).find('.attachment-item-name').each(function () {
                if ($(this).text() === fileName) found = true;
            });
            return found;
        }

        function uploadFiles(files) {
            var filesToUpload = [];
            for (var i = 0; i < files.length; i++) {
                if (!isDuplicateName(files[i].name)) {
                    filesToUpload.push(files[i]);
                }
            }
            if (filesToUpload.length === 0) return;

            var formData = new FormData();
            formData.append('folderId', $('#' + folderSelectId).val());
            for (var j = 0; j < filesToUpload.length; j++) {
                formData.append('file', filesToUpload[j]);
            }

            $.ajax({
                url: apiRoot + 'NewsletterApi/Upload',
                type: 'POST',
                data: formData,
                processData: false,
                contentType: false,
                beforeSend: sf.setModuleHeaders,
                success: function (data) {
                    if (data.success && data.files) {
                        for (var k = 0; k < data.files.length; k++) {
                            addServerFile(data.files[k].fileId, data.files[k].fileName);
                        }
                        // Refresh the file list dropdown
                        loadFiles($('#' + folderSelectId).val());
                    }
                },
                error: function () {
                    // Show error near the dropzone
                    var $dz = $('#' + dropzoneId);
                    var $msg = $('<div class="dnnFormMessage dnnFormWarning">Upload failed.</div>');
                    $dz.after($msg);
                    $msg.fadeOut(3000, function () { $msg.remove(); });
                }
            });
        }

        var $dz = $('#' + dropzoneId);
        var $fi = $('#' + uploadInputId);

        // Folder change loads files
        $('#' + folderSelectId).on('change', function () {
            loadFiles($(this).val());
        });

        // File select auto-adds on change
        $('#' + fileSelectId).on('change', function () {
            var $sel = $(this);
            var fid = $sel.val();
            var fname = $sel.find('option:selected').text();
            if (fid) {
                addServerFile(fid, fname);
                $sel.val(''); // reset to "None Specified"
            }
        });

        // Drop zone events
        $dz.on('dragover dragenter', function (e) {
            e.preventDefault();
            e.stopPropagation();
            $(this).addClass('dragover');
        }).on('dragleave', function (e) {
            e.preventDefault();
            e.stopPropagation();
            $(this).removeClass('dragover');
        }).on('drop', function (e) {
            e.preventDefault();
            e.stopPropagation();
            $(this).removeClass('dragover');
            var files = e.originalEvent.dataTransfer.files;
            if (files.length) uploadFiles(Array.prototype.slice.call(files));
        }).on('click', function (e) {
            if (!$(e.target).is('input')) $fi.click();
        });

        // File input change — immediate upload
        $fi.on('change', function () {
            if (this.files.length) {
                var files = [];
                for (var i = 0; i < this.files.length; i++) files.push(this.files[i]);
                uploadFiles(files);
                this.value = ''; // reset so same file can be selected again
            }
        });

        // Remove file from list
        $('#' + fileListId).on('click', '.attachment-item-remove', function () {
            $(this).closest('.attachment-item').remove();
        });
    }

    $(function () {
        $('.dnnAttachmentPicker').each(function () {
            initAttachmentPicker($(this));
        });
    });
}(jQuery));
