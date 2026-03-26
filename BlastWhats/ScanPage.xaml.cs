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
        }

        private void AutoDetectNodeServerPath()
        {
            // Mencari folder NodeJsServer yang sejajar dengan folder project aplikasi ini (wablasterCSharp)
            // Asumsi: Struktur foldermu adalah ...\wablasterCSharp\NamaAplikasiWPF\bin\Debug\net... 
            // Kita naik beberapa level direktori untuk mencarinya.
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            DirectoryInfo dirInfo = new DirectoryInfo(baseDir);

            // Mundur maksimal 5 level untuk mencari folder NodeJsServer
            for (int i = 0; i < 5; i++)
            {
                if (dirInfo == null) break;

                string potentialPath = Path.Combine(dirInfo.FullName, "NodeJsServer");
                if (Directory.Exists(potentialPath) && File.Exists(Path.Combine(potentialPath, "server.js")))
                {
                    this.nodeServerPath = potentialPath;
                    break; // Ditemukan
                }
                dirInfo = dirInfo.Parent;
            }
        }

        private void SetupQRCodeUpdateTimer()
        {
            qrCodeUpdateTimer = new DispatcherTimer();
            qrCodeUpdateTimer.Interval = TimeSpan.FromSeconds(5); // Dipercepat jadi 5 detik agar respons UI lebih cepat
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

            // Jika path belum ditemukan otomatis, minta pengguna memilih manual
            if (string.IsNullOrEmpty(this.nodeServerPath) || !File.Exists(Path.Combine(this.nodeServerPath, "server.js")))
            {
                var dialog = new CommonOpenFileDialog
                {
                    IsFolderPicker = true,
                    Title = "Pilih Folder 'NodeJsServer' (Yang berisi server.js)"
                };

                if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
                {
                    this.nodeServerPath = dialog.FileName;
                }
                else
                {
                    return; // Batal memilih
                }
            }

            if (!File.Exists(Path.Combine(this.nodeServerPath, "server.js")))
            {
                MessageBox.Show("File 'server.js' tidak ditemukan di dalam folder yang dipilih.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                // Hapus session lama jika ada, agar bisa scan QR baru
                string authDirPath = Path.Combine(this.nodeServerPath, "baileys_auth_info");
                if (Directory.Exists(authDirPath))
                {
                    try
                    {
                        Directory.Delete(authDirPath, true);
                    }
                    catch (IOException)
                    {
                        // Terkadang file terkunci. Tunggu sebentar lalu coba lagi
                        System.Threading.Thread.Sleep(500);
                        Directory.Delete(authDirPath, true);
                    }
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = "node.exe",
                    Arguments = "server.js",
                    WorkingDirectory = this.nodeServerPath,
                    CreateNoWindow = true, // Sembunyikan terminal Node.js
                    UseShellExecute = false,
                    RedirectStandardOutput = true, // Opsional: untuk membaca log jika diperlukan
                    RedirectStandardError = true
                };

                nodeProcess = Process.Start(startInfo);
                StatusText.Text = "Menyalakan server, menunggu QR Code...";

                // Mulai polling QR Code
                qrCodeUpdateTimer.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Gagal mengaktifkan server Node.js: {ex.Message}\n\nPastikan NodeJS terinstal di PC ini.", "Error Server", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    nodeProcess.Kill(true); // Matikan paksa beserta proses anaknya
                }
                catch (Exception) { /* Abaikan jika proses sudah mati sendiri */ }
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
                // 1. Cek status koneksi dulu
                using var responseStatus = await client.GetAsync("http://localhost:3000/status");
                if (responseStatus.IsSuccessStatusCode)
                {
                    string jsonStatus = await responseStatus.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(jsonStatus);
                    string status = doc.RootElement.GetProperty("status").GetString();

                    if (status == "connected")
                    {
                        // Jika sudah terhubung, hentikan timer dan beritahu pengguna
                        qrCodeUpdateTimer.Stop();
                        StatusText.Text = "✅ WhatsApp berhasil terhubung!";
                        QrCodeImage.Source = null;
                        return;
                    }
                }

                // 2. Jika belum 'connected', coba tarik gambar QR Code
                string url = $"http://localhost:3000/qrcode?t={DateTime.Now.Ticks}";

                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(url);
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                image.EndInit();

                // Harus dibekukan (Freeze) jika dipakai lintas thread di WPF
                image.Freeze();

                QrCodeImage.Source = image;
                StatusText.Text = "Silakan Scan QR Code di atas menggunakan WhatsApp di HP Anda.";
            }
            catch (HttpRequestException)
            {
                // Terjadi jika server Node.js belum sepenuhnya menyala (API belum siap merespons)
                // Kita diamkan saja dan biarkan timer mencoba lagi di detik berikutnya
                QrCodeImage.Source = null;
            }
            catch (Exception)
            {
                // Gagal load gambar (misal server kirim 404 karena QR belum siap)
                QrCodeImage.Source = null;
            }
        }
    }
}