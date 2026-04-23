(function ($) {
    'use strict';

    function htmlEncode(str) {
        return $('<div/>').text(str == null ? '' : String(str)).html();
    }

    function initNewsletter($module) {
        if ($module.data('newsletter-init')) return;
        $module.data('newsletter-init', true);

        var mid = parseInt($module.attr('data-moduleid'), 10);
        var sf = $.ServicesFramework(mid);
        var apiRoot = sf.getServiceRoot('Newsletters');

        var $form = $module.find('.newsletter-form').first();

        // Prevent default form submission — all actions go through AJAX
        $form.on('submit', function (e) { e.preventDefault(); });

        // Tab initialization
        $module.dnnTabs();

        // Token input for recipients
        initTokenInput($module, mid, sf);

        // Send method toggle
        $module.find('[name="SendMethod"]').on('change', function () {
            $module.find('.relay-address-panel').toggle($(this).val() === 'RELAY');
        });

        // Mark the CKEditor-bound textarea as required for assistive tech.
        $module.find('textarea[name="Message"]').attr('aria-required', 'true');

        // Set multipart encoding for file uploads on the outer WebForms form
        $('form#Form').attr('enctype', 'multipart/form-data').attr('encoding', 'multipart/form-data');

        // Preview button
        $module.find('.btn-preview').on('click', function () {
            submitAction('Preview');
        });

        // Send button
        $module.find('.btn-send').on('click', function () {
            if (!confirmSend()) return;
            submitAction('Send');
        });

        function countAdditionalEmails() {
            var raw = $module.find('[name="AdditionalEmails"]').val() || '';
            if (!raw.trim()) return 0;
            return raw.split(/[;,\r\n]+/).filter(function (s) { return s.trim().length > 0; }).length;
        }

        function countTokenRecipients() {
            // tokenInput hides the original input and renders <li class="token-input-token-facebook"> entries.
            return $module.find('ul.token-input-list-facebook li.token-input-token-facebook').length;
        }

        function confirmSend() {
            var groups = countTokenRecipients();
            var emails = countAdditionalEmails();
            var template = $module.attr('data-text-confirmsend')
                || 'Send this newsletter to {0} recipient group(s) and {1} additional email address(es)? This action cannot be undone.';
            var noRecipientsMsg = $module.attr('data-text-norecipients')
                || 'No recipients selected. Send anyway?';
            var msg = (groups === 0 && emails === 0)
                ? noRecipientsMsg
                : template.replace('{0}', groups).replace('{1}', emails);
            return window.confirm(msg);
        }

        // Cancel preview — purely client-side
        $module.find('.btn-cancel-preview').on('click', function () {
            $module.find('.preview-panel').hide();
        });

        function submitAction(action) {
            // Sync CKEditor content to textarea
            for (var name in CKEDITOR.instances) {
                CKEDITOR.instances[name].updateElement();
            }

            // Double-submit prevention: ignore re-clicks while a request is in flight.
            if ($module.data('newsletter-busy')) {
                return;
            }
            $module.data('newsletter-busy', true);
            var $actionButtons = $module.find('.btn-preview, .btn-send').prop('disabled', true).attr('aria-disabled', 'true');
            $module.attr('aria-busy', 'true');
            $module.find('.newsletter-spinner').prop('hidden', false);

            var data = collectFormData();

            $.ajax({
                url: apiRoot + 'NewsletterApi/' + action,
                type: 'POST',
                data: JSON.stringify(data),
                contentType: 'application/json; charset=utf-8',
                beforeSend: sf.setModuleHeaders,
                success: function (result) {
                    if (action === 'Preview' && result.previewVisible) {
                        $module.find('.preview-subject-content').text(result.previewSubject);
                        $module.find('.preview-body-content').html(result.previewBody);
                        $module.find('.preview-panel').show();
                        showStatus('', '');
                    } else {
                        $module.find('.preview-panel').hide();
                        showStatus(result.statusMessage, result.statusCssClass);
                    }
                },
                error: function (xhr) {
                    var msg = 'An error occurred';
                    try {
                        var err = JSON.parse(xhr.responseText);
                        if (err.statusMessage) msg = err.statusMessage;
                        else if (err.Message) msg = err.Message;
                    } catch (e) { /* ignore parse errors */ }
                    showStatus(msg, 'dnnFormMessage dnnFormError');
                },
                complete: function () {
                    // Always reset busy state so the user is never stuck if success/error doesn't fire.
                    $module.data('newsletter-busy', false);
                    $actionButtons.prop('disabled', false).removeAttr('aria-disabled');
                    $module.removeAttr('aria-busy');
                    $module.find('.newsletter-spinner').prop('hidden', true);
                }
            });
        }

        function collectFormData() {
            return {
                Recipients: $module.find('[name="Recipients"]').val() || '',
                AdditionalEmails: $module.find('[name="AdditionalEmails"]').val() || '',
                Subject: $module.find('[name="Subject"]').val() || '',
                Message: $module.find('[name="Message"]').val() || '',
                From: $module.find('[name="From"]').val() || '',
                ReplyTo: $module.find('[name="ReplyTo"]').val() || '',
                RelayAddress: $module.find('[name="RelayAddress"]').val() || '',
                Priority: $module.find('[name="Priority"]').val() || '2',
                SendMethod: $module.find('[name="SendMethod"]').val() || 'TO',
                SendAction: $module.find('[name="SendAction"]:checked').val() || 'A',
                ReplaceTokens: $module.find('[name="ReplaceTokens"]').is(':checked'),
                IsHtmlMessage: true,
                SelectedLanguages: $module.find('[name="SelectedLanguages"]:checked').map(function () {
                    return this.value;
                }).get(),
                AttachmentFileIds: $module.find('[name="AttachmentFileIds"]').map(function () {
                    return parseInt(this.value, 10);
                }).get()
            };
        }

        function showStatus(message, cssClass) {
            var $status = $module.find('.status-area');
            if (message) {
                $status.html('<div class="' + cssClass + '">' + htmlEncode(message) + '</div>').show();
            } else {
                $status.empty().hide();
            }
        }
    }

    function initTokenInput($module, mid, sf) {
        var searchUrl = sf.getServiceRoot('InternalServices') + 'MessagingService/Search';
        var $recipients = $module.find('[name="Recipients"]');

        var initialEntries = [];
        try {
            var entriesAttr = $module.attr('data-initial-entries');
            if (entriesAttr) initialEntries = JSON.parse(entriesAttr);
        } catch (e) { /* ignore parse errors */ }

        var noResultsText = $module.attr('data-text-noresults') || 'No results';
        var searchingText = $module.attr('data-text-searching') || 'Searching...';

        if (!$recipients.data('tokenInputObject')) {
            $recipients.tokenInput(searchUrl, {
                theme: 'facebook',
                prePopulate: initialEntries,
                minChars: 2,
                preventDuplicates: true,
                hintText: '',
                noResultsText: noResultsText,
                searchingText: searchingText,
                resultsFormatter: function (item) {
                    var name = htmlEncode(item.name);
                    var icon = htmlEncode(item.iconfile);
                    if (item.id && String(item.id).indexOf('user-') === 0) {
                        return "<li class='user'><img src=\"" + icon + "\" title=\"" + name + "\" height='25' width='25' /><span>" + name + "</span></li>";
                    }
                    if (item.id && String(item.id).indexOf('role-') === 0) {
                        return "<li class='role'><img src=\"" + icon + "\" title=\"" + name + "\" height='25' width='25' /><span>" + name + "</span></li>";
                    }
                    return '<li>' + name + '</li>';
                },
                onError: function (xhr, status) {
                    var messageNode = $('<div/>')
                        .addClass('dnnFormMessage dnnFormWarning')
                        .text('An error occurred while getting suggestions: ' + status);
                    $recipients.before(messageNode);
                    messageNode.fadeOut(3000, 'easeInExpo', function () {
                        messageNode.remove();
                    });
                }
            });
        }

        // Update label target for token input
        var recipientId = $recipients.attr('id');
        if (recipientId) {
            $module.find('label[for="' + recipientId + '"]').attr('for', 'token-input-' + recipientId);
        }
    }

    $(function () {
        $('.dnnNewsletters').each(function () {
            initNewsletter($(this));
        });
    });
}(jQuery));
