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

        public ProgressWindow()
        {
            InitializeComponent();
        }

        public void UpdateProgress(int current, int total, string log)
        {
            StatusTextBlock.Text = $"({current}/{total}) - {log}";
            MainProgressBar.Value = (double)current / total * 100;
        }

        // 2. Buat method untuk event click tombol Batal
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // Nonaktifkan tombol agar tidak bisa diklik berkali-kali
            CancelButton.IsEnabled = false;
            // 3. Picu event CancelClicked
            CancelClicked?.Invoke(this, EventArgs.Empty);
        }
    }
}
