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

- **VS Code**: Press `Ctrl+Shift+B`
- **Visual Studio**: Open `Dnn.Modules.Newsletters.sln` and build with `Ctrl+Shift+B` or `Build > Build Solution`
- **Command line**:

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" Dnn.Modules.Newsletters.sln -p:Configuration=Release
```

### DNN Bin Root

The project references DNN assemblies from a local DNN installation. Update `BuildScripts/BuildProperties.props` to point to your DNN `bin` folder:

```xml
<DnnBinRoot Condition="'$(DnnBinRoot)' == ''">C:\DNN\mvc_test\bin\</DnnBinRoot>
```

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
