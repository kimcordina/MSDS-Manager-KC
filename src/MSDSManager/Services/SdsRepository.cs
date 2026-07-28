using System.IO;
using Microsoft.Data.Sqlite;
using MSDSManager.Models;

namespace MSDSManager.Services;

public sealed class SdsRepository
{
    private readonly string _connectionString;

    public SdsRepository(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
    }

    public void Initialize()
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            CREATE TABLE IF NOT EXISTS documents (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                file_path TEXT NOT NULL UNIQUE,
                file_name TEXT NOT NULL,
                relative_path TEXT NOT NULL,
                category TEXT NOT NULL,
                product_name TEXT NOT NULL,
                product_name_from_pdf TEXT,
                version TEXT,
                revision_date TEXT,
                supplier TEXT,
                status INTEGER NOT NULL DEFAULT 0,
                status_reason TEXT,
                last_supplier_verified_at TEXT,
                indexed_at TEXT NOT NULL,
                file_last_write_utc TEXT NOT NULL,
                file_size_bytes INTEGER NOT NULL,
                is_favourite INTEGER NOT NULL DEFAULT 0,
                last_used_at TEXT,
                extract_preview TEXT
            );

            CREATE TABLE IF NOT EXISTS aliases (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                document_id INTEGER NOT NULL,
                alias TEXT NOT NULL,
                UNIQUE(document_id, alias),
                FOREIGN KEY(document_id) REFERENCES documents(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS packs (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL UNIQUE,
                notes TEXT,
                created_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS pack_items (
                pack_id INTEGER NOT NULL,
                document_id INTEGER NOT NULL,
                PRIMARY KEY(pack_id, document_id),
                FOREIGN KEY(pack_id) REFERENCES packs(id) ON DELETE CASCADE,
                FOREIGN KEY(document_id) REFERENCES documents(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_documents_category ON documents(category);
            CREATE INDEX IF NOT EXISTS idx_documents_product ON documents(product_name);
            CREATE INDEX IF NOT EXISTS idx_aliases_alias ON aliases(alias);

            CREATE TABLE IF NOT EXISTS activity_log (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                created_at TEXT NOT NULL,
                action TEXT NOT NULL,
                detail TEXT,
                document_id INTEGER,
                product_name TEXT
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public void UpsertDocument(SdsDocument doc)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO documents (
                file_path, file_name, relative_path, category, product_name, product_name_from_pdf,
                version, revision_date, supplier, status, status_reason, last_supplier_verified_at,
                indexed_at, file_last_write_utc, file_size_bytes, is_favourite, last_used_at, extract_preview
            ) VALUES (
                $file_path, $file_name, $relative_path, $category, $product_name, $product_name_from_pdf,
                $version, $revision_date, $supplier, $status, $status_reason, $last_supplier_verified_at,
                $indexed_at, $file_last_write_utc, $file_size_bytes, $is_favourite, $last_used_at, $extract_preview
            )
            ON CONFLICT(file_path) DO UPDATE SET
                file_name = excluded.file_name,
                relative_path = excluded.relative_path,
                category = excluded.category,
                product_name = excluded.product_name,
                product_name_from_pdf = excluded.product_name_from_pdf,
                version = excluded.version,
                revision_date = excluded.revision_date,
                supplier = excluded.supplier,
                status = excluded.status,
                status_reason = excluded.status_reason,
                indexed_at = excluded.indexed_at,
                file_last_write_utc = excluded.file_last_write_utc,
                file_size_bytes = excluded.file_size_bytes,
                extract_preview = excluded.extract_preview;
            """;
        AddDocumentParams(cmd, doc);
        cmd.ExecuteNonQuery();
    }

    public void RemoveMissingFiles(IEnumerable<string> existingPaths)
    {
        var keep = existingPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        using var connection = Open();
        using var listCmd = connection.CreateCommand();
        listCmd.CommandText = "SELECT id, file_path FROM documents;";
        var toDelete = new List<long>();
        using (var reader = listCmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var id = reader.GetInt64(0);
                var path = reader.GetString(1);
                if (!keep.Contains(path) || !File.Exists(path))
                    toDelete.Add(id);
            }
        }

        foreach (var id in toDelete)
        {
            using var del = connection.CreateCommand();
            del.CommandText = "DELETE FROM documents WHERE id = $id;";
            del.Parameters.AddWithValue("$id", id);
            del.ExecuteNonQuery();
        }
    }

    public void UpdateStatuses(IEnumerable<SdsDocument> docs)
    {
        using var connection = Open();
        using var tx = connection.BeginTransaction();
        foreach (var doc in docs)
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText =
                """
                UPDATE documents
                SET status = $status, status_reason = $status_reason
                WHERE id = $id;
                """;
            cmd.Parameters.AddWithValue("$status", (int)doc.Status);
            cmd.Parameters.AddWithValue("$status_reason", (object?)doc.StatusReason ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$id", doc.Id);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public IReadOnlyList<SdsDocument> GetAllDocuments()
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM documents ORDER BY category, product_name, file_name;";
        return ReadDocuments(cmd).ToList();
    }

    public IReadOnlyList<SdsDocument> Search(string? query, string? category, bool favouritesOnly = false, bool recentOnly = false)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        var where = new List<string>();

        if (!string.IsNullOrWhiteSpace(category) &&
            !string.Equals(category, "All", StringComparison.OrdinalIgnoreCase))
        {
            // Exact folder or any deeper nested path under it (Kitchen matches Kitchen/Dishwashing)
            where.Add("(d.category = $category OR d.category LIKE $category_like)");
            cmd.Parameters.AddWithValue("$category", category);
            cmd.Parameters.AddWithValue("$category_like", category.TrimEnd('/') + "/%");
        }

        if (favouritesOnly)
            where.Add("d.is_favourite = 1");

        if (recentOnly)
            where.Add("d.last_used_at IS NOT NULL");

        if (!string.IsNullOrWhiteSpace(query))
        {
            where.Add(
                """
                (
                    d.product_name LIKE $q OR
                    d.file_name LIKE $q OR
                    d.relative_path LIKE $q OR
                    IFNULL(d.product_name_from_pdf, '') LIKE $q OR
                    EXISTS (SELECT 1 FROM aliases a WHERE a.document_id = d.id AND a.alias LIKE $q)
                )
                """);
            cmd.Parameters.AddWithValue("$q", $"%{query.Trim()}%");
        }

        var sql = "SELECT d.* FROM documents d";
        if (where.Count > 0)
            sql += " WHERE " + string.Join(" AND ", where);

        sql += recentOnly
            ? " ORDER BY d.last_used_at DESC LIMIT 50;"
            : " ORDER BY d.category, d.product_name, d.file_name;";

        cmd.CommandText = sql;
        return ReadDocuments(cmd).ToList();
    }

    public IReadOnlyList<string> GetCategories()
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT category FROM documents ORDER BY category;";
        var list = new List<string> { "All" };
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(reader.GetString(0));
        return list;
    }

    public void SetFavourite(long documentId, bool isFavourite)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE documents SET is_favourite = $fav WHERE id = $id;";
        cmd.Parameters.AddWithValue("$fav", isFavourite ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", documentId);
        cmd.ExecuteNonQuery();
    }

    public void TouchUsed(IEnumerable<long> documentIds)
    {
        using var connection = Open();
        var now = DateTime.UtcNow.ToString("O");
        foreach (var id in documentIds)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE documents SET last_used_at = $now WHERE id = $id;";
            cmd.Parameters.AddWithValue("$now", now);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    public void AddAlias(long documentId, string alias)
    {
        alias = alias.Trim();
        if (string.IsNullOrWhiteSpace(alias))
            return;

        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            INSERT OR IGNORE INTO aliases (document_id, alias)
            VALUES ($document_id, $alias);
            """;
        cmd.Parameters.AddWithValue("$document_id", documentId);
        cmd.Parameters.AddWithValue("$alias", alias);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<string> GetAliases(long documentId)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT alias FROM aliases WHERE document_id = $id ORDER BY alias;";
        cmd.Parameters.AddWithValue("$id", documentId);
        var list = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(reader.GetString(0));
        return list;
    }

    public long CreatePack(string name, string? notes, IEnumerable<long> documentIds)
    {
        using var connection = Open();
        using var tx = connection.BeginTransaction();

        long packId;
        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText =
                """
                INSERT INTO packs (name, notes, created_at)
                VALUES ($name, $notes, $created_at);
                """;
            cmd.Parameters.AddWithValue("$name", name.Trim());
            cmd.Parameters.AddWithValue("$notes", (object?)notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$created_at", DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }

        using (var idCmd = connection.CreateCommand())
        {
            idCmd.Transaction = tx;
            idCmd.CommandText = "SELECT last_insert_rowid();";
            packId = (long)(idCmd.ExecuteScalar() ?? 0L);
        }

        foreach (var docId in documentIds.Distinct())
        {
            using var item = connection.CreateCommand();
            item.Transaction = tx;
            item.CommandText =
                """
                INSERT OR IGNORE INTO pack_items (pack_id, document_id)
                VALUES ($pack_id, $document_id);
                """;
            item.Parameters.AddWithValue("$pack_id", packId);
            item.Parameters.AddWithValue("$document_id", docId);
            item.ExecuteNonQuery();
        }

        tx.Commit();
        return packId;
    }

    public IReadOnlyList<SavedPack> GetPacks()
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, name, notes, created_at FROM packs ORDER BY name;";
        var packs = new List<SavedPack>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            packs.Add(new SavedPack
            {
                Id = reader.GetInt64(0),
                Name = reader.GetString(1),
                Notes = reader.IsDBNull(2) ? null : reader.GetString(2),
                CreatedAt = DateTime.Parse(reader.GetString(3))
            });
        }

        foreach (var pack in packs)
        {
            using var items = connection.CreateCommand();
            items.CommandText = "SELECT document_id FROM pack_items WHERE pack_id = $id;";
            items.Parameters.AddWithValue("$id", pack.Id);
            using var itemReader = items.ExecuteReader();
            while (itemReader.Read())
                pack.DocumentIds.Add(itemReader.GetInt64(0));
        }

        return packs;
    }

    public void DeletePack(long packId)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM packs WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", packId);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<SdsDocument> GetDocumentsByIds(IEnumerable<long> ids)
    {
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0)
            return [];

        using var connection = Open();
        using var cmd = connection.CreateCommand();
        var paramNames = idList.Select((id, i) =>
        {
            var name = $"$id{i}";
            cmd.Parameters.AddWithValue(name, id);
            return name;
        });
        cmd.CommandText = $"SELECT * FROM documents WHERE id IN ({string.Join(",", paramNames)});";
        return ReadDocuments(cmd).ToList();
    }

    public void MarkSupplierVerified(long documentId, DateTime? verifiedAt = null, string? supplier = null)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            UPDATE documents
            SET last_supplier_verified_at = $verified,
                supplier = COALESCE($supplier, supplier)
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$verified", (verifiedAt ?? DateTime.Today).ToString("O"));
        cmd.Parameters.AddWithValue("$supplier", string.IsNullOrWhiteSpace(supplier) ? DBNull.Value : supplier.Trim());
        cmd.Parameters.AddWithValue("$id", documentId);
        cmd.ExecuteNonQuery();
    }

    public void UpdateSupplier(long documentId, string? supplier)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE documents SET supplier = $supplier WHERE id = $id;";
        cmd.Parameters.AddWithValue("$supplier", string.IsNullOrWhiteSpace(supplier) ? DBNull.Value : supplier.Trim());
        cmd.Parameters.AddWithValue("$id", documentId);
        cmd.ExecuteNonQuery();
    }

    public void LogActivity(string action, string? detail = null, long? documentId = null, string? productName = null)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO activity_log (created_at, action, detail, document_id, product_name)
            VALUES ($created_at, $action, $detail, $document_id, $product_name);
            """;
        cmd.Parameters.AddWithValue("$created_at", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$action", action);
        cmd.Parameters.AddWithValue("$detail", (object?)detail ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$document_id", (object?)documentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$product_name", (object?)productName ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<ActivityLogEntry> GetRecentActivity(int limit = 100)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            SELECT id, created_at, action, detail, document_id, product_name
            FROM activity_log
            ORDER BY id DESC
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$limit", limit);
        var list = new List<ActivityLogEntry>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new ActivityLogEntry
            {
                Id = reader.GetInt64(0),
                CreatedAt = DateTime.Parse(reader.GetString(1)),
                Action = reader.GetString(2),
                Detail = reader.IsDBNull(3) ? null : reader.GetString(3),
                DocumentId = reader.IsDBNull(4) ? null : reader.GetInt64(4),
                ProductName = reader.IsDBNull(5) ? null : reader.GetString(5)
            });
        }
        return list;
    }

    private static void AddDocumentParams(SqliteCommand cmd, SdsDocument doc)
    {
        cmd.Parameters.AddWithValue("$file_path", doc.FilePath);
        cmd.Parameters.AddWithValue("$file_name", doc.FileName);
        cmd.Parameters.AddWithValue("$relative_path", doc.RelativePath);
        cmd.Parameters.AddWithValue("$category", doc.Category);
        cmd.Parameters.AddWithValue("$product_name", doc.ProductName);
        cmd.Parameters.AddWithValue("$product_name_from_pdf", (object?)doc.ProductNameFromPdf ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$version", (object?)doc.Version ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$revision_date", doc.RevisionDate?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$supplier", (object?)doc.Supplier ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$status", (int)doc.Status);
        cmd.Parameters.AddWithValue("$status_reason", (object?)doc.StatusReason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$last_supplier_verified_at", doc.LastSupplierVerifiedAt?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$indexed_at", doc.IndexedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$file_last_write_utc", doc.FileLastWriteUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$file_size_bytes", doc.FileSizeBytes);
        cmd.Parameters.AddWithValue("$is_favourite", doc.IsFavourite ? 1 : 0);
        cmd.Parameters.AddWithValue("$last_used_at", doc.LastUsedAt?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$extract_preview", (object?)doc.ExtractPreview ?? DBNull.Value);
    }

    private static IEnumerable<SdsDocument> ReadDocuments(SqliteCommand cmd)
    {
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            yield return new SdsDocument
            {
                Id = reader.GetInt64(reader.GetOrdinal("id")),
                FilePath = reader.GetString(reader.GetOrdinal("file_path")),
                FileName = reader.GetString(reader.GetOrdinal("file_name")),
                RelativePath = reader.GetString(reader.GetOrdinal("relative_path")),
                Category = reader.GetString(reader.GetOrdinal("category")),
                ProductName = reader.GetString(reader.GetOrdinal("product_name")),
                ProductNameFromPdf = reader.IsDBNull(reader.GetOrdinal("product_name_from_pdf"))
                    ? null : reader.GetString(reader.GetOrdinal("product_name_from_pdf")),
                Version = reader.IsDBNull(reader.GetOrdinal("version"))
                    ? null : reader.GetString(reader.GetOrdinal("version")),
                RevisionDate = reader.IsDBNull(reader.GetOrdinal("revision_date"))
                    ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("revision_date"))),
                Supplier = reader.IsDBNull(reader.GetOrdinal("supplier"))
                    ? null : reader.GetString(reader.GetOrdinal("supplier")),
                Status = (DocumentStatus)reader.GetInt32(reader.GetOrdinal("status")),
                StatusReason = reader.IsDBNull(reader.GetOrdinal("status_reason"))
                    ? null : reader.GetString(reader.GetOrdinal("status_reason")),
                LastSupplierVerifiedAt = reader.IsDBNull(reader.GetOrdinal("last_supplier_verified_at"))
                    ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("last_supplier_verified_at"))),
                IndexedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("indexed_at"))),
                FileLastWriteUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("file_last_write_utc"))),
                FileSizeBytes = reader.GetInt64(reader.GetOrdinal("file_size_bytes")),
                IsFavourite = reader.GetInt32(reader.GetOrdinal("is_favourite")) == 1,
                LastUsedAt = reader.IsDBNull(reader.GetOrdinal("last_used_at"))
                    ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("last_used_at"))),
                ExtractPreview = reader.IsDBNull(reader.GetOrdinal("extract_preview"))
                    ? null : reader.GetString(reader.GetOrdinal("extract_preview"))
            };
        }
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys = ON;";
        cmd.ExecuteNonQuery();
        return connection;
    }
}
