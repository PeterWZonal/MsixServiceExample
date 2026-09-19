Demo of a working .Net 10.0 Windows Service deployed with msix, plus an msix **modification package** that adds a plugin to it.
I was having trouble finding working examples and docs of how to do this.

Layout:
```
WindowsPackagingProject.sln                       root solution (all projects below)
BackgroundService\
  BackgroundService\                              the service (net10.0-windows10.0.19041.0, Microsoft.NET.Sdk.Worker)
  BackgroundService.Contracts\                    IPeriodicMessageSource - the plugin contract, shipped in the main package
  WindowsPackagingProject\                        main msix package (service registration, Identity 510bbcc0-...)
FactModification\
  FactPlugin\                                     plugin (net10.0 class library) implementing IPeriodicMessageSource
  FactModificationPackage\                        modification package that ships FactPlugin.dll to BackgroundService\Plugins\
```

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
   - WindowsPackagingProject_1.1.0.0_x64.appxsym
   - WindowsPackagingProject_1.1.0.0_x64.cer
   - WindowsPackagingProject_1.1.0.0_x64.msixbundle
 - Install the .net 10.0 runtime there **before** installing the package (otherwise the service fails its first auto-start and must be started manually).
   The app is built with `RollForward=Major`, so any newer installed major runtime will also work:
```
$env:DOTNET_CLI_TELEMETRY_OPTOUT="true"
$env:DOTNET_NOLOGO="true"
curl.exe https://dot.net/v1/dotnet-install.ps1  -L -o .\dotnet-install.ps1
powershell -ExecutionPolicy Bypass -File .\dotnet-install.ps1 -Channel 10.0 -Runtime dotnet -InstallDir "C:\Program Files\dotnet"
```
 - If using a self signed cert for the Msix package, trust it on the test machine:
   - Right click WindowsPackagingProject_1.1.0.0_x64.cer -> Install
   - Use options: Local Machine / Place in following store: Trusted People
 - Using powershell, install the msix with `Add-AppxPackage .\WindowsPackagingProject_1.1.0.0_x64.msixbundle`
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

## Modification package (FactModification)

The service logs a joke every 60 s. Installing the `FactModificationPackage` msix on top of it makes the *same* service also log a
fun fact every 45 s, without changing or reinstalling the main package. It is a plain msix
[modification package](https://learn.microsoft.com/windows/msix/modification-packages): `rescap6:ModificationPackage` in
`Properties`, a `uap4:MainPackageDependency` on the main package's identity name, the same `Publisher`, and **no** `Applications`
or `Capabilities`. Its only payload is `BackgroundService\Plugins\FactPlugin.dll` (+ `.deps.json`), mirroring the folder the
service exe lives in inside the main package.

How the plugin gets loaded:
 - `BackgroundService.Contracts` defines `IPeriodicMessageSource { Name; Interval; GetMessage(); }`. It ships in the main package.
   `FactPlugin` references it with `Private=false` / `ExcludeAssets=runtime` so the contract dll is *not* copied into the
   modification package - the plugin binds to the host's copy at runtime.
 - At startup `PluginLocator` looks for `*.dll` in two places and logs (Warning level, so it lands in the Application event log)
   what it finds in each:
   1. `<exe folder>\Plugins\` - the "merged view" of the main package folder.
   2. `<optional package InstalledLocation>\BackgroundService\Plugins\` for every optional/modification package registered
      against the main package. Registrations are found via `Windows.ApplicationModel.Package.Current.Dependencies` and, because
      that is empty in a service (see below), via `Windows.Management.Deployment.PackageManager`: find the users that have the
      main package family registered (`FindUsers`), enumerate their optional packages
      (`FindPackagesForUserWithPackageTypes(sid, PackageTypes.Optional)`) and keep those whose `AppxManifest.xml` has a
      `MainPackageDependency` naming the main package.
 - Each plugin dll is loaded in its own `AssemblyLoadContext` (`PluginLoadContext`, backed by `AssemblyDependencyResolver`) that
   returns `null` for the contract assembly so type identity is preserved. Every public, non-abstract `IPeriodicMessageSource`
   gets its own `PeriodicTimer` loop in `PluginHostService`, logging `[<Name>] <message>`.

Empirical findings (Windows 10 2004 / 19041 x64, service running as LocalSystem):
 - **The modification package's files are *not* visible through the main package's folder.** The service logs
   `Plugin discovery (local): directory does not exist: C:\Program Files\WindowsApps\510bbcc0-..._1.1.0.0_x64__1agf9ebjbgtd8\BackgroundService\Plugins`
   both before and after the modification package is installed. Files only exist under the modification package's own
   `InstalledLocation` (`C:\Program Files\WindowsApps\MsixServiceExample.FactModification_1.0.0.0_x64__1agf9ebjbgtd8\BackgroundService\Plugins\`),
   so the host has to resolve the package graph itself.
 - `Package.Current` works in the packaged service (it logs the full package name), but `Package.Current.Dependencies` is
   **empty** even when the modification package is installed: optional package registrations are per-user and the service runs
   as LocalSystem. `Get-AppxPackage` run as the installing user *does* list the modification package under the main package's
   `Dependencies`. The `PackageManager` enumeration described above finds it:
   `PackageManager: optional package MsixServiceExample.FactModification_1.0.0.0_x64__1agf9ebjbgtd8 (user S-1-5-21-...) targets us`
   followed by `Loaded plugin FactPlugin (interval 00:00:45) from ...\BackgroundService\Plugins\FactPlugin.dll`, and then
   `[FactPlugin] ...` lines every 45 s interleaved with the jokes every 60 s.
 - The main package must be installed first; installing the modification package alone fails with `0x80073D12`
   ("A main app package is required to install this optional package"). `Add-AppxPackage main.msixbundle` followed by
   `Add-AppxPackage mod.msixbundle` works, as does the single-step
   `Add-AppxPackage -Path main.msixbundle -ExternalPackages @('mod.msixbundle')` (not required, just convenient).
 - Plugins are discovered at service start only: after adding or removing the modification package run
   `Restart-Service BackgroundService` (the single-step install above auto-starts the service with the plugin already present).
 - **Removing the modification package with `Remove-AppxPackage` also removed the main package** on this OS build (the
   AppXDeployment-Server log shows both in the `removePackageList`, and the service disappeared). The docs say the main app
   should simply revert, so treat this as OS-build-specific and re-install the main package afterwards if you hit it.
   With only the main package installed the service starts cleanly, logs `No plugins loaded` and no errors.

Build notes for the modification package project (`FactModificationPackage.wapproj`):
 - No `ProjectReference`/`EntryPointProjectUniqueName`; the plugin output is pulled in with `Content` items whose `Link`
   metadata sets the in-package path (`BackgroundService\Plugins\%(Filename)%(Extension)`). A `ProjectReference` would instead
   drop the files into a `FactPlugin\` subfolder. A `BeforeTargets="_ConvertItems"` target (and a solution dependency) builds the
   plugin first.
 - The DesktopBridge tasks refuse to build a package without an application ("Project must have a reference to an
   application"); setting the `EntryPointExe` property to any value satisfies them and only produces the same two
   `Could not find the '/Package/Applications/Application@Executable|EntryPoint'` warnings as the main package.
 - `GenerateAppxPackageRecipe` demands either `runFullTrust` or an `mp:PhoneIdentity` element; the manifest carries the
   (inert) `PhoneIdentity` element so that no capability has to be declared.
 - `BackgroundService.Contracts.csproj` uses `TreatAsLocalProperty="TargetFramework;RuntimeIdentifier"` because the packaging
   targets forward the service's publish-profile properties as global properties when they walk project references, which
   otherwise fails with NETSDK1005 for the plain `net10.0` library.

Command line build of everything (from the repo root, x64 only, sideload packages signed with a test cert):
```
msbuild WindowsPackagingProject.sln /restore /t:Build /p:Configuration=Release /p:Platform=x64 ^
  /p:UapAppxPackageBuildMode=SideloadOnly /p:AppxBundle=Always /p:AppxBundlePlatforms=x64 ^
  /p:AppxPackageDir=<out>\ /p:PackageCertificateKeyFile=<cert.pfx> /p:PackageCertificatePassword=<pw> /p:AppxPackageSigningEnabled=true
```

Todo:
- Get platform agnostic ("any cpu") publish working while staying framework-dependent
 - Add precondition that dotnet runtime is installed 