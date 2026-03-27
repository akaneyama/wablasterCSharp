using System;
using System.Collections.Generic;
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
using System.Windows.Shapes;

namespace BlastWhats
{
    /// <summary>
    /// Interaction logic for ProgressWindow.xaml
    /// </summary>
    public partial class ProgressWindow : Window
    {
        // 1. Deklarasikan sebuah event publik
        public event EventHandler CancelClicked;
        public bool IsFinished { get; set; } = false;
        public ProgressWindow()
        {
            InitializeComponent();
        }

        public void UpdateProgress(int current, int total, string log)
        {
            // [BARU] Cegah pembagian dengan angka 0
            if (total > 0)
            {
                StatusTextBlock.Text = $"({current}/{total}) - {log}";
                MainProgressBar.Value = (double)current / total * 100;
            }
            else
            {
                StatusTextBlock.Text = log;
                MainProgressBar.Value = 100; // Langsung penuh jika tidak ada data
            }
        }

        // 2. Buat method untuk event click tombol Batal
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // Nonaktifkan tombol agar tidak bisa diklik berkali-kali
            CancelButton.IsEnabled = false;

            // [BARU] Beri tahu pengguna bahwa sistem sedang memproses pembatalan
            StatusTextBlock.Text = "Membatalkan proses...";

            // 3. Picu event CancelClicked
            CancelClicked?.Invoke(this, EventArgs.Empty);
        }

        // [BARU] 4. Tangani jika pengguna menekan tombol silang 'X' di pojok kanan atas
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Jika tombol batal masih aktif (berarti proses belum selesai/dibatalkan)
            // [PERBAIKAN] Tambahkan pengecekan "!IsFinished"
            if (CancelButton.IsEnabled && !IsFinished)
            {
                e.Cancel = true;
                CancelButton_Click(this, null);
            }

            base.OnClosing(e);
        }
    }
}