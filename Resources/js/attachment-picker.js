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
        var addBtnId = prefix + '_AddBtn';
        var pendingFiles = [];

        function loadFiles(folderId) {
            var sf = $.ServicesFramework(mid);
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

        function formatSize(bytes) {
            if (bytes === 0) return '0 B';
            var k = 1024, sizes = ['B', 'KB', 'MB', 'GB'];
            var i = Math.floor(Math.log(bytes) / Math.log(k));
            return parseFloat((bytes / Math.pow(k, i)).toFixed(1)) + ' ' + sizes[i];
        }

        function addServerFile(fileId, fileName) {
            var $list = $('#' + fileListId);
            // Check duplicate by file ID
            if ($list.find('[data-file-id="' + fileId + '"]').length > 0) return;
            // Check duplicate by file name (could be a pending file with same name)
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

        function addPendingFile(file, index) {
            var $list = $('#' + fileListId);
            var $item = $('<div class="attachment-item" data-pending="' + index + '"></div>');
            $item.append('<span class="attachment-item-icon">&#128196;</span>');
            $item.append('<span class="attachment-item-name">' + htmlEncode(file.name) + '</span>');
            $item.append('<span class="attachment-item-badge">' + formatSize(file.size) + '</span>');
            $item.append('<button type="button" class="attachment-item-remove" title="Remove">&times;</button>');
            $list.append($item);
        }

        function syncFileInput() {
            var dt = new DataTransfer();
            for (var i = 0; i < pendingFiles.length; i++) {
                if (pendingFiles[i] !== null) dt.items.add(pendingFiles[i]);
            }
            document.getElementById(uploadInputId).files = dt.files;
        }

        function isDuplicatePending(file) {
            for (var i = 0; i < pendingFiles.length; i++) {
                if (pendingFiles[i] !== null &&
                    pendingFiles[i].name === file.name &&
                    pendingFiles[i].size === file.size) {
                    return true;
                }
            }
            return false;
        }

        function addFiles(fileArray) {
            for (var i = 0; i < fileArray.length; i++) {
                if (isDuplicatePending(fileArray[i])) continue;
                var idx = pendingFiles.length;
                pendingFiles.push(fileArray[i]);
                addPendingFile(fileArray[i], idx);
            }
            syncFileInput();
        }

        // Set enctype for file upload
        var $form = $picker.closest('form');
        $form.attr('enctype', 'multipart/form-data').attr('encoding', 'multipart/form-data');
        $('form#Form').attr('enctype', 'multipart/form-data').attr('encoding', 'multipart/form-data');

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
            if (files.length) addFiles(Array.prototype.slice.call(files));
        }).on('click', function (e) {
            if (!$(e.target).is('input')) $fi.click();
        });

        // File input change
        $fi.on('change', function () {
            if (this.files.length) {
                var files = [];
                for (var i = 0; i < this.files.length; i++) files.push(this.files[i]);
                addFiles(files);
            }
        });

        // Remove file from list
        $('#' + fileListId).on('click', '.attachment-item-remove', function () {
            var $item = $(this).closest('.attachment-item');
            var pi = $item.attr('data-pending');
            if (pi !== undefined) {
                pendingFiles[parseInt(pi)] = null;
                syncFileInput();
            }
            $item.remove();
        });
    }

    $(function () {
        $('.dnnAttachmentPicker').each(function () {
            initAttachmentPicker($(this));
        });
    });
}(jQuery));
