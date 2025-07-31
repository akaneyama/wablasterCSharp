using Microsoft.Win32;
using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ListView;



namespace BlastWhats
{
    /// <summary>
    /// Interaction logic for BlastPage.xaml
    /// </summary>
    public partial class BlastPage : Page
    {
        // DataTable sekarang menjadi milik halaman ini, bukan MainWindow
        private DataTable excelData;
        private static readonly HttpClient client = new HttpClient();
        public BlastPage()
        {
            InitializeComponent();

            DatabaseHelper.InitializeDatabase();
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

                    // Langsung perbarui daftar variabel setelah upload berhasil
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
        }
        private void LoadLogs()
        {
            LogDataGrid.ItemsSource = DatabaseHelper.GetLogs().DefaultView;
        }

        // Event handler untuk tombol refresh
        private void BtnRefreshLogs_Click(object sender, RoutedEventArgs e)
        {
            LoadLogs();
        }
        private async void BtnStartBlast_Click(object sender, RoutedEventArgs e)
        {
            if (this.excelData == null || this.excelData.Rows.Count == 0) { /* ... validasi ... */ return; }
            if (PhoneNumberColumnComboBox.SelectedItem == null) { /* ... validasi ... */ return; }

            var cts = new CancellationTokenSource();
            var progressWindow = new ProgressWindow();
            var mainWindow = (MainWindow)Application.Current.MainWindow;

            // Definisikan Progress<T> dengan tipe data baru untuk log
            var progress = new Progress<(int count, string log)>(update =>
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

                await Task.Run(async () =>
                {
                    int sentCount = 0;
                    foreach (DataRow row in this.excelData.Rows)
                    {
                        cts.Token.ThrowIfCancellationRequested();
                        string targetPhoneNumber = row[phoneNumberColumnName]?.ToString();
                        string logMessage = $"Mengirim ke {targetPhoneNumber}... ";
                        string status = "Gagal"; // Default status
                        string details = "";
                       

                        if (string.IsNullOrWhiteSpace(targetPhoneNumber))
                        {
                            logMessage += "Nomor kosong, dilewati.";
                            (progress as IProgress<(int, string)>).Report((sentCount, logMessage));
                            continue;
                        }

                        string finalMessage = messageTemplate;
                        foreach (DataColumn col in this.excelData.Columns)
                        {
                            string placeholder = $"{{{col.ColumnName}}}";
                            string value = row[col.ColumnName]?.ToString() ?? "";
                            finalMessage = finalMessage.Replace(placeholder, value, StringComparison.OrdinalIgnoreCase);
                        }

                        // --- KIRIM PESAN KE API NODE.JS ---
                        try
                        {
                            //MessageBox.Show($"${targetPhoneNumber} {logMessage}");
                            //logMessage += "Berhasil.";
                            var payload = new { number = targetPhoneNumber, message = finalMessage };
                            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                            var response = await client.PostAsync("http://localhost:3000/send", content, cts.Token);

                            if (response.IsSuccessStatusCode)
                            {
                                logMessage += "Berhasil.";

                                status = "Berhasil"; // Update status
                            }
                            else
                            {
                                // Baca pesan error dari server jika ada
                                string errorMsg = await response.Content.ReadAsStringAsync();
                                logMessage += $"Gagal ({response.StatusCode}): {errorMsg}";
                            }
                        }
                        catch (Exception ex)
                        {
                            logMessage += $"Error: {ex.Message}";
                        }
                        // ------------------------------------


                        DatabaseHelper.AddLog(targetPhoneNumber, finalMessage, status, details);
                       
                        sentCount++;
                        (progress as IProgress<(int, string)>).Report((sentCount, logMessage));

                        // Beri jeda singkat antar pesan untuk menghindari spam
                        await Task.Delay(500, cts.Token);
                        if (sentCount % 10 == 0 && sentCount < totalMessages)
                        {
                            string pauseLog = $"Mengambil jeda 10 detik...";
                            // Laporkan status jeda ke UI
                            (progress as IProgress<(int, string)>).Report((sentCount, pauseLog));

                            // Jeda selama 10 detik
                            await Task.Delay(10000, cts.Token);
                        }
                    }
                }, cts.Token);

                MessageBox.Show("Proses pengiriman selesai.", "Selesai", MessageBoxButton.OK, MessageBoxImage.Information);
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
        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            // Set tanggal default untuk DatePicker
            EndDatePicker.SelectedDate = DateTime.Now;
            StartDatePicker.SelectedDate = DateTime.Now.AddDays(-7); // Contoh: 7 hari terakhir

            // Langsung muat log saat halaman ditampilkan
            LoadAllLogs();
        }
        private void LoadAllLogs()
        {
            LogDataGrid.ItemsSource = DatabaseHelper.GetLogs().DefaultView;
        }
        private void BtnFilter_Click(object sender, RoutedEventArgs e)
        {
            // Validasi input tanggal
            if (StartDatePicker.SelectedDate == null || EndDatePicker.SelectedDate == null)
            {
                MessageBox.Show("Silakan pilih tanggal mulai dan tanggal akhir.", "Peringatan");
                return;
            }

            DateTime startDate = StartDatePicker.SelectedDate.Value;
            DateTime endDate = EndDatePicker.SelectedDate.Value;

            if (startDate > endDate)
            {
                MessageBox.Show("Tanggal mulai tidak boleh lebih besar dari tanggal akhir.", "Peringatan");
                return;
            }

            // Panggil method database yang baru dengan tanggal yang dipilih
            LogDataGrid.ItemsSource = DatabaseHelper.GetLogsByDate(startDate, endDate).DefaultView;
        }
        private DataTable ReadExcelToDataTable(string filePath)
        {
            DataTable dt = new DataTable();
            int headerRow = 2; // Tentukan baris header
            int startDataRow = 3; // Tentukan baris pertama data

            using (var package = new ExcelPackage(new FileInfo(filePath)))
            {
                ExcelWorksheet worksheet = package.Workbook.Worksheets.FirstOrDefault();
                if (worksheet == null) return dt;

                foreach (var headerCell in worksheet.Cells[headerRow, 1, headerRow, worksheet.Dimension.End.Column])
                {
                    dt.Columns.Add(headerCell.Text);
                }

                for (int rowNum = startDataRow; rowNum <= worksheet.Dimension.End.Row; rowNum++)
                {
                    var wsRow = worksheet.Cells[rowNum, 1, rowNum, dt.Columns.Count];
                    DataRow row = dt.Rows.Add();
                    foreach (var cell in wsRow)
                    {
                        row[cell.Start.Column - 1] = cell.Text;
                    }
                }
            }
            return dt;
        }
    }
}
