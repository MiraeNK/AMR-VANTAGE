# FMR AMR Dashboard Architecture

## 1. Tujuan
Aplikasi ini adalah dashboard Fleet Management Robot (FMR) untuk AMR.
Tujuannya:
- Memantau status robot realtime
- Menampilkan data baterai, posisi, dan status navigasi
- Mengelola perintah navigasi, cancel, dan emergency stop melalui MQTT
- Menyediakan arsitektur MVVM yang dapat diperluas

## 2. Package Utama
- `MQTTnet` dan `MQTTnet.Extensions.ManagedClient`: broker embedded + client MQTT
- `MaterialDesignThemes` dan `MaterialDesignColors`: UI tema Material Design
- `CommunityToolkit.Mvvm`: pattern MVVM, `ObservableObject`, `RelayCommand`
- `Newtonsoft.Json`: serialisasi payload MQTT

## 3. Struktur Proyek
```
FMR.AisinAMR/
├── Models/
│   ├── RobotStatus.cs
│   ├── MqttMessage.cs
│   ├── NavGoal.cs
│   ├── RobotIdentity.cs
├── Services/
│   ├── MqttServerService.cs
│   ├── MqttClientService.cs
│   └── NavigationService.cs (opsional)
├── Views/
│   ├── MainWindow.xaml
│   ├── DashboardView.xaml
│   ├── FleetView.xaml
│   ├── MapView.xaml
│   └── SettingsView.xaml
├── ViewModels/
│   ├── MainViewModel.cs
│   ├── DashboardViewModel.cs
│   ├── FleetViewModel.cs
│   ├── MapViewModel.cs
│   └── SettingsViewModel.cs
├── Converters/
├── Helpers/
└── FMR.AisinAMR.csproj
```

## 4. Sistem Kerja MQTT
1. `MqttServerService` menjalankan embedded broker lokal di port `1883`
2. `MqttClientService` terhubung ke broker sebagai client dan subscribe ke topik robot
3. Robot mengirim data ke topik:
   - `amr/{robot_id}/status/pose`
   - `amr/{robot_id}/status/health`
   - `amr/{robot_id}/event/arrived`
   - `amr/{robot_id}/event/error`
4. Dashboard menerima pesan melalui client dan memperbarui `ViewModel`
5. Aplikasi mengirim perintah ke robot menggunakan topik:
   - `amr/{robot_id}/cmd/goal`
   - `amr/{robot_id}/cmd/cancel`
   - `amr/{robot_id}/cmd/estop`

## 5. Rekomendasi Arsitektur
- Pisahkan `Views` dan `ViewModels` untuk setiap halaman
- Simpan `RobotStatus` sebagai model domain
- Gunakan service MQTT terpisah:
  - broker local
  - client untuk subscribe/publish
- Gunakan `NavigationService` untuk pengaturan halaman
- Tambahkan `SettingsView` untuk konfigurasi broker dan robot

## 6. Langkah Perbaikan yang sudah diterapkan
- Package `MaterialDesignColors` ditambahkan ke `FMR.AisinAMR.csproj`
- `MqttClientService` ditambahkan untuk memisahkan client MQTT dari broker
- Model data baru ditambahkan: `MqttMessage`, `NavGoal`, `RobotIdentity`
- Arsitektur untuk MQTT sekarang lebih modular

## 7. Catatan
Jika ingin memperluas ke dashboard penuh, langkah berikutnya adalah memecah konten `MainWindow.xaml` ke dalam user control:
- `DashboardView.xaml`
- `FleetView.xaml`
- `MapView.xaml`
- `SettingsView.xaml`

Dan gunakan `ContentControl` yang menampilkan `CurrentViewModel`.
