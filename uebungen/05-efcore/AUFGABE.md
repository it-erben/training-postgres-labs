# Übung 5: EF Core, 40 Minuten

## Worum es geht

Bis hierher hast du SQL selbst geschrieben. EF Core erzeugt es aus einem
Modell und LINQ-Ausdrücken. Du bekommst ein Objektmodell mit
Änderungsverfolgung, siehst dafür aber nicht mehr direkt, welche
Anweisungen zum Server gehen. Diese Übung macht die Anweisungen
wieder sichtbar: Die Tests bauen den Kontext mit einem Interceptor, der
jede gesendete Anweisung zählt und die letzte festhält.

Das Schema existiert bereits aus den Übungen 1 und 2, mit Enum-Typ,
Bereichsspalte und JSON-Dokument. EF Core wird darauf abgebildet, ohne
eigene Migration. Geübt werden vier Muster: Abfragen ohne N+1, Übersichten
ohne Änderungsverfolgung, Mengenänderungen ohne Laden und die
Systemspalte `xmin` als Versionsmerkmal für optimistische Sperren.

## Was du vorfindest

```text
Rental/Database/Migrations/005_bonus.sql       fertig: customer.bonus_points integer NOT NULL DEFAULT 0
Rental/Model/Entities.cs                       fertig: Entitäten, CustomerWithBookings, ICustomerOverview
Rental/Model/RentalContext.cs                  Stub: ConfigureProvider und OnModelCreating leer, CustomerOverview wirft
Rental.Tests/Exercise05EfCore.cs
```

Die Entitäten haben PascalCase-Namen, die Tabellen und Spalten
Kleinbuchstaben mit Unterstrich. Die Zuordnung ist Teil der Aufgabe.

```csharp
public sealed class CustomerEntity { Id, Name, BonusPoints, List<BookingEntity> Bookings }
public sealed class DeviceEntity   { Id, Name, Category }
public sealed class BookingEntity  { Id, DeviceId, Device, CustomerId, Customer,
                                     NpgsqlRange<DateTime> TimeRange, DeviceCondition ConditionAtPickup, ExtraInfo Extra }
```

`ExtraInfo` ist dieselbe Klasse wie in Übung 2 (`PickupLocation`, `Notes`).

So bauen die Tests den Kontext:

```csharp
new RentalContext(new DbContextOptionsBuilder<RentalContext>()
    .UseNpgsql(db.AppSource, RentalContext.ConfigureProvider)
    .AddInterceptors(counter)
    .Options)
```

`db.AppSource` ist deine DataSource aus Übung 2, mit Enum- und
JSON-Zuordnung. `RentalContext.ConfigureProvider` ist eine statische Methode,
die du füllst; sie bekommt den Provider-Builder. Der Interceptor gehört
den Tests.

## Schritt für Schritt

### 1. Enum auf Provider-Ebene

Datei `Rental/Model/RentalContext.cs`, Methode `ConfigureProvider`.

Die DataSource kennt den Enum bereits. EF Core muss ihn zusätzlich kennen,
um Eigenschaften vom Typ `DeviceCondition` auf den Servertyp abzubilden
statt auf `integer`:

```csharp
provider.MapEnum<DeviceCondition>("device_condition", "rental");
```

Ohne diesen Aufruf meldet der Test `Model_matches_existing_schema`
beim Lesen: `Reading as 'System.Int32' is not supported for fields having
DataTypeName 'rental.device_condition'`.

### 2. Tabellen und Spalten

Methode `OnModelCreating`. Zuerst das Schema, dann je Entität Tabelle und
Spaltennamen:

```csharp
modelBuilder.HasDefaultSchema("rental");

modelBuilder.Entity<CustomerEntity>(k =>
{
    k.ToTable("customer");
    k.Property(x => x.Id).HasColumnName("id");
    k.Property(x => x.Name).HasColumnName("name");
    k.Property(x => x.BonusPoints).HasColumnName("bonus_points");
    k.HasMany(x => x.Bookings).WithOne(b => b.Customer).HasForeignKey(b => b.CustomerId);
});
```

Für `DeviceEntity` entsprechend `device` mit `id`, `name`, `category`.
Für `BookingEntity` die Tabelle `booking` mit `id`, `device_id`,
`customer_id`, `time_range` und `condition_at_pickup`, dazu die Beziehung zu
`Device`.

Die Bereichsspalte braucht den Typ ausdrücklich, weil `NpgsqlRange<DateTime>`
auch `tsrange` sein könnte:

```csharp
b.Property(x => x.TimeRange).HasColumnName("time_range").HasColumnType("tstzrange");
```

Ohne `HasDefaultSchema` sucht EF Core in `public`; ohne `ToTable` unter
dem Klassennamen `CustomerEntity`. Beides meldet `42P01: relation does not
exist`.

### 3. Das JSON-Dokument

`Extra` wird als Owned Type in einer JSON-Spalte abgelegt. Die Schlüsselnamen
müssen zu dem passen, was Übung 2 mit camelCase geschrieben hat, sonst liest EF
Core `PickupLocation` und findet `pickupLocation` nicht:

```csharp
b.OwnsOne(x => x.Extra, z =>
{
    z.ToJson("extra_info");
    z.Property(x => x.PickupLocation).HasJsonPropertyName("pickupLocation");
    z.Property(x => x.Notes).HasJsonPropertyName("notes");
});
```

Ein Filter auf `b.Extra.PickupLocation` wird dann zu
`("extra_info" ->> 'pickupLocation')` übersetzt; der Test
`Extra_info_is_mapped_as_jsonb` sucht `->>` in `ToQueryString()`.

### 4. xmin als Versionsmerkmal

Jede Zeile in PostgreSQL trägt in der Systemspalte `xmin` die ID der
Transaktion, die sie zuletzt geschrieben hat. EF Core kann sie als
Versionsspalte verwenden, ohne dass die Tabelle eine eigene Spalte braucht:

```csharp
k.Property<uint>("xmin").IsRowVersion();
```

Beim `SaveChanges` erzeugt EF Core dann
`UPDATE ... WHERE id = @p1 AND xmin = @p2`. Trifft das `UPDATE` keine
Zeile, weil jemand dazwischen geschrieben hat, wirft EF Core
`DbUpdateConcurrencyException`. Der Test lädt denselben Kunden in zwei
Kontexten, speichert im zweiten und erwartet im ersten die Ausnahme.

### 5. Kundenübersicht ohne N+1

Klasse `CustomerOverview`, Methode `LoadAsync`. Der Test legt 50 Kunden
mit je 3 Buchungen an, setzt den Zähler auf null, ruft `LoadAsync` auf und
erwartet höchstens zwei Anweisungen. Wer die Kunden lädt und dann für
jeden Kunden `Bookings` nachlädt, sendet 51.

```csharp
var customers = await ctx.Customers
    .AsNoTracking()
    .Include(k => k.Bookings)
    .OrderBy(k => k.Id)
    .ToListAsync(ct);
```

`Include` erzeugt einen `JOIN` in einer Anweisung. `AsNoTracking` sorgt
dafür, dass die geladenen Objekte nicht im `ChangeTracker` landen; der
Test `Overview_loads_without_tracking` prüft `ChangeTracker.Entries()`
danach auf leer. Die Ergebnisse werden in `CustomerWithBookings` umgeformt.

### 6. Bonusgutschrift als Mengenoperation

Methode `CreditBonusAsync(int points)`. Alle Kunden mit mindestens
einer Buchung bekommen Punkte, ohne dass die Kunden geladen werden:

```csharp
return await ctx.Customers
    .Where(k => k.Bookings.Any())
    .ExecuteUpdateAsync(s => s.SetProperty(k => k.BonusPoints, k => k.BonusPoints + points), ct);
```

Das ist ein einziges `UPDATE ... WHERE EXISTS (...)`. Der Rückgabewert ist
die Zahl der betroffenen Zeilen. Der Test erwartet genau eine Anweisung
mit `UPDATE` und prüft die Punkte eines Kunden mit und eines ohne Buchung.

## Die Tests im Detail

| Test                                             | Was er prüft                                                                               | Wenn er rot ist                                                                  |
| ------------------------------------------------ | ------------------------------------------------------------------------------------------ | -------------------------------------------------------------------------------- |
| `Model_matches_existing_schema`                  | Zählt Geräte und Kunden, lädt eine Buchung: Zeitraum-Grenzen, Enum, `Extra.PickupLocation` | Tabellen- oder Spaltenname falsch, Enum nicht gemappt, JSON-Schlüssel PascalCase |
| `Extra_info_is_mapped_as_jsonb`                  | `ToQueryString` eines Filters auf `Extra.PickupLocation` enthält `->>`, zwei Treffer       | `ToJson` fehlt (dann eigene Tabelle) oder falscher Spaltenname                   |
| `Customer_overview_needs_at_most_two_statements` | 50 Kunden mit 150 Buchungen, Zähler `<= 2`, Summe der Buchungen stimmt                     | Nachladen je Kunde (51 Anweisungen)                                              |
| `Overview_loads_without_tracking`                | `ChangeTracker.Entries()` leer nach `LoadAsync`                                            | `AsNoTracking` fehlt                                                             |
| `Bonus_credit_uses_ExecuteUpdate`                | Genau eine Anweisung mit `UPDATE`, Punkte 10 bei Kunde mit Buchung, 0 ohne                 | Laden und `SaveChanges` (mehrere Anweisungen), oder Filter auf Buchungen fehlt   |
| `Concurrent_change_is_detected_via_xmin`         | Zwei Kontexte, `DbUpdateConcurrencyException` im ersten, `xmin` in der Anweisung           | `IsRowVersion` auf `xmin` fehlt; der erste Kontext überschreibt still            |

## Fallstricke

- Der Interceptor zählt jede Anweisung, auch die, die EF Core beim ersten
  Zugriff auf das Modell sendet. Die Tests setzen den Zähler deshalb
  unmittelbar vor dem geprüften Aufruf zurück.
- `Include` mit mehreren Sammlungen kann ein kartesisches Produkt erzeugen;
  `AsSplitQuery()` teilt in zwei Anweisungen. Für diese Übung reicht ein
  `Include`.
- `ExecuteUpdateAsync` umgeht den `ChangeTracker`. Objekte, die schon
  geladen sind, bleiben unverändert.
- Die Tabelle `booking` hat in Übung 6 eine weitere Spalte
  `returned_at`. EF Core stört sich nicht an Spalten, die im Modell
  fehlen.

## Bonus

`Booking_duration_is_computed_on_the_server` (Trait `Stretch=true`): Eine
Projektion auf `(b.TimeRange.UpperBound - b.TimeRange.LowerBound).TotalHours`
soll in eine Anweisung übersetzt werden, die `extra_info` nicht lädt. Der
Provider übersetzt `UpperBound` und `LowerBound` in `upper()` und
`lower()` und die Differenz in ein Intervall. Wenn das Modell aus Schritt 2
stimmt, ist dieser Test bereits grün; er zeigt, wie weit die Übersetzung
reicht.

## Fertig, wenn

`dotnet test --filter-trait Exercise=05 --filter-not-trait Stretch=true` sechs
grüne Tests meldet.
