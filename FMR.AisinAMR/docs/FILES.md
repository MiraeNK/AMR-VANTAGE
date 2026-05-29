# File Reference — FMR.AisinAMR

Ringkasan file penting dan peran singkatnya:

- `App.xaml` — resource dictionary, tema MaterialDesign, warna dan style global.
- `Views/MainWindow.xaml` — shell aplikasi, sidebar navigasi, header dan host konten.
- `Views/DashboardView.xaml` — tampilan dashboard (metrik, log MQTT).
- `Views/FleetView.xaml` — tampilan daftar robot (placeholder saat ini).
- `Views/MapView.xaml` — tampilan peta (placeholder).
- `Views/SettingsView.xaml` — halaman konfigurasi (placeholder).

- `ViewModels/MainViewModel.cs` — pusat logika: inisialisasi MQTT services, event handling, navigasi, commands (Refresh, SendNavGoal, SendEStop, SendCancel).

- `Services/MqttServerService.cs` — embedded MQTT broker implementation. Menangani client connect/disconnect, publikasi, port handling.
- `Services/MqttClientService.cs` — managed client yang meng-handle subscribe/publish ke topik robot.

- `Models/RobotStatus.cs` — model observable untuk status robot (poses, battery, velocities, isOnline, dll.).
- `Models/MqttMessage.cs` — model untuk menyimpan log event MQTT di UI.
- `Converters/Converters.cs` — `PercentToWidthConverter`, `BoolToColorConverter`.
- `Converters/PageNameToVisibilityConverter.cs` — bantu navigasi view visibility berdasarkan `CurrentPage`.

- `FMR.AisinAMR.csproj` — project file: dependencies (`MaterialDesignThemes`, `MQTTnet`, `Newtonsoft.Json`, `CommunityToolkit.Mvvm`).

Catatan: jika ada file duplikat dari eksperimen sebelumnya, hapus file lama yang tidak dipakai (mis. duplikat `MqttServerService` di folder `Models/`).
