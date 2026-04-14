# DNN Newsletters Module

A bulk email / newsletter module for DNN (formerly DotNetNuke). Originally one of the core admin modules distributed with DNN, now maintained as a standalone community module rebuilt on the DNN MVC pipeline with Razor views.

## Features

- Compose HTML or plain-text newsletters with CKEditor
- Send to DNN roles, individual users, or additional email addresses
- Token replacement for personalized emails (user name, profile fields, etc.)
- Multi-file attachments via server file picker and drag & drop upload
- Preview before sending
- Send synchronously or asynchronously (bulk)
- TO, BCC, or relay address modes
- Multi-language filtering
- Mobile-responsive UI

## Requirements

- DNN Platform 10.x+
- .NET Framework 4.8
- ASP.NET MVC 5

## Project Structure

```
Controls/              Controller (NewsletterViewControl.cs) and API
Components/            API controller, route mapper, upgrade controller
Models/                ViewModels (NewsletterViewModel, AttachmentPickerModel)
Views/                 Razor views and shared partials
Resources/css/         Stylesheets (module.css, attachment-picker.css)
Resources/js/          JavaScript (edit.js, attachment-picker.js)
App_LocalResources/    Localization resource files
Providers/             SQL data providers
BuildScripts/          MSBuild packaging targets
```

## Build

See [BuildScripts/README.md](BuildScripts/README.md) for build instructions and configuration.

## Test Mail Server

A Docker Compose file is included to run a local SMTP catch-all server (MailPit) for testing:

```powershell
docker compose -f docker-compose.mailserver.yml up -d
```

- **SMTP**: `localhost:1025`
- **Web UI**: http://localhost:8025

Configure DNN SMTP settings to point to `localhost` port `1025`.

## Contributing

If you would like to contribute to this project, please read [CONTRIBUTING.md](https://github.com/DNNCommunity/DNN.Newsletter/blob/master/.github/CONTRIBUTING.md)
