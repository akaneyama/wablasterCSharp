using Microsoft.WindowsAPICodePack.Dialogs;
using System.Diagnostics;
using System.IO;
using System.Net.Http;          // Untuk membuat request API
using System.Text;
using System.Text.Json;         // Untuk mem-parsing JSON
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;   // Untuk Timer

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
        private IntPtr jobHandle;

        public ScanPage()
        {
            InitializeComponent();
            SetupQRCodeUpdateTimer();
        }

        private void SetupQRCodeUpdateTimer()
        {
            qrCodeUpdateTimer = new DispatcherTimer();
            qrCodeUpdateTimer.Interval = TimeSpan.FromSeconds(10); 
            qrCodeUpdateTimer.Tick += async (sender, e) =>
            {
                await LoadQRCode();
            };
         
            qrCodeUpdateTimer.Start();
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
                var result = MessageBox.Show("Server sudah berjalan. Apakah Anda ingin memulai ulang dengan folder baru?", "Konfirmasi", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    StopNodeServer(); 
                }
                else
                {
                    return; 
                }
            }

            // 1. Tampilkan dialog untuk memilih folder
            var dialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                Title = "Pilih Folder 'NodeJsServer' Anda"
            };

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                // 2. Simpan path yang dipilih
                this.nodeServerPath = dialog.FileName;

                // 3. Validasi dan jalankan server dari path yang dipilih
                if (!File.Exists(Path.Combine(this.nodeServerPath, "server.js")))
                {
                    MessageBox.Show("File 'server.js' tidak ditemukan di dalam folder yang dipilih.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = "node.exe",
                    Arguments = "server.js",
                    // -------------------------

                    WorkingDirectory = this.nodeServerPath,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                try
                {

                   if(Directory.Exists(Path.Combine(this.nodeServerPath, "baileys_auth_info"))){
                        MessageBox.Show($" Menghapus {this.nodeServerPath}/baileys_auth_info");
                        Directory.Delete(Path.Combine(this.nodeServerPath, "baileys_auth_info"),true);
                    }


                    nodeProcess = Process.Start(startInfo);
                    StatusText.Text = "Server diaktifkan, mengambil QR code...";
                    qrCodeUpdateTimer.Start();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Gagal mengaktifkan server: {ex.Message}", "Error");
                }
            }
        }

        // Method publik yang bisa dipanggil dari MainWindow untuk mematikan server
        public void StopNodeServer()
        {
            // Hentikan timer polling QR code
            if (qrCodeUpdateTimer != null)
            {
                qrCodeUpdateTimer.Stop();
            }

            // Hentikan proses Node.js jika sedang berjalan
            if (nodeProcess != null && !nodeProcess.HasExited)
            {
                nodeProcess.Kill(); // Paksa matikan proses
                nodeProcess = null; // Set ke null setelah dimatikan
            }
        }
      
        private async System.Threading.Tasks.Task LoadQRCode()
        {
            try
            {
                // ===================================================================
                // PERBAIKAN KUNCI ADA DI SINI: Menambahkan parameter acak ke URL
                string url = $"http://localhost:3000/qrcode?t={DateTime.Now.Ticks}";
                // ===================================================================

                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(url); // Gunakan UriSource untuk simple loading
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreImageCache; // Paksa untuk tidak cache
                image.EndInit();

                QrCodeImage.Source = image;
            }
            catch (Exception)
            {
                // Jika gagal (misal server kirim 404), kosongkan gambar
                QrCodeImage.Source = null;
            }
        }
    }

}
