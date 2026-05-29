# FMR.AisinAMR — Fleet Management (Dokumentasi Ringkas)

Ringkasan:
- Aplikasi WPF (.NET 10) untuk monitoring dan kontrol AMR (Autonomous Mobile Robots).
- Arsitektur: MVVM (CommunityToolkit.Mvvm), MaterialDesign UI, MQTT broker (embedded) + managed MQTT client.

Fitur utama:
- Embedded MQTT broker (`MqttServerService`) untuk menerima koneksi robot.
- Managed MQTT client (`MqttClientService`) untuk subscribe/publish pesan.
- Dashboard real-time menampilkan status robot, battery, pose, dan log MQTT.
- Modular Views: `DashboardView`, `FleetView`, `MapView`, `SettingsView`.

Quickstart (pengembangan):
1. Pastikan .NET 10 SDK terpasang.
2. Restore & build:

```powershell
dotnet restore
dotnet build
```

3. Jalankan:

```powershell
dotnet run --project FMR.AisinAMR.csproj
```

Catatan: jika exe terkunci saat build, hentikan proses `FMR.AisinAMR` yang berjalan.

Lokasi dokumentasi tambahan:
- `docs/ARCHITECTURE.md` — arsitektur, diagram, topik MQTT
- `docs/FILES.md` — daftar file penting dan peran
- `docs/USAGE.md` — panduan penggunaan, konfigurasi, troubleshooting
