$(function () {
    $('.nl-module').each(function () {
        var $module = $(this);
        var moduleId = $module.attr('data-moduleid');

        var editorConfigeditortxtContent = {};
        if (window['editorConfigeditor' + moduleId])
            editorConfigeditortxtContent = window['editorConfigeditor' + moduleId];

        CKEDITOR.replace('Message', editorConfigeditortxtContent);
    });
});
