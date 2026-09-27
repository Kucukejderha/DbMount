# ASCOS DbMount 1.1

[![License: AGPL v3](https://img.shields.io/badge/License-AGPL%20v3-blue.svg)](LICENSE)

ASCOS DbMount; bir klasördeki MSSQL veritabanı dosyalarını (MDF/NDF/LDF) SQL Server
örneklerine **toplu veya tek tek bağlamaya (mount/attach)** ve **ayırmaya
(unmount/detach)** yarayan, ASCOS kurumsal temasına sahip bir Windows masaüstü
uygulamasıdır.

## Ne yapar

- Veritabanı dosyalarının bulunduğu klasörü seçin; klasördeki tüm `.mdf`
  dosyaları boyutlarıyla listelenir.
- **Dosya seç…** ile bilgisayarın herhangi bir yerinden MDF dosyaları da
  listeye eklenebilir; dosyalar bulundukları klasörde yerinde bağlanır.
- Her dosya için sunucudaki durumu gösterilir: **Bağlı** veya **Bağlı değil**.
- Dosyaları tek tek işaretleyin veya **Tümünü seç** ile toplu işlem yapın.
- **Ekle** seçili dosyaları sunucuya bağlar; **Çıkar** seçili bağlı
  veritabanlarını sunucudan ayırır.
- Her işlemden önce **onay penceresi** açılır: işlenecek veritabanları,
  eylemleri ve dosya boyutları listelenir; onay vermeden hiçbir işlem başlamaz.
- İşlem öncesi **ön denetim** yapılır:
  - Çıkarma için: veritabanını kullanan etkin bağlantılar algılanır ve
    "Logo/Netsis gibi bir uygulama kullanıyor olabilir" uyarısı gösterilir.
  - Ekleme için: dosyanın başka bir uygulama veya SQL Server örneği
    tarafından kilitli olup olmadığı denetlenir.
- Dosyalar hiçbir zaman taşınmaz veya silinmez.
- Veritabanına ait tüm dosyalar (MDF + NDF + LDF) MDF başlığından otomatik
  algılanır; LDF'siz dosyalar için log dosyası yeniden oluşturulur.
- Çıkarma işleminde etkin bağlantılar `SINGLE_USER WITH ROLLBACK IMMEDIATE`
  ile kapatılır.

## Hata kaydı ve destek

- Tüm işlemler, bağlantı sonuçları ve hatalar ayrıntılı biçimde
  `%LOCALAPPDATA%\DbMount\DbMount.log` dosyasına yazılır (2 MB'ta
  `DbMount.previous.log` olarak döner). Parola asla kayda yazılmaz.
- **Kaydı aç** düğmesi bu günlüğü Not Defteri'nde açar.
- **Hata kaydı gönder** düğmesi, kullanıcı onayı alındıktan sonra günlüğü
  destek sunucusuna yükler. İşlem hata ile sonuçlandığında aynı onay sorusu
  otomatik olarak da sorulur.
- Gönderilen kayıt; uygulama sürümü, Windows sürümü, sunucu adı, dosya
  yolları ve işlem özetlerini içerir; parola içermez.

### Destek sunucusu kurulumu

`server/log-upload.php` dosyasını web sunucusuna kopyalayın (örn.
`rotaniz.com/ascos-araclar/dbmount/log-upload.php`). Uygulamanın gönderdiği
kayıtlar aynı dizinde `logs/` klasörü altında günlük dosyalara eklenir.
Uygulamanın kullandığı adres `Common.cs` içindeki `Common.UploadUrl`
değeridir; sunucu adresinize göre güncelleyip yeniden derleyin.

## Desteklenen sistemler

- Windows 10 1507 ve sonrası, Windows 11 (x86/x64)
- Klasik .NET Framework 4.x/CLR 4 — ayrıca .NET 8/Windows App SDK kurulumu
  **gerektirmez**, tek EXE dosyasıdır
- SQL Server 2005+ dosya biçimi (2008–2022 dahil) ve yerel/uzak tüm örnekler
- Windows kimlik doğrulaması veya SQL Server (sa) kullanıcı adı/parola

## Gereksinimler ve izinler

1. Hedef SQL Server örneğinde **sysadmin** yetkisi gerekir.
2. SQL Server hizmet hesabının (ör. `NT SERVICE\MSSQLSERVER`) dosyaların
   bulunduğu klasörlere **okuma/yazma NTFS izni** olmalıdır:
   ```
   icacls "D:\Veritabanlari" /grant "NT Service\MSSQLSERVER:(OI)(CI)F"
   ```
3. Veritabanı sürümü hedef sunucudan yeni olamaz (örn. SQL 2019 dosyası
   SQL 2017 örneğine eklenemez). Uygulama bu durumu Türkçe açıklar.

## Kullanım

1. **SQL Server** kutusundan yerel örneği seçin (örnekler otomatik taranır:
   `localhost`, `localhost\SQLEXPRESS` …) veya uzak sunucu adını yazın.
2. **Kullanıcı adı / Şifre** satırından kimlik doğrulamasını seçin; SQL auth
   için kullanıcı adı/parola girin. Parola, **Parolayı anımsa** işaretliyse
   Windows DPAPI ile şifrelenip `HKCU\SOFTWARE\ASCOS\DbMount` altında saklanır.
3. **Bağlantıyı sına** ile bağlantıyı doğrulayın.
4. **Dosya yolu** satırındaki **Klasör seç…** ile klasörü seçin veya
   **Dosya seç…** ile tek tek MDF dosyaları ekleyin.
5. Listeden dosyaları işaretleyin; **Ekle** veya **Çıkar** düğmesini kullanın.
6. Onay penceresinde işlem listesini ve uyarıları gözden geçirip **Devam et**
   deyin.
7. İşlemler arka planda sırayla yürütülür; sonuç her satırda ve alt durum
   çubuğunda görünür. Hata olursa Türkçe açıklamalı özet gösterilir ve hata
   kaydı gönderme seçeneği sunulur.

## Logo

- `ASCOS-DbMount.png` — 256 px saydam PNG (arayüz ve web sitesi için)
- `ASCOS-DbMount.ico` — 16–256 px çok boyutlu uygulama simgesi
- `logo-1024.png` — web sitesi yayını için yüksek çözünürlüklü kaynak
- Logoyu yeniden üretmek için: `powershell -File tools\make-logo.ps1`

## Kaynaktan derleme

Gereksinim: Windows 10/11 ve yerleşik `.NET Framework v4.0.30319\csc.exe`.

```bat
cd "ASCOS SQLdb"
build.cmd
```

Çıktılar `dist` klasöründe oluşur (`ASCOS-DbMount.exe`, logo, kılavuz, lisans).

## Güncelleme / kaldırma

Program kurulum gerektirmez; EXE'yi istediğiniz klasöre kopyalayın. Kayıtlı
bağlantı ayarlarını sıfırlamak için kayıt defterinden
`HKCU\SOFTWARE\ASCOS\DbMount` anahtarını silin. Önceki `ASCOS SQLdb`
sürümünün ayarları (`HKCU\SOFTWARE\ASCOS\SQLdb`) ilk çalıştırmada otomatik
taşınır.

## Tanılama

Bağlantı ve işlem hataları doğrudan arayüzde Türkçe açıklamalarıyla gösterilir;
ayrıntılar hata kaydına yazılır. Parola hiçbir yere yazılmaz.

## Lisans

Copyright (C) 2026 ASCOS DbMount contributors.

ASCOS DbMount, **GNU Affero General Public License v3.0** (`AGPL-3.0-only`)
altında yayımlanır. Tam koşullar için [LICENSE](LICENSE) dosyasına bakın.
Program hiçbir garanti verilmeden sunulur.

