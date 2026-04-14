# Build Scripts

This folder contains MSBuild configuration files used to build and package the DNN Newsletters module.

## Files

| File | Description |
|------|-------------|
| `BuildProperties.props` | Build properties (DNN bin path, Visual Studio version/path) |
| `ModulePackage.targets` | MSBuild targets for creating DNN install and source packages |
| `MSBuild.Community.Tasks.Targets` | Community tasks used by the packaging targets (Zip, XmlRead, etc.) |

## Configuration

Edit `BuildProperties.props` to match your local environment:

```xml
<!-- Path to your DNN installation's bin folder -->
<DnnBinRoot>C:\DNN\mvc_test\bin\</DnnBinRoot>

<!-- Visual Studio version and install path -->
<VisualStudioVersion>18.0</VisualStudioVersion>
<VisualStudioInstallationRoot>C:\Program Files\Microsoft Visual Studio\18\Community\</VisualStudioInstallationRoot>
```

## How to Build

### VS Code

Press `Ctrl+Shift+B` to run the default build task. This is configured in `.vscode/tasks.json` and runs MSBuild with `Release` configuration.

### Visual Studio

Open `Dnn.Modules.Newsletters.sln` and build with `Ctrl+Shift+B` or **Build > Build Solution**.

### Command Line

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" Dnn.Modules.Newsletters.sln -p:Configuration=Release
```

## Packaging

When building in **Release** mode, the `ModulePackage.targets` automatically creates two DNN install packages in the `install/` folder:

- `dnn_Newsletters_<version>_Install.zip` — Install package (views, CSS, JS, resources, DLL, SQL providers, manifest)
- `dnn_Newsletters_<version>_Source.zip` — Source package (everything above plus source code, csproj, sln)

These zip files can be installed via the DNN **Extensions > Install Extension** page.
