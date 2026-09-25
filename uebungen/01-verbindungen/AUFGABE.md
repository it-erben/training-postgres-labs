# Übung 1: Verbindungen und Anweisungen, 35 Minuten

## Worum es geht

Jede .NET-Anwendung, die mit PostgreSQL spricht, trifft am Anfang vier
Entscheidungen, oft ohne es zu merken: Wie viele Serververbindungen hält sie
offen? Wie erkennt ein DBA ihre Verbindungen in `pg_stat_activity`? Werden
wiederholte Anweisungen auf dem Server vorbereitet? Und gehen Werte als
Parameter oder als Teil des SQL-Textes zum Server?

In Npgsql bündelt die `NpgsqlDataSource` diese Entscheidungen. Sie wird
einmal beim Start gebaut, lebt so lange wie die Anwendung und verwaltet den
Pool. Alles, was danach Verbindungen braucht, bekommt die DataSource
übergeben. In dieser Übung baust du sie und den ersten Zugriff darauf, den
Gerätekatalog.

## Was du vorfindest

```text
Rental/Database/RentalDataSource.cs              Stub: Create wirft NotImplementedException
Rental/Database/Migrator.cs                      fertig: spielt SQL-Migrationen ein
Rental/Database/Migrations/001_devices.sql       fertig: Tabellen device und customer mit sechs Geräten
Rental/Devices/Device.cs                         fertig: Record Device und Schnittstelle IDeviceCatalog
Rental/Devices/DeviceCatalog.cs                  Stub: beide Suchen werfen NotImplementedException
Rental.Tests/Infrastructure/DatabaseFixture.cs   fertig: Schema neu aufbauen, DataSources bereitstellen
Rental.Tests/Exercise01Connections.cs            die Tests dieser Übung
```

Die Migration legt an:

```sql
CREATE TABLE rental.device   (id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY, name text NOT NULL, category text NOT NULL);
CREATE TABLE rental.customer (id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY, name text NOT NULL);
```

Geräte: Bohrhammer und Akkuschrauber (`drills`), Stichsäge und Kreissäge
(`saws`), Leiter und Gerüst (`access`).

Die Fixture ruft `RentalDataSource.Create(TestEnvironment.ConnectionString)`
beim ersten Zugriff auf `db.AppSource` auf. Solange der Stub wirft, scheitern
alle Tests der Übung mit
`NotImplementedException: Übung 1: RentalDataSource.Create ...`. Rot ist hier
der erwartete Startzustand.

## Schritt für Schritt

### 1. Die DataSource bauen

Datei `Rental/Database/RentalDataSource.cs`, Methode
`Create(string connectionString)`.

Die übergebene Zeichenfolge ist die aus `RENTAL_CONNECTION`. Du ergänzt
sie um Einstellungen und baust daraus die DataSource. Der Weg ohne
Zeichenkettenbastelei:

```csharp
var settings = new NpgsqlConnectionStringBuilder(connectionString)
{
    ApplicationName = AppName,   // "rental", Konstante ist vorgegeben
    // Poolgrenze, Timeout, Auto-Prepare: siehe Tabelle
};
return new NpgsqlDataSourceBuilder(settings.ConnectionString).Build();
```

Die Eigenschaften des Builders entsprechen den Parametern der
Verbindungszeichenfolge:

| Eigenschaft            | Parameter                 | Wert für diese Übung | Warum                                                                      |
| ---------------------- | ------------------------- | -------------------- | -------------------------------------------------------------------------- |
| `ApplicationName`      | `Application Name`        | `rental`             | Erscheint in `pg_stat_activity.application_name`; die Tests filtern danach |
| `MaxPoolSize`          | `Maximum Pool Size`       | `4`                  | Obergrenze offener Serververbindungen dieser DataSource                    |
| `MinPoolSize`          | `Minimum Pool Size`       | `0`                  | Keine Verbindung wird ohne Bedarf offen gehalten                           |
| `Timeout`              | `Timeout`                 | `5` oder kleiner     | Sekunden Wartezeit auf eine freie Poolverbindung                           |
| `MaxAutoPrepare`       | `Max Auto Prepare`        | `20`                 | Höchstzahl automatisch vorbereiteter Anweisungen je Verbindung             |
| `AutoPrepareMinUsages` | `Auto Prepare Min Usages` | `2`                  | Ab der zweiten Ausführung wird vorbereitet                                 |

Nach diesem Schritt kompilieren die Tests weiterhin. Vier Tests rufen
den Katalog auf und scheitern deshalb mit `NotImplementedException` aus
dem Katalog: die beiden Suchtests,
`DataSource_sets_application_name_rental` und
`Connection_returns_to_pool_after_use`. Der Pool-Test
und der Prepared-Test prüfen nur die DataSource und werden schon grün. Der
Bonus-Test bleibt rot, bis `Options` gesetzt ist.

### 2. Suche nach Kategorie

Datei `Rental/Devices/DeviceCatalog.cs`. Die Klasse bekommt die
DataSource im Konstruktor (`DeviceCatalog(NpgsqlDataSource dataSource)`).

`dataSource.CreateCommand(sql)` liefert ein Kommando, das sich beim Ausführen
selbst eine Verbindung aus dem Pool holt und sie danach zurückgibt. Werte
gehen als Positionsparameter `$1`, `$2` in den SQL-Text und als
`NpgsqlParameter` in die Parameterliste:

```csharp
await using var cmd = dataSource.CreateCommand(
    "SELECT id, name, category FROM rental.device WHERE category = $1 ORDER BY name");
cmd.Parameters.Add(new NpgsqlParameter<string> { TypedValue = category });
await using var reader = await cmd.ExecuteReaderAsync(ct);
while (await reader.ReadAsync(ct))
{
    // reader.GetInt32(0), reader.GetString(1), reader.GetString(2)
}
```

Die Ergebnisliste ist nach Name sortiert; der Test erwartet
`Akkuschrauber` vor `Bohrhammer`.

### 3. Suche nach Namensbestandteil

Gleiche Datei, `FindByNameAsync(string searchTerm)`. Gesucht wird
ohne Rücksicht auf Groß- und Kleinschreibung nach einem Teil des Namens.
Der Suchbegriff bleibt ein Parameter. Die Prozentzeichen des Musters setzt
der SQL-Text um den Parameter, der Wert selbst bleibt ohne `%`:

```sql
WHERE name ILIKE '%' || $1 || '%'
```

Der Test übergibt als Suchbegriff `'; DROP TABLE rental.device; --`. Mit
einem Parameter ist das ein harmloser Text ohne Treffer. Wer den Begriff
in den SQL-Text einbaut, verliert die Tabelle; die Fixture legt sie beim
nächsten Lauf zwar neu an, der Test ist trotzdem rot.

### 4. Verbindungen zurückgeben

`await using` auf Kommando und Reader sorgt dafür, dass die Verbindung nach
dem Lesen an den Pool zurückgeht. Auf dem Server bleibt sie offen, aber im
Zustand `idle`. Ein vergessenes `Dispose` hält sie `active` oder in einer
Transaktion; genau das prüft der Test
`Connection_returns_to_pool_after_use`.

## Die Tests im Detail

| Test                                           | Was er tut                                                                                   | Wenn er rot ist                                                             |
| ---------------------------------------------- | -------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------- |
| `Find_by_category_returns_expected_devices`    | Ruft `FindByCategoryAsync("drills")` auf, erwartet zwei Namen in Reihenfolge                 | Sortierung fehlt oder falsche Spalte im WHERE                               |
| `Search_uses_parameters_not_concatenation`     | Sucht mit dem DROP-Text, erwartet 0 Treffer und weiterhin 6 Geräte; sucht dann `säge`        | Suchbegriff wurde in den SQL-Text eingebaut, oder ILIKE fehlt               |
| `DataSource_sets_application_name_rental`      | Nach einer Suche zählt er Verbindungen mit `application_name = 'rental'`                     | `ApplicationName` nicht gesetzt oder anders geschrieben                     |
| `Connection_returns_to_pool_after_use`         | Nach einer Suche darf keine Anwendungsverbindung `active` oder `idle in transaction` sein    | Reader oder Kommando nicht disposed, Transaktion offen gelassen             |
| `Pool_holds_at_most_four_server_connections`   | Öffnet vier Verbindungen aus deiner DataSource, zählt sie am Server, fordert eine fünfte an  | `MaxPoolSize` fehlt (Standard 100), `Timeout` zu groß (Standard 15 s)       |
| `DataSource_auto_prepares_repeated_statements` | Führt dieselbe Anweisung dreimal auf einer Verbindung aus und liest `pg_prepared_statements` | `MaxAutoPrepare` ist 0 (Standard); Auto-Prepare ist in Npgsql ausgeschaltet |

Die Pool-Tests zählen die Verbindungen am Server. Wer `pg_stat_activity` in
pgAdmin parallel offen hat, sieht die vier Verbindungen mit dem Namen
`rental` und den Test selbst als `rental_test`.

## Fallstricke

- `NpgsqlDataSource.Create(connectionString)` ohne Builder funktioniert, setzt
  aber keine der geforderten Einstellungen.
- Ein `MaxPoolSize` kleiner als 4 lässt den Pool-Test scheitern, weil er
  vier Verbindungen gleichzeitig hält. Ein Wert über 4 lässt ihn ebenfalls
  scheitern, weil die fünfte Anforderung dann durchgeht.
- Auto-Prepare gilt je Serververbindung. Der Test öffnet deshalb eine
  Verbindung aus deiner DataSource und führt die Anweisung dreimal genau
  dort aus.

## Bonus

`Statement_timeout_applies_per_connection` (Trait `Stretch=true`): Der Test
führt `SELECT pg_sleep(5)` über deine DataSource aus und erwartet eine
`PostgresException` mit SQLSTATE `57014`. Der Parameter `Options` der
Verbindungszeichenfolge übergibt Serverparameter beim Verbindungsaufbau:

```csharp
Options = "-c statement_timeout=4s",
```

Die Grenze gilt dann für jede Anweisung deiner Anwendung in allen weiteren
Übungen. Der Import in Übung 4 muss darunter bleiben; mit COPY ist das kein
Problem, mit Einzel-INSERTs schon.

## Fertig, wenn

`dotnet test --filter-trait Exercise=01 --filter-not-trait Stretch=true` sechs
grüne Tests meldet. Danach: eigene Arbeit committen und mit
`git checkout -b meine-uebung-02 uebung-02-start` weiter.
