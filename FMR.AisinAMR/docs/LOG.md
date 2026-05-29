# LOG AKTIVITAS PENGEMBANGAN

## Ringkasan penuh proses yang sudah dilakukan

### 1. Analisis awal
- Projek WPF `.NET 10` dengan UI MaterialDesign, MQTT, dan MVVM.
- Tujuan utama: perbaiki build errors, stabilkan arsitektur MQTT, dan pecah UI menjadi view modular.

### 2. Perbaikan XAML dan struktur UI
- Membuat `Views/DashboardView.xaml` sebagai tampilan dashboard terpisah.
- Membuat `Views/FleetView.xaml`, `Views/MapView.xaml`, dan `Views/SettingsView.xaml` untuk menyiapkan fragment UI terpisah.
- Menambahkan namespace view `xmlns:views="clr-namespace:FMR.AisinAMR.Views"` ke `Views/MainWindow.xaml`.
- Mengganti konten dashboard monolitik di `MainWindow.xaml` dengan empat kontrol view yang muncul/tersembunyi berdasar `CurrentPage`.
- Menambahkan `Convert` resources di `App.xaml`:
  - `PercentToWidthConverter`
  - `PageNameToVisibilityConverter`

### 3. Validasi dan perbaikan error XAML
- Deteksi dan perbaiki error XAML di `MainWindow.xaml` akibat markup `TextBlock.Style` yang salah.
- Memperbaiki tag `TextBlock` yang terbuka/tutup tidak valid di dalam `MainWindow.xaml`.
- Memastikan semua style dan resource converter terdaftar dengan benar di `App.xaml`.

### 4. Perbaikan kode C# dan duplikasi
- Menemukan duplikat `Services/MqttServerService.cs` yang juga muncul di `Models/`.
- Menghapus file duplikat `Models/MqttServerService.cs` agar hanya ada satu definisi service.

### 5. Dokumentasi internal
- Membuat dokumentasi terpusat di folder `docs/`:
  - `docs/README.md` — overview, fitur, quickstart.
  - `docs/ARCHITECTURE.md` — arsitektur software, komponen, data flow, MQTT topics.
  - `docs/FILES.md` — deskripsi file penting dan peran tiap file.
  - `docs/USAGE.md` — penggunaan, build, troubleshooting, catatan produksi.
- Menyusun semua catatan ke dalam satu log tunggal `docs/LOG.md` ini.

### 6. Verifikasi build
- Menjalankan `dotnet build` dan memastikan build sukses setelah perbaikan.
- Menghentikan proses `FMR.AisinAMR` yang mengunci output ketika diperlukan.
- Verifikasi akhir: `dotnet build` sukses untuk proyek `FMR.AisinAMR`.

### 7. Status saat ini
- UI telah dipisah ke view modular.
- Arsitektur MQTT telah distabilkan secara logis melalui service dan event handling.
- Dokumen ringkas dan panduan sudah ditambahkan ke workspace.
- Build berhasil tanpa error setelah perbaikan.

---

Jika Anda ingin, saya bisa lanjut menyatukan log ini dengan `README.md` utama atau membuat entry `CHANGELOG` yang lebih formal.