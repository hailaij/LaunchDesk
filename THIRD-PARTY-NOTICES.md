# Third-party notices

The service host references Microsoft `System.ServiceProcess.ServiceController`
10.0.0 and its transitive `System.Diagnostics.EventLog` dependency, under the MIT
license. Lockfiles pin the resolved packages. Licenses are bundled in
`runtime-notices/`.

The installer vendors Inno Setup's simplified Chinese translation. Attribution,
source URL and SHA-256 are recorded in `installer/Languages/README.md`.

Self-contained distributions include Microsoft .NET 10 runtime components,
Windows Desktop runtime components, and their transitive runtime assets. These
are obtained from Microsoft's official NuGet packages. Their license and
third-party notice files are bundled under `runtime-notices/` in release ZIPs.

Windows, WPF, WinForms, GDI, and Segoe icon fonts are platform technologies.
System fonts and Windows components are not copied into this source repository.

Official references:
- https://github.com/dotnet/runtime
- https://github.com/dotnet/wpf
- https://github.com/dotnet/winforms
