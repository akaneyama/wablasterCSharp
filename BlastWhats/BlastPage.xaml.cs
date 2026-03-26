using Microsoft.Win32;
using OfficeOpenXml; // Pastikan menggunakan EPPlus
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Net.Http;
using System.Text.Json;

namespace BlastWhats
{
    /// <summary>
    /// Interaction logic for BlastPage.xaml
    /// </summary>
    public partial class BlastPage : Page
    {
        private DataTable excelData;

        // HttpClient sebaiknya static agar tidak menghabiskan socket (Best Practice)
        private static readonly HttpClient client = new HttpClient();

        public BlastPage()
        {
            InitializeComponent();

            // [BARU] Wajib untuk EPPlus versi 5 ke atas agar tidak error saat membaca Excel
            ExcelPackage.License.SetNonCommercialPersonal("Penggunaan Pribadi");

            DatabaseHelper.InitializeDatabase();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            EndDatePicker.SelectedDate = DateTime.Now;
            StartDatePicker.SelectedDate = DateTime.Now.AddDays(-7);

            LoadAllLogs();
        }

        private void BtnUploadExcel_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Excel Files|*.xlsx;*.xls|All files|*.*",
                Title = "Pilih File Excel"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string filePath = openFileDialog.FileName;
                FilePathText.Text = $"File: {System.IO.Path.GetFileName(filePath)}";

                try
                {
                    this.excelData = ReadExcelToDataTable(filePath);
                    ExcelDataGrid.ItemsSource = this.excelData.DefaultView;

                    UpdateVariableList();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Terjadi error saat membaca file Excel: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void UpdateVariableList()
        {
            if (this.excelData == null) return;

            var formattedVariableNames = new List<string>();
            var rawColumnNames = new List<string>();

            foreach (DataColumn col in this.excelData.Columns)
            {
                formattedVariableNames.Add($"{{{col.ColumnName}}}");
                rawColumnNames.Add(col.ColumnName);
            }

            VariablesListBox.ItemsSource = formattedVariableNames;
            PhoneNumberColumnComboBox.ItemsSource = rawColumnNames;

            // Otomatis pilih item pertama jika ada
            if (rawColumnNames.Count > 0)
                PhoneNumberColumnComboBox.SelectedIndex = 0;
        }

        private void LoadAllLogs()
        {
            LogDataGrid.ItemsSource = DatabaseHelper.GetLogs().DefaultView;
        }

        private void BtnRefreshLogs_Click(object sender, RoutedEventArgs e)
        {
            LoadAllLogs();
        }

        private void BtnFilter_Click(object sender, RoutedEventArgs e)
        {
            if (StartDatePicker.SelectedDate == null || EndDatePicker.SelectedDate == null)
            {
                MessageBox.Show("Silakan pilih tanggal mulai dan tanggal akhir.", "Peringatan", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DateTime startDate = StartDatePicker.SelectedDate.Value;
            DateTime endDate = EndDatePicker.SelectedDate.Value;

            if (startDate > endDate)
            {
                MessageBox.Show("Tanggal mulai tidak boleh lebih besar dari tanggal akhir.", "Peringatan", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            LogDataGrid.ItemsSource = DatabaseHelper.GetLogsByDate(startDate, endDate).DefaultView;
        }

        private async void BtnStartBlast_Click(object sender, RoutedEventArgs e)
        {
            // [BARU] Lengkapi validasi
            if (this.excelData == null || this.excelData.Rows.Count == 0)
            {
                MessageBox.Show("Silakan unggah file Excel yang berisi data terlebih dahulu.", "Peringatan", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (PhoneNumberColumnComboBox.SelectedItem == null)
            {
                MessageBox.Show("Silakan pilih kolom mana yang berisi Nomor WA.", "Peringatan", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var cts = new CancellationTokenSource();
            var progressWindow = new ProgressWindow();
            var mainWindow = (MainWindow)Application.Current.MainWindow;

            // [BARU] Cast IProgress di awal agar penulisan di bawah lebih bersih
            IProgress<(int count, string log)> progressReporter = new Progress<(int count, string log)>(update =>
            {
                progressWindow.UpdateProgress(update.count, this.excelData.Rows.Count, update.log);
            });

            try
            {
                mainWindow?.SetUiEnabled(false);
                progressWindow.CancelClicked += (s, ev) => { cts.Cancel(); };
                progressWindow.Show();

                string messageTemplate = MessageTextBox.Text;
                string phoneNumberColumnName = PhoneNumberColumnComboBox.SelectedItem.ToString();
                int totalMessages = this.excelData.Rows.Count;

                // [BARU] Ambil nama-nama kolom ke dalam List untuk menghindari cross-thread exception di dalam Task.Run
                var columnNames = this.excelData.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();

                // [BARU] Ekstrak data baris menjadi list kamus (dictionary) agar aman dibaca di background thread
                var rowsData = new List<Dictionary<string, string>>();
                foreach (DataRow row in this.excelData.Rows)
                {
                    var rowDict = new Dictionary<string, string>();
                    foreach (var col in columnNames)
                    {
                        rowDict[col] = row[col]?.ToString() ?? "";
                    }
                    rowsData.Add(rowDict);
                }

                await Task.Run(async () =>
                {
                    int sentCount = 0;

                    foreach (var rowDict in rowsData)
                    {
                        cts.Token.ThrowIfCancellationRequested();

                        string targetPhoneNumber = rowDict.ContainsKey(phoneNumberColumnName) ? rowDict[phoneNumberColumnName] : "";
                        string logMessage = $"Mengirim ke {targetPhoneNumber}... ";
                        string status = "Gagal";
                        string details = "";

                        if (string.IsNullOrWhiteSpace(targetPhoneNumber))
                        {
                            logMessage += "Nomor kosong, dilewati.";
                            progressReporter.Report((sentCount, logMessage));
                            continue;
                        }

                        // Replace variabel pesan
                        string finalMessage = messageTemplate;
                        foreach (var colName in columnNames)
                        {
                            string placeholder = $"{{{colName}}}";
                            string value = rowDict[colName];
                            finalMessage = finalMessage.Replace(placeholder, value, StringComparison.OrdinalIgnoreCase);
                        }

                        // --- KIRIM PESAN KE API NODE.JS ---
                        try
                        {
                            var payload = new { number = targetPhoneNumber, message = finalMessage };
                            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                            // [BARU] Gunakan 'using' agar memory response langsung dihapus setelah dipakai
                            using var response = await client.PostAsync("http://localhost:3000/send", content, cts.Token);

                            if (response.IsSuccessStatusCode)
                            {
                                logMessage += "Berhasil.";
                                status = "Berhasil";
                            }
                            else
                            {
                                string errorMsg = await response.Content.ReadAsStringAsync();
                                logMessage += $"Gagal ({response.StatusCode}): {errorMsg}";
                                details = errorMsg; // Simpan ke detail database
                            }
                        }
                        catch (Exception ex)
                        {
                            logMessage += $"Error API: {ex.Message}";
                            details = ex.Message;
                        }
                        // ------------------------------------

                        DatabaseHelper.AddLog(targetPhoneNumber, finalMessage, status, details);

                        sentCount++;
                        progressReporter.Report((sentCount, logMessage));

                        // Beri jeda 500ms agar aman dari blokir WA
                        await Task.Delay(500, cts.Token);

                        // Jeda panjang setiap 10 pesan
                        if (sentCount % 10 == 0 && sentCount < totalMessages)
                        {
                            progressReporter.Report((sentCount, "Mengambil jeda 10 detik untuk keamanan..."));
                            await Task.Delay(10000, cts.Token);
                        }
                    }
                }, cts.Token);

                MessageBox.Show("Proses pengiriman selesai.", "Selesai", MessageBoxButton.OK, MessageBoxImage.Information);

                // Segarkan log otomatis setelah pengiriman selesai
                LoadAllLogs();
            }
            catch (OperationCanceledException)
            {
                MessageBox.Show("Proses dibatalkan oleh pengguna.", "Dibatalkan", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Terjadi error saat proses blast: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                progressWindow.Close();
                mainWindow?.SetUiEnabled(true);
            }
        }

        private DataTable ReadExcelToDataTable(string filePath)
        {
            DataTable dt = new DataTable();
            int headerRow = 2; // Baris header
            int startDataRow = 3; // Baris data

            using (var package = new ExcelPackage(new FileInfo(filePath)))
            {
                ExcelWorksheet worksheet = package.Workbook.Worksheets.FirstOrDefault();
                if (worksheet == null) return dt;

                // Membaca Header
                for (int col = 1; col <= worksheet.Dimension.End.Column; col++)
                {
                    string headerText = worksheet.Cells[headerRow, col].Text;
                    // Beri nama default jika header kosong agar tidak error
                    if (string.IsNullOrWhiteSpace(headerText)) headerText = $"Column{col}";

                    // Pastikan nama kolom unik
                    while (dt.Columns.Contains(headerText))
                    {
                        headerText += "_1";
                    }

                    dt.Columns.Add(headerText);
                }

                // Membaca Data
                for (int rowNum = startDataRow; rowNum <= worksheet.Dimension.End.Row; rowNum++)
                {
                    DataRow row = dt.Rows.Add();
                    for (int col = 1; col <= dt.Columns.Count; col++)
                    {
                        row[col - 1] = worksheet.Cells[rowNum, col].Text;
                    }
                }
            }
            return dt;
        }
    }
}