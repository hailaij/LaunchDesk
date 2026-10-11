# Third-party notices

LaunchDesk source code has no third-party NuGet PackageReference dependencies.

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
