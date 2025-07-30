using System;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Windows;

namespace BlastWhats
{
    public static class DatabaseHelper
    {
        // Tentukan nama file database
        private static readonly string dbFileName = "blast_history.sqlite";
        // Buat string koneksi
        private static readonly string connectionString = $"Data Source={dbFileName};Version=3;";

        // Method yang dipanggil sekali untuk memastikan database dan tabel ada
        public static DataTable GetLogsByDate(DateTime startDate, DateTime endDate)
        {
            DataTable dt = new DataTable();
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    // Query diubah untuk menggunakan WHERE clause pada kolom Timestamp
                    string selectQuery = "SELECT Timestamp, RecipientNumber, Message, Status, Details FROM Logs WHERE Timestamp >= @start AND Timestamp <= @end ORDER BY Timestamp DESC";
                    using (var command = new SQLiteCommand(selectQuery, connection))
                    {
                        // Tambahkan parameter untuk mencegah SQL Injection
                        command.Parameters.AddWithValue("@start", startDate.Date); // Ambil bagian tanggal saja
                        command.Parameters.AddWithValue("@end", endDate.Date.AddDays(1).AddTicks(-1)); // Ambil sampai akhir hari

                        using (var adapter = new SQLiteDataAdapter(command))
                        {
                            adapter.Fill(dt);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Gagal memuat log dari database: {ex.Message}");
            }
            return dt;
        }
        public static void InitializeDatabase()
        {
            // Buat file database jika belum ada
            if (!File.Exists(dbFileName))
            {
                SQLiteConnection.CreateFile(dbFileName);
            }

            // Buat tabel jika belum ada
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
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

        // Method untuk menambahkan entri log baru
        public static void AddLog(string number, string message, string status, string details = "")
        {
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    string insertQuery = "INSERT INTO Logs (Timestamp, RecipientNumber, Message, Status, Details) VALUES (@ts, @num, @msg, @stat, @det)";
                    using (var command = new SQLiteCommand(insertQuery, connection))
                    {
                        command.Parameters.AddWithValue("@ts", DateTime.Now);
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
                // Tampilkan pesan error jika gagal menyimpan log
                MessageBox.Show($"Gagal menyimpan log ke database: {ex.Message}");
            }
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
                MessageBox.Show($"Gagal memuat log dari database: {ex.Message}");
            }
            return dt;
        }
    }
}