# WD Sound

Website profil, katalog sewa, dan CMS untuk Wandi Sound System di Jawa Timur. Website publik tersedia dalam bahasa Indonesia dan Inggris. Dashboard menggunakan ASP.NET Core Identity dengan peran Admin dan Editor.

## Pengembangan

Prasyarat: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) sesuai `global.json`, Node.js 22 atau lebih baru, npm, dan SQL Server untuk basis data utama. Bootstrap 5.3.9 dan Alpine.js 3.17.5 dikunci melalui npm, lalu disalin ke aset aplikasi.

```sh
npm --prefix src/WDSound.Web ci --ignore-scripts
npm --prefix src/WDSound.Web run build
dotnet restore --locked-mode
dotnet build --no-restore
```

### Jalankan dengan SQL Server

Isi variabel lingkungan `ConnectionStrings__Catalog` dengan koneksi SQL Server yang sah. Aplikasi menerapkan migrasi EF Core saat startup. Berikan hak migrasi pada basis data yang dituju. Untuk produksi gunakan TLS yang tervalidasi. Isi `BootstrapAdmin__Email` dan `BootstrapAdmin__Password` lewat pengelola rahasia untuk membuat Admin pertama. Kata sandi minimal 12 karakter dan harus memenuhi aturan ASP.NET Core Identity. Hapus kedua variabel bootstrap setelah akun berhasil dibuat.

```sh
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://127.0.0.1:5182 \
  dotnet run --project src/WDSound.Web --no-build
```

Buka `/id` untuk website, `/en` untuk versi Inggris, dan `/admin` untuk dashboard. Gunakan basis data baru jika menjalankan pengujian HTTP pada SQL Server.

### Demo lokal

Mode Development dapat menggunakan SQLite. Tambahkan `Database__UseSqlite=true` dan, bila ingin katalog contoh, `Demo__Seed=true` pada perintah startup di atas. Demo hanya diisi saat tabel konten kosong. Data demo adalah contoh pengujian. Harga dan konten produksi diisi lewat CMS.

### Pengujian

```sh
dotnet test --no-restore
bash tests/run-smoke.sh
dotnet tool restore
dotnet ef migrations script --idempotent --project src/WDSound.Web -o /tmp/wdsound-migrations.sql
```

Smoke test memerlukan Bash, Python 3, curl, dan OpenSSL. Skrip menggunakan basis data SQLite sementara, atau SQL Server jika `ConnectionStrings__Catalog` diisi. GitHub Actions dikonfigurasi untuk keduanya.

## Pengelolaan konten

- Buat draf lewat dashboard. Editor dapat membuat dan memperbarui draf serta mengunggah media. Admin dapat menerbitkan dan mengelola akun, pengaturan, serta pesan masuk.
- Isi judul dan deskripsi Indonesia serta Inggris sebelum publikasi. Produk memerlukan kategori terbit dan gambar utama. Gambar pertama pada daftar ID menjadi gambar utama. Isi artikel disimpan sebagai teks dan di-escape saat ditampilkan.
- Harga mulai dari dihitung dari varian aktif, terpisah per satuan. Paket dapat mencantumkan komposisi dan jumlah, dengan harga yang dikonfirmasi melalui pertanyaan langsung sambil menunggu keputusan harga paket.
- WhatsApp dan email menggunakan nama item, jumlah, tanggal acara, dan varian jika dipilih. Formulir kontak mencatat nama dan pesan untuk Admin.
- Untuk beranda, gunakan konten Home dengan slug `home` sebagai hero. Gunakan `primary-cta` dan `secondary-cta` untuk tombol, atau slug bagian seperti `products` dan `gallery` untuk menyesuaikan judul.
- Publikasikan item navigasi dengan slug halaman internal untuk menyesuaikan label dan urutan. Tandai produk, paket, atau galeri untuk menampilkannya di beranda.

## Konfigurasi sebelum rilis

Atur `Site__Origin` ke origin HTTPS final agar canonical, hreflang, dan sitemap memakai domain yang benar. Isi kontak resmi, kode verifikasi Search Console, dan GA4 Measurement ID di dashboard. GA4 menyala setelah persetujuan pengunjung dan memakai event `view_product`, `view_package`, `contact_whatsapp_click`, serta `contact_email_click`, tanpa memasukkan isi pertanyaan ke parameter event.

Atur `Media__Root` dan `DataProtection__KeysPath` ke lokasi persisten dengan akses terbatas pada akun layanan. Simpan cadangan basis data SQL Server dan direktori media. Verifikasi pemulihan cadangan, tampilan perangkat, data dan foto resmi, persetujuan identitas merek, serta kebijakan privasi dan retensi sebelum produksi. Gambar panggung pada template adalah ilustrasi. Logo dan palet mengikuti arah desain PRD dan menunggu persetujuan.
