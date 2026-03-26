using System;
using System.Data;
using System.Data.SQLite;
using System.Windows;

namespace BlastWhats
{
    public static class DatabaseHelper
    {
        // Tentukan nama file database
        private static readonly string dbFileName = "blast_history.sqlite";
        // Buat string koneksi
        private static readonly string connectionString = $"Data Source={dbFileName};Version=3;";

        // [BARU] Kunci untuk mencegah tabrakan penulisan database
        private static readonly object dbLock = new object();

        // Method yang dipanggil sekali untuk memastikan database dan tabel ada
        public static void InitializeDatabase()
        {
            try
            {
                // File akan otomatis dibuat oleh SQLite jika belum ada saat Open() dipanggil
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();

                    // [BARU] Aktifkan mode WAL (Write-Ahead Logging) agar database lebih ngebut dan aman
                    using (var pragmaCommand = new SQLiteCommand("PRAGMA journal_mode=WAL;", connection))
                    {
                        pragmaCommand.ExecuteNonQuery();
                    }

                    string createTableQuery = @"
                    CREATE TABLE IF NOT EXISTS Logs (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Timestamp DATETIME NOT NULL,
                        RecipientNumber TEXT NOT NULL,
                        Message TEXT NOT NULL,
                        Status TEXT NOT NULL,
                        Details TEXT
                    );";
                    using (var command = new SQLiteCommand(createTableQuery, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Gagal inisialisasi database: {ex.Message}", "Error Database", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Method untuk menambahkan entri log baru
        public static void AddLog(string number, string message, string status, string details = "")
        {
            // [BARU] Kunci area ini agar hanya ada 1 antrean yang menyimpan log di satu waktu
            lock (dbLock)
            {
                try
                {
                    using (var connection = new SQLiteConnection(connectionString))
                    {
                        connection.Open();
                        string insertQuery = "INSERT INTO Logs (Timestamp, RecipientNumber, Message, Status, Details) VALUES (@ts, @num, @msg, @stat, @det)";
                        using (var command = new SQLiteCommand(insertQuery, connection))
                        {
                            // Gunakan format standar string untuk tanggal agar mudah di-filter nanti
                            command.Parameters.AddWithValue("@ts", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                            command.Parameters.AddWithValue("@num", number);
                            command.Parameters.AddWithValue("@msg", message);
                            command.Parameters.AddWithValue("@stat", status);
                            command.Parameters.AddWithValue("@det", details);
                            command.ExecuteNonQuery();
                        }
                    }
                }
                catch (Exception ex)
                {
                    // [BARU] Jangan pakai MessageBox di background thread. Cukup log ke output
                    Console.WriteLine($"Gagal menyimpan log ke database: {ex.Message}");
                }
            }
        }

        public static DataTable GetLogsByDate(DateTime startDate, DateTime endDate)
        {
            DataTable dt = new DataTable();
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    string selectQuery = "SELECT Timestamp, RecipientNumber, Message, Status, Details FROM Logs WHERE Timestamp >= @start AND Timestamp <= @end ORDER BY Timestamp DESC";
                    using (var command = new SQLiteCommand(selectQuery, connection))
                    {
                        // Sesuaikan format parameter dengan saat insert
                        command.Parameters.AddWithValue("@start", startDate.Date.ToString("yyyy-MM-dd 00:00:00"));
                        command.Parameters.AddWithValue("@end", endDate.Date.ToString("yyyy-MM-dd 23:59:59"));

                        using (var adapter = new SQLiteDataAdapter(command))
                        {
                            adapter.Fill(dt);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Gagal memuat log dari database: {ex.Message}", "Error Database", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return dt;
        }

        // Method untuk mengambil semua data log
        public static DataTable GetLogs()
        {
            DataTable dt = new DataTable();
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    string selectQuery = "SELECT Timestamp, RecipientNumber, Message, Status, Details FROM Logs ORDER BY Timestamp DESC";
                    using (var adapter = new SQLiteDataAdapter(selectQuery, connection))
                    {
                        adapter.Fill(dt);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Gagal memuat log dari database: {ex.Message}", "Error Database", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return dt;
        }
    }
}