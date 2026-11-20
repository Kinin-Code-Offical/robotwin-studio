# RobotWin Studio SDK — ortam, kuvvet alanları, betikler ve eklentiler

*(Türkçe her bölümün başında; English follows in each section.)*

## 1. Genel bakış · Overview

**TR** — Bir dünyanın fiziğine kendi kodunuzu üç düzeyde ekleyebilirsiniz:

| Düzey | Nerede | Gerekenler | Ne için |
|---|---|---|---|
| **İfade dili** | Nesne → *Ortam ve davranışlar* → *Davranış ekle → İfade*, ya da Ortam → *İfade alanı* | Hiçbir şey | Hızlı kuvvet yasaları: `F.x = -k*(x - x0) - c*vx`, `wind.x = 3*sin(t)` |
| **C# betikleri** | Dünya → *Betikler* sekmesi → *Yeni C# betiği* | .NET 9 SDK (uygulama indirmez) | Durumlu davranışlar, sensörler, denetleyiciler; dünya dosyasıyla birlikte kaydedilir |
| **Eklenti DLL'leri** | `%APPDATA%\RobotWinStudio\Plugins\<ad>\` veya dünya dosyasının yanındaki `Plugins\` | Herhangi bir derleyici (şablon: `sdk/templates/RobotWinPlugin`) | Kütüphaneler, özel devre elemanları, paylaşılan modeller |

Hepsi **sabit 1 ms fizik adımının içinde**, çözücüden önce, belirli bir sırayla çalışır; rastgelelik dünya tohumundan gelir. Aynı dünya aynı sonucu verir.

**EN** — Custom physics can be added at three levels: the in-app **expression language** (no tools needed), **C# scripts** saved inside the world (compiled with an installed .NET 9 SDK — nothing is downloaded), and **plugin DLLs** (any compiler; template in `sdk/templates/RobotWinPlugin`). All of them run inside the fixed 1 ms physics step, before the solver, in a fixed order, with seeded randomness — runs are reproducible.

## 2. Ortam modeli · Environment model

Ortam sekmesi (Dünya → palet → *Ortam*) · the *Ortam* palette tab.

### Hava · Air
- **TR** Yoğunluk, ortam sıcaklığı *T*, deniz seviyesi basıncı *p₀*, rakım *h* ve bağıl nemden hesaplanır:
  `p = p₀·(1 − 2.25577·10⁻⁵·h)^5.25588`, `ρ = p_d/(R_d·T) + p_v/(R_v·T)` (Tetens doymuş buhar basıncı). 15 °C, 1013.25 hPa → 1.225 kg/m³. İsterseniz sabit bir yoğunluk girin (Mars ≈ 0.02).
- **EN** Density from temperature, sea-level pressure, altitude and humidity (barometric formula + humid-air gas law). A fixed density can override it. The older `WorldSpec.AirDensity` is still honoured.
- Sınırlama / limitation: tek bir yoğunluk bütün dünyaya uygulanır (yükseklikle değişim ihmal edilir). One density for the whole world.

### Rüzgâr · Wind
- **Ortalama rüzgâr / mean wind**: hız, *estiği* yön (0° = +X'e doğru, 90° = +Y'ye doğru), dikey bileşen.
- **Yükseklik profili / height profile**: düzgün; üstel yasa `u(z) = u_ref·(z/z_ref)^α`; logaritmik `u(z) = u_ref·ln(z/z₀)/ln(z_ref/z₀)`.
- **Türbülans / turbulence** ("Dryden benzeri"): her eksende birinci dereceden Gauss–Markov süreci
  `u[k+1] = a·u[k] + σ·√(1−a²)·n`, `a = exp(−V·Δt/L)`; σ yatay şiddet, dikey σ = oran·σ, ilinti süresi `L/V`.
  Boyuna bileşen Dryden spektrumunun tam karşılığıdır; yanal/dikey Dryden spektrumları ikinci derecedir, burada aynı birinci derece süzgeçle yaklaşıklanır. Türbülans uzayda düzgündür (bütün dünyada tek örnek) — L'den çok küçük nesneler için uygundur.
  *EN: first-order Gauss–Markov per axis (exact Dryden longitudinal spectrum; lateral/vertical approximated), spatially uniform.*
- **Ani esintiler / discrete gusts**: rüzgâr yönünde `½A·(1 − cos 2πt/T)` biçimli, genliği A·[0.5, 1], aralar üstel dağılımlı.
- **Tohum / seed**: bütün rastgele süreçler `SdkRandom` (SplitMix64) ile; .NET sürümünden bağımsız aynı dizi.

### Aerodinamik · Aerodynamics
- **Sürükleme / drag** her hareketli nesneye: `F = −½·ρ·(Cd·A)(û)·|v_rel|·v_rel`, `v_rel = v − rüzgâr`. `Cd·A` gövde eksenlerinde tutulur; eğik akışta yön kosinüsleriyle karıştırılır. Varsayılan Cd: kutu 1.05 (her yüz), küre 0.47, silindir 1.0 (yandan) / 0.9 (uçtan), gövde/model 0.8 (sınır kutusu). Nesne başına Cd, referans alan veya kapatma: sağ panel → *Ortam ve davranışlar*.
  Sürükleme fizik motorunun alt adımlarında hesaplanır ve tek adımda hızı tersine çeviremeyecek şekilde sınırlanır.
- **Kaldırma / buoyancy** `ρ·V·g` (isteğe bağlı; hacim çarpışma şeklinden veya elle).
- **Dönme sönümü / rotational damping** `−c·ρ·r⁵·|ω|·ω` (r = sınır küresi yarıçapı; kaba yaklaşım).
- **Pervaneler / propellers**: rüzgâr modeli `PhysicsScene.WindProvider` üzerinden pervane giriş havasına da girer (rotor sürüklemesi), yoğunluk itkiyi ölçekler.

### Kuvvet alanları · Force fields (Ortam → *Kuvvet alanları ve hava akımları*)

| Tür / kind | Davranış | Parametreler |
|---|---|---|
| Düzgün `uniform` | sabit kuvvet (N) veya ivme (m/s²) | yön, büyüklük |
| Çekici/itici `radial` | merkeze doğru `S·f(r)`; `f`: 1/r² (R'de 1), doğrusal, Gauss, sabit | R, azalma |
| Girdap `vortex` | Rankine girdabı: çekirdekte `S·r/R`, dışında `S·R/r`; içe akış, yukarı akış. Varsayılan **rüzgâr** olarak (sürükleme ve pervanelerle etkiler), isterseniz doğrudan kuvvet | eksen, R |
| Fan `fan` | kutu bölgesinde hava akımı (rüzgâra eklenir), ~6 çaptan sonra serbest jet gibi azalır, kenarlarda yumuşar | yön, çıkış hızı |
| Konveyör `conveyor` | bölgedeki nesnelerin yatay hızını τ = 50 ms ile bant hızına çeker, zemin sürtünmesini dengeler; en büyük kuvvet (tutuş + μ_zemin)·m·g | bant yönü/hızı, tutuş |
| İfade `expression` | her nesneye `F.*`/`T.*` veya her noktada `wind.*` | program |
| Eklenti `plugin` | `IForceField` / `IWindSource` uygulayan eklenti türü | eklentinin parametreleri |

Alanlar bir kutu bölgesiyle sınırlanabilir (merkez, boyut, dönüş) ve belirli nesnelere hedeflenebilir.
*EN: fields can be bounded by an oriented box and targeted to selected objects. Fans and (by default) vortices add air velocity, so they act through drag and on propellers.*

### Görselleştirme · Visualisation
3B görünümde zemin üzerinde rüzgâr okları, köşede rüzgâr çorabı (rüzgâr güçlendikçe yatayına kalkar), alan bölgelerinin çerçeveleri (fan mavi, konveyör sarı, girdap yeşil, diğerleri mor). Çalışırken canlı rüzgâr, dururken ortalama rüzgâr + fanlar. Sağ panel: nesnedeki rüzgâr, havaya göre hız, sürükleme kuvveti, Cd·A, betik kuvvetleri ve yayımlanan değerler.
*EN: wind arrows, a wind sock, field outlines; live values in the inspector.*

## 3. İfade dili · Expression language

```text
# yay + sönüm (x0 = başlangıç konumu)
k = 20; c = 0.5
F.x = -k*(x - x0) - c*vx
F.z = m*g*0.5 * (t > 1)          # karşılaştırmalar 0/1 verir
TL.z = -0.01*wz                  # gövde ekseninde tork
```
- İşleçler (öncelik sırasıyla, düşükten yükseğe) · operators: `?:`, `||`, `&&`, `< > <= >= == !=`, `+ -`, `* / %`, tekli `- + !`, `^` (üs, sağdan; `-2^2 = -4`, `2^3^2 = 512`; `**` da olur). `%` taban (floor) kalanıdır: `-7 % 3 = 2`.
- Fonksiyonlar · functions: `sin cos tan asin acos atan atan2 sinh cosh tanh sqrt cbrt abs exp ln log log10 log2 pow min max clamp sign floor ceil round frac hypot lerp step smoothstep deg rad if noise hash square saw pulse`. Sabitler: `pi tau e g0 true false`.
- **Nesne davranışı girdileri** · inputs: `t dt x y z vx vy vz wx wy wz speed m g x0 y0 z0 roll pitch yaw rho tempC air.x air.y air.z wind.x wind.y wind.z airspeed h` (+ *Sabitler* kutusundaki adlar, ör. `k=20, c=0.5`).
- **Çıktılar** · outputs: `F.x F.y F.z` (N, dünya), `FL.x…` (N, gövde ekseni), `T.x…` (N·m, dünya), `TL.x…` (gövde). `Fx` = `F.x` kısaltması. Atanmayan çıktılar 0'dır.
- **İfade alanında** ayrıca `wind.x wind.y wind.z` **çıktıdır** (o noktaya eklenen rüzgâr, m/s); temel rüzgâr `air.*` ile okunur. Rüzgâr değerlendirilirken hız girdileri 0'dır.
- Yerel değişkenler (`k = 20`) her adımda yeniden hesaplanır; nesne ifadelerinde *Ad.değişken* olarak yayımlanır (Betikler sekmesinde ve sağ panelde görünür).
- Hatalar Türkçe ve konumludur: `Satır 2, sütun 7: Bilinmeyen değişken 'sped'. Bunu mu demek istediniz: speed?`. Döngü ve kullanıcı fonksiyonu yoktur; her program sınırlı sürede biter (en fazla 8000 karakter, 4000 düğüm, 64 iç içe).
- NaN/∞ kuvvet üreten ifade hata sayılır (bkz. §6).

**EN** — Statements separated by `;` or new lines, `#` or `//` comments. Booleans are 0/1. Object behaviours read the body state and write `F.*`, `FL.*`, `T.*`, `TL.*`; expression fields may also write `wind.*` (added air velocity at a point; base wind readable as `air.*`). The parser/evaluator is hand-written (`RobotTwin.CoreSim.Sdk.Expressions.ExpressionProgram`), compiles to closures, and has no loops, so every program terminates.

## 4. C# API (`RobotTwin.CoreSim.Sdk`)

```csharp
[Behavior("Yay ile merkeze çek", Description = "…")]          // name in the "Davranış ekle" menu
public sealed class SpringToPoint : WorldBehavior
{
    [Parameter("Yay sabiti", Unit = "N/m")] public double K { get; set; } = 20;   // editable in the inspector
    [Parameter("Hedef", Unit = "m")] public Vec3 Target { get; set; }

    public override void OnStart(IBehaviorContext ctx) { }                    // once
    public override void OnStep(IBehaviorContext ctx, double dt)             // every 1 ms, before the solver
    {
        var b = ctx.Self!;                                                    // the object it is attached to
        b.ApplyForce((Target - b.Position) * K - b.Velocity * 0.5);
        ctx.Publish("uzaklık", (Target - b.Position).Length);                // inspector, other code, SDK sensor part
    }
    public override void OnAfterStep(IBehaviorContext ctx, double dt) { }   // after the solver (sensors)
    public override void OnStop(IBehaviorContext ctx) { }
}
```

| Tür / type | Amaç |
|---|---|
| `IWorldBehavior` / `WorldBehavior` | Her adımda çalışan kod (`OnStart`, `OnStep`, `OnAfterStep`, `OnStop`). `[Behavior(AttachTo = "world")]` dünya genelinde bir örnek. |
| `IBehaviorContext` | `Time, Dt, StepIndex, Self, Bodies, Body("ad veya O3"), Joints, Joint(id), Air (Density, TemperatureC, Pressure, WindAt), Gravity, Circuit, Parameters, Param(), Random (tohumlu), Publish/Read, Raycast, Log/Warn` |
| `ISimBody` | `Id, Name, Object, Mass, Inertia, Position, Orientation, Velocity, AngularVelocity, Volume, Material, DragArea (yazılabilir), LocalToWorld/WorldToLocal, PointVelocity, ApplyForce, ApplyForceAtPoint, ApplyLocalForce, ApplyTorque, Teleport, SetVelocity, LastSdkForce`. Kuvvetler yalnızca o adım için geçerlidir. |
| `ISimJoint` | `Angle, Speed, Axis, Anchor, MotorTorque (kalıcı), Drive, TargetAngle, TargetSpeed, Stiffness, Damping, MaxTorque, ReactionForce/Torque, Parent, Child` |
| `IForceField` / `ForceField` | `Affects(body)`, `Evaluate(body, ctx) → (Force, Torque)`; `Configure(parameters, spec)` alanın konumunu/parametrelerini alır |
| `IWindSource` | `WindAt(point, time)` → eklenen hava hızı (sürükleme + pervaneler) |
| `ICircuitAccess` | `Voltage(nesne, pin)`, `Signal(anahtar)`, `Devices(nesne[, iç RefDes])` → cihaz modellerine doğrudan erişim (özel sensörler) |
| `IPartProvider` | Özel devre parçaları: `PartDefinition` + `Build` içinde `AnalogDevice` alt sınıfları (`Stamp`, `Accept`) |
| `SensorDevice` | Hazır Thevenin çıkışı (OUT–GND). Kütüphanedeki **"Programlanabilir sensör (SDK)"** parçası `signal` parametresindeki yayımlanmış değeri gerilime çevirir (`V = değer·ölçek + kayma`, sınırlı) — Arduino `analogRead` ile okunabilir. |
| `SdkRandom` | SplitMix64 + Box–Muller; `ctx.Random` her ek için ayrı, tohumlu akış |

**EN** — Same surface: behaviours (per object or per world), fields, wind sources, circuit access, custom parts made of `AnalogDevice` subclasses, a ready `SensorDevice` and the "Programmable sensor (SDK)" part whose output follows a published value. Units are SI, world frame Z up.

## 5. Eklentiler · Plugins

**Klasör düzeni · layout**
```text
%APPDATA%\RobotWinStudio\Plugins\
  AcmeWind\
    plugin.json
    Acme.Wind.dll        (+ .pdb, + kendi bağımlılıkları)
<dünya klasörü>\Plugins\...   (Ayarlar'dan kapatılabilir)
```
**plugin.json**
```json
{ "id": "acme.wind", "name": "ACME rüzgâr", "version": "1.2.0", "author": "ACME",
  "description": "…", "assembly": "Acme.Wind.dll", "entryTypes": [], "sdkVersion": 1 }
```
- `id` benzersiz olmalı, `:` `/` `\` içeremez; `builtin` ve `script` ayrılmıştır. `entryTypes` boşsa derlemedeki bütün uygun public türler alınır. plugin.json yoksa tek DLL'li klasör dosya adıyla yüklenir.
- Her eklenti **kendi toplanabilir `AssemblyLoadContext`'inde**, DLL'in bellekteki kopyasından yüklenir: diskteki DLL kilitlenmez; yeniden derleyip *Eklentileri yeniden yükle* deyin. `RobotTwin.CoreSim` her zaman uygulamanınkiyle paylaşılır (DLL'i eklenti klasörüne kopyalamayın).
- **Dünya başına etkinleştirme**: bir eklentinin davranışları/alanları yalnızca dünyanın *Betikler* sekmesinde **"Bu dünyada etkin"** ise çalışır (bir eklenti türü eklediğinizde otomatik açılır). Ayarlar → *Eklentiler ve betikler* bölümünden bir eklenti tamamen kapatılabilir.
- **Kaldırma** (unload) ancak eklentinin nesnelerini tutan bir şey kalmayınca tamamlanır. Betikler sekmesindeki *Eklentileri yeniden yükle* simülasyonu önce durdurur; Ayarlar'dakini kullanmadan önce çalışan dünyayı siz durdurun.

**Şablon · template** — `sdk/templates/RobotWinPlugin`:
`dotnet build -c Release -p:Deploy=true` → DLL ve plugin.json `%APPDATA%\RobotWinStudio\Plugins\RobotWinPlugin.Samples\` klasörüne kopyalanır. Depo dışında `-p:RobotWinDir="<RobotTwin.CoreSim.dll klasörü>\"` verin. Örnekler: rüzgâr türbini (C_P(λ), Betz sınırı, jeneratör yükü), dipol mıknatıs (`3μ₀m₁m₂/2πr⁴`), Reynolds sayısına bağlı küre sürüklemesi, termal yükselme (`IWindSource`), yumuşak sınır (`ForceField`), anemometre (özel `AnalogDevice` + `IPartProvider` + sensör davranışı).

**EN** — Each plugin is a folder with a DLL and `plugin.json`. Plugins load into their own collectible `AssemblyLoadContext` from an in-memory copy (the file stays writable; rebuild and press *Reload plugins*); the SDK assembly is shared with the host. A plugin's types run in a world only if the plugin is enabled for that world. Unloading completes when nothing references the plugin's objects (the simulation is stopped first).

## 6. Güvenlik ve yalıtım · Safety and isolation
- **TR** Her kullanıcı çağrısı (`OnStart/OnStep/OnAfterStep/OnStop`, alan değerlendirmesi, rüzgâr kaynağı) ayrı ayrı `try/catch` içindedir. Hata Olaylar günlüğüne Türkçe yazılır (tür, mesaj, ilk yığın satırı, zaman); **3 hatadan sonra** o davranış/alan bu çalıştırma için kapatılır, gerisi çalışmaya devam eder. İfadelerin ve alanların ürettiği NaN/∞ kuvvetler uygulanmaz ve hata sayılır; `ISimBody.ApplyForce` NaN/∞ değerleri sessizce yok sayar.
- **Adım bütçesi**: her davranış/alan için adım başına ortalama süre ölçülür; dünya ayarındaki bütçeyi (varsayılan 0.25 ms) aşan için bir kez uyarı verilir. Betikler panelinde canlı süreler görünür.
- **Sınırlar (dürüstçe)**: .NET'te çalışan kod zorla durdurulamaz — sonsuz döngüye giren bir eklenti uygulamayı dondurur; `StackOverflowException` yakalanamaz. Eklenti ve betik kodu uygulamanın tüm yetkileriyle (dosya, ağ) çalışır: **korumalı alan (sandbox) değildir**. Yalnızca güvendiğiniz eklentileri kurun; başkalarından gelen dünyalarda dünya klasörü eklentilerini Ayarlar'dan kapatın. İfade dili ise tamamen güvenlidir (döngü yok, G/Ç yok).
- **EN** Every user call is isolated with try/catch, reported in Turkish, and the offender is switched off after 3 errors; NaN forces are rejected; per-step time is measured with a one-time budget warning. This is *not* a sandbox: plugin/script code has full process rights and cannot be pre-empted (an infinite loop hangs the app). The expression language is safe.

## 7. C# betikleri · Scripts
- **TR** Dünya → *Betikler* → *Yeni C# betiği*. Betikler dünya dosyasında saklanır. Derleme için bilgisayarda **.NET 9 SDK** gerekir (uygulama hiçbir şey indirmez): betikler geçici bir projeye (`%LOCALAPPDATA%\RobotWinStudio\Scripts\<özet>`) yazılır, `global.json` ile 9.x SDK'ya sabitlenir, NuGet kaynağı boş yerel bir klasöre yönlendirilir ve `dotnet build` çalıştırılır. Sonuç içerik özetine göre önbelleğe alınır; derleme hataları dosya/satır/sütunla gösterilir. Değişen betikler dünya başlatılırken otomatik derlenir (Ayarlar'dan kapatılabilir). SDK yoksa nedeni açıkça yazılır; ifade dili ve hazır eklentiler yine çalışır.
- Betik türlerinin anahtarı `script:<Ad.Alanı.Tür>`'dür. NuGet paketi kullanılamaz.
- **EN** Scripts live in the world file, are compiled with the user's .NET 9 SDK via `dotnet build` in a temp project (pinned SDK, empty local package source — no downloads), cached by content hash, and loaded like a plugin. If no suitable SDK is installed the UI explains why.

## 8. Belirlenimcilik · Determinism
- **TR** Rüzgâr modeli, alanlar ve davranışlar `WorldPhysics.Advance` içindeki sabit adımda (`Step`, varsayılan 1 ms) çalışır; sıra sabittir (alanlar, sonra davranışlar, ekleme sırasıyla). Rastgele sayılar dünya tohumundan türetilir. Aynı dünya + aynı tohum → aynı yörünge (testle doğrulandı). Ekran kare hızı veya gerçek zaman çarpanı sonucu değiştirmez. Kendi kodunuzda `System.Random`, saat veya çok iş parçacığı kullanmayın; `ctx.Random` kullanın.
- **EN** Everything runs inside the fixed physics step in a fixed order with seeded randomness; same world + same seed ⇒ identical trajectories (tested). Avoid wall-clock time, `System.Random` and threads in your code — use `ctx.Random`.

## 9. Doğrulama · Validation (tests in `CoreSim/tests/RobotTwin.CoreSim.Tests/SdkTests.cs`)
- Düşen pinpon topu: `v(t) = v_t·tanh(g·t/v_t)` ve `v_t = √(2mg/ρCdA)` ile %1 / %0.5 içinde.
- Rüzgârın hafif kutuya ilk kuvveti `½ρ·Cd·A·v²` ile %2 içinde; nesne başına Cd ve sürükleme kapatma.
- Dryden türbülansı: ortalama ≈ 0, σ ve dikey σ %4 içinde, `ρ(τ = L/V) ≈ e⁻¹`; aynı tohum aynı dizi.
- Esintiler, profil yasaları, ISA yoğunluğu/basıncı/viskozitesi.
- İfade dili: öncelik, fonksiyonlar, hata konumları ve öneriler.
- Eklenti: testte üretilen gerçek bir DLL (Reflection.Emit `PersistedAssemblyBuilder`) yüklenir, çalışır, diskte değiştirilip yeniden yüklenir, kaldırılır ve yükleme bağlamının toplandığı doğrulanır; test derlemesindeki türler aynı yoldan kaydedilir; hata yalıtımı ve bütçe uyarısı; .NET SDK varsa betik derleme.
