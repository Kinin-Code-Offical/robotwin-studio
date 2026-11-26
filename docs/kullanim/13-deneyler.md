# 13. Deneyler: parametre taraması ve güven aralıkları

Bir dünyayı bir kez çalıştırmak bir sonuç verir; bir deney, aynı dünyayı değiştirdiğiniz ayarların her birleşiminde ve her birini birkaç tohumla (sensör gürültüsü, parça toleransları, `random()`) çalıştırıp her ölçütün ortalamasını, standart sapmasını ve %95 güven aralığını verir. Deneyler pencere açılmadan, arka planda ve uygulamanın kendi devre, fizik ve sensör çözücüleriyle koşar.

## Deney dosyası

Deney bir JSON dosyasıdır:

```json
{
  "name": "Serbest düşme",
  "worldFile": "dusen-top.rwworld",
  "duration": 0.3,
  "sampleInterval": 0.005,
  "repeats": 5,
  "seed": 1,
  "tolerances": false,
  "firmware": true,
  "factors": [
    { "target": "environment.Gravity", "values": ["9.81", "1.62"] },
    { "target": "Top.z", "from": 0.5, "to": 1.5, "steps": 3 }
  ],
  "metrics": [
    { "name": "en yüksek hız", "source": "Top.speed", "reduce": "max" },
    { "name": "düşüş", "source": "Top.z", "reduce": "range" }
  ]
}
```

- **duration** her koşunun simüle süresi (s), **sampleInterval** ölçütlerin hangi sıklıkla okunduğu.
- **repeats** her birleşimin kaç kez çalışacağı; tohumlar `seed`, `seed + 1`, … olur. Aynı tohum aynı gürültüyü ve aynı tolerans çekilişini verir: deney birebir tekrarlanabilir.
- **tolerances** açıksa her koşuda parça değerleri tolerans bantlarından yeniden çekilir (Monte Carlo).
- **firmware** kapalıysa kartların yazılımı çalışmaz; yalnız devre ve mekanik.

### Etkenler

| Hedef | Ne değişir |
| --- | --- |
| `environment.<ayar>` | Ortam: `AmbientC`, `Gravity`, `Friction`, `WindX`, `WallHeight`, `AirDensity` … |
| `<nesne>.<parametre>` | Bir parçanın parametresi, anahtarıyla: `rpm`, `fault`, `fault_value`, `model` … |
| `<nesne>.x`, `.y`, `.z`, `.yaw` | Yerleştirildiği konum (m) ve yönü (°) |
| `<nesne>.mass` | Kütlesi (kg) |

Nesne adıyla ya da kimliğiyle yazılır. Değerler `values` ile tek tek ya da `from`–`to` aralığında `steps` adımla verilir. Birden çok etkenin her değeri birbiriyle birleştirilir.

### Ölçütler

| Kaynak | Ne okunur |
| --- | --- |
| `<nesne>.x`, `.y`, `.z` | Konumu (m) |
| `<nesne>.speed` | Hızı (m/s) |
| `<nesne>.distance` | Koşu boyunca aldığı yol (m) |
| `<nesne>.roll`, `.pitch`, `.yaw` | Duruşu (°) |
| `<nesne>.temperature` | Sıcaklığı (°C) |
| `node:<ağ ya da parça.pin>` | Gerilimi (V) |

**reduce** örnekleri tek sayıya indirir: `final` (son değer), `mean`, `min`, `max`, `rms`, `range` (en büyük − en küçük).

Pencereden: **Simülasyon → Deney: parametre taraması…** açık dünyada etkenleri ve ölçütleri listeden seçtirir, deneyi arka planda çalıştırır, her birleşimin ortalamasını ve güven aralığını gösterir ve dosyaları **Belgeler\RobotWin\Deneyler** altına, çalıştırılan dünyanın bir kopyasıyla birlikte yazar. **Deneyi kaydet** ayarları JSON olarak saklar; komut satırı aynı dosyayı çalıştırır.

Rastgele bir şey içermeyen bir dünyada (gürültülü sensör, tolerans, `random()` yoksa) her tohum aynı sonucu verir; standart sapma 0 çıkar.

## Çalıştırma

```powershell
RobotWinStudio.exe --experiment deney.json --experiment-out sonuclar
```

`--experiment-world dunya.rwworld` deneydeki dünya dosyasının yerine geçer. Klasöre şunlar yazılır:

- **runs.csv:** her koşu, birleşimi, tohumu, etken değerleri ve ölçütleri;
- **summary.csv:** her birleşimde her ölçütün n, ortalama, standart sapma ve %95 güven aralığı;
- **report.md:** neyin çalıştırıldığı ve her ölçüt için bir tablo;
- **experiment.json:** çalıştırılan deney, dünya dosyasının tam yoluyla (tekrar için);
- **progress.log:** ilerleme.

Çıkış kodu 0 ise bütün koşular bitti, 1 ise bazıları hata verdi (hatası `runs.csv`'de ve raporda), 2 ise deney başlayamadı.

## Python ve Jupyter

`python/robotwin/experiment.py` aynı deneyi Python'dan kurar ve çalıştırır (başsız çalıştırıcı `RobotWin.Headless --experiment` ile); sonuç tabloları `to_pandas()` ile DataFrame olur. `python/examples/experiment.ipynb` sıcaklık ve kütle taramasını güven aralığı çubuklarıyla çizer. Ayrıntı: [python/README.md](https://github.com/Kinin-Code-Offical/robotwin-studio/blob/main/python/README.md).

## Güven aralığı

Bir birleşimin *n* koşusunda ölçütün ortalaması *x̄*, örnek standart sapması *s* ise %95 güven aralığı *x̄ ± t·s/√n*'dir; *t*, *n − 1* serbestlik dereceli Student t dağılımının iki yanlı %95 değeridir (*n* = 2'de 12,71, *n* = 10'da 2,26, çok koşuda 1,96). Tek koşulu birleşimin aralığı verilmez.
