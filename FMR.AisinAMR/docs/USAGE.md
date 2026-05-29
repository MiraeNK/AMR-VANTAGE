# Usage & Troubleshooting

Persyaratan
- .NET 10 SDK
- (Opsional) Visual Studio 2022/2023 atau VS Code

Menjalankan aplikasi

```powershell
dotnet restore
dotnet build
dotnet run --project FMR.AisinAMR.csproj
```

Debug & common issues
- Error: file `FMR.AisinAMR.exe` terkunci saat build
  - Solusi: hentikan proses yang menjalankan exe, misal di Windows Task Manager, atau dari PowerShell:

```powershell
Get-Process -Name FMR.AisinAMR -ErrorAction SilentlyContinue | Stop-Process -Force
```

- Port MQTT bentrok
  - `MqttServerService` mencoba port default; jika port sudah dipakai, service akan mencari port alternatif atau laporkan error. Periksa log/exception saat startup.

- Build error terkait XAML (mis. malformed tags)
  - Pastikan tidak ada tag XAML yang tertutup ganda (`/>` lalu `</TextBlock.Style>`). Gunakan editor yang menyorot struktur XAML.

Konfigurasi
- Untuk mengubah topik atau port, buka `Services/MqttServerService.cs` dan `Services/MqttClientService.cs` dan sesuaikan konstanta atau pembacaan konfigurasi.

Menerapkan ke produksi
- Gunakan broker MQTT terpisah (contoh Mosquitto) dan matikan embedded broker.
- Aktifkan TLS, autentikasi client, dan policy topic untuk keamanan.

Pertanyaan lanjutan
- Mau saya generate diagram lebih rinci (sequence, component), atau contoh file konfigurasi `appsettings.json` untuk memuat port/topik secara dinamis?