using OfficeOpenXml;
using System.Data;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace BlastWhats
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public void SetUiEnabled(bool isEnabled)
        {
            
        }
        

        // Properti untuk menyimpan data dari Excel.
        // Properti ini bisa diakses dari halaman mana pun.
        public DataTable ExcelData { get; set; }

  
        private readonly Page scanPage;

        private readonly Page blastPage;
        public MainWindow()
        {
            InitializeComponent();
            ExcelPackage.License.SetNonCommercialPersonal("Daffa");

            // LANGKAH 2: Buat instance dari setiap halaman HANYA SATU KALI saat jendela utama dibuat.
            scanPage = new ScanPage();
            blastPage = new BlastPage();

            // Tampilkan halaman default saat aplikasi pertama kali dibuka
            MainFrame.Content = scanPage;
        }
        private void BtnScan_Click(object sender, RoutedEventArgs e)
        {
            MainFrame.Navigate(scanPage);
        }

       

        private void BtnBlast_Click(object sender, RoutedEventArgs e)
        {
            MainFrame.Navigate(blastPage);
        }

        private void ExitMenu_Click(object sender, RoutedEventArgs e)
        {
            ScanPage scanPage = new ScanPage();
            scanPage.StopNodeServer();
            Application.Current.Shutdown();
        }
    }
}