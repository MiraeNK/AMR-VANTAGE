# FMR VANTAGE — Logo concept 01

Status: KONSEP UNTUK DITINJAU. Belum dipasang ke aplikasi.
Tanggal: 8 November 2026.

## Arah desain
Simbol dua titik robot dan dua jalur yang bergabung menggambarkan koordinasi armada AGV/AMR pada segmen bersama.
Wordmark lengkap FMR VANTAGE menggunakan huruf geometris miring untuk kesan engineering dan gerak terarah.
Referensi suasana industrial: SolidWorks, HIKROBOT, SICK, Endress+Hauser. Bentuk logo dibuat tersendiri.

## File
- fmr-vantage-primary.svg / .png: logo utama biru + navy, latar transparan.
- fmr-vantage-white.svg / .png: logo putih untuk latar gelap, transparan.
- fmr-vantage-monochrome.svg / .png: logo satu warna navy, transparan.
- fmr-vantage-symbol.svg / .png: simbol saja untuk penggunaan kecil.
- preview.png: lembar pratinjau di latar terang/gelap; bukan master logo.
- source/imagegen-exploration.png: konsep awal dari built-in image_gen.
- source/prompt.txt: prompt eksplorasi asli.
- source/build-logo.cjs: sumber pembuatan SVG final dan ekspor PNG.

SVG final berisi path dan bentuk vektor; tidak menanam bitmap dan tidak memerlukan font eksternal.
PNG logo horizontal: 3600 x 960 px; simbol: 1024 x 1024 px.
PNG final diekspor langsung dari SVG sehingga geometri kedua format konsisten.
Warna: Industrial blue #075BCC, midnight navy #071D39, white #FFFFFF.
Sisakan ruang kosong di sekitar logo; jangan diregangkan atau mengubah rasio.
Pratinjau hanya ilustrasi pemakaian, tanpa perubahan source aplikasi.

## Proses
Eksplorasi visual dibuat memakai built-in image_gen, kemudian digambar ulang sebagai geometri SVG bersih.
PNG final dirender dari SVG menggunakan sharp. Tidak menggunakan fallback API/CLI.

