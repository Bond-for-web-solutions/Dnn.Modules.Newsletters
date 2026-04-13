$(function () {

    //var portalId = $('#dnnEditHtml').attr('data-portalid');
    //var tabId = $('#dnnEditHtml').attr('data-tabid');
    var moduleId = $('#dnnNewsletters').attr('data-moduleid');

    var editorConfigeditortxtContent = {};
    if (window['editorConfigeditor' + moduleId])
        editorConfigeditortxtContent = window['editorConfigeditor' + moduleId];

    CKEDITOR.replace('Message', editorConfigeditortxtContent);

    //var initPage = function () {
    //
    //    $('#dnnEditHtml form').ajaxForm({
    //        success: function () {
    //           window.location = $('#dnnEditHtml').attr('data-returnurl');
    //        },
    //        beforeSerialize: function () {
    //            for (var instanceName in CKEDITOR.instances)
    //                CKEDITOR.instances[instanceName].updateElement();
    //        }
    //    });


        //$('#cmdEdit').click(function () {
        //   var action = $(this).attr('data-action');
        //    $('#dnnEditHtml form').ajaxSubmit({
        //        url: action,
        //        target: '#dnnEditHtml',
        //        success: function () {
        //            initPage();
        //            CKEDITOR.replace('EditorContent', editorConfigeditortxtContent);
        //        },
        //    });
        //    // return false to prevent normal browser submit and page navigation
        //    return false;
        //});

    //}
    //initPage();
});