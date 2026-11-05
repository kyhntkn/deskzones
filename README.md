# DeskZones

Masaüstünü isimli, yarı saydam bölmelere ayıran Windows programı.

- Bölmelerin konum, boyut ve içeriği hatırlanır (`%AppData%\DeskZones\config.json`)
- Masaüstüne düşen yeni dosyalar kurallara göre ilgili bölmeye girer, alt alta/yan yana dizilir
- Simgeleri bölmeler arasında ve bölme içinde sürükleyip sıralayabilirsin
- Bölme adı: başlığa çift tıkla. Sağ tık: kurallar, varsayılan bölme, yeni/sil
- Sistem tepsisi simgesinden: yeni bölme, masaüstü simgelerini göster/gizle, çıkış

## Kurallar

Bölmenin sağ tık menüsünden "Otomatik yerleştirme kuralları": `.pdf, .docx` gibi uzantılar,
klasörler için `[klasör]`. Hiçbir kurala uymayan öğe, ★ işaretli varsayılan bölmeye girer.

## Yerelde çalıştırma

    dotnet run --project src/DeskZones

(.NET 8 SDK gerekir.)

## GitHub'da derleme

Etiket gönder, Actions `.exe` ve `.msix` üretip Releases'e koyar:

    git tag v1.0.0
    git push origin v1.0.0

## Microsoft Store

1. Partner Center'da uygulama adını ayır.
2. `packaging/AppxManifest.xml` içindeki kimlik bilgileri Partner Center değerleriyle dolduruldu.
3. Release'teki `DeskZones.msix` dosyasını gönderime yükle (Store paketi kendisi imzalar).
4. `runFullTrust` yeteneği için sertifikasyonda gerekçe iste: "Masaüstü simgelerini bölmelere ayıran
   masaüstü düzenleyici; Explorer masaüstü penceresiyle etkileşir."

Kendi bilgisayarında MSIX'i denemek için imzalaman gerekir; .exe ile test etmek daha kolay.

## Logo

`design/logo.svg`, `design/logo.png`. Yeniden üretmek için: `python design/make_logo.py` (Pillow + numpy).

## Bilinen sınırlar

- "Masaüstünü göster" (Win+D) bölmeleri de gizleyebilir.
- Simgeler gizliyken masaüstüne sağ tık menüsü yine çalışır, ama masaüstündeki öğeleri sadece bölmelerden görürsün.
- MSIX sürümünde "Windows ile başlat" Ayarlar > Uygulamalar > Başlangıç'tan açılır.
