# Arsitektur FMR Vantage

```
 Robot / simulator                     FMR (proses FMR Vantage)
 ┌──────────────────┐   MQTT    ┌──────────────────────────────────────────────────────────┐
 │ adapter AGV/AMR  │◀────────▶ │ EmbeddedBroker ─ FleetCoordinator ─ ClearanceManager      │
 │  path, state     │ vantage/v1│                    (kontrak pesan)   ├ PathNormalizer      │
 │  enforce limitS  │           │                                       ├ IConflictDetector   │
 └──────────────────┘           │                                       └ reservation table   │
                                │ OccupancyOracle + RasterOracle  (bukti independen)          │
                                │ WPF Studio: workspace, segment table, trace, evaluation     │
                                └──────────────────────────────────────────────────────────┘
```

## Proyek

| Proyek | Isi | Bergantung pada |
|---|---|---|
| `src/Vantage.Core` | Geometri (SAT, footprint, area sapuan), normalisasi jalur → segmen, detektor konflik + oracle raster + ablasi node-ID, `ClearanceManager` (mesin keadaan Tabel II.1) | — (murni, dapat diuji) |
| `src/Vantage.Fleet` | Kontrak pesan, broker MQTT tertanam, `FleetCoordinator`, simulator dua robot, skenario Tabel III.2, oracle okupansi, runner eksperimen headless | Core, MQTTnet |
| `src/Vantage.Studio` | Aplikasi WPF (UI operator/engineer) | Core, Fleet |
| `tests/Vantage.Core.Tests` | Uji unit + uji terarah per skenario | Core, Fleet |
| `adapters/ros2` | Adapter ROS 2 ↔ MQTT untuk robot fisik | rclpy, paho-mqtt |
| `FMR.AisinAMR` | Purwarupa awal magang (referensi, tidak dipakai lagi) | — |

## Alur keputusan (Pers. III.1)

1. **Normalisasi** — `PathNormalizer` memvalidasi pose (berhingga, di dalam peta, tanpa duplikat), menyimpan semua vertex asal, memotong jalur di node rute (AGV) dan setiap `SegmentLength`, lalu membangun `SweptArea` = gabungan hull footprint+ε pada sampel 5 cm.
2. **Deteksi** — `ConflictGraph` dibangun dari semua segmen aktif robot berbeda. Detektor utama: irisan area sapuan (SAT). Pembanding: `RasterOracle` (grid 2 cm, kode terpisah) dan `NodeIdDetector` (ablasi).
3. **Blok konflik** — run segmen berkonflik yang bersambung, diperpanjang dengan segmen bebas sampai robot punya ruang keluar (panjang footprint + 2ε). Satu blok diberikan **atomik**, sehingga robot tidak pernah memegang sebagian koridor sambil menunggu sisanya.
4. **Grant** — di bawah satu lock: `Valid(i,r)` (robot terdaftar, misi/revisi aktif) ∧ `Compatible` (tidak ada reservasi robot lain pada segmen yang berkonflik) ∧ tidak mendahului permintaan lebih awal yang menginginkan ruang yang sama (FCFS, prioritas tetap sebagai tie-break; robot yang sudah memegang bagian blok selalu didahulukan).
5. **Okupansi & pelepasan** — progres > `StartS` → `Occupied`. Pelepasan memakai syarat pada `docs/MESSAGE_CONTRACT.md` butir 4. Masuk ke segmen `Waiting` dicatat sebagai pelanggaran urutan.
6. **Revisi** — izin yang belum dimasuki dicabut; segmen yang sedang ditempati menjadi *legacy* dan tetap direservasi sampai footprint keluar.

## Evaluasi

`SimulationEngine.RunHeadlessAsync` menjalankan skenario di bus in-process (deterministik). Kebenaran diambil dari:

* **oracle geometri** (`RasterOracle`): label konflik per pasangan segmen → TP/FP/TN/FN, FNR, FPR;
* **oracle okupansi** (`OccupancyOracle`): pose sebenarnya dari simulator → izin tidak kompatibel, masuk tanpa izin, tabrakan footprint.

Keputusan FMR tidak pernah dipakai sebagai label kebenaran. Baseline reservasi jalur penuh (`ClearanceMode.FullPathBaseline`) dan ablasi node-ID dijalankan pada skenario dan kondisi yang sama.
