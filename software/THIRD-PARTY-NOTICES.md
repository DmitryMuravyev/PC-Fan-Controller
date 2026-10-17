# Third-Party Notices

Fan Controller uses the third-party components listed below. Each component is
licensed by its authors under the license given here; the full license texts are
available at the linked project pages and in the respective NuGet packages.

## Service (FanController.Service)

| Component | License | Project |
|---|---|---|
| LibreHardwareMonitorLib | MPL-2.0 | https://github.com/LibreHardwareMonitor/LibreHardwareMonitor |
| DiskInfoToolkit (dependency of LibreHardwareMonitorLib) | MPL-2.0 | https://github.com/Blacktempel/DiskInfoToolkit |
| RAMSPDToolkit-NDD (dependency of LibreHardwareMonitorLib) | MPL-2.0 | https://github.com/Blacktempel/RAMSPDToolkit |
| HidSharp | Apache-2.0 | https://software.seekye.com/hidsharp |
| protobuf-net, protobuf-net.Grpc, protobuf-net.Grpc.AspNetCore | Apache-2.0 | https://github.com/protobuf-net/protobuf-net |
| gRPC for .NET (Grpc.AspNetCore.Server, Grpc.Core.Api, Grpc.Net.Common) | Apache-2.0 | https://github.com/grpc/grpc-dotnet |
| Serilog, Serilog.AspNetCore, Serilog.Expressions, Serilog.Sinks.File, Serilog.Sinks.Debug | Apache-2.0 | https://github.com/serilog/serilog |
| Microsoft.Extensions.Hosting.WindowsServices / .Systemd, ASP.NET Core, .NET runtime | MIT | https://github.com/dotnet |

The LibreHardwareMonitorLib components are used unmodified, as NuGet packages.
Their source code is available at the project pages above (MPL-2.0 section 3.2).

### PawnIO driver (Windows)

On Windows, LibreHardwareMonitorLib accesses CPU, chipset and Super I/O sensors
through the PawnIO driver (https://pawnio.eu, https://github.com/namazso/PawnIO.Setup).
PawnIO is **not** included in this repository or in the published packages: the
service installation script downloads the official, digitally signed installer from
the PawnIO project and runs it, unless the driver is already installed. PawnIO is
licensed by its author under its own terms; see the PawnIO project pages.

## Client application (FanController)

| Component | License | Project |
|---|---|---|
| Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Controls.DataGrid | MIT | https://github.com/AvaloniaUI/Avalonia |
| Avalonia.Fonts.Inter (Inter typeface: SIL Open Font License 1.1) | MIT / OFL-1.1 | https://github.com/AvaloniaUI/Avalonia, https://github.com/rsms/inter |
| CommunityToolkit.Mvvm | MIT | https://github.com/CommunityToolkit/dotnet |
| Xaml.Behaviors.Avalonia | MIT | https://github.com/wieslawsoltes/Xaml.Behaviors |
| protobuf-net, protobuf-net.Grpc, protobuf-net.Grpc.ClientFactory | Apache-2.0 | https://github.com/protobuf-net/protobuf-net |
| gRPC for .NET (Grpc.Net.Client, Grpc.Net.ClientFactory, Grpc.Core.Api) | Apache-2.0 | https://github.com/grpc/grpc-dotnet |
| .NET runtime | MIT | https://github.com/dotnet |

AvaloniaUI.DiagnosticsSupport is referenced for Debug builds only and is not part
of the released application.

## Shared contracts (FanController.Contracts)

| Component | License | Project |
|---|---|---|
| protobuf-net, protobuf-net.Grpc | Apache-2.0 | https://github.com/protobuf-net/protobuf-net |
