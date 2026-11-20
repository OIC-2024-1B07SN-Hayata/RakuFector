using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FXForge.AudioEffects;
using Microsoft.Data.Sqlite;

namespace FXForge.Data;

public sealed record PresetListItem(long Id, string Name);

public sealed record SavedTone(
    ToneSettings Settings,
    bool OutputEnabled);

public sealed class RakufekutaDatabase
{
    private const int ApplicationId = 1380337490;

    private static readonly SemaphoreSlim Gate = new(1, 1);

    public string DatabasePath { get; }

    public RakufekutaDatabase(string? path = null)
    {
        DatabasePath = Path.GetFullPath(path ?? Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "FXForge",
            "rakufekuta-.db"));
    }

    public Task<IReadOnlyList<PresetListItem>> ListAsync() =>
        Run(() =>
        {
            using var connection = Open();
            using var command = connection.CreateCommand();

            command.CommandText = """
                SELECT PRESET_ID, PRESET_NAME
                FROM preset
                ORDER BY PRESET_ID;
                """;

            using var reader = command.ExecuteReader();

            var items = new List<PresetListItem>();

            while (reader.Read())
            {
                items.Add(new PresetListItem(
                    reader.GetInt64(0),
                    reader.GetString(1)));
            }

            return (IReadOnlyList<PresetListItem>)items;
        });

    public Task<long> SaveNewAsync(string name, SavedTone tone)
    {
        name = (name ?? "").Trim();

        if (name.Length is < 1 or > 80)
        {
            throw new ArgumentException(
                "プリセット名は1～80文字で入力してください。");
        }

        Validate(tone);

        return Run(() =>
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();

            using (var command = Command(
                connection,
                transaction,
                """
                SELECT COUNT(*)
                FROM preset
                WHERE PRESET_NAME = $name;
                """,
                ("$name", name)))
            {
                if (Convert.ToInt64(command.ExecuteScalar()) != 0)
                {
                    throw new InvalidOperationException(
                        "同じ名前が登録されています。\n" +
                        "別の名前で新規保存してください。");
                }
            }

            var settings = tone.Settings;

            Execute(
                connection,
                transaction,
                """
                INSERT INTO preset (
                    PRESET_NAME,
                    OUTPUT_LEVEL,
                    OUTPUT_ENABLED,
                    REVERSE_DRIVES
                )
                VALUES ($name, $level, $enabled, $reverse);
                """,
                ("$name", name),
                ("$level", settings.OutputLevel * 100),
                ("$enabled", tone.OutputEnabled ? 1 : 0),
                ("$reverse", settings.ReverseDrives ? 1 : 0));

            long id;

            using (var command = Command(
                connection,
                transaction,
                "SELECT last_insert_rowid();"))
            {
                id = Convert.ToInt64(command.ExecuteScalar());
            }

            InsertDrive(
                connection, transaction, id, "A", settings.A);

            InsertDrive(
                connection, transaction, id, "B", settings.B);

            var amp = settings.Amp;

            Execute(
                connection,
                transaction,
                """
                INSERT INTO amp (
                    PRESET_ID, GAIN, BASS, MIDDLE,
                    HIGH, TREBLE, LEVEL, SWITCH
                )
                VALUES (
                    $id, $gain, $bass, $middle,
                    $high, $treble, $level, $enabled
                );
                """,
                ("$id", id),
                ("$gain", amp.Gain * 100),
                ("$bass", amp.Bass * 100),
                ("$middle", amp.Middle * 100),
                ("$high", amp.High * 100),
                ("$treble", amp.Treble * 100),
                ("$level", amp.Level * 100),
                ("$enabled", amp.Enabled ? 1 : 0));

            transaction.Commit();

            return id;
        });
    }

    /// <summary>
    /// プリセットID・名前を維持して設定を更新する。
    /// 全処理が成功したときだけコミットする。
    /// </summary>
    public Task UpdateAsync(long id, SavedTone tone)
    {
        if (id <= 0)
            throw new ArgumentOutOfRangeException(nameof(id));

        Validate(tone);

        return Run(() =>
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();

            var settings = tone.Settings;

            using (var command = Command(
                connection,
                transaction,
                """
                UPDATE preset
                SET OUTPUT_LEVEL = $level,
                    OUTPUT_ENABLED = $enabled,
                    REVERSE_DRIVES = $reverse
                WHERE PRESET_ID = $id;
                """,
                ("$id", id),
                ("$level", settings.OutputLevel * 100),
                ("$enabled", tone.OutputEnabled ? 1 : 0),
                ("$reverse", settings.ReverseDrives ? 1 : 0)))
            {
                if (command.ExecuteNonQuery() != 1)
                {
                    throw new InvalidOperationException(
                        "更新対象のプリセットがありません。");
                }
            }

            UpdateDrive(
                connection, transaction, id, "A", settings.A);

            UpdateDrive(
                connection, transaction, id, "B", settings.B);

            var amp = settings.Amp;

            using (var command = Command(
                connection,
                transaction,
                """
                UPDATE amp
                SET GAIN = $gain,
                    BASS = $bass,
                    MIDDLE = $middle,
                    HIGH = $high,
                    TREBLE = $treble,
                    LEVEL = $level,
                    SWITCH = $enabled
                WHERE PRESET_ID = $id;
                """,
                ("$id", id),
                ("$gain", amp.Gain * 100),
                ("$bass", amp.Bass * 100),
                ("$middle", amp.Middle * 100),
                ("$high", amp.High * 100),
                ("$treble", amp.Treble * 100),
                ("$level", amp.Level * 100),
                ("$enabled", amp.Enabled ? 1 : 0)))
            {
                if (command.ExecuteNonQuery() != 1)
                {
                    throw new InvalidDataException(
                        "更新対象のAMPデータがありません。");
                }
            }

            transaction.Commit();

            return true;
        });
    }

    public Task<SavedTone> LoadAsync(long id) =>
        Run(() =>
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();

            double output;
            bool outputEnabled;
            bool reverse;

            using (var command = Command(
                connection,
                transaction,
                """
                SELECT
                    OUTPUT_LEVEL,
                    OUTPUT_ENABLED,
                    REVERSE_DRIVES
                FROM preset
                WHERE PRESET_ID = $id;
                """,
                ("$id", id)))
            using (var reader = command.ExecuteReader())
            {
                if (!reader.Read())
                {
                    throw new InvalidOperationException(
                        "選択したプリセットが見つかりません。");
                }

                output = ReadValue(reader, 0);
                outputEnabled = ReadFlag(reader, 1);
                reverse = ReadFlag(reader, 2);
            }

            var drives = new Dictionary<string, DriveSettings>();

            using (var command = Command(
                connection,
                transaction,
                """
                SELECT
                    SLOT, TYPE, GAIN, TONE, LEVEL, SWITCH, 0
                FROM overdrive
                WHERE PRESET_ID = $id

                UNION ALL

                SELECT
                    SLOT, TYPE, GAIN, TONE, LEVEL, SWITCH, 1
                FROM distortion
                WHERE PRESET_ID = $id;
                """,
                ("$id", id)))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    string slot = reader.GetString(0);
                    long type = reader.GetInt64(1);

                    DriveKind kind =
                        (reader.GetInt64(6), type) switch
                        {
                            (0, 0) => DriveKind.Sd1,
                            (0, 1) => DriveKind.Bd2,
                            (0, 2) => DriveKind.Ts9,
                            (1, 0) => DriveKind.Ds1,
                            (1, 1) => DriveKind.Rat,

                            _ => throw new InvalidDataException(
                                "未対応の歪み種別です。")
                        };

                    var drive = new DriveSettings(
                        kind,
                        ReadFlag(reader, 5),
                        ReadValue(reader, 2),
                        ReadValue(reader, 3),
                        ReadValue(reader, 4));

                    if (slot is not ("A" or "B") ||
                        !drives.TryAdd(slot, drive))
                    {
                        throw new InvalidDataException(
                            "歪みスロットが重複または不正です。");
                    }
                }
            }

            if (!drives.ContainsKey("A") ||
                !drives.ContainsKey("B"))
            {
                throw new InvalidDataException(
                    "DRIVE A/Bのデータが不足しています。");
            }

            AmpSettings amp;

            using (var command = Command(
                connection,
                transaction,
                """
                SELECT
                    SWITCH, GAIN, BASS, MIDDLE,
                    TREBLE, LEVEL, HIGH
                FROM amp
                WHERE PRESET_ID = $id;
                """,
                ("$id", id)))
            using (var reader = command.ExecuteReader())
            {
                if (!reader.Read())
                {
                    throw new InvalidDataException(
                        "AMPのデータがありません。");
                }

                amp = new AmpSettings(
                    ReadFlag(reader, 0),
                    ReadValue(reader, 1),
                    ReadValue(reader, 2),
                    ReadValue(reader, 3),
                    ReadValue(reader, 4),
                    ReadValue(reader, 5))
                {
                    High = ReadValue(reader, 6)
                };

                if (reader.Read())
                {
                    throw new InvalidDataException(
                        "AMPのデータが重複しています。");
                }
            }

            var result = new SavedTone(
                new ToneSettings(
                    drives["A"],
                    drives["B"],
                    reverse,
                    amp,
                    output),
                outputEnabled);

            Validate(result);

            transaction.Commit();

            return result;
        });

    private static async Task<T> Run<T>(Func<T> action)
    {
        await Gate.WaitAsync().ConfigureAwait(false);

        try
        {
            return await Task.Run(action).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    private SqliteConnection Open()
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(DatabasePath)!);

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = false,
            DefaultTimeout = 5
        };

        var connection = new SqliteConnection(builder.ToString());

        try
        {
            connection.Open();

            using var transaction = connection.BeginTransaction();

            long count;

            using (var command = Command(
                connection,
                transaction,
                """
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE name NOT LIKE 'sqlite_%';
                """))
            {
                count = Convert.ToInt64(command.ExecuteScalar());
            }

            if (count == 0)
            {
                Execute(connection, transaction, SchemaSql);

                Execute(
                    connection,
                    transaction,
                    $"PRAGMA application_id = {ApplicationId};" +
                    "PRAGMA user_version = 2;");
            }
            else
            {
                long appId;
                long version;

                using (var command = Command(
                    connection,
                    transaction,
                    "PRAGMA application_id;"))
                {
                    appId = Convert.ToInt64(command.ExecuteScalar());
                }

                using (var command = Command(
                    connection,
                    transaction,
                    "PRAGMA user_version;"))
                {
                    version = Convert.ToInt64(command.ExecuteScalar());
                }

                if (appId != ApplicationId || version != 2)
                {
                    throw new InvalidDataException(
                        "既存DBは今回の保存形式と異なります。\n" +
                        "変更せず処理を中止しました。\n\n" +
                        DatabasePath);
                }
            }

            transaction.Commit();

            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static (string Table, int Type) DriveMapping(
        DriveKind kind)
    {
        return kind switch
        {
            DriveKind.Sd1 => ("overdrive", 0),
            DriveKind.Bd2 => ("overdrive", 1),
            DriveKind.Ts9 => ("overdrive", 2),
            DriveKind.Ds1 => ("distortion", 0),
            DriveKind.Rat => ("distortion", 1),

            _ => throw new InvalidDataException(
                "未対応の歪み種別です。")
        };
    }

    private static void InsertDrive(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long id,
        string slot,
        DriveSettings drive)
    {
        var mapping = DriveMapping(drive.Kind);

        Execute(
            connection,
            transaction,
            $"INSERT INTO {mapping.Table} " +
            "(PRESET_ID, SLOT, TYPE, GAIN, TONE, LEVEL, SWITCH) " +
            "VALUES ($id, $slot, $type, $gain, $tone, $level, $enabled);",
            ("$id", id),
            ("$slot", slot),
            ("$type", mapping.Type),
            ("$gain", drive.Drive * 100),
            ("$tone", drive.Tone * 100),
            ("$level", drive.Level * 100),
            ("$enabled", drive.Enabled ? 1 : 0));
    }

    private static void UpdateDrive(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long id,
        string slot,
        DriveSettings drive)
    {
        string? oldTable = null;

        using (var command = Command(
            connection,
            transaction,
            """
            SELECT 'overdrive'
            FROM overdrive
            WHERE PRESET_ID = $id AND SLOT = $slot

            UNION ALL

            SELECT 'distortion'
            FROM distortion
            WHERE PRESET_ID = $id AND SLOT = $slot;
            """,
            ("$id", id),
            ("$slot", slot)))
        using (var reader = command.ExecuteReader())
        {
            if (reader.Read())
                oldTable = reader.GetString(0);

            if (oldTable is null || reader.Read())
            {
                throw new InvalidDataException(
                    "更新対象のDRIVEデータが不足または重複しています。");
            }
        }

        var mapping = DriveMapping(drive.Kind);

        if (oldTable != mapping.Table)
        {
            // ODとDSをまたぐ種類変更の場合のみ保存先テーブルを移す。
            // 他テーブルから旧行への参照があれば外部キー制約で停止し、
            // 更新全体がロールバックされる。
            Execute(
                connection,
                transaction,
                $"DELETE FROM {oldTable} " +
                "WHERE PRESET_ID = $id AND SLOT = $slot;",
                ("$id", id),
                ("$slot", slot));

            InsertDrive(
                connection,
                transaction,
                id,
                slot,
                drive);

            return;
        }

        // 同じ分類なら行IDを保持したまま更新する。
        Execute(
            connection,
            transaction,
            $"UPDATE {mapping.Table} " +
            "SET TYPE = $type, GAIN = $gain, TONE = $tone, " +
            "LEVEL = $level, SWITCH = $enabled " +
            "WHERE PRESET_ID = $id AND SLOT = $slot;",
            ("$id", id),
            ("$slot", slot),
            ("$type", mapping.Type),
            ("$gain", drive.Drive * 100),
            ("$tone", drive.Tone * 100),
            ("$level", drive.Level * 100),
            ("$enabled", drive.Enabled ? 1 : 0));
    }

    private static SqliteCommand Command(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        params (string Name, object Value)[] values)
    {
        var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = sql;

        foreach (var value in values)
        {
            command.Parameters.AddWithValue(
                value.Name,
                value.Value);
        }

        return command;
    }

    private static void Execute(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        params (string Name, object Value)[] values)
    {
        using var command = Command(
            connection,
            transaction,
            sql,
            values);

        command.ExecuteNonQuery();
    }

    private static double ReadValue(
        SqliteDataReader reader,
        int ordinal)
    {
        double value = reader.GetDouble(ordinal) / 100;

        CheckUnit(value);

        return value;
    }

    private static bool ReadFlag(
        SqliteDataReader reader,
        int ordinal)
    {
        return reader.GetInt64(ordinal) switch
        {
            0 => false,
            1 => true,

            _ => throw new InvalidDataException(
                "ON/OFFの値が不正です。")
        };
    }

    public static void Validate(SavedTone tone)
    {
        ArgumentNullException.ThrowIfNull(tone);
        ArgumentNullException.ThrowIfNull(tone.Settings);

        var settings = tone.Settings;

        foreach (var drive in new[] { settings.A, settings.B })
        {
            ArgumentNullException.ThrowIfNull(drive);

            if (!Enum.IsDefined(drive.Kind))
            {
                throw new InvalidDataException(
                    "歪み種別が不正です。");
            }

            CheckUnit(drive.Drive);
            CheckUnit(drive.Tone);
            CheckUnit(drive.Level);
        }

        ArgumentNullException.ThrowIfNull(settings.Amp);

        var amp = settings.Amp;

        foreach (double value in new[]
        {
            amp.Gain,
            amp.Bass,
            amp.Middle,
            amp.High,
            amp.Treble,
            amp.Level,
            settings.OutputLevel
        })
        {
            CheckUnit(value);
        }
    }

    private static void CheckUnit(double value)
    {
        if (!double.IsFinite(value) || value < 0 || value > 1)
        {
            throw new InvalidDataException(
                "設定値が0～100の範囲外です。");
        }
    }

    private const string SchemaSql = """
        CREATE TABLE preset (
            PRESET_ID INTEGER PRIMARY KEY AUTOINCREMENT,
            PRESET_NAME TEXT NOT NULL UNIQUE
                CHECK(length(trim(PRESET_NAME)) BETWEEN 1 AND 80),
            OUTPUT_LEVEL REAL NOT NULL
                CHECK(OUTPUT_LEVEL BETWEEN 0 AND 100),
            OUTPUT_ENABLED INTEGER NOT NULL
                CHECK(OUTPUT_ENABLED IN (0,1)),
            REVERSE_DRIVES INTEGER NOT NULL
                CHECK(REVERSE_DRIVES IN (0,1))
        );

        CREATE TABLE overdrive (
            OD_ID INTEGER PRIMARY KEY AUTOINCREMENT,
            PRESET_ID INTEGER NOT NULL
                REFERENCES preset(PRESET_ID),
            TYPE INTEGER NOT NULL CHECK(TYPE IN (0,1,2)),
            GAIN REAL NOT NULL CHECK(GAIN BETWEEN 0 AND 100),
            TONE REAL NOT NULL CHECK(TONE BETWEEN 0 AND 100),
            LEVEL REAL NOT NULL CHECK(LEVEL BETWEEN 0 AND 100),
            SWITCH INTEGER NOT NULL CHECK(SWITCH IN (0,1)),
            SLOT TEXT NOT NULL CHECK(SLOT IN ('A','B')),
            UNIQUE(PRESET_ID,SLOT)
        );

        CREATE TABLE distortion (
            DS_ID INTEGER PRIMARY KEY AUTOINCREMENT,
            PRESET_ID INTEGER NOT NULL
                REFERENCES preset(PRESET_ID),
            TYPE INTEGER NOT NULL CHECK(TYPE IN (0,1)),
            GAIN REAL NOT NULL CHECK(GAIN BETWEEN 0 AND 100),
            TONE REAL NOT NULL CHECK(TONE BETWEEN 0 AND 100),
            LEVEL REAL NOT NULL CHECK(LEVEL BETWEEN 0 AND 100),
            SWITCH INTEGER NOT NULL CHECK(SWITCH IN (0,1)),
            SLOT TEXT NOT NULL CHECK(SLOT IN ('A','B')),
            UNIQUE(PRESET_ID,SLOT)
        );

        CREATE TABLE amp (
            AMP_ID INTEGER PRIMARY KEY AUTOINCREMENT,
            PRESET_ID INTEGER NOT NULL UNIQUE
                REFERENCES preset(PRESET_ID),
            GAIN REAL NOT NULL CHECK(GAIN BETWEEN 0 AND 100),
            BASS REAL NOT NULL CHECK(BASS BETWEEN 0 AND 100),
            MIDDLE REAL NOT NULL CHECK(MIDDLE BETWEEN 0 AND 100),
            HIGH REAL NOT NULL CHECK(HIGH BETWEEN 0 AND 100),
            TREBLE REAL NOT NULL CHECK(TREBLE BETWEEN 0 AND 100),
            SWITCH INTEGER NOT NULL CHECK(SWITCH IN (0,1)),
            LEVEL REAL NOT NULL CHECK(LEVEL BETWEEN 0 AND 100)
        );

        CREATE TABLE delay (
            DELAY_ID INTEGER PRIMARY KEY AUTOINCREMENT,
            PRESET_ID INTEGER NOT NULL
                REFERENCES preset(PRESET_ID),
            TIME REAL CHECK(TIME >= 0),
            FEEDBACK REAL CHECK(FEEDBACK BETWEEN 0 AND 100),
            LEVEL REAL CHECK(LEVEL BETWEEN 0 AND 100),
            SWITCH INTEGER NOT NULL CHECK(SWITCH IN (0,1))
        );

        CREATE TABLE foot_switch (
            foot_ID INTEGER PRIMARY KEY AUTOINCREMENT,
            PRESET_ID INTEGER REFERENCES preset(PRESET_ID),
            DELY_ID INTEGER REFERENCES delay(DELAY_ID),
            OD_ID INTEGER REFERENCES overdrive(OD_ID),
            DS_ID INTEGER REFERENCES distortion(DS_ID),
            AMP_ID INTEGER REFERENCES amp(AMP_ID)
        );

        CREATE TRIGGER od_slot_insert
        BEFORE INSERT ON overdrive
        WHEN EXISTS(
            SELECT 1 FROM distortion
            WHERE PRESET_ID=NEW.PRESET_ID AND SLOT=NEW.SLOT
        )
        BEGIN
            SELECT RAISE(ABORT,'Drive slot already occupied');
        END;

        CREATE TRIGGER ds_slot_insert
        BEFORE INSERT ON distortion
        WHEN EXISTS(
            SELECT 1 FROM overdrive
            WHERE PRESET_ID=NEW.PRESET_ID AND SLOT=NEW.SLOT
        )
        BEGIN
            SELECT RAISE(ABORT,'Drive slot already occupied');
        END;

        CREATE TRIGGER od_slot_update
        BEFORE UPDATE OF PRESET_ID,SLOT ON overdrive
        WHEN EXISTS(
            SELECT 1 FROM distortion
            WHERE PRESET_ID=NEW.PRESET_ID AND SLOT=NEW.SLOT
        )
        BEGIN
            SELECT RAISE(ABORT,'Drive slot already occupied');
        END;

        CREATE TRIGGER ds_slot_update
        BEFORE UPDATE OF PRESET_ID,SLOT ON distortion
        WHEN EXISTS(
            SELECT 1 FROM overdrive
            WHERE PRESET_ID=NEW.PRESET_ID AND SLOT=NEW.SLOT
        )
        BEGIN
            SELECT RAISE(ABORT,'Drive slot already occupied');
        END;
        """;
}