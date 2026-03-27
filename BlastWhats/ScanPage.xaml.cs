using Microsoft.WindowsAPICodePack.Dialogs;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace BlastWhats
{
    /// <summary>
    /// Interaction logic for ScanPage.xaml
    /// </summary>
    public partial class ScanPage : Page
    {
        private Process nodeProcess;
        private string nodeServerPath;
        private static readonly HttpClient client = new HttpClient();
        private DispatcherTimer qrCodeUpdateTimer;

        public ScanPage()
        {
            InitializeComponent();
            SetupQRCodeUpdateTimer();

            // Coba temukan folder NodeJsServer secara otomatis saat halaman dimuat
            AutoDetectNodeServerPath();

            // [BARU] Mendaftarkan TRIGGER saat aplikasi (Window) ditutup agar proses Node mati
            Application.Current.Exit += Application_Exit;
        }

        // Fungsi trigger untuk memastikan server mati saat aplikasi di-close
        private void Application_Exit(object sender, ExitEventArgs e)
        {
            StopNodeServer();
        }

        private void AutoDetectNodeServerPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            DirectoryInfo dirInfo = new DirectoryInfo(baseDir);

            for (int i = 0; i < 5; i++)
            {
                if (dirInfo == null) break;

                string potentialPath = Path.Combine(dirInfo.FullName, "NodeJsServer");
                if (Directory.Exists(potentialPath) && File.Exists(Path.Combine(potentialPath, "server.js")))
                {
                    this.nodeServerPath = potentialPath;
                    break;
                }
                dirInfo = dirInfo.Parent;
            }
        }

        private void SetupQRCodeUpdateTimer()
        {
            qrCodeUpdateTimer = new DispatcherTimer();
            qrCodeUpdateTimer.Interval = TimeSpan.FromSeconds(5);
            qrCodeUpdateTimer.Tick += async (sender, e) =>
            {
                await LoadQRCode();
            };
        }

        private void BtnStopServer_Click(object sender, RoutedEventArgs e)
        {
            StopNodeServer();
            StatusText.Text = "Server dimatikan.";
            QrCodeImage.Source = null;
        }

        private void BtnStartServer_Click(object sender, RoutedEventArgs e)
        {
            if (nodeProcess != null && !nodeProcess.HasExited)
            {
                var result = MessageBox.Show("Server sudah berjalan. Apakah Anda ingin memulai ulang dan memindai QR baru (logout)?", "Konfirmasi", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    StopNodeServer();
                }
                else
                {
                    return;
                }
            }

            if (string.IsNullOrEmpty(this.nodeServerPath) || !File.Exists(Path.Combine(this.nodeServerPath, "server.js")))
            {
                var dialog = new CommonOpenFileDialog
                {
                    IsFolderPicker = true,
                    Title = "Pilih Folder 'NodeJsServer' (Yang berisi server.js dan node.exe)"
                };

                if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
                {
                    this.nodeServerPath = dialog.FileName;
                }
                else
                {
                    return;
                }
            }

            if (!File.Exists(Path.Combine(this.nodeServerPath, "server.js")))
            {
                MessageBox.Show("File 'server.js' tidak ditemukan di dalam folder yang dipilih.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // [BARU] Validasi apakah node.exe portable sudah ada di dalam folder
            string portableNodePath = Path.Combine(this.nodeServerPath, "node.exe");
            if (!File.Exists(portableNodePath))
            {
                MessageBox.Show("File 'node.exe' tidak ditemukan di folder server!\n\nPastikan Anda sudah meng-copy node.exe ke dalam folder NodeJsServer agar aplikasi ini bisa berjalan secara portable.", "Error Portable Mode", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                string authDirPath = Path.Combine(this.nodeServerPath, "baileys_auth_info");
                if (Directory.Exists(authDirPath))
                {
                    try
                    {
                        Directory.Delete(authDirPath, true);
                    }
                    catch (IOException)
                    {
                        System.Threading.Thread.Sleep(500);
                        Directory.Delete(authDirPath, true);
                    }
                }

                // [BARU] Gunakan node.exe lokal (portable)
                var startInfo = new ProcessStartInfo
                {
                    FileName = portableNodePath, // <-- Menggunakan node.exe di dalam folder
                    Arguments = "server.js",
                    WorkingDirectory = this.nodeServerPath,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                nodeProcess = Process.Start(startInfo);
                StatusText.Text = "Menyalakan server, menunggu QR Code...";

                qrCodeUpdateTimer.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Gagal mengaktifkan server Node.js: {ex.Message}", "Error Server", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void StopNodeServer()
        {
            if (qrCodeUpdateTimer != null)
            {
                qrCodeUpdateTimer.Stop();
            }

            if (nodeProcess != null && !nodeProcess.HasExited)
            {
                try
                {
                    nodeProcess.Kill(true);
                }
                catch (Exception) { }
                finally
                {
                    nodeProcess.Dispose();
                    nodeProcess = null;
                }
            }
        }

        private async Task LoadQRCode()
        {
            try
            {
                using var responseStatus = await client.GetAsync("http://localhost:3000/status");
                if (responseStatus.IsSuccessStatusCode)
                {
                    string jsonStatus = await responseStatus.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(jsonStatus);
                    string status = doc.RootElement.GetProperty("status").GetString();

                    if (status == "connected")
                    {
                        qrCodeUpdateTimer.Stop();
                        StatusText.Text = "✅ WhatsApp berhasil terhubung!";
                        QrCodeImage.Source = null;
                        return;
                    }
                }

                string url = $"http://localhost:3000/qrcode?t={DateTime.Now.Ticks}";
                var imageBytes = await client.GetByteArrayAsync(url);

                using (var ms = new MemoryStream(imageBytes))
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = ms;
                    image.EndInit();

                    image.Freeze();

                    QrCodeImage.Source = image;
                    StatusText.Text = "Silakan Scan QR Code di atas menggunakan WhatsApp di HP Anda.";
                }
            }
            catch (HttpRequestException)
            {
                QrCodeImage.Source = null;
            }
            catch (Exception)
            {
                QrCodeImage.Source = null;
            }
        }

        private void BtnClearBailey_Click(object sender, RoutedEventArgs e)
        {
            // 1. Pastikan path server sudah diketahui
            if (string.IsNullOrEmpty(this.nodeServerPath))
            {
                MessageBox.Show("Folder server belum terdeteksi. Silakan jalankan server setidaknya satu kali.", "Peringatan", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 2. Minta konfirmasi dari pengguna agar tidak tidak sengaja terpencet
            var result = MessageBox.Show("Apakah Anda yakin ingin menghapus sesi WhatsApp saat ini (Log Out)?\n\nAnda harus men-scan ulang QR Code setelah ini.", "Konfirmasi Log Out", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    // 3. Matikan server terlebih dahulu agar file tidak terkunci oleh Node.js
                    StopNodeServer();
                    QrCodeImage.Source = null;
                    StatusText.Text = "Menghapus sesi WhatsApp...";

                    // 4. Cari dan hapus folder baileys_auth_info
                    string authDirPath = Path.Combine(this.nodeServerPath, "baileys_auth_info");

                    if (Directory.Exists(authDirPath))
                    {
                        try
                        {
                            Directory.Delete(authDirPath, true); // 'true' untuk menghapus semua isi di dalamnya
                        }
                        catch (IOException)
                        {
                            // Terkadang Windows butuh waktu sepersekian detik untuk melepaskan file setelah proses di-kill
                            System.Threading.Thread.Sleep(1000);
                            Directory.Delete(authDirPath, true);
                        }
                    }

                    // 5. Beri tahu pengguna bahwa proses berhasil
                    StatusText.Text = "Sesi terhapus. Silakan klik 'Mulai / Restart Server' untuk scan QR baru.";
                    MessageBox.Show("Sesi WhatsApp berhasil dihapus!\n\nSilakan klik 'Mulai / Restart Server' untuk memunculkan QR Code baru.", "Berhasil", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Gagal menghapus sesi WhatsApp: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}