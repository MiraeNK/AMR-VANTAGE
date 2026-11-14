# Kontrak Pesan `vantage/v1`

Transport: MQTT 3.1.1, QoS 1, broker tertanam di FMR (port 1883). Semua payload berupa JSON camelCase.
Prefix topik: `vantage/v1/{robotId}/…`

| Topik | Arah | Retain | Padanan VDA 5050 3.0 | Isi |
|---|---|---|---|---|
| `factsheet` | robot → FMR | ya | `factsheet` (subset) | `vehicleClass` (AGV/AMR), `footprint {front, rear, left, right}`, `priority` |
| `connection` | robot → FMR | ya, juga *last will* | `connection` | `connectionState`: ONLINE / OFFLINE / CONNECTIONBROKEN |
| `path` | robot → FMR | tidak | **ekstensi internal** (bukan topik VDA) | `missionId`, `revision`, `source`, `poses[] {x, y, theta, nodeId?}` |
| `state` | robot → FMR | tidak | `state` (subset + ekstensi) | `header.headerId` (urutan), `missionId` (= orderId), `revision` (= orderUpdateId), `agvPosition`, `velocity`, `pathProgress` (ekstensi), `driving` |
| `clearance` | FMR → robot | tidak | `responses` untuk zone RELEASE | `decisionNo`, `missionId`, `revision`, `status`, `segments[]`, `limitS`, `reason`, `leaseExpiry` (selalu null) |
| `command` | FMR → robot | tidak | `instantActions` (subset) | `pause` / `resume` |

`header` = `{headerId, timestamp, version "1.0.0", manufacturer, serialNumber, t}`.

## Aturan validitas (bukan tugas transport)

Broker yang berhasil mengantar pesan **tidak** membuktikan keputusan benar. Validitas diperiksa di aplikasi:

1. **Path** — revisi yang sama dengan revisi aktif → `Duplicate` (diabaikan, tidak ada keputusan baru). Revisi lebih kecil → `Stale` (ditolak). Revisi lebih besar → izin lama yang belum dimasuki dicabut (`REVOKED`), segmen yang sedang ditempati tetap direservasi sampai footprint keluar.
2. **State** — `headerId` ≤ yang terakhir diterima → duplikat, dibuang seluruhnya. State untuk revisi lama hanya memperbarui pose fisik (untuk pelepasan), tidak memajukan progres revisi baru.
3. **Clearance (di adapter)** — keputusan untuk revisi lain, atau dengan `decisionNo` ≤ yang terakhir diterapkan, diabaikan. Robot tidak boleh memajukan titik acuannya melewati `limitS` pada revisi aktif.
4. **Pelepasan** — tidak pernah berdasarkan waktu (`leaseExpiry` = null). Reservasi segmen dilepas hanya bila progres sudah melewati ujung segmen **dan** footprint + ε pada pose aktual tidak lagi menyentuh area sapuan robot lain yang berkonflik dengan segmen itu. Klaim progres saja (laporan keluar prematur) tidak melepas apa pun.

## Status `clearance`

| status | arti |
|---|---|
| `GRANTED` | blok segmen diizinkan; `limitS` maju |
| `QUEUED` | blok berkonflik dengan reservasi aktif atau permintaan yang lebih dulu; `reason` menyebut penyebabnya |
| `REVOKED` | izin yang belum dimasuki ditarik (revisi baru, atau segmen menjadi konflik karena jalur baru robot lain) |
| `REJECTED` | dicadangkan untuk path tidak valid |

## Contoh

```json
// vantage/v1/AMR-01/clearance
{"header":{"headerId":42,"timestamp":"2026-11-08T03:12:44Z","version":"1.0.0","manufacturer":"VANTAGE","serialNumber":"FMR","t":19.65},
 "decisionNo":9,"missionId":"M1","revision":0,"status":"GRANTED",
 "segments":["AMR-01:M1:r0:s002","…","AMR-01:M1:r0:s013"],"limitS":14.11,
 "reason":"Compatible with active reservations"}
```
