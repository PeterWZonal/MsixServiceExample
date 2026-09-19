Demo of a working .Net 6.0 Windows Service deployed with msix.
I was having trouble finding working examples and docs of how to do this.

Instructions to build and test locally:
 - Open in visual studio (I used VS2022)
 - Select Release configuration
 - Build BackgroundService (ignore any warnings)
 - Publish BackgroundService
 - Right-click WindowsPackagingProject and select Publish->Create App Packages
 - Go through the wizard
   - Select/create a signing certificate
 - Publish should succeed
 - Copy published files to the deployment target
   - WindowsPackagingProject_1.0.1.0_x64.appxsym
   - WindowsPackagingProject_1.0.1.0_x64.cer
   - WindowsPackagingProject_1.0.1.0_x64.msixbundle
 - Install .net 6.0 runtime there:
```
$env:DOTNET_CLI_TELEMETRY_OPTOUT="true"
$env:DOTNET_NOLOGO="true"
curl.exe https://dot.net/v1/dotnet-install.ps1  -L -o .\dotnet-install.ps1
.\dotnet-install -Channel LTS -runtime "aspnetcore" -InstallDir "C:\Program Files\dotnet"
.\dotnet-install -Channel LTS -runtime "windowsdesktop" -InstallDir "C:\Program Files\dotnet"
```
 - If using a self signed cert for the Msix package, trust it on the test machine:
   - Right click WindowsPackagingProject_1.0.1.0_x64.cer -> Install
   - Use options: Local Machine / Place in following store: Trusted People
 - Using powershell, install the msix with `Add-AppxPackage .\WindowsPackagingProject_1.0.1.0_x64.msixbundle`
 - Check the application event log to verify its running

The main problems I encountered were:
 - Packaging an app that doesn't require an entry in the Start Menu (i.e. its just a background service that always runs, other apps connect to it) gives a non-working start menu entry by default.
   - Fix is to remove the default attributes from the template: `Executable="$targetnametoken$.exe" EntryPoint="$targetentrypoint$"`, and add a StartPage attribute (which doesn't have to be valid)
   - However, this causes the following warnings when packaging:
     - `Could not find the '/Package/Applications/Application@Executable' attribute. Please add it back to the app manifest file.`
     - `Could not find the '/Package/Applications/Application@EntryPoint' attribute. Please add it back to the app manifest file.`

 - .Net project target will be deployed as self-contained (containing the whole .net framework) by default, even though you may have already set a default "folder" publish profile. To deploy as framework-dependent:
   - The publish profile must be selected under <PackagingProject> -> Dependencies -> Applications -> <App Name> -> Properties -> Publishing Profile. Help for this in VS2019 says "Only for .NET Core 3 projects" but works with higher.
   - The selected build configuration in Visual Studio must match the bitness of the publish profile (I'm using only Release | x64):
     - In the .Net project, check Publish -> Create a folder profile / select one -> Show all settings -> Check Configuration is "Release | x64"
     - Select the packaging project, Publish -> Create App Packages -> Select sideloading and select architectures that match the publish profile configuration

 - Documentation on how to configure a service in an Msix package from scratch is pretty thin e.g. https://docs.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-desktop6-service. How I did this:
   - Create a project from VS2019 template "Windows Application Packaging Project" 
   - In `Package.appxmanifest`, add the `desktop6` namespace attribute on `<Package>`:
       `xmlns:desktop6="http://schemas.microsoft.com/appx/manifest/desktop/windows10/6"`
   - Also in `Package.appxmanifest`, add the service extension child element of `<Package>`:
      <Extensions>
        <desktop6:Extension Category="windows.service" EntryPoint="Windows.FullTrustApplication" Executable="BackgroundService\BackgroundService.exe">
              <desktop6:Service Name="BackgroundService" StartupType="auto" StartAccount="localSystem"/>
        </desktop6:Extension>
      </Extensions>
   - There's also `desktop7:Service` but this causes the service registartion to be skipped on Windows 10 

 - I did see some claims that localSystem services aren't supported but it works when I tested it (I assume these caveats were for publishing via Windows Store)

Todo:
- Get platform agnostic ("any cpu") publish working while staying framework-dependent
 - Add precondition that dotnet runtime is installed 