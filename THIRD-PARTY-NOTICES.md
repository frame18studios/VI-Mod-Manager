# Third-party notices

VI Mod Manager is licensed under the MIT License in [LICENSE](LICENSE). The application also uses third-party components, which retain their own licenses. This document identifies the package versions resolved for the Windows x64 build; it does not replace each project's license or notice.

| Component | Resolved version | License | Source / license reference |
| --- | --- | --- | --- |
| Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent and Avalonia platform packages | 11.3.23 | MIT | [Avalonia](https://github.com/AvaloniaUI/Avalonia) |
| Avalonia.BuildServices | 11.3.2 | MIT | [Avalonia.BuildServices](https://github.com/AvaloniaUI/Avalonia.BuildServices) |
| Avalonia.Angle.Windows.Natives | 2.1.25547.20250602 | BSD 3-Clause | [ANGLE license](https://github.com/google/angle/blob/main/LICENSE) |
| HarfBuzzSharp and HarfBuzzSharp.NativeAssets.Win32 | 8.3.1.1 | MIT | [SkiaSharp repository](https://github.com/mono/SkiaSharp) |
| SkiaSharp and SkiaSharp.NativeAssets.Win32 | 2.88.9 | MIT | [SkiaSharp license](https://github.com/mono/SkiaSharp/blob/main/LICENSE.txt) |
| MicroCom.Runtime | 0.11.0 | MIT | [MicroCom](https://github.com/kekekeks/MicroCom) |
| Tmds.DBus.Protocol | 0.21.3 | MIT | [Tmds.DBus](https://github.com/tmds/Tmds.DBus) |

## Runtime and build components

- The self-contained **portable EXE** includes the .NET 10 runtime. Its license and third-party notices are maintained by Microsoft in the [.NET runtime repository](https://github.com/dotnet/runtime/tree/release/10.0).
- The **light EXE** requires the .NET 10 Desktop Runtime to be installed separately by the user. That runtime is governed by Microsoft's terms.
- `Microsoft.NET.ILLink.Tasks` is used during build and is not included as an application runtime dependency.
- `Avalonia.BuildServices` and the .NET SDK are build-time tools. Their telemetry runs only during development/build, not in the shipped EXEs. Set `DOTNET_CLI_TELEMETRY_OPTOUT=1` to opt out of .NET SDK telemetry when building.

Only the Windows x64 runtime assets are copied into the published EXEs. RID-specific packages for Linux, macOS, and WebAssembly are resolved during restore but are not shipped in the Windows release.

Package versions and licenses can change when dependencies are updated. Review the resolved dependency list and the corresponding upstream notices before publishing a new release. No third-party project listed here endorses VI Mod Manager. This file is provided for attribution and is not legal advice.
