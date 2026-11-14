# Starship Mission Tracker
## Projekt specifikáció és fejlesztési terv

**Projekt:** Starship Mission Tracker  
**Célplatform:** Google Earth Pro  
**Dokumentum típusa:** Technical Specification / Development Plan  
**Státusz:** Initial Development  
**Elsődleges cél:** A SpaceX Starship és Super Heavy repülési útvonalának 3D-s megjelenítése, tervezett és tényleges pálya követése.

---

# 1. Projekt áttekintése

A projekt célja egy olyan alkalmazás fejlesztése, amely a Google Earth Pro segítségével háromdimenziós környezetben jeleníti meg a SpaceX Starship küldetéseit.

A rendszernek képesnek kell lennie:

- A tervezett repülési útvonal megjelenítésére.
- Az aktuális repülési pozíció követésére, amennyiben rendelkezésre áll megfelelő telemetria.
- A tervezett és tényleges útvonal egyidejű megjelenítésére.
- A Starship és Super Heavy külön kezelésére.
- Részletes 3D modellek és egyszerű navigációs ikonok megjelenítésére.
- A repülési események vizualizálására.
- Korábbi küldetések visszajátszására.

A Google Earth Pro nem közvetlenül az adatforrásokkal kommunikál. Egy saját backend dolgozza fel az adatokat, és KML/KMZ formátumban szolgáltatja azokat a Google Earth számára.

## 1.1. Alapvető működési koncepció

```text
External Data Sources
        |
        v
Data Acquisition
        |
        v
Mission Processing Backend
        |
        +---- Trajectory Processing
        |
        +---- Telemetry Processing
        |
        +---- Mission Events
        |
        v
KML / KMZ Generator
        |
        v
Google Earth Pro
```

A Google Earth Pro a megjelenítési réteg, a backend pedig az adatgyűjtésért, feldolgozásért és koordinátaszámításért felel.

---

# 2. Technológiai stack

Az elsődleges technológiai döntések:

| Komponens | Technológia |
|---|---|
| Backend | C# / .NET 10 |
| API | ASP.NET Core Minimal API |
| Adattárolás | SQLite |
| Adatfeldolgozás | C# és szükség esetén Python |
| Térinformatikai számítások | GeographicLib |
| Térbeli adatmegjelenítés | KML 2.2 + Google Extensions |
| 3D modell | COLLADA (.dae) |
| 3D modellezés | Blender |
| Kommunikáció | HTTP / NetworkLink |
| Megjelenítés | Google Earth Pro |
| Fejlesztőkörnyezet | Rider / Visual Studio |

A rendszer legyen moduláris, és az adatforrások legyenek egymástól függetlenül cserélhetők.

A Google Earth Pro legyen az elsődleges célplatform. A Google Earth Web későbbi kompatibilitása nem MVP-követelmény.

---

# 3. Funkcionális követelmények

## 3.1. Tervezett repülési útvonal

A rendszernek képesnek kell lennie egy küldetés előre meghatározott repülési profiljának megjelenítésére.

Az útvonal pontjai az alábbi adatokat tartalmazzák:

- Latitude
- Longitude
- Altitude
- Timestamp
- Flight phase
- Data source
- Estimated / measured status

A pálya háromdimenziós legyen, tehát ne egyszerű földfelszíni útvonalat jelenítsen meg.

A tervezett pálya akkor is legyen megjeleníthető, amikor nincs aktív küldetés.

## 3.2. Élő követés

A rendszer támogassa a repülő járművek aktuális pozíciójának frissítését.

Elvárások:

- Automatikus pozíciófrissítés.
- Történeti pályaszakasz megőrzése.
- Aktuális pozíció elkülönítése a tervezett útvonaltól.
- Adatforrás és adatminőség jelzése.
- Hiányzó vagy elavult telemetria kezelése.

Fontos: nem feltételezhető, hogy a SpaceX nyilvános élő GPS-telemetria API-t biztosít. A tényleges követés csak ellenőrzött, hozzáférhető adatok alapján valósítható meg.

Ha csak részleges telemetria áll rendelkezésre, a rendszer becsült pozíciót jeleníthet meg, de ezt egyértelműen jelölnie kell.

## 3.3. 3D Starship modell

A Google Earth Pro-ban részletes Starship-modell jelenjen meg.

Követelmények:

- COLLADA formátumú modell.
- Földrajzi koordinátákhoz kötött elhelyezés.
- Magasságkezelés.
- Heading, pitch és roll támogatása.
- Haladási irány szerinti orientáció.
- Megfelelő méretezés.

A modellt külön kell kezelni a navigációs ikontól.

## 3.4. Navigációs ikon

A részletes 3D modell mellett egy egyszerű, könnyen felismerhető ikon is jelenjen meg.

Az ikon célja, hogy nagyobb földgömbnézetből is jól látható legyen az űrhajó aktuális pozíciója.

A két megjelenítési mód lehetőleg külön kapcsolható legyen.

## 3.5. Útvonalak vizualizációja

Javasolt színkódolás:

| Útvonal | Szín |
|---|---|
| Tervezett pálya | Kék |
| Ténylegesen megtett pálya | Zöld |
| Bizonytalan / becsült szakasz | Narancssárga |
| Hátralévő tervezett pálya | Kék, eltérő stílusban |

A tervezett és tényleges útvonal mindig külön objektum legyen.

## 3.6. Repülési események

Támogatandó események:

- Liftoff
- Max Q
- MECO
- Stage Separation
- Booster Boostback
- Booster Landing Burn
- Booster Landing
- Starship Engine Cutoff
- Coast
- Reentry
- Landing Burn
- Splashdown / Landing

Az eseményekhez tartozzon:

- Esemény neve.
- Időpont.
- Koordináta, ha rendelkezésre áll.
- Magasság, ha rendelkezésre áll.
- Rövid leírás.
- Adatforrás.

Nem minden esemény kötelező minden küldetésnél.

---

# 4. Adatforrások

Az adatgyűjtő rétegnek több, egymástól független forrást kell támogatnia.

## 4.1. Hivatalos források

Elsődleges vizsgálati területek:

- SpaceX küldetésinformációk.
- FAA dokumentációk.
- FAA környezetvédelmi dokumentumok.
- NOTAM és légtérkorlátozási információk.
- Egyéb hivatalos repülési és küldetési dokumentumok.

A hivatalos dokumentumok felhasználhatók a tervezett repülési profil rekonstruálásához.

Fontos különbség: a lezárt veszélyzónák nem feltétlenül jelentik a tényleges repülési útvonalat.

## 4.2. Nyilvános telemetria

Vizsgálandó lehetőségek:

- Nyilvános küldetésadatok.
- SpaceX webcast.
- Webcastben megjelenített telemetria.
- Külső telemetria-adatbázisok.
- Független pályarekonstrukciós projektek.

A webcastből történő adatkinyeréshez később OpenCV és OCR (például Tesseract) használható.

## 4.3. Fallback megoldás

Ha nincs megfelelő élő telemetria, a rendszer használjon rekonstruált repülési profilt.

A rekonstrukció alapja lehet:

- Előzetesen publikált repülési profil.
- Repülési idő.
- Magasság.
- Sebesség.
- Indítási hely.
- Ismert események.
- Nyilvános pályaadatok.

A becsült és tényleges pozíciókat minden esetben külön kell kezelni.

---

# 5. Backend architektúra

A backend moduláris felépítésű legyen.

## 5.1. Fő modulok

### Mission

A küldetések kezeléséért felel.

Feladatok:
- Küldetések létrehozása és lekérdezése.
- Küldetés státuszának kezelése.
- Járművek hozzárendelése.
- Aktuális repülési fázis kezelése.

### DataAcquisition

Külső adatforrásokból gyűjt adatokat.

Feladatok:
- API-k lekérdezése.
- Dokumentumok feldolgozása.
- Webcast-adatok fogadása.
- Adatforrások státuszának kezelése.

### Telemetry

A telemetria feldolgozásáért felel.

Feladatok:
- Telemetriaadatok normalizálása.
- Időbélyegek kezelése.
- Adatvalidáció.
- Becsült és mért adatok elkülönítése.

### Trajectory

A háromdimenziós útvonal kezeléséért felel.

Feladatok:
- Tervezett pálya generálása.
- Tényleges pálya kezelése.
- Koordinátainterpoláció.
- Geodéziai számítások.
- Magasságkezelés.

### Kml

A Google Earth számára szükséges KML/KMZ fájlokat generálja.

Feladatok:
- KML generálása.
- KMZ csomagolása.
- Modellek és ikonok hivatkozása.
- NetworkLink támogatása.
- Történeti pályák generálása.

---

# 6. Javasolt solution struktúra

```text
StarshipMissionTracker/
│
├── StarshipMissionTracker.sln
│
├── src/
│   │
│   ├── StarshipMissionTracker.Api/
│   │   ├── Endpoints/
│   │   ├── Program.cs
│   │   └── appsettings.json
│   │
│   ├── StarshipMissionTracker.Core/
│   │   ├── Entities/
│   │   ├── Enums/
│   │   ├── Interfaces/
│   │   └── Models/
│   │
│   ├── StarshipMissionTracker.Data/
│   │   ├── Context/
│   │   ├── Configurations/
│   │   └── Repositories/
│   │
│   ├── StarshipMissionTracker.DataAcquisition/
│   │   ├── Sources/
│   │   ├── Collectors/
│   │   └── Parsers/
│   │
│   ├── StarshipMissionTracker.Telemetry/
│   │   ├── Processing/
│   │   ├── Validation/
│   │   └── Reconstruction/
│   │
│   ├── StarshipMissionTracker.Trajectory/
│   │   ├── Calculation/
│   │   ├── Interpolation/
│   │   └── Models/
│   │
│   └── StarshipMissionTracker.Kml/
│       ├── Generators/
│       ├── Templates/
│       └── Packaging/
│
├── assets/
│   ├── models/
│   │   └── starship.dae
│   ├── icons/
│   │   ├── starship.png
│   │   └── booster.png
│   └── textures/
│
├── data/
│   ├── missions/
│   ├── telemetry/
│   └── trajectories/
│
├── output/
│
└── docs/
```

A solution struktúrája javaslat, az implementáció során módosítható.

---

# 7. Adatmodell

## 7.1. Mission

| Mező | Típus |
|---|---|
| Id | Guid |
| Name | string |
| MissionCode | string |
| Status | enum |
| PlannedLaunchTime | DateTime? |
| ActualLaunchTime | DateTime? |
| Description | string? |

## 7.2. Vehicle

| Mező | Típus |
|---|---|
| Id | Guid |
| MissionId | Guid |
| Name | string |
| Type | VehicleType |
| ModelPath | string? |

VehicleType:
- Starship
- SuperHeavy

## 7.3. TrajectoryPoint

| Mező | Típus |
|---|---|
| Id | long |
| TrajectoryId | Guid |
| Timestamp | DateTime |
| Latitude | double |
| Longitude | double |
| AltitudeMeters | double |
| IsEstimated | bool |
| SourceId | Guid? |

## 7.4. Trajectory

| Mező | Típus |
|---|---|
| Id | Guid |
| MissionId | Guid |
| VehicleId | Guid |
| Type | TrajectoryType |
| CreatedAt | DateTime |

TrajectoryType:
- Planned
- Actual
- Reconstructed

## 7.5. Telemetry

| Mező | Típus |
|---|---|
| Id | long |
| VehicleId | Guid |
| Timestamp | DateTime |
| AltitudeMeters | double? |
| VelocityMetersPerSecond | double? |
| Latitude | double? |
| Longitude | double? |
| SourceId | Guid |
| IsEstimated | bool |

## 7.6. MissionEvent

| Mező | Típus |
|---|---|
| Id | Guid |
| MissionId | Guid |
| VehicleId | Guid? |
| EventType | enum |
| Timestamp | DateTime? |
| Latitude | double? |
| Longitude | double? |
| AltitudeMeters | double? |
| Description | string? |

## 7.7. DataSource

| Mező | Típus |
|---|---|
| Id | Guid |
| Name | string |
| Type | DataSourceType |
| Url | string? |
| Reliability | enum |

---

# 8. API tervezete

Javasolt REST végpontok:

| Method | Endpoint | Feladat |
|---|---|---|
| GET | /api/missions | Küldetések |
| GET | /api/missions/current | Aktuális küldetés |
| GET | /api/missions/{id} | Küldetés részletei |
| GET | /api/missions/{id}/trajectory | Pályaadatok |
| GET | /api/missions/{id}/telemetry | Telemetria |
| GET | /api/missions/{id}/events | Események |
| GET | /kml/master | Fő KML |
| GET | /kml/live | Aktuális pozíció |
| GET | /kml/planned | Tervezett pálya |
| GET | /kml/history | Történeti pálya |

A végpontok az első MVP-ben még nem feltétlenül szükségesek mind.

---

# 9. KML/KMZ megjelenítés

## 9.1. Objektumhierarchia

```text
Starship Mission Tracker
│
├── Mission Information
│
├── Planned Trajectory
│   ├── Starship
│   └── Super Heavy
│
├── Actual Trajectory
│   ├── Starship
│   └── Super Heavy
│
├── Vehicles
│   ├── Starship 3D Model
│   ├── Starship Icon
│   └── Booster
│
└── Mission Events
    ├── Liftoff
    ├── Stage Separation
    ├── Reentry
    └── Landing
```

## 9.2. Használandó KML elemek

- `Placemark`: földrajzi objektumok.
- `Point`: járművek aktuális pozíciója.
- `LineString`: tervezett és tényleges pályaszakaszok.
- `Model`: 3D Starship modell.
- `gx:Track`: időbélyeges mozgás.
- `NetworkLink`: dinamikus adatfrissítés.
- `Folder`: logikai objektumcsoportok.
- `Style`: színek és megjelenítési beállítások.

## 9.3. NetworkLink

A fő KML egy lokális backendhez kapcsolódik.

Példa:

```xml
<NetworkLink>
    <name>Starship Live Tracking</name>
    <Link>
        <href>http://127.0.0.1:8080/kml/live</href>
        <refreshMode>onInterval</refreshMode>
        <refreshInterval>5</refreshInterval>
    </Link>
</NetworkLink>
```

Az öt másodperces frissítés kezdeti konfiguráció. A tényleges gyakoriságot a rendelkezésre álló telemetria, a Google Earth viselkedése és a backend terhelése alapján kell optimalizálni.

A statikus tervezett útvonalat nem szükséges minden frissítéskor újragenerálni.

---

# 10. Koordinátarendszer és pályaszámítás

Minden pályapont háromdimenziós koordinátát tartalmaz:

```text
Longitude
Latitude
Altitude
```

A magasságot méterben kell tárolni.

A KML koordináták sorrendje:

```text
longitude,latitude,altitude
```

Különösen fontos a magasság referencia-rendszerének kezelése.

A geodéziai magasság és a tengerszint feletti magasság nem feltétlenül azonos. A forrásadatok magassági referencia-rendszerét dokumentálni kell, és szükség esetén átszámítást kell végezni.

A pályaszámításnak kezelnie kell:

- Föld görbületét.
- Háromdimenziós koordinátákat.
- Magasságot.
- Időbélyegeket.
- Interpolációt.
- Járműorientációt.

Az interpoláció nem helyettesítheti a tényleges telemetriát.

---

# 11. Adatminőség és megbízhatóság

A rendszer egyik legfontosabb követelménye az adatforrások megbízhatóságának kezelése.

Minden adatpontnál különüljön el:

1. Mért adat.
2. Publikált, de nem közvetlen telemetria.
3. Rekonstruált adat.
4. Szimulált adat.

Javasolt státuszok:

```csharp
public enum DataQuality
{
    Unknown,
    Simulated,
    Estimated,
    Reconstructed,
    Published,
    Measured
}
```

A rendszer soha ne jelenítsen meg szimulált vagy rekonstruált pozíciót valódi GPS-pozícióként.

A forrásadatokat lehetőleg változatlanul is meg kell őrizni, hogy a feldolgozási algoritmusok később újra lefuttathatók legyenek.

---

# 12. Fejlesztési roadmap

## Phase 1 – MVP: Static KML/KMZ

Cél: első működő Google Earth Pro prototípus.

Feladatok:

- [ ] Solution létrehozása.
- [ ] Minimális KML generátor.
- [ ] Starship 3D modell importálása.
- [ ] Navigációs ikon.
- [ ] 3D-s repülési útvonal.
- [ ] Repülési események.
- [ ] KMZ generálás.
- [ ] Tesztelés Google Earth Pro-ban.

Elfogadási feltétel:

Egyetlen KMZ megnyitásával látható legyen a Starship modellje, ikonja, tervezett útvonala és néhány küldetési esemény.

## Phase 2 – Dynamic Tracking

- [ ] ASP.NET Core backend.
- [ ] NetworkLink.
- [ ] SQLite adatbázis.
- [ ] Szimulált telemetria.
- [ ] Aktuális pozíció frissítése.
- [ ] Történeti pálya.
- [ ] Starship és Super Heavy külön kezelése.

Elfogadási feltétel:

A Starship szimulált adatok alapján mozogjon a Google Earth Pro-ban, miközben a ténylegesen megtett útvonal folyamatosan bővül.

## Phase 3 – Real Data Integration

- [ ] SpaceX adatforrások feltérképezése.
- [ ] FAA dokumentumok feldolgozása.
- [ ] Küldetési adatok normalizálása.
- [ ] Korábbi repülések rekonstruálása.
- [ ] Webcast OCR prototípus.
- [ ] Telemetria-adapterek.
- [ ] Adatminőség ellenőrzése.

Elfogadási feltétel:

Legalább egy korábbi küldetés feldolgozott, forrásokkal dokumentált adatok alapján megjeleníthető legyen.

## Phase 4 – Advanced Features

- [ ] Időalapú visszajátszás.
- [ ] Küldetésválasztó.
- [ ] Repülési események részletes kezelése.
- [ ] 3D modell orientációjának frissítése.
- [ ] Booster követése.
- [ ] Sebesség- és magasságadatok.
- [ ] Adatminőség vizualizációja.

---

# 13. Agentek közötti feladatmegosztás

A fejlesztést több, egymástól jól elválasztható feladatra lehet bontani.

## Agent 1 – Architecture & Core

Feladat:
- Solution és projektstruktúra.
- Core domain modellek.
- Interfészek.
- Enumok.
- Adatmodell.
- Függőségi irányelvek.

Elvárás: stabil domainréteg, amelyre a többi modul épülhet.

## Agent 2 – KML & Google Earth

Feladat:
- KML generátor.
- KMZ csomagolás.
- 3D modellek betöltése.
- NetworkLink.
- gx:Track.
- Google Earth Pro kompatibilitási tesztek.

Elvárás: önállóan tesztelhető KML-generáló modul.

## Agent 3 – Trajectory & Mathematics

Feladat:
- Koordinátaszámítás.
- Geodéziai számítások.
- Interpoláció.
- 3D útvonal.
- Járműorientáció.
- Magassági referencia-rendszerek.

Elvárás: egységtesztekkel rendelkező pályaszámító komponens.

## Agent 4 – Data Acquisition & Research

Feladat:
- Hivatalos adatforrások feltérképezése.
- Nyilvános telemetriaforrások keresése.
- FAA dokumentumok.
- NOTAM adatok.
- Webcast telemetria lehetőségei.
- Adatforrás-adapterek tervezése.

Elvárás: ellenőrzött adatforrás-katalógus, hozzáférési feltételekkel és példákkal.

## Agent 5 – Database & API

Feladat:
- EF Core.
- SQLite.
- Entitások konfigurációja.
- Repository vagy megfelelő adatkezelési réteg.
- REST API.
- KML endpointok.

Elvárás: működő adatbázis és API.

Az agentek közös Core modelleket és interfészeket használjanak. Az egymással ütköző fájlmódosításokat kerülni kell.

---

# 14. Első fejlesztési prioritások

A fejlesztést az alábbi sorrendben érdemes elkezdeni:

1. Core domain modellek és interfészek.
2. KML-generátor.
3. Statikus KMZ prototípus.
4. Google Earth Pro kompatibilitási tesztek.
5. Trajectory modul.
6. Backend és adatbázis.
7. NetworkLink és szimulált telemetria.
8. Valós adatforrások integrációja.

Nem érdemes az összes modult egyszerre implementálni.

A legfontosabb korai mérföldkő az első működő KMZ.

---

# 15. Referenciák és kutatási kiindulópontok

## Google Earth és KML

- KML Reference: https://developers.google.com/kml/documentation/kmlreference
- KML Tutorials: https://developers.google.com/kml/documentation
- Google Earth Pro: https://www.google.com/earth/about/versions/

Kiemelten vizsgálandó:
- Model
- NetworkLink
- gx:Track
- LineString
- LookAt
- TimeStamp / TimeSpan

## Pályarekonstrukció

- APOGEE – Starship Flight Profiles: https://apogee.davidtphung.com/

Az APOGEE hasznos referencia a korábbi Starship-küldetések és pályarekonstrukciók tanulmányozásához.

## Hivatalos adatforrások

- SpaceX: https://www.spacex.com/
- FAA: https://www.faa.gov/
- FAA NOTAM: https://notams.aim.faa.gov/

A források aktuális elérhetőségét és hozzáférési feltételeit fejlesztés előtt ellenőrizni kell.

---

# 16. Fejlesztési alapelvek

1. **Source independence:** az adatforrások ne legyenek összekötve a KML-generátorral.
2. **Data provenance:** minden adat eredete legyen visszakövethető.
3. **No fabricated telemetry:** becsült adat soha ne jelenjen meg valódi telemetriaként.
4. **Modularity:** a modulok külön fejleszthetők és tesztelhetők legyenek.
5. **Testability:** a pályaszámítás és KML-generálás automatizált tesztekkel rendelkezzen.
6. **Incremental development:** először működő statikus prototípus, utána dinamikus funkciók.
7. **Google Earth compatibility:** a KML-t mindig a tényleges célalkalmazásban kell tesztelni.
8. **Documentation:** minden külső adatforrás és számítási feltételezés legyen dokumentálva.

---

# 17. Első konkrét feladat

A projekt első célja egy önállóan megnyitható Google Earth Pro KMZ prototípus létrehozása.

A prototípusnak tartalmaznia kell:

- Egy Starship 3D modellt.
- Egy navigációs ikont.
- Egy közelítő, háromdimenziós Starship repülési útvonalat.
- Néhány repülési eseményt.
- Elkülönített KML rétegeket.

Ehhez egyelőre nem szükséges backend, adatbázis vagy élő telemetria.

**Definition of Done:**

A generált KMZ megnyitható Google Earth Pro-ban, a Starship megfelelően elhelyezkedik a Föld felszínéhez viszonyítva, a modell orientációja értelmes, és a teljes tervezett útvonal háromdimenziósan látható.

Ez a prototípus lesz a későbbi teljes Starship Mission Tracker alapja.