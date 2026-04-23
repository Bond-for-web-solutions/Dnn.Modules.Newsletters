(function ($) {
    'use strict';

    function htmlEncode(str) {
        return $('<div/>').text(str == null ? '' : String(str)).html();
    }

    // ----- Toast (singleton, shared across all module instances) ------------
    var toastTimer = null;
    var $toastContainer = null;
    var $currentToast = null;

    function ensureToastContainer() {
        if ($toastContainer && $toastContainer.length && $.contains(document.body, $toastContainer[0])) {
            return $toastContainer;
        }
        $toastContainer = $('<div class="nl-toast-container" role="region" aria-live="polite" aria-label="Notifications"></div>');
        $('body').append($toastContainer);
        return $toastContainer;
    }

    function toastIconChar(type) {
        if (type === 'success') return '\u2713';
        if (type === 'error') return '\u00D7';
        return '!';
    }

    function showToast(message, type) {
        if (!message) return;
        type = type || 'warning';
        var $c = ensureToastContainer();

        // Reset timer & remove the previous toast immediately so the new one
        // is shown right away (no fade-out delay between messages).
        if (toastTimer) { clearTimeout(toastTimer); toastTimer = null; }
        if ($currentToast) { $currentToast.remove(); $currentToast = null; }

        var $toast = $(
            '<div class="nl-toast nl-toast-' + type + '" role="alert">' +
                '<span class="nl-toast-icon" aria-hidden="true">' + toastIconChar(type) + '</span>' +
                '<span class="nl-toast-body"></span>' +
                '<button type="button" class="nl-toast-close" aria-label="Dismiss">\u00D7</button>' +
            '</div>'
        );
        $toast.find('.nl-toast-body').text(message);
        $toast.find('.nl-toast-close').on('click', function () { dismissToast(); });

        $c.append($toast);
        $currentToast = $toast;
        // Trigger entrance transition on next frame.
        requestAnimationFrame(function () { $toast.addClass('nl-toast-show'); });

        toastTimer = setTimeout(dismissToast, 3000);
    }

    function dismissToast() {
        if (toastTimer) { clearTimeout(toastTimer); toastTimer = null; }
        if (!$currentToast) return;
        var $t = $currentToast;
        $currentToast = null;
        $t.removeClass('nl-toast-show');
        setTimeout(function () { $t.remove(); }, 200);
    }

    function toastTypeFromCssClass(cssClass) {
        var c = String(cssClass || '');
        if (/success/i.test(c)) return 'success';
        if (/error/i.test(c)) return 'error';
        return 'warning';
    }

    function initTabs($module) {
        var $tabs = $module.find('.nl-tabs > .nl-tab');
        var $panels = $module.find('.nl-panel');
        $tabs.on('click keydown', function (e) {
            if (e.type === 'keydown' && e.key !== 'Enter' && e.key !== ' ') return;
            e.preventDefault();
            var $t = $(this);
            var target = $t.data('target');
            $tabs.removeClass('nl-tab-active').attr('tabindex', '-1');
            $t.addClass('nl-tab-active').attr('tabindex', '0');
            $panels.removeClass('nl-panel-active').attr('hidden', true);
            $module.find(target).addClass('nl-panel-active').removeAttr('hidden');
        });
    }

    function initNewsletter($module) {
        if ($module.data('newsletter-init')) return;
        $module.data('newsletter-init', true);

        var mid = parseInt($module.attr('data-moduleid'), 10);
        var sf = $.ServicesFramework(mid);
        var apiRoot = sf.getServiceRoot('Newsletters');

        var $form = $module.find('.nl-form').first();

        // Prevent default form submission — all actions go through AJAX
        $form.on('submit', function (e) { e.preventDefault(); });

        initTabs($module);

        // Token input for recipients
        initTokenInput($module, mid, sf);

        // Send method toggle
        $module.find('[name="SendMethod"]').on('change', function () {
            var show = $(this).val() === 'RELAY';
            var $relay = $module.find('.nl-relay-row');
            if (show) { $relay.removeAttr('hidden'); } else { $relay.attr('hidden', true); }
        });

        // Mark the CKEditor-bound textarea as required for assistive tech.
        $module.find('textarea[name="Message"]').attr('aria-required', 'true');

        // Set multipart encoding for file uploads on the outer WebForms form
        $('form#Form').attr('enctype', 'multipart/form-data').attr('encoding', 'multipart/form-data');

        // Preview button
        $module.find('.nl-btn-preview').on('click', function () {
            if (!validateSubjectMessage()) return;
            submitAction('Preview');
        });

        // Send button
        $module.find('.nl-btn-send').on('click', function () {
            if (!validateSubjectMessage()) return;
            var groups = countTokenRecipients();
            var emails = countAdditionalEmails();
            if (groups === 0 && emails === 0) {
                var noRecipientsMsg = $module.attr('data-text-norecipients')
                    || 'No recipients selected. Please add at least one recipient before sending.';
                showToast(noRecipientsMsg, 'error');
                return;
            }
            submitAction('Send');
        });

        function syncCkEditor() {
            if (typeof CKEDITOR === 'undefined' || !CKEDITOR.instances) return;
            try {
                for (var name in CKEDITOR.instances) {
                    if (CKEDITOR.instances[name] && typeof CKEDITOR.instances[name].updateElement === 'function') {
                        CKEDITOR.instances[name].updateElement();
                    }
                }
            } catch (e) { /* ignore CKEditor sync errors */ }
        }

        function validateSubjectMessage() {
            syncCkEditor();
            var subject = ($module.find('[name="Subject"]').val() || '').trim();
            var message = ($module.find('[name="Message"]').val() || '').trim();
            if (!subject || !message) {
                var msg = $module.attr('data-text-missingfields')
                    || 'Please enter both a subject and a message.';
                showToast(msg, 'error');
                return false;
            }
            return true;
        }

        function countAdditionalEmails() {
            var raw = $module.find('[name="AdditionalEmails"]').val() || '';
            if (!raw.trim()) return 0;
            return raw.split(/[;,\r\n]+/).filter(function (s) { return s.trim().length > 0; }).length;
        }

        function countTokenRecipients() {
            // tokenInput hides the original input and renders <li class="token-input-token-facebook"> entries.
            return $module.find('ul.token-input-list-facebook li.token-input-token-facebook').length;
        }

        // Cancel preview — purely client-side
        $module.find('.nl-btn-cancel-preview').on('click', function () {
            $module.find('.nl-preview').attr('hidden', true);
        });

        function submitAction(action) {
            // Sync CKEditor content to textarea (safe if CKEDITOR isn't loaded)
            syncCkEditor();

            // Double-submit prevention: ignore re-clicks while a request is in flight.
            if ($module.data('newsletter-busy')) {
                return;
            }
            $module.data('newsletter-busy', true);
            var $actionButtons = $module.find('.nl-btn-preview, .nl-btn-send').prop('disabled', true).attr('aria-disabled', 'true');
            $module.attr('aria-busy', 'true');
            // Only show the spinner for Send (which can take a while).
            // Preview is fast and the user explicitly asked not to show a loader for it.
            var showSpinner = action !== 'Preview';
            if (showSpinner) {
                $module.find('.nl-spinner').prop('hidden', false);
            }

            var data = collectFormData();

            $.ajax({
                url: apiRoot + 'NewsletterApi/' + action,
                type: 'POST',
                data: JSON.stringify(data),
                contentType: 'application/json; charset=utf-8',
                beforeSend: sf.setModuleHeaders,
                success: function (result) {
                    if (action === 'Preview' && result.previewVisible) {
                        $module.find('.nl-preview-subject').text(result.previewSubject);
                        $module.find('.nl-preview-body').html(result.previewBody);
                        $module.find('.nl-preview').removeAttr('hidden');
                        // Always show a confirmation toast for Preview so the user gets feedback.
                        var previewMsg = result.statusMessage
                            || $module.attr('data-text-previewready')
                            || 'Preview generated.';
                        var previewType = result.statusMessage
                            ? toastTypeFromCssClass(result.statusCssClass)
                            : 'success';
                        showToast(previewMsg, previewType);
                    } else {
                        $module.find('.nl-preview').attr('hidden', true);
                        // Always surface a toast for Send so the user always gets feedback.
                        var sendMsg = result.statusMessage;
                        var sendType = toastTypeFromCssClass(result.statusCssClass);
                        if (!sendMsg) {
                            if (action === 'Send') {
                                sendMsg = $module.attr('data-text-sendsuccess')
                                    || 'Newsletter sent successfully.';
                                sendType = result.success === false ? 'error' : 'success';
                            }
                        } else if (action === 'Send' && result.success === true && !/success|error|warning/i.test(String(result.statusCssClass || ''))) {
                            sendType = 'success';
                        }
                        if (sendMsg) {
                            showToast(sendMsg, sendType);
                        }
                    }
                },
                error: function (xhr) {
                    var msg = 'An error occurred';
                    try {
                        var err = JSON.parse(xhr.responseText);
                        if (err.statusMessage) msg = err.statusMessage;
                        else if (err.Message) msg = err.Message;
                    } catch (e) { /* ignore parse errors */ }
                    showToast(msg, 'error');
                },
                complete: function () {
                    // Always reset busy state so the user is never stuck if success/error doesn't fire.
                    $module.data('newsletter-busy', false);
                    $actionButtons.prop('disabled', false).removeAttr('aria-disabled');
                    $module.removeAttr('aria-busy');
                    $module.find('.nl-spinner').prop('hidden', true);
                }
            });
        }

        function normalizeStatusClass(serverClass) {
            // Translate any legacy DNN class returned by the API into our scoped classes.
            var c = String(serverClass || '');
            if (c.indexOf('Success') >= 0) return 'nl-msg nl-msg-success';
            if (c.indexOf('Error') >= 0) return 'nl-msg nl-msg-error';
            return 'nl-msg nl-msg-warning';
        }

        // Surface any server-rendered initial status as a toast on first load.
        var $initialStatus = $module.find('.nl-status > div').first();
        if ($initialStatus.length) {
            var initialMsg = $initialStatus.text().trim();
            if (initialMsg) {
                showToast(initialMsg, toastTypeFromCssClass($initialStatus.attr('class')));
            }
            $module.find('.nl-status').empty();
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
            // Kept for backwards compatibility; routes to the toast.
            if (message) showToast(message, toastTypeFromCssClass(cssClass));
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
                        .addClass('nl-msg nl-msg-warning')
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
        $('.nl-module').each(function () {
            initNewsletter($(this));
        });
    });
}(jQuery));
